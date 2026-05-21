namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Identifies semantic relationship categories emitted by shared Roslyn extraction.
    /// </summary>
    /// <remarks>
    /// Work Item 1 emits containment only, but the shared enum establishes the graph-ready relationship vocabulary for later symbol dependency slices.
    /// </remarks>
    public enum SemanticRelationshipKind
    {
        /// <summary>
        /// Represents a directly observed declaration containment relationship.
        /// </summary>
        Contains
    }
}
