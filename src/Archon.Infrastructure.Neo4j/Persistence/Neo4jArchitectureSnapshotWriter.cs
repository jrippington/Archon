using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Domain.Graph.Model;
using Archon.Infrastructure.Neo4j.Configuration;
using Archon.Infrastructure.Neo4j.Driver;
using Archon.Infrastructure.Neo4j.Schema;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.Persistence
{
    /// <summary>
    /// Persists the Work Item 4 minimal architecture snapshot shape into Neo4j.
    /// </summary>
    /// <remarks>
        /// The writer persists repositories, solutions, one snapshot header, architecture nodes, canonical evidence nodes, metrics,
        /// snapshot-to-solution relationships, node-to-evidence relationships, metric-to-evidence relationships, and metric-to-node
        /// relationships. High-volume list-parameter batching is introduced incrementally while this adapter preserves one transaction and
        /// stable-key merge semantics.
    /// </remarks>
    public sealed class Neo4jArchitectureSnapshotWriter : IArchitectureSnapshotWriter
    {
        private readonly INeo4jSessionProvider _sessionProvider;
        private readonly IArchitectureGraphInitializer _graphInitializer;
        private readonly Neo4jSnapshotPersistenceMapper _mapper;
        private readonly Neo4jPersistenceStageLogger _stageLogger;
        private readonly int _persistenceBatchSize;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jArchitectureSnapshotWriter"/> class.
        /// </summary>
        /// <param name="sessionProvider">The session provider used to open Neo4j write sessions.</param>
        /// <param name="graphInitializer">The graph initializer used to ensure schema exists before writing data.</param>
        /// <param name="mapper">The mapper that converts domain facts into Neo4j parameter dictionaries.</param>
        /// <param name="stageLogger">The credential-safe logger for persistence stages.</param>
        /// <param name="options">The validated Neo4j options that provide persistence batch-size tuning.</param>
        public Neo4jArchitectureSnapshotWriter(
            INeo4jSessionProvider sessionProvider,
            IArchitectureGraphInitializer graphInitializer,
            Neo4jSnapshotPersistenceMapper mapper,
            Neo4jPersistenceStageLogger stageLogger,
            IOptions<Neo4jOptions> options)
        {
            // Dependencies are stored only; no graph work happens until WriteSnapshotAsync is called by an explicit caller. The batch size
            // is copied from validated options once because the writer is a singleton and uses a consistent value for each write attempt.
            ArgumentNullException.ThrowIfNull(options);
            _sessionProvider = sessionProvider;
            _graphInitializer = graphInitializer;
            _mapper = mapper;
            _stageLogger = stageLogger;
            _persistenceBatchSize = options.Value.PersistenceBatchSize;
        }

        /// <summary>
        /// Persists one minimal architecture snapshot into Neo4j using stable-key merge semantics.
        /// </summary>
        /// <param name="snapshot">The assembled architecture snapshot containing the minimal Work Item 4 sections.</param>
        /// <param name="cancellationToken">A token that cancels persistence before schema initialization or before the write transaction starts.</param>
        /// <returns>A result describing success, counts, warnings, and safe errors.</returns>
        public async Task<SnapshotPersistenceResult> WriteSnapshotAsync(ExtractedArchitectureSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            // The writer validates application input before any Neo4j transaction so invalid snapshots do not produce partial data.
            ArgumentNullException.ThrowIfNull(snapshot);
            string? snapshotStableKey = snapshot.SnapshotHeader?.StableKey.Value;
            Neo4jPersistenceDiagnosticCollector diagnostics = new(snapshot);
            _stageLogger.LogStageStarting(PersistenceStage.SnapshotPersistence, snapshotStableKey);

            try
            {
                SnapshotValidationResult validation = diagnostics.Measure("Persistence.PrepareSnapshot", () => ValidateSnapshot(snapshot));
                if (!validation.Succeeded)
                {
                    return SnapshotPersistenceResult.Failure(snapshotStableKey, validation.Error!, diagnostics: diagnostics.Complete(completed: false));
                }

                GraphInitializationResult initializationResult = await diagnostics.MeasureAsync("Persistence.Indexing", () => _graphInitializer.InitializeAsync(cancellationToken)).ConfigureAwait(false);
                if (!initializationResult.Succeeded)
                {
                    PersistenceError error = initializationResult.Errors.FirstOrDefault()
                        ?? new PersistenceError(PersistenceStage.SchemaInitialization, "GraphInitializationFailed", "Graph initialization failed before snapshot persistence.");
                    return SnapshotPersistenceResult.Failure(snapshotStableKey, error, initializationResult.Warnings, diagnostics.Complete(completed: false));
                }

                CanonicalEvidenceSet canonicalEvidence = diagnostics.Measure("Persistence.MaterializePayload", () => BuildCanonicalEvidence(snapshot.Evidence));
                diagnostics.RecordAlreadyMaterializedStage("Persistence.NormalizeIdentities");
                SnapshotPersistenceCounts counts = await PersistValidatedSnapshotAsync(snapshot, canonicalEvidence, diagnostics, cancellationToken).ConfigureAwait(false);

                _stageLogger.LogStageCompleted(PersistenceStage.SnapshotPersistence, snapshotStableKey);
                return SnapshotPersistenceResult.Success(snapshotStableKey!, counts, initializationResult.Warnings, diagnostics.Complete(completed: true));
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation is represented as a safe failure so callers can distinguish interruption from data validation errors.
                _stageLogger.LogStageFailed(exception, PersistenceStage.SnapshotPersistence, snapshotStableKey);
                return SnapshotPersistenceResult.Failure(
                    snapshotStableKey,
                    new PersistenceError(PersistenceStage.SnapshotPersistence, "SnapshotPersistenceCanceled", "Snapshot persistence was canceled."),
                    diagnostics: diagnostics.Complete(completed: false));
            }
            catch (Neo4jException exception)
            {
                // Neo4j failures are logged with exception details but returned as credential-safe application diagnostics.
                _stageLogger.LogStageFailed(exception, PersistenceStage.SnapshotPersistence, snapshotStableKey);
                return SnapshotPersistenceResult.Failure(
                    snapshotStableKey,
                    new PersistenceError(PersistenceStage.SnapshotPersistence, "SnapshotPersistenceFailed", "Neo4j snapshot persistence failed."),
                    diagnostics: diagnostics.Complete(completed: false));
            }
            catch (Exception exception)
            {
                // Infrastructure failures outside the Neo4j exception hierarchy are still translated so diagnostic capture never masks the original persistence attempt.
                _stageLogger.LogStageFailed(exception, PersistenceStage.SnapshotPersistence, snapshotStableKey);
                return SnapshotPersistenceResult.Failure(
                    snapshotStableKey,
                    new PersistenceError(PersistenceStage.SnapshotPersistence, "SnapshotPersistenceFailed", "Snapshot persistence failed. Review server logs for details."),
                    diagnostics: diagnostics.Complete(completed: false));
            }
        }

        /// <summary>
        /// Validates the minimal snapshot structure before persistence starts.
        /// </summary>
        /// <param name="snapshot">The snapshot to validate.</param>
        /// <returns>A validation result containing a safe error when the snapshot is invalid.</returns>
        private static SnapshotValidationResult ValidateSnapshot(ExtractedArchitectureSnapshot snapshot)
        {
            // Work Item 4 requires one snapshot header and explicit references between header, repository, solution, nodes, and evidence.
            if (snapshot.SnapshotHeader is null)
            {
                return SnapshotValidationResult.Failure("MissingSnapshotHeader", "Snapshot persistence requires a snapshot header.");
            }

            string snapshotStableKey = snapshot.SnapshotHeader.StableKey.Value;
            string repositoryStableKey = snapshot.SnapshotHeader.RepositoryStableKey.Value;
            if (!snapshot.Repositories.Any(repository => StringComparer.Ordinal.Equals(repository.StableKey.Value, repositoryStableKey)))
            {
                return SnapshotValidationResult.Failure("MissingRepositoryReference", "Snapshot persistence requires the referenced repository record.");
            }

            if (snapshot.Solutions.Any(solution => !snapshot.Repositories.Any(repository => StringComparer.Ordinal.Equals(repository.StableKey.Value, solution.RepositoryStableKey.Value))))
            {
                return SnapshotValidationResult.Failure("MissingSolutionRepositoryReference", "Snapshot persistence requires every solution to reference a supplied repository.");
            }

            if (snapshot.Nodes.Any(node => !StringComparer.Ordinal.Equals(node.SnapshotStableKey.Value, snapshotStableKey))
                || snapshot.Evidence.Any(evidence => !StringComparer.Ordinal.Equals(evidence.SnapshotStableKey.Value, snapshotStableKey))
                || snapshot.Metrics.Any(metric => !StringComparer.Ordinal.Equals(metric.SnapshotStableKey.Value, snapshotStableKey)))
            {
                return SnapshotValidationResult.Failure("MismatchedSnapshotScope", "Minimal snapshot records must use the snapshot header stable key as their snapshot scope.");
            }

            HashSet<string> evidenceStableKeys = snapshot.Evidence.Select(evidence => evidence.StableKey.Value).ToHashSet(StringComparer.Ordinal);
            if (snapshot.Nodes.Any(node => node.PrimaryEvidenceStableKey is not null && !evidenceStableKeys.Contains(node.PrimaryEvidenceStableKey.Value.Value)))
            {
                return SnapshotValidationResult.Failure("MissingNodeEvidenceReference", "Architecture nodes must not reference missing primary evidence records.");
            }

            if (snapshot.Metrics.Any(metric => metric.PrimaryEvidenceStableKey is not null && !evidenceStableKeys.Contains(metric.PrimaryEvidenceStableKey.Value.Value)))
            {
                return SnapshotValidationResult.Failure("MissingMetricEvidenceReference", "Metrics must not reference missing primary evidence records.");
            }

            HashSet<string> nodeStableKeys = snapshot.Nodes.Select(node => node.StableKey.Value).ToHashSet(StringComparer.Ordinal);
            if (snapshot.Metrics.Any(metric => metric.NodeStableKey is not null && !nodeStableKeys.Contains(metric.NodeStableKey.Value.Value)))
            {
                return SnapshotValidationResult.Failure("MissingMetricNodeReference", "Metrics must not reference missing architecture node targets.");
            }

            return SnapshotValidationResult.Success();
        }

        /// <summary>
        /// Deduplicates evidence records within one snapshot using canonical evidence payload identity.
        /// </summary>
        /// <param name="evidenceRecords">The evidence records supplied by the snapshot.</param>
        /// <returns>A canonical evidence set containing unique evidence nodes and stable-key remapping for duplicates.</returns>
        private CanonicalEvidenceSet BuildCanonicalEvidence(IReadOnlyList<EvidenceRecord> evidenceRecords)
        {
            // The first equivalent evidence record becomes canonical; duplicate stable keys are remapped to that canonical stable key.
            Dictionary<string, EvidenceRecord> canonicalByPayload = new(StringComparer.Ordinal);
            Dictionary<string, string> canonicalStableKeyByInputStableKey = new(StringComparer.Ordinal);

            foreach (EvidenceRecord evidence in evidenceRecords)
            {
                string deduplicationKey = _mapper.GetEvidenceDeduplicationKey(evidence);
                if (!canonicalByPayload.TryGetValue(deduplicationKey, out EvidenceRecord? canonical))
                {
                    canonical = evidence;
                    canonicalByPayload.Add(deduplicationKey, canonical);
                }

                canonicalStableKeyByInputStableKey[evidence.StableKey.Value] = canonical.StableKey.Value;
            }

            return new CanonicalEvidenceSet(canonicalByPayload.Values.ToArray(), canonicalStableKeyByInputStableKey);
        }

        /// <summary>
        /// Persists a validated minimal snapshot in a single Neo4j write transaction.
        /// </summary>
        /// <param name="snapshot">The validated snapshot to persist.</param>
        /// <param name="canonicalEvidence">The canonical evidence records and duplicate-stable-key remapping.</param>
        /// <param name="diagnostics">The diagnostic collector that measures nested persistence sub-stages and final count values.</param>
        /// <param name="cancellationToken">A token that cancels before transaction execution starts.</param>
        /// <returns>The aggregate persisted counts for the transaction.</returns>
        private async Task<SnapshotPersistenceCounts> PersistValidatedSnapshotAsync(ExtractedArchitectureSnapshot snapshot, CanonicalEvidenceSet canonicalEvidence, Neo4jPersistenceDiagnosticCollector diagnostics, CancellationToken cancellationToken)
        {
            // A single write transaction prevents a completed result from being returned for partially persisted minimal snapshots.
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Write);
            int operationCount = 0;

            SnapshotPersistenceCounts counts = await diagnostics.MeasureAsync(
                "Persistence.Commit",
                () => session.ExecuteWriteAsync(
                async transaction =>
                {
                    await diagnostics.MeasureAsync("Persistence.WriteRepositories", async () =>
                    {
                        operationCount += await RunBatchesAsync(
                            transaction,
                            RepositoryMergeBatchCypher,
                            "repositories",
                            snapshot.Repositories,
                            static (mapper, repository) => mapper.MapRepository(repository)).ConfigureAwait(false);
                    }).ConfigureAwait(false);

                    await diagnostics.MeasureAsync("Persistence.WriteSolutions", async () =>
                    {
                        operationCount += await RunBatchesAsync(
                            transaction,
                            SolutionMergeBatchCypher,
                            "solutions",
                            snapshot.Solutions,
                            static (mapper, solution) => mapper.MapSolution(solution)).ConfigureAwait(false);
                    }).ConfigureAwait(false);

                    await diagnostics.MeasureAsync("Persistence.WriteSnapshotHeader", async () =>
                    {
                        await RunAsync(transaction, SnapshotMergeCypher, _mapper.MapSnapshot(snapshot.SnapshotHeader!)).ConfigureAwait(false);
                        operationCount++;
                    }).ConfigureAwait(false);

                    await diagnostics.MeasureAsync("Persistence.WriteNodes", async () =>
                    {
                        // Architecture nodes now use the same bounded list-parameter batching as other high-volume snapshot sections. The
                        // operation count therefore records executed Cypher batches rather than individual node rows, which is the diagnostic
                        // meaning required for post-batching persistence overhead analysis.
                        operationCount += await RunBatchesAsync(
                            transaction,
                            NodeMergeBatchCypher,
                            "nodes",
                            snapshot.Nodes,
                            static (mapper, node) => mapper.MapNode(node)).ConfigureAwait(false);
                    }).ConfigureAwait(false);

                    await diagnostics.MeasureAsync("Persistence.WriteMetrics", async () =>
                    {
                        // Metric rows are a high-volume persistence hotspot, so one bounded UNWIND batch now writes many metric nodes
                        // while operation count still records the number of Cypher executions instead of the number of metric rows.
                        operationCount += await RunBatchesAsync(
                            transaction,
                            MetricMergeBatchCypher,
                            "metrics",
                            snapshot.Metrics,
                            static (mapper, metric) => mapper.MapMetric(metric)).ConfigureAwait(false);
                    }).ConfigureAwait(false);

                    await diagnostics.MeasureAsync("Persistence.WriteEvidence", async () =>
                    {
                        // Evidence is canonicalized before this stage, so the batch contains only the deduplicated records that should become
                        // ArchonEvidence nodes. Relationship stages continue to use the canonical stable-key map for duplicate input records.
                        operationCount += await RunBatchesAsync(
                            transaction,
                            EvidenceMergeBatchCypher,
                            "evidenceRecords",
                            canonicalEvidence.Records,
                            static (mapper, evidence) => mapper.MapEvidence(evidence)).ConfigureAwait(false);
                    }).ConfigureAwait(false);

                    RelationshipWriteCounts relationshipWriteCounts = await diagnostics.MeasureAsync("Persistence.WriteRelationships", async () =>
                    {
                        string snapshotStableKey = snapshot.SnapshotHeader!.StableKey.Value;
                        IReadOnlyList<IReadOnlyDictionary<string, object?>> snapshotSolutionRelationships = MapSnapshotSolutionRelationships(snapshotStableKey, snapshot.Solutions);
                        IReadOnlyList<IReadOnlyDictionary<string, object?>> nodeEvidenceRelationships = MapNodeEvidenceRelationships(snapshotStableKey, snapshot.Nodes, canonicalEvidence);
                        IReadOnlyList<IReadOnlyDictionary<string, object?>> metricEvidenceRelationships = MapMetricEvidenceRelationships(snapshotStableKey, snapshot.Metrics, canonicalEvidence);
                        IReadOnlyList<IReadOnlyDictionary<string, object?>> metricTargetRelationships = MapMetricTargetRelationships(snapshotStableKey, snapshot.Metrics);

                        operationCount += await diagnostics.MeasureAsync(
                            "Persistence.WriteSnapshotSolutionRelationships",
                            () => RunValidatedRelationshipBatchesAsync(transaction, SnapshotSolutionRelationshipBatchCypher, "relationships", snapshotSolutionRelationships, "snapshot-to-solution")).ConfigureAwait(false);
                        operationCount += await diagnostics.MeasureAsync(
                            "Persistence.WriteNodeEvidenceRelationships",
                            () => RunValidatedRelationshipBatchesAsync(transaction, NodeEvidenceRelationshipBatchCypher, "relationships", nodeEvidenceRelationships, "node-to-evidence")).ConfigureAwait(false);
                        operationCount += await diagnostics.MeasureAsync(
                            "Persistence.WriteMetricEvidenceRelationships",
                            () => RunValidatedRelationshipBatchesAsync(transaction, MetricEvidenceRelationshipBatchCypher, "relationships", metricEvidenceRelationships, "metric-to-evidence")).ConfigureAwait(false);
                        operationCount += await diagnostics.MeasureAsync(
                            "Persistence.WriteMetricTargetRelationships",
                            () => RunValidatedRelationshipBatchesAsync(transaction, MetricNodeTargetRelationshipBatchCypher, "relationships", metricTargetRelationships, "metric-to-node")).ConfigureAwait(false);

                        return new RelationshipWriteCounts(snapshotSolutionRelationships.Count, nodeEvidenceRelationships.Count, metricEvidenceRelationships.Count, metricTargetRelationships.Count);
                    }).ConfigureAwait(false);

                    return new SnapshotPersistenceCounts(
                        snapshot.Repositories.Count,
                        snapshot.Solutions.Count,
                        1,
                        snapshot.Nodes.Count,
                        canonicalEvidence.Records.Count,
                        architectureRelationships: 0,
                        snapshotSolutionRelationships: relationshipWriteCounts.SnapshotSolutionRelationships,
                        nodeEvidenceRelationships: relationshipWriteCounts.NodeEvidenceRelationships,
                        relationshipEndpointRelationships: 0,
                        relationshipEvidenceRelationships: 0,
                        metrics: snapshot.Metrics.Count,
                        metricEvidenceRelationships: relationshipWriteCounts.MetricEvidenceRelationships,
                        metricTargetRelationships: relationshipWriteCounts.MetricTargetRelationships);
                })).ConfigureAwait(false);
            diagnostics.UpdateCompletedCounts(snapshot, counts, operationCount, batchCount: 1);
            return counts;
        }

        /// <summary>
        /// Executes one Cypher statement and consumes the result stream.
        /// </summary>
        /// <param name="transaction">The active Neo4j transaction receiving the statement.</param>
        /// <param name="cypher">The parameterized Cypher statement to execute.</param>
        /// <param name="parameters">The parameter dictionary for the statement.</param>
        /// <returns>A task that completes after Neo4j has consumed the statement result.</returns>
        private static async Task RunAsync(IAsyncQueryRunner transaction, string cypher, IReadOnlyDictionary<string, object?> parameters)
        {
            // Consuming each cursor surfaces statement failures before the next persistence stage runs.
            IResultCursor cursor = await transaction.RunAsync(cypher, parameters).ConfigureAwait(false);
            await cursor.ConsumeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Maps homogeneous graph records and executes them through the bounded batch executor.
        /// </summary>
        /// <typeparam name="TRecord">The graph record type being mapped for one static persistence statement.</typeparam>
        /// <param name="transaction">The active Neo4j transaction receiving each batch statement.</param>
        /// <param name="cypher">The static parameterized Cypher statement to execute for every non-empty batch.</param>
        /// <param name="parameterName">The Cypher parameter name that receives each mapped record batch.</param>
        /// <param name="records">The graph records to map and partition into configured-size batches.</param>
        /// <param name="mapRecord">The mapper function that converts one graph record to Neo4j parameters.</param>
        /// <returns>The number of Cypher executions performed for the supplied records.</returns>
        private async Task<int> RunBatchesAsync<TRecord>(
            IAsyncQueryRunner transaction,
            string cypher,
            string parameterName,
            IReadOnlyList<TRecord> records,
            Func<Neo4jSnapshotPersistenceMapper, TRecord, IReadOnlyDictionary<string, object?>> mapRecord)
        {
            // Mapping is performed before execution so each Cypher batch receives only parameter values and the Cypher text remains static.
            List<IReadOnlyDictionary<string, object?>> mappedRecords = new(capacity: records.Count);
            foreach (TRecord record in records)
            {
                mappedRecords.Add(mapRecord(_mapper, record));
            }

            return await Neo4jPersistenceBatchExecutor.ExecuteBatchesAsync(transaction, cypher, parameterName, mappedRecords, _persistenceBatchSize).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes relationship payloads through bounded list-parameter batches and verifies every requested endpoint pair matched graph records.
        /// </summary>
        /// <param name="transaction">The active Neo4j transaction receiving each relationship batch statement.</param>
        /// <param name="cypher">The static relationship Cypher statement that returns a <c>matchedRows</c> count for each batch.</param>
        /// <param name="parameterName">The Cypher list parameter name that receives the current relationship batch.</param>
        /// <param name="relationships">The already materialized relationship endpoint payloads to write.</param>
        /// <param name="relationshipFamily">The safe relationship-family name used in controlled validation failures.</param>
        /// <returns>The number of Cypher executions performed for the supplied relationship payloads.</returns>
        private async Task<int> RunValidatedRelationshipBatchesAsync(
            IAsyncQueryRunner transaction,
            string cypher,
            string parameterName,
            IReadOnlyList<IReadOnlyDictionary<string, object?>> relationships,
            string relationshipFamily)
        {
            // Relationship batches need stronger validation than node upserts because a MATCH miss can otherwise turn into a silent no-op.
            // Each statement returns how many input rows matched endpoints, and the writer fails the transaction if that count differs from
            // the bounded batch size. The exception message uses only a safe family label and counts, never Cypher text or parameter values.
            if (relationships.Count == 0)
            {
                return 0;
            }

            int operationCount = 0;
            for (int offset = 0; offset < relationships.Count; offset += _persistenceBatchSize)
            {
                int currentBatchSize = Math.Min(_persistenceBatchSize, relationships.Count - offset);
                List<IReadOnlyDictionary<string, object?>> batch = new(capacity: currentBatchSize);
                for (int index = 0; index < currentBatchSize; index++)
                {
                    batch.Add(relationships[offset + index]);
                }

                Dictionary<string, object> parameters = new(StringComparer.Ordinal)
                {
                    [parameterName] = batch
                };

                IResultCursor cursor = await transaction.RunAsync(cypher, parameters).ConfigureAwait(false);
                IRecord record = await cursor.SingleAsync().ConfigureAwait(false);
                int matchedRows = Convert.ToInt32(record["matchedRows"], System.Globalization.CultureInfo.InvariantCulture);
                await cursor.ConsumeAsync().ConfigureAwait(false);

                if (matchedRows != currentBatchSize)
                {
                    throw new InvalidOperationException($"Neo4j relationship persistence matched {matchedRows} of {currentBatchSize} {relationshipFamily} relationship endpoints.");
                }

                operationCount++;
            }

            return operationCount;
        }

        /// <summary>
        /// Materializes snapshot-to-solution relationship endpoint rows for batched Cypher execution.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the relationship family.</param>
        /// <param name="solutions">The solution records included by the snapshot.</param>
        /// <returns>A list of stable-key endpoint payloads for snapshot-to-solution relationships.</returns>
        private static IReadOnlyList<IReadOnlyDictionary<string, object?>> MapSnapshotSolutionRelationships(string snapshotStableKey, IReadOnlyList<SolutionModel> solutions)
        {
            // The snapshot endpoint is repeated per row so the Cypher statement can stay fully static and parameterized.
            List<IReadOnlyDictionary<string, object?>> relationships = new(capacity: solutions.Count);
            foreach (SolutionModel solution in solutions)
            {
                relationships.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["snapshotStableKey"] = snapshotStableKey,
                    ["solutionStableKey"] = solution.StableKey.Value
                });
            }

            return relationships;
        }

        /// <summary>
        /// Materializes node-to-evidence support relationship endpoint rows with canonical evidence stable keys.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes both node and evidence endpoints.</param>
        /// <param name="nodes">The architecture nodes whose primary evidence links should be created.</param>
        /// <param name="canonicalEvidence">The canonical evidence set used to remap duplicate evidence stable keys.</param>
        /// <returns>A list of stable-key endpoint payloads for node evidence support relationships.</returns>
        private static IReadOnlyList<IReadOnlyDictionary<string, object?>> MapNodeEvidenceRelationships(string snapshotStableKey, IReadOnlyList<ArchitectureNode> nodes, CanonicalEvidenceSet canonicalEvidence)
        {
            // Node support links must use canonical evidence stable keys because duplicate evidence rows are not persisted as separate nodes.
            List<IReadOnlyDictionary<string, object?>> relationships = [];
            foreach (ArchitectureNode node in nodes.Where(static node => node.PrimaryEvidenceStableKey is not null))
            {
                string canonicalEvidenceStableKey = canonicalEvidence.CanonicalStableKeyByInputStableKey[node.PrimaryEvidenceStableKey!.Value.Value];
                relationships.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["snapshotStableKey"] = snapshotStableKey,
                    ["nodeStableKey"] = node.StableKey.Value,
                    ["evidenceStableKey"] = canonicalEvidenceStableKey
                });
            }

            return relationships;
        }

        /// <summary>
        /// Materializes metric-to-evidence support relationship endpoint rows with canonical evidence stable keys.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes both metric and evidence endpoints.</param>
        /// <param name="metrics">The metric records whose primary evidence links should be created.</param>
        /// <param name="canonicalEvidence">The canonical evidence set used to remap duplicate evidence stable keys.</param>
        /// <returns>A list of stable-key endpoint payloads for metric evidence support relationships.</returns>
        private static IReadOnlyList<IReadOnlyDictionary<string, object?>> MapMetricEvidenceRelationships(string snapshotStableKey, IReadOnlyList<MetricRecord> metrics, CanonicalEvidenceSet canonicalEvidence)
        {
            // Metric evidence links follow the same canonicalization rule as nodes so relationship targets match the actually persisted evidence nodes.
            List<IReadOnlyDictionary<string, object?>> relationships = [];
            foreach (MetricRecord metric in metrics.Where(static metric => metric.PrimaryEvidenceStableKey is not null))
            {
                string canonicalEvidenceStableKey = canonicalEvidence.CanonicalStableKeyByInputStableKey[metric.PrimaryEvidenceStableKey!.Value.Value];
                relationships.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["snapshotStableKey"] = snapshotStableKey,
                    ["metricStableKey"] = metric.StableKey.Value,
                    ["evidenceStableKey"] = canonicalEvidenceStableKey
                });
            }

            return relationships;
        }

        /// <summary>
        /// Materializes metric-to-node target relationship endpoint rows for metrics that measure architecture nodes.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes both metric and node endpoints.</param>
        /// <param name="metrics">The metric records whose node target links should be created.</param>
        /// <returns>A list of stable-key endpoint payloads for metric target relationships.</returns>
        private static IReadOnlyList<IReadOnlyDictionary<string, object?>> MapMetricTargetRelationships(string snapshotStableKey, IReadOnlyList<MetricRecord> metrics)
        {
            // Only metrics with node targets produce MEASURES_NODE relationships; edge-targeted metrics keep their edge stable key as a property for now.
            List<IReadOnlyDictionary<string, object?>> relationships = [];
            foreach (MetricRecord metric in metrics.Where(static metric => metric.NodeStableKey is not null))
            {
                relationships.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["snapshotStableKey"] = snapshotStableKey,
                    ["metricStableKey"] = metric.StableKey.Value,
                    ["nodeStableKey"] = metric.NodeStableKey!.Value.Value
                });
            }

            return relationships;
        }

        /// <summary>
        /// Upserts repository records from one bounded list-parameter batch using global stable-key identity.
        /// </summary>
        private const string RepositoryMergeBatchCypher = @"
