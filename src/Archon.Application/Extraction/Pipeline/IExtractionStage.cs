using Archon.Application.Extraction.Accumulation;

namespace Archon.Application.Extraction.Pipeline
{
    /// <summary>
    /// Defines one deterministic extraction stage that contributes facts or diagnostics to a shared accumulation model.
    /// </summary>
    public interface IExtractionStage
    {
        /// <summary>
        /// Gets the stable stage identifier used for ordering, logging, progress reporting, and diagnostics.
        /// </summary>
        string StageId { get; }

        /// <summary>
        /// Executes the stage against resolved input and shared accumulation state.
        /// </summary>
        /// <param name="context">The stage context containing validated input, accepted run state, and accumulation state.</param>
        /// <param name="cancellationToken">The cancellation token that stops stage execution before or during long-running work.</param>
        /// <returns>The stage result describing whether the pipeline can continue.</returns>
        Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken);
    }
}
