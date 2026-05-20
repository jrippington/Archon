using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.Driver
{
    /// <summary>
    /// Creates Neo4j driver instances from validated infrastructure configuration.
    /// </summary>
    /// <remarks>
    /// The factory is a narrow lifecycle seam that lets tests verify registration and disposal behavior without forcing higher
    /// layers to understand driver construction details or credential handling.
    /// </remarks>
    public interface INeo4jDriverFactory
    {
        /// <summary>
        /// Creates a configured Neo4j driver instance for dependency-injection ownership.
        /// </summary>
        /// <returns>A configured <see cref="IDriver"/> that the service provider owns and disposes.</returns>
        IDriver CreateDriver();
    }
}
