namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Identifies the graph declaration category produced by semantic symbol extraction.
    /// </summary>
    /// <remarks>
    /// The declaration kind intentionally mirrors the normalized graph node kinds used by the domain model while remaining independent from persistence-specific concerns.
    /// </remarks>
    public enum SemanticDeclarationKind
    {
        /// <summary>
        /// Represents a namespace declaration or compiler namespace symbol.
        /// </summary>
        Namespace,

        /// <summary>
        /// Represents a source-declared type such as a class, struct, interface, enum, record, or delegate.
        /// </summary>
        Type,

        /// <summary>
        /// Represents a method-like member, including constructors.
        /// </summary>
        Method,

        /// <summary>
        /// Represents a property or indexer declaration.
        /// </summary>
        Property,

        /// <summary>
        /// Represents a field or constant declaration.
        /// </summary>
        Field
    }
}
