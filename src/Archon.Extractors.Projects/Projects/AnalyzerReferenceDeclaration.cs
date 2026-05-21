using Archon.Domain.Graph.Metadata;

namespace Archon.Extractors.Projects.Projects
{
    /// <summary>
    /// Represents one static `Analyzer` item declaration discovered in a supported project file.
    /// </summary>
    /// <param name="DeclaringProjectRelativePath">The repository-relative project path that declares the analyzer item.</param>
    /// <param name="DeclaredInclude">The raw analyzer include value declared in project XML.</param>
    /// <param name="ResolvedRelativePath">The repository-relative analyzer path when it resolves inside the submitted repository.</param>
    /// <param name="IsRepositoryContained">A value indicating whether the analyzer path resolves inside the submitted repository.</param>
    /// <param name="LineNumber">The XML source line for the analyzer item when available.</param>
    /// <param name="SnippetHash">The deterministic hash of the analyzer XML snippet when available.</param>
    /// <param name="SnippetPreview">The concise analyzer XML snippet preview when available.</param>
    internal sealed record AnalyzerReferenceDeclaration(
        string DeclaringProjectRelativePath,
        string DeclaredInclude,
        string? ResolvedRelativePath,
        bool IsRepositoryContained,
        int? LineNumber,
        string? SnippetHash,
        string? SnippetPreview)
    {
        /// <summary>
        /// Converts this analyzer declaration into deterministic project metadata and evidence metadata.
        /// </summary>
        /// <returns>Graph metadata describing analyzer declaration identity, resolution state, and source path.</returns>
        internal GraphMetadata ToGraphMetadata()
        {
            // Analyzer metadata keeps raw and resolved paths visible while avoiding absolute machine-specific paths.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["analyzer.declaringProject"] = DeclaringProjectRelativePath,
                ["analyzer.declaredInclude"] = DeclaredInclude,
                ["analyzer.isRepositoryContained"] = IsRepositoryContained
            };

            if (!string.IsNullOrWhiteSpace(ResolvedRelativePath))
            {
                values["analyzer.resolvedRelativePath"] = ResolvedRelativePath.Trim();
            }

            if (LineNumber.HasValue)
            {
                values["analyzer.lineNumber"] = LineNumber.Value;
            }

            return GraphMetadata.From(values);
        }
    }
}
