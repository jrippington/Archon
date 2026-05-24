namespace Archon.Application.Rules
{
    /// <summary>
    /// Stores validated rule catalog records in memory for tests and default extraction composition without an external database.
    /// </summary>
    public sealed class InMemoryRuleCatalogStore : IRuleCatalogStore
    {
        /// <summary>
        /// Stores versioned rule catalog entries by rule code and version using ordinal comparison for deterministic replacement.
        /// </summary>
        private readonly Dictionary<string, RuleCatalogEntry> _rules = new(StringComparer.Ordinal);

        /// <summary>
        /// Protects the in-memory catalog from concurrent extraction runs mutating the dictionary at the same time.
        /// </summary>
        private readonly object _syncRoot = new();

        /// <summary>
        /// Upserts validated rule catalog entries by stable rule code and exact rule version.
        /// </summary>
        /// <param name="rules">The validated catalog entries to persist as versioned catalog records.</param>
        /// <param name="cancellationToken">The cancellation token that can stop persistence before the in-memory store is updated.</param>
        /// <returns>A successful result with the number of versioned entries offered to the store.</returns>
        public Task<RuleCatalogUpsertResult> UpsertRulesAsync(IEnumerable<RuleCatalogEntry> rules, CancellationToken cancellationToken)
        {
            // The in-memory store mirrors Neo4j merge identity: existing code/version entries are replaced, while new versions coexist.
            ArgumentNullException.ThrowIfNull(rules);
            cancellationToken.ThrowIfCancellationRequested();
            RuleCatalogEntry[] entries = rules.ToArray();
            lock (_syncRoot)
            {
                foreach (RuleCatalogEntry rule in entries)
                {
                    _rules[BuildRuleVersionKey(rule.RuleCode, rule.Version)] = rule;
                }
            }

            return Task.FromResult(RuleCatalogUpsertResult.Success(entries.Length));
        }

        /// <summary>
        /// Retrieves persisted rule catalog entries in deterministic rule code and version order.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before the in-memory snapshot is copied.</param>
        /// <returns>The in-memory persisted rule catalog entries.</returns>
        public Task<IReadOnlyList<RuleCatalogEntry>> GetRulesAsync(CancellationToken cancellationToken)
        {
            // A copied, sorted snapshot prevents callers from observing dictionary mutation while reading persisted catalog state.
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(GetRulesSnapshotForDiagnostics());
        }

        /// <summary>
        /// Retrieves a deterministic snapshot of persisted rule catalog entries for diagnostics.
        /// </summary>
        /// <returns>A copied in-memory snapshot sorted by rule code and version.</returns>
        internal IReadOnlyList<RuleCatalogEntry> GetRulesSnapshotForDiagnostics()
        {
            lock (_syncRoot)
            {
                return _rules.Values
                    .OrderBy(static rule => rule.RuleCode, StringComparer.Ordinal)
                    .ThenBy(static rule => rule.Version, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        /// <summary>
        /// Builds the private composite identity used by the in-memory rule catalog dictionary.
        /// </summary>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="ruleVersion">The exact rule version.</param>
        /// <returns>A deterministic composite key for in-memory replacement.</returns>
        private static string BuildRuleVersionKey(string ruleCode, string ruleVersion)
        {
            // The separator is private to the store; external contracts continue to expose rule code and version as separate fields.
            return string.Concat(ruleCode, "\u001F", ruleVersion);
        }
    }
}
