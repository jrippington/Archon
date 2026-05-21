using Archon.Application.Extraction.Resolution;

namespace Archon.Application.Extraction.Runs
{
    /// <summary>
    /// Defines replaceable operational storage for extraction run lifecycle state.
    /// </summary>
    public interface IExtractionRunHistory
    {
        /// <summary>
        /// Creates a new accepted run record from normalized extraction input.
        /// </summary>
        /// <param name="resolvedInput">The normalized input accepted for asynchronous extraction.</param>
        /// <param name="startedUtc">The UTC timestamp assigned to the accepted run.</param>
        /// <param name="cancellationToken">The cancellation token for the create operation.</param>
        /// <returns>The created run state.</returns>
        Task<ExtractionRun> CreateAsync(ResolvedExtractionInput resolvedInput, DateTimeOffset startedUtc, CancellationToken cancellationToken);

        /// <summary>
        /// Replaces the stored state for an existing run.
        /// </summary>
        /// <param name="run">The run state to store.</param>
        /// <param name="cancellationToken">The cancellation token for the update operation.</param>
        /// <returns>A task that completes after the state is stored.</returns>
        Task UpdateAsync(ExtractionRun run, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves a run by its stable identifier.
        /// </summary>
        /// <param name="runId">The stable run identifier to retrieve.</param>
        /// <param name="cancellationToken">The cancellation token for the read operation.</param>
        /// <returns>The matching run when found; otherwise <see langword="null"/>.</returns>
        Task<ExtractionRun?> GetAsync(ExtractionRunId runId, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves recent runs in deterministic newest-first order.
        /// </summary>
        /// <param name="limit">The maximum number of runs to return.</param>
        /// <param name="cancellationToken">The cancellation token for the read operation.</param>
        /// <returns>The recent run states.</returns>
        Task<IReadOnlyList<ExtractionRun>> GetRecentAsync(int limit, CancellationToken cancellationToken);
    }
}
