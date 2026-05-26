namespace Archon.Application.Dashboard
{
    /// <summary>
    /// Describes the repository and optional solution scope applied to a dashboard summary response.
    /// </summary>
    public sealed class DashboardScopeDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DashboardScopeDto"/> class.
        /// </summary>
        /// <param name="repositoryStableKey">The stable key of the repository scope.</param>
        /// <param name="repositoryName">The display name of the repository scope when known.</param>
        /// <param name="solutionStableKey">The stable key of the solution scope when one was selected.</param>
        /// <param name="solutionName">The display name of the solution scope when one was selected and known.</param>
        public DashboardScopeDto(string? repositoryStableKey, string? repositoryName, string? solutionStableKey, string? solutionName)
        {
            // Scope metadata makes the applied repository and solution boundary explicit in every successful response envelope.
            RepositoryStableKey = repositoryStableKey ?? string.Empty;
            RepositoryName = repositoryName;
            SolutionStableKey = solutionStableKey;
            SolutionName = solutionName;
        }

        /// <summary>
        /// Gets the stable key of the repository scope.
        /// </summary>
        public string RepositoryStableKey { get; }

        /// <summary>
        /// Gets the display name of the repository scope when known.
        /// </summary>
        public string? RepositoryName { get; }

        /// <summary>
        /// Gets the stable key of the solution scope when one was selected.
        /// </summary>
        public string? SolutionStableKey { get; }

        /// <summary>
        /// Gets the display name of the solution scope when one was selected and known.
        /// </summary>
        public string? SolutionName { get; }
    }
}