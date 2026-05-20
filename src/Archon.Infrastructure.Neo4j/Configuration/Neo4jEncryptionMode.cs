namespace Archon.Infrastructure.Neo4j.Configuration
{
    /// <summary>
    /// Describes how the Neo4j driver should negotiate transport encryption for Bolt connections.
    /// </summary>
    /// <remarks>
    /// The values intentionally model the small set of encryption choices Archon exposes to configuration so callers do not pass
    /// arbitrary driver-specific strings through the options object.
    /// </remarks>
    public enum Neo4jEncryptionMode
    {
        /// <summary>
        /// Uses the Neo4j driver default for the configured URI scheme.
        /// </summary>
        Default,

        /// <summary>
        /// Forces encrypted transport when the Neo4j deployment and certificate configuration support it.
        /// </summary>
        Encrypted,

        /// <summary>
        /// Forces unencrypted transport, which is appropriate for the local Testcontainers and Aspire development paths.
        /// </summary>
        Unencrypted
    }
}
