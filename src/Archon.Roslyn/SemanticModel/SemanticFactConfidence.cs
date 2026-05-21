namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Identifies the confidence category assigned to a semantic declaration, relationship, dependency, or unknown fact.
    /// </summary>
    /// <remarks>
    /// Confidence separates compiler-resolved facts from inferred or unresolved facts so downstream graph consumers can reason about architectural certainty without inspecting Roslyn-specific details.
    /// </remarks>
    public enum SemanticFactConfidence
    {
        /// <summary>
        /// Indicates that Roslyn compiler binding resolved the fact to a concrete symbol or symbol relationship.
        /// </summary>
        CompilerResolved,

        /// <summary>
        /// Indicates that the fact was produced by deterministic inference rather than direct compiler symbol binding.
        /// </summary>
        Inferred,

        /// <summary>
        /// Indicates that the fact was only partially resolved because one or more symbol details were unavailable.
        /// </summary>
        PartiallyResolved,

        /// <summary>
        /// Indicates that the fact represents an unresolved or unsupported semantic relationship.
        /// </summary>
        Unresolved
    }
}
