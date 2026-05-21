namespace Archon.Application.Extraction.Pipeline
{
    /// <summary>
    /// Provides the minimal WP004 placeholder extraction stage used to prove the pipeline boundary without real extraction facts.
    /// </summary>
    public sealed class PlaceholderExtractionStage : IExtractionStage
    {
        /// <summary>
        /// Gets the stable stage identifier used by logs, progress updates, and diagnostics.
        /// </summary>
        public string StageId => "placeholder";

        /// <summary>
        /// Executes the placeholder stage and records that no real extractor facts were produced in this slice.
        /// </summary>
        /// <param name="context">The stage context containing validated input, accepted run state, and accumulation state.</param>
        /// <param name="cancellationToken">The cancellation token for the placeholder stage.</param>
        /// <returns>A successful stage result because placeholder execution is non-blocking.</returns>
        public Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // Placeholder execution deliberately contributes only a warning so future slices cannot mistake it for real extraction capability.
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            context.Accumulation.AddWarning(
                "The placeholder extraction stage ran successfully but produced no repository, Roslyn, runtime, data-access, UI, markdown, MCP, rule, or full architecture facts.");
            return Task.FromResult(ExtractionStageResult.Success());
        }
    }
}
