using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using OpenDeepWiki.Services.Repositories;

namespace OpenDeepWiki.Services.Wiki;

public sealed class RoslynWorkflowSemanticProvider : IWorkflowSemanticProvider
{
    private static readonly HashSet<string> StatusMemberNames =
    [
        "Status",
        "State",
        "RequestStatus"
    ];

    private static readonly HashSet<string> RegistrationMethodNames =
    [
        "AddHostedService",
        "AddScoped",
        "AddTransient",
        "AddSingleton"
    ];

    private static readonly HashSet<string> ServiceLocatorMethodNames =
    [
        "Resolve",
        "ResolveNamed",
        "ResolveOptional",
        "ResolveOptionalNamed"
    ];

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly MsBuildWorkspaceBootstrap _workspaceBootstrap;
    private readonly ILogger<RoslynWorkflowSemanticProvider> _logger;

    public RoslynWorkflowSemanticProvider(
        MsBuildWorkspaceBootstrap workspaceBootstrap,
        ILogger<RoslynWorkflowSemanticProvider> logger)
    {
        _workspaceBootstrap = workspaceBootstrap ?? throw new ArgumentNullException(nameof(workspaceBootstrap));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanHandle(RepositoryWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        return EnumerateWorkspaceFiles(workspace.WorkingDirectory, "*.sln").Any() ||
               EnumerateWorkspaceFiles(workspace.WorkingDirectory, "*.csproj").Any();
    }

    public async Task<WorkflowSemanticGraph> BuildGraphAsync(
        RepositoryWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (!Directory.Exists(workspace.WorkingDirectory))
        {
            throw new DirectoryNotFoundException($"Repository workspace directory was not found: {workspace.WorkingDirectory}");
        }

        using var msbuildWorkspace = _workspaceBootstrap.CreateWorkspace();
        var solution = await OpenSolutionAsync(msbuildWorkspace, workspace.WorkingDirectory, cancellationToken);
        var builder = new GraphBuilder(workspace.WorkingDirectory);

        foreach (var project in solution.Projects.Where(project => project.Language == LanguageNames.CSharp))
        {
            await IndexProjectNodesAsync(project, builder, cancellationToken);
        }

        foreach (var project in solution.Projects.Where(project => project.Language == LanguageNames.CSharp))
        {
            await AnalyzeProjectEdgesAsync(project, builder, cancellationToken);
        }

        return builder.Build();
    }

    private async Task IndexProjectNodesAsync(
        Project project,
        GraphBuilder builder,
        CancellationToken cancellationToken)
    {
        foreach (var document in project.Documents.Where(static item => item.SupportsSyntaxTree && item.FilePath is not null))
        {
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (syntaxRoot is null || semanticModel is null)
            {
                continue;
            }

            foreach (var declaration in syntaxRoot.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol symbol)
                {
                    continue;
                }

                builder.AddOrUpdateNode(symbol, ResolveFilePath(symbol, document.FilePath, builder.RootPath));
                AddImplementationEdges(builder, symbol, document.FilePath);
            }
        }
    }

    private async Task AnalyzeProjectEdgesAsync(
        Project project,
        GraphBuilder builder,
        CancellationToken cancellationToken)
    {
        foreach (var document in project.Documents.Where(static item => item.SupportsSyntaxTree && item.FilePath is not null))
        {
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (syntaxRoot is null || semanticModel is null || document.FilePath is null)
            {
                continue;
            }

            foreach (var invocation in syntaxRoot.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                AnalyzeInvocation(invocation, semanticModel, document.FilePath, builder);
            }

            foreach (var assignment in syntaxRoot.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                AnalyzeAssignment(assignment, semanticModel, document.FilePath, builder);
            }

            foreach (var returnStatement in syntaxRoot.DescendantNodes().OfType<ReturnStatementSyntax>())
            {
                AnalyzeReturnStatement(returnStatement, semanticModel, document.FilePath, builder);
            }
        }
    }

