namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Describes a persistence failure in application-owned terms.
    /// </summary>
    /// <remarks>
    /// The error contract intentionally carries safe diagnostic information rather than infrastructure exceptions, driver objects,
    /// or database-specific identifiers. Infrastructure adapters should translate failures into this type before returning them to
    /// application callers.
    /// </remarks>
    public sealed record PersistenceError
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PersistenceError"/> record.
        /// </summary>
        /// <param name="stage">The persistence stage where the error occurred.</param>
        /// <param name="code">A stable, credential-safe error code that callers can use for diagnostics or tests.</param>
        /// <param name="message">A human-readable, credential-safe description of the error.</param>
        public PersistenceError(PersistenceStage stage, string code, string message)
        {
            // The constructor normalizes required text so result objects cannot silently carry blank diagnostic fields.
            Stage = stage;
            Code = string.IsNullOrWhiteSpace(code) ? "PersistenceError" : code.Trim();
            Message = string.IsNullOrWhiteSpace(message) ? "A persistence error occurred." : message.Trim();
        }

        /// <summary>
        /// Gets the persistence stage where the error occurred.
        /// </summary>
        public PersistenceStage Stage { get; }

        /// <summary>
        /// Gets a stable, credential-safe error code for diagnostics and tests.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets a human-readable, credential-safe description of the error.
        /// </summary>
        public string Message { get; }
    }
}
