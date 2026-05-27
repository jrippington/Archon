using Archon.Application.Extraction.Pipeline;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Razor;
using Archon.Extractors.Ui;
using Microsoft.Extensions.Logging;

namespace Archon.Api.Extraction
{
    /// <summary>
    /// Runs the WP011 Razor Pages and MVC Razor extraction slice as part of API-triggered extraction orchestration.
    /// </summary>
    /// <remarks>
    /// The stage is an orchestration adapter only. It delegates static `.cshtml` artifact analysis to <see cref="RazorPageViewExtractor" /> and merges the returned graph facts into the shared accumulator without compiling Razor, starting ASP.NET Core, rendering views, calling APIs, or writing directly to Neo4j.
    /// </remarks>
    public sealed class Wp011RazorPageViewExtractionStage : IExtractionStage
    {
        /// <summary>
        /// Stores the Razor extractor that performs deterministic `.cshtml` artifact analysis.
        /// </summary>
        private readonly RazorPageViewExtractor _extractor;

        /// <summary>
        /// Stores the logger used for credential-safe WP011 Razor orchestration diagnostics.
        /// </summary>
        private readonly ILogger<Wp011RazorPageViewExtractionStage> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Wp011RazorPageViewExtractionStage" /> class.
        /// </summary>
        /// <param name="extractor">The Razor extractor responsible for projecting `.cshtml` artifacts into graph facts.</param>
        /// <param name="logger">The logger used for stage start, completion, cancellation, and degraded extraction diagnostics.</param>
        public Wp011RazorPageViewExtractionStage(RazorPageViewExtractor extractor, ILogger<Wp011RazorPageViewExtractionStage> logger)
        {
            // Constructor injection keeps API composition separate from extractor implementation details.
            _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the stable stage identifier used by pipeline ordering, progress reporting, and diagnostics.
        /// </summary>
        public string StageId => "wp011-razor-page-view";

        /// <summary>
        /// Executes static Razor Pages and MVC Razor extraction and merges the resulting snapshot into shared accumulation.
        /// </summary>
        /// <param name="context">The pipeline context containing resolved repository input, accepted run state, and shared accumulation.</param>
        /// <param name="cancellationToken">The cancellation token that stops Razor artifact discovery and graph projection.</param>
        /// <returns>A successful stage result when Razor extraction completes or produces non-fatal diagnostics.</returns>
        public async Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // WP011 Razor extraction is non-blocking because absence of `.cshtml` artifacts or partial markup should not prevent other extraction facts from being persisted.
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Starting WP011 Razor Pages and MVC Razor extraction for run {RunId}.",
                context.Run.RunId.ToString());

            StableKey snapshotStableKey = CreateSnapshotStableKey(context.ResolvedInput.RepositoryRootDirectory, context.Run.RunId.ToString());
            RazorPageViewExtractionResult result = await _extractor.ExtractAsync(new RazorPageViewExtractionRequest(snapshotStableKey, context.ResolvedInput.RepositoryRootDirectory), cancellationToken).ConfigureAwait(false);
            UiSnapshotMergeSummary mergeSummary = context.Accumulation.MergeUiSnapshot(result.Snapshot);

            _logger.LogInformation(
                "Completed WP011 Razor Pages and MVC Razor extraction for run {RunId}; added {NodeCount} node(s), {EdgeCount} edge(s), {EvidenceCount} evidence record(s), {WarningCount} warning(s), and {ErrorCount} error(s) after stable-key deduplication.",
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