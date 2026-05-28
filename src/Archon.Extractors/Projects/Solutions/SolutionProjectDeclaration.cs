using Archon.Domain.Graph.Metadata;

namespace Archon.Extractors.Projects.Solutions
{
    /// <summary>
    /// Represents one project declaration discovered inside a submitted Visual Studio solution file.
    /// </summary>
    /// <param name="Name">The project display name declared in the solution file.</param>
    /// <param name="DeclaredPath">The project path text declared in the solution file.</param>
    /// <param name="ProjectTypeGuid">The solution project-type GUID text when present.</param>
    /// <param name="ProjectGuid">The project GUID text when present.</param>
    /// <param name="LineNumber">The one-based line number containing the declaration.</param>
    internal sealed record SolutionProjectDeclaration(
        string Name,
        string DeclaredPath,
        string? ProjectTypeGuid,
        string? ProjectGuid,
        int LineNumber)
    {
        /// <summary>
        /// Converts the project declaration into deterministic metadata suitable for evidence records.
        /// </summary>
        /// <returns>Canonical graph metadata that describes the visible solution-file project declaration without storing full file content.</returns>
        internal GraphMetadata ToMetadata()
        {
            // Evidence metadata preserves concise project declaration fields so later troubleshooting can identify which solution line supported a fact.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["solutionProject.name"] = Name,
                ["solutionProject.declaredPath"] = DeclaredPath
            };

            if (!string.IsNullOrWhiteSpace(ProjectTypeGuid))
            {
                values["solutionProject.projectTypeGuid"] = ProjectTypeGuid;
            }

            if (!string.IsNullOrWhiteSpace(ProjectGuid))
            {
                values["solutionProject.projectGuid"] = ProjectGuid;
            }

            return GraphMetadata.From(values);
        }
    }
}
