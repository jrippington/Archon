using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Domain.Graph.Model;
using Archon.Infrastructure.Neo4j.Driver;
using Archon.Infrastructure.Neo4j.Schema;
using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.Persistence
{
    /// <summary>
    /// Persists architecture snapshots into Neo4j using stable graph identities.
    /// </summary>
    /// <remarks>
    /// The writer persists repositories, solutions, one snapshot header, architecture nodes, architecture relationship nodes, canonical
    /// evidence nodes, versioned rule catalog entries, snapshot-scoped findings, snapshot-scoped metrics, generated summaries,
    /// snapshot-to-solution relationships, node-to-evidence relationships, relationship endpoint links, relationship evidence links,
    /// finding-to-rule links, finding-to-node links, finding-to-evidence links, metric evidence and target links, and generated-summary
    /// snapshot and target links.
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
        /// Persists one architecture snapshot into Neo4j using stable-key merge semantics.
        /// </summary>
        /// <param name="snapshot">The assembled architecture snapshot containing repositories, solutions, nodes, relationships, and evidence.</param>
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
            // Snapshot persistence requires one header and explicit references between the header, repository, solutions, nodes, edges,
            // evidence, findings, metrics, and generated summaries before any Neo4j write transaction starts.
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
                || snapshot.Edges.Any(edge => !StringComparer.Ordinal.Equals(edge.SnapshotStableKey.Value, snapshotStableKey))
                || snapshot.Evidence.Any(evidence => !StringComparer.Ordinal.Equals(evidence.SnapshotStableKey.Value, snapshotStableKey))
                || snapshot.Findings.Any(finding => !StringComparer.Ordinal.Equals(finding.SnapshotStableKey.Value, snapshotStableKey))
                || snapshot.Metrics.Any(metric => !StringComparer.Ordinal.Equals(metric.SnapshotStableKey.Value, snapshotStableKey))
                || snapshot.GeneratedSummaries.Any(generatedSummary => !StringComparer.Ordinal.Equals(generatedSummary.SnapshotStableKey.Value, snapshotStableKey)))
            {
                return SnapshotValidationResult.Failure("MismatchedSnapshotScope", "Snapshot records must use the snapshot header stable key as their snapshot scope.");
            }

            HashSet<string> nodeStableKeys = snapshot.Nodes.Select(node => node.StableKey.Value).ToHashSet(StringComparer.Ordinal);
            if (snapshot.Edges.Any(edge => !nodeStableKeys.Contains(edge.SourceNodeStableKey.Value)))
            {
                return SnapshotValidationResult.Failure("MissingRelationshipSourceNodeReference", "Architecture relationships must not reference missing source nodes.");
            }

            if (snapshot.Edges.Any(edge => !nodeStableKeys.Contains(edge.TargetNodeStableKey.Value)))
            {
                return SnapshotValidationResult.Failure("MissingRelationshipTargetNodeReference", "Architecture relationships must not reference missing target nodes.");
            }

            HashSet<string> evidenceStableKeys = snapshot.Evidence.Select(evidence => evidence.StableKey.Value).ToHashSet(StringComparer.Ordinal);
            if (snapshot.Nodes.Any(node => node.PrimaryEvidenceStableKey is not null && !evidenceStableKeys.Contains(node.PrimaryEvidenceStableKey.Value.Value)))
            {
                return SnapshotValidationResult.Failure("MissingNodeEvidenceReference", "Architecture nodes must not reference missing primary evidence records.");
            }

            if (snapshot.Edges.Any(edge => edge.PrimaryEvidenceStableKey is not null && !evidenceStableKeys.Contains(edge.PrimaryEvidenceStableKey.Value.Value)))
            {
                return SnapshotValidationResult.Failure("MissingRelationshipEvidenceReference", "Architecture relationships must not reference missing primary evidence records.");
            }

            HashSet<string> ruleVersionKeys = snapshot.Rules.Select(rule => BuildRuleVersionKey(rule.RuleCode, rule.Version)).ToHashSet(StringComparer.Ordinal);
            if (snapshot.Findings.Any(finding => !ruleVersionKeys.Contains(BuildRuleVersionKey(finding.RuleCode, finding.RuleVersion))))
            {
                return SnapshotValidationResult.Failure("MissingFindingRuleReference", "Findings must reference a supplied rule code and version.");
            }

            if (snapshot.Findings.Any(finding => finding.PrimaryNodeStableKey is not null && !nodeStableKeys.Contains(finding.PrimaryNodeStableKey.Value.Value)))
            {
                return SnapshotValidationResult.Failure("MissingFindingNodeReference", "Findings must not reference missing primary architecture nodes.");
            }

            if (snapshot.Findings.Any(finding => finding.PrimaryEvidenceStableKey is not null && !evidenceStableKeys.Contains(finding.PrimaryEvidenceStableKey.Value.Value)))
            {
                return SnapshotValidationResult.Failure("MissingFindingEvidenceReference", "Findings must not reference missing primary evidence records.");
            }

            HashSet<string> relationshipStableKeys = snapshot.Edges.Select(edge => edge.StableKey.Value).ToHashSet(StringComparer.Ordinal);
            if (snapshot.Metrics.Any(metric => metric.NodeStableKey is not null && !nodeStableKeys.Contains(metric.NodeStableKey.Value.Value)))
            {
                return SnapshotValidationResult.Failure("MissingMetricNodeReference", "Metrics must not reference missing target architecture nodes.");
            }

            if (snapshot.Metrics.Any(metric => metric.EdgeStableKey is not null && !relationshipStableKeys.Contains(metric.EdgeStableKey.Value.Value)))
            {
                return SnapshotValidationResult.Failure("MissingMetricRelationshipReference", "Metrics must not reference missing target architecture relationships.");
            }

            if (snapshot.Metrics.Any(metric => metric.PrimaryEvidenceStableKey is not null && !evidenceStableKeys.Contains(metric.PrimaryEvidenceStableKey.Value.Value)))
            {
                return SnapshotValidationResult.Failure("MissingMetricEvidenceReference", "Metrics must not reference missing primary evidence records.");
            }

            HashSet<string> summaryTargetStableKeys = BuildGeneratedSummaryTargetStableKeys(snapshotStableKey, nodeStableKeys, relationshipStableKeys);
            if (snapshot.GeneratedSummaries.Any(generatedSummary => generatedSummary.TargetStableKey is not null && !summaryTargetStableKeys.Contains(generatedSummary.TargetStableKey.Value.Value)))
            {
                return SnapshotValidationResult.Failure("MissingGeneratedSummaryTargetReference", "Generated summaries must not reference missing target records.");
            }

            return SnapshotValidationResult.Success();
        }

        /// <summary>
        /// Builds the stable-key set that generated summaries may target in the current persistence slice.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot currently being validated.</param>
        /// <param name="nodeStableKeys">The snapshot-scoped architecture node stable keys supplied by the snapshot.</param>
        /// <param name="relationshipStableKeys">The snapshot-scoped architecture relationship stable keys supplied by the snapshot.</param>
        /// <returns>A stable-key set containing the snapshot, architecture nodes, and architecture relationships that summaries may link to.</returns>
        private static HashSet<string> BuildGeneratedSummaryTargetStableKeys(string snapshotStableKey, HashSet<string> nodeStableKeys, HashSet<string> relationshipStableKeys)
        {
            // Generated summaries can describe the snapshot itself or a persisted graph fact that already has a stable key in the same
            // snapshot. Later slices can expand this set when additional summary target record types become first-class graph nodes.
            HashSet<string> targetStableKeys = new(StringComparer.Ordinal) { snapshotStableKey };
            targetStableKeys.UnionWith(nodeStableKeys);
            targetStableKeys.UnionWith(relationshipStableKeys);
            return targetStableKeys;
        }

        /// <summary>
        /// Builds the composite identity string used to validate rule code and version references before persistence.
        /// </summary>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="ruleVersion">The stable rule version.</param>
        /// <returns>A deterministic composite key for in-memory validation only.</returns>
        private static string BuildRuleVersionKey(string ruleCode, string ruleVersion)
        {
            // The separator is not exposed outside validation; Neo4j still receives ruleCode and ruleVersion as separate constrained properties.
            return string.Concat(ruleCode, "\u001F", ruleVersion);
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
        /// Persists a validated snapshot in a single Neo4j write transaction.
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

                    foreach (ArchitectureEdge edge in snapshot.Edges)
                    {
                        await RunAsync(transaction, RelationshipMergeCypher, _mapper.MapRelationship(edge)).ConfigureAwait(false);
                    }

                    foreach (RuleDefinition rule in snapshot.Rules)
                    {
                        await RunAsync(transaction, RuleMergeCypher, _mapper.MapRule(rule)).ConfigureAwait(false);
                    }

                    foreach (FindingRecord finding in snapshot.Findings)
                    {
                        await RunAsync(transaction, FindingMergeCypher, _mapper.MapFinding(finding)).ConfigureAwait(false);
                    }

                    foreach (MetricRecord metric in snapshot.Metrics)
                    {
                        await RunAsync(transaction, MetricMergeCypher, _mapper.MapMetric(metric)).ConfigureAwait(false);
                    }

                    foreach (GeneratedSummary generatedSummary in snapshot.GeneratedSummaries)
                    {
                        await RunAsync(transaction, GeneratedSummaryMergeCypher, _mapper.MapGeneratedSummary(generatedSummary)).ConfigureAwait(false);
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

                    int relationshipEndpointRelationships = 0;
                    int relationshipEvidenceRelationships = 0;
                    foreach (ArchitectureEdge edge in snapshot.Edges)
                    {
                        await RunAsync(transaction, RelationshipSourceCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["snapshotStableKey"] = snapshot.SnapshotHeader!.StableKey.Value,
                            ["relationshipStableKey"] = edge.StableKey.Value,
                            ["sourceNodeStableKey"] = edge.SourceNodeStableKey.Value
                        }).ConfigureAwait(false);
                        relationshipEndpointRelationships++;

                        await RunAsync(transaction, RelationshipTargetCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["snapshotStableKey"] = snapshot.SnapshotHeader!.StableKey.Value,
                            ["relationshipStableKey"] = edge.StableKey.Value,
                            ["targetNodeStableKey"] = edge.TargetNodeStableKey.Value
                        }).ConfigureAwait(false);
                        relationshipEndpointRelationships++;

                        if (edge.PrimaryEvidenceStableKey is not null)
                        {
                            string canonicalEvidenceStableKey = canonicalEvidence.CanonicalStableKeyByInputStableKey[edge.PrimaryEvidenceStableKey.Value.Value];
                            await RunAsync(transaction, RelationshipEvidenceCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["snapshotStableKey"] = snapshot.SnapshotHeader!.StableKey.Value,
                                ["relationshipStableKey"] = edge.StableKey.Value,
                                ["evidenceStableKey"] = canonicalEvidenceStableKey
                            }).ConfigureAwait(false);
                            relationshipEvidenceRelationships++;
                        }
                    }

                    int findingRuleRelationships = 0;
                    int findingNodeRelationships = 0;
                    int findingEvidenceRelationships = 0;
                    foreach (FindingRecord finding in snapshot.Findings)
                    {
                        await RunAsync(transaction, FindingRuleCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["snapshotStableKey"] = snapshot.SnapshotHeader!.StableKey.Value,
                            ["findingStableKey"] = finding.StableKey.Value,
                            ["ruleCode"] = finding.RuleCode,
                            ["ruleVersion"] = finding.RuleVersion
                        }).ConfigureAwait(false);
                        findingRuleRelationships++;

                        if (finding.PrimaryNodeStableKey is not null)
                        {
                            await RunAsync(transaction, FindingNodeCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["snapshotStableKey"] = snapshot.SnapshotHeader!.StableKey.Value,
                                ["findingStableKey"] = finding.StableKey.Value,
                                ["nodeStableKey"] = finding.PrimaryNodeStableKey.Value.Value
                            }).ConfigureAwait(false);
                            findingNodeRelationships++;
                        }

                        if (finding.PrimaryEvidenceStableKey is not null)
                        {
                            string canonicalEvidenceStableKey = canonicalEvidence.CanonicalStableKeyByInputStableKey[finding.PrimaryEvidenceStableKey.Value.Value];
                            await RunAsync(transaction, FindingEvidenceCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["snapshotStableKey"] = snapshot.SnapshotHeader!.StableKey.Value,
                                ["findingStableKey"] = finding.StableKey.Value,
                                ["evidenceStableKey"] = canonicalEvidenceStableKey
                            }).ConfigureAwait(false);
                            findingEvidenceRelationships++;
                        }
                    }

                    int metricEvidenceRelationships = 0;
                    int metricTargetRelationships = 0;
                    foreach (MetricRecord metric in snapshot.Metrics)
                    {
                        if (metric.PrimaryEvidenceStableKey is not null)
                        {
                            string canonicalEvidenceStableKey = canonicalEvidence.CanonicalStableKeyByInputStableKey[metric.PrimaryEvidenceStableKey.Value.Value];
                            await RunAsync(transaction, MetricEvidenceCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["snapshotStableKey"] = snapshot.SnapshotHeader!.StableKey.Value,
                                ["metricStableKey"] = metric.StableKey.Value,
                                ["evidenceStableKey"] = canonicalEvidenceStableKey
                            }).ConfigureAwait(false);
                            metricEvidenceRelationships++;
                        }

                        if (metric.NodeStableKey is not null)
                        {
                            await RunAsync(transaction, MetricNodeTargetCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["snapshotStableKey"] = snapshot.SnapshotHeader!.StableKey.Value,
                                ["metricStableKey"] = metric.StableKey.Value,
                                ["nodeStableKey"] = metric.NodeStableKey.Value.Value
                            }).ConfigureAwait(false);
                            metricTargetRelationships++;
                        }

                        if (metric.EdgeStableKey is not null)
                        {
                            await RunAsync(transaction, MetricRelationshipTargetCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["snapshotStableKey"] = snapshot.SnapshotHeader!.StableKey.Value,
                                ["metricStableKey"] = metric.StableKey.Value,
                                ["relationshipStableKey"] = metric.EdgeStableKey.Value.Value
                            }).ConfigureAwait(false);
                            metricTargetRelationships++;
                        }
                    }

                    int summarySnapshotRelationships = 0;
                    int summaryTargetRelationships = 0;
                    foreach (GeneratedSummary generatedSummary in snapshot.GeneratedSummaries)
                    {
                        await RunAsync(transaction, GeneratedSummarySnapshotCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["snapshotStableKey"] = snapshot.SnapshotHeader!.StableKey.Value,
                            ["summaryStableKey"] = generatedSummary.StableKey.Value
                        }).ConfigureAwait(false);
                        summarySnapshotRelationships++;

                        if (generatedSummary.TargetStableKey is not null && !StringComparer.Ordinal.Equals(generatedSummary.TargetStableKey.Value.Value, snapshot.SnapshotHeader!.StableKey.Value))
                        {
                            bool targetIsNode = snapshot.Nodes.Any(node => StringComparer.Ordinal.Equals(node.StableKey.Value, generatedSummary.TargetStableKey.Value.Value));
                            string targetCypher = targetIsNode ? GeneratedSummaryNodeTargetCypher : GeneratedSummaryRelationshipTargetCypher;
                            await RunAsync(transaction, targetCypher, new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["snapshotStableKey"] = snapshot.SnapshotHeader!.StableKey.Value,
                                ["summaryStableKey"] = generatedSummary.StableKey.Value,
                                ["targetStableKey"] = generatedSummary.TargetStableKey.Value.Value
                            }).ConfigureAwait(false);
                            summaryTargetRelationships++;
                        }
                    }

                    return new SnapshotPersistenceCounts(
                        snapshot.Repositories.Count,
                        snapshot.Solutions.Count,
                        1,
                        snapshot.Nodes.Count,
                        canonicalEvidence.Records.Count,
                        snapshot.Edges.Count,
                        snapshot.Solutions.Count,
                        nodeEvidenceRelationships,
                        relationshipEndpointRelationships,
                        relationshipEvidenceRelationships,
                        snapshot.Rules.Count,
                        snapshot.Findings.Count,
                        findingRuleRelationships,
                        findingNodeRelationships,
                        findingEvidenceRelationships,
                        snapshot.Metrics.Count,
                        metricEvidenceRelationships,
                        metricTargetRelationships,
                        snapshot.GeneratedSummaries.Count,
                        summarySnapshotRelationships,
                        summaryTargetRelationships);
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

        private const string RelationshipMergeCypher = @"
