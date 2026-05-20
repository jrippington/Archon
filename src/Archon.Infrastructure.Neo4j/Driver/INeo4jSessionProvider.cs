using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.Driver
{
    /// <summary>
    /// Opens Neo4j asynchronous sessions for infrastructure components that need to execute Cypher.
    /// </summary>
    /// <remarks>
    /// The provider hides the singleton driver and database-selection details from health checks, schema initialization, and later
    /// persistence code while still returning the official session abstraction for transaction execution.
    /// </remarks>
    public interface INeo4jSessionProvider
    {
        /// <summary>
        /// Opens a new asynchronous Neo4j session targeting the configured Archon database.
        /// </summary>
        /// <param name="accessMode">The intended session access mode, such as read for health probes or write for persistence.</param>
        /// <returns>An asynchronous Neo4j session that the caller must dispose after use.</returns>
        IAsyncSession OpenSession(AccessMode accessMode);
    }
}
