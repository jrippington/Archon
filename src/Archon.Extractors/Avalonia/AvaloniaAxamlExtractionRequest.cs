using Archon.Domain.Graph.Identity;

namespace Archon.Extractors.Avalonia
{
    /// <summary>
    /// Describes the repository-scoped input for the static UI extraction Avalonia AXAML extraction slice.
    /// </summary>
    public sealed record AvaloniaAxamlExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AvaloniaAxamlExtractionRequest" /> record.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that will receive extracted Avalonia facts.</param>
        /// <param name="repositoryRootDirectory">The accepted repository root directory that should be searched for Avalonia projects, AXAML artifacts, source files, view locators, and ReactiveUI evidence.</param>
        public AvaloniaAxamlExtractionRequest(StableKey snapshotStableKey, string repositoryRootDirectory)
        {
            // The extractor requires a concrete repository root because stable keys and evidence paths must stay repository-relative and machine-independent.
            if (string.IsNullOrWhiteSpace(repositoryRootDirectory))
            {
                throw new ArgumentException("Avalonia extraction requires a repository root directory.", nameof(repositoryRootDirectory));
            }

            SnapshotStableKey = snapshotStableKey;
            RepositoryRootDirectory = Path.GetFullPath(repositoryRootDirectory.Trim());
        }

        /// <summary>
        /// Gets the stable key of the snapshot that will receive extracted Avalonia facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the accepted repository root directory that should be searched for Avalonia artifacts.
        /// </summary>
        public string RepositoryRootDirectory { get; }
    }
}
