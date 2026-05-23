using Archon.Application.Extraction.Pipeline;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Avalonia;
using Archon.Extractors.Ui;
using Microsoft.Extensions.Logging;

namespace Archon.Api.Extraction
{
    /// <summary>
    /// Runs the WP011 Avalonia AXAML extraction slice as part of API-triggered extraction orchestration.
    /// </summary>
    /// <remarks>
    /// The stage is an orchestration adapter only. It delegates static project, AXAML, view-locator, ReactiveUI, navigation, and code-behind analysis to <see cref="AvaloniaAxamlExtractor" /> and merges the returned graph facts into the shared accumulator without requiring Avalonia packages to load, starting desktop lifetimes, rendering UI, opening databases, or writing directly to Neo4j.
    /// </remarks>
    public sealed class Wp011AvaloniaAxamlExtractionStage : IExtractionStage
    {
        /// <summary>
        /// Stores the Avalonia extractor that performs deterministic static AXAML and source analysis.
        /// </summary>
        private readonly AvaloniaAxamlExtractor _extractor;

        /// <summary>
        /// Stores the logger used for credential-safe WP011 Avalonia orchestration diagnostics.
        /// </summary>
        private readonly ILogger<Wp011AvaloniaAxamlExtractionStage> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Wp011AvaloniaAxamlExtractionStage" /> class.
        /// </summary>
        /// <param name="extractor">The Avalonia extractor responsible for projecting static AXAML, view-locator, ReactiveUI, navigation, and code-behind evidence into graph facts.</param>
        /// <param name="logger">The logger used for stage start, completion, cancellation, and degraded extraction diagnostics.</param>
        public Wp011AvaloniaAxamlExtractionStage(AvaloniaAxamlExtractor extractor, ILogger<Wp011AvaloniaAxamlExtractionStage> logger)
        {
            // Constructor injection keeps API composition separate from extractor implementation details and supports focused stage tests.
            _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the stable stage identifier used by pipeline ordering, progress reporting, and diagnostics.
        /// </summary>
        public string StageId => "wp011-avalonia-axaml";

        /// <summary>
        /// Executes static Avalonia extraction and merges the resulting snapshot into shared accumulation.
        /// </summary>
        /// <param name="context">The pipeline context containing resolved repository input, accepted run state, and shared accumulation.</param>
        /// <param name="cancellationToken">The cancellation token that stops Avalonia artifact discovery and graph projection.</param>
        /// <returns>A successful stage result when Avalonia extraction completes or produces non-fatal diagnostics.</returns>
        public async Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // WP011 Avalonia extraction is non-blocking because absence of Avalonia artifacts should not prevent other extraction facts from being persisted.
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Starting WP011 Avalonia AXAML extraction for run {RunId}.",
                context.Run.RunId.ToString());

            StableKey snapshotStableKey = CreateSnapshotStableKey(context.ResolvedInput.RepositoryRootDirectory, context.Run.RunId.ToString());
            AvaloniaAxamlExtractionResult result = await _extractor.ExtractAsync(new AvaloniaAxamlExtractionRequest(snapshotStableKey, context.ResolvedInput.RepositoryRootDirectory), cancellationToken).ConfigureAwait(false);
            UiSnapshotMergeSummary mergeSummary = context.Accumulation.MergeUiSnapshot(result.Snapshot);

            _logger.LogInformation(
                "Completed WP011 Avalonia AXAML extraction for run {RunId}; added {NodeCount} node(s), {EdgeCount} edge(s), {EvidenceCount} evidence record(s), {WarningCount} warning(s), and {ErrorCount} error(s) after stable-key deduplication.",
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
            // Snapshot identity mirrors adjacent API extraction stages so independently emitted snapshots merge under the current run scope.
            string repositoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(repositoryRootDirectory));
            return new StableKey($"snapshot://{repositoryName}/{runId}");
        }
    }
}
