using Archon.Domain.Graph.Identity;

namespace Archon.Extractors.WinUI
{
    /// <summary>
    /// Describes the repository-scoped input for the WP011 WinUI XAML and packaging extraction slice.
    /// </summary>
    public sealed record WinUiXamlExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WinUiXamlExtractionRequest" /> record.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that will receive extracted WinUI facts.</param>
        /// <param name="repositoryRootDirectory">The accepted repository root directory that should be searched for WinUI projects, XAML artifacts, source files, and packaging metadata.</param>
        public WinUiXamlExtractionRequest(StableKey snapshotStableKey, string repositoryRootDirectory)
        {
            // The extractor requires a concrete repository root because stable keys and evidence paths must stay repository-relative and machine-independent.
            if (string.IsNullOrWhiteSpace(repositoryRootDirectory))
            {
                throw new ArgumentException("WinUI extraction requires a repository root directory.", nameof(repositoryRootDirectory));
            }

            SnapshotStableKey = snapshotStableKey;
            RepositoryRootDirectory = Path.GetFullPath(repositoryRootDirectory.Trim());
        }

        /// <summary>
        /// Gets the stable key of the snapshot that will receive extracted WinUI facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the accepted repository root directory that should be searched for WinUI artifacts.
        /// </summary>
        public string RepositoryRootDirectory { get; }
    }
}
