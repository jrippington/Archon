using Archon.Api.Extraction;
using Archon.Api.Extraction.Contracts;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Graph.Persistence;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Model;
using Archon.Extractors.Projects.Solutions;
using Archon.Infrastructure.Roslyn.Extraction;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Archon.Api.Extraction.Tests
{
    /// <summary>
    /// Verifies the WP004 extraction HTTP endpoints translate requests into application-layer start and status behavior.
    /// </summary>
    public sealed class ExtractionEndpointTests : IDisposable
    {
        /// <summary>
        /// Stores temporary repository roots created by endpoint tests for deterministic cleanup.
        /// </summary>
        private readonly List<string> _temporaryDirectories = [];

        /// <summary>
        /// Deletes temporary repository directories created by endpoint tests.
        /// </summary>
        public void Dispose()
        {
            // Endpoint tests use real filesystem paths because validation is part of the public HTTP behavior.
            foreach (string temporaryDirectory in _temporaryDirectories)
            {
                DeleteTemporaryDirectoryWithRetry(temporaryDirectory);
            }
        }

        /// <summary>
        /// Deletes a temporary repository directory while tolerating brief background extraction file handles.
        /// </summary>
        /// <param name="temporaryDirectory">The temporary repository directory created by the test.</param>
        private static void DeleteTemporaryDirectoryWithRetry(string temporaryDirectory)
        {
            // The in-process scheduler can still be finishing solution-file reads after an endpoint returns Accepted, so cleanup retries transient sharing violations.
            const int MaximumAttempts = 5;
            for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                try
                {
                    if (Directory.Exists(temporaryDirectory))
                    {
                        Directory.Delete(temporaryDirectory, recursive: true);
                    }

                    return;
                }
                catch (IOException) when (attempt < MaximumAttempts)
                {
                    // A short backoff lets the background extraction task release its file handle without making normal cleanup slow.
                    Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
                }
                catch (UnauthorizedAccessException) when (attempt < MaximumAttempts)
                {
                    // Windows can report transient file-handle cleanup races as unauthorized access, so retry them the same way.
                    Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
                }
            }
        }

        /// <summary>
        /// Verifies extraction API service registration composes project, semantic, WP007, WP008, and WP009 extraction stages instead of the WP004 placeholder stage.
        /// </summary>
        [Fact]
        public void AddArchonExtractionApi_WhenServicesAreBuilt_ShouldRegisterProjectSemanticWp007Wp008AndWp009ExtractionStages()
        {
            // The API module is the existing composition boundary for the extraction pipeline, so this test guards the ordered stage registration path.
            ServiceCollection services = new();

            services.AddArchonExtractionApi();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IExtractionStage[] stages = serviceProvider.GetServices<IExtractionStage>().ToArray();
            Assert.Collection(
                stages,
                stage =>
                {
                    Assert.IsType<RepositorySolutionExtractionStage>(stage);
                    Assert.Equal("project-repository-solution", stage.StageId);
                },
                stage =>
                {
                    Assert.IsType<RoslynSemanticExtractionStage>(stage);
                    Assert.Equal("roslyn-semantic", stage.StageId);
                },
                stage =>
                {
                    Assert.IsType<Wp007ExtractionStage>(stage);
                    Assert.Equal("wp007-configuration-dependency-injection", stage.StageId);
                },
                stage =>
                {
                    Assert.IsType<Wp008AspNetCoreMinimalApiExtractionStage>(stage);
                    Assert.Equal("wp008-aspnet-core-minimal-api", stage.StageId);
                },
                stage =>
                {
                    Assert.IsType<Wp009DataAccessExtractionStage>(stage);
                    Assert.Equal("wp009-data-access-dbml", stage.StageId);
                });
        }

        /// <summary>
        /// Verifies POST /extractions accepts a valid request and returns a queued run identifier quickly.
        /// </summary>
        /// <returns>A task that completes after the endpoint response is validated.</returns>
        [Fact]
        public async Task PostExtractions_WhenRequestIsValid_ShouldReturnAcceptedRun()
        {
            // The in-memory test host exercises route mapping and JSON serialization without starting Kestrel or Aspire.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln");
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();
            StartExtractionApiRequest request = new(
                repositoryRoot,
                ["CustomerSuite.sln"],
                BranchName: "main",
                CommitSha: "abcdef1234567890",
                RequestedBy: "developer@example.invalid",
                Metadata: new Dictionary<string, string>
                {
                    ["source"] = "api-test"
                });

            HttpResponseMessage response = await client.PostAsJsonAsync("/extractions", request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("runId").GetString()));
            Assert.Equal("Queued", body.RootElement.GetProperty("status").GetString());
            Assert.Equal(1, body.RootElement.GetProperty("submittedRequest").GetProperty("solutionPaths").GetArrayLength());
        }

        /// <summary>
        /// Verifies the API-triggered extraction path persists WP008 ASP.NET Core minimal API endpoint facts through the snapshot writer seam.
        /// </summary>
        /// <returns>A task that completes after the completed run and recorded endpoint snapshot content have been asserted.</returns>
        [Fact]
        public async Task GetExtractionStatus_WhenWp008MinimalApiExtractionRuns_ShouldPersistEndpointFactsThroughSnapshotWriter()
        {
            // The test exercises the accepted API orchestration path without starting Aspire, Neo4j, or the target ASP.NET Core application.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln", "Customer.Api", "Customer.Api.csproj");
            CreateProjectFile(repositoryRoot, "Customer.Api.csproj", "Program.cs");
            CreateMinimalApiProgramFile(repositoryRoot, "Program.cs");
            RecordingSnapshotWriter writer = new("snapshot://wp008-api-test");
            await using WebApplication app = await CreateApplicationAsync(services => services.AddSingleton<IArchitectureSnapshotWriter>(writer));
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage startResponse = await client.PostAsJsonAsync(
                "/extractions",
                new StartExtractionApiRequest(repositoryRoot, ["CustomerSuite.sln"], null, null, null, null));
            using JsonDocument startBody = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
            string runId = startBody.RootElement.GetProperty("runId").GetString()!;

            JsonDocument statusBody = await PollForTerminalStatusAsync(client, runId);

            using (statusBody)
            {
                Assert.Equal("Completed", statusBody.RootElement.GetProperty("status").GetString());
                Assert.NotNull(writer.WrittenSnapshot);
                ExtractedArchitectureSnapshot snapshot = writer.WrittenSnapshot;
                ArchitectureNode endpointNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "GET /customers/{id}");
                Assert.Equal("project://Customer.Api.csproj", endpointNode.ProjectStableKey?.Value);
                Assert.Contains("\"runtimeKind\":\"MinimalApi\"", endpointNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"framework\":\"ASP.NET Core\"", endpointNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"httpMethod\":\"GET\"", endpointNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"routeTemplate\":\"/customers/{id}\"", endpointNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.DeclaresEndpoint && edge.SourceNodeStableKey.Value == "project://Customer.Api.csproj" && edge.TargetNodeStableKey == endpointNode.StableKey);
                Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "Program.cs" && evidence.SymbolName == "MapGet" && evidence.SnippetPreview?.Contains("MapGet(\"/customers/{id}\"", StringComparison.Ordinal) == true);
                Assert.Empty(snapshot.Errors);
            }
        }

        /// <summary>
        /// Verifies the API-triggered extraction path persists all currently wired WP008 runtime slices through the shared snapshot writer seam.
        /// </summary>
        /// <returns>A task that completes after runtime graph facts from web, console, worker, and non-HTTP consumer slices have been asserted.</returns>
        [Fact]
        public async Task GetExtractionStatus_WhenWp008RuntimeExtractionRuns_ShouldPersistRuntimeFactsThroughSnapshotWriter()
        {
            // This test is the Work Item 7 orchestration guard: it proves runtime extraction runs after earlier stages through the public API path, without Aspire, target application startup, or direct extractor persistence.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln", "Customer.Runtime", "Customer.Runtime.csproj");
            CreateProjectFile(repositoryRoot, "Customer.Runtime.csproj", "RuntimeProgram.cs");
            CreateRuntimeProgramFile(repositoryRoot, "RuntimeProgram.cs");
            RecordingSnapshotWriter writer = new("snapshot://wp008-runtime-api-test");
            await using WebApplication app = await CreateApplicationAsync(services => services.AddSingleton<IArchitectureSnapshotWriter>(writer));
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage startResponse = await client.PostAsJsonAsync(
                "/extractions",
                new StartExtractionApiRequest(repositoryRoot, ["CustomerSuite.sln"], null, null, null, null));
            using JsonDocument startBody = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
            string runId = startBody.RootElement.GetProperty("runId").GetString()!;

            JsonDocument statusBody = await PollForTerminalStatusAsync(client, runId);

            using (statusBody)
            {
                Assert.Equal("Completed", statusBody.RootElement.GetProperty("status").GetString());
                Assert.NotNull(writer.WrittenSnapshot);
                ExtractedArchitectureSnapshot snapshot = writer.WrittenSnapshot;
                ArchitectureNode endpointNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "GET /customers/{id}");
                ArchitectureNode consoleNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.Metadata.ToCanonicalJson().Contains("\"runtimeKind\":\"ConsoleEntryPoint\"", StringComparison.Ordinal));
                ArchitectureNode hostedServiceNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.HostedService && node.DisplayName == "Worker");
                ArchitectureNode queueNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Queue && node.DisplayName == "orders");
                ArchitectureNode handlerNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "HandleOrderAsync");
                ArchitectureNode scheduledJobNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.Metadata.ToCanonicalJson().Contains("\"runtimeKind\":\"ScheduledJob\"", StringComparison.Ordinal));
                Assert.Contains("\"runtimeKind\":\"BackgroundService\"", hostedServiceNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"transportKind\":\"AzureServiceBus\"", queueNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"schedulerTechnology\":\"Hangfire\"", scheduledJobNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.DeclaresEndpoint && edge.TargetNodeStableKey == endpointNode.StableKey);
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Handles && edge.SourceNodeStableKey == handlerNode.StableKey && edge.TargetNodeStableKey == queueNode.StableKey);
                Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "RuntimeProgram.cs" && evidence.SymbolName == "MapGet");
                Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "RuntimeProgram.cs" && evidence.SymbolName == "CreateProcessor");
                Assert.Contains(snapshot.Warnings, warning => warning.Contains("no matching AddHostedService registration", StringComparison.Ordinal));
                Assert.Empty(snapshot.Errors);
                Assert.NotNull(consoleNode.PrimaryEvidenceStableKey);
            }
        }

        /// <summary>
        /// Verifies the API-triggered extraction path persists WP009 LINQ to SQL DBML data-access facts through the snapshot writer seam.
        /// </summary>
        /// <returns>A task that completes after the completed run and recorded DBML snapshot content have been asserted.</returns>
        [Fact]
        public async Task GetExtractionStatus_WhenWp009DbmlExtractionRuns_ShouldPersistDataAccessFactsThroughSnapshotWriter()
        {
            // The test proves DBML extraction participates in the public API-triggered pipeline without target database connectivity or direct Neo4j writes.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln");
            CreateDbmlModelFile(repositoryRoot, "Data", "Northwind.dbml");
            RecordingSnapshotWriter writer = new("snapshot://wp009-dbml-api-test");
            await using WebApplication app = await CreateApplicationAsync(services => services.AddSingleton<IArchitectureSnapshotWriter>(writer));
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage startResponse = await client.PostAsJsonAsync(
                "/extractions",
                new StartExtractionApiRequest(repositoryRoot, ["CustomerSuite.sln"], null, null, null, null));
            using JsonDocument startBody = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
            string runId = startBody.RootElement.GetProperty("runId").GetString()!;

            JsonDocument statusBody = await PollForTerminalStatusAsync(client, runId);

            using (statusBody)
            {
                Assert.Equal("Completed", statusBody.RootElement.GetProperty("status").GetString());
                Assert.NotNull(writer.WrittenSnapshot);
                ExtractedArchitectureSnapshot snapshot = writer.WrittenSnapshot;
                ArchitectureNode contextNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.LinqToSqlDataContext);
                ArchitectureNode entityNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Entity);
                ArchitectureNode tableNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseTable);
                ArchitectureNode columnNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseColumn);
                ArchitectureNode procedureNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.StoredProcedure);
                Assert.Equal("NorthwindDataContext", contextNode.DisplayName);
                Assert.Contains("\"dataAccessTechnology\":\"LinqToSql\"", contextNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.MapsEntity && edge.SourceNodeStableKey == contextNode.StableKey && edge.TargetNodeStableKey == entityNode.StableKey);
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.MapsTable && edge.SourceNodeStableKey == entityNode.StableKey && edge.TargetNodeStableKey == tableNode.StableKey);
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.MapsColumn && edge.SourceNodeStableKey == tableNode.StableKey && edge.TargetNodeStableKey == columnNode.StableKey);
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.CallsStoredProcedure && edge.SourceNodeStableKey == contextNode.StableKey && edge.TargetNodeStableKey == procedureNode.StableKey);
                Assert.Contains(snapshot.Evidence, evidence => evidence.EvidenceKind == EvidenceKind.Dbml && evidence.FilePath.Value == "Data/Northwind.dbml");
                Assert.Empty(snapshot.Errors);
            }
        }

        /// <summary>
        /// Verifies the API-triggered extraction path correlates WP009 data-access facts with earlier configuration, dependency-injection, and runtime facts.
        /// </summary>
        /// <returns>A task that completes after the integrated data-access snapshot has been asserted.</returns>
        [Fact]
        public async Task GetExtractionStatus_WhenWp009IntegratedExtractionRuns_ShouldCorrelateConfigurationDependencyInjectionRuntimeAndDataAccessFacts()
        {
            // The fixture intentionally exercises all API-wired precursor stages before WP009 so cross-slice correlation is validated at the public orchestration seam.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln", "Customer.Api", "Customer.Api.csproj");
            CreateProjectFile(repositoryRoot, "Customer.Api.csproj", "Customer.Api.DataAccess.cs");
            CreateWp009IntegratedDataAccessSourceFile(repositoryRoot, "Customer.Api.DataAccess.cs");
            CreateWp009ConnectionConfigurationFiles(repositoryRoot);
            RecordingSnapshotWriter writer = new("snapshot://wp009-integrated-api-test");
            await using WebApplication app = await CreateApplicationAsync(services => services.AddSingleton<IArchitectureSnapshotWriter>(writer));
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage startResponse = await client.PostAsJsonAsync(
                "/extractions",
                new StartExtractionApiRequest(repositoryRoot, ["CustomerSuite.sln"], null, null, null, null));
            using JsonDocument startBody = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
            string runId = startBody.RootElement.GetProperty("runId").GetString()!;

            JsonDocument statusBody = await PollForTerminalStatusAsync(client, runId);

            using (statusBody)
            {
                Assert.Equal("Completed", statusBody.RootElement.GetProperty("status").GetString());
                Assert.NotNull(writer.WrittenSnapshot);
                ExtractedArchitectureSnapshot snapshot = writer.WrittenSnapshot;
                ArchitectureNode dbContextNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.DbContext && node.DisplayName == "CustomerDbContext");
                ArchitectureNode configurationNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.ConfigurationKey && node.StableKey.Value == "config://Legacy:ConnectionStrings:MainDb");
                ArchitectureNode customerTableNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseTable && node.StableKey.Value == "dbtable://Customer.Api.csproj#dbo.Customers");
                ArchitectureNode runtimeMethodNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "ExecuteAsync" && node.StableKey.Value.StartsWith("method://Customer.Api.CustomerWorker.ExecuteAsync", StringComparison.Ordinal));
                ArchitectureEdge runtimeCorrelationEdge = Assert.Single(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.DependsOn && edge.SourceNodeStableKey == runtimeMethodNode.StableKey && edge.Metadata.ToCanonicalJson().Contains("\"correlationKind\":\"RuntimeDataAccessMethod\"", StringComparison.Ordinal));
                ArchitectureNode dataAccessMethodNode = Assert.Single(snapshot.Nodes, node => node.StableKey == runtimeCorrelationEdge.TargetNodeStableKey);
                Assert.Contains("\"connectionStringKey\":\"MainDb\"", dbContextNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesConfig && edge.SourceNodeStableKey == dbContextNode.StableKey && edge.TargetNodeStableKey == configurationNode.StableKey && edge.Metadata.ToCanonicalJson().Contains("\"correlationKind\":\"DataAccessConnectionStringKey\"", StringComparison.Ordinal));
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesDbContext && edge.SourceNodeStableKey.Value == "type://Customer.Api.CustomerDbContext" && edge.TargetNodeStableKey == dbContextNode.StableKey && edge.Metadata.ToCanonicalJson().Contains("\"correlationKind\":\"DependencyInjectionDbContextRegistration\"", StringComparison.Ordinal));
                Assert.Equal("LoadCustomers", dataAccessMethodNode.DisplayName);
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ReadsTable && edge.TargetNodeStableKey == customerTableNode.StableKey);
                Assert.Equal(snapshot.Nodes.Count, snapshot.Nodes.Select(node => node.StableKey.Value).Distinct(StringComparer.Ordinal).Count());
                Assert.Equal(snapshot.Edges.Count, snapshot.Edges.Select(edge => edge.StableKey.Value).Distinct(StringComparer.Ordinal).Count());
                Assert.DoesNotContain(snapshot.Evidence.Where(evidence => evidence.EvidenceKind is not null && evidence.EvidenceKind.Value is not "CompilerDiagnostic" and not "CompilerSymbol"), evidence => evidence.SnippetPreview?.Contains("SuperSecretPassword123!", StringComparison.Ordinal) == true);
                Assert.Empty(snapshot.Errors);
            }
        }

        /// <summary>
        /// Verifies the API-triggered extraction path persists Roslyn semantic facts through the snapshot writer seam.
        /// </summary>
        /// <returns>A task that completes after the completed run and recorded snapshot have been asserted.</returns>
        [Fact]
        public async Task GetExtractionStatus_WhenSemanticExtractionRuns_ShouldPersistSemanticFactsThroughSnapshotWriter()
        {
            // This test exercises the shared API orchestration path without Aspire or Neo4j by replacing only the application persistence port.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln", "Customer.Api", "Customer.Api.csproj");
            CreateProjectFile(repositoryRoot, "Customer.Api.csproj", "Customer.Api.CustomerService.cs");
            CreateCSharpSourceFile(repositoryRoot, "Customer.Api.CustomerService.cs");
            RecordingSnapshotWriter writer = new("snapshot://semantic-api-test");
            await using WebApplication app = await CreateApplicationAsync(services => services.AddSingleton<IArchitectureSnapshotWriter>(writer));
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage startResponse = await client.PostAsJsonAsync(
                "/extractions",
                new StartExtractionApiRequest(repositoryRoot, ["CustomerSuite.sln"], null, null, null, null));
            using JsonDocument startBody = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
            string runId = startBody.RootElement.GetProperty("runId").GetString()!;

            JsonDocument statusBody = await PollForTerminalStatusAsync(client, runId);

            using (statusBody)
            {
                Assert.Equal("Completed", statusBody.RootElement.GetProperty("status").GetString());
                Assert.NotNull(writer.WrittenSnapshot);
                ExtractedArchitectureSnapshot snapshot = writer.WrittenSnapshot;
                Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Type && node.DisplayName == "CustomerService" && node.ProjectStableKey?.Value == "project://Customer.Api.csproj");
                Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "GetName");
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Contains && edge.SourceNodeStableKey.Value.Contains("type://", StringComparison.Ordinal));
                Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "Customer.Api.CustomerService.cs" && evidence.SymbolName == "CustomerService");
                Assert.Empty(snapshot.Errors);
            }
        }

        /// <summary>
        /// Creates a top-level ASP.NET Core minimal API program source file for WP008 endpoint extraction tests.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that contains the source file.</param>
        /// <param name="relativeSourcePath">The repository-relative source path to write.</param>
        private static void CreateMinimalApiProgramFile(string repositoryRoot, string relativeSourcePath)
        {
            // Local stubs make the fixture compile under the lightweight Roslyn loader while preserving the Program.cs MapGet shape WP008 extracts.
            string sourcePath = Path.Combine(repositoryRoot, relativeSourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(
                sourcePath,
                string.Join(
                    Environment.NewLine,
                    [
                        "public sealed class WebApplication",
                        "{",
                        "    public static WebApplicationBuilder CreateBuilder(string[] args) => new();",
                        "    public void MapGet(string route, object handler) { }",
                        "    public void Run() { }",
                        "}",
                        "public sealed class WebApplicationBuilder",
                        "{",
                        "    public WebApplication Build() => new();",
                        "}",
                        "public static class Results",
                        "{",
                        "    public static object Ok(object? value = null) => new();",
                        "}",
                        "var builder = WebApplication.CreateBuilder(args);",
                        "var app = builder.Build();",
                        "app.MapGet(\"/customers/{id}\", (int id) => Results.Ok(id));",
                        "app.Run();"
                    ]));
        }

        /// <summary>
        /// Creates a runtime-rich C# source file that exercises the currently orchestrated WP008 runtime slices in one API extraction request.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that contains the source file.</param>
        /// <param name="relativeSourcePath">The repository-relative source path to write.</param>
        private static void CreateRuntimeProgramFile(string repositoryRoot, string relativeSourcePath)
        {
            // The fixture keeps all runtime patterns in one submitted project so the API-stage context handoff can be asserted from a single recorded snapshot.
            string sourcePath = Path.Combine(repositoryRoot, relativeSourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(
                sourcePath,
                string.Join(
                    Environment.NewLine,
                    [
                        "namespace Microsoft.Extensions.Hosting { public interface IHostedService { System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken); System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken); } public abstract class BackgroundService : IHostedService { public virtual System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask; public virtual System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask; protected abstract System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken); } }",
                        "namespace Hangfire { public static class RecurringJob { public static void AddOrUpdate<T>(string recurringJobId, System.Linq.Expressions.Expression<System.Action<T>> methodCall, string cronExpression) { } } }",
                        "public sealed class WebApplication { public static WebApplicationBuilder CreateBuilder(string[] args) => new(); public void MapGet(string route, object handler) { } public void Run() { } }",
                        "public sealed class WebApplicationBuilder { public WebApplication Build() => new(); }",
                        "public static class Results { public static object Ok(object? value = null) => new(); }",
                        "public sealed class ServiceBusClient { public ServiceBusProcessor CreateProcessor(string queueName) => new(); }",
                        "public sealed class ServiceBusProcessor { public event System.Func<ProcessMessageEventArgs, System.Threading.Tasks.Task>? ProcessMessageAsync; }",
                        "public sealed class ProcessMessageEventArgs { }",
                        "public sealed class Worker : Microsoft.Extensions.Hosting.BackgroundService { protected override System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken) => System.Threading.Tasks.Task.CompletedTask; }",
                        "public sealed class QueueConsumer { public void Configure(ServiceBusClient client) { ServiceBusProcessor processor = client.CreateProcessor(\"orders\"); processor.ProcessMessageAsync += HandleOrderAsync; } public System.Threading.Tasks.Task HandleOrderAsync(ProcessMessageEventArgs args) => System.Threading.Tasks.Task.CompletedTask; }",
                        "public sealed class CustomerSyncJob { public void Configure() { Hangfire.RecurringJob.AddOrUpdate<CustomerSyncJob>(\"customer-sync\", job => job.SyncCustomers(), \"0 0 * * *\"); } public void SyncCustomers() { } }",
                        "var builder = WebApplication.CreateBuilder(args);",
                        "var app = builder.Build();",
                        "app.MapGet(\"/customers/{id}\", (int id) => Results.Ok(id));",
                        "var consumer = new QueueConsumer();",
                        "consumer.Configure(new ServiceBusClient());",
                        "var scheduler = new CustomerSyncJob();",
                        "scheduler.Configure();",
                        "app.Run();"
                    ]));
        }

        /// <summary>
        /// Verifies the API-triggered extraction path composes WP007 dependency-injection and configuration extractors into one snapshot.
        /// </summary>
        /// <returns>A task that completes after the completed run and combined WP007 snapshot content have been asserted.</returns>
        [Fact]
        public async Task GetExtractionStatus_WhenWp007ExtractionRuns_ShouldPersistDependencyInjectionAndConfigurationFacts()
        {
            // The fixture deliberately flows through the public API route so WP007 composition is validated at the same orchestration seam used by callers.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln", "Customer.Api", "Customer.Api.csproj");
            CreateProjectFile(repositoryRoot, "Customer.Api.csproj", "Customer.Api.Composition.cs");
            CreateWp007SourceFile(repositoryRoot, "Customer.Api.Composition.cs");
            CreateWp007ConfigurationFiles(repositoryRoot);
            RecordingSnapshotWriter writer = new("snapshot://wp007-api-test");
            await using WebApplication app = await CreateApplicationAsync(services => services.AddSingleton<IArchitectureSnapshotWriter>(writer));
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage startResponse = await client.PostAsJsonAsync(
                "/extractions",
                new StartExtractionApiRequest(repositoryRoot, ["CustomerSuite.sln"], null, null, null, null));
            using JsonDocument startBody = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
            string runId = startBody.RootElement.GetProperty("runId").GetString()!;

            JsonDocument statusBody = await PollForTerminalStatusAsync(client, runId);

            using (statusBody)
            {
                Assert.Equal("Completed", statusBody.RootElement.GetProperty("status").GetString());
                Assert.NotNull(writer.WrittenSnapshot);
                ExtractedArchitectureSnapshot snapshot = writer.WrittenSnapshot;
                Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.ConfigurationKey && node.StableKey.Value == "config://Services:Orders:BaseUrl");
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesConfig && edge.TargetNodeStableKey.Value == "config://Services:Orders:BaseUrl");
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.RegisteredAsService && edge.SourceNodeStableKey.Value == "type://Customer.Api.OrderService" && edge.TargetNodeStableKey.Value == "type://Customer.Api.IOrderService");
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.RegisteredAsService && edge.Metadata.ToCanonicalJson().Contains("\"containerKind\":\"Autofac\"", StringComparison.Ordinal));
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Injects && edge.SourceNodeStableKey.Value == "type://Customer.Api.OrderService" && edge.TargetNodeStableKey.Value == "type://Customer.Api.IRepository");
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.DependsOn && edge.SourceNodeStableKey.Value == "type://Customer.Api.OrderService" && edge.TargetNodeStableKey.Value == "type://Customer.Api.IRepository");
                Assert.Contains(snapshot.Warnings, warning => warning.Contains("Unsupported legacy container registration", StringComparison.Ordinal));
                Assert.Empty(snapshot.Errors);
                Assert.DoesNotContain(snapshot.Evidence, evidence => evidence.SnippetPreview?.Contains("SuperSecretPassword123!", StringComparison.Ordinal) == true);
                Assert.Equal(snapshot.Edges.Count, snapshot.Edges.Select(edge => edge.StableKey.Value).Distinct(StringComparer.Ordinal).Count());
            }
        }

        /// <summary>
        /// Verifies extraction routes are exposed at the resolved WP004 paths and not behind an accidental common API prefix.
        /// </summary>
        /// <returns>A task that completes after direct and prefixed route responses are validated.</returns>
        [Fact]
        public async Task ExtractionEndpoints_WhenCalledThroughResolvedRoutes_ShouldNotRequireApiPrefix()
        {
            // Route hardening protects the public contract from accidental host-level prefix drift while still using the in-memory test server.
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage directHistoryResponse = await client.GetAsync("/extractions");
            HttpResponseMessage prefixedHistoryResponse = await client.GetAsync("/api/extractions");
            HttpResponseMessage directStatusResponse = await client.GetAsync("/extractions/not-a-run-id");
            HttpResponseMessage prefixedStatusResponse = await client.GetAsync("/api/extractions/not-a-run-id");

            Assert.Equal(HttpStatusCode.OK, directHistoryResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, prefixedHistoryResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, directStatusResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, prefixedStatusResponse.StatusCode);
        }

        /// <summary>
        /// Verifies POST /extractions returns validation details and does not create a run when the request is invalid.
        /// </summary>
        /// <returns>A task that completes after the validation response is validated.</returns>
        [Fact]
        public async Task PostExtractions_WhenRequestIsInvalid_ShouldReturnValidationProblemWithoutCreatingRun()
        {
            // Missing repository root is enough to prove the API returns client errors before accepting a run.
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();
            StartExtractionApiRequest request = new(
                RepositoryRootDirectory: " ",
                SolutionPaths: ["CustomerSuite.sln"],
                BranchName: null,
                CommitSha: null,
                RequestedBy: null,
                Metadata: null);

            HttpResponseMessage response = await client.PostAsJsonAsync("/extractions", request);
            HttpResponseMessage historyResponse = await client.GetAsync("/extractions/00000000-0000-0000-0000-000000000000");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, historyResponse.StatusCode);
            using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            Assert.Contains("RepositoryRootRequired", body.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        }

        /// <summary>
        /// Verifies validation responses and history summaries expose metadata keys without leaking sensitive metadata values.
        /// </summary>
        /// <returns>A task that completes after validation and accepted-run redaction responses are inspected.</returns>
        [Fact]
        public async Task ExtractionEndpoints_WhenMetadataContainsSensitiveValues_ShouldRedactMetadataValues()
        {
            // Metadata value redaction is asserted through both a rejected request and an accepted request summary.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln");
            string secretValue = "Server=localhost;Password=SuperSecretPassword123!;User Id=neo4j";
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();

            StartExtractionApiRequest invalidRequest = new(
                RepositoryRootDirectory: " ",
                SolutionPaths: ["CustomerSuite.sln"],
                BranchName: null,
                CommitSha: null,
                RequestedBy: null,
                Metadata: new Dictionary<string, string>
                {
                    ["connectionString"] = secretValue
                });
            StartExtractionApiRequest validRequest = new(
                repositoryRoot,
                ["CustomerSuite.sln"],
                BranchName: null,
                CommitSha: null,
                RequestedBy: null,
                Metadata: new Dictionary<string, string>
                {
                    ["connectionString"] = secretValue
                });

            HttpResponseMessage invalidResponse = await client.PostAsJsonAsync("/extractions", invalidRequest);
            HttpResponseMessage validResponse = await client.PostAsJsonAsync("/extractions", validRequest);

            string invalidBody = await invalidResponse.Content.ReadAsStringAsync();
            string validBody = await validResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, validResponse.StatusCode);
            Assert.DoesNotContain(secretValue, invalidBody, StringComparison.Ordinal);
            Assert.DoesNotContain(secretValue, validBody, StringComparison.Ordinal);
            Assert.Contains("connectionString", validBody, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies GET /extractions/{runId} returns status for a run created by the start endpoint.
        /// </summary>
        /// <returns>A task that completes after the status response is validated.</returns>
        [Fact]
        public async Task GetExtractionStatus_WhenRunExists_ShouldReturnCurrentRunStatus()
        {
            // Status retrieval uses the run identifier returned by the start endpoint to mimic a polling API consumer.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln");
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();
            HttpResponseMessage startResponse = await client.PostAsJsonAsync(
                "/extractions",
                new StartExtractionApiRequest(repositoryRoot, ["CustomerSuite.sln"], null, null, null, null));
            using JsonDocument startBody = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
            string runId = startBody.RootElement.GetProperty("runId").GetString()!;

            HttpResponseMessage statusResponse = await client.GetAsync($"/extractions/{runId}");

            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
            using JsonDocument statusBody = await JsonDocument.ParseAsync(await statusResponse.Content.ReadAsStreamAsync());
            Assert.Equal(runId, statusBody.RootElement.GetProperty("runId").GetString());
            Assert.Contains(statusBody.RootElement.GetProperty("status").GetString(), new[] { "Queued", "Running", "Completed" });
        }

        /// <summary>
        /// Verifies GET /extractions/{runId} exposes terminal completion details, progress fields, warnings, errors, and snapshot identity.
        /// </summary>
        /// <returns>A task that completes after a completed asynchronous run status response is validated.</returns>
        [Fact]
        public async Task GetExtractionStatus_WhenRunCompletes_ShouldReturnProgressDiagnosticsAndSnapshotIdentity()
        {
            // Polling to completion proves the API status contract exposes the final persistence identity and progress details, not just acceptance state.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln");
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();
            HttpResponseMessage startResponse = await client.PostAsJsonAsync(
                "/extractions",
                new StartExtractionApiRequest(repositoryRoot, ["CustomerSuite.sln"], null, null, null, null));
            using JsonDocument startBody = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
            string runId = startBody.RootElement.GetProperty("runId").GetString()!;

            JsonDocument statusBody = await PollForTerminalStatusAsync(client, runId);

            using (statusBody)
            {
                Assert.Equal(runId, statusBody.RootElement.GetProperty("runId").GetString());
                Assert.Equal("Completed", statusBody.RootElement.GetProperty("status").GetString());
                Assert.True(statusBody.RootElement.TryGetProperty("completedUtc", out JsonElement completedUtc));
                Assert.NotEqual(JsonValueKind.Null, completedUtc.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(statusBody.RootElement.GetProperty("snapshotIdentity").GetString()));

                JsonElement progress = statusBody.RootElement.GetProperty("progress");
                Assert.Equal("Completed", progress.GetProperty("stage").GetString());
                Assert.Equal("Extraction snapshot persisted successfully.", progress.GetProperty("message").GetString());
                Assert.Equal(100, progress.GetProperty("percentage").GetInt32());
                Assert.NotEqual(default, progress.GetProperty("lastUpdatedUtc").GetDateTimeOffset());

                Assert.Empty(statusBody.RootElement.GetProperty("warnings").EnumerateArray());
                Assert.Empty(statusBody.RootElement.GetProperty("errors").EnumerateArray());
            }
        }

        /// <summary>
        /// Verifies GET /extractions/{runId} returns not found for an unknown run identifier.
        /// </summary>
        /// <returns>A task that completes after the not-found response is validated.</returns>
        [Fact]
        public async Task GetExtractionStatus_WhenRunIsMissing_ShouldReturnNotFound()
        {
            // Unknown run identifiers must not throw or leak internals; they translate to a controlled 404 response.
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.GetAsync("/extractions/00000000-0000-0000-0000-000000000000");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        /// <summary>
        /// Verifies GET /extractions returns recent extraction runs with deterministic summary fields.
        /// </summary>
        /// <returns>A task that completes after the history response is validated.</returns>
        [Fact]
        public async Task GetExtractions_WhenRunsExist_ShouldReturnRecentRunSummaries()
        {
            // The history endpoint gives API consumers a polling-friendly list without requiring each run id up front.
            string firstRepositoryRoot = CreateRepositoryRoot();
            string secondRepositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(firstRepositoryRoot, "FirstSuite.sln");
            CreateSolutionFile(secondRepositoryRoot, "SecondSuite.sln");
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();
            await client.PostAsJsonAsync("/extractions", new StartExtractionApiRequest(firstRepositoryRoot, ["FirstSuite.sln"], null, null, null, null));
            await client.PostAsJsonAsync("/extractions", new StartExtractionApiRequest(secondRepositoryRoot, ["SecondSuite.sln"], null, null, null, null));

            HttpResponseMessage response = await client.GetAsync("/extractions");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            JsonElement runs = body.RootElement.GetProperty("runs");
            Assert.Equal(2, runs.GetArrayLength());
            Assert.Contains(runs[0].GetProperty("status").GetString(), new[] { "Queued", "Running", "Completed" });
            Assert.Equal(1, runs[0].GetProperty("solutionCount").GetInt32());
            Assert.True(runs[0].GetProperty("warningCount").GetInt32() >= 0);
            Assert.Equal(0, runs[0].GetProperty("errorCount").GetInt32());
            Assert.False(runs[0].GetRawText().Contains("System.", StringComparison.Ordinal));
        }

        /// <summary>
        /// Verifies GET /extractions honors the optional limit query parameter when returning history.
        /// </summary>
        /// <returns>A task that completes after the limited history response is validated.</returns>
        [Fact]
        public async Task GetExtractions_WhenLimitIsProvided_ShouldLimitRecentRunSummaries()
        {
            // Limit support keeps the first history endpoint lightweight while avoiding a full paging contract in this slice.
            string firstRepositoryRoot = CreateRepositoryRoot();
            string secondRepositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(firstRepositoryRoot, "FirstSuite.sln");
            CreateSolutionFile(secondRepositoryRoot, "SecondSuite.sln");
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();
            await client.PostAsJsonAsync("/extractions", new StartExtractionApiRequest(firstRepositoryRoot, ["FirstSuite.sln"], null, null, null, null));
            await client.PostAsJsonAsync("/extractions", new StartExtractionApiRequest(secondRepositoryRoot, ["SecondSuite.sln"], null, null, null, null));

            HttpResponseMessage response = await client.GetAsync("/extractions?limit=1");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            Assert.Single(body.RootElement.GetProperty("runs").EnumerateArray());
        }

        /// <summary>
        /// Verifies accepted-run persistence failures are exposed as controlled diagnostics without stack traces or secret values.
        /// </summary>
        /// <returns>A task that completes after a failed asynchronous run is visible through the status endpoint.</returns>
        [Fact]
        public async Task GetExtractionStatus_WhenAcceptedRunFails_ShouldReturnRedactedFailureDetails()
        {
            // A deterministic failing persistence writer proves runtime failure redaction after the request has already been accepted.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln");
            string secretValue = "Password=RuntimeSecret123!";
            await using WebApplication app = await CreateApplicationAsync(services => services.AddSingleton<IArchitectureSnapshotWriter>(new FailingSnapshotWriter(secretValue)));
            using HttpClient client = app.GetTestClient();
            HttpResponseMessage startResponse = await client.PostAsJsonAsync(
                "/extractions",
                new StartExtractionApiRequest(repositoryRoot, ["CustomerSuite.sln"], null, null, null, null));
            using JsonDocument startBody = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
            string runId = startBody.RootElement.GetProperty("runId").GetString()!;

            JsonDocument statusBody = await PollForTerminalStatusAsync(client, runId);

            using (statusBody)
            {
                string rawStatus = statusBody.RootElement.GetRawText();
                Assert.Equal("Failed", statusBody.RootElement.GetProperty("status").GetString());
                JsonElement error = Assert.Single(statusBody.RootElement.GetProperty("errors").EnumerateArray());
                Assert.Equal("PersistenceUnavailable", error.GetProperty("code").GetString());
                Assert.Equal("SnapshotPersistence", error.GetProperty("stage").GetString());
                Assert.Equal("Diagnostic details were redacted. Review server logs for details.", error.GetProperty("message").GetString());
                Assert.DoesNotContain(secretValue, rawStatus, StringComparison.Ordinal);
                Assert.DoesNotContain("System.", rawStatus, StringComparison.Ordinal);
                Assert.DoesNotContain(" at ", rawStatus, StringComparison.Ordinal);
                Assert.DoesNotContain("StackTrace", rawStatus, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Creates a test web application with extraction services and endpoints mapped.
        /// </summary>
        /// <returns>A started in-memory web application.</returns>
        private static async Task<WebApplication> CreateApplicationAsync(Action<IServiceCollection>? configureServices = null)
        {
            // The module-level host keeps endpoint tests focused on extraction routes rather than unrelated host composition.
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddArchonExtractionApi();
            configureServices?.Invoke(builder.Services);

            WebApplication app = builder.Build();
            app.MapArchonExtractionApi();
            await app.StartAsync();
            return app;
        }

        /// <summary>
        /// Polls the status endpoint until the asynchronous in-process scheduler records a terminal state.
        /// </summary>
        /// <param name="client">The HTTP client connected to the in-memory test server.</param>
        /// <param name="runId">The run identifier returned by the start endpoint.</param>
        /// <returns>The final status response body as a JSON document owned by the caller.</returns>
        private static async Task<JsonDocument> PollForTerminalStatusAsync(HttpClient client, string runId)
        {
            // The in-process scheduler runs on a background task, so polling avoids race-prone fixed sleeps while keeping the test bounded.
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < deadline)
            {
                HttpResponseMessage statusResponse = await client.GetAsync($"/extractions/{runId}");
                Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
                JsonDocument body = await JsonDocument.ParseAsync(await statusResponse.Content.ReadAsStreamAsync());
                string? status = body.RootElement.GetProperty("status").GetString();
                if (string.Equals(status, "Completed", StringComparison.Ordinal) || string.Equals(status, "Failed", StringComparison.Ordinal))
                {
                    return body;
                }

                body.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(25));
            }

            throw new TimeoutException("The extraction run did not reach a terminal status before the test timeout.");
        }

        /// <summary>
        /// Creates a temporary repository root directory for a test request.
        /// </summary>
        /// <returns>The absolute path to the created repository root.</returns>
        private string CreateRepositoryRoot()
        {
            // A unique directory per test prevents path normalization assertions from observing stale files.
            string path = Path.Combine(Path.GetTempPath(), "archon-wp004-api-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            _temporaryDirectories.Add(path);
            return path;
        }

        /// <summary>
        /// Creates an empty placeholder solution file under a repository root.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that should contain the solution file.</param>
        /// <param name="pathParts">The nested path parts ending with the solution file name.</param>
        /// <returns>The absolute path to the created solution file.</returns>
        private static string CreateSolutionFile(string repositoryRoot, params string[] pathParts)
        {
            // The WP005 project extraction stage now reads submitted solution headers, so endpoint fixtures use valid minimal solution content.
            string solutionPath = Path.Combine([repositoryRoot, .. pathParts]);
            Directory.CreateDirectory(Path.GetDirectoryName(solutionPath)!);
            File.WriteAllText(
                solutionPath,
                string.Join(
                    Environment.NewLine,
                    [
                        "Microsoft Visual Studio Solution File, Format Version 12.00",
                        "# Visual Studio Version 17",
                        "Global",
                        "EndGlobal"
                    ]));
            return solutionPath;
        }

        /// <summary>
        /// Creates a minimal Visual Studio solution file containing one supported project declaration.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that should contain the solution file.</param>
        /// <param name="relativeSolutionPath">The repository-relative solution file path to write.</param>
        /// <param name="projectName">The project name declared by the solution file.</param>
        /// <param name="projectPath">The project path declared by the solution file.</param>
        /// <returns>The absolute path to the created solution file.</returns>
        private static string CreateSolutionFile(string repositoryRoot, string relativeSolutionPath, string projectName, string projectPath)
        {
            // Semantic API tests need a real project declaration so the project and semantic stages see the same submitted project context.
            string solutionPath = Path.Combine(repositoryRoot, relativeSolutionPath);
            Directory.CreateDirectory(Path.GetDirectoryName(solutionPath)!);
            File.WriteAllText(
                solutionPath,
                string.Join(
                    Environment.NewLine,
                    [
                        "Microsoft Visual Studio Solution File, Format Version 12.00",
                        "# Visual Studio Version 17",
                        $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{projectName}\", \"{projectPath}\", \"{{33333333-3333-3333-3333-333333333333}}\"",
                        "EndProject",
                        "Global",
                        "EndGlobal"
                    ]));
            return solutionPath;
        }

        /// <summary>
        /// Creates a representative LINQ to SQL DBML model file under a repository root.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that should contain the DBML file.</param>
        /// <param name="pathParts">The nested path parts ending with the DBML file name.</param>
        /// <returns>The absolute path to the created DBML file.</returns>
        private static string CreateDbmlModelFile(string repositoryRoot, params string[] pathParts)
        {
            // The API integration fixture contains only static model metadata so extraction cannot accidentally depend on a live database.
            string dbmlPath = Path.Combine([repositoryRoot, .. pathParts]);
            Directory.CreateDirectory(Path.GetDirectoryName(dbmlPath)!);
            File.WriteAllText(
                dbmlPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Database Name="Northwind" Class="NorthwindDataContext" xmlns="http://schemas.microsoft.com/linqtosql/dbml/2007">
                  <Connection Mode="AppSettings" SettingsPropertyName="NorthwindConnectionString" Provider="System.Data.SqlClient" />
                  <Table Name="dbo.Customers" Member="Customers">
                    <Type Name="Customer">
                      <Column Name="CustomerID" Member="CustomerID" Type="System.String" DbType="NChar(5) NOT NULL" IsPrimaryKey="true" CanBeNull="false" />
                    </Type>
                  </Table>
                  <Function Name="dbo.GetCustomerOrders" Method="GetCustomerOrders" />
                </Database>
                """);
            return dbmlPath;
        }

        /// <summary>
        /// Creates a C# source fixture that combines WP007 DI, WP008 runtime, EF Core context, and ADO.NET raw SQL usage for WP009 final integration tests.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that contains the source file.</param>
        /// <param name="relativeSourcePath">The repository-relative source path to write.</param>
        private static void CreateWp009IntegratedDataAccessSourceFile(string repositoryRoot, string relativeSourcePath)
        {
            // Local framework stubs keep the fixture self-contained while still giving Roslyn enough symbol shape for all participating extractors.
            string sourcePath = Path.Combine(repositoryRoot, relativeSourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(
                sourcePath,
                string.Join(
                    Environment.NewLine,
                    [
                        "namespace Microsoft.Extensions.Hosting { public interface IHostedService { System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken); System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken); } public abstract class BackgroundService : IHostedService { public virtual System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask; public virtual System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask; protected abstract System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken); } }",
                        "namespace Microsoft.Extensions.DependencyInjection { public interface IServiceCollection { } public static class ServiceCollectionServiceExtensions { public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services) => services; public static IServiceCollection AddSingleton<TService, TImplementation>(this IServiceCollection services) => services; public static IServiceCollection AddHostedService<THostedService>(this IServiceCollection services) => services; } public static class EntityFrameworkServiceCollectionExtensions { public static IServiceCollection AddDbContext<TContext>(this IServiceCollection services, System.Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> optionsAction) => services; } }",
                        "namespace Microsoft.EntityFrameworkCore { public class DbContext { public DbContext() { } public DbContext(DbContextOptions options) { } public DbSet<TEntity> Set<TEntity>() where TEntity : class => new(); } public class DbContextOptions { } public class DbContextOptions<TContext> : DbContextOptions { } public class DbContextOptionsBuilder { public DbContextOptionsBuilder UseSqlServer(string connectionString) => this; } public class DbContextOptionsBuilder<TContext> : DbContextOptionsBuilder { } public class DbSet<TEntity> where TEntity : class { public System.Collections.Generic.IEnumerable<TEntity> ToList() => new TEntity[0]; public void Add(TEntity entity) { } } }",
                        "namespace System.Configuration { public static class ConfigurationManager { public static ConnectionStringSettingsCollection ConnectionStrings { get; } = new(); } public sealed class ConnectionStringSettingsCollection { public ConnectionStringSettings? this[string name] => null; } public sealed class ConnectionStringSettings { public string ConnectionString => \"name=MainDb\"; } }",
                        "namespace System.Data.SqlClient { public sealed class SqlConnection { public SqlConnection(string connectionString) { } } public sealed class SqlCommand { public SqlCommand(string commandText, SqlConnection connection) { } public object? ExecuteScalar() => null; } }",
                        "namespace Customer.Api",
                        "{",
                        "    using Microsoft.EntityFrameworkCore;",
                        "    using Microsoft.Extensions.DependencyInjection;",
                        "    using Microsoft.Extensions.Hosting;",
                        "    using System.Configuration;",
                        "    using System.Data.SqlClient;",
                        "    using System.Threading;",
                        "    using System.Threading.Tasks;",
                        "    public sealed class Customer",
                        "    {",
                        "        public int Id { get; set; }",
                        "        public string? Name { get; set; }",
                        "    }",
                        "    public sealed class CustomerDbContext : DbContext",
                        "    {",
                        "        public CustomerDbContext(DbContextOptions<CustomerDbContext> options) : base(options) { }",
                        "        public DbSet<Customer> Customers { get; set; } = new();",
                        "    }",
                        "    public sealed class CustomerRepository",
                        "    {",
                        "        private readonly CustomerDbContext _dbContext;",
                        "        public CustomerRepository(CustomerDbContext dbContext) { _dbContext = dbContext; }",
                        "        public void LoadCustomers()",
                        "        {",
                        "            _ = _dbContext.Customers.ToList();",
                        "            using var connection = new SqlConnection(\"name=MainDb\");",
                        "            using var command = new SqlCommand(\"SELECT Id, Name FROM dbo.Customers WHERE Token = 'SuperSecretPassword123!'\", connection);",
                        "            _ = command.ExecuteScalar();",
                        "        }",
                        "    }",
                        "    public sealed class CustomerWorker : BackgroundService",
                        "    {",
                        "        private readonly CustomerRepository _repository;",
                        "        public CustomerWorker(CustomerRepository repository) { _repository = repository; }",
                        "        protected override Task ExecuteAsync(CancellationToken stoppingToken)",
                        "        {",
                        "            _repository.LoadCustomers();",
                        "            return Task.CompletedTask;",
                        "        }",
                        "    }",
                        "    public static class Composition",
                        "    {",
                        "        public static void Configure(IServiceCollection services)",
                        "        {",
                        "            services.AddDbContext<CustomerDbContext>(options => options.UseSqlServer(\"name=MainDb\"));",
                        "            services.AddScoped<CustomerRepository, CustomerRepository>();",
                        "            services.AddHostedService<CustomerWorker>();",
                        "            _ = ConfigurationManager.ConnectionStrings[\"MainDb\"];",
                        "        }",
                        "    }",
                        "}"
                    ]));
        }

        /// <summary>
        /// Creates configuration artifacts containing a redacted connection string key used by the integrated WP009 API fixture.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that should contain configuration files.</param>
        private static void CreateWp009ConnectionConfigurationFiles(string repositoryRoot)
        {
            // Legacy configuration is used because its stable keys explicitly distinguish connection-string entries and values are already redacted by WP007.
            File.WriteAllText(
                Path.Combine(repositoryRoot, "app.config"),
                string.Join(
                    Environment.NewLine,
                    [
                        "<configuration>",
                        "  <connectionStrings><add name=\"MainDb\" connectionString=\"Server=localhost;Database=Customers;User Id=sa;Password=SuperSecretPassword123!\" /></connectionStrings>",
                        "</configuration>"
                    ]));
        }

        /// <summary>
        /// Creates a minimal C# SDK-style project file that includes one compile item.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that contains the project file.</param>
        /// <param name="relativeProjectPath">The repository-relative project path to write.</param>
        /// <param name="sourceInclude">The source file include path written into the project file.</param>
        private static void CreateProjectFile(string repositoryRoot, string relativeProjectPath, string sourceInclude)
        {
            // The semantic stage reads project files directly in tests, so the fixture declares the exact source file it should compile.
            string projectPath = Path.Combine(repositoryRoot, relativeProjectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(
                projectPath,
                string.Join(
                    Environment.NewLine,
                    [
                        "<Project Sdk=\"Microsoft.NET.Sdk\">",
                        "  <PropertyGroup>",
                        "    <TargetFramework>net10.0</TargetFramework>",
                        "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
                        "  </PropertyGroup>",
                        "  <ItemGroup>",
                        $"    <Compile Include=\"{sourceInclude}\" />",
                        "  </ItemGroup>",
                        "</Project>"
                    ]));
        }

        /// <summary>
        /// Creates a representative C# source file containing WP007 DI, legacy container, options, and ConfigurationManager usage.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that contains the source file.</param>
        /// <param name="relativeSourcePath">The repository-relative source path to write.</param>
        private static void CreateWp007SourceFile(string repositoryRoot, string relativeSourcePath)
        {
            // Local stubs make the API integration test self-contained while still forcing Roslyn to bind realistic API owners and method shapes.
            string sourcePath = Path.Combine(repositoryRoot, relativeSourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(
                sourcePath,
                string.Join(
                    Environment.NewLine,
                    [
                        "using System;",
                        "namespace Microsoft.Extensions.DependencyInjection { public interface IServiceCollection { } public static class ServiceCollectionServiceExtensions { public static IServiceCollection AddSingleton<TService, TImplementation>(this IServiceCollection services) => services; public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services) => services; } }",
                        "namespace Microsoft.Extensions.Configuration { public interface IConfiguration { string? this[string key] { get; set; } IConfigurationSection GetSection(string key); } public interface IConfigurationSection : IConfiguration { } public static class ConfigurationBinder { public static T? Get<T>(this IConfiguration configuration) => default; public static void Bind(this IConfiguration configuration, object instance) { } } }",
                        "namespace Microsoft.Extensions.Options { public interface IOptions<out TOptions> { TOptions Value { get; } } }",
                        "namespace Autofac { public sealed class ContainerBuilder { public RegistrationBuilder<TImplementation> RegisterType<TImplementation>() => new RegistrationBuilder<TImplementation>(); public RegistrationBuilder<object> RegisterAssemblyTypes(params object[] assemblies) => new RegistrationBuilder<object>(); } public sealed class RegistrationBuilder<TImplementation> { public RegistrationBuilder<TImplementation> As<TService>() => this; } }",
                        "namespace System.Configuration { public static class ConfigurationManager { public static NameValueCollection AppSettings { get; } = new(); public static ConnectionStringSettingsCollection ConnectionStrings { get; } = new(); } public sealed class NameValueCollection { public string? this[string key] => null; } public sealed class ConnectionStringSettingsCollection { public ConnectionStringSettings? this[string name] => null; } public sealed class ConnectionStringSettings { public string? ConnectionString { get; } } }",
                        "namespace Customer.Api",
                        "{",
                        "    using Autofac;",
                        "    using Microsoft.Extensions.Configuration;",
                        "    using Microsoft.Extensions.DependencyInjection;",
                        "    using Microsoft.Extensions.Options;",
                        "    using System.Configuration;",
                        "    public interface IRepository { }",
                        "    public sealed class Repository : IRepository { }",
                        "    public interface IOrderService { }",
                        "    public sealed class OrderService : IOrderService { public OrderService(IRepository repository) { } }",
                        "    public sealed class OrderOptions { public string? BaseUrl { get; set; } }",
                        "    public static class Composition",
                        "    {",
                        "        public static void Configure(IServiceCollection services, IConfiguration configuration)",
                        "        {",
                        "            services.AddRepositoryModule();",
                        "            services.AddSingleton<IOrderService, OrderService>();",
                        "            _ = configuration[\"Services:Orders:BaseUrl\"];",
                        "            _ = configuration.GetSection(\"Services:Orders\").Get<OrderOptions>();",
                        "            _ = ConfigurationManager.AppSettings[\"LegacyFeature\"];",
                        "            _ = ConfigurationManager.ConnectionStrings[\"MainDb\"];",
                        "            var builder = new ContainerBuilder();",
                        "            builder.RegisterType<OrderService>().As<IOrderService>();",
                        "            builder.RegisterAssemblyTypes(typeof(Composition));",
                        "        }",
                        "        public static IServiceCollection AddRepositoryModule(this IServiceCollection services)",
                        "        {",
                        "            services.AddScoped<IRepository, Repository>();",
                        "            return services;",
                        "        }",
                        "    }",
                        "    public sealed class OptionsConsumer { public OptionsConsumer(IOptions<OrderOptions> options) { } }",
                        "}"
                    ]));
        }

        /// <summary>
        /// Creates modern and legacy configuration artifacts used by the WP007 API integration fixture.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that should contain configuration files.</param>
        private static void CreateWp007ConfigurationFiles(string repositoryRoot)
        {
            // Both artifact families are present so the composed configuration extractor must merge modern and legacy facts into one snapshot.
            File.WriteAllText(
                Path.Combine(repositoryRoot, "appsettings.json"),
                "{ \"Services\": { \"Orders\": { \"BaseUrl\": \"https://orders.example.invalid\", \"ApiKey\": \"SuperSecretPassword123!\" } } }");
            File.WriteAllText(
                Path.Combine(repositoryRoot, "app.config"),
                string.Join(
                    Environment.NewLine,
                    [
                        "<configuration>",
                        "  <appSettings><add key=\"LegacyFeature\" value=\"enabled\" /></appSettings>",
                        "  <connectionStrings><add name=\"MainDb\" connectionString=\"Server=localhost;Password=SuperSecretPassword123!\" /></connectionStrings>",
                        "</configuration>"
                    ]));
        }

        /// <summary>
        /// Creates a small C# source file containing declarations that should flow through semantic extraction.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that contains the source file.</param>
        /// <param name="relativeSourcePath">The repository-relative source path to write.</param>
        private static void CreateCSharpSourceFile(string repositoryRoot, string relativeSourcePath)
        {
            // The source intentionally includes a namespace, type, constructor, method, property, and field so semantic graph projection is visible.
            string sourcePath = Path.Combine(repositoryRoot, relativeSourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(
                sourcePath,
                string.Join(
                    Environment.NewLine,
                    [
                        "namespace Customer.Api;",
                        "",
                        "public sealed class CustomerService",
                        "{",
                        "    private readonly string _name;",
                        "",
                        "    public CustomerService()",
                        "    {",
                        "        _name = \"Ada\";",
                        "    }",
                        "",
                        "    public string Name => _name;",
                        "",
                        "    public string GetName()",
                        "    {",
                        "        return Name;",
                        "    }",
                        "}"
                    ]));
        }

        /// <summary>
        /// Records the snapshot supplied by completed API orchestration while returning a deterministic persistence success.
        /// </summary>
        private sealed class RecordingSnapshotWriter : IArchitectureSnapshotWriter
        {
            /// <summary>
            /// Stores the stable snapshot identity returned to orchestration after the write.
            /// </summary>
            private readonly string _snapshotStableKey;

            /// <summary>
            /// Initializes a new instance of the <see cref="RecordingSnapshotWriter" /> class.
            /// </summary>
            /// <param name="snapshotStableKey">The stable snapshot identity returned by the test writer.</param>
            public RecordingSnapshotWriter(string snapshotStableKey)
            {
                // The writer uses a caller-supplied identity so endpoint assertions can focus on graph content rather than generated ids.
                _snapshotStableKey = snapshotStableKey;
            }

            /// <summary>
            /// Gets the snapshot most recently supplied to the writer.
            /// </summary>
            public ExtractedArchitectureSnapshot? WrittenSnapshot { get; private set; }

            /// <summary>
            /// Records the assembled snapshot and returns successful counts for API orchestration.
            /// </summary>
            /// <param name="snapshot">The assembled snapshot supplied by orchestration.</param>
            /// <param name="cancellationToken">The cancellation token for the simulated write.</param>
            /// <returns>A successful persistence result with section counts from the recorded snapshot.</returns>
            public Task<SnapshotPersistenceResult> WriteSnapshotAsync(ExtractedArchitectureSnapshot snapshot, CancellationToken cancellationToken = default)
            {
                // The test double intentionally records the application contract before persistence adapters can translate it.
                ArgumentNullException.ThrowIfNull(snapshot);
                cancellationToken.ThrowIfCancellationRequested();
                WrittenSnapshot = snapshot;
                SnapshotPersistenceCounts counts = new(
                    snapshot.Repositories.Count,
                    snapshot.Solutions.Count,
                    snapshot.SnapshotHeader is null ? 0 : 1,
                    snapshot.Nodes.Count,
                    snapshot.Evidence.Count,
                    snapshot.Edges.Count,
                    snapshot.Solutions.Count,
                    0,
                    snapshot.Edges.Count * 2,
                    0,
                    snapshot.Rules.Count,
                    snapshot.Findings.Count,
                    0,
                    0,
                    0,
                    snapshot.Metrics.Count,
                    0,
                    0,
                    snapshot.GeneratedSummaries.Count,
                    snapshot.GeneratedSummaries.Count,
                    0);
                return Task.FromResult(SnapshotPersistenceResult.Success(_snapshotStableKey, counts));
            }
        }

        /// <summary>
        /// Provides a deterministic persistence failure for API redaction tests.
        /// </summary>
        private sealed class FailingSnapshotWriter : IArchitectureSnapshotWriter
        {
            /// <summary>
            /// Stores a sensitive value that must never appear in the public API response.
            /// </summary>
            private readonly string _sensitiveValue;

            /// <summary>
            /// Initializes a new instance of the <see cref="FailingSnapshotWriter"/> class.
            /// </summary>
            /// <param name="sensitiveValue">The secret-like value used to prove response redaction.</param>
            public FailingSnapshotWriter(string sensitiveValue)
            {
                // The writer keeps the value only for the test assertion; the returned diagnostic deliberately omits it.
                _sensitiveValue = sensitiveValue;
            }

            /// <summary>
            /// Returns a controlled persistence failure without exposing infrastructure exception details.
            /// </summary>
            /// <param name="snapshot">The assembled snapshot that would normally be persisted.</param>
            /// <param name="cancellationToken">The cancellation token for the simulated persistence operation.</param>
            /// <returns>A failed persistence result with a user-actionable, credential-safe diagnostic.</returns>
            public Task<SnapshotPersistenceResult> WriteSnapshotAsync(ExtractedArchitectureSnapshot snapshot, CancellationToken cancellationToken = default)
            {
                // Touch the sensitive field so the test double models a writer that knows a secret while still returning only safe text.
                Assert.False(string.IsNullOrWhiteSpace(_sensitiveValue));
                SnapshotPersistenceResult result = SnapshotPersistenceResult.Failure(
                    snapshot.SnapshotHeader is null ? null : snapshot.SnapshotHeader.StableKey.ToString(),
                    new PersistenceError(
                        PersistenceStage.SnapshotPersistence,
                        "PersistenceUnavailable",
                        "System.InvalidOperationException: Snapshot persistence failed at Neo4j.Driver with Password=" + _sensitiveValue + " at Infrastructure.Adapter"));
                return Task.FromResult(result);
            }
        }
    }
}