UNWIND $repositories AS repositoryRow
MERGE (repository:ArchonRepository { stableKey: repositoryRow.stableKey })
SET repository.name = repositoryRow.name,
    repository.rootPath = repositoryRow.rootPath,
    repository.remoteUrl = repositoryRow.remoteUrl,
    repository.defaultBranch = repositoryRow.defaultBranch,
    repository.metadataJson = repositoryRow.metadataJson";

        /// <summary>
        /// Upserts solution records from one bounded list-parameter batch using global solution stable-key identity.
        /// </summary>
        private const string SolutionMergeBatchCypher = @"
UNWIND $solutions AS solutionRow
MERGE (solution:ArchonSolution { stableKey: solutionRow.stableKey })
SET solution.repositoryStableKey = solutionRow.repositoryStableKey,
    solution.name = solutionRow.name,
    solution.path = solutionRow.path,
    solution.metadataJson = solutionRow.metadataJson";

        /// <summary>
        /// Upserts the single snapshot header record for one persistence attempt.
        /// </summary>
        private const string SnapshotMergeCypher = @"
MERGE (snapshot:ArchonSnapshot { stableKey: $stableKey })
SET snapshot.repositoryStableKey = $repositoryStableKey,
    snapshot.branchName = $branchName,
    snapshot.commitSha = $commitSha,
    snapshot.startedUtc = $startedUtc,
    snapshot.completedUtc = $completedUtc,
    snapshot.extractionVersion = $extractionVersion,
    snapshot.status = $status,
    snapshot.warningsJson = $warningsJson,
    snapshot.errorsJson = $errorsJson,
    snapshot.metadataJson = $metadataJson";

        /// <summary>
        /// Upserts architecture node records from one bounded list-parameter batch using snapshot scope plus node stable key as the merge identity.
        /// </summary>
        private const string NodeMergeBatchCypher = @"
