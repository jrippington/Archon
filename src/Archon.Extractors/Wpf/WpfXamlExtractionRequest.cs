using Archon.Domain.Graph.Identity;

namespace Archon.Extractors.Wpf
{
    /// <summary>
    /// Describes the repository-scoped input for the static UI extraction WPF XAML extraction slice.
    /// </summary>
    public sealed record WpfXamlExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WpfXamlExtractionRequest" /> record.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that will receive extracted WPF facts.</param>
        /// <param name="repositoryRootDirectory">The accepted repository root directory that should be searched for WPF projects and artifacts.</param>
        public WpfXamlExtractionRequest(StableKey snapshotStableKey, string repositoryRootDirectory)
        {
            // The extractor needs a concrete repository root because stable keys and evidence paths must remain repository-relative and machine-independent.
            if (string.IsNullOrWhiteSpace(repositoryRootDirectory))
            {
                throw new ArgumentException("WPF extraction requires a repository root directory.", nameof(repositoryRootDirectory));
            }

            SnapshotStableKey = snapshotStableKey;
            RepositoryRootDirectory = Path.GetFullPath(repositoryRootDirectory.Trim());
        }

        /// <summary>
        /// Gets the stable key of the snapshot that will receive extracted WPF facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the accepted repository root directory that should be searched for WPF artifacts.
        /// </summary>
        public string RepositoryRootDirectory { get; }
    }
}
