namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Identifies the normalized compiler diagnostic severity stored by semantic extraction.
    /// </summary>
    /// <remarks>
    /// The enum avoids exposing Roslyn diagnostic objects at the graph-ready boundary while preserving the severity categories contributors use when judging extraction quality.
    /// </remarks>
    public enum SemanticDiagnosticSeverity
    {
        /// <summary>
        /// Indicates that the compiler reported hidden informational context.
        /// </summary>
        Hidden,

        /// <summary>
        /// Indicates that the compiler reported an informational diagnostic.
        /// </summary>
        Info,

        /// <summary>
        /// Indicates that the compiler reported a warning that may reduce semantic certainty but does not necessarily prevent extraction.
        /// </summary>
        Warning,

        /// <summary>
        /// Indicates that the compiler reported an error that may leave symbols unresolved while still allowing partial extraction.
        /// </summary>
        Error
    }
}
