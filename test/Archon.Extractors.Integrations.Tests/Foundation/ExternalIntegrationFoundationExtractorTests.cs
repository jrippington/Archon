using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Integrations.Foundation;
using Xunit;

namespace Archon.Extractors.Integrations.Tests.Foundation
{
    /// <summary>
    /// Verifies the minimal WP010 integration foundation extractor projects explicit evidence into graph facts safely.
    /// </summary>
    public sealed class ExternalIntegrationFoundationExtractorTests
    {
        /// <summary>
        /// Verifies an empty request runs successfully without producing false-positive integration facts.
        /// </summary>
        [Fact]
        public void Extract_WhenNoEvidenceExists_ShouldReturnEmptySnapshot()
        {
            // The foundation slice must be safe to run before later WP010 detectors contribute integration observations.
            ExternalIntegrationFoundationExtractor extractor = new();
            ExternalIntegrationExtractionRequest request = new(CreateSnapshotStableKey(), "D:/Dev/Archon", []);

            ExternalIntegrationExtractionResult result = extractor.Extract(request, CancellationToken.None);

            Assert.Empty(result.Snapshot.Nodes);
            Assert.Empty(result.Snapshot.Edges);
            Assert.Empty(result.Snapshot.Evidence);
            Assert.Empty(result.Snapshot.Warnings);
            Assert.Empty(result.Snapshot.Errors);
        }

        /// <summary>
        /// Verifies explicit service evidence creates a deterministic external-service node and call relationship.
        /// </summary>
        [Fact]
        public void Extract_WhenExternalServiceObservationExists_ShouldEmitServiceNodeEdgeAndEvidence()
        {
            // The observation models a later HTTP detector contributing evidence without the foundation extractor performing network access.
            ExternalIntegrationFoundationExtractor extractor = new();
            ExternalIntegrationObservation observation = new(
                ExternalIntegrationTargetKind.ExternalService,
                "Billing API",
                "Http",
                "HttpClient",
                "OutboundClient",
                "method://App.Client.SendAsync",
                EdgeKind.CallsExternalService,
                "src/App/Client.cs",
                12,
                14,
                "SendAsync",
                "App.Client",
                "await _httpClient.SendAsync(request);",
                "HttpClient.SendAsync",
                UnknownReason: null,
                ConfigurationKeyStableKey: StableKeyGenerator.ForConfigurationKey("Integrations:Billing:BaseUrl"));
            ExternalIntegrationExtractionRequest request = new(CreateSnapshotStableKey(), "D:/Dev/Archon", [observation]);

            ExternalIntegrationExtractionResult result = extractor.Extract(request, CancellationToken.None);

            Assert.Single(result.Snapshot.Nodes);
            Assert.Single(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.CallsExternalService);
            Assert.Single(result.Snapshot.Evidence);
            Assert.Equal("Billing API", result.Snapshot.Nodes.Single().DisplayName);
            Assert.Equal("externalservice://Billing API", result.Snapshot.Nodes.Single().StableKey.Value);
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesConfig);
            Assert.Empty(result.Snapshot.Errors);
        }

        /// <summary>
        /// Verifies unknown integration evidence remains explicit and warning-backed rather than inventing a service identity.
        /// </summary>
        [Fact]
        public void Extract_WhenUnknownObservationExists_ShouldEmitUnknownFactAndWarning()
        {
            // Unknown observations are still useful when evidence proves an integration but does not reveal a deterministic target name.
            ExternalIntegrationFoundationExtractor extractor = new();
            ExternalIntegrationObservation observation = new(
                ExternalIntegrationTargetKind.ExternalService,
                null,
                "Http",
                "HttpClient",
                "OutboundClient",
                "method://App.Client.SendAsync",
                EdgeKind.CallsExternalService,
                "D:/Dev/Archon/src/App/Client.cs",
                21,
                21,
                "SendAsync",
                "App.Client",
                "await _httpClient.GetAsync(dynamicUrl);",
                "DynamicUrl",
                "HTTP endpoint is computed at runtime.",
                ConfigurationKeyStableKey: null);
            ExternalIntegrationExtractionRequest request = new(CreateSnapshotStableKey(), "D:/Dev/Archon", [observation]);

            ExternalIntegrationExtractionResult result = extractor.Extract(request, CancellationToken.None);

            Assert.Single(result.Snapshot.Nodes);
            Assert.True(result.Snapshot.Nodes.Single().UnknownState.HasUnknownData);
            Assert.StartsWith("externalservice://unknown/src/App/Client.cs/21/", result.Snapshot.Nodes.Single().StableKey.Value, StringComparison.Ordinal);
            Assert.Contains("HTTP endpoint is computed at runtime.", result.Snapshot.Warnings.Single());
        }

        /// <summary>
        /// Verifies cancellation is observed before graph fact projection begins.
        /// </summary>
        [Fact]
        public void Extract_WhenCanceled_ShouldThrowOperationCanceledException()
        {
            // Cancellation protects API-triggered extraction from doing unnecessary static analysis after the run is stopped.
            ExternalIntegrationFoundationExtractor extractor = new();
            ExternalIntegrationObservation observation = new(
                ExternalIntegrationTargetKind.Queue,
                "orders",
                "Messaging",
                "AzureServiceBus",
                "Consumer",
                "method://App.Worker.HandleAsync",
                EdgeKind.Handles,
                "src/App/Worker.cs",
                8,
                8,
                "Subscribe",
                "App.Worker",
                "processor.ProcessMessageAsync += HandleAsync;",
                "AzureServiceBusProcessor",
                UnknownReason: null,
                ConfigurationKeyStableKey: null);
            ExternalIntegrationExtractionRequest request = new(CreateSnapshotStableKey(), "D:/Dev/Archon", [observation]);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => extractor.Extract(request, cancellation.Token));
        }

        /// <summary>
        /// Creates the deterministic snapshot stable key shared by extractor tests.
        /// </summary>
        /// <returns>A snapshot stable key for the test extraction run.</returns>
        private static StableKey CreateSnapshotStableKey()
        {
            // Tests use a fixed snapshot key so expected node, edge, and evidence identities remain deterministic.
            return new StableKey("snapshot://archon/test-run");
        }
    }
}
