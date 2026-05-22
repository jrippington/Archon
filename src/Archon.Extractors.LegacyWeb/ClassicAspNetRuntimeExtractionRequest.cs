using Archon.Domain.Graph.Identity;

namespace Archon.Extractors.LegacyWeb
{
    /// <summary>
    /// Carries the repository, project, and snapshot scope required by the classic ASP.NET runtime extractor.
    /// </summary>
    /// <remarks>
    /// The request intentionally uses repository-contained artifact paths rather than a running application host. Classic ASP.NET extraction must read project XML, configuration files, markup files, and source files as static evidence only.
    /// </remarks>
    public sealed class ClassicAspNetRuntimeExtractionRequest
    {
        /// <summary>
        /// Initializes a request after validating the snapshot and repository-contained project context.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted classic runtime graph facts.</param>
        /// <param name="repositoryRootDirectory">The absolute repository root used to derive repository-relative evidence paths.</param>
        /// <param name="projectPath">The repository-relative or absolute path to the classic ASP.NET project file.</param>
        public ClassicAspNetRuntimeExtractionRequest(StableKey snapshotStableKey, string repositoryRootDirectory, string projectPath)
        {
            // The extractor needs all three values to keep facts scoped, deterministic, and tied to repository evidence.
            SnapshotStableKey = snapshotStableKey;
            RepositoryRootDirectory = RequireText(repositoryRootDirectory, nameof(repositoryRootDirectory));
            ProjectPath = RequireText(projectPath, nameof(projectPath));
        }

        /// <summary>
        /// Gets the stable key of the snapshot that receives extracted classic runtime graph facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the absolute repository root used to derive repository-relative evidence paths.
        /// </summary>
        public string RepositoryRootDirectory { get; }

        /// <summary>
        /// Gets the repository-relative or absolute path to the classic ASP.NET project file.
        /// </summary>
        public string ProjectPath { get; }

        /// <summary>
        /// Requires non-empty request text before extraction begins.
        /// </summary>
        /// <param name="value">The request text supplied by infrastructure or tests.</param>
        /// <param name="parameterName">The parameter name used for validation failures.</param>
        /// <returns>The trimmed request text.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Repository and project paths become stable-key and evidence inputs, so blanks are rejected at the boundary.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Classic ASP.NET extraction request values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }
    }
}
