using Archon.Application.Graph.Persistence;
using Archon.Infrastructure.Neo4j.Driver;
using Archon.Infrastructure.Neo4j.Schema;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using System.Text.Json;

namespace Archon.Infrastructure.Neo4j.Persistence
{
    /// <summary>
    /// Provides Neo4j-backed snapshot lifecycle listing for the management API surface.
    /// </summary>
    /// <remarks>
    /// The adapter reads only stable public snapshot, repository, and solution lifecycle fields from Neo4j. It keeps Neo4j driver records,
    /// Cypher text, internal node identifiers, and infrastructure exception details inside the infrastructure layer while returning the
    /// application-owned <see cref="SnapshotLifecycleQueryResult"/> contract.
    /// </remarks>
    public sealed class Neo4jSnapshotLifecycleQuery : ISnapshotLifecycleQuery
    {
        /// <summary>
        /// Opens configured Neo4j sessions for controlled lifecycle reads.
        /// </summary>
        private readonly INeo4jSessionProvider _sessionProvider;

        /// <summary>
        /// Logs credential-safe lifecycle query failures and summary details.
        /// </summary>
        private readonly ILogger<Neo4jSnapshotLifecycleQuery> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jSnapshotLifecycleQuery"/> class.
        /// </summary>
        /// <param name="sessionProvider">The provider used to open Neo4j read sessions.</param>
        /// <param name="logger">The logger used for credential-safe diagnostics.</param>
        public Neo4jSnapshotLifecycleQuery(INeo4jSessionProvider sessionProvider, ILogger<Neo4jSnapshotLifecycleQuery> logger)
        {
            // The adapter stores dependencies only; no database work happens until a caller executes a lifecycle read.
            _sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Lists persisted snapshot lifecycle rows through parameterized Cypher and deterministic newest-first ordering.
        /// </summary>
        /// <param name="query">The normalized lifecycle query filters and take limit approved by the application service.</param>
        /// <param name="cancellationToken">The token that cancels the lifecycle read before or during session work.</param>
        /// <returns>A lifecycle result containing rows, total count, take limit, and safe warnings.</returns>
        public async Task<SnapshotLifecycleQueryResult> ListSnapshotsAsync(SnapshotLifecycleQueryRequest query, CancellationToken cancellationToken)
        {
            // The query accepts only approved filter values as parameters and never interpolates caller values into Cypher text.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Read);
            IReadOnlyDictionary<string, object?> parameters = CreateParameters(query);

            try
            {
                IReadOnlyList<IRecord> records = await session.ExecuteReadAsync(
                    async transaction =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        IResultCursor cursor = await transaction.RunAsync(LifecycleListCypher, parameters).ConfigureAwait(false);
                        return await cursor.ToListAsync().ConfigureAwait(false);
                    }).ConfigureAwait(false);

                SnapshotLifecycleQueryRow[] rows = records.Select(MapRow).ToArray();
                int totalCount = records.Count == 0 ? 0 : Convert.ToInt32(records[0]["totalCount"], System.Globalization.CultureInfo.InvariantCulture);
                IReadOnlyList<string> warnings = CreateWarnings(rows, totalCount, query.Take);
                _logger.LogDebug("Read {ReturnedRowCount} snapshot lifecycle rows from Neo4j with total count {TotalCount}.", rows.Length, totalCount);
                return new SnapshotLifecycleQueryResult(rows, totalCount, query.Take, warnings);
            }
            catch (Neo4jException exception)
            {
                // Driver details remain in logs only; API callers receive the generic management endpoint failure response.
                _logger.LogError(exception, "Neo4j snapshot lifecycle listing failed.");
                throw;
            }
        }

