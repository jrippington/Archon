namespace Archon.Application.Rules
{
    /// <summary>
    /// Describes the outcome of persisting a validated WP012 rule catalog through an application-layer port.
    /// </summary>
    public sealed class RuleCatalogUpsertResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleCatalogUpsertResult"/> class.
        /// </summary>
        /// <param name="succeeded">A value indicating whether the catalog upsert completed without blocking errors.</param>
        /// <param name="upsertedRuleCount">The number of versioned rule records offered to the persistence adapter.</param>
        /// <param name="warnings">The non-blocking diagnostics produced while persisting the catalog.</param>
        /// <param name="errors">The blocking diagnostics produced while persisting the catalog.</param>
        private RuleCatalogUpsertResult(bool succeeded, int upsertedRuleCount, IEnumerable<string> warnings, IEnumerable<string> errors)
        {
            // The result carries credential-safe text diagnostics so extraction orchestration can surface failures without exposing driver details.
            if (upsertedRuleCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(upsertedRuleCount), "The upserted rule count cannot be negative.");
            }

            Succeeded = succeeded;
            UpsertedRuleCount = upsertedRuleCount;
            Warnings = NormalizeDiagnostics(warnings);
            Errors = NormalizeDiagnostics(errors);
        }

        /// <summary>
        /// Gets a value indicating whether the catalog upsert completed without blocking errors.
        /// </summary>
        public bool Succeeded { get; }

        /// <summary>
        /// Gets the number of versioned rule records offered to the persistence adapter.
        /// </summary>
        public int UpsertedRuleCount { get; }

        /// <summary>
        /// Gets the non-blocking diagnostics produced while persisting the catalog.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Gets the blocking diagnostics produced while persisting the catalog.
        /// </summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>
        /// Creates a successful catalog upsert result.
        /// </summary>
        /// <param name="upsertedRuleCount">The number of versioned rule records offered to the persistence adapter.</param>
        /// <param name="warnings">The optional non-blocking diagnostics produced while persisting the catalog.</param>
        /// <returns>A successful upsert result.</returns>
        public static RuleCatalogUpsertResult Success(int upsertedRuleCount, IEnumerable<string>? warnings = null)
        {
            // Successful upserts may still include warnings from schema initialization or adapter-level non-blocking observations.
            return new RuleCatalogUpsertResult(succeeded: true, upsertedRuleCount, warnings ?? [], []);
        }

        /// <summary>
        /// Creates a failed catalog upsert result.
        /// </summary>
        /// <param name="errors">The blocking diagnostics that explain why persistence failed.</param>
        /// <param name="warnings">The optional non-blocking diagnostics produced before the failure.</param>
        /// <returns>A failed upsert result.</returns>
        public static RuleCatalogUpsertResult Failure(IEnumerable<string> errors, IEnumerable<string>? warnings = null)
        {
            // Failures use a zero count because callers cannot rely on partial adapter writes after a blocking persistence diagnostic.
            ArgumentNullException.ThrowIfNull(errors);
            return new RuleCatalogUpsertResult(succeeded: false, upsertedRuleCount: 0, warnings ?? [], errors);
        }

        /// <summary>
        /// Normalizes diagnostic text into a deterministic immutable list.
        /// </summary>
        /// <param name="diagnostics">The diagnostic messages to normalize.</param>
        /// <returns>A list of trimmed non-empty diagnostic messages.</returns>
        private static IReadOnlyList<string> NormalizeDiagnostics(IEnumerable<string> diagnostics)
        {
            // Blank diagnostics do not help API callers or contributors understand catalog persistence behavior.
            ArgumentNullException.ThrowIfNull(diagnostics);
            return diagnostics.Where(static diagnostic => !string.IsNullOrWhiteSpace(diagnostic)).Select(static diagnostic => diagnostic.Trim()).ToArray();
        }
    }
}
