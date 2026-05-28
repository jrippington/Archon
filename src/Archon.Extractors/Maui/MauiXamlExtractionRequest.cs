using Archon.Domain.Graph.Identity;

namespace Archon.Extractors.Maui
{
    /// <summary>
    /// Describes the repository-scoped input for the static UI extraction .NET MAUI XAML extraction slice.
    /// </summary>
    public sealed record MauiXamlExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MauiXamlExtractionRequest" /> record.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that will receive extracted MAUI facts.</param>
        /// <param name="repositoryRootDirectory">The accepted repository root directory that should be searched for MAUI projects, XAML artifacts, source files, and platform heads.</param>
        public MauiXamlExtractionRequest(StableKey snapshotStableKey, string repositoryRootDirectory)
        {
            // The extractor requires a concrete repository root because stable keys and evidence paths must stay repository-relative and machine-independent.
            if (string.IsNullOrWhiteSpace(repositoryRootDirectory))
            {
                throw new ArgumentException("MAUI extraction requires a repository root directory.", nameof(repositoryRootDirectory));
            }

            SnapshotStableKey = snapshotStableKey;
            RepositoryRootDirectory = Path.GetFullPath(repositoryRootDirectory.Trim());
        }

        /// <summary>
        /// Gets the stable key of the snapshot that will receive extracted MAUI facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the accepted repository root directory that should be searched for MAUI artifacts.
        /// </summary>
        public string RepositoryRootDirectory { get; }
    }
}
