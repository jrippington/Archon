using Archon.Infrastructure.Neo4j.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.Driver
{
    /// <summary>
    /// Opens Neo4j sessions from the dependency-injection-owned singleton driver.
    /// </summary>
    /// <remarks>
    /// The provider centralizes database targeting so higher-level infrastructure components do not duplicate session configuration
    /// or accidentally omit the configured database name.
    /// </remarks>
    public sealed class Neo4jSessionProvider : INeo4jSessionProvider
    {
        private readonly IDriver _driver;
        private readonly IOptions<Neo4jOptions> _options;
        private readonly ILogger<Neo4jSessionProvider> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jSessionProvider"/> class.
        /// </summary>
        /// <param name="driver">The singleton Neo4j driver owned by the dependency-injection container.</param>
        /// <param name="options">The validated Neo4j options that provide the database name.</param>
        /// <param name="logger">The logger used for credential-safe session lifecycle messages.</param>
        public Neo4jSessionProvider(IDriver driver, IOptions<Neo4jOptions> options, ILogger<Neo4jSessionProvider> logger)
        {
            // The provider does not own disposal of the driver. The service provider disposes the singleton driver at shutdown.
            _driver = driver;
            _options = options;
            _logger = logger;
        }

        /// <summary>
        /// Opens a new asynchronous session for the configured database and access mode.
        /// </summary>
        /// <param name="accessMode">The requested driver access mode for read or write routing.</param>
        /// <returns>An <see cref="IAsyncSession"/> that callers dispose after the operation completes.</returns>
        public IAsyncSession OpenSession(AccessMode accessMode)
        {
            // Accessing options here ensures a malformed database name fails before session creation, not after a query has been
            // sent. The logged details identify routing intent without exposing credentials.
            Neo4jOptions options = _options.Value;

            _logger.LogDebug(
                "Opening Neo4j session for database {Database} with access mode {AccessMode}.",
                options.Database,
                accessMode);

            return _driver.AsyncSession(sessionBuilder => sessionBuilder
                .WithDatabase(options.Database!)
                .WithDefaultAccessMode(accessMode));
        }
    }
}
