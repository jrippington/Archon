using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Integrations.Foundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Archon.Api.Extraction.Tests
{
    /// <summary>
    /// Verifies the external integration external integration stage participates in API-triggered extraction orchestration.
    /// </summary>
    public sealed class ExternalIntegrationExtractionStageTests
    {
        /// <summary>
        /// Verifies API module registration includes the external integration stage in the extraction pipeline.
        /// </summary>
        [Fact]
        public void AddArchonExtractionApi_ShouldRegisterExternalIntegrationIntegrationStage()
        {
            // The API module is the established composition seam for extraction stages.
            ServiceProvider provider = new ServiceCollection()
                .AddArchonExtractionApi()
                .BuildServiceProvider();

            IExtractionStage[] stages = provider.GetServices<IExtractionStage>().ToArray();

            Assert.Contains(stages, stage => stage is ExternalIntegrationExtractionStage);
            Assert.Contains(stages, stage => string.Equals(stage.StageId, "wp010-external-integrations", StringComparison.Ordinal));
        }

        /// <summary>
        /// Verifies the stage can run as a no-op through the shared snapshot accumulator.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenNoIntegrationEvidenceExists_ShouldCompleteWithoutFacts()
        {
            // The foundation stage must be runnable before later detector work items add source observations.
            ExternalIntegrationExtractionStage stage = new(NullLogger<ExternalIntegrationExtractionStage>.Instance);
            ExtractionStageContext context = CreateContext();

            ExtractionStageResult result = await stage.ExecuteAsync(context, CancellationToken.None);
            ExtractedArchitectureSnapshot snapshot = context.Accumulation.ToSnapshot();

            Assert.False(result.HasBlockingError);
            Assert.DoesNotContain(snapshot.Nodes, node => node.NodeKind == NodeKind.ExternalService || node.NodeKind == NodeKind.Queue || node.NodeKind == NodeKind.Topic);
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies explicit test observations are projected through the API stage and shared accumulator.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenObservationProviderContributesEvidence_ShouldAccumulateIntegrationFacts()
        {
            // The provider seam lets tests and later detectors prove stage behavior without performing live calls.
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
                12,
                "SendAsync",
                "App.Client",
                "await _httpClient.SendAsync(request);",
                "HttpClient.SendAsync",
                UnknownReason: null,
                ConfigurationKeyStableKey: null);
            ExternalIntegrationExtractionStage stage = new(
                new ExternalIntegrationFoundationExtractor(),
                new StaticExternalIntegrationObservationProvider([observation]),
                NullLogger<ExternalIntegrationExtractionStage>.Instance);
            ExtractionStageContext context = CreateContext();

            ExtractionStageResult result = await stage.ExecuteAsync(context, CancellationToken.None);
            ExtractedArchitectureSnapshot snapshot = context.Accumulation.ToSnapshot();

            Assert.False(result.HasBlockingError);
            Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.ExternalService);
            Assert.Single(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.CallsExternalService);
            Assert.Single(snapshot.Evidence);
        }

        /// <summary>
        /// Verifies provider warnings and errors are propagated through the shared snapshot accumulator.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenObservationProviderReportsDiagnostics_ShouldPropagateWarningsAndErrors()
        {
            // Diagnostics must remain visible to run status and snapshot consumers even when no graph facts are emitted.
            ExternalIntegrationExtractionStage stage = new(
                new ExternalIntegrationFoundationExtractor(),
                new StaticExternalIntegrationObservationProvider([], ["degraded integration scan"], ["integration scan failed safely"]),
                NullLogger<ExternalIntegrationExtractionStage>.Instance);
            ExtractionStageContext context = CreateContext();

            ExtractionStageResult result = await stage.ExecuteAsync(context, CancellationToken.None);
            ExtractedArchitectureSnapshot snapshot = context.Accumulation.ToSnapshot();

            Assert.False(result.HasBlockingError);
            Assert.Contains("degraded integration scan", snapshot.Warnings);
            Assert.Contains("integration scan failed safely", snapshot.Errors);
        }

        /// <summary>
        /// Creates a pipeline stage context suitable for API stage tests.
        /// </summary>
        /// <returns>A stage context with normalized repository input and an empty accumulator.</returns>
        private static ExtractionStageContext CreateContext()
        {
            // The context mirrors an accepted API-triggered extraction run without requiring endpoint hosting.
            ResolvedExtractionInput input = new(
                "D:/Dev/Archon",
                ["D:/Dev/Archon/Archon.slnx"],
                BranchName: "main",
                CommitSha: "abcdef1234567890",
                RequestedBy: "developer@example.invalid",
                Metadata: new Dictionary<string, string>());
            ExtractionRun run = new(
                ExtractionRunId.New(),
                ExtractionRunStatus.Queued,
                new ExtractionRunRequestSummary(input.RepositoryRootDirectory, input.SolutionPaths, input.BranchName, input.CommitSha, input.RequestedBy, input.Metadata.Keys.ToArray()),
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                completedUtc: null,
                new ExtractionRunProgress("Queued", "Queued for external integration stage test.", 0, new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero)),
                warnings: null,
                errors: null,
                timings: null,
                snapshotIdentity: null);

            return new ExtractionStageContext(input, run, new Archon.Application.Extraction.Accumulation.ArchitectureSnapshotAccumulator());
        }
    }
}
