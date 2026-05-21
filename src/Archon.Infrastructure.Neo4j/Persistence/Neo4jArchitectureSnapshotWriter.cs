using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Domain.Graph.Model;
using Archon.Infrastructure.Neo4j.Driver;
using Archon.Infrastructure.Neo4j.Schema;
using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.Persistence
{
    /// <summary>
    /// Persists the Work Item 4 minimal architecture snapshot shape into Neo4j.
    /// </summary>
    /// <remarks>
    /// The writer persists repositories, solutions, one snapshot header, architecture nodes, canonical evidence nodes, snapshot-to-solution
    /// relationships, and node-to-evidence relationships. Later work items extend this workflow for architecture relationships, rules,
    /// findings, metrics, and generated summaries.
    /// </remarks>
    public sealed class Neo4jArchitectureSnapshotWriter : IArchitectureSnapshotWriter
    {
        private readonly INeo4jSessionProvider _sessionProvider;
        private readonly IArchitectureGraphInitializer _graphInitializer;
        private readonly Neo4jSnapshotPersistenceMapper _mapper;
        private readonly Neo4jPersistenceStageLogger _stageLogger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jArchitectureSnapshotWriter"/> class.
        /// </summary>
        /// <param name="sessionProvider">The session provider used to open Neo4j write sessions.</param>
        /// <param name="graphInitializer">The graph initializer used to ensure schema exists before writing data.</param>
        /// <param name="mapper">The mapper that converts domain facts into Neo4j parameter dictionaries.</param>
        /// <param name="stageLogger">The credential-safe logger for persistence stages.</param>
        public Neo4jArchitectureSnapshotWriter(
            INeo4jSessionProvider sessionProvider,
            IArchitectureGraphInitializer graphInitializer,
            Neo4jSnapshotPersistenceMapper mapper,
            Neo4jPersistenceStageLogger stageLogger)
        {
            // Dependencies are stored only; no graph work happens until WriteSnapshotAsync is called by an explicit caller.
            _sessionProvider = sessionProvider;
            _graphInitializer = graphInitializer;
            _mapper = mapper;
            _stageLogger = stageLogger;
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
            _stageLogger.LogStageStarting(PersistenceStage.SnapshotPersistence, snapshotStableKey);

            try
            {
                SnapshotValidationResult validation = ValidateSnapshot(snapshot);
                if (!validation.Succeeded)
                {
                    return SnapshotPersistenceResult.Failure(snapshotStableKey, validation.Error!);
                }

                GraphInitializationResult initializationResult = await _graphInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
                if (!initializationResult.Succeeded)
                {
                    PersistenceError error = initializationResult.Errors.FirstOrDefault()
                        ?? new PersistenceError(PersistenceStage.SchemaInitialization, "GraphInitializationFailed", "Graph initialization failed before snapshot persistence.");
                    return SnapshotPersistenceResult.Failure(snapshotStableKey, error, initializationResult.Warnings);
                }

                CanonicalEvidenceSet canonicalEvidence = BuildCanonicalEvidence(snapshot.Evidence);
                SnapshotPersistenceCounts counts = await PersistValidatedSnapshotAsync(snapshot, canonicalEvidence, cancellationToken).ConfigureAwait(false);

                _stageLogger.LogStageCompleted(PersistenceStage.SnapshotPersistence, snapshotStableKey);
                return SnapshotPersistenceResult.Success(snapshotStableKey!, counts, initializationResult.Warnings);
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation is represented as a safe failure so callers can distinguish interruption from data validation errors.
                _stageLogger.LogStageFailed(exception, PersistenceStage.SnapshotPersistence, snapshotStableKey);
                return SnapshotPersistenceResult.Failure(
                    snapshotStableKey,
                    new PersistenceError(PersistenceStage.SnapshotPersistence, "SnapshotPersistenceCanceled", "Snapshot persistence was canceled."));
            }
            catch (Neo4jException exception)
            {
                // Neo4j failures are logged with exception details but returned as credential-safe application diagnostics.
                _stageLogger.LogStageFailed(exception, PersistenceStage.SnapshotPersistence, snapshotStableKey);
                return SnapshotPersistenceResult.Failure(
                    snapshotStableKey,
                    new PersistenceError(PersistenceStage.SnapshotPersistence, "SnapshotPersistenceFailed", "Neo4j snapshot persistence failed."));
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
                || snapshot.Evidence.Any(evidence => !StringComparer.Ordinal.Equals(evidence.SnapshotStableKey.Value, snapshotStableKey)))
            {
                return SnapshotValidationResult.Failure("MismatchedSnapshotScope", "Minimal snapshot records must use the snapshot header stable key as their snapshot scope.");
            }

            HashSet<string> evidenceStableKeys = snapshot.Evidence.Select(evidence => evidence.StableKey.Value).ToHashSet(StringComparer.Ordinal);
            if (snapshot.Nodes.Any(node => node.PrimaryEvidenceStableKey is not null && !evidenceStableKeys.Contains(node.PrimaryEvidenceStableKey.Value.Value)))
            {
                return SnapshotValidationResult.Failure("MissingNodeEvidenceReference", "Architecture nodes must not reference missing primary evidence records.");
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
        /// <param name="cancellationToken">A token that cancels before transaction execution starts.</param>
        /// <returns>The aggregate persisted counts for the transaction.</returns>
        private async Task<SnapshotPersistenceCounts> PersistValidatedSnapshotAsync(ExtractedArchitectureSnapshot snapshot, CanonicalEvidenceSet canonicalEvidence, CancellationToken cancellationToken)
        {
            // A single write transaction prevents a completed result from being returned for partially persisted minimal snapshots.
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Write);

            return await session.ExecuteWriteAsync(
                async transaction =>
                {
                    foreach (RepositoryModel repository in snapshot.Repositories)
                    {
                        await RunAsync(transaction, RepositoryMergeCypher, _mapper.MapRepository(repository)).ConfigureAwait(false);
                    }

                    foreach (SolutionModel solution in snapshot.Solutions)
                    {
                        await RunAsync(transaction, SolutionMergeCypher, _mapper.MapSolution(solution)).ConfigureAwait(false);
                    }

                    await RunAsync(transaction, SnapshotMergeCypher, _mapper.MapSnapshot(snapshot.SnapshotHeader!)).ConfigureAwait(false);

                    foreach (ArchitectureNode node in snapshot.Nodes)
                    {
                        await RunAsync(transaction, NodeMergeCypher, _mapper.MapNode(node)).ConfigureAwait(false);
                    }

                    foreach (EvidenceRecord evidence in canonicalEvidence.Records)
                    {
                        await RunAsync(transaction, EvidenceMergeCypher, _mapper.MapEvidence(evidence)).ConfigureAwait(false);
                    }

                    foreach (SolutionModel solution in snapshot.Solutions)
                    {
                        await RunAsync(transaction, SnapshotSolutionRelationshipCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["snapshotStableKey"] = snapshot.SnapshotHeader!.StableKey.Value,
                            ["solutionStableKey"] = solution.StableKey.Value
                        }).ConfigureAwait(false);
                    }

                    int nodeEvidenceRelationships = 0;
                    foreach (ArchitectureNode node in snapshot.Nodes.Where(node => node.PrimaryEvidenceStableKey is not null))
                    {
                        string canonicalEvidenceStableKey = canonicalEvidence.CanonicalStableKeyByInputStableKey[node.PrimaryEvidenceStableKey!.Value.Value];
                        await RunAsync(transaction, NodeEvidenceRelationshipCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["snapshotStableKey"] = snapshot.SnapshotHeader!.StableKey.Value,
                            ["nodeStableKey"] = node.StableKey.Value,
                            ["evidenceStableKey"] = canonicalEvidenceStableKey
                        }).ConfigureAwait(false);
                        nodeEvidenceRelationships++;
                    }

                    return new SnapshotPersistenceCounts(
                        snapshot.Repositories.Count,
                        snapshot.Solutions.Count,
                        1,
                        snapshot.Nodes.Count,
                        canonicalEvidence.Records.Count,
                        snapshot.Solutions.Count,
                        nodeEvidenceRelationships);
                }).ConfigureAwait(false);
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

        private const string RepositoryMergeCypher = @"
MERGE (repository:ArchonRepository { stableKey: $stableKey })
SET repository.name = $name,
    repository.rootPath = $rootPath,
    repository.remoteUrl = $remoteUrl,
    repository.defaultBranch = $defaultBranch,
    repository.metadataJson = $metadataJson";

        private const string SolutionMergeCypher = @"
MERGE (solution:ArchonSolution { stableKey: $stableKey })
SET solution.repositoryStableKey = $repositoryStableKey,
    solution.name = $name,
    solution.path = $path,
    solution.metadataJson = $metadataJson";

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

        private const string NodeMergeCypher = @"
MERGE (node:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $stableKey })
SET node.nodeKind = $nodeKind,
    node.displayName = $displayName,
    node.qualifiedName = $qualifiedName,
    node.searchName = $searchName,
    node.language = $language,
    node.projectStableKey = $projectStableKey,
    node.parentNodeStableKey = $parentNodeStableKey,
    node.knowledgeKind = $knowledgeKind,
    node.ownership = $ownership,
    node.externalCategory = $externalCategory,
    node.confidence = $confidence,
    node.hasUnknownData = $hasUnknownData,
    node.unknownReason = $unknownReason,
    node.primaryEvidenceStableKey = $primaryEvidenceStableKey,
    node.metadataJson = $metadataJson,
    node.fingerprint = $fingerprint";

        private const string EvidenceMergeCypher = @"
MERGE (evidence:ArchonEvidence { snapshotStableKey: $snapshotStableKey, stableKey: $stableKey })
SET evidence.evidenceKind = $evidenceKind,
    evidence.filePath = $filePath,
    evidence.startLine = $startLine,
    evidence.endLine = $endLine,
    evidence.symbolName = $symbolName,
    evidence.containingSymbol = $containingSymbol,
    evidence.snippetHash = $snippetHash,
    evidence.snippetPreview = $snippetPreview,
    evidence.knowledgeKind = $knowledgeKind,
    evidence.confidence = $confidence,
    evidence.hasUnknownData = $hasUnknownData,
    evidence.unknownReason = $unknownReason,
    evidence.metadataJson = $metadataJson,
    evidence.fingerprint = $fingerprint";

        private const string SnapshotSolutionRelationshipCypher = @"
MATCH (snapshot:ArchonSnapshot { stableKey: $snapshotStableKey })
MATCH (solution:ArchonSolution { stableKey: $solutionStableKey })
MERGE (snapshot)-[:INCLUDES_SOLUTION]->(solution)";

        private const string NodeEvidenceRelationshipCypher = @"
MATCH (node:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $nodeStableKey })
MATCH (evidence:ArchonEvidence { snapshotStableKey: $snapshotStableKey, stableKey: $evidenceStableKey })
MERGE (node)-[:SUPPORTED_BY_EVIDENCE]->(evidence)";

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
    }
}
