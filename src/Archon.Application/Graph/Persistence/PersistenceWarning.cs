namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Describes a non-fatal persistence warning in application-owned terms.
    /// </summary>
    /// <remarks>
    /// Warnings let infrastructure report noteworthy but non-blocking conditions, such as an already-existing schema object or a
    /// fallback path, without forcing callers to parse logs or database-specific messages.
    /// </remarks>
    public sealed record PersistenceWarning
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PersistenceWarning"/> record.
        /// </summary>
        /// <param name="stage">The persistence stage that produced the warning.</param>
        /// <param name="code">A stable, credential-safe warning code.</param>
        /// <param name="message">A human-readable, credential-safe warning message.</param>
        public PersistenceWarning(PersistenceStage stage, string code, string message)
        {
            // Normalization keeps warning output deterministic and prevents empty diagnostic values from escaping the adapter.
            Stage = stage;
            Code = string.IsNullOrWhiteSpace(code) ? "PersistenceWarning" : code.Trim();
            Message = string.IsNullOrWhiteSpace(message) ? "A persistence warning occurred." : message.Trim();
        }

        /// <summary>
        /// Gets the persistence stage that produced the warning.
        /// </summary>
        public PersistenceStage Stage { get; }

        /// <summary>
        /// Gets a stable, credential-safe warning code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets a human-readable, credential-safe warning message.
        /// </summary>
        public string Message { get; }
    }
}
