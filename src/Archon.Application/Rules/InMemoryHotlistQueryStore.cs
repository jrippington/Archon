using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Provides controlled in-memory query behavior for WP012 catalog and hotlist API tests and default composition.
    /// </summary>
    public sealed class InMemoryHotlistQueryStore : IHotlistQueryStore
    {
        /// <summary>
        /// Reads persisted rule catalog entries from the in-memory catalog store.
        /// </summary>
        private readonly IRuleCatalogStore _ruleCatalogStore;

        /// <summary>
        /// Reads persisted findings from the in-memory finding store.
        /// </summary>
        private readonly IFindingStore _findingStore;

        /// <summary>
        /// Initializes a new instance of the <see cref="InMemoryHotlistQueryStore"/> class.
        /// </summary>
        /// <param name="ruleCatalogStore">The rule catalog store used for catalog reads.</param>
        /// <param name="findingStore">The finding store used for finding reads.</param>
        public InMemoryHotlistQueryStore(IRuleCatalogStore ruleCatalogStore, IFindingStore findingStore)
        {
            // The query store composes existing in-memory ports so tests use the same contracts as production adapters.
            _ruleCatalogStore = ruleCatalogStore ?? throw new ArgumentNullException(nameof(ruleCatalogStore));
            _findingStore = findingStore ?? throw new ArgumentNullException(nameof(findingStore));
        }

        /// <summary>
        /// Retrieves persisted rules matching the supplied controlled catalog query.
        /// </summary>
        /// <param name="query">The controlled filter and paging contract for the catalog query.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>A bounded page of persisted rule catalog entries.</returns>
        public async Task<PagedQueryResult<RuleCatalogEntry>> QueryRulesAsync(RuleCatalogQuery query, CancellationToken cancellationToken)
        {
            // Filtering is field-by-field and deterministic; arbitrary predicates are not accepted by the contract.
            ArgumentNullException.ThrowIfNull(query);
            IReadOnlyList<RuleCatalogEntry> rules = await _ruleCatalogStore.GetRulesAsync(cancellationToken).ConfigureAwait(false);
            RuleCatalogEntry[] matches = rules
                .Where(rule => MatchesOptional(rule.RuleCode, query.RuleCode))
                .Where(rule => MatchesOptional(rule.Version, query.Version))
                .Where(rule => MatchesOptional(rule.Category.Value, query.Category))
                .Where(rule => MatchesOptional(rule.Severity.Value, query.Severity))
                .Where(rule => !query.Enabled.HasValue || rule.Enabled == query.Enabled.Value)
                .Where(rule => !query.BuiltIn.HasValue || rule.IsBuiltIn == query.BuiltIn.Value)
                .Where(rule => MatchesOptional(rule.OwnerScope, query.OwnerScope))
                .OrderBy(static rule => rule.RuleCode, StringComparer.Ordinal)
                .ThenBy(static rule => rule.Version, StringComparer.Ordinal)
                .ToArray();
            return new PagedQueryResult<RuleCatalogEntry>(matches.Skip(query.Skip).Take(query.Take), matches.Length, query.Skip, query.Take);
        }

        /// <summary>
        /// Retrieves one persisted rule by exact rule code and version.
        /// </summary>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="version">The exact rule version.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>The matching rule entry, or <see langword="null"/> when none exists.</returns>
        public async Task<RuleCatalogEntry?> GetRuleAsync(string ruleCode, string version, CancellationToken cancellationToken)
        {
            // Detail lookup is exact by rule code and version because those fields form rule catalog identity.
            RuleCatalogQuery query = new(RequireText(ruleCode, nameof(ruleCode)), RequireText(version, nameof(version)), null, null, null, null, null, 0, 1);
            PagedQueryResult<RuleCatalogEntry> result = await QueryRulesAsync(query, cancellationToken).ConfigureAwait(false);
            return result.Items.SingleOrDefault();
        }

        /// <summary>
        /// Retrieves persisted findings matching the supplied controlled hotlist query.
        /// </summary>
        /// <param name="query">The controlled filter and paging contract for the hotlist query.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>A bounded page of persisted finding records.</returns>
        public async Task<PagedQueryResult<FindingRecord>> QueryFindingsAsync(HotlistQuery query, CancellationToken cancellationToken)
        {
            // In-memory querying uses known snapshot reads; when no snapshot is supplied, it can only query in-memory stores that expose diagnostic snapshots.
            ArgumentNullException.ThrowIfNull(query);
            IReadOnlyList<FindingRecord> source = await GetFindingSourceAsync(query.SnapshotStableKey, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<RuleCatalogEntry> rules = await _ruleCatalogStore.GetRulesAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<string, RuleCatalogEntry> ruleIndex = rules.ToDictionary(static rule => BuildRuleKey(rule.RuleCode, rule.Version), StringComparer.Ordinal);
            FindingRecord[] matches = source
                .Where(finding => MatchesOptional(GetRuleCategory(ruleIndex, finding), query.Category))
                .Where(finding => MatchesOptional(finding.Severity.Value, query.Severity))
                .Where(finding => !query.CriticalOnly.HasValue || !query.CriticalOnly.Value || StringComparer.Ordinal.Equals(finding.Severity.Value, FindingSeverity.Critical.Value))
                .Where(finding => MatchesOptional(finding.Status.Value, query.Status))
                .Where(finding => MatchesOptional(finding.RuleCode, query.RuleCode))
                .Where(finding => MatchesOptional(ReadMetadataText(finding, "projectStableKey"), query.ProjectStableKey))
                .Where(finding => MatchesMetadataIndicator(finding, "legacyDataAccess", query.LegacyDataAccess))
                .Where(finding => MatchesMetadataIndicator(finding, "outOfSupport", query.OutOfSupport))
                .Where(finding => MatchesMetadataIndicator(finding, "securitySensitive", query.SecuritySensitive))
                .Where(finding => MatchesMetadataIndicator(finding, "frameworkOnly", query.FrameworkOnly))
                .Where(finding => MatchesOptional(ReadMetadataText(finding, "technology"), query.Technology) || MatchesOptional(ReadMetadataText(finding, "technologyFamily"), query.Technology))
                .Where(finding => query.AffectedNodeStableKey is null || finding.AffectedNodeStableKeys.Any(key => StringComparer.Ordinal.Equals(key.Value, query.AffectedNodeStableKey)))
                .OrderByDescending(static finding => finding.Severity.Value, StringComparer.Ordinal)
                .ThenBy(static finding => finding.RuleCode, StringComparer.Ordinal)
                .ThenBy(static finding => finding.StableKey.Value, StringComparer.Ordinal)
                .ToArray();
            return new PagedQueryResult<FindingRecord>(matches.Skip(query.Skip).Take(query.Take), matches.Length, query.Skip, query.Take);
        }

        /// <summary>
        /// Retrieves historical finding records for one cross-snapshot history key.
        /// </summary>
        /// <param name="historyKey">The deterministic finding history key.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>The historical finding records in deterministic snapshot order.</returns>
        public async Task<IReadOnlyList<FindingRecord>> GetFindingHistoryRecordsAsync(string historyKey, CancellationToken cancellationToken)
        {
            // History records are returned by stores that can enumerate all known findings; otherwise the result is empty rather than unbounded.
            string normalizedHistoryKey = RequireText(historyKey, nameof(historyKey));
            IReadOnlyList<FindingRecord> source = await GetFindingSourceAsync(snapshotStableKey: null, cancellationToken).ConfigureAwait(false);
            return source
                .Where(finding => StringComparer.Ordinal.Equals(finding.HistoryKey, normalizedHistoryKey))
                .OrderBy(static finding => finding.SnapshotStableKey.Value, StringComparer.Ordinal)
                .ThenBy(static finding => finding.StableKey.Value, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Gets a finding source for query evaluation.
        /// </summary>
        /// <param name="snapshotStableKey">The optional snapshot stable key requested by the query.</param>
        /// <param name="cancellationToken">The cancellation token for store reads.</param>
        /// <returns>A deterministic finding sequence available to the in-memory query path.</returns>
        private async Task<IReadOnlyList<FindingRecord>> GetFindingSourceAsync(string? snapshotStableKey, CancellationToken cancellationToken)
        {
            // The IFindingStore contract provides snapshot reads; the in-memory implementation adds a diagnostic all-snapshot path for query API tests.
            if (!string.IsNullOrWhiteSpace(snapshotStableKey))
            {
                return await _findingStore.GetFindingsBySnapshotAsync(snapshotStableKey, cancellationToken).ConfigureAwait(false);
            }

            if (_findingStore is InMemoryFindingStore inMemoryStore)
            {
                return inMemoryStore.GetFindingsSnapshotForDiagnostics();
            }

            return [];
        }

        /// <summary>
        /// Compares optional exact filter text using ordinal semantics.
        /// </summary>
        /// <param name="actual">The actual stored value.</param>
        /// <param name="expected">The optional expected filter value.</param>
        /// <returns><see langword="true"/> when the expected filter is absent or matches the actual value.</returns>
        private static bool MatchesOptional(string? actual, string? expected)
        {
            // All WP012 filters are exact controlled filters; substring and arbitrary pattern matching are intentionally not supported here.
            return expected is null || StringComparer.Ordinal.Equals(actual, expected);
        }

        /// <summary>
        /// Compares an optional boolean indicator filter against a finding metadata value.
        /// </summary>
        /// <param name="finding">The finding whose metadata should be inspected.</param>
        /// <param name="metadataName">The lower camel case metadata property name to inspect.</param>
        /// <param name="expected">The optional expected boolean value.</param>
        /// <returns><see langword="true"/> when the filter is absent or the metadata indicator matches the requested value.</returns>
        private static bool MatchesMetadataIndicator(FindingRecord finding, string metadataName, bool? expected)
        {
            // Work Item 8 uses specific modernization indicator filters rather than arbitrary metadata predicate evaluation.
            return !expected.HasValue || ReadMetadataBoolean(finding, metadataName) == expected.Value;
        }

        /// <summary>
        /// Reads a lower camel case boolean metadata value from a finding.
        /// </summary>
        /// <param name="finding">The finding containing metadata JSON.</param>
        /// <param name="metadataName">The stable lower camel case metadata property name.</param>
        /// <returns>The metadata boolean value, or <see langword="false"/> when absent or not a boolean.</returns>
        private static bool ReadMetadataBoolean(FindingRecord finding, string metadataName)
        {
            // Boolean filters are intentionally limited to known indicator fields used by the public hotlist API.
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(finding.Metadata.ToCanonicalJson());
            return document.RootElement.TryGetProperty(metadataName, out System.Text.Json.JsonElement value) && value.ValueKind == System.Text.Json.JsonValueKind.True;
        }

        /// <summary>
        /// Builds the private rule lookup key used while joining findings to catalog metadata.
        /// </summary>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="version">The exact rule version.</param>
        /// <returns>A deterministic lookup key.</returns>
        private static string BuildRuleKey(string ruleCode, string version)
        {
            // The delimiter is private to this index; public contracts keep code and version separate.
            return string.Concat(ruleCode, "\u001F", version);
        }

        /// <summary>
        /// Reads a finding's rule category from the joined catalog entry when available.
        /// </summary>
        /// <param name="ruleIndex">The rule index keyed by code and version.</param>
        /// <param name="finding">The finding whose category should be resolved.</param>
        /// <returns>The category value, or <see langword="null"/> when no catalog rule is available.</returns>
        private static string? GetRuleCategory(IReadOnlyDictionary<string, RuleCatalogEntry> ruleIndex, FindingRecord finding)
        {
            // Missing catalog records are tolerated so findings remain queryable even when rule catalog history is incomplete in a test fixture.
            return ruleIndex.TryGetValue(BuildRuleKey(finding.RuleCode, finding.RuleVersion), out RuleCatalogEntry? rule) ? rule.Category.Value : null;
        }

        /// <summary>
        /// Reads a lower camel case string metadata value from a finding.
        /// </summary>
        /// <param name="finding">The finding containing metadata JSON.</param>
        /// <param name="metadataName">The stable lower camel case metadata property name.</param>
        /// <returns>The metadata text value, or <see langword="null"/> when absent.</returns>
        private static string? ReadMetadataText(FindingRecord finding, string metadataName)
        {
            // Metadata filtering is intentionally limited to stable lower camel case fields already produced by Archon, not arbitrary JSONPath.
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(finding.Metadata.ToCanonicalJson());
            return document.RootElement.TryGetProperty(metadataName, out System.Text.Json.JsonElement value) && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;
        }

        /// <summary>
        /// Requires a non-empty text value and returns its trimmed form.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <param name="parameterName">The parameter name used for validation failures.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Store lookups need explicit identities so query results are never ambiguous.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }
}
