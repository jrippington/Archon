using Archon.Application.Extraction.Resolution;

namespace Archon.Application.Extraction.Runs
{
    /// <summary>
    /// Provides a deterministic in-memory implementation of extraction run history for tests and local API execution.
    /// </summary>
    public sealed class InMemoryExtractionRunHistory : IExtractionRunHistory
    {
        /// <summary>
        /// Synchronizes access to the run dictionary so concurrent HTTP requests observe consistent state.
        /// </summary>
        private readonly object _syncRoot = new();

        /// <summary>
        /// Stores the latest run state by public run identifier.
        /// </summary>
        private readonly Dictionary<ExtractionRunId, ExtractionRun> _runs = [];

        /// <summary>
        /// Creates a new queued extraction run from normalized request values.
        /// </summary>
        /// <param name="resolvedInput">The normalized input accepted for asynchronous extraction.</param>
        /// <param name="startedUtc">The UTC timestamp assigned to the accepted run.</param>
        /// <param name="cancellationToken">The cancellation token for the create operation.</param>
        /// <returns>The created queued run.</returns>
        public Task<ExtractionRun> CreateAsync(ResolvedExtractionInput resolvedInput, DateTimeOffset startedUtc, CancellationToken cancellationToken)
        {
            // Create performs no I/O but still observes cancellation so application code has a consistent async contract.
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(resolvedInput);

            ExtractionRun run = new(
                ExtractionRunId.New(),
                ExtractionRunStatus.Queued,
                new ExtractionRunRequestSummary(
                    resolvedInput.RepositoryRootDirectory,
                    resolvedInput.SolutionPaths.ToArray(),
                    resolvedInput.BranchName,
                    resolvedInput.CommitSha,
                    resolvedInput.RequestedBy,
                    resolvedInput.Metadata.Keys.Order(StringComparer.Ordinal).ToArray()),
                startedUtc,
                completedUtc: null,
                new ExtractionRunProgress(
                    "Queued",
                    "Extraction request accepted and queued for asynchronous execution.",
                    Percentage: 0,
                    LastUpdatedUtc: startedUtc),
                warnings: null,
                errors: null,
                snapshotIdentity: null);

            lock (_syncRoot)
            {
                _runs.Add(run.RunId, run);
            }

            return Task.FromResult(run);
        }

        /// <summary>
        /// Replaces the latest stored run state for an existing run identifier.
        /// </summary>
        /// <param name="run">The run state to store.</param>
        /// <param name="cancellationToken">The cancellation token for the update operation.</param>
        /// <returns>A task that completes after the run is stored.</returns>
        public Task UpdateAsync(ExtractionRun run, CancellationToken cancellationToken)
        {
            // Update uses replacement rather than mutation to keep returned run objects immutable to callers.
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(run);

            lock (_syncRoot)
            {
                _runs[run.RunId] = run;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Retrieves the latest state for a run identifier.
        /// </summary>
        /// <param name="runId">The stable run identifier to retrieve.</param>
        /// <param name="cancellationToken">The cancellation token for the read operation.</param>
        /// <returns>The latest run state when present; otherwise <see langword="null"/>.</returns>
        public Task<ExtractionRun?> GetAsync(ExtractionRunId runId, CancellationToken cancellationToken)
        {
            // Lookup is lock-protected so callers never observe dictionary mutation while enumerating or updating.
            cancellationToken.ThrowIfCancellationRequested();

            lock (_syncRoot)
            {
                _runs.TryGetValue(runId, out ExtractionRun? run);
                return Task.FromResult(run);
            }
        }

        /// <summary>
        /// Retrieves recent run states in deterministic newest-first order.
        /// </summary>
        /// <param name="limit">The maximum number of runs to return.</param>
        /// <param name="cancellationToken">The cancellation token for the read operation.</param>
        /// <returns>The recent run states ordered by start time descending and run identifier ascending for ties.</returns>
        public Task<IReadOnlyList<ExtractionRun>> GetRecentAsync(int limit, CancellationToken cancellationToken)
        {
            // The deterministic secondary ordering keeps tests stable when runs share the same timestamp.
            cancellationToken.ThrowIfCancellationRequested();

            lock (_syncRoot)
            {
                IReadOnlyList<ExtractionRun> runs = _runs.Values
                    .OrderByDescending(run => run.StartedUtc)
                    .ThenBy(run => run.RunId.Value)
                    .Take(Math.Max(0, limit))
                    .ToArray();

                return Task.FromResult(runs);
            }
        }
    }
}