        /// <summary>
        /// Creates the parameter object consumed by the static lifecycle Cypher statement.
        /// </summary>
        /// <param name="query">The normalized lifecycle query filters and take limit.</param>
        /// <returns>A dictionary of scalar Cypher parameters.</returns>
        private static IReadOnlyDictionary<string, object?> CreateParameters(SnapshotLifecycleQueryRequest query)
        {
            // DateTimeOffset values are converted to UTC DateTime values because that is the shape persisted by the snapshot writer.
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["repositoryStableKey"] = query.RepositoryStableKey,
                ["solutionStableKey"] = query.SolutionStableKey,
                ["status"] = query.Status,
                ["fromUtc"] = query.FromUtc?.UtcDateTime,
                ["toUtc"] = query.ToUtc?.UtcDateTime,
                ["commitSha"] = query.CommitSha,
                ["take"] = query.Take
            };
        }

        /// <summary>
        /// Maps one Neo4j record into the application-owned lifecycle row contract.
        /// </summary>
        /// <param name="record">The Neo4j query record returned by the controlled lifecycle statement.</param>
        /// <returns>The mapped lifecycle row with safe warning and error counts.</returns>
        private static SnapshotLifecycleQueryRow MapRow(IRecord record)
        {
            // The mapper reads named projected fields rather than returning graph nodes so internal identifiers cannot leak outward.
            return new SnapshotLifecycleQueryRow(
                ReadRequiredString(record, "snapshotStableKey"),
                ReadRequiredString(record, "repositoryStableKey"),
                ReadOptionalString(record, "solutionStableKey"),
                ReadRequiredString(record, "status"),
                ReadOptionalString(record, "branchName"),
                ReadOptionalString(record, "commitSha"),
                ReadRequiredDateTimeOffset(record, "startedUtc"),
                ReadOptionalDateTimeOffset(record, "completedUtc"),
                CountJsonArray(ReadOptionalString(record, "warningsJson")),
                CountJsonArray(ReadOptionalString(record, "errorsJson")));
        }

        /// <summary>
        /// Creates credential-safe warnings for incomplete or truncated lifecycle results.
        /// </summary>
        /// <param name="rows">The bounded rows returned to the application layer.</param>
        /// <param name="totalCount">The total number of rows matching the approved filters.</param>
        /// <param name="take">The effective take limit.</param>
        /// <returns>Safe warnings suitable for management API responses.</returns>
        private static IReadOnlyList<string> CreateWarnings(IReadOnlyList<SnapshotLifecycleQueryRow> rows, int totalCount, int take)
        {
            // Warnings describe response completeness without exposing database internals or query text.
            List<string> warnings = [];
            if (totalCount > take)
            {
                warnings.Add("Snapshot lifecycle response was truncated by the take limit.");
            }

            if (rows.Any(static row => row.CompletedUtc is null))
            {
                warnings.Add("One or more snapshots have incomplete lifecycle timestamps.");
            }

            return warnings;
        }

        /// <summary>
        /// Reads a required string field from a projected Neo4j record.
        /// </summary>
        /// <param name="record">The record containing the projected field.</param>
        /// <param name="key">The projected field name.</param>
        /// <returns>The non-empty string value.</returns>
        private static string ReadRequiredString(IRecord record, string key)
        {
            // Required lifecycle fields should be present on persisted snapshot nodes; missing values indicate incomplete stored data.
            string? value = ReadOptionalString(record, key);
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
        }

