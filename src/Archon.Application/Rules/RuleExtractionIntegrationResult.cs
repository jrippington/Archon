namespace Archon.Application.Rules
{
    /// <summary>
    /// Describes the extraction-stage outcome for WP012 rule catalog loading, persistence, and evaluation.
    /// </summary>
    public sealed class RuleExtractionIntegrationResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleExtractionIntegrationResult"/> class.
        /// </summary>
        /// <param name="loadedRuleCount">The number of validated rules loaded from copied output content.</param>
        /// <param name="upsertedRuleCount">The number of rules offered to catalog persistence.</param>
        /// <param name="evaluatedRuleCount">The number of enabled rules selected for evaluation.</param>
        /// <param name="matchCount">The number of satisfied rule matches returned by the evaluator.</param>
        /// <param name="warnings">The non-blocking diagnostics produced by catalog loading, persistence, graph projection, or evaluation.</param>
        public RuleExtractionIntegrationResult(int loadedRuleCount, int upsertedRuleCount, int evaluatedRuleCount, int matchCount, IEnumerable<string> warnings)
        {
            // Counts are captured separately so tests and run diagnostics can prove the load-upsert-evaluate sequence occurred.
            LoadedRuleCount = ValidateCount(loadedRuleCount, nameof(loadedRuleCount));
            UpsertedRuleCount = ValidateCount(upsertedRuleCount, nameof(upsertedRuleCount));
            EvaluatedRuleCount = ValidateCount(evaluatedRuleCount, nameof(evaluatedRuleCount));
            MatchCount = ValidateCount(matchCount, nameof(matchCount));
            Warnings = NormalizeWarnings(warnings);
        }

        /// <summary>
        /// Gets the number of validated rules loaded from copied output content.
        /// </summary>
        public int LoadedRuleCount { get; }

        /// <summary>
        /// Gets the number of rules offered to catalog persistence.
        /// </summary>
        public int UpsertedRuleCount { get; }

        /// <summary>
        /// Gets the number of enabled rules selected for evaluation.
        /// </summary>
        public int EvaluatedRuleCount { get; }

        /// <summary>
        /// Gets the number of satisfied rule matches returned by the evaluator.
        /// </summary>
        public int MatchCount { get; }

        /// <summary>
        /// Gets the non-blocking diagnostics produced by catalog loading, persistence, graph projection, or evaluation.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Validates a non-negative result count.
        /// </summary>
        /// <param name="count">The candidate count value.</param>
        /// <param name="parameterName">The parameter name used when reporting invalid input.</param>
        /// <returns>The validated count.</returns>
        private static int ValidateCount(int count, string parameterName)
        {
            // Negative counts would make extraction progress diagnostics misleading.
            return count < 0 ? throw new ArgumentOutOfRangeException(parameterName, "Counts cannot be negative.") : count;
        }

        /// <summary>
        /// Normalizes warning diagnostics into immutable text.
        /// </summary>
        /// <param name="warnings">The warning diagnostics to normalize.</param>
        /// <returns>A list of trimmed non-empty warning messages.</returns>
        private static IReadOnlyList<string> NormalizeWarnings(IEnumerable<string> warnings)
        {
            // Warning text is surfaced through extraction diagnostics, so blank entries are removed before storage.
            ArgumentNullException.ThrowIfNull(warnings);
            return warnings.Where(static warning => !string.IsNullOrWhiteSpace(warning)).Select(static warning => warning.Trim()).ToArray();
        }
    }
}