UNWIND $nodes AS nodeRow
MERGE (node:ArchonNode { snapshotStableKey: nodeRow.snapshotStableKey, stableKey: nodeRow.stableKey })
SET node.nodeKind = nodeRow.nodeKind,
    node.displayName = nodeRow.displayName,
    node.qualifiedName = nodeRow.qualifiedName,
    node.searchName = nodeRow.searchName,
    node.language = nodeRow.language,
    node.projectStableKey = nodeRow.projectStableKey,
    node.parentNodeStableKey = nodeRow.parentNodeStableKey,
    node.knowledgeKind = nodeRow.knowledgeKind,
    node.ownership = nodeRow.ownership,
    node.externalCategory = nodeRow.externalCategory,
    node.confidence = nodeRow.confidence,
    node.hasUnknownData = nodeRow.hasUnknownData,
    node.unknownReason = nodeRow.unknownReason,
    node.primaryEvidenceStableKey = nodeRow.primaryEvidenceStableKey,
    node.metadataJson = nodeRow.metadataJson,
    node.fingerprint = nodeRow.fingerprint";

        /// <summary>
        /// Upserts metric records from one bounded list-parameter batch using snapshot scope plus metric stable key as the merge identity.
        /// </summary>
        private const string MetricMergeBatchCypher = @"
