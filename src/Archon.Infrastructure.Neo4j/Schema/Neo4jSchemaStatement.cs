namespace Archon.Infrastructure.Neo4j.Schema
{
    /// <summary>
    /// Represents one idempotent Neo4j schema statement that should be executed during graph initialization.
    /// </summary>
    /// <remarks>
    /// The statement object carries operational metadata alongside Cypher so the initializer can log progress and return useful
    /// application-level diagnostics without parsing Cypher text.
    /// </remarks>
    public sealed record Neo4jSchemaStatement
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jSchemaStatement"/> record.
        /// </summary>
        /// <param name="name">The stable Neo4j schema object name created by this statement.</param>
        /// <param name="kind">The schema object kind, such as constraint or index.</param>
        /// <param name="cypher">The idempotent Cypher statement that creates the schema object.</param>
        public Neo4jSchemaStatement(string name, string kind, string cypher)
        {
            // The catalog is hand-authored, but defensive normalization keeps logs and diagnostics stable if future entries are added.
            Name = string.IsNullOrWhiteSpace(name) ? "unnamed_schema_object" : name.Trim();
            Kind = string.IsNullOrWhiteSpace(kind) ? "schema" : kind.Trim();
            Cypher = string.IsNullOrWhiteSpace(cypher) ? throw new ArgumentException("Schema statement Cypher is required.", nameof(cypher)) : cypher.Trim();
        }

        /// <summary>
        /// Gets the stable Neo4j schema object name created by this statement.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the schema object kind, such as constraint or index.
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// Gets the idempotent Cypher statement that creates the schema object.
        /// </summary>
        public string Cypher { get; }
    }
}
