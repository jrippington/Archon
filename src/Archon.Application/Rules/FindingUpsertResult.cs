namespace Archon.Application.Rules
{
    /// <summary>
    /// Describes the outcome of persisting WP012 findings through an application-layer port.
    /// </summary>
    public sealed class FindingUpsertResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FindingUpsertResult"/> class.
        /// </summary>
        /// <param name="succeeded">A value indicating whether the finding upsert completed without blocking errors.</param>
        /// <param name="upsertedFindingCount">The number of finding records offered to the persistence adapter.</param>
        /// <param name="warnings">The non-blocking diagnostics produced while persisting findings.</param>
        /// <param name="errors">The blocking diagnostics produced while persisting findings.</param>
        private FindingUpsertResult(bool succeeded, int upsertedFindingCount, IEnumerable<string> warnings, IEnumerable<string> errors)
        {
            // The result mirrors catalog persistence so extraction can report finding persistence consistently.
            if (upsertedFindingCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(upsertedFindingCount), "The upserted finding count cannot be negative.");
            }

            Succeeded = succeeded;
            UpsertedFindingCount = upsertedFindingCount;
            Warnings = NormalizeDiagnostics(warnings);
            Errors = NormalizeDiagnostics(errors);
        }

        /// <summary>
        /// Gets a value indicating whether the finding upsert completed without blocking errors.
        /// </summary>
        public bool Succeeded { get; }

        /// <summary>
        /// Gets the number of finding records offered to the persistence adapter.
        /// </summary>
        public int UpsertedFindingCount { get; }

        /// <summary>
        /// Gets the non-blocking diagnostics produced while persisting findings.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Gets the blocking diagnostics produced while persisting findings.
        /// </summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>
        /// Creates a successful finding upsert result.
        /// </summary>
        /// <param name="upsertedFindingCount">The number of finding records offered to the persistence adapter.</param>
        /// <param name="warnings">The optional non-blocking diagnostics produced while persisting findings.</param>
        /// <returns>A successful finding upsert result.</returns>
        public static FindingUpsertResult Success(int upsertedFindingCount, IEnumerable<string>? warnings = null)
        {
            // Successful writes can still carry non-blocking warnings from adapter validation or schema initialization.
            return new FindingUpsertResult(succeeded: true, upsertedFindingCount, warnings ?? [], []);
        }

        /// <summary>
        /// Creates a failed finding upsert result.
        /// </summary>
        /// <param name="errors">The blocking diagnostics that explain why persistence failed.</param>
        /// <param name="warnings">The optional non-blocking diagnostics produced before the failure.</param>
        /// <returns>A failed finding upsert result.</returns>
        public static FindingUpsertResult Failure(IEnumerable<string> errors, IEnumerable<string>? warnings = null)
        {
            // Failed writes use a zero count because callers should not rely on partial adapter side effects.
            ArgumentNullException.ThrowIfNull(errors);
            return new FindingUpsertResult(succeeded: false, upsertedFindingCount: 0, warnings ?? [], errors);
        }

        /// <summary>
        /// Normalizes diagnostic text into a deterministic immutable list.
        /// </summary>
        /// <param name="diagnostics">The diagnostic messages to normalize.</param>
        /// <returns>A list of trimmed non-empty diagnostic messages.</returns>
        private static IReadOnlyList<string> NormalizeDiagnostics(IEnumerable<string> diagnostics)
        {
            // Blank diagnostics do not explain persistence behavior and are omitted.
            ArgumentNullException.ThrowIfNull(diagnostics);
            return diagnostics.Where(static diagnostic => !string.IsNullOrWhiteSpace(diagnostic)).Select(static diagnostic => diagnostic.Trim()).ToArray();
        }
    }
}
