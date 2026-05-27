using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Application.Extraction.Snapshots;
using Archon.Application.Graph.Persistence;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Archon.Application.Extraction.Orchestration
{
    /// <summary>
    /// Coordinates accepted asynchronous extraction work from operational run state through pipeline execution, snapshot assembly, and persistence.
    /// </summary>
    public sealed class ExtractionOrchestrator
    {
        /// <summary>
        /// Stores and updates accepted extraction run lifecycle state.
        /// </summary>
        private readonly IExtractionRunHistory _runHistory;

        /// <summary>
        /// Executes configured extraction stages against a shared accumulation model.
        /// </summary>
        private readonly ExtractionPipelineRunner _pipelineRunner;

        /// <summary>
        /// Builds the generalized snapshot contract that is safe to hand to persistence.
        /// </summary>
        private readonly ExtractionSnapshotAssembler _snapshotAssembler;

        /// <summary>
        /// Persists assembled snapshots through the application-layer WP003 port.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Logs credential-safe orchestration events and failures.
        /// </summary>
        private readonly ILogger<ExtractionOrchestrator> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtractionOrchestrator"/> class.
        /// </summary>
        /// <param name="runHistory">The run-history store containing accepted run state and resolved input.</param>
        /// <param name="pipelineRunner">The deterministic extraction stage pipeline runner.</param>
        /// <param name="snapshotAssembler">The snapshot assembler that produces the generalized persistence contract.</param>
        /// <param name="snapshotWriter">The WP003 snapshot persistence abstraction.</param>
        /// <param name="logger">The logger used for credential-safe orchestration diagnostics.</param>
        public ExtractionOrchestrator(
            IExtractionRunHistory runHistory,
            ExtractionPipelineRunner pipelineRunner,
            ExtractionSnapshotAssembler snapshotAssembler,
            IArchitectureSnapshotWriter snapshotWriter,
            ILogger<ExtractionOrchestrator> logger)
        {
            // Constructor injection keeps orchestration application-owned and independently testable from API and Neo4j infrastructure.
            _runHistory = runHistory ?? throw new ArgumentNullException(nameof(runHistory));
            _pipelineRunner = pipelineRunner ?? throw new ArgumentNullException(nameof(pipelineRunner));
            _snapshotAssembler = snapshotAssembler ?? throw new ArgumentNullException(nameof(snapshotAssembler));
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Executes accepted extraction work for a queued run and records a terminal lifecycle state.
        /// </summary>
        /// <param name="runId">The accepted run identifier whose work should execute.</param>
        /// <param name="cancellationToken">The cancellation token for orchestration, pipeline, assembly, and persistence.</param>
        /// <returns><see langword="true"/> when the run completed successfully; otherwise <see langword="false"/>.</returns>
        public async Task<bool> ExecuteAsync(ExtractionRunId runId, CancellationToken cancellationToken)
        {
            // The scheduler only passes a run id, so orchestration reconstructs execution context from accepted run history.
            cancellationToken.ThrowIfCancellationRequested();
            ExtractionRun? run = await _runHistory.GetAsync(runId, cancellationToken).ConfigureAwait(false);
            if (run is null)
            {
                _logger.LogWarning("Skipping extraction orchestration because run {RunId} was not found.", runId.ToString());
                return false;
            }

            try
            {
                Stopwatch totalStopwatch = Stopwatch.StartNew();
                List<ExtractionRunTiming> completedTimings = [];
                Stopwatch validationStopwatch = Stopwatch.StartNew();
                ResolvedExtractionInput resolvedInput = CreateResolvedInput(run.SubmittedRequest);
                validationStopwatch.Stop();
                completedTimings.Add(CreateTiming("Validation", validationStopwatch));
                await UpdateRunAsync(run, ExtractionRunStatus.Running, "Validation", "Accepted request context is ready for extraction.", 10, null, null, null, cancellationToken).ConfigureAwait(false);

                run = await GetRequiredRunAsync(runId, cancellationToken).ConfigureAwait(false);
                await UpdateRunAsync(run, ExtractionRunStatus.Running, "Pipeline", "Executing extraction stages.", 35, null, null, null, cancellationToken).ConfigureAwait(false);

                Stopwatch pipelineStopwatch = Stopwatch.StartNew();
                ExtractionPipelineResult pipelineResult = await _pipelineRunner.ExecuteAsync(resolvedInput, run, cancellationToken).ConfigureAwait(false);
                pipelineStopwatch.Stop();
                completedTimings.AddRange(pipelineResult.StageTimings);
                completedTimings.Add(CreateTiming("Pipeline", pipelineStopwatch));
                IReadOnlyList<ExtractionRunWarning> pipelineWarnings = CreateWarnings(pipelineResult.Accumulation, pipelineResult.FailedStageId ?? "Pipeline");
                if (!pipelineResult.Succeeded)
                {
                    IReadOnlyList<ExtractionRunError> pipelineErrors = CreateErrors(pipelineResult.Accumulation, pipelineResult.FailedStageId ?? "Pipeline");
                    await UpdateRunAsync(run, ExtractionRunStatus.Failed, "Pipeline", "Extraction stage execution failed.", 100, pipelineWarnings, pipelineErrors, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
                    await AppendTimingsAsync(runId, completedTimings.Concat([CreateTiming("Total", totalStopwatch)]), CancellationToken.None).ConfigureAwait(false);
                    return false;
                }

                run = await GetRequiredRunAsync(runId, cancellationToken).ConfigureAwait(false);
                await UpdateRunAsync(run, ExtractionRunStatus.Running, "Assembly", "Assembling generalized architecture snapshot.", 65, pipelineWarnings, null, null, cancellationToken).ConfigureAwait(false);

                run = await GetRequiredRunAsync(runId, cancellationToken).ConfigureAwait(false);
                Stopwatch assemblyStopwatch = Stopwatch.StartNew();
                ExtractedArchitectureSnapshot snapshot = _snapshotAssembler.Assemble(run, resolvedInput, pipelineResult.Accumulation);
                assemblyStopwatch.Stop();
                completedTimings.Add(CreateTiming("Assembly", assemblyStopwatch));
                await UpdateRunAsync(run, ExtractionRunStatus.Running, "Persistence", "Persisting architecture snapshot.", 85, null, null, null, cancellationToken).ConfigureAwait(false);

                Stopwatch persistenceStopwatch = Stopwatch.StartNew();
                SnapshotPersistenceResult persistenceResult = await _snapshotWriter.WriteSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
                persistenceStopwatch.Stop();
                completedTimings.Add(CreateTiming("Persistence", persistenceStopwatch));
                if (!persistenceResult.Succeeded)
                {
                    IReadOnlyList<ExtractionRunWarning> persistenceWarnings = CreateWarnings(persistenceResult.Warnings);
                    IReadOnlyList<ExtractionRunError> persistenceErrors = CreateErrors(persistenceResult.Errors);
                    run = await GetRequiredRunAsync(runId, cancellationToken).ConfigureAwait(false);
                    await UpdateRunAsync(run, ExtractionRunStatus.Failed, "Persistence", "Snapshot persistence failed.", 100, persistenceWarnings, persistenceErrors, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
                    await AppendTimingsAsync(runId, completedTimings.Concat([CreateTiming("Total", totalStopwatch)]), CancellationToken.None).ConfigureAwait(false);
                    return false;
                }

                run = await GetRequiredRunAsync(runId, cancellationToken).ConfigureAwait(false);
                IReadOnlyList<ExtractionRunWarning> successWarnings = CreateWarnings(persistenceResult.Warnings);
                await UpdateRunAsync(run, ExtractionRunStatus.Completed, "Completed", "Extraction snapshot persisted successfully.", 100, successWarnings, null, DateTimeOffset.UtcNow, cancellationToken, persistenceResult.SnapshotStableKey).ConfigureAwait(false);
                await AppendTimingsAsync(runId, completedTimings.Concat([CreateTiming("Total", totalStopwatch)]), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Extraction run {RunId} completed and persisted snapshot {SnapshotStableKey}.", runId.ToString(), persistenceResult.SnapshotStableKey);
                return true;
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected during shutdown and should remain observable as a controlled run failure for polling clients.
                await RecordUnexpectedFailureAsync(runId, "Cancelled", "Extraction work was cancelled before completion.", cancellationToken: CancellationToken.None).ConfigureAwait(false);
                return false;
            }
            catch (Exception exception)
            {
                // Unexpected exceptions are logged with details for operators but exposed through status as a safe generic error.
                _logger.LogError(exception, "Extraction orchestration failed unexpectedly for run {RunId}.", runId.ToString());
                await RecordUnexpectedFailureAsync(runId, "UnexpectedFailure", "Extraction orchestration failed. Review server logs for details.", CancellationToken.None).ConfigureAwait(false);
                return false;
            }
        }

        /// <summary>
        /// Rebuilds normalized extraction input from the accepted run summary.
        /// </summary>
        /// <param name="summary">The accepted request summary captured when the run was created.</param>
        /// <returns>The resolved extraction input used by pipeline and assembly execution.</returns>
        private static ResolvedExtractionInput CreateResolvedInput(ExtractionRunRequestSummary summary)
        {
            // The run-history summary is only created after validation. It retains metadata keys for diagnostics, but intentionally omits
            // caller-supplied metadata values, so the orchestrator reconstructs safe key placeholders for downstream boundary metadata.
            ArgumentNullException.ThrowIfNull(summary);
            return new ResolvedExtractionInput(
                summary.RepositoryRootDirectory,
                summary.SolutionPaths,
                summary.BranchName,
                summary.CommitSha,
                summary.RequestedBy,
                summary.MetadataKeys.ToDictionary(key => key, key => key, StringComparer.Ordinal));
        }

        /// <summary>
        /// Retrieves the latest run state or throws when an accepted run disappears unexpectedly.
        /// </summary>
        /// <param name="runId">The accepted run identifier to retrieve.</param>
        /// <param name="cancellationToken">The cancellation token for the lookup.</param>
        /// <returns>The latest run state.</returns>
        private async Task<ExtractionRun> GetRequiredRunAsync(ExtractionRunId runId, CancellationToken cancellationToken)
        {
            // A missing run after orchestration starts indicates a store inconsistency that should become a controlled failure.
            ExtractionRun? run = await _runHistory.GetAsync(runId, cancellationToken).ConfigureAwait(false);
            return run ?? throw new InvalidOperationException("Accepted extraction run state was not found during orchestration.");
        }

        /// <summary>
        /// Updates run state with a progress point, diagnostics, optional terminal time, and optional snapshot identity.
        /// </summary>
        /// <param name="run">The latest run snapshot to update.</param>
        /// <param name="status">The lifecycle status to record.</param>
        /// <param name="stage">The stage name to expose in progress.</param>
        /// <param name="message">The credential-safe progress message.</param>
        /// <param name="percentage">The optional progress percentage.</param>
        /// <param name="warnings">The optional warnings to append.</param>
        /// <param name="errors">The optional errors to append.</param>
        /// <param name="completedUtc">The optional terminal timestamp.</param>
        /// <param name="cancellationToken">The cancellation token for the update operation.</param>
        /// <param name="snapshotIdentity">The optional stable snapshot identity to record after persistence succeeds.</param>
        /// <returns>A task that completes after the store update has finished.</returns>
        private async Task UpdateRunAsync(
            ExtractionRun run,
            ExtractionRunStatus status,
            string stage,
            string message,
            int? percentage,
            IEnumerable<ExtractionRunWarning>? warnings,
            IEnumerable<ExtractionRunError>? errors,
            DateTimeOffset? completedUtc,
            CancellationToken cancellationToken,
            string? snapshotIdentity = null)
        {
            // Direct store updates avoid a circular dependency from orchestration back to the start application service.
            ExtractionRunProgress progress = new(stage, message, percentage, DateTimeOffset.UtcNow);
            ExtractionRun updatedRun = run.WithStatus(status, progress, completedUtc, snapshotIdentity).WithDiagnostics(warnings, errors);
            await _runHistory.UpdateAsync(updatedRun, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Appends completed timing records to the latest run state.
        /// </summary>
        /// <param name="runId">The accepted run identifier to update.</param>
        /// <param name="timings">The timing records to append.</param>
        /// <param name="cancellationToken">The cancellation token for the update operation.</param>
        /// <returns>A task that completes after the store update has finished.</returns>
        private async Task AppendTimingsAsync(ExtractionRunId runId, IEnumerable<ExtractionRunTiming> timings, CancellationToken cancellationToken)
        {
            // Timings are appended after terminal state updates so status polling receives one complete performance summary.
            ExtractionRun run = await GetRequiredRunAsync(runId, cancellationToken).ConfigureAwait(false);
            ExtractionRun updatedRun = run.WithTimings(timings);
            await _runHistory.UpdateAsync(updatedRun, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a timing record from a stopped or currently running stopwatch.
        /// </summary>
        /// <param name="stage">The measured stage name.</param>
        /// <param name="stopwatch">The stopwatch containing elapsed duration.</param>
        /// <returns>A timing record suitable for run status responses.</returns>
        private static ExtractionRunTiming CreateTiming(string stage, Stopwatch stopwatch)
        {
            // Stop defensively so callers can pass the total stopwatch without repeating boilerplate.
            if (stopwatch.IsRunning)
            {
                stopwatch.Stop();
            }

            return new ExtractionRunTiming(stage, stopwatch.ElapsedMilliseconds, DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Converts accumulated pipeline warnings into run diagnostics.
        /// </summary>
        /// <param name="accumulation">The accumulation model containing warning diagnostics.</param>
        /// <param name="stage">The stage to associate with the warnings.</param>
        /// <returns>Run warning diagnostics for status readers.</returns>
        private static IReadOnlyList<ExtractionRunWarning> CreateWarnings(ArchitectureSnapshotAccumulator accumulation, string stage)
        {
            // Accumulator warnings are snapshot diagnostics; the run lifecycle mirrors them for polling clients.
            ArgumentNullException.ThrowIfNull(accumulation);
            return accumulation.ToSnapshot().Warnings
                .Select(warning => new ExtractionRunWarning("PipelineWarning", warning, stage, DateTimeOffset.UtcNow))
                .ToArray();
        }

        /// <summary>
        /// Converts persistence warnings into run diagnostics.
        /// </summary>
        /// <param name="warnings">The persistence warnings returned by the writer.</param>
        /// <returns>Run warning diagnostics for status readers.</returns>
        private static IReadOnlyList<ExtractionRunWarning> CreateWarnings(IEnumerable<PersistenceWarning> warnings)
        {
            // Persistence warnings are already application-owned and safe to surface through run diagnostics.
            ArgumentNullException.ThrowIfNull(warnings);
            return warnings
                .Select(warning => new ExtractionRunWarning(warning.Code, warning.Message, warning.Stage.ToString(), DateTimeOffset.UtcNow))
                .ToArray();
        }

        /// <summary>
        /// Converts accumulated pipeline errors into run diagnostics.
        /// </summary>
        /// <param name="accumulation">The accumulation model containing error diagnostics.</param>
        /// <param name="stage">The stage to associate with the errors.</param>
        /// <returns>Run error diagnostics for status readers.</returns>
        private static IReadOnlyList<ExtractionRunError> CreateErrors(ArchitectureSnapshotAccumulator accumulation, string stage)
        {
            // Pipeline blocking errors are controlled and credential-safe because stages must return sanitized messages.
            ArgumentNullException.ThrowIfNull(accumulation);
            return accumulation.ToSnapshot().Errors
                .Select(error => new ExtractionRunError("PipelineError", error, stage, DateTimeOffset.UtcNow))
                .ToArray();
        }

        /// <summary>
        /// Converts persistence errors into run diagnostics.
        /// </summary>
        /// <param name="errors">The persistence errors returned by the writer.</param>
        /// <returns>Run error diagnostics for status readers.</returns>
        private static IReadOnlyList<ExtractionRunError> CreateErrors(IEnumerable<PersistenceError> errors)
        {
            // Persistence errors are application-owned diagnostics produced by infrastructure adapters without driver details.
            ArgumentNullException.ThrowIfNull(errors);
            return errors
                .Select(error => new ExtractionRunError(error.Code, error.Message, error.Stage.ToString(), DateTimeOffset.UtcNow))
                .ToArray();
        }

        /// <summary>
        /// Records an unexpected terminal failure for an accepted run.
        /// </summary>
        /// <param name="runId">The accepted run identifier that failed.</param>
        /// <param name="code">The safe error code to expose.</param>
        /// <param name="message">The safe error message to expose.</param>
        /// <param name="cancellationToken">The cancellation token for the failure update.</param>
        /// <returns>A task that completes after the failure has been recorded when the run still exists.</returns>
        private async Task RecordUnexpectedFailureAsync(ExtractionRunId runId, string code, string message, CancellationToken cancellationToken)
        {
            // A fresh lookup preserves warnings or progress already recorded before the exception occurred.
            ExtractionRun? run = await _runHistory.GetAsync(runId, cancellationToken).ConfigureAwait(false);
            if (run is null)
            {
                return;
            }

            ExtractionRunError error = new(code, message, "Orchestration", DateTimeOffset.UtcNow);
            await UpdateRunAsync(run, ExtractionRunStatus.Failed, "Failed", message, 100, null, [error], DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        }
    }
}
