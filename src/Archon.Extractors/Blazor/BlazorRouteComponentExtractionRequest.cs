using Archon.Domain.Graph.Identity;

namespace Archon.Extractors.Blazor
{
    /// <summary>
    /// Describes the repository-scoped input for the static UI extraction Blazor route and component extraction slice.
    /// </summary>
    public sealed record BlazorRouteComponentExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BlazorRouteComponentExtractionRequest" /> record.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that will receive extracted Blazor facts.</param>
        /// <param name="repositoryRootDirectory">The accepted repository root directory that should be searched for Blazor artifacts.</param>
        public BlazorRouteComponentExtractionRequest(StableKey snapshotStableKey, string repositoryRootDirectory)
        {
            // The extractor needs a real repository root because discovery must avoid machine-specific paths in graph output.
            if (string.IsNullOrWhiteSpace(repositoryRootDirectory))
            {
                throw new ArgumentException("Blazor extraction requires a repository root directory.", nameof(repositoryRootDirectory));
            }

            SnapshotStableKey = snapshotStableKey;
            RepositoryRootDirectory = Path.GetFullPath(repositoryRootDirectory.Trim());
        }

        /// <summary>
        /// Gets the stable key of the snapshot that will receive extracted Blazor facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the accepted repository root directory that should be searched for Blazor artifacts.
        /// </summary>
        public string RepositoryRootDirectory { get; }
    }
}