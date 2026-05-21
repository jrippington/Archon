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
        /// Indicates that the fact came from generated source and should be distinguished from hand-maintained source facts.
        /// </summary>
        Generated,

        /// <summary>
        /// Indicates that the fact targets a compiler metadata symbol without a source declaration in the analyzed repository.
        /// </summary>
        MetadataOnly,

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
