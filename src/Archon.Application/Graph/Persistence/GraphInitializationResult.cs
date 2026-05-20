namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Reports the outcome of graph schema initialization in application-owned terms.
    /// </summary>
    /// <remarks>
    /// The result contains aggregate counts and safe diagnostics rather than database-specific schema objects. This lets hosts and
    /// tests reason about initialization success without depending on Neo4j driver types.
    /// </remarks>
    public sealed record GraphInitializationResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GraphInitializationResult"/> record.
        /// </summary>
        /// <param name="succeeded">A value indicating whether graph initialization completed without errors.</param>
        /// <param name="statementsExecuted">The number of schema statements attempted successfully by the initializer.</param>
        /// <param name="warnings">The non-fatal warnings produced during initialization.</param>
        /// <param name="errors">The fatal errors produced during initialization.</param>
        public GraphInitializationResult(
            bool succeeded,
            int statementsExecuted,
            IReadOnlyList<PersistenceWarning> warnings,
            IReadOnlyList<PersistenceError> errors)
        {
            // The constructor copies diagnostic collections so callers cannot mutate a completed result after it is returned.
            Succeeded = succeeded;
            StatementsExecuted = Math.Max(0, statementsExecuted);
            Warnings = warnings.ToArray();
            Errors = errors.ToArray();
        }

        /// <summary>
        /// Gets a value indicating whether graph initialization completed without fatal errors.
        /// </summary>
        public bool Succeeded { get; }

        /// <summary>
        /// Gets the number of schema statements attempted successfully by the initializer.
        /// </summary>
        public int StatementsExecuted { get; }

        /// <summary>
        /// Gets the non-fatal warnings produced during initialization.
        /// </summary>
        public IReadOnlyList<PersistenceWarning> Warnings { get; }

        /// <summary>
        /// Gets the fatal errors produced during initialization.
        /// </summary>
        public IReadOnlyList<PersistenceError> Errors { get; }

        /// <summary>
        /// Creates a successful initialization result.
        /// </summary>
        /// <param name="statementsExecuted">The number of schema statements attempted successfully by the initializer.</param>
        /// <param name="warnings">The optional non-fatal warnings produced during initialization.</param>
        /// <returns>A successful graph initialization result.</returns>
        public static GraphInitializationResult Success(int statementsExecuted, IReadOnlyList<PersistenceWarning>? warnings = null)
        {
            // Success results never carry fatal errors, but they may preserve warning details for operational visibility.
            return new GraphInitializationResult(true, statementsExecuted, warnings ?? Array.Empty<PersistenceWarning>(), Array.Empty<PersistenceError>());
        }

        /// <summary>
        /// Creates a failed initialization result.
        /// </summary>
        /// <param name="statementsExecuted">The number of schema statements completed before the failure occurred.</param>
        /// <param name="error">The fatal error that caused initialization to fail.</param>
        /// <param name="warnings">The optional non-fatal warnings produced before the failure occurred.</param>
        /// <returns>A failed graph initialization result.</returns>
        public static GraphInitializationResult Failure(int statementsExecuted, PersistenceError error, IReadOnlyList<PersistenceWarning>? warnings = null)
        {
            // Failure results preserve the completed-statement count so operators can correlate logs with the schema catalog order.
            return new GraphInitializationResult(false, statementsExecuted, warnings ?? Array.Empty<PersistenceWarning>(), new[] { error });
        }
    }
}
