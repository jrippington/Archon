using System.Text.Json;
using Archon.Application.Rules;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Infrastructure.Neo4j.Driver;
using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.Persistence
{
    /// <summary>
    /// Implements controlled WP012 rule catalog and hotlist read queries over Neo4j without exposing arbitrary Cypher to callers.
    /// </summary>
    public sealed class Neo4jHotlistQueryStore : IHotlistQueryStore
    {
        /// <summary>
        /// Opens Neo4j sessions for controlled query reads.
        /// </summary>
        private readonly INeo4jSessionProvider _sessionProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jHotlistQueryStore"/> class.
        /// </summary>
        /// <param name="sessionProvider">The session provider used to open Neo4j read sessions.</param>
        public Neo4jHotlistQueryStore(INeo4jSessionProvider sessionProvider)
        {
            // The query store accepts only the session provider because all query text is static and parameterized in this adapter.
            _sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
        }

        /// <summary>
        /// Retrieves persisted rules matching the supplied controlled catalog query.
        /// </summary>
        /// <param name="query">The controlled filter and paging contract for the catalog query.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before query execution.</param>
        /// <returns>A bounded page of persisted rule catalog entries.</returns>
        public async Task<PagedQueryResult<RuleCatalogEntry>> QueryRulesAsync(RuleCatalogQuery query, CancellationToken cancellationToken)
        {
            // Cypher is static and parameterized; callers can only influence values for approved filters and page bounds.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Read);
            IReadOnlyDictionary<string, object?> parameters = MapRuleQueryParameters(query);
            IResultCursor countCursor = await session.RunAsync(RuleQueryCountCypher, parameters).ConfigureAwait(false);
            int totalCount = Convert.ToInt32((await countCursor.SingleAsync().ConfigureAwait(false))["totalCount"].As<long>(), System.Globalization.CultureInfo.InvariantCulture);
            IResultCursor cursor = await session.RunAsync(RuleQueryCypher, parameters).ConfigureAwait(false);
            IReadOnlyList<IRecord> records = await cursor.ToListAsync().ConfigureAwait(false);
            return new PagedQueryResult<RuleCatalogEntry>(records.Select(MapRuleCatalogEntry), totalCount, query.Skip, query.Take);
        }

        /// <summary>
        /// Retrieves one persisted rule by exact rule code and version.
        /// </summary>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="version">The exact rule version.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before query execution.</param>
        /// <returns>The matching rule entry, or <see langword="null"/> when none exists.</returns>
        public async Task<RuleCatalogEntry?> GetRuleAsync(string ruleCode, string version, CancellationToken cancellationToken)
        {
            // Exact detail lookup is implemented by reusing the list query so filter semantics remain identical.
            RuleCatalogQuery query = new(RequireText(ruleCode, nameof(ruleCode)), RequireText(version, nameof(version)), null, null, null, null, null, 0, 1);
            PagedQueryResult<RuleCatalogEntry> result = await QueryRulesAsync(query, cancellationToken).ConfigureAwait(false);
            return result.Items.SingleOrDefault();
        }

        /// <summary>
        /// Retrieves persisted findings matching the supplied controlled hotlist query.
        /// </summary>
        /// <param name="query">The controlled filter and paging contract for the hotlist query.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before query execution.</param>
        /// <returns>A bounded page of persisted finding records.</returns>
        public async Task<PagedQueryResult<FindingRecord>> QueryFindingsAsync(HotlistQuery query, CancellationToken cancellationToken)
        {
            // Hotlist query text never includes caller-provided fragments; all filters are optional static predicates.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Read);
            IReadOnlyDictionary<string, object?> parameters = MapHotlistQueryParameters(query);
            IResultCursor countCursor = await session.RunAsync(HotlistQueryCountCypher, parameters).ConfigureAwait(false);
            int totalCount = Convert.ToInt32((await countCursor.SingleAsync().ConfigureAwait(false))["totalCount"].As<long>(), System.Globalization.CultureInfo.InvariantCulture);
            IResultCursor cursor = await session.RunAsync(HotlistQueryCypher, parameters).ConfigureAwait(false);
            IReadOnlyList<IRecord> records = await cursor.ToListAsync().ConfigureAwait(false);
            return new PagedQueryResult<FindingRecord>(records.Select(MapFindingRecord), totalCount, query.Skip, query.Take);
        }

        /// <summary>
        /// Retrieves historical finding records for one cross-snapshot history key.
        /// </summary>
        /// <param name="historyKey">The deterministic finding history key.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before query execution.</param>
        /// <returns>The historical finding records in deterministic snapshot order.</returns>
        public async Task<IReadOnlyList<FindingRecord>> GetFindingHistoryRecordsAsync(string historyKey, CancellationToken cancellationToken)
        {
            // History uses the stable history key property and never traverses by database-local node identifiers.
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Read);
            IResultCursor cursor = await session.RunAsync(FindingHistoryRecordsCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["historyKey"] = RequireText(historyKey, nameof(historyKey))
            }).ConfigureAwait(false);
            IReadOnlyList<IRecord> records = await cursor.ToListAsync().ConfigureAwait(false);
            return records.Select(MapFindingRecord).ToArray();
        }

        /// <summary>
        /// Maps rule query filter and paging values to Neo4j parameters.
        /// </summary>
        /// <param name="query">The controlled rule query.</param>
        /// <returns>The parameter dictionary used by static rule query Cypher.</returns>
        private static IReadOnlyDictionary<string, object?> MapRuleQueryParameters(RuleCatalogQuery query)
        {
            // Parameters are simple scalar values so user input cannot alter the Cypher structure.
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["ruleCode"] = query.RuleCode,
                ["version"] = query.Version,
                ["category"] = query.Category,
                ["severity"] = query.Severity,
                ["enabled"] = query.Enabled,
                ["builtIn"] = query.BuiltIn,
                ["ownerScope"] = query.OwnerScope,
                ["skip"] = query.Skip,
                ["take"] = query.Take
            };
        }

        /// <summary>
        /// Maps hotlist query filter and paging values to Neo4j parameters.
        /// </summary>
        /// <param name="query">The controlled hotlist query.</param>
        /// <returns>The parameter dictionary used by static hotlist query Cypher.</returns>
        private static IReadOnlyDictionary<string, object?> MapHotlistQueryParameters(HotlistQuery query)
        {
            // The project filter is matched against finding metadata and linked node project properties when those fields exist.
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["snapshotStableKey"] = query.SnapshotStableKey,
                ["category"] = query.Category,
                ["severity"] = query.Severity,
                ["status"] = query.Status,
                ["projectStableKey"] = query.ProjectStableKey,
                ["affectedNodeStableKey"] = query.AffectedNodeStableKey,
                ["skip"] = query.Skip,
                ["take"] = query.Take
            };
        }

        /// <summary>
        /// Maps a Neo4j rule record into an application rule catalog entry.
        /// </summary>
        /// <param name="record">The Neo4j record containing rule properties.</param>
        /// <returns>The application rule catalog entry.</returns>
        private static RuleCatalogEntry MapRuleCatalogEntry(IRecord record)
        {
            // Query projection preserves the normalized definition JSON while rebuilding an empty detection group for DTO consumers that do not evaluate rules.
            IReadOnlyDictionary<string, object> rule = record["rule"].As<IReadOnlyDictionary<string, object>>();
            return new RuleCatalogEntry(
                ReadString(rule, "ruleCode"),
                ReadString(rule, "name"),
                RuleCategory.Parse(ReadString(rule, "category")),
                FindingSeverity.Parse(ReadString(rule, "severity")),
                RuleFindingStatus.Parse(ReadString(rule, "defaultStatus")),
                ReadBoolean(rule, "enabled"),
                ReadString(rule, "ruleVersion"),
                ReadString(rule, "description"),
                ReadString(rule, "definitionJson"),
                ReadJsonStringArray(rule, "sourceUrlsJson"),
                ReadBoolean(rule, "isBuiltIn"),
                ReadNullableString(rule, "ownerScope"),
                [],
                [],
                [],
                [],
                ReadMetadata(rule, "metadataJson"),
                new RuleDetectionGroup([], RuleDetectionMatch.MatchAll, [], []),
                "neo4j://rule-catalog");
        }

        /// <summary>
        /// Maps a Neo4j finding record into a domain finding record.
        /// </summary>
        /// <param name="record">The Neo4j record containing finding properties.</param>
        /// <returns>The domain finding record.</returns>
        private static FindingRecord MapFindingRecord(IRecord record)
        {
            // Finding projection mirrors Neo4jFindingStore so query APIs and persistence reads interpret properties consistently.
            IReadOnlyDictionary<string, object> finding = record["finding"].As<IReadOnlyDictionary<string, object>>();
            return new FindingRecord(
                new StableKey(ReadString(finding, "snapshotStableKey")),
                new StableKey(ReadString(finding, "stableKey")),
                ReadString(finding, "ruleCode"),
                ReadString(finding, "ruleVersion"),
                FindingSeverity.Parse(ReadString(finding, "severity")),
                FindingStatus.Parse(ReadString(finding, "status")),
                ReadString(finding, "title"),
                ReadString(finding, "description"),
                KnowledgeKind.Parse(ReadString(finding, "knowledgeKind")),
                new Confidence(ReadDecimal(finding, "confidence")),
                new UnknownState(ReadBoolean(finding, "hasUnknownData"), ReadNullableString(finding, "unknownReason")),
                ReadOptionalStableKey(finding, "primaryNodeStableKey"),
                ReadOptionalStableKey(finding, "primaryEvidenceStableKey"),
                ReadOptionalStableKey(finding, "firstSeenSnapshotStableKey"),
                ReadOptionalStableKey(finding, "latestSeenSnapshotStableKey"),
                ReadNullableString(finding, "suppressionReason"),
                ReadNullableString(finding, "suppressedBy"),
                ReadStableKeyArray(finding, "affectedNodeStableKeys"),
                ReadStableKeyArray(finding, "evidenceStableKeys"),
                ReadString(finding, "historyKey"),
                ReadMetadata(finding, "metadataJson"),
                new Fingerprint(ReadString(finding, "fingerprint")));
        }

        /// <summary>
        /// Reads and parses graph metadata JSON from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property containing metadata JSON.</param>
        /// <returns>The parsed canonical graph metadata.</returns>
        private static GraphMetadata ReadMetadata(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Metadata is reconstructed through the domain factory so lower camel case and reserved-name validation still apply.
            string json = values.TryGetValue(propertyName, out object? value) && value is not null ? value.As<string>() : "{}";
            using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            Dictionary<string, object?> metadataValues = new(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                metadataValues[property.Name] = property.Value.Clone();
            }

            return metadataValues.Count == 0 ? GraphMetadata.Empty : GraphMetadata.From(metadataValues);
        }

        /// <summary>
        /// Reads a required string property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>The non-empty string property value.</returns>
        private static string ReadString(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Required persisted properties must be present because they map to non-null application or domain fields.
            return values.TryGetValue(propertyName, out object? value) && value is not null && !string.IsNullOrWhiteSpace(value.As<string>())
                ? value.As<string>()
                : throw new InvalidOperationException($"Neo4j query projection is missing required property '{propertyName}'.");
        }

        /// <summary>
        /// Reads an optional string property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>The optional string property value.</returns>
        private static string? ReadNullableString(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Neo4j omits null-valued properties, so missing and null both map to null.
            return values.TryGetValue(propertyName, out object? value) && value is not null ? value.As<string>() : null;
        }

        /// <summary>
        /// Reads a boolean property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>The boolean property value.</returns>
        private static bool ReadBoolean(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Missing booleans default to false only for optional persisted flags.
            return values.TryGetValue(propertyName, out object? value) && value is not null && value.As<bool>();
        }

        /// <summary>
        /// Reads a decimal property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>The decimal value.</returns>
        private static decimal ReadDecimal(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Neo4j numeric values are converted invariantly to preserve confidence precision.
            object value = values[propertyName];
            return decimal.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads an optional stable key property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>The stable key, or <see langword="null"/> when absent.</returns>
        private static StableKey? ReadOptionalStableKey(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Optional links may be absent when a finding has no primary node or evidence.
            string? value = ReadNullableString(values, propertyName);
            return string.IsNullOrWhiteSpace(value) ? null : new StableKey(value);
        }

        /// <summary>
        /// Reads a stable-key array property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>A deterministic stable-key list.</returns>
        private static IReadOnlyList<StableKey> ReadStableKeyArray(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Stable-key arrays preserve multi-node and multi-evidence relationships for query responses.
            if (!values.TryGetValue(propertyName, out object? value) || value is null)
            {
                return [];
            }

            return value.As<List<object>>()
                .Select(static item => item.As<string>())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => new StableKey(item))
                .OrderBy(static stableKey => stableKey.Value, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Reads a JSON string-array property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The JSON array property to parse.</param>
        /// <returns>A deterministic text array.</returns>
        private static IReadOnlyList<string> ReadJsonStringArray(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Rule catalog source URLs are stored as normalized JSON text by the persistence mapper.
            string? json = ReadNullableString(values, propertyName);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }

        /// <summary>
        /// Requires a non-empty text value and returns its trimmed form.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <param name="parameterName">The parameter name used for validation failures.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Query detail lookups need explicit identities to avoid ambiguous results.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }

        /// <summary>
        /// Gets controlled static Cypher for the paged rule catalog query.
        /// </summary>
        public const string RuleQueryCypher = @"
MATCH (rule:ArchonRule)
WHERE ($ruleCode IS NULL OR rule.ruleCode = $ruleCode)
  AND ($version IS NULL OR rule.ruleVersion = $version)
  AND ($category IS NULL OR rule.category = $category)
  AND ($severity IS NULL OR rule.severity = $severity)
  AND ($enabled IS NULL OR rule.enabled = $enabled)
  AND ($builtIn IS NULL OR rule.isBuiltIn = $builtIn)
  AND ($ownerScope IS NULL OR rule.ownerScope = $ownerScope)
RETURN properties(rule) AS rule
ORDER BY rule.ruleCode, rule.ruleVersion
SKIP $skip
LIMIT $take";

        /// <summary>
        /// Gets controlled static Cypher for the rule catalog count query.
        /// </summary>
        public const string RuleQueryCountCypher = @"
MATCH (rule:ArchonRule)
WHERE ($ruleCode IS NULL OR rule.ruleCode = $ruleCode)
  AND ($version IS NULL OR rule.ruleVersion = $version)
  AND ($category IS NULL OR rule.category = $category)
  AND ($severity IS NULL OR rule.severity = $severity)
  AND ($enabled IS NULL OR rule.enabled = $enabled)
  AND ($builtIn IS NULL OR rule.isBuiltIn = $builtIn)
  AND ($ownerScope IS NULL OR rule.ownerScope = $ownerScope)
RETURN count(rule) AS totalCount";

        /// <summary>
        /// Gets controlled static Cypher for the paged hotlist finding query.
        /// </summary>
        public const string HotlistQueryCypher = @"
MATCH (finding:ArchonFinding)
OPTIONAL MATCH (finding)-[:CLASSIFIED_BY_RULE]->(rule:ArchonRule)
WHERE ($snapshotStableKey IS NULL OR finding.snapshotStableKey = $snapshotStableKey)
  AND ($category IS NULL OR rule.category = $category)
  AND ($severity IS NULL OR finding.severity = $severity)
  AND ($status IS NULL OR finding.status = $status)
  AND ($affectedNodeStableKey IS NULL OR $affectedNodeStableKey IN coalesce(finding.affectedNodeStableKeys, []))
  AND ($projectStableKey IS NULL OR finding.projectStableKey = $projectStableKey)
RETURN properties(finding) AS finding
ORDER BY finding.severity DESC, finding.ruleCode, finding.stableKey
SKIP $skip
LIMIT $take";

        /// <summary>
        /// Gets controlled static Cypher for the hotlist finding count query.
        /// </summary>
        public const string HotlistQueryCountCypher = @"
MATCH (finding:ArchonFinding)
OPTIONAL MATCH (finding)-[:CLASSIFIED_BY_RULE]->(rule:ArchonRule)
WHERE ($snapshotStableKey IS NULL OR finding.snapshotStableKey = $snapshotStableKey)
  AND ($category IS NULL OR rule.category = $category)
  AND ($severity IS NULL OR finding.severity = $severity)
  AND ($status IS NULL OR finding.status = $status)
  AND ($affectedNodeStableKey IS NULL OR $affectedNodeStableKey IN coalesce(finding.affectedNodeStableKeys, []))
  AND ($projectStableKey IS NULL OR finding.projectStableKey = $projectStableKey)
RETURN count(finding) AS totalCount";

        /// <summary>
        /// Gets controlled static Cypher for finding history record retrieval.
        /// </summary>
        public const string FindingHistoryRecordsCypher = @"
MATCH (finding:ArchonFinding { historyKey: $historyKey })
RETURN properties(finding) AS finding
ORDER BY finding.snapshotStableKey, finding.stableKey";
    }
}
