using Archon.Application.Extraction.Runs;
using Archon.Application.Extraction.Scheduling;
using Archon.Application.Extraction.Validation;
using Microsoft.Extensions.Logging;

namespace Archon.Application.Extraction.Requests
{
    /// <summary>
    /// Coordinates the first WP004 application path for accepting extraction requests and reading run status.
    /// </summary>
    public sealed class StartExtractionApplicationService
    {
        /// <summary>
        /// Validates submitted requests and produces normalized extraction input.
        /// </summary>
        private readonly StartExtractionRequestValidator _validator;

        /// <summary>
        /// Stores operational run lifecycle state.
        /// </summary>
        private readonly IExtractionRunHistory _runHistory;

        /// <summary>
        /// Dispatches accepted work outside the immediate request path.
        /// </summary>
        private readonly IExtractionWorkScheduler _scheduler;

        /// <summary>
        /// Logs credential-safe acceptance, validation, and scheduling events.
        /// </summary>
        private readonly ILogger<StartExtractionApplicationService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="StartExtractionApplicationService"/> class.
        /// </summary>
        /// <param name="validator">The validator used before any run state is created.</param>
        /// <param name="runHistory">The run-history store used for accepted run state.</param>
        /// <param name="scheduler">The scheduler used to dispatch accepted work asynchronously.</param>
        /// <param name="logger">The logger used for credential-safe application events.</param>
        public StartExtractionApplicationService(
            StartExtractionRequestValidator validator,
            IExtractionRunHistory runHistory,
            IExtractionWorkScheduler scheduler,
            ILogger<StartExtractionApplicationService> logger)
        {
            // Constructor injection keeps the application use case replaceable and testable without service locators.
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _runHistory = runHistory ?? throw new ArgumentNullException(nameof(runHistory));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Validates, accepts, records, and schedules a new extraction request.
        /// </summary>
        /// <param name="request">The extraction start request submitted by a caller.</param>
        /// <param name="cancellationToken">The cancellation token for validation, run creation, and scheduling.</param>
        /// <returns>The accepted run or validation errors when the request is rejected before acceptance.</returns>
        public async Task<StartExtractionResult> StartAsync(StartExtractionRequest request, CancellationToken cancellationToken)
        {
            // The method intentionally returns after scheduling and does not wait for extraction or persistence work.
            ArgumentNullException.ThrowIfNull(request);
            StartExtractionValidationResult validationResult = _validator.Validate(request);
            if (!validationResult.IsValid || validationResult.ResolvedInput is null)
            {
                _logger.LogInformation(
                    "Extraction start request rejected with {ValidationErrorCount} validation errors.",
                    validationResult.Errors.Count);
                return new StartExtractionResult(null, validationResult.Errors);
            }

            ExtractionRun run = await _runHistory.CreateAsync(validationResult.ResolvedInput, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Extraction run {RunId} accepted with {SolutionCount} submitted solutions.",
                run.RunId.ToString(),
                run.SubmittedRequest.SolutionPaths.Count);

            await _scheduler.ScheduleAsync(run.RunId, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Extraction run {RunId} queued for asynchronous execution.", run.RunId.ToString());

            return new StartExtractionResult(run, []);
        }

        /// <summary>
        /// Retrieves the current status for an extraction run identifier.
        /// </summary>
        /// <param name="runId">The run identifier to retrieve.</param>
        /// <param name="cancellationToken">The cancellation token for the lookup.</param>
        /// <returns>The current run state when found; otherwise <see langword="null"/>.</returns>
        public Task<ExtractionRun?> GetStatusAsync(ExtractionRunId runId, CancellationToken cancellationToken)
        {
            // Status retrieval is a thin application query over operational state; API code translates null to not found.
            return _runHistory.GetAsync(runId, cancellationToken);
        }

        /// <summary>
        /// Retrieves recent extraction runs for history views and polling clients.
        /// </summary>
        /// <param name="limit">The maximum number of recent runs to return.</param>
        /// <param name="cancellationToken">The cancellation token for the history query.</param>
        /// <returns>The recent runs in deterministic newest-first order.</returns>
        public Task<IReadOnlyList<ExtractionRun>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken)
        {
            // The service owns the default query boundary so API endpoints do not talk directly to the run-history store.
            return _runHistory.GetRecentAsync(limit, cancellationToken);
        }

        /// <summary>
        /// Updates lifecycle progress and appends diagnostics for an accepted extraction run.
        /// </summary>
        /// <param name="runId">The stable identifier of the run to update.</param>
        /// <param name="status">The lifecycle status that should become visible to status and history readers.</param>
        /// <param name="progress">The progress value that describes the current stage and message.</param>
        /// <param name="warnings">The optional warning diagnostics to append to the run.</param>
        /// <param name="errors">The optional error diagnostics to append to the run.</param>
        /// <param name="completedUtc">The optional terminal timestamp when the run has completed, failed, or been cancelled.</param>
        /// <param name="snapshotIdentity">The optional stable snapshot identity returned after persistence succeeds.</param>
        /// <param name="persistenceDiagnostics">The optional persistence-specific diagnostic breakdown to retain with the run.</param>
        /// <param name="cancellationToken">The cancellation token for the read-modify-write operation.</param>
        /// <returns><see langword="true"/> when the run was found and updated; otherwise <see langword="false"/>.</returns>
        public async Task<bool> UpdateRunProgressAsync(
            ExtractionRunId runId,
            ExtractionRunStatus status,
            ExtractionRunProgress progress,
            IEnumerable<ExtractionRunWarning>? warnings,
            IEnumerable<ExtractionRunError>? errors,
            DateTimeOffset? completedUtc,
            string? snapshotIdentity,
            ExtractionRunPersistenceDiagnostics? persistenceDiagnostics,
            CancellationToken cancellationToken)
        {
            // Background orchestration will use this read-modify-write seam to expose progress without mutating run snapshots in place.
            ArgumentNullException.ThrowIfNull(progress);

            ExtractionRun? currentRun = await _runHistory.GetAsync(runId, cancellationToken).ConfigureAwait(false);
            if (currentRun is null)
            {
                _logger.LogWarning("Skipping extraction progress update because run {RunId} was not found.", runId.ToString());
                return false;
            }

            ExtractionRun updatedRun = currentRun.WithStatus(status, progress, completedUtc, snapshotIdentity)
                .WithDiagnostics(warnings, errors)
                .WithPersistenceDiagnostics(persistenceDiagnostics ?? currentRun.PersistenceDiagnostics);
            await _runHistory.UpdateAsync(updatedRun, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Extraction run {RunId} progress updated to {Status} at stage {Stage}.",
                runId.ToString(),
                status.ToString(),
                progress.Stage);

            return true;
        }
    }
}
