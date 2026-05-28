using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Application.Graph.Persistence;
using Archon.Infrastructure.Neo4j.Driver;
using Archon.Infrastructure.Neo4j.Schema;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using System.Text.Json;

namespace Archon.Infrastructure.Neo4j.Persistence
{
    /// <summary>
    /// Provides Neo4j-backed durable storage for extraction run lifecycle state.
    /// </summary>
    /// <remarks>
    /// The store implements the application-owned <see cref="IExtractionRunHistory"/> port without exposing Neo4j driver types outside
    /// the infrastructure layer. It persists the run node and the safe request summary node together so status and history readers can
    /// reconstruct current API responses after process restart.
    /// </remarks>
    public sealed class Neo4jExtractionRunHistory : IExtractionRunHistory
    {
        /// <summary>
        /// Provides serialization settings for compact diagnostic payloads stored on run nodes.
        /// </summary>
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// Opens configured Neo4j sessions for read and write transactions.
        /// </summary>
        private readonly INeo4jSessionProvider _sessionProvider;

        /// <summary>
        /// Ensures graph schema exists before write operations rely on run identifiers and indexes.
        /// </summary>
        private readonly IArchitectureGraphInitializer _graphInitializer;

        /// <summary>
        /// Logs credential-safe persistence events and failure summaries.
        /// </summary>
        private readonly ILogger<Neo4jExtractionRunHistory> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jExtractionRunHistory"/> class.
        /// </summary>
        /// <param name="sessionProvider">The provider used to open configured Neo4j sessions.</param>
        /// <param name="graphInitializer">The schema initializer used before durable run writes.</param>
        /// <param name="logger">The logger used for credential-safe diagnostics.</param>
        public Neo4jExtractionRunHistory(
            INeo4jSessionProvider sessionProvider,
            IArchitectureGraphInitializer graphInitializer,
            ILogger<Neo4jExtractionRunHistory> logger)
        {
            // Constructor injection keeps this infrastructure adapter testable and prevents service-locator use in persistence code.
            _sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
            _graphInitializer = graphInitializer ?? throw new ArgumentNullException(nameof(graphInitializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a durable queued extraction run and associated safe request summary in one write transaction.
        /// </summary>
        /// <param name="resolvedInput">The normalized extraction input accepted by the application layer.</param>
        /// <param name="startedUtc">The UTC timestamp assigned to the accepted run.</param>
        /// <param name="cancellationToken">The token that cancels schema initialization or the write before it starts.</param>
        /// <returns>The created queued run that is already durable when returned.</returns>
        public async Task<ExtractionRun> CreateAsync(ResolvedExtractionInput resolvedInput, DateTimeOffset startedUtc, CancellationToken cancellationToken)
        {
            // The start path must persist before scheduling, so schema initialization and transaction commit both happen before returning.
            ArgumentNullException.ThrowIfNull(resolvedInput);
            cancellationToken.ThrowIfCancellationRequested();

            ExtractionRun run = new(
                ExtractionRunId.New(),
                ExtractionRunStatus.Queued,
                new ExtractionRunRequestSummary(
                    resolvedInput.RepositoryRootDirectory,
                    resolvedInput.SolutionPaths.ToArray(),
                    resolvedInput.BranchName,
                    resolvedInput.CommitSha,
                    resolvedInput.RequestedBy,
                    resolvedInput.Metadata.Keys.Order(StringComparer.Ordinal).ToArray()),
                startedUtc.ToUniversalTime(),
                completedUtc: null,
                new ExtractionRunProgress(
                    "Queued",
                    "Extraction request accepted and queued for asynchronous execution.",
                    Percentage: 0,
                    LastUpdatedUtc: startedUtc.ToUniversalTime()),
                warnings: null,
                errors: null,
                timings: null,
                snapshotIdentity: null);

            await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
            await PersistRunAsync(run, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Persisted accepted extraction run {RunId} to Neo4j before scheduling.", run.RunId.ToString());
            return run;
        }

        /// <summary>
        /// Replaces the durable state for an existing extraction run without erasing its request summary.
        /// </summary>
        /// <param name="run">The complete application run snapshot to persist.</param>
        /// <param name="cancellationToken">The token that cancels the write before it starts.</param>
        /// <returns>A task that completes when Neo4j has committed the replacement state.</returns>
        public async Task UpdateAsync(ExtractionRun run, CancellationToken cancellationToken)
        {
            // Updates merge by public run id so repeated progress writes remain idempotent at the graph identity level.
            ArgumentNullException.ThrowIfNull(run);
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
            await PersistRunAsync(run, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Persisted extraction run {RunId} state {Status} to Neo4j.", run.RunId.ToString(), run.Status.ToString());
        }

        /// <summary>
        /// Reads one durable extraction run by public run identifier.
        /// </summary>
        /// <param name="runId">The public run identifier to retrieve.</param>
        /// <param name="cancellationToken">The token that cancels the read before it starts.</param>
        /// <returns>The mapped run when found; otherwise <see langword="null"/>.</returns>
        public async Task<ExtractionRun?> GetAsync(ExtractionRunId runId, CancellationToken cancellationToken)
        {
            // Reads use a relationship to the request node so status responses never depend on process-local request state.
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Read);

            try
            {
                IRecord? record = await session.ExecuteReadAsync(
                    async transaction =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        IResultCursor cursor = await transaction.RunAsync(
                            $@"
MATCH (run:{Neo4jSchemaNames.Labels.ExtractionRun} {{{Neo4jSchemaNames.Properties.RunId}: $runId}})
OPTIONAL MATCH (run)-[:{Neo4jSchemaNames.Relationships.HasExtractionRunRequest}]->(request:{Neo4jSchemaNames.Labels.ExtractionRunRequest})
RETURN run, request",
                            new { runId = runId.ToString() }).ConfigureAwait(false);

                        return await cursor.SingleOrDefaultAsync().ConfigureAwait(false);
                    }).ConfigureAwait(false);

                return record is null ? null : MapRun(record);
            }
            catch (Neo4jException exception)
            {
                // Driver details remain in logs only; API callers receive the normal missing or infrastructure-failure path from callers.
                _logger.LogError(exception, "Neo4j extraction run lookup failed for run {RunId}.", runId.ToString());
                throw;
            }
        }

        /// <summary>
        /// Reads recent durable extraction runs in deterministic newest-first order.
        /// </summary>
        /// <param name="limit">The maximum number of runs to retrieve.</param>
        /// <param name="cancellationToken">The token that cancels the read before it starts.</param>
        /// <returns>The mapped recent runs ordered by start time descending and run id ascending for ties.</returns>
        public async Task<IReadOnlyList<ExtractionRun>> GetRecentAsync(int limit, CancellationToken cancellationToken)
        {
            // The query mirrors in-memory ordering so API history behavior remains compatible while storage becomes durable.
            cancellationToken.ThrowIfCancellationRequested();
            int effectiveLimit = Math.Max(0, limit);
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Read);

            try
            {
                IReadOnlyList<IRecord> records = await session.ExecuteReadAsync(
                    async transaction =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        IResultCursor cursor = await transaction.RunAsync(
                            $@"
MATCH (run:{Neo4jSchemaNames.Labels.ExtractionRun})
OPTIONAL MATCH (run)-[:{Neo4jSchemaNames.Relationships.HasExtractionRunRequest}]->(request:{Neo4jSchemaNames.Labels.ExtractionRunRequest})
RETURN run, request
ORDER BY run.{Neo4jSchemaNames.Properties.StartedUtc} DESC, run.{Neo4jSchemaNames.Properties.RunId} ASC
LIMIT $limit",
                            new { limit = effectiveLimit }).ConfigureAwait(false);

                        return await cursor.ToListAsync().ConfigureAwait(false);
                    }).ConfigureAwait(false);

                return records.Select(MapRun).ToArray();
            }
            catch (Neo4jException exception)
            {
                // The exception is rethrown after safe logging so upstream health and API layers can choose their own response shape.
                _logger.LogError(exception, "Neo4j extraction run history lookup failed for limit {Limit}.", effectiveLimit);
                throw;
            }
        }

        /// <summary>
        /// Ensures Neo4j schema is initialized before writes rely on extraction run constraints.
        /// </summary>
        /// <param name="cancellationToken">The token that cancels schema initialization.</param>
        /// <returns>A task that completes when schema initialization has succeeded.</returns>
        private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
        {
            // Schema initialization is idempotent; executing it here protects hosts that write a run before explicit startup initialization.
            GraphInitializationResult initializationResult = await _graphInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
            if (!initializationResult.Succeeded)
            {
                _logger.LogError(
                    "Neo4j schema initialization failed before extraction run persistence after {StatementCount} statements.",
                    initializationResult.StatementsExecuted);
                throw new InvalidOperationException("Neo4j schema initialization failed before extraction run persistence.");
            }
        }

        /// <summary>
        /// Persists a complete run snapshot and its request summary in one Neo4j write transaction.
        /// </summary>
        /// <param name="run">The complete run snapshot to write.</param>
        /// <param name="cancellationToken">The token that cancels the transaction before it starts.</param>
        /// <returns>A task that completes after the transaction commits.</returns>
        private async Task PersistRunAsync(ExtractionRun run, CancellationToken cancellationToken)
        {
            // The query uses only schema-name constants for labels, properties, and relationship types; caller data remains parameters.
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Write);
            IReadOnlyDictionary<string, object?> parameters = CreateRunParameters(run);

            try
            {
                await session.ExecuteWriteAsync(
                    async transaction =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        IResultCursor cursor = await transaction.RunAsync(
                            $@"
MERGE (run:{Neo4jSchemaNames.Labels.ExtractionRun} {{{Neo4jSchemaNames.Properties.RunId}: $runId}})
SET run.{Neo4jSchemaNames.Properties.Status} = $status,
    run.{Neo4jSchemaNames.Properties.StartedUtc} = $startedUtc,
    run.{Neo4jSchemaNames.Properties.CompletedUtc} = $completedUtc,
    run.{Neo4jSchemaNames.Properties.ProgressStage} = $progressStage,
    run.{Neo4jSchemaNames.Properties.ProgressMessage} = $progressMessage,
    run.{Neo4jSchemaNames.Properties.ProgressPercentage} = $progressPercentage,
    run.{Neo4jSchemaNames.Properties.ProgressLastUpdatedUtc} = $progressLastUpdatedUtc,
    run.{Neo4jSchemaNames.Properties.WarningCount} = $warningCount,
    run.{Neo4jSchemaNames.Properties.ErrorCount} = $errorCount,
    run.{Neo4jSchemaNames.Properties.SnapshotStableKey} = $snapshotStableKey,
    run.{Neo4jSchemaNames.Properties.WarningDiagnosticsJson} = $warningDiagnosticsJson,
    run.{Neo4jSchemaNames.Properties.ErrorDiagnosticsJson} = $errorDiagnosticsJson,
    run.{Neo4jSchemaNames.Properties.TimingDiagnosticsJson} = $timingDiagnosticsJson,
    run.{Neo4jSchemaNames.Properties.PersistenceDiagnosticsJson} = $persistenceDiagnosticsJson
MERGE (request:{Neo4jSchemaNames.Labels.ExtractionRunRequest} {{{Neo4jSchemaNames.Properties.RunId}: $runId}})
SET request.{Neo4jSchemaNames.Properties.RepositoryRootDirectory} = $repositoryRootDirectory,
    request.{Neo4jSchemaNames.Properties.SolutionPaths} = $solutionPaths,
    request.{Neo4jSchemaNames.Properties.BranchName} = $branchName,
    request.{Neo4jSchemaNames.Properties.CommitSha} = $commitSha,
    request.{Neo4jSchemaNames.Properties.RequestedBy} = $requestedBy,
    request.{Neo4jSchemaNames.Properties.MetadataKeys} = $metadataKeys
MERGE (run)-[:{Neo4jSchemaNames.Relationships.HasExtractionRunRequest}]->(request)
WITH run
OPTIONAL MATCH (run)-[staleProducedSnapshot:{Neo4jSchemaNames.Relationships.ProducedSnapshot}]->(linkedSnapshot:{Neo4jSchemaNames.Labels.Snapshot})
WHERE $snapshotStableKey IS NULL OR $shouldLinkProducedSnapshot = false OR linkedSnapshot.{Neo4jSchemaNames.Properties.StableKey} <> $snapshotStableKey
DELETE staleProducedSnapshot
WITH run
OPTIONAL MATCH (snapshot:{Neo4jSchemaNames.Labels.Snapshot} {{{Neo4jSchemaNames.Properties.StableKey}: $snapshotStableKey}})
FOREACH (_ IN CASE WHEN $shouldLinkProducedSnapshot = true AND snapshot IS NOT NULL THEN [1] ELSE [] END |
    MERGE (run)-[:{Neo4jSchemaNames.Relationships.ProducedSnapshot}]->(snapshot))",
                            parameters).ConfigureAwait(false);
                        await cursor.ConsumeAsync().ConfigureAwait(false);
                    }).ConfigureAwait(false);
            }
            catch (Neo4jException exception)
            {
                // Raw Cypher, connection values, and driver internals stay out of application models and HTTP responses.
                _logger.LogError(exception, "Neo4j extraction run persistence failed for run {RunId}.", run.RunId.ToString());
                throw;
            }
        }

