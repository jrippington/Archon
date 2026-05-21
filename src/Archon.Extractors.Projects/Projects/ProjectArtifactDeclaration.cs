using Archon.Domain.Graph.Metadata;

namespace Archon.Extractors.Projects.Projects
{
    /// <summary>
    /// Represents a repository-contained source artifact that supports project extraction facts and can be represented as a FilePath node.
    /// </summary>
    /// <param name="RelativePath">The repository-relative artifact path normalized with forward slash separators.</param>
    /// <param name="ArtifactKind">The concise artifact kind, such as solution, project, analyzer, package config, central packages, or imported build file.</param>
    /// <param name="DeclaringProjectRelativePath">The repository-relative project path that introduced the artifact when applicable.</param>
    internal sealed record ProjectArtifactDeclaration(
        string RelativePath,
        string ArtifactKind,
        string? DeclaringProjectRelativePath)
    {
        /// <summary>
        /// Converts this artifact into deterministic FilePath node metadata.
        /// </summary>
        /// <returns>Graph metadata describing artifact path, kind, and owning project context.</returns>
        internal GraphMetadata ToGraphMetadata()
        {
            // FilePath nodes carry only path-oriented artifact metadata and never contain file contents.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["filePath.relativePath"] = RelativePath,
                ["filePath.artifactKind"] = ArtifactKind
            };

            if (!string.IsNullOrWhiteSpace(DeclaringProjectRelativePath))
            {
                values["filePath.declaringProject"] = DeclaringProjectRelativePath.Trim();
            }

            return GraphMetadata.From(values);
        }
    }
}