MERGE (relationship:ArchonRelationship { snapshotStableKey: $snapshotStableKey, stableKey: $stableKey })
SET relationship.edgeKind = $edgeKind,
    relationship.sourceNodeStableKey = $sourceNodeStableKey,
    relationship.targetNodeStableKey = $targetNodeStableKey,
    relationship.isDirect = $isDirect,
    relationship.knowledgeKind = $knowledgeKind,
    relationship.confidence = $confidence,
    relationship.hasUnknownData = $hasUnknownData,
    relationship.unknownReason = $unknownReason,
    relationship.primaryEvidenceStableKey = $primaryEvidenceStableKey,
    relationship.metadataJson = $metadataJson,
    relationship.fingerprint = $fingerprint";

        private const string RuleMergeCypher = @"
MERGE (rule:ArchonRule { ruleCode: $ruleCode, ruleVersion: $ruleVersion })
SET rule.name = $name,
    rule.category = $category,
    rule.severity = $severity,
    rule.defaultStatus = $defaultStatus,
    rule.enabled = $enabled,
    rule.description = $description,
    rule.definitionJson = $definitionJson,
    rule.sourceUrlsJson = $sourceUrlsJson,
    rule.isBuiltIn = $isBuiltIn,
    rule.ownerScope = $ownerScope,
    rule.metadataJson = $metadataJson";

        private const string FindingMergeCypher = @"
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
    finding.metadataJson = $metadataJson,
    finding.fingerprint = $fingerprint";

        private const string MetricMergeCypher = @"
