namespace Archon.Infrastructure.Neo4j.Configuration
{
    /// <summary>
    /// Represents the validated configuration required to connect Archon infrastructure code to Neo4j.
    /// </summary>
    /// <remarks>
    /// The options live in the infrastructure adapter because they describe an external database implementation rather than a
    /// domain concept. Password values must be supplied through secure configuration providers in real hosts and must never be
    /// copied into validation or logging messages.
    /// </remarks>
    public sealed class Neo4jOptions
    {
        /// <summary>
        /// Defines the configuration section name used by host applications and tests when binding Neo4j settings.
        /// </summary>
        public const string SectionName = "Neo4j";

        /// <summary>
        /// Defines the default number of homogeneous persistence records sent in one high-volume Neo4j list-parameter batch.
        /// </summary>
        /// <remarks>
        /// The value is deliberately conservative for local containers and development machines while still reducing per-record Cypher
        /// execution overhead for large snapshots. Hosts can override <see cref="PersistenceBatchSize" /> when resource limits require
        /// smaller or larger payloads.
        /// </remarks>
        public const int DefaultPersistenceBatchSize = 1_000;

        /// <summary>
        /// Gets or sets the Bolt-compatible Neo4j connection URI, such as <c>bolt://localhost:7687</c>.
        /// </summary>
        public string? Uri { get; set; }

        /// <summary>
        /// Gets or sets the Neo4j database name to target for queries when the server supports multiple databases.
        /// </summary>
        /// <remarks>
        /// Community and local development installations usually use <c>neo4j</c>. Archon still validates the value so later
        /// persistence operations can use one deterministic database target.
        /// </remarks>
        public string? Database { get; set; } = "neo4j";

        /// <summary>
        /// Gets or sets the user name used for Neo4j basic authentication.
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// Gets or sets the secret password used for Neo4j basic authentication.
        /// </summary>
        /// <remarks>
        /// This value is intentionally a plain string option because the .NET configuration system supplies resolved values to
        /// consumers. Validation and logging code must treat it as sensitive and avoid echoing it in failures.
        /// </remarks>
        public string? Password { get; set; }

        /// <summary>
        /// Gets or sets the maximum time allowed when acquiring a connection from the driver pool.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the maximum time the driver may spend retrying transient transaction work.
        /// </summary>
        public TimeSpan MaxTransactionRetryTime { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the optional transport encryption mode for the Neo4j driver.
        /// </summary>
        public Neo4jEncryptionMode EncryptionMode { get; set; } = Neo4jEncryptionMode.Default;

        /// <summary>
        /// Gets or sets the maximum number of records sent to one static high-volume persistence statement.
        /// </summary>
        /// <remarks>
        /// Snapshot persistence uses this value to bound <c>UNWIND</c>-style list parameters so large node, metric, evidence, or
        /// relationship sections can be written with fewer Cypher executions without building an unbounded parameter payload. The
        /// default is <see cref="DefaultPersistenceBatchSize" /> rows when configuration omits the setting.
        /// </remarks>
        public int PersistenceBatchSize { get; set; } = DefaultPersistenceBatchSize;
    }
}
