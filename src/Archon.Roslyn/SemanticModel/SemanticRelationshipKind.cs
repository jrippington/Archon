namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Identifies semantic relationship categories emitted by shared Roslyn extraction.
    /// </summary>
    /// <remarks>
    /// The relationship vocabulary mirrors the graph edge language used by semantic extraction while staying independent from persistence-specific implementation details.
    /// </remarks>
    public enum SemanticRelationshipKind
    {
        /// <summary>
        /// Represents a directly observed declaration containment relationship.
        /// </summary>
        Contains,

        /// <summary>
        /// Represents a compiler-resolved invocation from one executable symbol to another method-like symbol.
        /// </summary>
        Calls,

        /// <summary>
        /// Represents a type or member implementation relationship to an interface contract.
        /// </summary>
        Implements,

        /// <summary>
        /// Represents a type inheritance or member override relationship.
        /// </summary>
        Inherits,

        /// <summary>
        /// Represents a constructor parameter dependency accepted as a constructor-injected collaborator.
        /// </summary>
        Injects,

        /// <summary>
        /// Represents a symbol dependency that is not more specifically described by another relationship kind.
        /// </summary>
        DependsOn
    }
}
