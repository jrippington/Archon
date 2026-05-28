using Archon.Application.Extraction.Contracts;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Extractors.AspNet.Runtime;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Archon.Extractors.Tests.AspNet
{
    /// <summary>
    /// Verifies the worker hosted-service runtime extractor contributes graph-ready facts for generic-host and hosted-service source shapes.
    /// </summary>
    public sealed class WorkerHostedServiceRuntimeExtractorTests
    {
        /// <summary>
        /// Verifies a BackgroundService implementation produces hosted-service, type, execution method, evidence, and worker host setup facts.
        /// </summary>
        [Fact]
        public void Extract_WhenBackgroundServiceExists_ShouldContributeHostedServiceAndExecutionMethodFacts()
        {
            // The fixture intentionally includes generic host setup and a BackgroundService implementation to exercise both project and hosted-service facts.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-worker-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.Worker"));
            try
            {
                string programPath = Path.Combine(repositoryRoot, "src", "Customer.Worker", "Program.cs");
                string workerPath = Path.Combine(repositoryRoot, "src", "Customer.Worker", "Worker.cs");
                File.WriteAllText(programPath, CreateWorkerHostSource());
                File.WriteAllText(workerPath, CreateBackgroundServiceSource());
                IReadOnlyList<SemanticExtractionRequest> semanticRequests = CreateCSharpSemanticRequests(repositoryRoot, "src/Customer.Worker/Customer.Worker.csproj", [programPath, workerPath]);
                WorkerHostedServiceRuntimeExtractor extractor = new();

                WorkerHostedServiceExtractionResult result = extractor.Extract(new WorkerHostedServiceExtractionRequest(new StableKey("snapshot://worker-test"), semanticRequests), CancellationToken.None);

                ArchitectureNode hostedServiceNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.HostedService);
                Assert.Equal("Worker", hostedServiceNode.DisplayName);
                Assert.Equal("Customer.Worker.Worker", hostedServiceNode.QualifiedName);
                Assert.True(hostedServiceNode.UnknownState.HasUnknownData);
                Assert.Contains("\"runtimeKind\":\"BackgroundService\"", hostedServiceNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"detectionMode\":\"BackgroundServiceInheritance\"", hostedServiceNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"registrationCorrelated\":false", hostedServiceNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);

                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Project && node.StableKey.Value == "project://src/Customer.Worker/Customer.Worker.csproj" && node.Metadata.ToCanonicalJson().Contains("\"runtimeKind\":\"WorkerServiceHost\"", StringComparison.Ordinal));
                ArchitectureNode methodNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "ExecuteAsync");
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.DependsOn && edge.SourceNodeStableKey == hostedServiceNode.StableKey && edge.TargetNodeStableKey == methodNode.StableKey);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.SymbolName == "Worker" && evidence.FilePath.Value == "src/Customer.Worker/Worker.cs");
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("no matching AddHostedService registration", StringComparison.Ordinal));
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated test runs isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies an IHostedService implementation with a matching prior DI registration is correlated and remains a known fact.
        /// </summary>
        [Fact]
        public void Extract_WhenHostedServiceHasPriorRegistration_ShouldCorrelateRegistrationEvidence()
        {
            // The prior snapshot mimics dependency-injection AddHostedService output so Work Item 5 can prove cross-slice correlation without invoking the DI extractor.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-worker-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.Worker"));
            try
            {
                string servicePath = Path.Combine(repositoryRoot, "src", "Customer.Worker", "TimedService.cs");
                File.WriteAllText(servicePath, CreateHostedServiceSource());
                IReadOnlyList<SemanticExtractionRequest> semanticRequests = CreateCSharpSemanticRequests(repositoryRoot, "src/Customer.Worker/Customer.Worker.csproj", [servicePath]);
                ExtractedArchitectureSnapshot priorSnapshot = CreatePriorHostedServiceRegistrationSnapshot("Customer.Worker.TimedService");
                WorkerHostedServiceRuntimeExtractor extractor = new();

                WorkerHostedServiceExtractionResult result = extractor.Extract(new WorkerHostedServiceExtractionRequest(new StableKey("snapshot://worker-test"), semanticRequests, priorSnapshot), CancellationToken.None);

                ArchitectureNode hostedServiceNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.HostedService);
                Assert.Equal("TimedService", hostedServiceNode.DisplayName);
                Assert.False(hostedServiceNode.UnknownState.HasUnknownData);
                string metadata = hostedServiceNode.Metadata.ToCanonicalJson();
                Assert.Contains("\"runtimeKind\":\"HostedService\"", metadata, StringComparison.Ordinal);
                Assert.Contains("\"detectionMode\":\"HostedServiceImplementation\"", metadata, StringComparison.Ordinal);
                Assert.Contains("\"registrationCorrelated\":true", metadata, StringComparison.Ordinal);
                Assert.Contains("\"registrationMethod\":\"AddHostedService\"", metadata, StringComparison.Ordinal);
                Assert.Contains("\"registrationLifetime\":\"Singleton\"", metadata, StringComparison.Ordinal);
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "StartAsync");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "StopAsync");
                Assert.Empty(result.Snapshot.Warnings);
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated test runs isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Creates semantic extraction requests for multiple C# source documents in one lightweight project compilation.
        /// </summary>
        /// <param name="repositoryRoot">The absolute repository root that scopes repository-relative evidence paths.</param>
        /// <param name="projectContext">The repository-relative project path used to scope project and stable keys.</param>
        /// <param name="documentPaths">The absolute source document paths to parse.</param>
        /// <returns>Semantic extraction requests with C# syntax trees and semantic models.</returns>
        private static IReadOnlyList<SemanticExtractionRequest> CreateCSharpSemanticRequests(string repositoryRoot, string projectContext, IReadOnlyList<string> documentPaths)
        {
            // The lightweight compilation defines local hosting abstractions so tests do not need target project restore or external package references.
            IReadOnlyList<SyntaxTree> syntaxTrees = documentPaths.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)).ToArray();
            CSharpCompilation compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(projectContext),
                syntaxTrees,
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Task).Assembly.Location), MetadataReference.CreateFromFile(typeof(CancellationToken).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            return syntaxTrees.Select(syntaxTree => new SemanticExtractionRequest(repositoryRoot, projectContext, syntaxTree.FilePath, syntaxTree, compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true))).ToArray();
        }

        /// <summary>
        /// Creates a prior snapshot containing one hosted-service dependency-injection registration edge.
        /// </summary>
        /// <param name="implementationType">The implementation type name recorded by the DI extractor.</param>
        /// <returns>A prior snapshot that can be used for worker hosted-service correlation tests.</returns>
        private static ExtractedArchitectureSnapshot CreatePriorHostedServiceRegistrationSnapshot(string implementationType)
        {
            // The edge shape mirrors REGISTERED_AS_SERVICE metadata fields consumed by worker-hosted-service correlation.
            StableKey snapshotStableKey = new("snapshot://worker-test");
            StableKey sourceStableKey = StableKeyGenerator.ForProject("src/Customer.Worker/Customer.Worker.csproj");
            StableKey targetStableKey = StableKeyGenerator.ForType(implementationType);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["backgroundService"] = false,
                ["hostedService"] = true,
                ["implementationType"] = implementationType,
                ["lifetime"] = "Singleton",
                ["registrationMethod"] = "AddHostedService",
                ["serviceType"] = "IHostedService"
            });
            ArchitectureEdge edge = new(snapshotStableKey, new StableKey("edge://registered-as-service:test"), EdgeKind.RegisteredAsService, sourceStableKey, targetStableKey, isDirect: true, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, new StableKey("evidence://di-registration:test"), metadata, FingerprintGenerator.ForEdge(EdgeKind.RegisteredAsService, sourceStableKey, targetStableKey, isDirect: true, KnowledgeKind.Fact, metadata));
            return new ExtractedArchitectureSnapshot(null, null, null, null, [edge], null, null, null, null, null, null, null);
        }

        /// <summary>
        /// Creates fixture source containing generic host setup for a worker project.
        /// </summary>
        /// <returns>The C# source text for generic host setup.</returns>
        private static string CreateWorkerHostSource()
        {
            // The source shape mirrors a common worker template Program.cs without requiring real hosting package references.
            return string.Join(
                Environment.NewLine,
                "namespace Microsoft.Extensions.Hosting",
                "{",
                "    public static class Host",
                "    {",
                "        public static IHostBuilder CreateDefaultBuilder(string[] args) => null!;",
                "    }",
                "    public interface IHostBuilder",
                "    {",
                "        IHostBuilder ConfigureServices(System.Action<object, object> configureDelegate);",
                "        void Run();",
                "    }",
                "}",
                "namespace Customer.Worker;",
                "public static class Program",
                "{",
                "    public static void Main(string[] args)",
                "    {",
                "        Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)",
                "            .ConfigureServices((context, services) => { })",
                "            .Run();",
                "    }",
                "}");
        }

        /// <summary>
        /// Creates fixture source containing a BackgroundService implementation.
        /// </summary>
        /// <returns>The C# source text for a background-service fixture.</returns>
        private static string CreateBackgroundServiceSource()
        {
            // Local hosting abstractions let Roslyn bind BackgroundService inheritance deterministically in the test compilation.
            return string.Join(
                Environment.NewLine,
                "namespace Microsoft.Extensions.Hosting",
                "{",
                "    public interface IHostedService",
                "    {",
                "        System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken);",
                "        System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken);",
                "    }",
                "    public abstract class BackgroundService : IHostedService",
                "    {",
                "        public virtual System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;",
                "        public virtual System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;",
                "        protected abstract System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken);",
                "    }",
                "}",
                "namespace Customer.Worker;",
                "public sealed class Worker : Microsoft.Extensions.Hosting.BackgroundService",
                "{",
                "    protected override System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken)",
                "    {",
                "        return System.Threading.Tasks.Task.CompletedTask;",
                "    }",
                "}");
        }

        /// <summary>
        /// Creates fixture source containing a direct IHostedService implementation.
        /// </summary>
        /// <returns>The C# source text for a hosted-service fixture.</returns>
        private static string CreateHostedServiceSource()
        {
            // The fixture covers explicit IHostedService implementations that do not derive from BackgroundService.
            return string.Join(
                Environment.NewLine,
                "namespace Microsoft.Extensions.Hosting",
                "{",
                "    public interface IHostedService",
                "    {",
                "        System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken);",
                "        System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken);",
                "    }",
                "}",
                "namespace Customer.Worker;",
                "public sealed class TimedService : Microsoft.Extensions.Hosting.IHostedService",
                "{",
                "    public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken)",
                "    {",
                "        return System.Threading.Tasks.Task.CompletedTask;",
                "    }",
                "    public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken)",
                "    {",
                "        return System.Threading.Tasks.Task.CompletedTask;",
                "    }",
                "}");
        }
    }
}