MERGE (metric:ArchonMetric { snapshotStableKey: $snapshotStableKey, stableKey: $stableKey })
SET metric.metricKind = $metricKind,
    metric.scopeKind = $scopeKind,
    metric.nodeStableKey = $nodeStableKey,
    metric.edgeStableKey = $edgeStableKey,
    metric.primaryEvidenceStableKey = $primaryEvidenceStableKey,
    metric.name = $name,
    metric.numericValue = $numericValue,
    metric.textValue = $textValue,
    metric.unit = $unit,
    metric.metadataJson = $metadataJson,
    metric.fingerprint = $fingerprint";

        private const string GeneratedSummaryMergeCypher = @"
MERGE (generatedSummary:ArchonGeneratedSummary { snapshotStableKey: $snapshotStableKey, stableKey: $stableKey })
SET generatedSummary.summaryKind = $summaryKind,
    generatedSummary.targetStableKey = $targetStableKey,
    generatedSummary.format = $format,
    generatedSummary.title = $title,
    generatedSummary.content = $content,
    generatedSummary.metadataJson = $metadataJson,
    generatedSummary.fingerprint = $fingerprint";

        private const string SnapshotSolutionRelationshipCypher = @"
MATCH (snapshot:ArchonSnapshot { stableKey: $snapshotStableKey })
MATCH (solution:ArchonSolution { stableKey: $solutionStableKey })
MERGE (snapshot)-[:INCLUDES_SOLUTION]->(solution)";

        private const string NodeEvidenceRelationshipCypher = @"
