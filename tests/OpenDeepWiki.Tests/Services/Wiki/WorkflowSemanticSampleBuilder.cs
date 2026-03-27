using OpenDeepWiki.Services.Repositories;

namespace OpenDeepWiki.Tests.Services.Wiki;

internal sealed class WorkflowSemanticSampleBuilder : IDisposable
{
    private readonly string _rootPath;

    private WorkflowSemanticSampleBuilder(string rootPath)
    {
        _rootPath = rootPath;
    }

    public string RootPath => _rootPath;

    public RepositoryWorkspace CreateWorkspace()
    {
        return new RepositoryWorkspace
        {
            WorkingDirectory = _rootPath,
            Organization = "token",
            RepositoryName = "Wms.Sample",
            BranchName = "main",
            SourceLocation = _rootPath,
            GitUrl = _rootPath,
            CommitId = "sample-head"
        };
    }

    public static WorkflowSemanticSampleBuilder CreateWmsInboundSample()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "OpenDeepWiki.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var builder = new WorkflowSemanticSampleBuilder(rootPath);
        builder.WriteProjectFile();
        builder.WriteDomainFiles();
        builder.WriteControllerFile();
        builder.WriteWorkerFile();
        builder.WriteExecutorFiles();
        builder.WriteRegistrationFile();

