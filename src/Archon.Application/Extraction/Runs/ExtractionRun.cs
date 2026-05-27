namespace Archon.Application.Extraction.Runs
{
    /// <summary>
    /// Represents the operational state recorded for an asynchronous extraction run.
    /// </summary>
    public sealed class ExtractionRun
    {
        /// <summary>
        /// Initializes a new immutable snapshot of extraction run state.
        /// </summary>
        /// <param name="runId">The stable public run identifier.</param>
        /// <param name="status">The current lifecycle status.</param>
        /// <param name="submittedRequest">The accepted request summary retained for auditing and troubleshooting.</param>
        /// <param name="startedUtc">The UTC timestamp when the run was accepted.</param>
        /// <param name="completedUtc">The optional UTC timestamp when the run reached a terminal state.</param>
        /// <param name="progress">The current progress details exposed through status polling.</param>
        /// <param name="warnings">The warning diagnostics recorded so far.</param>
        /// <param name="errors">The error diagnostics recorded so far.</param>
        /// <param name="timings">The measured extraction step durations recorded so far.</param>
        /// <param name="snapshotIdentity">The optional persisted snapshot stable identity once persistence succeeds.</param>
        /// <param name="persistenceDiagnostics">The optional persistence-specific diagnostic breakdown for this run.</param>
        public ExtractionRun(
            ExtractionRunId runId,
            ExtractionRunStatus status,
            ExtractionRunRequestSummary submittedRequest,
            DateTimeOffset startedUtc,
            DateTimeOffset? completedUtc,
            ExtractionRunProgress progress,
            IEnumerable<ExtractionRunWarning>? warnings,
            IEnumerable<ExtractionRunError>? errors,
            IEnumerable<ExtractionRunTiming>? timings,
            string? snapshotIdentity,
            ExtractionRunPersistenceDiagnostics? persistenceDiagnostics = null)
        {
            // The run object is immutable from consumers' perspective so the store controls lifecycle changes consistently.
            ArgumentNullException.ThrowIfNull(submittedRequest);
            ArgumentNullException.ThrowIfNull(progress);

            RunId = runId;
            Status = status;
            SubmittedRequest = submittedRequest;
            StartedUtc = startedUtc;
            CompletedUtc = completedUtc;
            Progress = progress;
            Warnings = warnings?.ToArray() ?? [];
            Errors = errors?.ToArray() ?? [];
            Timings = timings?.ToArray() ?? [];
            SnapshotIdentity = snapshotIdentity;
            PersistenceDiagnostics = persistenceDiagnostics;
        }

        /// <summary>
        /// Gets the stable public run identifier.
        /// </summary>
        public ExtractionRunId RunId { get; }

        /// <summary>
        /// Gets the current lifecycle status.
        /// </summary>
        public ExtractionRunStatus Status { get; }

        /// <summary>
        /// Gets the accepted request summary retained for auditing and troubleshooting.
        /// </summary>
        public ExtractionRunRequestSummary SubmittedRequest { get; }

        /// <summary>
        /// Gets the UTC timestamp when the run was accepted.
        /// </summary>
        public DateTimeOffset StartedUtc { get; }

        /// <summary>
        /// Gets the optional UTC timestamp when the run reached a terminal state.
        /// </summary>
        public DateTimeOffset? CompletedUtc { get; }

        /// <summary>
        /// Gets the current progress details exposed through status polling.
        /// </summary>
        public ExtractionRunProgress Progress { get; }

        /// <summary>
        /// Gets the warning diagnostics recorded so far.
        /// </summary>
        public IReadOnlyList<ExtractionRunWarning> Warnings { get; }

        /// <summary>
        /// Gets the error diagnostics recorded so far.
        /// </summary>
        public IReadOnlyList<ExtractionRunError> Errors { get; }

        /// <summary>
        /// Gets measured extraction step durations recorded for status diagnostics.
        /// </summary>
        public IReadOnlyList<ExtractionRunTiming> Timings { get; }

        /// <summary>
        /// Gets the optional persisted snapshot stable identity once persistence succeeds.
        /// </summary>
        public string? SnapshotIdentity { get; }

        /// <summary>
        /// Gets the optional persistence-specific diagnostic breakdown for this run.
        /// </summary>
        public ExtractionRunPersistenceDiagnostics? PersistenceDiagnostics { get; }

        /// <summary>
        /// Creates a copy of this run with a new status and progress value.
        /// </summary>
        /// <param name="status">The replacement lifecycle status.</param>
        /// <param name="progress">The replacement progress details.</param>
        /// <param name="completedUtc">The optional replacement terminal timestamp.</param>
        /// <param name="snapshotIdentity">The optional replacement snapshot identity.</param>
        /// <returns>A new run snapshot with the requested values applied.</returns>
        public ExtractionRun WithStatus(
            ExtractionRunStatus status,
            ExtractionRunProgress progress,
            DateTimeOffset? completedUtc = null,
            string? snapshotIdentity = null)
        {
            // Copying preserves immutable read models while allowing the store to replace the current state atomically.
            return new ExtractionRun(
                RunId,
                status,
                SubmittedRequest,
                StartedUtc,
                completedUtc ?? CompletedUtc,
                progress,
                Warnings,
                Errors,
                Timings,
                snapshotIdentity ?? SnapshotIdentity,
                PersistenceDiagnostics);
        }

        /// <summary>
        /// Creates a copy of this run with additional warning and error diagnostics appended.
        /// </summary>
        /// <param name="warnings">The warning diagnostics to append to the current warning collection.</param>
        /// <param name="errors">The error diagnostics to append to the current error collection.</param>
        /// <returns>A new run snapshot containing existing diagnostics plus the supplied diagnostics.</returns>
        public ExtractionRun WithDiagnostics(
            IEnumerable<ExtractionRunWarning>? warnings,
            IEnumerable<ExtractionRunError>? errors)
        {
            // Diagnostics are appended rather than replaced so polling clients see the complete operational history for a run.
            return new ExtractionRun(
                RunId,
                Status,
                SubmittedRequest,
                StartedUtc,
                CompletedUtc,
                Progress,
                Warnings.Concat(warnings ?? []),
                Errors.Concat(errors ?? []),
                Timings,
                SnapshotIdentity,
                PersistenceDiagnostics);
        }

        /// <summary>
        /// Creates a copy of this run with additional timing records appended.
        /// </summary>
        /// <param name="timings">The timing records to append to the current timing collection.</param>
        /// <returns>A new run snapshot containing existing timings plus the supplied timings.</returns>
        public ExtractionRun WithTimings(IEnumerable<ExtractionRunTiming>? timings)
        {
            // Timings are appended as operations complete so status polling can show incremental performance diagnostics.
            return new ExtractionRun(
                RunId,
                Status,
                SubmittedRequest,
                StartedUtc,
                CompletedUtc,
                Progress,
                Warnings,
                Errors,
                Timings.Concat(timings ?? []),
                SnapshotIdentity,
                PersistenceDiagnostics);
        }

        /// <summary>
        /// Creates a copy of this run with a replacement persistence diagnostic breakdown.
        /// </summary>
        /// <param name="persistenceDiagnostics">The persistence diagnostic breakdown to associate with this run.</param>
        /// <returns>A new run snapshot containing the supplied persistence diagnostics and all existing lifecycle details.</returns>
        public ExtractionRun WithPersistenceDiagnostics(ExtractionRunPersistenceDiagnostics? persistenceDiagnostics)
        {
            // Diagnostics are replaced as a unit because each persistence result represents the latest known breakdown for one persistence attempt.
            return new ExtractionRun(
                RunId,
                Status,
                SubmittedRequest,
                StartedUtc,
                CompletedUtc,
                Progress,
                Warnings,
                Errors,
                Timings,
                SnapshotIdentity,
                persistenceDiagnostics);
        }
    }
}
