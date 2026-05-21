using Archon.Domain.Graph.Metadata;

namespace Archon.Extractors.Projects.Projects
{
    /// <summary>
    /// Represents one `ProjectReference` item discovered in a supported project file.
    /// </summary>
    /// <param name="DeclaringProjectRelativePath">The repository-relative path of the project file that declared the reference.</param>
    /// <param name="DeclaredInclude">The raw `Include` attribute text as declared in the project file.</param>
    /// <param name="ResolvedRelativePath">The repository-relative referenced project path when it can be resolved inside the repository.</param>
    /// <param name="IsRepositoryContained">A value indicating whether the normalized referenced path remains inside the submitted repository root.</param>
    /// <param name="LineNumber">The line number of the `ProjectReference` element when XML line information is available.</param>
    internal sealed record ProjectReferenceDeclaration(
        string DeclaringProjectRelativePath,
        string DeclaredInclude,
        string? ResolvedRelativePath,
        bool IsRepositoryContained,
        int? LineNumber)
    {
        /// <summary>
        /// Converts the declaration into deterministic evidence metadata for reference edges or unresolved-reference warnings.
        /// </summary>
        /// <returns>Graph metadata containing the raw declaration and normalized resolution state.</returns>
        internal GraphMetadata ToGraphMetadata()
        {
            // Reference evidence preserves the raw declaration for troubleshooting while keeping graph identity based on normalized paths.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["projectReference.declaringProjectPath"] = DeclaringProjectRelativePath,
                ["projectReference.declaredInclude"] = DeclaredInclude,
                ["projectReference.isRepositoryContained"] = IsRepositoryContained
            };

            AddOptional(values, "projectReference.resolvedRelativePath", ResolvedRelativePath);

            if (LineNumber.HasValue)
            {
                values["projectReference.lineNumber"] = LineNumber.Value;
            }

            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Adds optional text metadata after trimming insignificant whitespace.
        /// </summary>
        /// <param name="values">The metadata dictionary being assembled.</param>
        /// <param name="key">The metadata key to populate.</param>
        /// <param name="value">The optional text value to add.</param>
        private static void AddOptional(Dictionary<string, object?> values, string key, string? value)
        {
            // Optional reference values are omitted when blank so unresolved state remains explicit through the repository-contained flag.
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value.Trim();
            }
        }
    }
}