        return builder;
    }

    public static WorkflowSemanticSampleBuilder CreateWcsNamedExecutorSample()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "OpenDeepWiki.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var builder = new WorkflowSemanticSampleBuilder(rootPath);
        builder.WriteProjectFile();
        builder.WriteNamedExecutorSupportFiles();
        builder.WriteNamedExecutorRepositoryFile();
        builder.WriteNamedExecutorControllerFile();
        builder.WriteNamedExecutorCompensationControllerFile();
        builder.WriteNamedExecutorServiceFile();
        builder.WriteNamedExecutorJobFile();
        builder.WriteNamedExecutorExecutorFiles();
        builder.WriteNamedExecutorHelperFile();

        return builder;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void WriteProjectFile()
    {
        WriteFile(
            "src/Wms.Sample/Wms.Sample.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <OutputType>Library</OutputType>
              </PropertyGroup>
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
              </ItemGroup>
            </Project>
            """);
    }

    private void WriteDomainFiles()
    {
        WriteFile(
            "src/Wms.Sample/Domain/ContainerPalletInboundRequest.cs",
            """
            namespace Wms.Sample.Domain;

            public enum InboundRequestStatus
            {
                Pending = 0,
                Processing = 1,
                Completed = 2
            }

            public sealed class ContainerPalletInboundRequest
            {
                public string Id { get; set; } = Guid.NewGuid().ToString("N");

                public string ContainerCode { get; set; } = string.Empty;

                public InboundRequestStatus Status { get; set; } = InboundRequestStatus.Pending;
            }
            """);

        WriteFile(
            "src/Wms.Sample/Domain/IInboundRequestRepository.cs",
            """
            namespace Wms.Sample.Domain;

            public interface IInboundRequestRepository
            {
                Task InsertAsync(ContainerPalletInboundRequest request, CancellationToken cancellationToken = default);

                Task<List<ContainerPalletInboundRequest>> ListPendingAsync(CancellationToken cancellationToken = default);

                Task SaveChangesAsync(CancellationToken cancellationToken = default);
            }

            internal sealed class InMemoryInboundRequestRepository : IInboundRequestRepository
            {
                public Task InsertAsync(ContainerPalletInboundRequest request, CancellationToken cancellationToken = default)
                {
                    return Task.CompletedTask;
                }

                public Task<List<ContainerPalletInboundRequest>> ListPendingAsync(CancellationToken cancellationToken = default)
                {
                    return Task.FromResult(new List<ContainerPalletInboundRequest>());
                }

                public Task SaveChangesAsync(CancellationToken cancellationToken = default)
                {
                    return Task.CompletedTask;
                }
            }
            """);
    }

    private void WriteControllerFile()
    {
        WriteFile(
            "src/Wms.Sample/Controllers/WcsInboundController.cs",
            """
            using Microsoft.AspNetCore.Mvc;
            using Wms.Sample.Domain;

            namespace Wms.Sample.Controllers;

            [ApiController]
            [Route("api/wcs/inbound")]
            public sealed class WcsInboundController(IInboundRequestRepository repository) : ControllerBase
            {
                [HttpPost("container-pallet")]
                public async Task<IActionResult> SubmitAsync(
                    [FromBody] ContainerPalletInboundRequest request,
                    CancellationToken cancellationToken)
                {
                    await repository.InsertAsync(request, cancellationToken);
                    return Accepted();
                }
            }
            """);
    }

    private void WriteWorkerFile()
    {
        WriteFile(
            "src/Wms.Sample/Workers/InboundRequestScanWorker.cs",
            """
            using Microsoft.Extensions.Hosting;
            using Wms.Sample.Domain;
            using Wms.Sample.Executors;

            namespace Wms.Sample.Workers;

            public sealed class InboundRequestScanWorker(
                IInboundRequestRepository repository,
                InboundExecutorFactory executorFactory) : BackgroundService
            {
                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    var pendingRequests = await repository.ListPendingAsync(stoppingToken);
                    foreach (var request in pendingRequests)
                    {
                        var executor = executorFactory.Resolve(request);
                        await executor.ExecuteAsync(request, stoppingToken);
                    }
                }
            }
            """);
    }

    private void WriteExecutorFiles()
    {
        WriteFile(
            "src/Wms.Sample/Executors/IInboundExecutor.cs",
            """
            using Wms.Sample.Domain;

            namespace Wms.Sample.Executors;

            public interface IInboundExecutor
            {
                Task ExecuteAsync(ContainerPalletInboundRequest request, CancellationToken cancellationToken = default);
            }
            """);

        WriteFile(
            "src/Wms.Sample/Executors/ContainerPalletInboundExecutor.cs",
            """
            using Wms.Sample.Domain;

            namespace Wms.Sample.Executors;

            public sealed class ContainerPalletInboundExecutor(IInboundRequestRepository repository) : IInboundExecutor
            {
                public async Task ExecuteAsync(ContainerPalletInboundRequest request, CancellationToken cancellationToken = default)
                {
                    request.Status = InboundRequestStatus.Processing;
                    await repository.SaveChangesAsync(cancellationToken);

                    request.Status = InboundRequestStatus.Completed;
                    await repository.SaveChangesAsync(cancellationToken);
                }
            }
            """);

        WriteFile(
            "src/Wms.Sample/Executors/InboundExecutorFactory.cs",
            """
            using Wms.Sample.Domain;

            namespace Wms.Sample.Executors;

            public sealed class InboundExecutorFactory(ContainerPalletInboundExecutor containerPalletInboundExecutor)
            {
                public IInboundExecutor Resolve(ContainerPalletInboundRequest request)
                {
                    return containerPalletInboundExecutor;
                }
            }
            """);
    }

    private void WriteRegistrationFile()
    {
        WriteFile(
            "src/Wms.Sample/Program.cs",
            """
            using Microsoft.Extensions.DependencyInjection;
            using Wms.Sample.Domain;
            using Wms.Sample.Executors;
            using Wms.Sample.Workers;

            namespace Wms.Sample;

            public static class Program
            {
                public static void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IInboundRequestRepository, InMemoryInboundRequestRepository>();
                    services.AddScoped<ContainerPalletInboundExecutor>();
                    services.AddScoped<IInboundExecutor, ContainerPalletInboundExecutor>();
                    services.AddScoped<InboundExecutorFactory>();
                    services.AddHostedService<InboundRequestScanWorker>();
                }
            }
            """);
    }

    private void WriteNamedExecutorSupportFiles()
    {
        WriteFile(
            "src/Wms.Sample/Infrastructure/WorkflowStubs.cs",
            """
            namespace Autofac
            {
                public interface ILifetimeScope
                {
                    T ResolveNamed<T>(string name);
                }
            }

            namespace Quartz
            {
                public interface IJob
                {
                    Task Execute(IJobExecutionContext context);
                }

                public interface IJobExecutionContext
                {
                }
            }

            namespace Wms.Sample.Infrastructure
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class IocAttribute(params string[] serviceKeys) : Attribute
                {
                    public string[] Names { get; } = serviceKeys;
                }

                public abstract class BaseJob : Quartz.IJob
                {
                    public Task Execute(Quartz.IJobExecutionContext context)
                    {
                        return ExecuteInner(context);
                    }

                    protected abstract Task ExecuteInner(Quartz.IJobExecutionContext context);
                }

                public abstract class WcsReqBody
                {
                }

                public sealed class WcsReq<TBody> where TBody : WcsReqBody, new()
                {
                    public TBody MsgBody { get; set; } = new();
                }
            }
            """);

        WriteFile(
            "src/Wms.Sample/Domain/WcsRequest.cs",
            """
            using Wms.Sample.Infrastructure;

            namespace Wms.Sample.Domain;

            public sealed class WcsRequest
            {
                public string Id { get; set; } = Guid.NewGuid().ToString("N");

                public string Status { get; set; } = "Pending";

                public string ToSystem { get; set; } = "WMS";

                public string MsgType { get; set; } = "WcsStnMoveIn";

                public string MsgCode { get; set; } = "None";

                public static WcsRequest CreateByJson(string json)
                {
                    return new WcsRequest();
                }

                public static string GetServiceName(string status, string toSystem, string msgType, string msgCode)
                {
                    return $"{msgType}.{msgCode}";
                }

                public WcsReq<TBody> GetReq<TBody>() where TBody : WcsReqBody, new()
                {
                    return new WcsReq<TBody>();
                }
            }

            public sealed class WcsStnMoveInBody : WcsReqBody
            {
                public string TargetStatus { get; set; } = "Completed";
            }
            """);
    }

    private void WriteNamedExecutorRepositoryFile()
    {
        WriteFile(
            "src/Wms.Sample/Domain/IWcsRequestRepository.cs",
            """
            namespace Wms.Sample.Domain;

            public interface IWcsRequestRepository
            {
                Task InsertAsync(WcsRequest request, CancellationToken cancellationToken = default);

                Task<List<WcsRequest>> ListPendingAsync(CancellationToken cancellationToken = default);

                Task<WcsRequest?> FindAsync(string id, CancellationToken cancellationToken = default);

                Task UpdateAsync(WcsRequest request, CancellationToken cancellationToken = default);
            }

            public sealed class InMemoryWcsRequestRepository : IWcsRequestRepository
            {
                public Task InsertAsync(WcsRequest request, CancellationToken cancellationToken = default)
                {
                    return Task.CompletedTask;
                }

                public Task<List<WcsRequest>> ListPendingAsync(CancellationToken cancellationToken = default)
                {
                    return Task.FromResult(new List<WcsRequest>());
                }

                public Task<WcsRequest?> FindAsync(string id, CancellationToken cancellationToken = default)
                {
                    return Task.FromResult<WcsRequest?>(new WcsRequest());
                }

                public Task UpdateAsync(WcsRequest request, CancellationToken cancellationToken = default)
                {
                    return Task.CompletedTask;
                }
            }
            """);
    }

    private void WriteNamedExecutorControllerFile()
    {
        WriteFile(
            "src/Wms.Sample/Controllers/WmsJobInterfaceController.cs",
            """
            using Microsoft.AspNetCore.Mvc;
            using Wms.Sample.Services;

            namespace Wms.Sample.Controllers;

            [ApiController]
            [Route("api/[controller]")]
            public sealed class WmsJobInterfaceController(IWcsRequestService wcsRequestService) : ControllerBase
            {
                [HttpPost]
                public async Task<IActionResult> Post(CancellationToken cancellationToken)
                {
                    await wcsRequestService.CreateByReq("{}", cancellationToken);
                    return Accepted();
                }
            }
            """);
    }

    private void WriteNamedExecutorServiceFile()
    {
        WriteFile(
            "src/Wms.Sample/Services/WcsRequestService.cs",
            """
            using Wms.Sample.Domain;

            namespace Wms.Sample.Services;

            public interface IWcsRequestService
            {
                Task CreateByReq(string json, CancellationToken cancellationToken = default);
            }

            public sealed class WcsRequestService(IWcsRequestRepository wcsRequestRepository) : IWcsRequestService
            {
                public async Task CreateByReq(string json, CancellationToken cancellationToken = default)
                {
                    var request = WcsRequest.CreateByJson(json);
                    await wcsRequestRepository.InsertAsync(request, cancellationToken);
                }
            }
            """);
    }

    private void WriteNamedExecutorCompensationControllerFile()
    {
        WriteFile(
            "src/Wms.Sample/Controllers/LogExternalInterfaceController.cs",
            """
            using Microsoft.AspNetCore.Mvc;
            using Wms.Sample.Services;

            namespace Wms.Sample.Controllers;

            [ApiController]
            [Route("api/[controller]")]
            public sealed class LogExternalInterfaceController(IWcsRequestService wcsRequestService) : ControllerBase
            {
                [HttpPost("{id:long}/retry")]
                public async Task<IActionResult> Retry(long id, CancellationToken cancellationToken)
                {
                    await wcsRequestService.CreateByReq($"retry-{id}", cancellationToken);
                    return Accepted();
                }
            }
            """);
    }

    private void WriteNamedExecutorJobFile()
    {
        WriteFile(
            "src/Wms.Sample/Jobs/WcsRequestWmsExecutorJob.cs",
            """
            using Autofac;
            using Quartz;
            using Wms.Sample.Domain;
            using Wms.Sample.Executors;
            using Wms.Sample.Infrastructure;

            namespace Wms.Sample.Jobs;

            public sealed class WcsRequestWmsExecutorJob(
                ILifetimeScope lifetimeScope,
                IWcsRequestRepository wcsRequestRepository) : BaseJob
            {
                protected override async Task ExecuteInner(IJobExecutionContext context)
                {
                    var requests = await wcsRequestRepository.ListPendingAsync();
                    foreach (var request in requests)
                    {
                        var executor = lifetimeScope.ResolveNamed<IWcsRequestExecutor>(
                            WcsRequest.GetServiceName(request.Status, request.ToSystem, request.MsgType, request.MsgCode));

                        await executor.Execute(request.Id);
                    }
                }
            }
            """);
    }

    private void WriteNamedExecutorExecutorFiles()
    {
        WriteFile(
            "src/Wms.Sample/Executors/IWcsRequestExecutor.cs",
            """
            namespace Wms.Sample.Executors;

            public interface IWcsRequestExecutor
            {
                Task Execute(string requestId, CancellationToken cancellationToken = default);
            }
            """);

        WriteFile(
            "src/Wms.Sample/Executors/WcsStnMoveInExecutor.cs",
            """
            using Wms.Sample.Domain;
            using Wms.Sample.Infrastructure;

            namespace Wms.Sample.Executors;

            /// <summary>
            /// 站台移入申请
            /// </summary>
            [Ioc("WcsStnMoveIn.None")]
            public sealed class WcsStnMoveInExecutor(IWcsRequestRepository wcsRequestRepository) : IWcsRequestExecutor
            {
                public async Task Execute(string requestId, CancellationToken cancellationToken = default)
                {
                    var request = await wcsRequestRepository.FindAsync(requestId, cancellationToken);
                    if (request is null)
                    {
                        return;
                    }

                    var req = request.GetReq<WcsStnMoveInBody>();
                    request.Status = "Processing";
                    await wcsRequestRepository.UpdateAsync(request, cancellationToken);

                    request.Status = req.MsgBody.TargetStatus;
                    await wcsRequestRepository.UpdateAsync(request, cancellationToken);
                }
            }
            """);
    }

    private void WriteNamedExecutorHelperFile()
    {
        WriteFile(
            "src/Wms.Sample/Helpers/MultipartRequestHelper.cs",
            """
            namespace Wms.Sample.Helpers;

            public sealed class MultipartRequestHelper
            {
                public string Normalize(string payload)
                {
                    return payload.Trim();
                }
            }
            """);
    }

    private void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
