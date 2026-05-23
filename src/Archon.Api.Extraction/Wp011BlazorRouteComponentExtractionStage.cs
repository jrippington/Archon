using Archon.Application.Extraction.Pipeline;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Blazor;
using Archon.Extractors.Ui;
using Microsoft.Extensions.Logging;

namespace Archon.Api.Extraction
{
    /// <summary>
    /// Runs the WP011 Blazor route and component extraction slice as part of API-triggered extraction orchestration.
    /// </summary>
    /// <remarks>
    /// The stage is an orchestration adapter only. It delegates static Razor artifact analysis to <see cref="BlazorRouteComponentExtractor" /> and merges the returned graph facts into the shared accumulator without rendering UI, starting the target application, calling APIs, or writing directly to Neo4j.
    /// </remarks>
    public sealed class Wp011BlazorRouteComponentExtractionStage : IExtractionStage
    {
        /// <summary>
        /// Stores the Blazor extractor that performs deterministic Razor artifact analysis.
        /// </summary>
        private readonly BlazorRouteComponentExtractor _extractor;

        /// <summary>
        /// Stores the logger used for credential-safe WP011 orchestration diagnostics.
        /// </summary>
        private readonly ILogger<Wp011BlazorRouteComponentExtractionStage> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Wp011BlazorRouteComponentExtractionStage" /> class.
        /// </summary>
        /// <param name="extractor">The Blazor extractor responsible for projecting Razor artifacts into graph facts.</param>
        /// <param name="logger">The logger used for stage start, completion, cancellation, and degraded extraction diagnostics.</param>
        public Wp011BlazorRouteComponentExtractionStage(BlazorRouteComponentExtractor extractor, ILogger<Wp011BlazorRouteComponentExtractionStage> logger)
        {
            // Constructor injection keeps stage composition in the API module while the extractor project owns all Blazor-specific logic.
            _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the stable stage identifier used by pipeline ordering, progress reporting, and diagnostics.
        /// </summary>
        public string StageId => "wp011-blazor-route-component";

        /// <summary>
        /// Executes static Blazor route and component extraction and merges the resulting snapshot into shared accumulation.
        /// </summary>
        /// <param name="context">The pipeline context containing resolved repository input, accepted run state, and shared accumulation.</param>
        /// <param name="cancellationToken">The cancellation token that stops Razor artifact discovery and graph projection.</param>
        /// <returns>A successful stage result when Blazor extraction completes or produces non-fatal diagnostics.</returns>
        public async Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // WP011 is non-blocking because absence of Blazor artifacts or partial Razor content should not prevent other extraction facts from being persisted.
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Starting WP011 Blazor route and component extraction for run {RunId}.",
                context.Run.RunId.ToString());

            StableKey snapshotStableKey = CreateSnapshotStableKey(context.ResolvedInput.RepositoryRootDirectory, context.Run.RunId.ToString());
            BlazorRouteComponentExtractionResult result = await _extractor.ExtractAsync(new BlazorRouteComponentExtractionRequest(snapshotStableKey, context.ResolvedInput.RepositoryRootDirectory), cancellationToken).ConfigureAwait(false);
            UiSnapshotMergeSummary mergeSummary = context.Accumulation.MergeUiSnapshot(result.Snapshot);

            _logger.LogInformation(
                "Completed WP011 Blazor route and component extraction for run {RunId}; added {NodeCount} node(s), {EdgeCount} edge(s), {EvidenceCount} evidence record(s), {WarningCount} warning(s), and {ErrorCount} error(s) after stable-key deduplication.",
                context.Run.RunId.ToString(),
                mergeSummary.NodeDelta,
                mergeSummary.EdgeDelta,
                mergeSummary.EvidenceDelta,
                mergeSummary.WarningDelta,
                mergeSummary.ErrorDelta);

            return ExtractionStageResult.Success();
        }

        /// <summary>
        /// Creates the snapshot stable key used by this API extraction stage.
        /// </summary>
        /// <param name="repositoryRootDirectory">The repository root directory for the analyzed repository.</param>
        /// <param name="runId">The accepted extraction run identifier.</param>
        /// <returns>A deterministic snapshot stable key scoped to the run.</returns>
        private static StableKey CreateSnapshotStableKey(string repositoryRootDirectory, string runId)
        {
            // Snapshot identity mirrors previous API extraction stages so independently emitted snapshots merge under the current run scope.
            string repositoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(repositoryRootDirectory));
            return new StableKey($"snapshot://{repositoryName}/{runId}");
        }
    }
}