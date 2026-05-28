using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.Integrations.InternalServices;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using Xunit;

namespace Archon.Extractors.Tests.Integrations.InternalServices
{
    /// <summary>
    /// Verifies that Work Item 6 correlates internal service calls only when deterministic client and endpoint evidence agree.
    /// </summary>
    public sealed class InternalServiceIntegrationExtractorTests
    {
        /// <summary>
        /// Confirms a client call is linked to endpoint, controller, method, project, and external-service metadata when route and configuration evidence match prior endpoint facts.
        /// </summary>
        [Fact]
        public void Extract_WhenInternalRouteAndEndpointFactsMatch_ShouldEmitCorrelatedInternalServiceCall()
        {
            // The fixture supplies both caller source and prior endpoint facts so the extractor can prove ownership without relying on service names alone.
            InternalServiceIntegrationExtractionResult result = ExtractCSharpFixture(CreateEndpointFacts());

            ArchitectureNode node = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.ExternalService && node.DisplayName == "Orders.Api");
            ArchitectureEdge edge = Assert.Single(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.CallsExternalService && edge.TargetNodeStableKey == node.StableKey && ContainsMetadata(edge, "\"isInternalService\":\"true\""));

            Assert.Empty(result.Errors);
            Assert.Contains("\"endpointStableKey\":\"endpoint://GET:/api/orders/{id}\"", node.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("\"controllerStableKey\":\"controller://Orders.Api.Controllers.OrdersController\"", node.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("\"methodStableKey\":\"method://Orders.Api.Controllers.OrdersController.GetById\"", edge.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Sample.Client/OrdersClient.cs" && evidence.SnippetPreview?.Contains("GetAsync", StringComparison.Ordinal) == true);
        }

        /// <summary>
        /// Confirms unresolved ownership and computed routes are represented as explicit unknowns instead of forced internal-service matches.
        /// </summary>
        [Fact]
        public void Extract_WhenOwnershipOrRouteIsUnresolved_ShouldEmitUnknownsAndWarnings()
        {
            // Negative cases guard the most important Work Item 6 rule: naming hints are not enough to claim internal ownership.
            InternalServiceIntegrationExtractionResult result = ExtractCSharpFixture([]);

            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.ExternalService && node.UnknownState.HasUnknownData);
            Assert.Contains(result.Warnings, warning => warning.Contains("Internal service ownership could not be resolved", StringComparison.Ordinal));
            Assert.Contains(result.Warnings, warning => warning.Contains("computed at runtime", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Snapshot.Nodes, node => node.DisplayName == "Orders.Api");
        }

        /// <summary>
        /// Confirms cross-slice quality behavior: stable identities deduplicate, confidence and unknown metadata are normalized, redaction runs before output, and cancellation is honored.
        /// </summary>
        [Fact]
        public void Extract_WhenQualityGateScenariosExist_ShouldDeduplicateRedactAndHonorCancellation()
        {
            // The duplicated calls should collapse by stable key, and secret-like setup text should never leak through evidence or diagnostics.
            InternalServiceIntegrationExtractionResult result = ExtractCSharpFixture(CreateEndpointFacts());
            IReadOnlyList<string> nodeKeys = result.Snapshot.Nodes.Select(node => node.StableKey.Value).ToArray();
            IReadOnlyList<string> edgeKeys = result.Snapshot.Edges.Select(edge => edge.StableKey.Value).ToArray();
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.Equal(nodeKeys.Count, nodeKeys.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(edgeKeys.Count, edgeKeys.Distinct(StringComparer.Ordinal).Count());
            Assert.Contains(result.Snapshot.Nodes, node => ContainsMetadata(node, "\"confidenceReason\":\"Internal service target is correlated by deterministic route evidence and prior endpoint facts.\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && ContainsMetadata(node, "Internal service ownership could not be resolved"));
            Assert.DoesNotContain(result.Snapshot.Evidence, evidence => ContainsSensitiveText(evidence.SnippetPreview));
            Assert.DoesNotContain(result.Snapshot.Nodes, node => ContainsSensitiveText(node.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Snapshot.Edges, edge => ContainsSensitiveText(edge.Metadata.ToCanonicalJson()));
            Assert.Throws<OperationCanceledException>(() => ExtractCSharpFixture(CreateEndpointFacts(), cancellation.Token));
        }

        /// <summary>
        /// Confirms Visual Basic documents are handled as an explicit current parity limit rather than being silently misinterpreted by C# syntax logic.
        /// </summary>
        [Fact]
        public void Extract_WhenVisualBasicDocumentIsSupplied_ShouldReportParityLimitWithoutFalsePositiveFacts()
        {
            // VB.NET semantic support exists in the repository, but this Work Item 6 detector currently limits client-call syntax extraction to C# and documents that limit in diagnostics.
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-internal-service-vb-fixture"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.Client", "OrdersClient.vb");
            string source = "Public Class OrdersClient\nEnd Class";
            SyntaxTree syntaxTree = VisualBasicSyntaxTree.ParseText(source, path: documentPath);
            VisualBasicCompilation compilation = VisualBasicCompilation.Create(
                "Sample.Client",
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            SemanticExtractionRequest semanticRequest = new(repositoryRoot, "src/Sample.Client/Sample.Client.vbproj", documentPath, syntaxTree, compilation.GetSemanticModel(syntaxTree));
            InternalServiceIntegrationExtractor extractor = new();

            InternalServiceIntegrationExtractionResult result = extractor.Extract(new InternalServiceIntegrationExtractionRequest(StableKeyGenerator.ForRepository("Sample.Client"), repositoryRoot, [semanticRequest], CreateEndpointFacts()));

            Assert.Empty(result.Snapshot.Nodes);
            Assert.Contains(result.Warnings, warning => warning.Contains("non-C# document", StringComparison.Ordinal));
        }

        /// <summary>
        /// Determines whether a node metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="node">The architecture node whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the node metadata.</param>
        /// <returns><see langword="true" /> when the metadata contains the fragment; otherwise, <see langword="false" />.</returns>
        private static bool ContainsMetadata(ArchitectureNode node, string expectedFragment)
        {
            // Canonical JSON comparisons keep assertions stable regardless of dictionary construction order.
            return node.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether an edge metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="edge">The architecture edge whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the edge metadata.</param>
        /// <returns><see langword="true" /> when the metadata contains the fragment; otherwise, <see langword="false" />.</returns>
        private static bool ContainsMetadata(ArchitectureEdge edge, string expectedFragment)
        {
            // Edge metadata assertions verify route ownership without depending on object reference identity.
            return edge.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a value contains secret-like fixture text that must be redacted.
        /// </summary>
        /// <param name="value">The output value to inspect.</param>
        /// <returns><see langword="true" /> when sensitive fixture text appears; otherwise, <see langword="false" />.</returns>
        private static bool ContainsSensitiveText(string? value)
        {
            // Work Item 6 requires redaction before metadata, evidence, warnings, errors, logs, API responses, or tests can observe secret-like data.
            return value?.Contains("InternalSecretToken", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("Authorization", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Creates deterministic endpoint facts that stand in for prior ASP.NET/runtime extraction output.
        /// </summary>
        /// <returns>The endpoint facts consumed by internal service correlation.</returns>
        private static IReadOnlyList<InternalServiceEndpointFact> CreateEndpointFacts()
        {
            // The endpoint fact includes endpoint, controller, method, project, base URL, and configuration-key ownership evidence required by the plan.
            return
            [
                new InternalServiceEndpointFact(
                    StableKeyGenerator.ForEndpoint("GET", "/api/orders/{id}"),
                    StableKeyGenerator.ForProject("src/Orders.Api/Orders.Api.csproj"),
                    "GET",
                    "/api/orders/{id}",
                    "Orders.Api",
                    StableKeyGenerator.ForController("Orders.Api.Controllers.OrdersController"),
                    StableKeyGenerator.ForMethod("Orders.Api.Controllers.OrdersController.GetById"),
                    "Services:Orders:BaseUrl",
                    "https://orders.internal.test")
            ];
        }

        /// <summary>
        /// Builds the C# source fixture and invokes the production internal service integration extractor.
        /// </summary>
        /// <param name="endpointFacts">The prior endpoint facts supplied to correlation.</param>
        /// <param name="cancellationToken">A token that can cancel extraction for quality-gate validation.</param>
        /// <returns>The extraction result for the C# fixture.</returns>
        private static InternalServiceIntegrationExtractionResult ExtractCSharpFixture(IReadOnlyList<InternalServiceEndpointFact> endpointFacts, CancellationToken cancellationToken = default)
        {
            // The fixture includes local framework stubs so Roslyn can bind HttpClient and configuration patterns without package restore or live services.
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-internal-service-fixture"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.Client", "OrdersClient.cs");
            string source = """
                namespace System.Net.Http
                {
                    using System.Threading.Tasks;

                    public sealed class HttpClient
                    {
                        public Task<string> GetAsync(string requestUri) => Task.FromResult(string.Empty);
                    }
                }

                namespace Microsoft.Extensions.Configuration
                {
                    public interface IConfiguration
                    {
                        string? this[string key] { get; set; }
                    }
                }

                namespace Sample.Client
                {
                    using Microsoft.Extensions.Configuration;
                    using System.Net.Http;
                    using System.Threading.Tasks;

                    public sealed class OrdersClient
                    {
                        private readonly HttpClient _httpClient;
                        private readonly IConfiguration _configuration;

                        public OrdersClient(HttpClient httpClient, IConfiguration configuration)
                        {
                            _httpClient = httpClient;
                            _configuration = configuration;
                            string token = "InternalSecretToken";
                            _ = token;
                        }

                        public async Task LoadAsync(string dynamicPath)
                        {
                            string baseUrl = _configuration["Services:Orders:BaseUrl"]!;
                            _ = baseUrl;
                            await _httpClient.GetAsync("https://orders.internal.test/api/orders/42");
                            await _httpClient.GetAsync("https://orders.internal.test/api/orders/42");
                            await _httpClient.GetAsync("/api/customers/17");
                            await _httpClient.GetAsync(dynamicPath);
                        }
                    }
                }
                """;
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: documentPath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Sample.Client",
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Task).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            SemanticExtractionRequest semanticRequest = new(repositoryRoot, "src/Sample.Client/Sample.Client.csproj", documentPath, syntaxTree, compilation.GetSemanticModel(syntaxTree));
            InternalServiceIntegrationExtractor extractor = new();

            return extractor.Extract(new InternalServiceIntegrationExtractionRequest(StableKeyGenerator.ForRepository("Sample.Client"), repositoryRoot, [semanticRequest], endpointFacts), cancellationToken);
        }
    }
}
