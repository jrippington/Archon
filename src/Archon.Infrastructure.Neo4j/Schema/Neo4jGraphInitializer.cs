using Archon.Application.Graph.Persistence;
using Archon.Infrastructure.Neo4j.Driver;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.Schema
{
    /// <summary>
    /// Initializes a configured Neo4j database with the constraints and indexes required by Archon graph persistence.
    /// </summary>
    /// <remarks>
    /// The initializer executes the explicit schema statement catalog in a deterministic order. It returns application-layer result
    /// contracts so callers do not need Neo4j driver types to understand success, counts, or safe failure diagnostics.
    /// </remarks>
    public sealed class Neo4jGraphInitializer : IArchitectureGraphInitializer
    {
        private readonly INeo4jSessionProvider _sessionProvider;
        private readonly Neo4jSchemaStatementCatalog _statementCatalog;
        private readonly ILogger<Neo4jGraphInitializer> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jGraphInitializer"/> class.
        /// </summary>
        /// <param name="sessionProvider">The session provider that opens configured write sessions for schema statements.</param>
        /// <param name="statementCatalog">The ordered catalog of idempotent schema statements.</param>
        /// <param name="logger">The logger used for credential-safe schema initialization progress and failures.</param>
        public Neo4jGraphInitializer(
            INeo4jSessionProvider sessionProvider,
            Neo4jSchemaStatementCatalog statementCatalog,
            ILogger<Neo4jGraphInitializer> logger)
        {
            // Dependencies are stored only; schema work is performed when InitializeAsync is called so hosts control when database
            // initialization occurs.
            _sessionProvider = sessionProvider;
            _statementCatalog = statementCatalog;
            _logger = logger;
        }

        /// <summary>
        /// Ensures the configured Neo4j database has the schema objects required by Archon graph persistence.
        /// </summary>
        /// <param name="cancellationToken">A token that cancels initialization before or between asynchronous schema statements.</param>
        /// <returns>A result describing initialization success, completed statement count, and safe diagnostics.</returns>
        public async Task<GraphInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
        {
            // The method deliberately executes schema statements one at a time. This keeps logs aligned with Neo4j metadata and
            // gives operators the last completed statement count if a deployment fails during initialization.
            IReadOnlyList<Neo4jSchemaStatement> statements = _statementCatalog.GetStatements();
            int statementsExecuted = 0;

            _logger.LogInformation("Starting Neo4j graph schema initialization with {StatementCount} statements.", statements.Count);

            try
            {
                await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Write);

                foreach (Neo4jSchemaStatement statement in statements)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _logger.LogInformation(
                        "Executing Neo4j schema {SchemaKind} {SchemaName}.",
                        statement.Kind,
                        statement.Name);

                    await session.ExecuteWriteAsync(
                        async transaction =>
                        {
                            // Schema Cypher contains no user-provided values; names are selected from a closed catalog of constants.
                            IResultCursor cursor = await transaction.RunAsync(statement.Cypher).ConfigureAwait(false);
                            await cursor.ConsumeAsync().ConfigureAwait(false);
                        }).ConfigureAwait(false);

                    statementsExecuted++;
                }

                _logger.LogInformation("Completed Neo4j graph schema initialization after {StatementCount} statements.", statementsExecuted);
                return GraphInitializationResult.Success(statementsExecuted);
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation is reported as a failed initialization result so callers can decide whether to retry or abort startup.
                _logger.LogWarning(exception, "Neo4j graph schema initialization was canceled after {StatementCount} statements.", statementsExecuted);
                return GraphInitializationResult.Failure(
                    statementsExecuted,
                    new PersistenceError(PersistenceStage.SchemaInitialization, "GraphInitializationCanceled", "Neo4j graph schema initialization was canceled."));
            }
            catch (Neo4jException exception)
            {
                // Neo4j failures are mapped to a safe application error; the exception is logged for local diagnostics without
                // adding credentials to result messages.
                _logger.LogError(exception, "Neo4j graph schema initialization failed after {StatementCount} statements.", statementsExecuted);
                return GraphInitializationResult.Failure(
                    statementsExecuted,
                    new PersistenceError(PersistenceStage.SchemaInitialization, "GraphSchemaInitializationFailed", "Neo4j graph schema initialization failed."));
            }
        }
    }
}
