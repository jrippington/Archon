namespace Archon.Application.Dashboard
{
    /// <summary>
    /// Defines stable machine-readable validation codes for dashboard summary requests.
    /// </summary>
    public static class DashboardSummaryValidationCodes
    {
        /// <summary>
        /// Identifies a request that omitted the required repository scope.
        /// </summary>
        public const string RepositoryStableKeyRequired = "RepositoryStableKeyRequired";

        /// <summary>
        /// Identifies a request whose explicit snapshot selector is not a stable snapshot key or the latest selector.
        /// </summary>
        public const string SnapshotSelectorInvalid = "SnapshotSelectorInvalid";

        /// <summary>
        /// Identifies a request whose repository scope could not be matched to any persisted snapshot.
        /// </summary>
        public const string RepositoryNotFound = "RepositoryNotFound";

        /// <summary>
        /// Identifies a request whose solution scope could not be matched within the selected repository.
        /// </summary>
        public const string SolutionNotFound = "SolutionNotFound";

        /// <summary>
        /// Identifies a request whose explicit snapshot selector could not be matched within the selected scope.
        /// </summary>
        public const string SnapshotNotFound = "SnapshotNotFound";
    }
}