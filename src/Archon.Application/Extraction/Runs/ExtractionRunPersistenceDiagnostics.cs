namespace Archon.Application.Extraction.Runs
{
    /// <summary>
    /// Carries persistence-specific diagnostic details for one extraction run status record.
    /// </summary>
    /// <remarks>
    /// The diagnostics section is optional on <see cref="ExtractionRun"/> so older or not-yet-persisting runs remain readable without synthetic data.
    /// </remarks>
    public sealed record ExtractionRunPersistenceDiagnostics
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExtractionRunPersistenceDiagnostics"/> record.
        /// </summary>
        /// <param name="timings">The ordered persistence sub-stage timings collected so far.</param>
        /// <param name="counts">The persistence volume and operation counts associated with the same run.</param>
        /// <param name="completed">A value indicating whether the diagnostic set represents a completed persistence attempt.</param>
        public ExtractionRunPersistenceDiagnostics(
            IEnumerable<ExtractionRunTiming>? timings,
            ExtractionRunPersistenceCounts counts,
            bool completed)
        {
            // Copying the collection preserves status immutability and retains the writer-provided completion order for API consumers.
            ArgumentNullException.ThrowIfNull(counts);
            Timings = timings?.ToArray() ?? [];
            Counts = counts;
            Completed = completed;
        }

        /// <summary>
        /// Gets the ordered persistence sub-stage timings collected so far.
        /// </summary>
        public IReadOnlyList<ExtractionRunTiming> Timings { get; }

        /// <summary>
        /// Gets the persistence volume and operation counts associated with the same run.
        /// </summary>
        public ExtractionRunPersistenceCounts Counts { get; }

        /// <summary>
        /// Gets a value indicating whether the diagnostic set represents a completed persistence attempt.
        /// </summary>
        public bool Completed { get; }
    }
}