    private void AnalyzeInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string filePath,
        GraphBuilder builder)
    {
        var sourceType = GetContainingTypeSymbol(invocation, semanticModel);
        var targetMethod = ResolveMethodSymbol(semanticModel, invocation);
        if (sourceType is null || targetMethod is null)
        {
            return;
        }

        if (TryAddRegistrationEdges(sourceType, targetMethod, invocation, semanticModel, filePath, builder))
        {
            return;
        }

        if (TryAddServiceLocatorResolutionEdges(sourceType, targetMethod, invocation, semanticModel, filePath, builder))
        {
            return;
        }

        TryAddRequestPayloadEdges(sourceType, targetMethod, invocation, filePath, builder);

        if (targetMethod.ContainingType is INamedTypeSymbol targetType)
        {
            builder.AddEdge(
                sourceType,
                targetType,
                WorkflowEdgeKind.Invokes,
                CreateEvidenceJson(filePath, invocation, new Dictionary<string, object?>
                {
                    ["method"] = targetMethod.Name
                }));
        }

        TryAddWriteEdges(sourceType, targetMethod, invocation, semanticModel, filePath, builder);
        TryAddQueryEdges(sourceType, targetMethod, invocation, filePath, builder);
    }

    private void AnalyzeAssignment(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        string filePath,
        GraphBuilder builder)
    {
        var sourceType = GetContainingTypeSymbol(assignment, semanticModel);
        if (sourceType is null)
        {
            return;
        }

        var targetSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
        if (targetSymbol is not IPropertySymbol and not IFieldSymbol)
        {
            return;
        }

        if (!StatusMemberNames.Contains(targetSymbol.Name))
        {
            return;
        }

        if (targetSymbol.ContainingType is not INamedTypeSymbol entityType)
        {
            return;
        }

        var statusSymbol = semanticModel.GetSymbolInfo(assignment.Right).Symbol;
        var statusValue = statusSymbol?.Name ?? assignment.Right.ToString();

        builder.AddEdge(
            sourceType,
            entityType,
            WorkflowEdgeKind.UpdatesStatus,
            CreateEvidenceJson(filePath, assignment, new Dictionary<string, object?>
            {
                ["member"] = targetSymbol.Name,
                ["value"] = statusValue
            }),
            deduplicationKey: statusValue);

        if (statusSymbol?.ContainingType is INamedTypeSymbol statusEnum)
        {
            builder.AddOrUpdateNode(statusEnum, ResolveFilePath(statusEnum, filePath, builder.RootPath));
        }
    }

    private void AnalyzeReturnStatement(
        ReturnStatementSyntax returnStatement,
        SemanticModel semanticModel,
        string filePath,
        GraphBuilder builder)
    {
        if (returnStatement.Expression is null)
        {
            return;
        }

        var sourceType = GetContainingTypeSymbol(returnStatement, semanticModel);
        if (sourceType is null || ClassifyNode(sourceType) != WorkflowNodeKind.ExecutorFactory)
        {
            return;
        }

        var returnedType = semanticModel.GetTypeInfo(returnStatement.Expression).Type as INamedTypeSymbol;
        if (returnedType is null || ClassifyNode(returnedType) != WorkflowNodeKind.Executor)
        {
            return;
        }

        builder.AddEdge(
            sourceType,
            returnedType,
            WorkflowEdgeKind.Dispatches,
            CreateEvidenceJson(filePath, returnStatement, new Dictionary<string, object?>
            {
                ["method"] = GetContainingMethodName(returnStatement)
            }));
    }

    private bool TryAddRegistrationEdges(
        INamedTypeSymbol sourceType,
        IMethodSymbol targetMethod,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string filePath,
        GraphBuilder builder)
    {
        if (!RegistrationMethodNames.Contains(targetMethod.Name))
        {
            return false;
        }

        var registeredTypes = new List<INamedTypeSymbol>();
        foreach (var typeArgument in targetMethod.TypeArguments.OfType<INamedTypeSymbol>())
        {
            registeredTypes.Add(typeArgument);
        }

        if (registeredTypes.Count == 0)
        {
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (argument.Expression is TypeOfExpressionSyntax typeOfExpression &&
                    semanticModel.GetTypeInfo(typeOfExpression.Type).Type is INamedTypeSymbol namedType)
                {
                    registeredTypes.Add(namedType);
                }
            }
        }

        if (registeredTypes.Count == 0)
        {
            return false;
        }

        var implementationType = registeredTypes[^1];
        builder.AddEdge(
            sourceType,
            implementationType,
            WorkflowEdgeKind.RegisteredBy,
            CreateEvidenceJson(filePath, invocation, new Dictionary<string, object?>
            {
                ["method"] = targetMethod.Name
            }));

        return true;
    }

    private bool TryAddServiceLocatorResolutionEdges(
        INamedTypeSymbol sourceType,
        IMethodSymbol targetMethod,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string filePath,
        GraphBuilder builder)
    {
        if (!ServiceLocatorMethodNames.Contains(targetMethod.Name))
        {
            return false;
        }

        var resolvedTypes = targetMethod.TypeArguments.OfType<INamedTypeSymbol>().ToList();
        if (resolvedTypes.Count == 0)
        {
            return false;
        }

        var serviceName = invocation.ArgumentList.Arguments.Count > 0
            ? semanticModel.GetConstantValue(invocation.ArgumentList.Arguments[0].Expression)
            : default;

        foreach (var resolvedType in resolvedTypes)
        {
            var resolvedKind = ClassifyNode(resolvedType);
            var edgeKind = resolvedKind is WorkflowNodeKind.Executor or WorkflowNodeKind.ExecutorFactory or WorkflowNodeKind.Handler
                ? WorkflowEdgeKind.Dispatches
                : WorkflowEdgeKind.Invokes;

            builder.AddEdge(
                sourceType,
                resolvedType,
                edgeKind,
                CreateEvidenceJson(filePath, invocation, new Dictionary<string, object?>
                {
                    ["method"] = targetMethod.Name,
                    ["resolvedType"] = resolvedType.Name,
                    ["serviceName"] = serviceName.HasValue ? serviceName.Value?.ToString() : null
                }),
                deduplicationKey: resolvedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        }

        return true;
    }

    private void TryAddRequestPayloadEdges(
        INamedTypeSymbol sourceType,
        IMethodSymbol targetMethod,
        InvocationExpressionSyntax invocation,
        string filePath,
        GraphBuilder builder)
    {
        if (!string.Equals(targetMethod.Name, "GetReq", StringComparison.Ordinal) ||
            targetMethod.TypeArguments.Length == 0)
        {
            return;
        }

        foreach (var requestType in targetMethod.TypeArguments.OfType<INamedTypeSymbol>())
        {
            if (ClassifyNode(requestType) != WorkflowNodeKind.RequestEntity)
            {
                continue;
            }

            builder.AddEdge(
                sourceType,
                requestType,
                WorkflowEdgeKind.ConsumesEntity,
                CreateEvidenceJson(filePath, invocation, new Dictionary<string, object?>
                {
                    ["method"] = targetMethod.Name,
                    ["requestType"] = requestType.Name
                }));
        }
    }

    private void TryAddWriteEdges(
        INamedTypeSymbol sourceType,
        IMethodSymbol targetMethod,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string filePath,
        GraphBuilder builder)
    {
        var targetKind = ClassifyNode(targetMethod.ContainingType);
        if (targetKind != WorkflowNodeKind.Repository && targetKind != WorkflowNodeKind.DbContext && !IsWriteLikeMethod(targetMethod.Name))
        {
            return;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            foreach (var entityType in ExtractRelevantEntityTypes(semanticModel.GetTypeInfo(argument.Expression).Type))
            {
                builder.AddEdge(
                    sourceType,
                    entityType,
                    WorkflowEdgeKind.Writes,
                    CreateEvidenceJson(filePath, invocation, new Dictionary<string, object?>
                    {
                        ["method"] = targetMethod.Name
                    }));
            }
        }
    }

    private void TryAddQueryEdges(
        INamedTypeSymbol sourceType,
        IMethodSymbol targetMethod,
        InvocationExpressionSyntax invocation,
        string filePath,
        GraphBuilder builder)
    {
        var targetKind = ClassifyNode(targetMethod.ContainingType);
        if (targetKind != WorkflowNodeKind.Repository && targetKind != WorkflowNodeKind.DbContext && !IsQueryLikeMethod(targetMethod.Name))
        {
            return;
        }

        foreach (var entityType in ExtractRelevantEntityTypes(targetMethod.ReturnType))
        {
            builder.AddEdge(
                sourceType,
                entityType,
                WorkflowEdgeKind.Queries,
                CreateEvidenceJson(filePath, invocation, new Dictionary<string, object?>
                {
                    ["method"] = targetMethod.Name
                }));
        }
    }

    private void AddImplementationEdges(GraphBuilder builder, INamedTypeSymbol symbol, string? filePath)
    {
        if (symbol.TypeKind != TypeKind.Class && symbol.TypeKind != TypeKind.Struct)
        {
            return;
        }

        foreach (var interfaceType in symbol.Interfaces)
        {
            if (ClassifyNode(interfaceType) == WorkflowNodeKind.Unknown)
            {
                continue;
            }

            builder.AddEdge(
                symbol,
                interfaceType,
                WorkflowEdgeKind.Implements,
                CreateEvidenceJson(
                    ResolveFilePath(symbol, filePath, builder.RootPath),
                    null,
                    new Dictionary<string, object?>
                    {
                        ["interface"] = interfaceType.Name
                    }));
        }
    }

    private async Task<Solution> OpenSolutionAsync(
        MSBuildWorkspace workspace,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var solutionPath = EnumerateWorkspaceFiles(workingDirectory, "*.sln").FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            _logger.LogDebug("Opening workflow discovery solution {SolutionPath}", solutionPath);
            return await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
        }

        var projectPaths = EnumerateWorkspaceFiles(workingDirectory, "*.csproj").ToList();
        if (projectPaths.Count == 0)
        {
            throw new InvalidOperationException($"No .sln or .csproj files were found under {workingDirectory}.");
        }

        Project? lastProject = null;
        foreach (var projectPath in projectPaths)
        {
            _logger.LogDebug("Opening workflow discovery project {ProjectPath}", projectPath);
            lastProject = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
        }

        return lastProject?.Solution
               ?? throw new InvalidOperationException($"Failed to load projects from {workingDirectory}.");
    }

    private static IEnumerable<string> EnumerateWorkspaceFiles(string rootPath, string pattern)
    {
        return Directory
            .EnumerateFiles(rootPath, pattern, SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                                  !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                                  !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path.Length);
    }

    private static IMethodSymbol? ResolveMethodSymbol(SemanticModel semanticModel, InvocationExpressionSyntax invocation)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        return symbolInfo.Symbol as IMethodSymbol
               ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    private static INamedTypeSymbol? GetContainingTypeSymbol(SyntaxNode syntaxNode, SemanticModel semanticModel)
    {
        var declaration = syntaxNode.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        return declaration is null
            ? null
            : semanticModel.GetDeclaredSymbol(declaration) as INamedTypeSymbol;
    }

    private static string GetContainingMethodName(SyntaxNode syntaxNode)
    {
        return syntaxNode.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText
               ?? string.Empty;
    }

    private static IEnumerable<INamedTypeSymbol> ExtractRelevantEntityTypes(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is null)
        {
            yield break;
        }

        if (typeSymbol is INamedTypeSymbol namedType &&
            namedType.Name == "Task" &&
            namedType.TypeArguments.Length == 1)
        {
            foreach (var innerType in ExtractRelevantEntityTypes(namedType.TypeArguments[0]))
            {
                yield return innerType;
            }

            yield break;
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            foreach (var innerType in ExtractRelevantEntityTypes(arrayType.ElementType))
            {
                yield return innerType;
            }

            yield break;
        }

        if (typeSymbol is INamedTypeSymbol genericNamedType && genericNamedType.TypeArguments.Length > 0)
        {
            foreach (var typeArgument in genericNamedType.TypeArguments)
            {
                foreach (var innerType in ExtractRelevantEntityTypes(typeArgument))
                {
                    yield return innerType;
                }
            }
        }

        if (typeSymbol is INamedTypeSymbol candidate &&
            ClassifyNode(candidate) is WorkflowNodeKind.RequestEntity or WorkflowNodeKind.Entity)
        {
            yield return candidate;
        }
    }

    private static bool IsWriteLikeMethod(string methodName)
    {
        return methodName.Contains("Insert", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Add", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Upsert", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsQueryLikeMethod(string methodName)
    {
        return methodName.Contains("List", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Get", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Find", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Query", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Scan", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveFilePath(INamedTypeSymbol symbol, string? fallbackPath, string rootPath)
    {
        var sourcePath = symbol.Locations.FirstOrDefault(static location => location.IsInSource)?.SourceTree?.FilePath
                         ?? fallbackPath
                         ?? string.Empty;

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return string.Empty;
        }

        return Path.GetRelativePath(rootPath, sourcePath).Replace('\\', '/');
    }

    private static string CreateEvidenceJson(
        string filePath,
        SyntaxNode? syntaxNode,
        Dictionary<string, object?> payload)
    {
        payload["filePath"] = filePath.Replace('\\', '/');
        if (syntaxNode is not null && syntaxNode.SyntaxTree is not null)
        {
            payload["line"] = syntaxNode.SyntaxTree.GetLineSpan(syntaxNode.Span).StartLinePosition.Line + 1;
        }

        return JsonSerializer.Serialize(payload);
    }

    private static WorkflowNodeKind ClassifyNode(INamedTypeSymbol? symbol)
    {
        if (symbol is null)
        {
            return WorkflowNodeKind.Unknown;
        }

        if (symbol.TypeKind == TypeKind.Enum &&
            (symbol.Name.Contains("Status", StringComparison.OrdinalIgnoreCase) ||
             symbol.Name.Contains("State", StringComparison.OrdinalIgnoreCase)))
        {
            return WorkflowNodeKind.StatusEnum;
        }

        var name = symbol.Name;
        if (name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase) || DerivesFrom(symbol, "ControllerBase"))
        {
            return WorkflowNodeKind.Controller;
        }

        if (DerivesFrom(symbol, "BackgroundService"))
        {
            return WorkflowNodeKind.BackgroundService;
        }

        if (DerivesFrom(symbol, "BaseJob") || ImplementsInterface(symbol, "IJob"))
        {
            return WorkflowNodeKind.HostedService;
        }

        if (ImplementsInterface(symbol, "IHostedService"))
        {
            return WorkflowNodeKind.HostedService;
        }

        if (DerivesFrom(symbol, "DbContext"))
        {
            return WorkflowNodeKind.DbContext;
        }

        if (IsNoiseLikeType(symbol.Name))
        {
            return WorkflowNodeKind.Unknown;
        }

        if (name.EndsWith("Factory", StringComparison.OrdinalIgnoreCase) &&
            name.Contains("Executor", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowNodeKind.ExecutorFactory;
        }

        if (name.EndsWith("Executor", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Executor", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowNodeKind.Executor;
        }

        if (name.EndsWith("Repository", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowNodeKind.Repository;
        }

        if (name.Equals("Program", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Startup", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Registration", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Service", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowNodeKind.Service;
        }

        if (symbol.TypeKind == TypeKind.Class &&
            DerivesFrom(symbol, "WcsReqBody"))
        {
            return WorkflowNodeKind.RequestEntity;
        }

        if (symbol.TypeKind == TypeKind.Class &&
            name.Contains("Request", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowNodeKind.RequestEntity;
        }

        if (symbol.TypeKind == TypeKind.Class &&
            symbol.DeclaredAccessibility == Accessibility.Public)
        {
            return WorkflowNodeKind.Entity;
        }

        return WorkflowNodeKind.Unknown;
    }

    private static bool IsNoiseLikeType(string name)
    {
        return name.EndsWith("Helper", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Dto", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Extensions", StringComparison.OrdinalIgnoreCase);
    }

    private static bool DerivesFrom(INamedTypeSymbol symbol, string baseTypeName)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.Name, baseTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsInterface(INamedTypeSymbol symbol, string interfaceName)
    {
        return symbol.AllInterfaces.Any(item => string.Equals(item.Name, interfaceName, StringComparison.Ordinal));
    }

    private static string CreateNodeMetadataJson(INamedTypeSymbol symbol)
    {
        var payload = new Dictionary<string, object?>
        {
            ["typeKind"] = symbol.TypeKind.ToString(),
            ["isAbstract"] = symbol.IsAbstract,
            ["isInterface"] = symbol.TypeKind == TypeKind.Interface
        };

        var documentationSummary = GetDocumentationSummary(symbol);
        if (!string.IsNullOrWhiteSpace(documentationSummary))
        {
            payload["documentationSummary"] = documentationSummary;
        }

        var serviceKeys = symbol.GetAttributes()
            .Where(attribute =>
                attribute.AttributeClass?.Name is "IocAttribute" or "Ioc")
            .SelectMany(ExtractAttributeValues)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (serviceKeys.Count > 0)
        {
            payload["serviceKeys"] = serviceKeys;
        }

        return JsonSerializer.Serialize(payload);
    }

    private static string? GetDocumentationSummary(INamedTypeSymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml(expandIncludes: true, cancellationToken: default);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            var document = XDocument.Parse($"<root>{xml}</root>");
            var summary = document.Root?
                .Descendants("summary")
                .Select(element => element.Value)
                .FirstOrDefault();

            return NormalizeDocumentationText(summary);
        }
        catch
        {
            return NormalizeDocumentationText(xml);
        }
    }

    private static string? NormalizeDocumentationText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = WhitespaceRegex.Replace(value, " ").Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static IEnumerable<string?> ExtractAttributeValues(AttributeData attribute)
    {
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Kind == TypedConstantKind.Array)
            {
                foreach (var value in argument.Values)
                {
                    yield return value.Value?.ToString();
                }

                continue;
            }

            yield return argument.Value?.ToString();
        }
    }

    private sealed class GraphBuilder
    {
        private readonly Dictionary<string, WorkflowGraphNode> _nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<EdgeKey, WorkflowGraphEdge> _edges = [];

        public GraphBuilder(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public void AddOrUpdateNode(INamedTypeSymbol symbol, string filePath)
        {
            var id = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
            var kind = ClassifyNode(symbol);
            var metadataJson = CreateNodeMetadataJson(symbol);
            if (_nodes.TryGetValue(id, out var existing))
            {
                _nodes[id] = new WorkflowGraphNode
                {
                    Id = id,
                    Kind = existing.Kind == WorkflowNodeKind.Unknown ? kind : existing.Kind,
                    DisplayName = existing.DisplayName,
                    FilePath = string.IsNullOrWhiteSpace(existing.FilePath) ? filePath : existing.FilePath,
                    SymbolName = existing.SymbolName,
                    MetadataJson = string.IsNullOrWhiteSpace(existing.MetadataJson) ? metadataJson : existing.MetadataJson
                };
                return;
            }

            _nodes[id] = new WorkflowGraphNode
            {
                Id = id,
                Kind = kind,
                DisplayName = symbol.Name,
                FilePath = filePath,
                SymbolName = id,
                MetadataJson = metadataJson
            };
        }

        public void AddEdge(
            INamedTypeSymbol fromSymbol,
            INamedTypeSymbol toSymbol,
            WorkflowEdgeKind kind,
            string? evidenceJson = null,
            string? deduplicationKey = null)
        {
            AddOrUpdateNode(fromSymbol, ResolveFilePath(fromSymbol, null, RootPath));
            AddOrUpdateNode(toSymbol, ResolveFilePath(toSymbol, null, RootPath));

            var fromId = fromSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
            var toId = toSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
            var edgeKey = new EdgeKey(fromId, toId, kind, deduplicationKey ?? string.Empty);

            if (_edges.ContainsKey(edgeKey))
            {
                return;
            }

            _edges[edgeKey] = new WorkflowGraphEdge
            {
                FromId = fromId,
                ToId = toId,
                Kind = kind,
                EvidenceJson = evidenceJson
            };
        }

        public WorkflowSemanticGraph Build()
        {
            return new WorkflowSemanticGraph
            {
                Nodes = _nodes.Values.OrderBy(static item => item.DisplayName, StringComparer.Ordinal).ToList(),
                Edges = _edges.Values
                    .OrderBy(static item => item.FromId, StringComparer.Ordinal)
                    .ThenBy(static item => item.ToId, StringComparer.Ordinal)
                    .ThenBy(static item => item.Kind)
                    .ToList()
            };
        }

        private readonly record struct EdgeKey(
            string FromId,
            string ToId,
            WorkflowEdgeKind Kind,
            string Discriminator);
    }
}
