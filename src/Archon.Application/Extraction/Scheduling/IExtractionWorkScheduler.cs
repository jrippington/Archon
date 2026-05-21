using Archon.Application.Extraction.Runs;

namespace Archon.Application.Extraction.Scheduling
{
    /// <summary>
    /// Defines the application seam that dispatches accepted extraction work outside the HTTP request path.
    /// </summary>
    public interface IExtractionWorkScheduler
    {
        /// <summary>
        /// Schedules accepted extraction work for asynchronous execution.
        /// </summary>
        /// <param name="runId">The accepted run identifier whose work should be dispatched.</param>
        /// <param name="cancellationToken">The cancellation token for the scheduling request.</param>
        /// <returns>A task that completes when scheduling has accepted the work item.</returns>
        Task ScheduleAsync(ExtractionRunId runId, CancellationToken cancellationToken);
    }
}