UNWIND $metrics AS metricRow
MERGE (metric:ArchonMetric { snapshotStableKey: metricRow.snapshotStableKey, stableKey: metricRow.stableKey })
SET metric.metricKind = metricRow.metricKind,
    metric.scopeKind = metricRow.scopeKind,
    metric.nodeStableKey = metricRow.nodeStableKey,
    metric.edgeStableKey = metricRow.edgeStableKey,
    metric.primaryEvidenceStableKey = metricRow.primaryEvidenceStableKey,
    metric.name = metricRow.name,
    metric.numericValue = metricRow.numericValue,
    metric.textValue = metricRow.textValue,
    metric.unit = metricRow.unit,
    metric.confidence = metricRow.confidence,
    metric.hasUnknownData = metricRow.hasUnknownData,
    metric.unknownReason = metricRow.unknownReason,
    metric.metadataJson = metricRow.metadataJson,
    metric.fingerprint = metricRow.fingerprint";

        /// <summary>
        /// Upserts canonical evidence records from one bounded list-parameter batch using snapshot scope plus evidence stable key as the merge identity.
        /// </summary>
        private const string EvidenceMergeBatchCypher = @"
UNWIND $evidenceRecords AS evidenceRow
MERGE (evidence:ArchonEvidence { snapshotStableKey: evidenceRow.snapshotStableKey, stableKey: evidenceRow.stableKey })
SET evidence.evidenceKind = evidenceRow.evidenceKind,
    evidence.filePath = evidenceRow.filePath,
    evidence.startLine = evidenceRow.startLine,
    evidence.endLine = evidenceRow.endLine,
    evidence.symbolName = evidenceRow.symbolName,
    evidence.containingSymbol = evidenceRow.containingSymbol,
    evidence.snippetHash = evidenceRow.snippetHash,
    evidence.snippetPreview = evidenceRow.snippetPreview,
    evidence.knowledgeKind = evidenceRow.knowledgeKind,
    evidence.confidence = evidenceRow.confidence,
    evidence.hasUnknownData = evidenceRow.hasUnknownData,
    evidence.unknownReason = evidenceRow.unknownReason,
    evidence.metadataJson = evidenceRow.metadataJson,
    evidence.fingerprint = evidenceRow.fingerprint";

        /// <summary>
        /// Creates idempotent snapshot-to-solution relationships from one bounded endpoint batch and reports matched rows.
        /// </summary>
        private const string SnapshotSolutionRelationshipBatchCypher = @"
