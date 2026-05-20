namespace Archon.Domain.Graph.Identity
{
    /// <summary>
    /// Represents a deterministic architecture graph identity that is independent of database-local identifiers.
    /// </summary>
    /// <remarks>
    /// A stable key is the durable string identity Archon uses to compare equivalent graph facts across extraction snapshots.
    /// It is deliberately separate from Neo4j internal IDs or any future relational identity because database identifiers are local to a store instance and cannot safely describe logical architecture sameness.
    /// </remarks>
    public readonly record struct StableKey
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StableKey"/> struct.
        /// </summary>
        /// <param name="value">The deterministic stable-key string, including its required prefix.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, or whitespace-only.</exception>
        public StableKey(string? value)
        {
            // Stable keys are graph identities, so accepting a blank string would create ambiguous graph facts.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Stable keys cannot be null, empty, or whitespace.", nameof(value));
            }

            Value = value;
        }

        /// <summary>
        /// Gets the deterministic external stable-key string.
        /// </summary>
        /// <remarks>
        /// The value is suitable for in-memory contracts, future Neo4j properties, API responses, MCP responses, markdown output, and tests.
        /// </remarks>
        public string Value { get; }

        /// <summary>
        /// Returns the stable-key string for diagnostics, serialization fallbacks, and display.
        /// </summary>
        /// <returns>The deterministic stable-key string.</returns>
        public override string ToString()
        {
            // ToString mirrors Value so incidental string formatting never exposes implementation details.
            return Value;
        }
    }
}
