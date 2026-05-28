using Archon.Application.Graph.Persistence;
using Archon.Infrastructure.Neo4j.Driver;
using Archon.Infrastructure.Neo4j.Schema;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.Persistence
{
    /// <summary>
    /// Provides Neo4j-backed destructive deletion for one persisted snapshot and its snapshot-scoped subgraph.
    /// </summary>
    /// <remarks>
    /// The adapter deletes records that are scoped by the public snapshot stable key while preserving shared repository, solution, rule,
    /// and extraction run records. It keeps Cypher text, Neo4j driver records, and internal identifiers inside infrastructure.
    /// </remarks>
    public sealed class Neo4jSnapshotDeletionStore : ISnapshotDeletionStore
    {
        /// <summary>
        /// Opens configured Neo4j sessions for controlled deletion transactions.
        /// </summary>
        private readonly INeo4jSessionProvider _sessionProvider;

        /// <summary>
        /// Ensures graph schema exists before deletion relies on labels and indexed stable-key properties.
        /// </summary>
        private readonly IArchitectureGraphInitializer _graphInitializer;

        /// <summary>
        /// Logs credential-safe deletion failures and count summaries.
        /// </summary>
        private readonly ILogger<Neo4jSnapshotDeletionStore> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jSnapshotDeletionStore"/> class.
        /// </summary>
        /// <param name="sessionProvider">The provider used to open Neo4j write sessions.</param>
        /// <param name="graphInitializer">The graph initializer used before delete operations execute.</param>
        /// <param name="logger">The logger used for credential-safe deletion diagnostics.</param>
        public Neo4jSnapshotDeletionStore(
            INeo4jSessionProvider sessionProvider,
            IArchitectureGraphInitializer graphInitializer,
            ILogger<Neo4jSnapshotDeletionStore> logger)
        {
            // Constructor injection keeps this infrastructure adapter behind the application deletion port and avoids service locator usage.
            _sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
            _graphInitializer = graphInitializer ?? throw new ArgumentNullException(nameof(graphInitializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Deletes one snapshot header and all currently persisted snapshot-scoped graph records in one controlled transaction.
        /// </summary>
        /// <param name="request">The normalized delete-one request containing the public snapshot stable key.</param>
        /// <param name="cancellationToken">The token that cancels schema initialization or transaction work before commit.</param>
        /// <returns>A safe deletion result with public identity and counts only.</returns>
        public async Task<SnapshotDeletionResult> DeleteSnapshotAsync(SnapshotDeletionRequest request, CancellationToken cancellationToken)
        {
            // Input has already been validated by the application service, but the adapter still rejects null requests defensively.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            GraphInitializationResult initializationResult = await _graphInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
            if (!initializationResult.Succeeded)
            {
                _logger.LogError(
                    "Neo4j schema initialization failed before snapshot deletion after {StatementCount} statements.",
                    initializationResult.StatementsExecuted);
                throw new InvalidOperationException("Neo4j schema initialization failed before snapshot deletion.");
            }

            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Write);
            IReadOnlyDictionary<string, object?> parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["snapshotStableKey"] = request.SnapshotStableKey
            };

            try
            {
                SnapshotDeletionResult result = await session.ExecuteWriteAsync(
                    async transaction =>
                    {
                        // The Cypher statement returns counts before deleting so the public response can describe what was removed without
                        // exposing database-local identifiers. DETACH DELETE removes all relationships incident to deleted snapshot-scoped nodes,
                        // including produced-snapshot links from preserved run records.
                        cancellationToken.ThrowIfCancellationRequested();
                        IResultCursor cursor = await transaction.RunAsync(DeleteSnapshotCypher, parameters).ConfigureAwait(false);
                        IReadOnlyList<IRecord> records = await cursor.ToListAsync().ConfigureAwait(false);
                        await cursor.ConsumeAsync().ConfigureAwait(false);
                        return MapResult(request.SnapshotStableKey, records);
                    }).ConfigureAwait(false);

                _logger.LogInformation(
                    "Neo4j snapshot deletion for {SnapshotStableKey} removed {DeletedSnapshotCount} snapshot nodes and {DeletedNodeCount} scoped data nodes.",
                    result.SnapshotStableKey,
                    result.DeletedSnapshotCount,
                    result.DeletedNodeCount);
                return result;
            }
            catch (Neo4jException exception)
            {
                // Raw Cypher, server addresses, and driver details remain in logs and never flow into application response models.
                _logger.LogError(exception, "Neo4j snapshot deletion failed for snapshot {SnapshotStableKey}.", request.SnapshotStableKey);
                throw;
            }
        }

        /// <summary>
        /// Deletes every snapshot header and every currently persisted snapshot-scoped graph record in one controlled transaction.
        /// </summary>
        /// <param name="request">The normalized delete-all request containing the validated confirmation phrase.</param>
        /// <param name="cancellationToken">The token that cancels schema initialization or transaction work before commit.</param>
        /// <returns>A safe aggregate deletion result with counts only.</returns>
        public async Task<SnapshotDeleteAllResult> DeleteAllSnapshotsAsync(SnapshotDeleteAllRequest request, CancellationToken cancellationToken)
        {
            // The application layer has already validated the confirmation phrase; infrastructure never accepts caller-defined filters or Cypher.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            GraphInitializationResult initializationResult = await _graphInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
            if (!initializationResult.Succeeded)
            {
                _logger.LogError(
                    "Neo4j schema initialization failed before delete-all snapshot cleanup after {StatementCount} statements.",
                    initializationResult.StatementsExecuted);
                throw new InvalidOperationException("Neo4j schema initialization failed before delete-all snapshot cleanup.");
            }

            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Write);
            try
            {
                SnapshotDeleteAllResult result = await session.ExecuteWriteAsync(
                    async transaction =>
                    {
                        // Counts are projected before DETACH DELETE so the API can report safe aggregate results without internal node IDs.
                        cancellationToken.ThrowIfCancellationRequested();
                        IResultCursor cursor = await transaction.RunAsync(DeleteAllSnapshotsCypher).ConfigureAwait(false);
                        IReadOnlyList<IRecord> records = await cursor.ToListAsync().ConfigureAwait(false);
                        await cursor.ConsumeAsync().ConfigureAwait(false);
                        return MapDeleteAllResult(records);
                    }).ConfigureAwait(false);

                _logger.LogInformation(
                    "Neo4j delete-all snapshot cleanup removed {DeletedSnapshotCount} snapshot nodes and {DeletedNodeCount} scoped data nodes.",
                    result.DeletedSnapshotCount,
                    result.DeletedNodeCount);
                return result;
            }
            catch (Neo4jException exception)
            {
                // Driver exception details and query text remain out of application response models.
                _logger.LogError(exception, "Neo4j delete-all snapshot cleanup failed.");
                throw;
            }
        }

        /// <summary>
        /// Maps one projected Neo4j deletion record into the application deletion result contract.
        /// </summary>
        /// <param name="snapshotStableKey">The public stable key targeted by the deletion request.</param>
        /// <param name="records">The projected count records returned by the deletion statement.</param>
        /// <returns>The storage-neutral deletion result.</returns>
        private static SnapshotDeletionResult MapResult(string snapshotStableKey, IReadOnlyList<IRecord> records)
        {
            // The UNION statement can observe the post-delete not-found state after a successful delete, so the mapper prefers the row that
            // reports a deleted snapshot and falls back to the first zero row for a true not-found request.
            IRecord record = records.FirstOrDefault(static candidate => ReadInt(candidate, "deletedSnapshotCount") > 0)
                ?? records.First();
            int deletedSnapshotCount = ReadInt(record, "deletedSnapshotCount");
            int deletedNodeCount = ReadInt(record, "deletedNodeCount");
            int deletedRelationshipCount = ReadInt(record, "deletedRelationshipCount");
            int affectedRunCount = ReadInt(record, "affectedRunCount");
            IReadOnlyList<string> warnings = deletedSnapshotCount > 0 && affectedRunCount > 0
                ? ["One or more preserved extraction runs referenced the deleted snapshot; their snapshot stable key remains as historical identity while the graph snapshot is unavailable."]
                : [];

            return new SnapshotDeletionResult(
                snapshotStableKey,
                SnapshotDeleted: deletedSnapshotCount > 0,
                deletedSnapshotCount,
                deletedNodeCount,
                deletedRelationshipCount,
                affectedRunCount,
                warnings);
        }

        /// <summary>
        /// Maps one projected Neo4j delete-all record into the application aggregate deletion result contract.
        /// </summary>
        /// <param name="records">The projected count records returned by the delete-all statement.</param>
        /// <returns>The storage-neutral aggregate deletion result.</returns>
        private static SnapshotDeleteAllResult MapDeleteAllResult(IReadOnlyList<IRecord> records)
        {
            // The UNION statement can also observe a post-delete zero row, so prefer the positive count row when cleanup removed snapshots.
            IRecord record = records.FirstOrDefault(static candidate => ReadInt(candidate, "deletedSnapshotCount") > 0)
                ?? records.First();
            int deletedSnapshotCount = ReadInt(record, "deletedSnapshotCount");
            int deletedNodeCount = ReadInt(record, "deletedNodeCount");
            int deletedRelationshipCount = ReadInt(record, "deletedRelationshipCount");
            int affectedRunCount = ReadInt(record, "affectedRunCount");
            IReadOnlyList<string> warnings = deletedSnapshotCount > 0 && affectedRunCount > 0
                ? ["One or more preserved extraction runs referenced deleted snapshots; their snapshot stable keys remain as historical identities while graph snapshots are unavailable."]
                : [];

            return new SnapshotDeleteAllResult(
                deletedSnapshotCount,
                deletedNodeCount,
                deletedRelationshipCount,
                affectedRunCount,
                warnings);
        }

        /// <summary>
        /// Reads an integer count from a projected Neo4j record.
        /// </summary>
        /// <param name="record">The record containing the count projection.</param>
        /// <param name="key">The projection name to read.</param>
        /// <returns>The projected count converted to <see cref="int"/>.</returns>
        private static int ReadInt(IRecord record, string key)
        {
            // Deletion counts are expected to be small enough for management responses; overflow should fail fast rather than wrap.
            return checked((int)record[key].As<long>());
        }

        /// <summary>
        /// Deletes a snapshot and all snapshot-scoped records while preserving shared and operational records.
        /// </summary>
        private const string DeleteSnapshotCypher = @"
MATCH (snapshot:ArchonSnapshot { stableKey: $snapshotStableKey })
OPTIONAL MATCH (snapshot)-[snapshotRelationship]-()
WITH snapshot, collect(DISTINCT snapshotRelationship) AS snapshotRelationships
OPTIONAL MATCH (run:ArchonExtractionRun { snapshotStableKey: $snapshotStableKey })
WITH snapshot, snapshotRelationships, collect(DISTINCT run) AS affectedRuns
OPTIONAL MATCH (node:ArchonNode { snapshotStableKey: $snapshotStableKey })
WITH snapshot, snapshotRelationships, affectedRuns, collect(DISTINCT node) AS nodes
OPTIONAL MATCH (relationshipNode:ArchonRelationship { snapshotStableKey: $snapshotStableKey })
WITH snapshot, snapshotRelationships, affectedRuns, nodes, collect(DISTINCT relationshipNode) AS relationshipNodes
OPTIONAL MATCH (evidence:ArchonEvidence { snapshotStableKey: $snapshotStableKey })
WITH snapshot, snapshotRelationships, affectedRuns, nodes, relationshipNodes, collect(DISTINCT evidence) AS evidenceRecords
OPTIONAL MATCH (finding:ArchonFinding { snapshotStableKey: $snapshotStableKey })
WITH snapshot, snapshotRelationships, affectedRuns, nodes, relationshipNodes, evidenceRecords, collect(DISTINCT finding) AS findings
OPTIONAL MATCH (metric:ArchonMetric { snapshotStableKey: $snapshotStableKey })
WITH snapshot, snapshotRelationships, affectedRuns, nodes, relationshipNodes, evidenceRecords, findings, collect(DISTINCT metric) AS metrics
OPTIONAL MATCH (summary:ArchonGeneratedSummary { snapshotStableKey: $snapshotStableKey })
WITH snapshot,
     affectedRuns,
     snapshotRelationships,
     nodes,
     relationshipNodes,
     evidenceRecords,
     findings,
     metrics,
     collect(DISTINCT summary) AS summaries
WITH snapshot,
     affectedRuns,
     snapshotRelationships,
     nodes + relationshipNodes + evidenceRecords + findings + metrics + summaries AS scopedNodes
UNWIND (CASE WHEN size(scopedNodes) = 0 THEN [null] ELSE scopedNodes END) AS scopedNode
OPTIONAL MATCH (scopedNode)-[scopedRelationship]-()
WITH snapshot,
     affectedRuns,
     scopedNodes,
     snapshotRelationships,
     collect(DISTINCT scopedRelationship) AS scopedRelationships
WITH snapshot,
     affectedRuns,
     scopedNodes,
     snapshotRelationships + scopedRelationships AS relationshipsToDelete
WITH snapshot,
     size(affectedRuns) AS affectedRunCount,
     size([node IN scopedNodes WHERE node IS NOT NULL]) AS deletedNodeCount,
     size([relationship IN relationshipsToDelete WHERE relationship IS NOT NULL]) AS deletedRelationshipCount,
     [node IN scopedNodes WHERE node IS NOT NULL] AS scopedNodesToDelete
DETACH DELETE snapshot
WITH affectedRunCount, deletedNodeCount, deletedRelationshipCount, scopedNodesToDelete
FOREACH (scopedNode IN scopedNodesToDelete | DETACH DELETE scopedNode)
RETURN 1 AS deletedSnapshotCount,
       deletedNodeCount AS deletedNodeCount,
       deletedRelationshipCount AS deletedRelationshipCount,
       affectedRunCount AS affectedRunCount
UNION
OPTIONAL MATCH (snapshot:ArchonSnapshot { stableKey: $snapshotStableKey })
WITH count(snapshot) AS snapshotCount
WHERE snapshotCount = 0
RETURN 0 AS deletedSnapshotCount,
       0 AS deletedNodeCount,
       0 AS deletedRelationshipCount,
       0 AS affectedRunCount";

        /// <summary>
        /// Deletes all snapshots and all snapshot-scoped records while preserving shared and operational records.
        /// </summary>
        private const string DeleteAllSnapshotsCypher = @"
CALL {
    MATCH (snapshot:ArchonSnapshot)
    WITH collect(DISTINCT snapshot) AS snapshots,
         collect(DISTINCT snapshot.stableKey) AS snapshotStableKeys
    WHERE size(snapshots) > 0
    OPTIONAL MATCH (run:ArchonExtractionRun)
    WHERE run.snapshotStableKey IN snapshotStableKeys
    WITH snapshots,
         snapshotStableKeys,
         collect(DISTINCT run) AS affectedRuns
    OPTIONAL MATCH (scopedNode)
    WHERE (scopedNode:ArchonNode OR scopedNode:ArchonRelationship OR scopedNode:ArchonEvidence OR scopedNode:ArchonFinding OR scopedNode:ArchonMetric OR scopedNode:ArchonGeneratedSummary)
      AND scopedNode.snapshotStableKey IN snapshotStableKeys
    WITH snapshots,
         affectedRuns,
         [node IN collect(DISTINCT scopedNode) WHERE node IS NOT NULL] AS scopedNodes
    WITH snapshots,
         affectedRuns,
         scopedNodes,
         snapshots + scopedNodes AS nodesToDelete
    UNWIND nodesToDelete AS nodeToDelete
    OPTIONAL MATCH (nodeToDelete)-[relationshipToDelete]-()
    WITH nodesToDelete,
         affectedRuns,
         scopedNodes,
         collect(DISTINCT relationshipToDelete) AS relationshipsToDelete
    WITH nodesToDelete,
         size([node IN nodesToDelete WHERE node:ArchonSnapshot]) AS deletedSnapshotCount,
         size(scopedNodes) AS deletedNodeCount,
         size([relationship IN relationshipsToDelete WHERE relationship IS NOT NULL]) AS deletedRelationshipCount,
         size(affectedRuns) AS affectedRunCount
    FOREACH (nodeToDelete IN nodesToDelete | DETACH DELETE nodeToDelete)
    RETURN deletedSnapshotCount AS deletedSnapshotCount,
           deletedNodeCount AS deletedNodeCount,
           deletedRelationshipCount AS deletedRelationshipCount,
           affectedRunCount AS affectedRunCount
    UNION
    OPTIONAL MATCH (snapshot:ArchonSnapshot)
    WITH count(snapshot) AS snapshotCount
    WHERE snapshotCount = 0
    RETURN 0 AS deletedSnapshotCount,
           0 AS deletedNodeCount,
           0 AS deletedRelationshipCount,
           0 AS affectedRunCount
}
RETURN deletedSnapshotCount,
       deletedNodeCount,
       deletedRelationshipCount,
       affectedRunCount";
    }
}
