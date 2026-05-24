namespace Archon.Application.Rules
{
    /// <summary>
    /// Carries the evaluator output and optional history context needed to create snapshot-owned finding records.
    /// </summary>
    public sealed class FindingConstructionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FindingConstructionRequest"/> class.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the findings being constructed.</param>
        /// <param name="rules">The validated rules available for matching by rule code and version.</param>
        /// <param name="matches">The satisfied evaluator matches that should become findings.</param>
        /// <param name="unknownStates">The evaluator unknown-state contexts that should be preserved in finding output.</param>
        public FindingConstructionRequest(
            string snapshotStableKey,
            IEnumerable<RuleCatalogEntry> rules,
            IEnumerable<RuleEvaluationMatch> matches,
            IEnumerable<RuleEvaluationUnknownState> unknownStates)
            : this(snapshotStableKey, rules, matches, unknownStates, [])
        {
            // This overload keeps callers concise when no previous history context is available for a first snapshot.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FindingConstructionRequest"/> class.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the findings being constructed.</param>
        /// <param name="rules">The validated rules available for matching by rule code and version.</param>
        /// <param name="matches">The satisfied evaluator matches that should become findings.</param>
        /// <param name="unknownStates">The evaluator unknown-state contexts that should be preserved in finding output.</param>
        /// <param name="historySeeds">The known cross-snapshot history records used to populate first-seen data.</param>
        public FindingConstructionRequest(
            string snapshotStableKey,
            IEnumerable<RuleCatalogEntry> rules,
            IEnumerable<RuleEvaluationMatch> matches,
            IEnumerable<RuleEvaluationUnknownState> unknownStates,
            IEnumerable<FindingHistorySeed> historySeeds)
        {
            // Construction normalizes sequences immediately so finding construction is deterministic and immune to caller mutation.
            SnapshotStableKey = RequireText(snapshotStableKey, nameof(snapshotStableKey));
            Rules = CopyRules(rules);
            Matches = CopyMatches(matches);
            UnknownStates = CopyUnknownStates(unknownStates);
            HistorySeeds = CopyHistorySeeds(historySeeds);
        }

        /// <summary>
        /// Gets the stable key of the snapshot that owns the findings being constructed.
        /// </summary>
        public string SnapshotStableKey { get; }

        /// <summary>
        /// Gets the validated rules available for matching by rule code and version.
        /// </summary>
        public IReadOnlyList<RuleCatalogEntry> Rules { get; }

        /// <summary>
        /// Gets the satisfied evaluator matches that should become findings.
        /// </summary>
        public IReadOnlyList<RuleEvaluationMatch> Matches { get; }

        /// <summary>
        /// Gets the evaluator unknown-state contexts that should be preserved in finding output.
        /// </summary>
        public IReadOnlyList<RuleEvaluationUnknownState> UnknownStates { get; }

        /// <summary>
        /// Gets the known cross-snapshot history records used to populate first-seen data.
        /// </summary>
        public IReadOnlyList<FindingHistorySeed> HistorySeeds { get; }

        /// <summary>
        /// Requires non-empty text and returns its trimmed form.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <param name="parameterName">The parameter name used in validation failures.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Snapshot identity is required because findings are persisted as snapshot-scoped records.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }

        /// <summary>
        /// Copies and orders rule entries by rule identity.
        /// </summary>
        /// <param name="rules">The source rule entries to copy.</param>
        /// <returns>A deterministic rule entry list.</returns>
        private static IReadOnlyList<RuleCatalogEntry> CopyRules(IEnumerable<RuleCatalogEntry> rules)
        {
            // Rule lookup later uses code/version, and ordering makes duplicate diagnostics deterministic.
            ArgumentNullException.ThrowIfNull(rules);
            return rules.OrderBy(static rule => rule.RuleCode, StringComparer.Ordinal).ThenBy(static rule => rule.Version, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Copies and orders evaluation matches by rule and primary node identity.
        /// </summary>
        /// <param name="matches">The source matches to copy.</param>
        /// <returns>A deterministic match list.</returns>
        private static IReadOnlyList<RuleEvaluationMatch> CopyMatches(IEnumerable<RuleEvaluationMatch> matches)
        {
            // Deterministic match order ensures duplicate handling keeps the same first record across machines.
            ArgumentNullException.ThrowIfNull(matches);
            return matches.OrderBy(static match => match.RuleCode, StringComparer.Ordinal).ThenBy(static match => match.RuleVersion, StringComparer.Ordinal).ThenBy(static match => match.PrimaryNodeStableKey, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Copies and orders unknown-state contexts by rule and node identity.
        /// </summary>
        /// <param name="unknownStates">The source unknown-state contexts to copy.</param>
        /// <returns>A deterministic unknown-state list.</returns>
        private static IReadOnlyList<RuleEvaluationUnknownState> CopyUnknownStates(IEnumerable<RuleEvaluationUnknownState> unknownStates)
        {
            // Unknown contexts are matched by rule and node when constructing finding uncertainty.
            ArgumentNullException.ThrowIfNull(unknownStates);
            return unknownStates.OrderBy(static unknown => unknown.RuleCode, StringComparer.Ordinal).ThenBy(static unknown => unknown.NodeStableKey, StringComparer.Ordinal).ThenBy(static unknown => unknown.Reason, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Copies and orders history seeds by history key.
        /// </summary>
        /// <param name="historySeeds">The source history seeds to copy.</param>
        /// <returns>A deterministic history seed list.</returns>
        private static IReadOnlyList<FindingHistorySeed> CopyHistorySeeds(IEnumerable<FindingHistorySeed> historySeeds)
        {
            // History seeds are optional but deterministic ordering makes repeated construction stable.
            ArgumentNullException.ThrowIfNull(historySeeds);
            return historySeeds.OrderBy(static seed => seed.HistoryKey, StringComparer.Ordinal).ToArray();
        }
    }
}
