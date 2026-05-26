namespace Archon.Api.Query.Contracts
{
    /// <summary>
    /// Describes the stable scope that was applied to a query API response.
    /// </summary>
    public sealed record QueryScopeMetadataResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QueryScopeMetadataResponse"/> record.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable key applied to the query.</param>
        /// <param name="repositoryName">The optional repository display name resolved for the query.</param>
        /// <param name="solutionStableKey">The optional solution stable key applied to the query.</param>
        /// <param name="solutionName">The optional solution display name resolved for the query.</param>
        public QueryScopeMetadataResponse(string repositoryStableKey, string? repositoryName, string? solutionStableKey, string? solutionName)
        {
            // Scope metadata uses stable identities rather than names as the durable contract with consumers.
            RepositoryStableKey = repositoryStableKey;
            RepositoryName = repositoryName;
            SolutionStableKey = solutionStableKey;
            SolutionName = solutionName;
        }

        /// <summary>
        /// Gets the repository stable key applied to the query.
        /// </summary>
        public string RepositoryStableKey { get; init; }

        /// <summary>
        /// Gets the optional repository display name resolved for the query.
        /// </summary>
        public string? RepositoryName { get; init; }

        /// <summary>
        /// Gets the optional solution stable key applied to the query.
        /// </summary>
        public string? SolutionStableKey { get; init; }

        /// <summary>
        /// Gets the optional solution display name resolved for the query.
        /// </summary>
        public string? SolutionName { get; init; }
    }
}