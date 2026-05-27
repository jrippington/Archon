using Archon.Application.Extraction.Pipeline;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Integrations.Foundation;
using Microsoft.Extensions.Logging;

namespace Archon.Api.Extraction
{
    /// <summary>
    /// Runs the WP010 external integration extraction foundation slice as part of the API-triggered extraction pipeline.
    /// </summary>
    /// <remarks>
    /// The stage is an orchestration adapter. It obtains deterministic static-analysis observations from a provider, delegates graph projection to the integration extractor project, and merges the resulting facts into the shared snapshot accumulator without executing analyzed code or contacting external services.
    /// </remarks>
    public sealed class Wp010ExternalIntegrationExtractionStage : IExtractionStage
    {
        /// <summary>
        /// Stores the graph-projection extractor that converts integration observations into snapshot facts.
        /// </summary>
        private readonly ExternalIntegrationFoundationExtractor _extractor;

        /// <summary>
        /// Stores the observation provider used by this foundation stage.
        /// </summary>
        private readonly IExternalIntegrationObservationProvider _observationProvider;

        /// <summary>
        /// Stores the logger used for credential-safe WP010 orchestration events.
        /// </summary>
        private readonly ILogger<Wp010ExternalIntegrationExtractionStage> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Wp010ExternalIntegrationExtractionStage" /> class.
        /// </summary>
        /// <param name="logger">The logger used for stage start, completion, cancellation, and degraded extraction messages.</param>
        public Wp010ExternalIntegrationExtractionStage(ILogger<Wp010ExternalIntegrationExtractionStage> logger)
            : this(new ExternalIntegrationFoundationExtractor(), new NoOpExternalIntegrationObservationProvider(), logger)
        {
            // The default constructor keeps API module registration simple while future detector work items can replace the provider through DI composition.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Wp010ExternalIntegrationExtractionStage" /> class with explicit dependencies.
        /// </summary>
        /// <param name="extractor">The extractor responsible for projecting integration observations into graph facts.</param>
        /// <param name="observationProvider">The provider responsible for collecting deterministic integration observations.</param>
        /// <param name="logger">The logger used for credential-safe stage diagnostics.</param>
        public Wp010ExternalIntegrationExtractionStage(ExternalIntegrationFoundationExtractor extractor, IExternalIntegrationObservationProvider observationProvider, ILogger<Wp010ExternalIntegrationExtractionStage> logger)
        {
            // Explicit dependencies make the stage independently testable and keep host registration free of extractor logic.
            _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
            _observationProvider = observationProvider ?? throw new ArgumentNullException(nameof(observationProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the stable stage identifier used by pipeline ordering, progress reporting, and diagnostics.
        /// </summary>
        public string StageId => "wp010-external-integrations";

        /// <summary>
        /// Collects deterministic integration observations, projects them into graph facts, and merges them into shared accumulation.
        /// </summary>
        /// <param name="context">The pipeline context containing resolved repository input, accepted run state, and shared accumulation.</param>
        /// <param name="cancellationToken">The cancellation token that stops observation collection and graph projection.</param>
        /// <returns>A successful stage result when WP010 foundation extraction completes or degrades non-fatally.</returns>
        public async Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // The foundation stage is non-blocking because it only projects supplied static observations and records provider diagnostics separately.
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Starting WP010 external integration extraction for run {RunId} with {SolutionCount} submitted solution path(s).",
                context.Run.RunId.ToString(),
                context.ResolvedInput.SolutionPaths.Count);

            ExternalIntegrationObservationBatch observationBatch = await _observationProvider.CollectAsync(context, cancellationToken).ConfigureAwait(false);
            foreach (string warning in observationBatch.Warnings)
            {
                // Provider warnings are preserved before graph projection so degraded observation collection is visible even when no facts are emitted.
                context.Accumulation.AddWarning(warning);
            }

            foreach (string error in observationBatch.Errors)
            {
                // Provider errors are non-blocking for the foundation slice but remain available to run status and snapshot consumers.
                context.Accumulation.AddError(error);
            }

            StableKey snapshotStableKey = CreateSnapshotStableKey(context.ResolvedInput.RepositoryRootDirectory, context.Run.RunId.ToString());
            ExternalIntegrationExtractionRequest request = new(snapshotStableKey, context.ResolvedInput.RepositoryRootDirectory, observationBatch.Observations);
            ExternalIntegrationExtractionResult result = _extractor.Extract(request, cancellationToken);
            context.Accumulation.Merge(result.Snapshot);

            _logger.LogInformation(
                "Completed WP010 external integration extraction for run {RunId}; projected {ObservationCount} observation(s), {NodeCount} node(s), {EdgeCount} edge(s), and {EvidenceCount} evidence record(s).",
                context.Run.RunId.ToString(),
                observationBatch.Observations.Count,
                result.Snapshot.Nodes.Count,
                result.Snapshot.Edges.Count,
                result.Snapshot.Evidence.Count);

            return ExtractionStageResult.Success();
        }

        /// <summary>
        /// Creates the snapshot stable key used by extraction stages that merge graph facts into the current run.
        /// </summary>
        /// <param name="repositoryRootDirectory">The repository root directory for the analyzed repository.</param>
        /// <param name="runId">The accepted extraction run identifier.</param>
        /// <returns>A deterministic snapshot stable key scoped to the run.</returns>
        private static StableKey CreateSnapshotStableKey(string repositoryRootDirectory, string runId)
        {
            // The snapshot key mirrors the assembler so contributed facts are scoped to the same final snapshot identity.
            StableKey repositoryStableKey = StableKeyGenerator.ForRepository(NormalizeIdentitySegment(repositoryRootDirectory));
            return StableKeyGenerator.ForSummary(repositoryStableKey.Value, "ExtractionRun", runId);
        }

        /// <summary>
        /// Normalizes a filesystem path into the repository identity segment used by final snapshot assembly.
        /// </summary>
        /// <param name="value">The absolute path value to normalize.</param>
        /// <returns>A deterministic lowercase segment suitable for stable-key generation.</returns>
        private static string NormalizeIdentitySegment(string value)
        {
            // Stable keys must match the final snapshot assembler so stage contributions pass persistence scope validation.
            string trimmed = Path.TrimEndingDirectorySeparator(value).Replace('\\', '/').Trim();
            return trimmed.ToLowerInvariant();
        }
    }
}
