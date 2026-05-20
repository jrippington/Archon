namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Reports the outcome of explicitly destructive graph recreation in application-owned terms.
    /// </summary>
    /// <remarks>
    /// Recreation is distinct from schema initialization because it can delete Archon-owned graph records. The result therefore records
    /// whether the destructive operation was authorized, how many records were cleared, whether schema initialization succeeded after
    /// clearing, and any safe warnings or errors returned by the infrastructure adapter.
    /// </remarks>
    public sealed record GraphRecreationResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GraphRecreationResult"/> record.
        /// </summary>
        /// <param name="succeeded">A value indicating whether graph recreation completed without fatal errors.</param>
        /// <param name="authorized">A value indicating whether the caller supplied the explicit destructive confirmation phrase.</param>
        /// <param name="recordsDeleted">The number of Archon-owned graph nodes deleted during recreation.</param>
        /// <param name="schemaStatementsExecuted">The number of schema statements successfully executed after clearing data.</param>
        /// <param name="warnings">The non-fatal warnings produced during recreation.</param>
        /// <param name="errors">The fatal errors produced during recreation.</param>
        public GraphRecreationResult(
            bool succeeded,
            bool authorized,
            long recordsDeleted,
            int schemaStatementsExecuted,
            IReadOnlyList<PersistenceWarning> warnings,
            IReadOnlyList<PersistenceError> errors)
        {
            // Counts are normalized defensively so callers never receive negative operational metrics from a failed adapter path.
            Succeeded = succeeded;
            Authorized = authorized;
            RecordsDeleted = Math.Max(0, recordsDeleted);
            SchemaStatementsExecuted = Math.Max(0, schemaStatementsExecuted);
            Warnings = warnings.ToArray();
            Errors = errors.ToArray();
        }

        /// <summary>
        /// Gets a value indicating whether graph recreation completed without fatal errors.
        /// </summary>
        public bool Succeeded { get; }

        /// <summary>
        /// Gets a value indicating whether the caller supplied the explicit destructive confirmation phrase.
        /// </summary>
        public bool Authorized { get; }

        /// <summary>
        /// Gets the number of Archon-owned graph nodes deleted during recreation.
        /// </summary>
        public long RecordsDeleted { get; }

        /// <summary>
        /// Gets the number of schema statements successfully executed after clearing data.
        /// </summary>
        public int SchemaStatementsExecuted { get; }

        /// <summary>
        /// Gets the non-fatal warnings produced during recreation.
        /// </summary>
        public IReadOnlyList<PersistenceWarning> Warnings { get; }

        /// <summary>
        /// Gets the fatal errors produced during recreation.
        /// </summary>
        public IReadOnlyList<PersistenceError> Errors { get; }

        /// <summary>
        /// Creates a result for a request that failed the destructive guard and did not clear data.
        /// </summary>
        /// <returns>An unauthorized graph recreation result containing a safe guard error.</returns>
        public static GraphRecreationResult Unauthorized()
        {
            // Unauthorized results are failures, but they prove the guard worked and no destructive records were touched.
            return new GraphRecreationResult(
                false,
                false,
                0,
                0,
                Array.Empty<PersistenceWarning>(),
                new[]
                {
                    new PersistenceError(
                        PersistenceStage.GraphRecreation,
                        "GraphRecreationNotAuthorized",
                        "Graph recreation requires the explicit destructive confirmation phrase.")
                });
        }

        /// <summary>
        /// Creates a successful graph recreation result.
        /// </summary>
        /// <param name="recordsDeleted">The number of Archon-owned graph nodes deleted during recreation.</param>
        /// <param name="schemaStatementsExecuted">The number of schema statements successfully executed after clearing data.</param>
        /// <param name="warnings">The optional non-fatal warnings produced during recreation.</param>
        /// <returns>A successful graph recreation result.</returns>
        public static GraphRecreationResult Success(long recordsDeleted, int schemaStatementsExecuted, IReadOnlyList<PersistenceWarning>? warnings = null)
        {
            // Success means the destructive guard passed, data clearing completed, and schema initialization completed afterward.
            return new GraphRecreationResult(true, true, recordsDeleted, schemaStatementsExecuted, warnings ?? Array.Empty<PersistenceWarning>(), Array.Empty<PersistenceError>());
        }

        /// <summary>
        /// Creates a failed graph recreation result after the destructive guard has already authorized the operation.
        /// </summary>
        /// <param name="recordsDeleted">The number of Archon-owned graph nodes deleted before the failure occurred.</param>
        /// <param name="schemaStatementsExecuted">The number of schema statements completed before the failure occurred.</param>
        /// <param name="error">The fatal error that caused recreation to fail.</param>
        /// <param name="warnings">The optional non-fatal warnings produced before the failure occurred.</param>
        /// <returns>A failed graph recreation result.</returns>
        public static GraphRecreationResult Failure(long recordsDeleted, int schemaStatementsExecuted, PersistenceError error, IReadOnlyList<PersistenceWarning>? warnings = null)
        {
            // Failures preserve deletion and schema counts so operators can distinguish guard failures from mid-recreation failures.
            return new GraphRecreationResult(false, true, recordsDeleted, schemaStatementsExecuted, warnings ?? Array.Empty<PersistenceWarning>(), new[] { error });
        }
    }
}