UNWIND $relationships AS relationshipRow
MATCH (snapshot:ArchonSnapshot { stableKey: relationshipRow.snapshotStableKey })
MATCH (solution:ArchonSolution { stableKey: relationshipRow.solutionStableKey })
MERGE (snapshot)-[:INCLUDES_SOLUTION]->(solution)
RETURN count(relationshipRow) AS matchedRows";

        /// <summary>
        /// Creates idempotent node-to-evidence support relationships from one bounded endpoint batch and reports matched rows.
        /// </summary>
        private const string NodeEvidenceRelationshipBatchCypher = @"
UNWIND $relationships AS relationshipRow
MATCH (node:ArchonNode { snapshotStableKey: relationshipRow.snapshotStableKey, stableKey: relationshipRow.nodeStableKey })
MATCH (evidence:ArchonEvidence { snapshotStableKey: relationshipRow.snapshotStableKey, stableKey: relationshipRow.evidenceStableKey })
MERGE (node)-[:SUPPORTED_BY_EVIDENCE]->(evidence)
RETURN count(relationshipRow) AS matchedRows";

        /// <summary>
        /// Creates idempotent metric-to-evidence support relationships from one bounded endpoint batch and reports matched rows.
        /// </summary>
        private const string MetricEvidenceRelationshipBatchCypher = @"
