namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Captures the explicit caller intent required before destructive graph recreation may run.
    /// </summary>
    /// <remarks>
    /// The confirmation phrase is deliberately loud and exact so ordinary initialization, health checking, and future snapshot
    /// persistence code cannot accidentally supply a truthy flag and erase graph data. Infrastructure adapters should reject every
    /// request that does not contain <see cref="RequiredConfirmationPhrase"/> exactly.
    /// </remarks>
    public sealed record GraphRecreationRequest
    {
        /// <summary>
        /// Gets the exact phrase required to authorize destructive graph recreation.
        /// </summary>
        public const string RequiredConfirmationPhrase = "DELETE ARCHON GRAPH DATA AND RECREATE SCHEMA";

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphRecreationRequest"/> record.
        /// </summary>
        /// <param name="confirmationPhrase">The exact destructive confirmation phrase supplied by the caller.</param>
        /// <param name="reason">The optional human-readable reason for the local or test reset.</param>
        public GraphRecreationRequest(string confirmationPhrase, string? reason = null)
        {
            // Store caller text without inferring authorization; IsAuthorized performs the exact comparison used by infrastructure.
            ConfirmationPhrase = confirmationPhrase ?? string.Empty;
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        }

        /// <summary>
        /// Gets the destructive confirmation phrase supplied by the caller.
        /// </summary>
        public string ConfirmationPhrase { get; }

        /// <summary>
        /// Gets the optional reason supplied by a developer or test for audit-friendly logging.
        /// </summary>
        public string? Reason { get; }

        /// <summary>
        /// Gets a value indicating whether the request contains the exact destructive confirmation phrase.
        /// </summary>
        public bool IsAuthorized
        {
            get
            {
                // Ordinal comparison avoids culture-sensitive surprises for the guard phrase that protects destructive behavior.
                return string.Equals(ConfirmationPhrase, RequiredConfirmationPhrase, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Creates a request that explicitly authorizes destructive graph recreation.
        /// </summary>
        /// <param name="reason">The optional human-readable reason for the local or test reset.</param>
        /// <returns>An authorized graph recreation request.</returns>
        public static GraphRecreationRequest CreateAuthorized(string? reason = null)
        {
            // This factory makes tests and local tooling readable while keeping the required phrase centralized in one contract.
            return new GraphRecreationRequest(RequiredConfirmationPhrase, reason);
        }
    }
}