        /// <summary>
        /// Creates Neo4j query parameters from an application run snapshot.
        /// </summary>
        /// <param name="run">The run snapshot to translate.</param>
        /// <returns>A parameter object containing only primitive and array values accepted by the Neo4j driver.</returns>
        private static IReadOnlyDictionary<string, object?> CreateRunParameters(ExtractionRun run)
        {
            // Date-time values are stored as ISO-8601 strings for deterministic ordering and straightforward test assertions.
            bool shouldLinkProducedSnapshot = run.Status == ExtractionRunStatus.Completed && !string.IsNullOrWhiteSpace(run.SnapshotIdentity);
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["runId"] = run.RunId.ToString(),
                ["status"] = run.Status.ToString(),
                ["startedUtc"] = FormatDateTime(run.StartedUtc),
                ["completedUtc"] = FormatNullableDateTime(run.CompletedUtc),
                ["progressStage"] = run.Progress.Stage,
                ["progressMessage"] = run.Progress.Message,
                ["progressPercentage"] = run.Progress.Percentage,
                ["progressLastUpdatedUtc"] = FormatDateTime(run.Progress.LastUpdatedUtc),
                ["warningCount"] = run.Warnings.Count,
                ["errorCount"] = run.Errors.Count,
                ["snapshotStableKey"] = run.SnapshotIdentity,
                ["shouldLinkProducedSnapshot"] = shouldLinkProducedSnapshot,
                ["warningDiagnosticsJson"] = JsonSerializer.Serialize(run.Warnings, SerializerOptions),
                ["errorDiagnosticsJson"] = JsonSerializer.Serialize(run.Errors, SerializerOptions),
                ["timingDiagnosticsJson"] = JsonSerializer.Serialize(run.Timings, SerializerOptions),
                ["persistenceDiagnosticsJson"] = SerializePersistenceDiagnostics(run.PersistenceDiagnostics),
                ["repositoryRootDirectory"] = run.SubmittedRequest.RepositoryRootDirectory,
                ["solutionPaths"] = run.SubmittedRequest.SolutionPaths.ToArray(),
                ["branchName"] = run.SubmittedRequest.BranchName,
                ["commitSha"] = run.SubmittedRequest.CommitSha,
                ["requestedBy"] = run.SubmittedRequest.RequestedBy,
                ["metadataKeys"] = run.SubmittedRequest.MetadataKeys.ToArray()
            };
        }

        /// <summary>
        /// Maps one Neo4j record containing a run node and optional request node back to the application run model.
        /// </summary>
        /// <param name="record">The Neo4j record returned by a run-history query.</param>
        /// <returns>The reconstructed application run snapshot.</returns>
        private static ExtractionRun MapRun(IRecord record)
        {
            // Request summaries are expected for WP019 records, but fallback defaults keep older partially migrated records readable.
            INode runNode = record["run"].As<INode>();
            INode? requestNode = record["request"].As<INode?>();
            ExtractionRunId.TryParse(GetRequiredString(runNode, Neo4jSchemaNames.Properties.RunId), out ExtractionRunId runId);

            return new ExtractionRun(
                runId,
                ParseStatus(GetOptionalString(runNode, Neo4jSchemaNames.Properties.Status)),
                new ExtractionRunRequestSummary(
                    GetOptionalString(requestNode, Neo4jSchemaNames.Properties.RepositoryRootDirectory) ?? string.Empty,
                    GetStringArray(requestNode, Neo4jSchemaNames.Properties.SolutionPaths),
                    GetOptionalString(requestNode, Neo4jSchemaNames.Properties.BranchName),
                    GetOptionalString(requestNode, Neo4jSchemaNames.Properties.CommitSha),
                    GetOptionalString(requestNode, Neo4jSchemaNames.Properties.RequestedBy),
                    GetStringArray(requestNode, Neo4jSchemaNames.Properties.MetadataKeys)),
                ParseDateTime(GetRequiredString(runNode, Neo4jSchemaNames.Properties.StartedUtc)),
                ParseNullableDateTime(GetOptionalString(runNode, Neo4jSchemaNames.Properties.CompletedUtc)),
                new ExtractionRunProgress(
                    GetOptionalString(runNode, Neo4jSchemaNames.Properties.ProgressStage) ?? "Unknown",
                    GetOptionalString(runNode, Neo4jSchemaNames.Properties.ProgressMessage) ?? "Progress details were not available.",
                    GetOptionalInt(runNode, Neo4jSchemaNames.Properties.ProgressPercentage),
                    ParseDateTime(GetOptionalString(runNode, Neo4jSchemaNames.Properties.ProgressLastUpdatedUtc) ?? GetRequiredString(runNode, Neo4jSchemaNames.Properties.StartedUtc))),
                DeserializeArray<ExtractionRunWarning>(GetOptionalString(runNode, Neo4jSchemaNames.Properties.WarningDiagnosticsJson)),
                DeserializeArray<ExtractionRunError>(GetOptionalString(runNode, Neo4jSchemaNames.Properties.ErrorDiagnosticsJson)),
                DeserializeArray<ExtractionRunTiming>(GetOptionalString(runNode, Neo4jSchemaNames.Properties.TimingDiagnosticsJson)),
                GetOptionalString(runNode, Neo4jSchemaNames.Properties.SnapshotStableKey),
                DeserializePersistenceDiagnostics(GetOptionalString(runNode, Neo4jSchemaNames.Properties.PersistenceDiagnosticsJson)));
        }

        /// <summary>
        /// Parses an extraction run lifecycle status from persisted text.
        /// </summary>
        /// <param name="status">The persisted status text.</param>
        /// <returns>The parsed status, or <see cref="ExtractionRunStatus.Failed"/> when the value is not recognized.</returns>
        private static ExtractionRunStatus ParseStatus(string? status)
        {
            // Unknown status text indicates malformed persisted data; mapping to Failed keeps records visible without inventing success.
            return Enum.TryParse(status, ignoreCase: false, out ExtractionRunStatus parsedStatus)
                ? parsedStatus
                : ExtractionRunStatus.Failed;
        }

        /// <summary>
        /// Formats a timestamp as a UTC ISO-8601 string.
        /// </summary>
        /// <param name="value">The timestamp to format.</param>
        /// <returns>The UTC ISO-8601 string used for durable ordering.</returns>
        private static string FormatDateTime(DateTimeOffset value)
        {
            // The O format sorts lexicographically for UTC values and round-trips exactly through DateTimeOffset parsing.
            return value.ToUniversalTime().ToString("O");
        }

        /// <summary>
        /// Formats an optional timestamp as a UTC ISO-8601 string.
        /// </summary>
        /// <param name="value">The optional timestamp to format.</param>
        /// <returns>The UTC ISO-8601 string, or <see langword="null"/> when no timestamp exists.</returns>
        private static string? FormatNullableDateTime(DateTimeOffset? value)
        {
            // Null terminal timestamps remain null so queued and running records are distinguishable from completed records.
            return value.HasValue ? FormatDateTime(value.Value) : null;
        }

        /// <summary>
        /// Parses a required UTC timestamp from persisted text.
        /// </summary>
        /// <param name="value">The persisted timestamp text.</param>
        /// <returns>The parsed timestamp normalized to UTC.</returns>
        private static DateTimeOffset ParseDateTime(string value)
        {
            // Persisted timestamps are written by this adapter, so invariant parsing is sufficient for round-trip recovery.
            return DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
        }

        /// <summary>
        /// Parses an optional UTC timestamp from persisted text.
        /// </summary>
        /// <param name="value">The persisted timestamp text, if present.</param>
        /// <returns>The parsed timestamp, or <see langword="null"/> when no value was persisted.</returns>
        private static DateTimeOffset? ParseNullableDateTime(string? value)
        {
            // Empty strings are treated like null so older manually edited records do not fail status mapping.
            return string.IsNullOrWhiteSpace(value) ? null : ParseDateTime(value);
        }

        /// <summary>
        /// Reads a required string property from a Neo4j node.
        /// </summary>
        /// <param name="node">The Neo4j node containing the property.</param>
        /// <param name="propertyName">The schema property name to read.</param>
        /// <returns>The stored string value.</returns>
        private static string GetRequiredString(INode node, string propertyName)
        {
            // Required values represent the minimum shape needed to reconstruct an extraction run.
            return node.Properties[propertyName].As<string>();
        }

        /// <summary>
        /// Reads an optional string property from a Neo4j node.
        /// </summary>
        /// <param name="node">The Neo4j node containing the property, if present.</param>
        /// <param name="propertyName">The schema property name to read.</param>
        /// <returns>The stored string value, or <see langword="null"/> when absent or null.</returns>
        private static string? GetOptionalString(INode? node, string propertyName)
        {
            // Neo4j omits null properties, so missing and null both map to a nullable application value.
            if (node is null || !node.Properties.TryGetValue(propertyName, out object? value) || value is null)
            {
                return null;
            }

            return value.As<string>();
        }

        /// <summary>
        /// Reads an optional integer property from a Neo4j node.
        /// </summary>
        /// <param name="node">The Neo4j node containing the property.</param>
        /// <param name="propertyName">The schema property name to read.</param>
        /// <returns>The stored integer value, or <see langword="null"/> when absent or null.</returns>
        private static int? GetOptionalInt(INode node, string propertyName)
        {
            // Numeric properties may be returned by the driver as wider integer types, so conversion goes through the driver helper.
            return node.Properties.TryGetValue(propertyName, out object? value) && value is not null ? value.As<int>() : null;
        }

        /// <summary>
        /// Reads a string array property from a Neo4j node.
        /// </summary>
        /// <param name="node">The Neo4j node containing the property, if present.</param>
        /// <param name="propertyName">The schema property name to read.</param>
        /// <returns>The stored strings, or an empty array when the property is absent.</returns>
        private static IReadOnlyList<string> GetStringArray(INode? node, string propertyName)
        {
            // Array properties store normalized paths and metadata keys in their original deterministic order.
            if (node is null || !node.Properties.TryGetValue(propertyName, out object? value) || value is null)
            {
                return [];
            }

            return value.As<List<string>>().ToArray();
        }

        /// <summary>
        /// Deserializes a JSON array property into application diagnostic records.
        /// </summary>
        /// <typeparam name="T">The diagnostic record type stored in the JSON array.</typeparam>
        /// <param name="json">The JSON text read from Neo4j.</param>
        /// <returns>The deserialized records, or an empty array when no JSON was persisted.</returns>
        private static IReadOnlyList<T> DeserializeArray<T>(string? json)
        {
            // Empty diagnostic JSON should not make older records unreadable; malformed JSON is allowed to fail fast as corrupt data.
            return string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<T[]>(json, SerializerOptions) ?? [];
        }

        /// <summary>
        /// Deserializes an optional JSON object property into an application diagnostic object.
        /// </summary>
        /// <typeparam name="T">The diagnostic object type stored in the JSON payload.</typeparam>
        /// <param name="json">The JSON text read from Neo4j.</param>
        /// <returns>The deserialized object, or <see langword="null"/> when no JSON was persisted.</returns>
        private static T? DeserializeObject<T>(string? json)
        {
            // Optional persistence diagnostics remain null for queued, running, older, or not-yet-instrumented runs.
            return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }

        /// <summary>
        /// Serializes optional persistence diagnostics into a JSON shape that can be explicitly reconstructed.
        /// </summary>
        /// <param name="diagnostics">The optional diagnostics object attached to a run.</param>
        /// <returns>Serialized JSON, or <see langword="null"/> when the run has no persistence diagnostics.</returns>
        private static string? SerializePersistenceDiagnostics(ExtractionRunPersistenceDiagnostics? diagnostics)
        {
            // The application diagnostics type uses a constructor with parameter names that are clearer for code than default JSON binding,
            // so persistence stores an explicit DTO shape to keep readback deterministic.
            return diagnostics is null
                ? null
                : JsonSerializer.Serialize(
                    new PersistenceDiagnosticsDocument(
                        diagnostics.Timings.ToArray(),
                        diagnostics.Counts,
                        diagnostics.Completed),
                    SerializerOptions);
        }

        /// <summary>
        /// Deserializes optional persistence diagnostics from the explicit persistence JSON shape.
        /// </summary>
        /// <param name="json">The JSON text stored on the run node.</param>
        /// <returns>The reconstructed diagnostics object, or <see langword="null"/> when no diagnostics were stored.</returns>
        private static ExtractionRunPersistenceDiagnostics? DeserializePersistenceDiagnostics(string? json)
        {
            // Older or queued runs may not have persistence diagnostics, so empty JSON maps to null rather than a synthetic empty document.
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            PersistenceDiagnosticsDocument? document = JsonSerializer.Deserialize<PersistenceDiagnosticsDocument>(json, SerializerOptions);
            return document is null
                ? null
                : new ExtractionRunPersistenceDiagnostics(document.Timings, document.Counts, document.Completed);
        }

        /// <summary>
        /// Defines the JSON document shape used for persisted extraction run persistence diagnostics.
        /// </summary>
        /// <param name="Timings">The ordered persistence sub-stage timings.</param>
        /// <param name="Counts">The persistence count values associated with the same run.</param>
        /// <param name="Completed">A value indicating whether the diagnostics represent a completed persistence attempt.</param>
        private sealed record PersistenceDiagnosticsDocument(
            ExtractionRunTiming[] Timings,
            ExtractionRunPersistenceCounts Counts,
            bool Completed);

    }
}
