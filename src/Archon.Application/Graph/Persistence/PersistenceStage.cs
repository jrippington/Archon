namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Identifies a high-level persistence stage without exposing any infrastructure-specific implementation type.
    /// </summary>
    /// <remarks>
    /// Application-layer result contracts use stages so hosts, tests, and later orchestration code can report where a failure
    /// occurred while preserving Onion Architecture boundaries. Infrastructure adapters may map their internal operations onto
    /// these stages, but callers should not need to know which database or driver produced the result.
    /// </remarks>
    public enum PersistenceStage
    {
        /// <summary>
        /// Represents an unspecified stage when a failure cannot be classified more precisely.
        /// </summary>
        Unknown,

        /// <summary>
        /// Represents validation of configuration, inputs, or prerequisites before persistence work starts.
        /// </summary>
        Validation,

        /// <summary>
        /// Represents opening or verifying connectivity to the configured persistence store.
        /// </summary>
        Connectivity,

        /// <summary>
        /// Represents graph schema initialization, including constraints and indexes.
        /// </summary>
        SchemaInitialization,

        /// <summary>
        /// Represents future snapshot persistence work after schema initialization has succeeded.
        /// </summary>
        SnapshotPersistence,

        /// <summary>
        /// Represents future graph recreation work for explicitly authorized development and test flows.
        /// </summary>
        GraphRecreation
    }
}