MATCH (node:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $nodeStableKey })
MATCH (evidence:ArchonEvidence { snapshotStableKey: $snapshotStableKey, stableKey: $evidenceStableKey })
MERGE (node)-[:SUPPORTED_BY_EVIDENCE]->(evidence)";

        private const string RelationshipSourceCypher = @"
MATCH (relationship:ArchonRelationship { snapshotStableKey: $snapshotStableKey, stableKey: $relationshipStableKey })
MATCH (source:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $sourceNodeStableKey })
MERGE (relationship)-[:RELATIONSHIP_SOURCE]->(source)";

        private const string RelationshipTargetCypher = @"
MATCH (relationship:ArchonRelationship { snapshotStableKey: $snapshotStableKey, stableKey: $relationshipStableKey })
MATCH (target:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $targetNodeStableKey })
MERGE (relationship)-[:RELATIONSHIP_TARGET]->(target)";

        private const string RelationshipEvidenceCypher = @"
MATCH (relationship:ArchonRelationship { snapshotStableKey: $snapshotStableKey, stableKey: $relationshipStableKey })
MATCH (evidence:ArchonEvidence { snapshotStableKey: $snapshotStableKey, stableKey: $evidenceStableKey })
MERGE (relationship)-[:SUPPORTED_BY_EVIDENCE]->(evidence)";

        private const string FindingRuleCypher = @"
