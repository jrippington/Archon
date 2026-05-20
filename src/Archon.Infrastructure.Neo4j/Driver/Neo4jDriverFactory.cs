using Archon.Infrastructure.Neo4j.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.Driver
{
    /// <summary>
    /// Creates official Neo4j .NET driver instances from validated Archon configuration.
    /// </summary>
    /// <remarks>
    /// The factory is responsible for translating Archon's safe options model into driver settings. It logs only non-secret
    /// operational context such as URI scheme and database name, and it leaves password values inside the driver auth token.
    /// </remarks>
    public sealed class Neo4jDriverFactory : INeo4jDriverFactory
    {
        private readonly IOptions<Neo4jOptions> _options;
        private readonly ILogger<Neo4jDriverFactory> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jDriverFactory"/> class.
        /// </summary>
        /// <param name="options">The validated Neo4j connection options supplied by the options pipeline.</param>
        /// <param name="logger">The logger used for credential-safe driver lifecycle messages.</param>
        public Neo4jDriverFactory(IOptions<Neo4jOptions> options, ILogger<Neo4jDriverFactory> logger)
        {
            // The constructor stores dependencies only. Options are read when CreateDriver executes so validation and reloadable
            // configuration behavior remain owned by the Microsoft options infrastructure.
            _options = options;
            _logger = logger;
        }

        /// <summary>
        /// Creates a configured Neo4j driver using the current validated options snapshot.
        /// </summary>
        /// <returns>A configured <see cref="IDriver"/> instance ready for singleton reuse by the infrastructure adapter.</returns>
        public IDriver CreateDriver()
        {
            // Accessing Value intentionally triggers options validation before credentials reach the driver constructor.
            Neo4jOptions options = _options.Value;
            Uri uri = new(options.Uri!, UriKind.Absolute);

            _logger.LogInformation(
                "Creating Neo4j driver for scheme {Scheme}, host {Host}, and database {Database}.",
                uri.Scheme,
                uri.Host,
                options.Database);

            IAuthToken authToken = AuthTokens.Basic(options.Username!, options.Password!);

            return GraphDatabase.Driver(options.Uri, authToken, configurationBuilder =>
            {
                // Driver-level timeouts and retry settings are configured here so every session provider and health check shares
                // one consistent connection policy.
                configurationBuilder.WithConnectionAcquisitionTimeout(options.ConnectionTimeout)
                    .WithMaxTransactionRetryTime(options.MaxTransactionRetryTime);

                ApplyEncryptionMode(configurationBuilder, options.EncryptionMode);
            });
        }

        /// <summary>
        /// Applies Archon's optional encryption setting to the Neo4j driver configuration builder.
        /// </summary>
        /// <param name="configurationBuilder">The Neo4j driver configuration builder being populated.</param>
        /// <param name="encryptionMode">The validated encryption mode requested by configuration.</param>
        private static void ApplyEncryptionMode(ConfigBuilder configurationBuilder, Neo4jEncryptionMode encryptionMode)
        {
            // The default mode leaves URI-scheme behavior untouched, while explicit modes let local tests and future production
            // deployments document the transport contract they expect.
            switch (encryptionMode)
            {
                case Neo4jEncryptionMode.Default:
                    break;
                case Neo4jEncryptionMode.Encrypted:
                    configurationBuilder.WithEncryptionLevel(EncryptionLevel.Encrypted);
                    break;
                case Neo4jEncryptionMode.Unencrypted:
                    configurationBuilder.WithEncryptionLevel(EncryptionLevel.None);
                    break;
            }
        }
    }
}
