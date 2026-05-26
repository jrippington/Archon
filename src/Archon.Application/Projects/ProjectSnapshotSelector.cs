namespace Archon.Application.Projects
{
    /// <summary>
    /// Represents the repository, solution, and snapshot scope used by controlled project queries.
    /// </summary>
    public sealed class ProjectSnapshotSelector
    {
        /// <summary>
        /// Defines the public selector value that requests deterministic latest snapshot resolution.
        /// </summary>
        public const string LatestSnapshotSelector = "latest";

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectSnapshotSelector"/> class.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable key that bounds snapshot resolution.</param>
        /// <param name="solutionStableKey">The optional solution stable key that narrows the project scope.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        public ProjectSnapshotSelector(string? repositoryStableKey, string? solutionStableKey, string? snapshotStableKey)
        {
            // Selector values are normalized once at the boundary so query services can compare stable identities deterministically.
            RepositoryStableKey = NormalizeOptional(repositoryStableKey);
            SolutionStableKey = NormalizeOptional(solutionStableKey);
            SnapshotStableKey = NormalizeOptional(snapshotStableKey) ?? LatestSnapshotSelector;
        }

        /// <summary>
        /// Gets the repository stable key that bounds snapshot resolution.
        /// </summary>
        public string? RepositoryStableKey { get; }

        /// <summary>
        /// Gets the optional solution stable key that narrows the project scope.
        /// </summary>
        public string? SolutionStableKey { get; }

        /// <summary>
        /// Gets the exact snapshot stable key or latest selector requested by the caller.
        /// </summary>
        public string SnapshotStableKey { get; }

        /// <summary>
        /// Gets a value indicating whether the caller requested latest snapshot resolution.
        /// </summary>
        public bool RequestsLatestSnapshot
        {
            get
            {
                // Latest and current are accepted aliases so API consumers can use natural snapshot-selection language.
                return string.Equals(SnapshotStableKey, LatestSnapshotSelector, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(SnapshotStableKey, "current", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Normalizes optional selector values from HTTP query-string input.
        /// </summary>
        /// <param name="value">The optional caller-supplied selector value.</param>
        /// <returns>The trimmed selector value, or <see langword="null"/> when no meaningful value was supplied.</returns>
        private static string? NormalizeOptional(string? value)
        {
            // Whitespace-only values are treated as omitted so validation can produce field-specific errors later.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