MATCH (finding:ArchonFinding { snapshotStableKey: $snapshotStableKey, stableKey: $findingStableKey })
MATCH (rule:ArchonRule { ruleCode: $ruleCode, ruleVersion: $ruleVersion })
MERGE (finding)-[:CLASSIFIED_BY_RULE]->(rule)";

        private const string FindingNodeCypher = @"
MATCH (finding:ArchonFinding { snapshotStableKey: $snapshotStableKey, stableKey: $findingStableKey })
MATCH (node:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $nodeStableKey })
MERGE (finding)-[:PRIMARY_NODE]->(node)";

        private const string FindingEvidenceCypher = @"
MATCH (finding:ArchonFinding { snapshotStableKey: $snapshotStableKey, stableKey: $findingStableKey })
MATCH (evidence:ArchonEvidence { snapshotStableKey: $snapshotStableKey, stableKey: $evidenceStableKey })
MERGE (finding)-[:SUPPORTED_BY_EVIDENCE]->(evidence)";

        private const string MetricEvidenceCypher = @"
MATCH (metric:ArchonMetric { snapshotStableKey: $snapshotStableKey, stableKey: $metricStableKey })
MATCH (evidence:ArchonEvidence { snapshotStableKey: $snapshotStableKey, stableKey: $evidenceStableKey })
MERGE (metric)-[:SUPPORTED_BY_EVIDENCE]->(evidence)";

        private const string MetricNodeTargetCypher = @"
