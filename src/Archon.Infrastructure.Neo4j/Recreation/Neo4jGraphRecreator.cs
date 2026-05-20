using Archon.Application.Graph.Persistence;
using Archon.Infrastructure.Neo4j.Driver;
using Archon.Infrastructure.Neo4j.Schema;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.Recreation
{
    /// <summary>
    /// Recreates the Archon-owned portion of a Neo4j graph after an explicit destructive confirmation request.
    /// </summary>
    /// <remarks>
    /// The recreator is intended for local development and automated integration tests. It deletes only nodes carrying Archon-owned
    /// labels from a closed catalog, then delegates schema creation to the existing graph initializer so constraints and indexes remain
    /// available after the reset. It is not a migration mechanism and must not be exposed through production API endpoints.
    /// </remarks>
    public sealed class Neo4jGraphRecreator : IArchitectureGraphRecreator
    {
        private static readonly IReadOnlyList<string> ArchonLabels = new[]
        {
            Neo4jSchemaNames.Labels.GeneratedSummary,
            Neo4jSchemaNames.Labels.Metric,
            Neo4jSchemaNames.Labels.Finding,
            Neo4jSchemaNames.Labels.Rule,
            Neo4jSchemaNames.Labels.Evidence,
            Neo4jSchemaNames.Labels.Relationship,
            Neo4jSchemaNames.Labels.Node,
            Neo4jSchemaNames.Labels.Snapshot,
            Neo4jSchemaNames.Labels.Solution,
            Neo4jSchemaNames.Labels.Repository
        };

        private readonly INeo4jSessionProvider _sessionProvider;
        private readonly IArchitectureGraphInitializer _graphInitializer;
        private readonly ILogger<Neo4jGraphRecreator> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jGraphRecreator"/> class.
        /// </summary>
        /// <param name="sessionProvider">The session provider that opens configured write sessions for destructive Cypher.</param>
        /// <param name="graphInitializer">The graph initializer used to recreate constraints and indexes after data is cleared.</param>
        /// <param name="logger">The logger used for credential-safe destructive-operation progress and failures.</param>
        public Neo4jGraphRecreator(
            INeo4jSessionProvider sessionProvider,
            IArchitectureGraphInitializer graphInitializer,
            ILogger<Neo4jGraphRecreator> logger)
        {
            // Store dependencies for later use; no destructive work occurs during construction or dependency-injection validation.
            _sessionProvider = sessionProvider;
            _graphInitializer = graphInitializer;
            _logger = logger;
        }

        /// <summary>
        /// Clears Archon-owned Neo4j graph records and recreates schema when the caller supplies the exact destructive confirmation phrase.
        /// </summary>
        /// <param name="request">The explicit recreation request containing the required destructive confirmation phrase.</param>
        /// <param name="cancellationToken">A token that cancels recreation before or between asynchronous graph operations.</param>
        /// <returns>A result describing authorization, deleted record count, schema initialization count, warnings, and errors.</returns>
        public async Task<GraphRecreationResult> RecreateGraphAsync(GraphRecreationRequest request, CancellationToken cancellationToken = default)
        {
            // The guard executes before opening a write session so ordinary callers cannot accidentally erase graph data.
            ArgumentNullException.ThrowIfNull(request);

            if (!request.IsAuthorized)
            {
                _logger.LogWarning("Rejected Neo4j graph recreation because the destructive confirmation phrase was not supplied.");
                return GraphRecreationResult.Unauthorized();
            }

            long recordsDeleted = 0;

            _logger.LogWarning(
                "Starting explicitly destructive Neo4j graph recreation for Archon-owned labels. Reason supplied: {HasReason}.",
                !string.IsNullOrWhiteSpace(request.Reason));

            try
            {
                recordsDeleted = await ClearArchonRecordsAsync(cancellationToken).ConfigureAwait(false);
                GraphInitializationResult initializationResult = await _graphInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);

                if (!initializationResult.Succeeded)
                {
                    // Schema initialization errors are translated into a recreation-stage failure because callers requested one reset operation.
                    PersistenceError error = initializationResult.Errors.FirstOrDefault()
                        ?? new PersistenceError(PersistenceStage.GraphRecreation, "GraphRecreationSchemaInitializationFailed", "Graph recreation cleared data but schema initialization failed.");

                    _logger.LogError(
                        "Neo4j graph recreation cleared {RecordsDeleted} records but schema initialization failed after {StatementCount} statements.",
                        recordsDeleted,
                        initializationResult.StatementsExecuted);

                    return GraphRecreationResult.Failure(recordsDeleted, initializationResult.StatementsExecuted, error, initializationResult.Warnings);
                }

                _logger.LogWarning(
                    "Completed explicitly destructive Neo4j graph recreation after deleting {RecordsDeleted} records and executing {StatementCount} schema statements.",
                    recordsDeleted,
                    initializationResult.StatementsExecuted);

                return GraphRecreationResult.Success(recordsDeleted, initializationResult.StatementsExecuted, initializationResult.Warnings);
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation returns a safe failure result so local tooling and tests can report an interrupted reset without secrets.
                _logger.LogWarning(exception, "Neo4j graph recreation was canceled after deleting {RecordsDeleted} records.", recordsDeleted);
                return GraphRecreationResult.Failure(
                    recordsDeleted,
                    0,
                    new PersistenceError(PersistenceStage.GraphRecreation, "GraphRecreationCanceled", "Neo4j graph recreation was canceled."));
            }
            catch (Neo4jException exception)
            {
                // Neo4j-specific failures are logged for diagnostics and mapped to a credential-safe application result.
                _logger.LogError(exception, "Neo4j graph recreation failed after deleting {RecordsDeleted} records.", recordsDeleted);
                return GraphRecreationResult.Failure(
                    recordsDeleted,
                    0,
                    new PersistenceError(PersistenceStage.GraphRecreation, "GraphRecreationFailed", "Neo4j graph recreation failed."));
            }
        }

        /// <summary>
        /// Deletes all nodes that carry Archon-owned labels from the configured Neo4j database.
        /// </summary>
        /// <param name="cancellationToken">A token that cancels the clear operation before or during transaction execution.</param>
        /// <returns>The number of distinct Archon-owned nodes deleted from the graph.</returns>
        private async Task<long> ClearArchonRecordsAsync(CancellationToken cancellationToken)
        {
            // The Cypher statement uses a closed list of labels from Neo4jSchemaNames. No caller-provided labels or relationship types
            // are interpolated, and DETACH DELETE removes relationships attached to deleted Archon nodes safely.
            await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Write);

            return await session.ExecuteWriteAsync(
                async transaction =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    IResultCursor cursor = await transaction.RunAsync(
                        "MATCH (n) WHERE any(label IN labels(n) WHERE label IN $labels) WITH DISTINCT n DETACH DELETE n RETURN count(n) AS recordsDeleted",
                        new { labels = ArchonLabels }).ConfigureAwait(false);

                    IRecord record = await cursor.SingleAsync().ConfigureAwait(false);
                    return record["recordsDeleted"].As<long>();
                }).ConfigureAwait(false);
        }
    }
}
