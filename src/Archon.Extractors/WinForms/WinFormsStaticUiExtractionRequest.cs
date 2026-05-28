using Archon.Domain.Graph.Identity;

namespace Archon.Extractors.WinForms
{
    /// <summary>
    /// Describes the repository-scoped input for the static UI extraction Windows Forms static UI extraction slice.
    /// </summary>
    public sealed record WinFormsStaticUiExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WinFormsStaticUiExtractionRequest" /> record.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that will receive extracted Windows Forms facts.</param>
        /// <param name="repositoryRootDirectory">The accepted repository root directory that should be searched for Windows Forms projects and artifacts.</param>
        public WinFormsStaticUiExtractionRequest(StableKey snapshotStableKey, string repositoryRootDirectory)
        {
            // The extractor needs a real repository root because stable keys and evidence paths must stay repository-relative and machine-independent.
            if (string.IsNullOrWhiteSpace(repositoryRootDirectory))
            {
                throw new ArgumentException("Windows Forms extraction requires a repository root directory.", nameof(repositoryRootDirectory));
            }

            SnapshotStableKey = snapshotStableKey;
            RepositoryRootDirectory = Path.GetFullPath(repositoryRootDirectory.Trim());
        }

        /// <summary>
        /// Gets the stable key of the snapshot that will receive extracted Windows Forms facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the accepted repository root directory that should be searched for Windows Forms artifacts.
        /// </summary>
        public string RepositoryRootDirectory { get; }
    }
}
