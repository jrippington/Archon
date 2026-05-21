namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Identifies the source language used by a semantic extraction request or extracted fact.
    /// </summary>
    /// <remarks>
    /// The shared Roslyn layer keeps language identity explicit so C# and Visual Basic extractors can project into the same graph vocabulary without relying on project names or file extensions alone.
    /// </remarks>
    public enum SourceLanguage
    {
        /// <summary>
        /// Represents C# source code parsed and bound by Roslyn.
        /// </summary>
        CSharp,

        /// <summary>
        /// Represents Visual Basic .NET source code parsed and bound by Roslyn.
        /// </summary>
        VisualBasic
    }
}
