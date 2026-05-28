using Archon.Domain.Graph.Identity;

namespace Archon.Extractors.Razor
{
    /// <summary>
    /// Describes the repository-scoped input for the static UI extraction Razor Pages and MVC Razor extraction slice.
    /// </summary>
    public sealed record RazorPageViewExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RazorPageViewExtractionRequest" /> record.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that will receive extracted Razor Pages and MVC Razor facts.</param>
        /// <param name="repositoryRootDirectory">The accepted repository root directory that should be searched for Razor artifacts.</param>
        public RazorPageViewExtractionRequest(StableKey snapshotStableKey, string repositoryRootDirectory)
        {
            // The extractor needs a concrete repository root because discovery and graph output must normalize every path relative to that boundary.
            if (string.IsNullOrWhiteSpace(repositoryRootDirectory))
            {
                throw new ArgumentException("Razor extraction requires a repository root directory.", nameof(repositoryRootDirectory));
            }

            SnapshotStableKey = snapshotStableKey;
            RepositoryRootDirectory = Path.GetFullPath(repositoryRootDirectory.Trim());
        }

        /// <summary>
        /// Gets the stable key of the snapshot that will receive extracted Razor Pages and MVC Razor facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the accepted repository root directory that should be searched for Razor artifacts.
        /// </summary>
        public string RepositoryRootDirectory { get; }
    }
}