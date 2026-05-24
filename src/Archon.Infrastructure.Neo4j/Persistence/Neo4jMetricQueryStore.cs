using System.Text.Json;
using Archon.Application.Metrics;
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
        /// Implements controlled WP013 snapshot and node-targeted metric read queries over Neo4j without exposing arbitrary Cypher to callers.
    /// </summary>
    public sealed class Neo4jMetricQueryStore : IMetricQueryStore
    {
        /// <summary>
        /// Opens Neo4j sessions for controlled metric query reads.
        /// </summary>
        private readonly INeo4jSessionProvider _sessionProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jMetricQueryStore"/> class.
        /// </summary>
        /// <param name="sessionProvider">The session provider used to open Neo4j read sessions.</param>
        public Neo4jMetricQueryStore(INeo4jSessionProvider sessionProvider)
        {
            // The query store accepts only the session provider because all query text is static and parameterized in this adapter.
            _sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
        }

        /// <summary>
        /// Retrieves persisted metrics matching the supplied controlled snapshot metric query.
        /// </summary>
        /// <param name="query">The controlled filter and paging contract for the metric query.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before query execution.</param>
        /// <returns>A bounded page of persisted metric records.</returns>
        public async Task<PagedQueryResult<MetricRecord>> QueryMetricsAsync(MetricQuery query, CancellationToken cancellationToken)
        {
            // Cypher is static and parameterized; callers can only influence approved filter values and page bounds.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Read);
            IReadOnlyDictionary<string, object?> parameters = MapMetricQueryParameters(query);
            IResultCursor countCursor = await session.RunAsync(MetricQueryCountCypher, parameters).ConfigureAwait(false);
            int totalCount = Convert.ToInt32((await countCursor.SingleAsync().ConfigureAwait(false))["totalCount"].As<long>(), System.Globalization.CultureInfo.InvariantCulture);
            IResultCursor cursor = await session.RunAsync(MetricQueryCypher, parameters).ConfigureAwait(false);
            IReadOnlyList<IRecord> records = await cursor.ToListAsync().ConfigureAwait(false);
            return new PagedQueryResult<MetricRecord>(records.Select(MapMetricRecord), totalCount, query.Skip, query.Take);
        }

        /// <summary>
        /// Maps metric query filter and paging values to Neo4j parameters.
        /// </summary>
        /// <param name="query">The controlled metric query.</param>
        /// <returns>The parameter dictionary used by static metric query Cypher.</returns>
        private static IReadOnlyDictionary<string, object?> MapMetricQueryParameters(MetricQuery query)
        {
            // Parameters are scalar values so user input cannot alter Cypher structure.
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["snapshotStableKey"] = query.SnapshotStableKey,
                ["metricKind"] = query.MetricKind,
                ["scopeKind"] = query.ScopeKind,
                ["projectStableKey"] = query.ProjectStableKey,
                ["skip"] = query.Skip,
                ["take"] = query.Take
            };
        }

        /// <summary>
        /// Maps a Neo4j metric record into a domain metric record.
        /// </summary>
        /// <param name="record">The Neo4j record containing metric properties.</param>
        /// <returns>The domain metric record.</returns>
        private static MetricRecord MapMetricRecord(IRecord record)
        {
            // Metric projection mirrors snapshot persistence so API reads interpret value, unknown-state, and fingerprint fields consistently.
            IReadOnlyDictionary<string, object> metric = record["metric"].As<IReadOnlyDictionary<string, object>>();
            return new MetricRecord(
                new StableKey(ReadString(metric, "snapshotStableKey")),
                new StableKey(ReadString(metric, "stableKey")),
                ReadString(metric, "metricKind"),
                MetricScopeKind.Parse(ReadString(metric, "scopeKind")),
                ReadOptionalStableKey(metric, "nodeStableKey"),
                ReadOptionalStableKey(metric, "edgeStableKey"),
                ReadOptionalStableKey(metric, "primaryEvidenceStableKey"),
                ReadString(metric, "name"),
                ReadNullableDecimal(metric, "numericValue"),
                ReadNullableString(metric, "textValue"),
                ReadNullableString(metric, "unit"),
                new Confidence(ReadDecimal(metric, "confidence")),
                new UnknownState(ReadBoolean(metric, "hasUnknownData"), ReadNullableString(metric, "unknownReason")),
                ReadMetadata(metric, "metadataJson"),
                new Fingerprint(ReadString(metric, "fingerprint")));
        }

        /// <summary>
        /// Reads and parses graph metadata JSON from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property containing metadata JSON.</param>
        /// <returns>The parsed canonical graph metadata.</returns>
        private static GraphMetadata ReadMetadata(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Metadata is reconstructed through the domain factory so canonical serialization and validation still apply.
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
        /// Reads a required decimal property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>The decimal value.</returns>
        private static decimal ReadDecimal(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Neo4j numeric values are converted invariantly to preserve precision across driver numeric representations.
            object value = values[propertyName];
            return decimal.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads an optional decimal property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>The optional decimal value.</returns>
        private static decimal? ReadNullableDecimal(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Metric values can be categorical text-only, so numeric values are optional.
            return values.TryGetValue(propertyName, out object? value) && value is not null
                ? decimal.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!, System.Globalization.CultureInfo.InvariantCulture)
                : null;
        }

        /// <summary>
        /// Reads an optional stable key property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>The stable key, or <see langword="null"/> when absent.</returns>
        private static StableKey? ReadOptionalStableKey(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Optional links may be absent for snapshot-scoped metrics that do not target a node or edge.
            string? value = ReadNullableString(values, propertyName);
            return string.IsNullOrWhiteSpace(value) ? null : new StableKey(value);
        }

        /// <summary>
        /// Gets controlled static Cypher for the paged snapshot metric query.
        /// </summary>
        public const string MetricQueryCypher = @"
MATCH (metric:ArchonMetric)
WHERE metric.snapshotStableKey = $snapshotStableKey
  AND ($metricKind IS NULL OR metric.metricKind = $metricKind)
  AND ($scopeKind IS NULL OR metric.scopeKind = $scopeKind)
  AND ($projectStableKey IS NULL OR metric.nodeStableKey = $projectStableKey)
RETURN properties(metric) AS metric
ORDER BY metric.metricKind, metric.scopeKind, metric.nodeStableKey, metric.stableKey
SKIP $skip
LIMIT $take";

        /// <summary>
        /// Gets controlled static Cypher for the snapshot metric count query.
        /// </summary>
        public const string MetricQueryCountCypher = @"
MATCH (metric:ArchonMetric)
WHERE metric.snapshotStableKey = $snapshotStableKey
  AND ($metricKind IS NULL OR metric.metricKind = $metricKind)
  AND ($scopeKind IS NULL OR metric.scopeKind = $scopeKind)
  AND ($projectStableKey IS NULL OR metric.nodeStableKey = $projectStableKey)
RETURN count(metric) AS totalCount";
    }
}