MATCH (metric:ArchonMetric { snapshotStableKey: $snapshotStableKey, stableKey: $metricStableKey })
MATCH (node:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $nodeStableKey })
MERGE (metric)-[:PRIMARY_NODE]->(node)";

        private const string MetricRelationshipTargetCypher = @"
MATCH (metric:ArchonMetric { snapshotStableKey: $snapshotStableKey, stableKey: $metricStableKey })
MATCH (relationship:ArchonRelationship { snapshotStableKey: $snapshotStableKey, stableKey: $relationshipStableKey })
MERGE (metric)-[:PRIMARY_RELATIONSHIP]->(relationship)";

        private const string GeneratedSummarySnapshotCypher = @"
MATCH (generatedSummary:ArchonGeneratedSummary { snapshotStableKey: $snapshotStableKey, stableKey: $summaryStableKey })
MATCH (snapshot:ArchonSnapshot { stableKey: $snapshotStableKey })
MERGE (generatedSummary)-[:SUMMARIZES_SNAPSHOT]->(snapshot)";

        private const string GeneratedSummaryNodeTargetCypher = @"
MATCH (generatedSummary:ArchonGeneratedSummary { snapshotStableKey: $snapshotStableKey, stableKey: $summaryStableKey })
MATCH (node:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $targetStableKey })
MERGE (generatedSummary)-[:PRIMARY_NODE]->(node)";

        private const string GeneratedSummaryRelationshipTargetCypher = @"
MATCH (generatedSummary:ArchonGeneratedSummary { snapshotStableKey: $snapshotStableKey, stableKey: $summaryStableKey })
MATCH (relationship:ArchonRelationship { snapshotStableKey: $snapshotStableKey, stableKey: $targetStableKey })
MERGE (generatedSummary)-[:PRIMARY_RELATIONSHIP]->(relationship)";

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
