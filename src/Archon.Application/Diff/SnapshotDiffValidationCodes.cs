namespace Archon.Application.Diff
{
    /// <summary>
    /// Defines deterministic validation codes returned by snapshot diff requests.
    /// </summary>
    public static class SnapshotDiffValidationCodes
    {
        /// <summary>
        /// Indicates that the current snapshot stable key was missing from the request.
        /// </summary>
        public const string CurrentSnapshotRequired = "CurrentSnapshotRequired";

        /// <summary>
        /// Indicates that the previous snapshot stable key was missing from the request.
        /// </summary>
        public const string PreviousSnapshotRequired = "PreviousSnapshotRequired";

        /// <summary>
        /// Indicates that the current snapshot was not found in the comparison source.
        /// </summary>
        public const string CurrentSnapshotNotFound = "CurrentSnapshotNotFound";

        /// <summary>
        /// Indicates that the previous snapshot was not found in the comparison source.
        /// </summary>
        public const string PreviousSnapshotNotFound = "PreviousSnapshotNotFound";

        /// <summary>
        /// Indicates that the current and previous snapshots belong to incompatible repositories.
        /// </summary>
        public const string IncompatibleRepository = "IncompatibleRepository";

        /// <summary>
        /// Indicates that the request included an unsupported diff domain filter.
        /// </summary>
        public const string UnsupportedDomain = "UnsupportedDomain";

        /// <summary>
        /// Indicates that the request included an unsupported change-kind filter.
        /// </summary>
        public const string UnsupportedChangeKind = "UnsupportedChangeKind";

        /// <summary>
        /// Indicates that the request included an invalid skip value.
        /// </summary>
        public const string SkipInvalid = "SkipInvalid";

        /// <summary>
        /// Indicates that the request included an invalid take value.
        /// </summary>
        public const string TakeInvalid = "TakeInvalid";

        /// <summary>
        /// Indicates that a repository stable key was not supplied for latest-to-previous comparison.
        /// </summary>
        public const string RepositoryStableKeyRequired = "RepositoryStableKeyRequired";

        /// <summary>
        /// Indicates that the requested repository scope was not found.
        /// </summary>
        public const string RepositoryNotFound = "RepositoryNotFound";

        /// <summary>
        /// Indicates that the requested solution scope was not found within the repository scope.
        /// </summary>
        public const string SolutionNotFound = "SolutionNotFound";

        /// <summary>
        /// Indicates that the requested repository or solution scope does not contain two comparable snapshots.
        /// </summary>
        public const string PreviousComparableSnapshotNotFound = "PreviousComparableSnapshotNotFound";
    }
}