UNWIND $relationships AS relationshipRow
MATCH (metric:ArchonMetric { snapshotStableKey: relationshipRow.snapshotStableKey, stableKey: relationshipRow.metricStableKey })
MATCH (evidence:ArchonEvidence { snapshotStableKey: relationshipRow.snapshotStableKey, stableKey: relationshipRow.evidenceStableKey })
MERGE (metric)-[:SUPPORTED_BY_EVIDENCE]->(evidence)
RETURN count(relationshipRow) AS matchedRows";

        /// <summary>
        /// Creates idempotent metric-to-node target relationships from one bounded endpoint batch and reports matched rows.
        /// </summary>
        private const string MetricNodeTargetRelationshipBatchCypher = @"
UNWIND $relationships AS relationshipRow
MATCH (metric:ArchonMetric { snapshotStableKey: relationshipRow.snapshotStableKey, stableKey: relationshipRow.metricStableKey })
MATCH (node:ArchonNode { snapshotStableKey: relationshipRow.snapshotStableKey, stableKey: relationshipRow.nodeStableKey })
MERGE (metric)-[:MEASURES_NODE]->(node)
RETURN count(relationshipRow) AS matchedRows";

        /// <summary>
        /// Captures validation success or a safe persistence error.
        /// </summary>
        /// <param name="succeeded">A value indicating whether validation succeeded.</param>
        /// <param name="error">The safe validation error when validation failed.</param>
        private sealed record SnapshotValidationResult(bool Succeeded, PersistenceError? Error)
        {
            /// <summary>
            /// Creates a successful validation result.
            /// </summary>
            /// <returns>A validation result with no error.</returns>
            public static SnapshotValidationResult Success()
            {
                // Successful validation carries no diagnostic error.
                return new SnapshotValidationResult(true, null);
            }

            /// <summary>
            /// Creates a failed validation result.
            /// </summary>
            /// <param name="code">The stable validation error code.</param>
            /// <param name="message">The safe validation error message.</param>
            /// <returns>A failed validation result with a persistence error.</returns>
            public static SnapshotValidationResult Failure(string code, string message)
            {
                // Validation failures are classified as snapshot persistence errors because they block the write workflow.
                return new SnapshotValidationResult(false, new PersistenceError(PersistenceStage.SnapshotPersistence, code, message));
            }
        }

        /// <summary>
        /// Holds canonical evidence records and duplicate stable-key remapping for one snapshot.
        /// </summary>
        /// <param name="Records">The canonical evidence records that should be persisted.</param>
        /// <param name="CanonicalStableKeyByInputStableKey">The mapping from every input evidence stable key to its canonical persisted stable key.</param>
        private sealed record CanonicalEvidenceSet(IReadOnlyList<EvidenceRecord> Records, IReadOnlyDictionary<string, string> CanonicalStableKeyByInputStableKey);

        /// <summary>
        /// Holds relationship counters produced while relationship statements are executed.
        /// </summary>
        /// <param name="SnapshotSolutionRelationships">The number of snapshot-to-solution relationships written.</param>
        /// <param name="NodeEvidenceRelationships">The number of node-to-evidence support relationships written.</param>
        /// <param name="MetricEvidenceRelationships">The number of metric-to-evidence support relationships written.</param>
        /// <param name="MetricTargetRelationships">The number of metric-to-node target relationships written.</param>
        private sealed record RelationshipWriteCounts(int SnapshotSolutionRelationships, int NodeEvidenceRelationships, int MetricEvidenceRelationships, int MetricTargetRelationships);
    }
}
