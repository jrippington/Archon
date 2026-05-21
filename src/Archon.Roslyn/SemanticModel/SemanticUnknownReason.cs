namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Identifies why semantic extraction could not produce a fully resolved compiler-backed fact.
    /// </summary>
    /// <remarks>
    /// Unknown reasons are query-facing categories. They keep later API, rule, and MCP consumers from treating unresolved semantic gaps as absent architecture information.
    /// </remarks>
    public enum SemanticUnknownReason
    {
        /// <summary>
        /// Indicates that Roslyn could not bind the referenced symbol.
        /// </summary>
        UnresolvedSymbol,

        /// <summary>
        /// Indicates that Roslyn found candidate symbols but could not choose one definitive overload or member.
        /// </summary>
        AmbiguousOverload,

        /// <summary>
        /// Indicates that a missing reference or metadata assembly likely prevented full binding.
        /// </summary>
        MissingReference,

        /// <summary>
        /// Indicates that the extractor recognized a semantic shape that this slice does not yet support fully.
        /// </summary>
        UnsupportedSemanticForm,

        /// <summary>
        /// Indicates that dynamic dispatch prevents deterministic static target resolution.
        /// </summary>
        DynamicDispatch,

        /// <summary>
        /// Indicates that reflection or string-based target lookup prevents deterministic static target resolution.
        /// </summary>
        ReflectionTarget,

        /// <summary>
        /// Indicates that Visual Basic late binding prevented deterministic static target resolution.
        /// </summary>
        LateBoundCall
    }
}