        /// <summary>
        /// Reads an optional string field from a projected Neo4j record.
        /// </summary>
        /// <param name="record">The record containing the projected field.</param>
        /// <param name="key">The projected field name.</param>
        /// <returns>The string value, or <see langword="null"/> when absent or null.</returns>
        private static string? ReadOptionalString(IRecord record, string key)
        {
            // Neo4j nulls are represented through driver null values; converting centrally keeps the row mapper compact.
            object value = record[key];
            return value is null ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads a required timestamp field from a projected Neo4j record.
        /// </summary>
        /// <param name="record">The record containing the projected field.</param>
        /// <param name="key">The projected field name.</param>
        /// <returns>The timestamp converted to a UTC <see cref="DateTimeOffset"/>.</returns>
        private static DateTimeOffset ReadRequiredDateTimeOffset(IRecord record, string key)
        {
            // Missing required timestamps are mapped to the Unix epoch so the row can remain safe and sortable rather than leaking internals.
            return ReadOptionalDateTimeOffset(record, key) ?? DateTimeOffset.UnixEpoch;
        }

        /// <summary>
        /// Reads an optional timestamp field from a projected Neo4j record.
        /// </summary>
        /// <param name="record">The record containing the projected field.</param>
        /// <param name="key">The projected field name.</param>
        /// <returns>The timestamp converted to UTC, or <see langword="null"/> when absent.</returns>
        private static DateTimeOffset? ReadOptionalDateTimeOffset(IRecord record, string key)
        {
            // Snapshot persistence currently writes UTC DateTime values; the mapper also handles Neo4j temporal values defensively.
            object value = record[key];
            return value switch
            {
                null => null,
                DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
                ZonedDateTime zonedDateTime => zonedDateTime.ToDateTimeOffset().ToUniversalTime(),
                LocalDateTime localDateTime => new DateTimeOffset(DateTime.SpecifyKind(localDateTime.ToDateTime(), DateTimeKind.Utc)),
                _ => DateTimeOffset.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
                    ? parsed.ToUniversalTime()
                    : null
            };
        }

        /// <summary>
        /// Counts string array entries in a compact JSON diagnostic property.
        /// </summary>
        /// <param name="json">The JSON array text stored on the snapshot node.</param>
        /// <returns>The number of array elements, or zero when no valid array is present.</returns>
        private static int CountJsonArray(string? json)
        {
            // Invalid or absent diagnostic JSON is treated as incomplete data rather than surfaced as raw storage content.
            if (string.IsNullOrWhiteSpace(json))
            {
                return 0;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetArrayLength() : 0;
            }
            catch (JsonException)
            {
                return 0;
            }
        }

        /// <summary>
        /// Lists persisted snapshot lifecycle rows with approved filters, total count, and deterministic ordering.
        /// </summary>
        private const string LifecycleListCypher = @"
MATCH (snapshot:ArchonSnapshot)
WHERE ($repositoryStableKey IS NULL OR snapshot.repositoryStableKey = $repositoryStableKey)
  AND ($status IS NULL OR snapshot.status = $status)
  AND ($commitSha IS NULL OR snapshot.commitSha = $commitSha)
  AND ($fromUtc IS NULL OR snapshot.startedUtc >= $fromUtc)
  AND ($toUtc IS NULL OR snapshot.startedUtc <= $toUtc)
OPTIONAL MATCH (snapshot)-[:INCLUDES_SOLUTION]->(solution:ArchonSolution)
WITH snapshot, min(solution.stableKey) AS solutionStableKey
WHERE ($solutionStableKey IS NULL OR solutionStableKey = $solutionStableKey)
WITH collect({
    snapshotStableKey: snapshot.stableKey,
    repositoryStableKey: snapshot.repositoryStableKey,
    solutionStableKey: solutionStableKey,
    status: snapshot.status,
    branchName: snapshot.branchName,
    commitSha: snapshot.commitSha,
    startedUtc: snapshot.startedUtc,
    completedUtc: snapshot.completedUtc,
    warningsJson: snapshot.warningsJson,
    errorsJson: snapshot.errorsJson
}) AS rows
WITH rows, size(rows) AS totalCount
UNWIND rows AS row
RETURN row.snapshotStableKey AS snapshotStableKey,
       row.repositoryStableKey AS repositoryStableKey,
       row.solutionStableKey AS solutionStableKey,
       row.status AS status,
       row.branchName AS branchName,
       row.commitSha AS commitSha,
       row.startedUtc AS startedUtc,
       row.completedUtc AS completedUtc,
       row.warningsJson AS warningsJson,
       row.errorsJson AS errorsJson,
       totalCount AS totalCount
ORDER BY startedUtc DESC, snapshotStableKey ASC
LIMIT $take";
    }
}
