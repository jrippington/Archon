namespace Archon.Application.Dashboard
{
    /// <summary>
    /// Represents the caller-supplied scope used to resolve the dashboard summary snapshot.
    /// </summary>
    public sealed class DashboardSnapshotSelector
    {
        /// <summary>
        /// Defines the selector value that asks Archon to resolve the latest available snapshot deterministically.
        /// </summary>
        public const string LatestSnapshotSelector = "latest";

        /// <summary>
        /// Initializes a new instance of the <see cref="DashboardSnapshotSelector"/> class.
        /// </summary>
        /// <param name="repositoryStableKey">The stable key of the repository scope requested by the caller.</param>
        /// <param name="solutionStableKey">The optional stable key of the solution scope requested by the caller.</param>
        /// <param name="snapshotStableKey">The optional snapshot stable key or latest selector requested by the caller.</param>
        public DashboardSnapshotSelector(string? repositoryStableKey, string? solutionStableKey, string? snapshotStableKey)
        {
            // The selector preserves raw optional values so validation can report all request problems together in a deterministic result.
            RepositoryStableKey = Normalize(repositoryStableKey);
            SolutionStableKey = Normalize(solutionStableKey);
            SnapshotStableKey = Normalize(snapshotStableKey);
        }

        /// <summary>
        /// Gets the stable key of the repository scope requested by the caller.
        /// </summary>
        public string? RepositoryStableKey { get; }

        /// <summary>
        /// Gets the optional stable key of the solution scope requested by the caller.
        /// </summary>
        public string? SolutionStableKey { get; }

        /// <summary>
        /// Gets the optional snapshot stable key or latest selector requested by the caller.
        /// </summary>
        public string? SnapshotStableKey { get; }

        /// <summary>
        /// Gets a value indicating whether the selector asks for deterministic latest snapshot resolution.
        /// </summary>
        public bool RequestsLatestSnapshot
        {
            get
            {
                // A missing snapshot selector and the explicit latest token both use the latest matching snapshot for the requested scope.
                return SnapshotStableKey is null || StringComparer.OrdinalIgnoreCase.Equals(SnapshotStableKey, LatestSnapshotSelector);
            }
        }

        /// <summary>
        /// Normalizes one optional selector value into a trimmed string or null.
        /// </summary>
        /// <param name="value">The raw query-string value supplied for one selector field.</param>
        /// <returns>A trimmed selector value, or null when the input is empty.</returns>
        private static string? Normalize(string? value)
        {
            // Empty query-string values are treated the same as absent values so validation can remain field-oriented.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}