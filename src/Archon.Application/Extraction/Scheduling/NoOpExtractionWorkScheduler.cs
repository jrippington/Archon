using Archon.Application.Extraction.Runs;

namespace Archon.Application.Extraction.Scheduling
{
    /// <summary>
    /// Provides the initial local scheduler adapter that accepts work without executing later orchestration slices.
    /// </summary>
    public sealed class NoOpExtractionWorkScheduler : IExtractionWorkScheduler
    {
        /// <summary>
        /// Accepts a run identifier as scheduled work and returns immediately.
        /// </summary>
        /// <param name="runId">The accepted run identifier whose work would be dispatched by a later scheduler.</param>
        /// <param name="cancellationToken">The cancellation token for the scheduling request.</param>
        /// <returns>A completed task once the work item is accepted by this placeholder scheduler.</returns>
        public Task ScheduleAsync(ExtractionRunId runId, CancellationToken cancellationToken)
        {
            // Work Item 1 proves the scheduling seam only; later slices replace this no-op with orchestration execution.
            cancellationToken.ThrowIfCancellationRequested();
            _ = runId;
            return Task.CompletedTask;
        }
    }
}
