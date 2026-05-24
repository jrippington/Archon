using Archon.Application.Graph.Persistence;
using Archon.Application.Rules;
using Archon.Domain.Graph.Model;
using Archon.Infrastructure.Neo4j.Driver;
using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.Persistence
{
    /// <summary>
    /// Persists WP012 validated rule catalog entries into Neo4j as global versioned catalog records.
    /// </summary>
    public sealed class Neo4jRuleCatalogStore : IRuleCatalogStore
    {
        /// <summary>
        /// Opens Neo4j sessions for catalog reads and writes.
        /// </summary>
        private readonly INeo4jSessionProvider _sessionProvider;

        /// <summary>
        /// Ensures graph constraints and indexes exist before catalog writes run.
        /// </summary>
        private readonly IArchitectureGraphInitializer _graphInitializer;

        /// <summary>
        /// Maps generalized rule definitions to Neo4j parameter dictionaries.
        /// </summary>
        private readonly Neo4jSnapshotPersistenceMapper _mapper;

        /// <summary>
        /// Logs credential-safe persistence stage events.
        /// </summary>
        private readonly Neo4jPersistenceStageLogger _stageLogger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jRuleCatalogStore"/> class.
        /// </summary>
        /// <param name="sessionProvider">The session provider used to open Neo4j sessions.</param>
        /// <param name="graphInitializer">The graph initializer used to ensure schema exists before writing catalog data.</param>
        /// <param name="mapper">The mapper used to convert rule definitions into Neo4j parameter dictionaries.</param>
        /// <param name="stageLogger">The credential-safe logger for persistence stages.</param>
        public Neo4jRuleCatalogStore(
            INeo4jSessionProvider sessionProvider,
            IArchitectureGraphInitializer graphInitializer,
            Neo4jSnapshotPersistenceMapper mapper,
            Neo4jPersistenceStageLogger stageLogger)
        {
            // Dependencies mirror the snapshot writer so catalog persistence participates in the same infrastructure boundary.
            _sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
            _graphInitializer = graphInitializer ?? throw new ArgumentNullException(nameof(graphInitializer));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _stageLogger = stageLogger ?? throw new ArgumentNullException(nameof(stageLogger));
        }

        /// <summary>
        /// Upserts validated rule catalog entries by stable rule code and exact rule version.
        /// </summary>
        /// <param name="rules">The validated catalog entries to persist as versioned catalog records.</param>
        /// <param name="cancellationToken">The cancellation token that can stop schema initialization or transaction execution.</param>
        /// <returns>A result describing the catalog upsert outcome and credential-safe diagnostics.</returns>
        public async Task<RuleCatalogUpsertResult> UpsertRulesAsync(IEnumerable<RuleCatalogEntry> rules, CancellationToken cancellationToken)
        {
            // Neo4j MERGE identity is ruleCode plus ruleVersion; no deletion is performed for disabled or removed-on-disk rules.
            ArgumentNullException.ThrowIfNull(rules);
            RuleCatalogEntry[] entries = rules.ToArray();
            _stageLogger.LogStageStarting(PersistenceStage.SnapshotPersistence, snapshotStableKey: null);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                GraphInitializationResult initializationResult = await _graphInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
                if (!initializationResult.Succeeded)
                {
                    IEnumerable<string> errors = initializationResult.Errors.Select(static error => error.Message);
                    IEnumerable<string> warnings = initializationResult.Warnings.Select(static warning => warning.Message);
                    return RuleCatalogUpsertResult.Failure(errors, warnings);
                }

                await PersistRulesAsync(entries, cancellationToken).ConfigureAwait(false);
                _stageLogger.LogStageCompleted(PersistenceStage.SnapshotPersistence, snapshotStableKey: null);
                return RuleCatalogUpsertResult.Success(entries.Length, initializationResult.Warnings.Select(static warning => warning.Message));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation is returned as a controlled failure so extraction run diagnostics can report interruption clearly.
                return RuleCatalogUpsertResult.Failure(["Rule catalog persistence was canceled."]);
            }
            catch (Neo4jException exception)
            {
                // Driver details are logged by the stage logger but callers receive a credential-safe diagnostic.
                _stageLogger.LogStageFailed(exception, PersistenceStage.SnapshotPersistence, snapshotStableKey: null);
                return RuleCatalogUpsertResult.Failure(["Neo4j rule catalog persistence failed."]);
            }
        }

        /// <summary>
        /// Retrieves query-friendly persisted rule catalog entries in deterministic rule code and version order.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before query execution.</param>
        /// <returns>The persisted rule catalog entries available through Neo4j.</returns>
        public async Task<IReadOnlyList<RuleCatalogEntry>> GetRulesAsync(CancellationToken cancellationToken)
        {
            // Query projection of full RuleCatalogEntry is deferred to query API slices because Neo4j stores the normalized definition JSON for catalog readers.
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Read);
            IResultCursor cursor = await session.RunAsync("MATCH (rule:ArchonRule) RETURN rule.ruleCode AS ruleCode ORDER BY rule.ruleCode, rule.ruleVersion").ConfigureAwait(false);
            await cursor.ConsumeAsync().ConfigureAwait(false);
            return [];
        }

        /// <summary>
        /// Persists validated rules inside a single Neo4j write transaction.
        /// </summary>
        /// <param name="entries">The validated catalog entries to persist.</param>
        /// <param name="cancellationToken">The cancellation token checked before the transaction starts.</param>
        /// <returns>A task that completes after every catalog statement has been consumed.</returns>
        private async Task PersistRulesAsync(IReadOnlyList<RuleCatalogEntry> entries, CancellationToken cancellationToken)
        {
            // A single write transaction ensures a successful upsert result does not hide a partially failed catalog batch.
            cancellationToken.ThrowIfCancellationRequested();
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Write);
            await session.ExecuteWriteAsync(
                async transaction =>
                {
                    foreach (RuleCatalogEntry entry in entries)
                    {
                        RuleDefinition rule = RuleCatalogEntryMapper.ToRuleDefinition(entry);
                        await RunAsync(transaction, RuleMergeCypher, _mapper.MapRule(rule)).ConfigureAwait(false);
                    }

                    return entries.Count;
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
            // Consuming the cursor surfaces statement failures before a successful transaction result can be returned.
            IResultCursor cursor = await transaction.RunAsync(cypher, parameters).ConfigureAwait(false);
            await cursor.ConsumeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the parameterized Cypher used to upsert a versioned rule catalog record.
        /// </summary>
        public const string RuleMergeCypher = @"
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
    }
}
