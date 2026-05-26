namespace Archon.Application.Projects
{
    /// <summary>
    /// Represents controlled lookup input for one project detail query.
    /// </summary>
    public sealed class ProjectDetailQuery
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectDetailQuery"/> class.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable key that bounds snapshot resolution.</param>
        /// <param name="solutionStableKey">The optional solution stable key that narrows the project scope.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="projectStableKey">The optional exact project stable key.</param>
        /// <param name="projectName">The optional project display-name lookup value.</param>
        public ProjectDetailQuery(string? repositoryStableKey, string? solutionStableKey, string? snapshotStableKey, string? projectStableKey, string? projectName)
        {
            // Detail lookup accepts either stable key or display name; validation later rejects missing or conflicting identity input.
            Selector = new ProjectSnapshotSelector(repositoryStableKey, solutionStableKey, snapshotStableKey);
            ProjectStableKey = NormalizeOptional(projectStableKey);
            ProjectName = NormalizeOptional(projectName);
        }

        /// <summary>
        /// Gets the repository, solution, and snapshot selector for the detail query.
        /// </summary>
        public ProjectSnapshotSelector Selector { get; }

        /// <summary>
        /// Gets the optional exact project stable key.
        /// </summary>
        public string? ProjectStableKey { get; }

        /// <summary>
        /// Gets the optional project display-name lookup value.
        /// </summary>
        public string? ProjectName { get; }

        /// <summary>
        /// Normalizes optional project lookup values from route or query-string input.
        /// </summary>
        /// <param name="value">The optional caller-supplied project identity value.</param>
        /// <returns>The trimmed identity value, or <see langword="null"/> when no meaningful value was supplied.</returns>
        private static string? NormalizeOptional(string? value)
        {
            // Treating whitespace as omitted lets the service produce the same validation code for all missing identity cases.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
