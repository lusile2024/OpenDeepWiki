using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OpenDeepWiki.Services.Wiki.Lsp;

public sealed class WorkflowExternalLspClient : IWorkflowExternalLspClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WorkflowExternalLspOptions _options;
    private readonly ILogger<WorkflowExternalLspClient> _logger;
    private readonly SemaphoreSlim _gate;
    private int _requestId;

    public WorkflowExternalLspClient(
        IOptions<WorkflowExternalLspOptions> options,
        ILogger<WorkflowExternalLspClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? new WorkflowExternalLspOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentRequests));
    }

    public async Task<WorkflowExternalLspSymbolResult> AnalyzeSymbolAsync(
        WorkflowExternalLspSymbolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.Enabled)
        {
            return BuildUnavailableResult("disabled", "external LSP 已关闭。");
        }

        if (string.IsNullOrWhiteSpace(_options.Command))
        {
            return BuildUnavailableResult("disabled", "未配置 external LSP command。");
        }

        var workspacePath = request.WorkspacePath;
        var absoluteFilePath = Path.IsPathRooted(request.FilePath)
            ? request.FilePath
            : Path.GetFullPath(Path.Combine(workspacePath, request.FilePath));

        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
        {
            return BuildFailureResult("workspace 不存在或为空。");
        }

        if (!File.Exists(absoluteFilePath))
        {
            return BuildFailureResult($"目标文件不存在：{absoluteFilePath}");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await AnalyzeInternalAsync(request, workspacePath, absoluteFilePath, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WorkflowExternalLspSymbolResult> AnalyzeInternalAsync(
        WorkflowExternalLspSymbolRequest request,
        string workspacePath,
        string absoluteFilePath,
        CancellationToken cancellationToken)
    {
        var diagnostics = new ConcurrentQueue<WorkflowLspDiagnostic>();
        using var process = CreateProcess(workspacePath, absoluteFilePath);

        try
        {
            if (!process.Start())
            {
                return BuildFailureResult("external LSP 进程启动失败。");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start external LSP process {Command}", _options.Command);
            return BuildFailureResult($"external LSP 启动失败：{ex.Message}");
        }

        var stderrPump = PumpDiagnosticsAsync(process.StandardError, diagnostics, cancellationToken);
        try
        {
            var stdin = process.StandardInput.BaseStream;
            var stdout = process.StandardOutput.BaseStream;

            var initializeParams = new LspInitializeParams
            {
                ProcessId = Environment.ProcessId,
                RootUri = new Uri(Path.GetFullPath(workspacePath)).AbsoluteUri
            };

            await SendRequestAsync(
                stdin,
                stdout,
                "initialize",
                initializeParams,
                _options.InitializeTimeoutMs,
                diagnostics,
                cancellationToken);

            await SendNotificationAsync(stdin, "initialized", new { }, cancellationToken);

            var textDocumentIdentifier = new LspTextDocumentIdentifier
            {
                Uri = new Uri(absoluteFilePath).AbsoluteUri
            };

            await SendNotificationAsync(
                stdin,
                "textDocument/didOpen",
                new LspDidOpenTextDocumentParams
                {
                    TextDocument = new LspTextDocumentItem
                    {
                        Uri = textDocumentIdentifier.Uri,
                        LanguageId = ResolveLanguageId(absoluteFilePath),
                        Version = 1,
                        Text = await File.ReadAllTextAsync(absoluteFilePath, cancellationToken)
                    }
                },
                cancellationToken);

            var warmupDelayMs = ResolveWarmupDelayMs(_options.Command, _options.WarmupDelayMs);
            if (warmupDelayMs > 0)
            {
                await Task.Delay(warmupDelayMs, cancellationToken);
            }

            var documentParams = new LspTextDocumentPositionParams
            {
                TextDocument = textDocumentIdentifier,
                Position = new LspPosition
                {
                    Line = Math.Max(0, request.LineNumber - 1),
                    Character = Math.Max(0, request.ColumnNumber - 1)
                }
            };

            var result = new WorkflowExternalLspSymbolResult
            {
                Attempted = true,
                Success = false,
                Strategy = "external-lsp",
                ServerName = string.IsNullOrWhiteSpace(request.PreferredServer)
                    ? Path.GetFileNameWithoutExtension(_options.Command)
                    : request.PreferredServer,
                SuggestedRootSymbolNames = string.IsNullOrWhiteSpace(request.SymbolName)
                    ? []
                    : [request.SymbolName]
            };
            var requestedFeatureCount = 0;

            if (request.AssistOptions.EnableDefinitionLookup)
            {
                requestedFeatureCount += 1;
                try
                {
                    var definitionPayload = await SendRequestAsync(
                        stdin,
                        stdout,
                        "textDocument/definition",
                        documentParams,
                        request.AssistOptions.RequestTimeoutMs,
                        diagnostics,
                        cancellationToken);
                    result.Definitions = ParseLocations(definitionPayload, "definition");
                }
                catch (Exception ex) when (IsRecoverableFeatureFailure(ex))
                {
                    diagnostics.Enqueue(new WorkflowLspDiagnostic
                    {
                        Level = "warning",
                        Message = $"definition lookup failed: {ex.Message}"
                    });
                }
            }

            if (request.AssistOptions.EnableReferenceLookup)
            {
                requestedFeatureCount += 1;
                try
                {
                    var referencesPayload = await SendRequestAsync(
                        stdin,
                        stdout,
                        "textDocument/references",
                        new LspReferenceParams
                        {
                            TextDocument = documentParams.TextDocument,
                            Position = documentParams.Position,
                            Context = new LspReferenceContext
                            {
                                IncludeDeclaration = true
                            }
                        },
                        request.AssistOptions.RequestTimeoutMs,
                        diagnostics,
                        cancellationToken);
                    result.References = ParseLocations(referencesPayload, "reference");
                }
                catch (Exception ex) when (IsRecoverableFeatureFailure(ex))
                {
                    diagnostics.Enqueue(new WorkflowLspDiagnostic
                    {
                        Level = "warning",
                        Message = $"reference lookup failed: {ex.Message}"
                    });
                }
            }

            if (request.AssistOptions.IncludeCallHierarchy && request.AssistOptions.EnablePrepareCallHierarchy)
            {
                requestedFeatureCount += 1;
                try
                {
                    var preparePayload = await SendRequestAsync(
                        stdin,
                        stdout,
                        "textDocument/prepareCallHierarchy",
                        documentParams,
                        request.AssistOptions.RequestTimeoutMs,
                        diagnostics,
                        cancellationToken);
                    var preparedItems = ParseCallHierarchyItems(preparePayload);
                    var rootItem = preparedItems.FirstOrDefault();
                    if (rootItem is not null)
                    {
                        if (string.IsNullOrWhiteSpace(request.SymbolName) &&
                            !string.IsNullOrWhiteSpace(rootItem.Name))
                        {
                            result.SuggestedRootSymbolNames.Add(rootItem.Name);
                        }

                        var outgoingPayload = await SendRequestAsync(
                            stdin,
                            stdout,
                            "callHierarchy/outgoingCalls",
                            new { item = rootItem },
                            request.AssistOptions.RequestTimeoutMs,
                            diagnostics,
                            cancellationToken);
                        result.CallHierarchyEdges.AddRange(ParseOutgoingEdges(rootItem, outgoingPayload));

                        var incomingPayload = await SendRequestAsync(
                            stdin,
                            stdout,
                            "callHierarchy/incomingCalls",
                            new { item = rootItem },
                            request.AssistOptions.RequestTimeoutMs,
                            diagnostics,
                            cancellationToken);
                        result.CallHierarchyEdges.AddRange(ParseIncomingEdges(rootItem, incomingPayload));
                    }
                }
                catch (Exception ex) when (IsRecoverableFeatureFailure(ex))
                {
                    diagnostics.Enqueue(new WorkflowLspDiagnostic
                    {
                        Level = "warning",
                        Message = $"call hierarchy lookup failed: {ex.Message}"
                    });
                }
            }

            result.SuggestedRootSymbolNames = result.SuggestedRootSymbolNames
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            result.SuggestedMustExplainSymbols = result.CallHierarchyEdges
                .SelectMany(edge => new[] { edge.FromSymbol, edge.ToSymbol })
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Except(result.SuggestedRootSymbolNames, StringComparer.OrdinalIgnoreCase)
                .Take(32)
                .ToList();
            result.Diagnostics = diagnostics.ToList();
            result.Success = requestedFeatureCount == 0 ||
                             result.Definitions.Count > 0 ||
                             result.References.Count > 0 ||
                             result.CallHierarchyEdges.Count > 0;

            if (!result.Success)
            {
                result.FailureReason = "external LSP 未返回可用定义、引用或调用层级结果。";
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return BuildFailureResult("external LSP 请求已取消。", diagnostics.ToList());
        }
        catch (TimeoutException ex)
        {
            return BuildFailureResult(ex.Message, diagnostics.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External LSP analysis failed for {FilePath}", absoluteFilePath);
            return BuildFailureResult($"external LSP 分析失败：{ex.Message}", diagnostics.ToList());
        }
        finally
        {
            await TryShutdownAsync(process, cancellationToken);
            await stderrPump;
        }
    }

    private Process CreateProcess(string workspacePath, string absoluteFilePath)
    {
        var workingDirectory = string.Equals(_options.WorkingDirectoryMode, "file", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(absoluteFilePath) ?? workspacePath
            : workspacePath;
        var command = ResolveCommandPath(_options.Command);
        var arguments = ResolveArguments(command, workspacePath, _options.Arguments);

        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
    }

    private static string ResolveCommandPath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        if (Path.IsPathRooted(expanded) && File.Exists(expanded))
        {
            return expanded;
        }

        if (HasCommandOnPath(expanded))
        {
            return expanded;
        }

        var dotnetToolShim = TryResolveDotNetGlobalToolShim(expanded);
        return dotnetToolShim ?? expanded;
    }

    private static bool HasCommandOnPath(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var candidates = BuildExecutableCandidates(command);
        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidate in candidates)
            {
                if (File.Exists(Path.Combine(segment, candidate)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? TryResolveDotNetGlobalToolShim(string command)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return null;
        }

        var toolDirectory = Path.Combine(userProfile, ".dotnet", "tools");
        if (!Directory.Exists(toolDirectory))
        {
            return null;
        }

        foreach (var candidate in BuildExecutableCandidates(command))
        {
            var fullPath = Path.Combine(toolDirectory, candidate);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    private static string ResolveArguments(string command, string workspacePath, string? configuredArguments)
    {
        var arguments = Environment.ExpandEnvironmentVariables(configuredArguments ?? string.Empty).Trim();
        if (!IsCsharpLsCommand(command) ||
            string.IsNullOrWhiteSpace(workspacePath) ||
            arguments.Contains("--solution", StringComparison.OrdinalIgnoreCase))
        {
            return arguments;
        }

        var solutionPath = TryDetectSolutionPath(workspacePath);
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return arguments;
        }

        return string.IsNullOrWhiteSpace(arguments)
            ? $"--solution {QuoteArgument(solutionPath)}"
            : $"{arguments} --solution {QuoteArgument(solutionPath)}";
    }

    private static bool IsCsharpLsCommand(string command)
    {
        var fileName = Path.GetFileNameWithoutExtension(command);
        return string.Equals(fileName, "csharp-ls", StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveWarmupDelayMs(string? command, int configuredDelayMs)
    {
        if (configuredDelayMs <= 0 || string.IsNullOrWhiteSpace(command))
        {
            return 0;
        }

        return IsCsharpLsCommand(command)
            ? configuredDelayMs
            : 0;
    }

    private static string? TryDetectSolutionPath(string workspacePath)
    {
        if (!Directory.Exists(workspacePath))
        {
            return null;
        }

        var solutions = Directory.GetFiles(workspacePath, "*.sln", SearchOption.TopDirectoryOnly);
        return solutions.Length == 1
            ? Path.GetFileName(solutions[0])
            : null;
    }

    private static string QuoteArgument(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal)
            ? $"\"{value}\""
            : value;
    }

    private static IReadOnlyList<string> BuildExecutableCandidates(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.HasExtension(command)
                ? [command]
                : [command, $"{command}.exe", $"{command}.cmd", $"{command}.bat"];
        }

        return [command];
    }

    private static string ResolveLanguageId(string absoluteFilePath)
    {
        return string.Equals(Path.GetExtension(absoluteFilePath), ".cs", StringComparison.OrdinalIgnoreCase)
            ? "csharp"
            : string.Empty;
    }

    private static bool IsRecoverableFeatureFailure(Exception exception)
    {
        return exception is TimeoutException or InvalidOperationException;
    }

    private async Task<JsonElement?> SendRequestAsync(
        Stream stdin,
        Stream stdout,
        string method,
        object? parameters,
        int timeoutMs,
        ConcurrentQueue<WorkflowLspDiagnostic>? diagnostics,
        CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _requestId);
        await WriteMessageAsync(
            stdin,
            new JsonRpcRequestEnvelope
            {
                Id = requestId,
                Method = method,
                Params = parameters
            },
            cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        while (true)
        {
            var readTask = ReadMessageAsync(stdout, CancellationToken.None);
            var completedTask = await Task.WhenAny(
                readTask,
                Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token));

            if (completedTask != readTask)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException($"external LSP 请求超时：{method}。");
            }

            var payload = await readTask;
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (_options.TracePayloads)
            {
                _logger.LogDebug("External LSP response for {Method}: {Payload}", method, payload);
            }

            if (await TryHandleServerMessageAsync(stdin, root, diagnostics, timeoutCts.Token))
            {
                continue;
            }

            if (root.TryGetProperty("id", out var idProperty) &&
                idProperty.ValueKind == JsonValueKind.Number &&
                idProperty.GetInt32() == requestId)
            {
                if (root.TryGetProperty("error", out var errorProperty) &&
                    errorProperty.ValueKind != JsonValueKind.Null &&
                    errorProperty.ValueKind != JsonValueKind.Undefined)
                {
                    throw new InvalidOperationException(errorProperty.ToString());
                }

                if (!root.TryGetProperty("result", out var resultProperty))
                {
                    return null;
                }

                return resultProperty.Clone();
            }
        }
    }

    private async Task<bool> TryHandleServerMessageAsync(
        Stream stdin,
        JsonElement root,
        ConcurrentQueue<WorkflowLspDiagnostic>? diagnostics,
        CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("method", out var methodProperty) ||
            methodProperty.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var method = methodProperty.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(method))
        {
            return false;
        }

        if (root.TryGetProperty("id", out var idProperty) &&
            idProperty.ValueKind != JsonValueKind.Null &&
            idProperty.ValueKind != JsonValueKind.Undefined)
        {
            diagnostics?.Enqueue(new WorkflowLspDiagnostic
            {
                Level = "info",
                Message = $"handled server request: {method}"
            });
            await SendServerRequestResponseAsync(stdin, idProperty, method, root, cancellationToken);
            return true;
        }

        CaptureServerNotification(method, root, diagnostics);
        return true;
    }

    private async Task SendServerRequestResponseAsync(
        Stream stdin,
        JsonElement idProperty,
        string method,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        object? result = method switch
        {
            "client/registerCapability" => null,
            "client/unregisterCapability" => null,
            "workspace/configuration" => BuildWorkspaceConfigurationResult(root),
            "workspace/workspaceFolders" => Array.Empty<object?>(),
            "window/workDoneProgress/create" => null,
            _ => null
        };

        if (_options.TracePayloads)
        {
            _logger.LogDebug("External LSP server request {Method} handled with default response.", method);
        }

        await WriteMessageAsync(
            stdin,
            new JsonRpcResponseEnvelope
            {
                Id = ConvertJsonRpcId(idProperty),
                Result = result
            },
            cancellationToken);
    }

    private static object? BuildWorkspaceConfigurationResult(JsonElement root)
    {
        if (!root.TryGetProperty("params", out var paramsProperty) ||
            !paramsProperty.TryGetProperty("items", out var itemsProperty) ||
            itemsProperty.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<object?>();
        }

        var results = new List<object?>();
        foreach (var _ in itemsProperty.EnumerateArray())
        {
            results.Add(null);
        }

        return results;
    }

    private void CaptureServerNotification(
        string method,
        JsonElement root,
        ConcurrentQueue<WorkflowLspDiagnostic>? diagnostics)
    {
        if (diagnostics is null)
        {
            return;
        }

        switch (method)
        {
            case "textDocument/publishDiagnostics":
                if (root.TryGetProperty("params", out var paramsProperty) &&
                    paramsProperty.TryGetProperty("diagnostics", out var itemsProperty) &&
                    itemsProperty.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in itemsProperty.EnumerateArray())
                    {
                        diagnostics.Enqueue(new WorkflowLspDiagnostic
                        {
                            Level = "info",
                            Message = item.TryGetProperty("message", out var messageProperty) && messageProperty.ValueKind == JsonValueKind.String
                                ? messageProperty.GetString() ?? method
                                : method
                        });
                    }
                }
                break;
            case "window/logMessage":
            case "window/showMessage":
                if (root.TryGetProperty("params", out var messageParams) &&
                    messageParams.TryGetProperty("message", out var notificationMessageProperty) &&
                    notificationMessageProperty.ValueKind == JsonValueKind.String)
                {
                    diagnostics.Enqueue(new WorkflowLspDiagnostic
                    {
                        Level = "info",
                        Message = notificationMessageProperty.GetString() ?? method
                    });
                }
                break;
        }
    }

    private static object? ConvertJsonRpcId(JsonElement idProperty)
    {
        return idProperty.ValueKind switch
        {
            JsonValueKind.Number when idProperty.TryGetInt32(out var intId) => intId,
            JsonValueKind.Number when idProperty.TryGetInt64(out var longId) => longId,
            JsonValueKind.String => idProperty.GetString(),
            _ => idProperty.GetRawText()
        };
    }

    private async Task SendNotificationAsync(
        Stream stdin,
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        await WriteMessageAsync(
            stdin,
            new JsonRpcRequestEnvelope
            {
                Method = method,
                Params = parameters
            },
            cancellationToken);
    }

    private async Task WriteMessageAsync(Stream stdin, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        if (_options.TracePayloads)
        {
            _logger.LogDebug("External LSP request payload: {Payload}", json);
        }

        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await stdin.WriteAsync(header, cancellationToken);
        await stdin.WriteAsync(body, cancellationToken);
        await stdin.FlushAsync(cancellationToken);
    }

    private static async Task<string> ReadMessageAsync(Stream stdout, CancellationToken cancellationToken)
    {
        var contentLength = 0;
        while (true)
        {
            var line = await ReadAsciiLineAsync(stdout, cancellationToken);
            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(line["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        if (contentLength <= 0)
        {
            throw new InvalidOperationException("external LSP 响应缺少 Content-Length。");
        }

        var buffer = new byte[contentLength];
        var offset = 0;
        while (offset < contentLength)
        {
            var bytesRead = await stdout.ReadAsync(buffer.AsMemory(offset, contentLength - offset), cancellationToken);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException("external LSP 响应提前结束。");
            }

            offset += bytesRead;
        }

        return Encoding.UTF8.GetString(buffer);
    }

    private static async Task<string> ReadAsciiLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        while (true)
        {
            var buffer = new byte[1];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (bytesRead == 0)
            {
                if (bytes.Count == 0)
                {
                    throw new InvalidOperationException("external LSP 输出流已关闭。");
                }

                break;
            }

            if (buffer[0] == '\n')
            {
                break;
            }

            if (buffer[0] != '\r')
            {
                bytes.Add(buffer[0]);
            }
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static async Task PumpDiagnosticsAsync(
        StreamReader reader,
        ConcurrentQueue<WorkflowLspDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            diagnostics.Enqueue(new WorkflowLspDiagnostic
            {
                Level = "info",
                Message = line.Trim()
            });
        }
    }

    private static List<WorkflowLspResolvedLocation> ParseLocations(JsonElement? payload, string source)
    {
        if (payload is null)
        {
            return [];
        }

        var items = payload.Value.ValueKind == JsonValueKind.Array
            ? payload.Value.EnumerateArray().ToList()
            : payload.Value.ValueKind == JsonValueKind.Object
                ? [payload.Value]
                : [];

        var results = new List<WorkflowLspResolvedLocation>();
        foreach (var item in items)
        {
            if (!item.TryGetProperty("uri", out var uriProperty) ||
                uriProperty.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var filePath = ToFilePath(uriProperty.GetString());
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            int? lineNumber = null;
            int? columnNumber = null;
            if (item.TryGetProperty("range", out var rangeProperty) &&
                rangeProperty.TryGetProperty("start", out var startProperty))
            {
                lineNumber = startProperty.TryGetProperty("line", out var lineProperty) && lineProperty.ValueKind == JsonValueKind.Number
                    ? lineProperty.GetInt32() + 1
                    : null;
                columnNumber = startProperty.TryGetProperty("character", out var columnProperty) && columnProperty.ValueKind == JsonValueKind.Number
                    ? columnProperty.GetInt32() + 1
                    : null;
            }

            results.Add(new WorkflowLspResolvedLocation
            {
                FilePath = filePath,
                LineNumber = lineNumber,
                ColumnNumber = columnNumber,
                Source = source
            });
        }

        return results;
    }

    private static List<LspCallHierarchyItem> ParseCallHierarchyItems(JsonElement? payload)
    {
        if (payload is null)
        {
            return [];
        }

        if (payload.Value.ValueKind == JsonValueKind.Array)
        {
            return payload.Value.Deserialize<List<LspCallHierarchyItem>>(JsonOptions) ?? [];
        }

        if (payload.Value.ValueKind == JsonValueKind.Object)
        {
            var single = payload.Value.Deserialize<LspCallHierarchyItem>(JsonOptions);
            return single is null ? [] : [single];
        }

        return [];
    }

    private static IEnumerable<WorkflowCallHierarchyEdge> ParseOutgoingEdges(
        LspCallHierarchyItem rootItem,
        JsonElement? payload)
    {
        if (payload is null || payload.Value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        var outgoing = payload.Value.Deserialize<List<LspCallHierarchyOutgoingCall>>(JsonOptions) ?? [];
        foreach (var item in outgoing)
        {
            if (string.IsNullOrWhiteSpace(item.To.Name))
            {
                continue;
            }

            yield return new WorkflowCallHierarchyEdge
            {
                FromSymbol = rootItem.Name,
                ToSymbol = item.To.Name,
                Kind = "outgoing-call",
                Reason = ToFilePath(item.To.Uri)
            };
        }
    }

    private static IEnumerable<WorkflowCallHierarchyEdge> ParseIncomingEdges(
        LspCallHierarchyItem rootItem,
        JsonElement? payload)
    {
        if (payload is null || payload.Value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        var incoming = payload.Value.Deserialize<List<LspCallHierarchyIncomingCall>>(JsonOptions) ?? [];
        foreach (var item in incoming)
        {
            if (string.IsNullOrWhiteSpace(item.From.Name))
            {
                continue;
            }

            yield return new WorkflowCallHierarchyEdge
            {
                FromSymbol = item.From.Name,
                ToSymbol = rootItem.Name,
                Kind = "incoming-call",
                Reason = ToFilePath(item.From.Uri)
            };
        }
    }

    private static string ToFilePath(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return string.Empty;
        }

        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            && parsed.IsFile
            ? parsed.LocalPath.Replace('\\', '/')
            : uri.Replace('\\', '/');
    }

    private static WorkflowExternalLspSymbolResult BuildUnavailableResult(string strategy, string message)
    {
        return new WorkflowExternalLspSymbolResult
        {
            Attempted = false,
            Success = false,
            Strategy = strategy,
            FailureReason = message,
            Diagnostics =
            [
                new WorkflowLspDiagnostic
                {
                    Level = "info",
                    Message = message
                }
            ]
        };
    }

    private static WorkflowExternalLspSymbolResult BuildFailureResult(
        string message,
        IReadOnlyCollection<WorkflowLspDiagnostic>? diagnostics = null)
    {
        return new WorkflowExternalLspSymbolResult
        {
            Attempted = true,
            Success = false,
            Strategy = "external-lsp",
            FailureReason = message,
            Diagnostics = (diagnostics ?? [])
                .Concat(
                [
                    new WorkflowLspDiagnostic
                    {
                        Level = "warning",
                        Message = message
                    }
                ])
                .ToList()
        };
    }

    private async Task TryShutdownAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            await SendNotificationAsync(process.StandardInput.BaseStream, "exit", null, cancellationToken);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to gracefully shutdown external LSP process.");
        }
    }
}
