using System.Text.Json;
using Archon.Application.Graph.Persistence;
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
    /// Persists WP012 findings, finding history, and suppression overlays in Neo4j through the application finding-store port.
    /// </summary>
    public sealed class Neo4jFindingStore : IFindingStore
    {
        /// <summary>
        /// Opens Neo4j sessions for finding reads and writes.
        /// </summary>
        private readonly INeo4jSessionProvider _sessionProvider;

        /// <summary>
        /// Ensures graph constraints and indexes exist before finding writes run.
        /// </summary>
        private readonly IArchitectureGraphInitializer _graphInitializer;

        /// <summary>
        /// Maps finding records to Neo4j parameter dictionaries.
        /// </summary>
        private readonly Neo4jSnapshotPersistenceMapper _mapper;

        /// <summary>
        /// Logs credential-safe persistence stage events.
        /// </summary>
        private readonly Neo4jPersistenceStageLogger _stageLogger;

        /// <summary>
        /// Applies suppression overlays consistently with application construction semantics.
        /// </summary>
        private readonly FindingConstructionService _findingConstructionService;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jFindingStore"/> class.
        /// </summary>
        /// <param name="sessionProvider">The session provider used to open Neo4j sessions.</param>
        /// <param name="graphInitializer">The graph initializer used to ensure schema exists before writing finding data.</param>
        /// <param name="mapper">The mapper used to convert finding records into Neo4j parameter dictionaries.</param>
        /// <param name="stageLogger">The credential-safe logger for persistence stages.</param>
        public Neo4jFindingStore(
            INeo4jSessionProvider sessionProvider,
            IArchitectureGraphInitializer graphInitializer,
            Neo4jSnapshotPersistenceMapper mapper,
            Neo4jPersistenceStageLogger stageLogger)
        {
            // Dependencies mirror the rule catalog store so finding persistence remains an infrastructure adapter behind Application contracts.
            _sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
            _graphInitializer = graphInitializer ?? throw new ArgumentNullException(nameof(graphInitializer));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _stageLogger = stageLogger ?? throw new ArgumentNullException(nameof(stageLogger));
            _findingConstructionService = new FindingConstructionService();
        }

        /// <summary>
        /// Upserts snapshot-owned finding records and creates rule, node, and evidence links where referenced records exist.
        /// </summary>
        /// <param name="findings">The finding records to persist.</param>
        /// <param name="cancellationToken">The cancellation token that can stop schema initialization or transaction execution.</param>
        /// <returns>A result describing the finding upsert outcome and safe diagnostics.</returns>
        public async Task<FindingUpsertResult> UpsertFindingsAsync(IEnumerable<FindingRecord> findings, CancellationToken cancellationToken)
        {
            // Neo4j MERGE identity is snapshotStableKey plus stableKey; no deletion is performed for omitted findings.
            ArgumentNullException.ThrowIfNull(findings);
            FindingRecord[] entries = findings.ToArray();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                GraphInitializationResult initializationResult = await _graphInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
                if (!initializationResult.Succeeded)
                {
                    return FindingUpsertResult.Failure(initializationResult.Errors.Select(static error => error.Message), initializationResult.Warnings.Select(static warning => warning.Message));
                }

                IReadOnlyList<SuppressFindingRequest> suppressions = await ReadSuppressionsAsync(cancellationToken).ConfigureAwait(false);
                FindingRecord[] suppressedEntries = entries.Select(finding => _findingConstructionService.ApplySuppression(finding, suppressions).Finding).ToArray();
                await PersistFindingsAsync(suppressedEntries, cancellationToken).ConfigureAwait(false);
                return FindingUpsertResult.Success(suppressedEntries.Length, initializationResult.Warnings.Select(static warning => warning.Message));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation is surfaced as a controlled failure so extraction diagnostics can report interruption clearly.
                return FindingUpsertResult.Failure(["Finding persistence was canceled."]);
            }
            catch (Neo4jException exception)
            {
                // Driver details are logged, while callers receive a credential-safe diagnostic.
                _stageLogger.LogStageFailed(exception, PersistenceStage.SnapshotPersistence, snapshotStableKey: null);
                return FindingUpsertResult.Failure(["Neo4j finding persistence failed."]);
            }
        }

        /// <summary>
        /// Retrieves persisted findings for one snapshot in deterministic stable-key order.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key whose findings should be retrieved.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before query execution.</param>
        /// <returns>The persisted findings for the requested snapshot.</returns>
        public async Task<IReadOnlyList<FindingRecord>> GetFindingsBySnapshotAsync(string snapshotStableKey, CancellationToken cancellationToken)
        {
            // Query projection uses first-class properties rather than parsing linked nodes or Neo4j internal IDs.
            string normalizedSnapshotKey = RequireText(snapshotStableKey, nameof(snapshotStableKey));
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Read);
            IResultCursor cursor = await session.RunAsync(FindingsBySnapshotCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["snapshotStableKey"] = normalizedSnapshotKey
            }).ConfigureAwait(false);
            IReadOnlyList<IRecord> records = await cursor.ToListAsync().ConfigureAwait(false);
            return records.Select(MapFindingRecord).ToArray();
        }

        /// <summary>
        /// Retrieves one persisted finding by snapshot and stable key.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the finding.</param>
        /// <param name="findingStableKey">The finding stable key to retrieve.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before query execution.</param>
        /// <returns>The matching finding, or <see langword="null"/> when no finding exists.</returns>
        public async Task<FindingRecord?> GetFindingAsync(string snapshotStableKey, string findingStableKey, CancellationToken cancellationToken)
        {
            // Snapshot and finding stable keys are both required because finding stable keys are snapshot-scoped.
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Read);
            IResultCursor cursor = await session.RunAsync(FindingByStableKeyCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["snapshotStableKey"] = RequireText(snapshotStableKey, nameof(snapshotStableKey)),
                ["stableKey"] = RequireText(findingStableKey, nameof(findingStableKey))
            }).ConfigureAwait(false);
            IRecord? record = await cursor.SingleOrDefaultAsync().ConfigureAwait(false);
            return record is null ? null : MapFindingRecord(record);
        }

        /// <summary>
        /// Retrieves cross-snapshot history seeds for the requested finding history keys.
        /// </summary>
        /// <param name="historyKeys">The deterministic finding history keys to resolve.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before query execution.</param>
        /// <returns>The history seeds known to Neo4j.</returns>
        public async Task<IReadOnlyList<FindingHistorySeed>> GetHistoryAsync(IEnumerable<string> historyKeys, CancellationToken cancellationToken)
        {
            // History lookup groups records by historyKey and resolves first/latest from logical snapshot stable-key properties, not database IDs.
            ArgumentNullException.ThrowIfNull(historyKeys);
            string[] requestedKeys = historyKeys.Where(static key => !string.IsNullOrWhiteSpace(key)).Select(static key => key.Trim()).Distinct(StringComparer.Ordinal).OrderBy(static key => key, StringComparer.Ordinal).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Read);
            IResultCursor cursor = await session.RunAsync(FindingHistoryCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["historyKeys"] = requestedKeys
            }).ConfigureAwait(false);
            IReadOnlyList<IRecord> records = await cursor.ToListAsync().ConfigureAwait(false);
            return records.Select(static record => new FindingHistorySeed(record["historyKey"].As<string>(), record["firstSeenSnapshotStableKey"].As<string>(), record["latestSeenSnapshotStableKey"].As<string>())).ToArray();
        }

        /// <summary>
        /// Persists suppression requests and applies them to matching findings without deleting underlying finding records.
        /// </summary>
        /// <param name="suppressionRequests">The suppression requests to persist and apply.</param>
        /// <param name="cancellationToken">The cancellation token that can stop persistence before query execution.</param>
        /// <returns>A result describing the suppression outcome.</returns>
        public async Task<SuppressionPersistenceResult> SuppressFindingsAsync(IEnumerable<SuppressFindingRequest> suppressionRequests, CancellationToken cancellationToken)
        {
            // Suppression requests are stored by history key and then applied to any current matching findings.
            ArgumentNullException.ThrowIfNull(suppressionRequests);
            SuppressFindingRequest[] requests = suppressionRequests.ToArray();
            List<SuppressFindingValidationError> validationErrors = [];
            try
            {
                GraphInitializationResult initializationResult = await _graphInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
                if (!initializationResult.Succeeded)
                {
                    return SuppressionPersistenceResult.Failure(initializationResult.Errors.Select(static error => error.Message), initializationResult.Warnings.Select(static warning => warning.Message));
                }

                int suppressedCount = await PersistSuppressionsAsync(requests, validationErrors, cancellationToken).ConfigureAwait(false);
                if (validationErrors.Count > 0)
                {
                    return SuppressionPersistenceResult.ValidationFailure(validationErrors, initializationResult.Warnings.Select(static warning => warning.Message));
                }

                return SuppressionPersistenceResult.Success(suppressedCount, initializationResult.Warnings.Select(static warning => warning.Message));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation is returned as a controlled failure so callers can report interruption clearly.
                return SuppressionPersistenceResult.Failure(["Finding suppression persistence was canceled."]);
            }
            catch (Neo4jException exception)
            {
                // Driver details are logged, while callers receive a credential-safe diagnostic.
                _stageLogger.LogStageFailed(exception, PersistenceStage.SnapshotPersistence, snapshotStableKey: null);
                return SuppressionPersistenceResult.Failure(["Neo4j finding suppression persistence failed."]);
            }
        }

        /// <summary>
        /// Persists findings in one Neo4j write transaction.
        /// </summary>
        /// <param name="findings">The findings to persist.</param>
        /// <param name="cancellationToken">The cancellation token checked before the transaction starts.</param>
        /// <returns>A task that completes after all statements have been consumed.</returns>
        private async Task PersistFindingsAsync(IReadOnlyList<FindingRecord> findings, CancellationToken cancellationToken)
        {
            // One transaction ensures a successful result does not hide a partially persisted finding batch.
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Write);
            await session.ExecuteWriteAsync(async transaction =>
            {
                foreach (FindingRecord finding in findings)
                {
                    IReadOnlyDictionary<string, object?> parameters = _mapper.MapFinding(finding);
                    await RunAsync(transaction, FindingMergeCypher, parameters).ConfigureAwait(false);
                    await RunAsync(transaction, FindingRuleRelationshipCypher, parameters).ConfigureAwait(false);
                    foreach (StableKey nodeStableKey in finding.AffectedNodeStableKeys)
                    {
                        await RunAsync(transaction, FindingAffectedNodeRelationshipCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["snapshotStableKey"] = finding.SnapshotStableKey.Value,
                            ["findingStableKey"] = finding.StableKey.Value,
                            ["nodeStableKey"] = nodeStableKey.Value
                        }).ConfigureAwait(false);
                    }

                    foreach (StableKey evidenceStableKey in finding.EvidenceStableKeys)
                    {
                        await RunAsync(transaction, FindingEvidenceRelationshipCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["snapshotStableKey"] = finding.SnapshotStableKey.Value,
                            ["findingStableKey"] = finding.StableKey.Value,
                            ["evidenceStableKey"] = evidenceStableKey.Value
                        }).ConfigureAwait(false);
                    }
                }

                return findings.Count;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads persisted suppression requests from Neo4j.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token checked before query execution.</param>
        /// <returns>The suppression requests currently stored in Neo4j.</returns>
        private async Task<IReadOnlyList<SuppressFindingRequest>> ReadSuppressionsAsync(CancellationToken cancellationToken)
        {
            // Suppressions are global by history key and are applied to matching snapshot findings during later upserts.
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Read);
            IResultCursor cursor = await session.RunAsync(SuppressionReadCypher).ConfigureAwait(false);
            IReadOnlyList<IRecord> records = await cursor.ToListAsync().ConfigureAwait(false);
            return records.Select(MapSuppressionRequest).ToArray();
        }

        /// <summary>
        /// Persists suppression requests and applies valid requests to matching findings.
        /// </summary>
        /// <param name="requests">The suppression requests to persist.</param>
        /// <param name="validationErrors">The validation error list receiving failed request validation.</param>
        /// <param name="cancellationToken">The cancellation token checked before transaction execution.</param>
        /// <returns>The number of findings updated as suppressed.</returns>
        private async Task<int> PersistSuppressionsAsync(IReadOnlyList<SuppressFindingRequest> requests, List<SuppressFindingValidationError> validationErrors, CancellationToken cancellationToken)
        {
            // The transaction records suppression intent even when no current finding exists, allowing later snapshots to inherit it.
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Write);
            return await session.ExecuteWriteAsync(async transaction =>
            {
                int suppressedCount = 0;
                foreach (SuppressFindingRequest request in requests)
                {
                    IResultCursor matchCursor = await transaction.RunAsync(FindingBySuppressionTargetCypher, MapSuppressionParameters(request)).ConfigureAwait(false);
                    IReadOnlyList<IRecord> matchingRecords = await matchCursor.ToListAsync().ConfigureAwait(false);
                    FindingRecord? firstFinding = matchingRecords.Count == 0 ? null : MapFindingRecord(matchingRecords[0]);
                    if (firstFinding is not null)
                    {
                        SuppressFindingResult validationResult = _findingConstructionService.ApplySuppression(firstFinding, [request]);
                        if (validationResult.ValidationErrors.Count > 0)
                        {
                            validationErrors.AddRange(validationResult.ValidationErrors);
                            continue;
                        }
                    }

                    await RunAsync(transaction, SuppressionMergeCypher, MapSuppressionParameters(request)).ConfigureAwait(false);
                    foreach (IRecord record in matchingRecords)
                    {
                        FindingRecord finding = MapFindingRecord(record);
                        FindingRecord suppressedFinding = _findingConstructionService.ApplySuppression(finding, [request]).Finding;
                        await RunAsync(transaction, FindingMergeCypher, _mapper.MapFinding(suppressedFinding)).ConfigureAwait(false);
                        suppressedCount++;
                    }
                }

                return suppressedCount;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes one parameterized Cypher statement and consumes the result stream.
        /// </summary>
        /// <param name="transaction">The active Neo4j transaction receiving the statement.</param>
        /// <param name="cypher">The parameterized Cypher statement to execute.</param>
        /// <param name="parameters">The parameter dictionary for the statement.</param>
        /// <returns>A task that completes after Neo4j has consumed the statement result.</returns>
        private static async Task RunAsync(IAsyncQueryRunner transaction, string cypher, IReadOnlyDictionary<string, object?> parameters)
        {
            // Consuming each cursor surfaces statement failures before subsequent persistence work continues.
            IResultCursor cursor = await transaction.RunAsync(cypher, parameters).ConfigureAwait(false);
            await cursor.ConsumeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Maps a Neo4j finding record into a domain finding record.
        /// </summary>
        /// <param name="record">The Neo4j record containing a <c>finding</c> map.</param>
        /// <returns>The mapped finding record.</returns>
        private static FindingRecord MapFindingRecord(IRecord record)
        {
            // Projection reads only stable logical properties and never Neo4j internal IDs.
            IReadOnlyDictionary<string, object> finding = record["finding"].As<IReadOnlyDictionary<string, object>>();
            GraphMetadata metadata = ReadMetadata(finding, "metadataJson");
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
                metadata,
                new Fingerprint(ReadString(finding, "fingerprint")));
        }

        /// <summary>
        /// Maps a Neo4j suppression record into an application suppression request.
        /// </summary>
        /// <param name="record">The Neo4j record containing a <c>suppression</c> map.</param>
        /// <returns>The mapped suppression request.</returns>
        private static SuppressFindingRequest MapSuppressionRequest(IRecord record)
        {
            // Suppression nodes preserve the same request fields used by application validation and matching.
            IReadOnlyDictionary<string, object> suppression = record["suppression"].As<IReadOnlyDictionary<string, object>>();
            return new SuppressFindingRequest(
                ReadString(suppression, "findingHistoryKey"),
                ReadString(suppression, "ruleCode"),
                ReadString(suppression, "ruleVersion"),
                ReadString(suppression, "primaryNodeStableKey"),
                ReadString(suppression, "reason"),
                ReadString(suppression, "suppressedBy"),
                ReadMetadata(suppression, "metadataJson"));
        }

        /// <summary>
        /// Maps a suppression request to Neo4j parameters.
        /// </summary>
        /// <param name="request">The suppression request to map.</param>
        /// <returns>A parameter dictionary for suppression statements.</returns>
        private static IReadOnlyDictionary<string, object?> MapSuppressionParameters(SuppressFindingRequest request)
        {
            // Suppression parameters are explicit and never include caller-supplied Cypher fragments.
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["findingHistoryKey"] = request.FindingHistoryKey,
                ["ruleCode"] = request.RuleCode,
                ["ruleVersion"] = request.RuleVersion,
                ["primaryNodeStableKey"] = request.PrimaryNodeStableKey,
                ["reason"] = request.Reason,
                ["suppressedBy"] = request.SuppressedBy,
                ["metadataJson"] = request.Metadata.ToCanonicalJson()
            };
        }

        /// <summary>
        /// Reads and parses graph metadata JSON from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property containing metadata JSON.</param>
        /// <returns>The parsed canonical graph metadata.</returns>
        private static GraphMetadata ReadMetadata(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // GraphMetadata does not expose a parse API, so JSON is converted to JsonElement and canonicalized through the public factory.
            string json = ReadString(values, propertyName);
            using JsonDocument document = JsonDocument.Parse(json);
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
            // Required persisted properties must be present because they map to non-null domain constructor parameters.
            return values.TryGetValue(propertyName, out object? value) && value is not null && !string.IsNullOrWhiteSpace(value.As<string>())
                ? value.As<string>()
                : throw new InvalidOperationException($"Neo4j finding projection is missing required property '{propertyName}'.");
        }

        /// <summary>
        /// Reads an optional string property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>The optional string property value.</returns>
        private static string? ReadNullableString(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Neo4j omits null-valued properties, so missing and null both map to null in the domain model.
            return values.TryGetValue(propertyName, out object? value) && value is not null ? value.As<string>() : null;
        }

        /// <summary>
        /// Reads an optional stable key property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>The stable key, or <see langword="null"/> when the property is absent.</returns>
        private static StableKey? ReadOptionalStableKey(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Optional links may be absent when a finding has no primary node or evidence.
            string? value = ReadNullableString(values, propertyName);
            return string.IsNullOrWhiteSpace(value) ? null : new StableKey(value);
        }

        /// <summary>
        /// Reads a decimal property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>The decimal value.</returns>
        private static decimal ReadDecimal(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Neo4j returns numeric values as driver numeric types; conversion through string keeps decimal parsing invariant.
            object value = values[propertyName];
            return decimal.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads a boolean property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>The boolean property value.</returns>
        private static bool ReadBoolean(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Unknown-state flags are stored as first-class booleans.
            return values.TryGetValue(propertyName, out object? value) && value is not null && value.As<bool>();
        }

        /// <summary>
        /// Reads a stable-key array property from a Neo4j property map.
        /// </summary>
        /// <param name="values">The Neo4j property map.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>A deterministic stable-key list.</returns>
        private static IReadOnlyList<StableKey> ReadStableKeyArray(IReadOnlyDictionary<string, object> values, string propertyName)
        {
            // Link arrays preserve multi-node and multi-evidence relationships even before query DTOs are introduced.
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
        /// Requires a non-empty text value and returns its trimmed form.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <param name="parameterName">The parameter name used for validation failures.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Store lookups require explicit stable identities to avoid ambiguous graph queries.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }

        /// <summary>
        /// Gets the parameterized Cypher used to upsert a snapshot-scoped finding node.
        /// </summary>
        public const string FindingMergeCypher = @"
MERGE (finding:ArchonFinding { snapshotStableKey: $snapshotStableKey, stableKey: $stableKey })
SET finding.ruleCode = $ruleCode,
    finding.ruleVersion = $ruleVersion,
    finding.severity = $severity,
    finding.status = $status,
    finding.title = $title,
    finding.description = $description,
    finding.knowledgeKind = $knowledgeKind,
    finding.confidence = $confidence,
    finding.hasUnknownData = $hasUnknownData,
    finding.unknownReason = $unknownReason,
    finding.primaryNodeStableKey = $primaryNodeStableKey,
    finding.primaryEvidenceStableKey = $primaryEvidenceStableKey,
    finding.firstSeenSnapshotStableKey = $firstSeenSnapshotStableKey,
    finding.latestSeenSnapshotStableKey = $latestSeenSnapshotStableKey,
    finding.suppressionReason = $suppressionReason,
    finding.suppressedBy = $suppressedBy,
    finding.affectedNodeStableKeys = $affectedNodeStableKeys,
    finding.evidenceStableKeys = $evidenceStableKeys,
    finding.historyKey = $historyKey,
    finding.metadataJson = $metadataJson,
    finding.fingerprint = $fingerprint";

        /// <summary>
        /// Gets the parameterized Cypher used to link a finding to the exact rule version that classified it.
        /// </summary>
        public const string FindingRuleRelationshipCypher = @"
MATCH (finding:ArchonFinding { snapshotStableKey: $snapshotStableKey, stableKey: $stableKey })
MATCH (rule:ArchonRule { ruleCode: $ruleCode, ruleVersion: $ruleVersion })
MERGE (finding)-[:CLASSIFIED_BY_RULE]->(rule)";

        /// <summary>
        /// Gets the parameterized Cypher used to link findings to affected architecture nodes.
        /// </summary>
        public const string FindingAffectedNodeRelationshipCypher = @"
MATCH (finding:ArchonFinding { snapshotStableKey: $snapshotStableKey, stableKey: $findingStableKey })
MATCH (node:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $nodeStableKey })
MERGE (finding)-[:PRIMARY_NODE]->(node)";

        /// <summary>
        /// Gets the parameterized Cypher used to link findings to evidence records.
        /// </summary>
        public const string FindingEvidenceRelationshipCypher = @"
MATCH (finding:ArchonFinding { snapshotStableKey: $snapshotStableKey, stableKey: $findingStableKey })
MATCH (evidence:ArchonEvidence { snapshotStableKey: $snapshotStableKey, stableKey: $evidenceStableKey })
MERGE (finding)-[:SUPPORTED_BY_EVIDENCE]->(evidence)";

        private const string FindingsBySnapshotCypher = @"
MATCH (finding:ArchonFinding { snapshotStableKey: $snapshotStableKey })
RETURN properties(finding) AS finding
ORDER BY finding.stableKey";

        private const string FindingByStableKeyCypher = @"
MATCH (finding:ArchonFinding { snapshotStableKey: $snapshotStableKey, stableKey: $stableKey })
RETURN properties(finding) AS finding";

        private const string FindingHistoryCypher = @"
MATCH (finding:ArchonFinding)
WHERE finding.historyKey IN $historyKeys
RETURN finding.historyKey AS historyKey,
       min(coalesce(finding.firstSeenSnapshotStableKey, finding.snapshotStableKey)) AS firstSeenSnapshotStableKey,
       max(coalesce(finding.latestSeenSnapshotStableKey, finding.snapshotStableKey)) AS latestSeenSnapshotStableKey
ORDER BY historyKey";

        private const string SuppressionReadCypher = @"
MATCH (suppression:ArchonFindingSuppression)
RETURN properties(suppression) AS suppression
ORDER BY suppression.findingHistoryKey";

        private const string SuppressionMergeCypher = @"
MERGE (suppression:ArchonFindingSuppression { findingHistoryKey: $findingHistoryKey })
SET suppression.ruleCode = $ruleCode,
    suppression.ruleVersion = $ruleVersion,
    suppression.primaryNodeStableKey = $primaryNodeStableKey,
    suppression.reason = $reason,
    suppression.suppressedBy = $suppressedBy,
    suppression.metadataJson = $metadataJson";

        private const string FindingBySuppressionTargetCypher = @"
MATCH (finding:ArchonFinding { historyKey: $findingHistoryKey, ruleCode: $ruleCode, ruleVersion: $ruleVersion, primaryNodeStableKey: $primaryNodeStableKey })
RETURN properties(finding) AS finding
ORDER BY finding.snapshotStableKey, finding.stableKey";
    }
}
