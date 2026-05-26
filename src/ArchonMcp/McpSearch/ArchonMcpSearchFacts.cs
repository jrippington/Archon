namespace ArchonMcp.McpSearch
{
    /// <summary>
    /// Represents the structured facts section returned by the <c>archon.search</c> MCP tool.
    /// </summary>
    public sealed record ArchonMcpSearchFacts
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpSearchFacts" /> record.
        /// </summary>
        /// <param name="queryText">The normalized search text used for the query.</param>
        /// <param name="repositoryStableKey">The repository stable key that bounded the query when available.</param>
        /// <param name="solutionStableKey">The solution stable key that narrowed the query when available.</param>
        /// <param name="projectStableKey">The project stable key that narrowed the query when supplied.</param>
        /// <param name="totalMatches">The total number of matches reported by the query layer before MCP result limiting.</param>
        /// <param name="returnedMatches">The number of matches returned in this MCP response.</param>
        /// <param name="dataAvailable">Indicates whether search data was available from the query layer.</param>
        /// <param name="groups">The deterministic result groups included in the response.</param>
        public ArchonMcpSearchFacts(
            string queryText,
            string? repositoryStableKey,
            string? solutionStableKey,
            string? projectStableKey,
            int totalMatches,
            int returnedMatches,
            bool dataAvailable,
            IEnumerable<ArchonMcpSearchResultGroup>? groups)
        {
            // Search facts preserve both scope metadata and grouped rows so callers can distinguish empty data from unavailable data.
            QueryText = queryText;
            RepositoryStableKey = repositoryStableKey;
            SolutionStableKey = solutionStableKey;
            ProjectStableKey = projectStableKey;
            TotalMatches = Math.Max(0, totalMatches);
            ReturnedMatches = Math.Max(0, returnedMatches);
            DataAvailable = dataAvailable;
            Groups = groups?.ToArray() ?? [];
        }

        /// <summary>
        /// Gets the normalized search text used for the query.
        /// </summary>
        public string QueryText { get; init; }

        /// <summary>
        /// Gets the repository stable key that bounded the query when available.
        /// </summary>
        public string? RepositoryStableKey { get; init; }

        /// <summary>
        /// Gets the solution stable key that narrowed the query when available.
        /// </summary>
        public string? SolutionStableKey { get; init; }

        /// <summary>
        /// Gets the project stable key that narrowed the query when supplied.
        /// </summary>
        public string? ProjectStableKey { get; init; }

        /// <summary>
        /// Gets the total number of matches reported by the query layer before MCP result limiting.
        /// </summary>
        public int TotalMatches { get; init; }

        /// <summary>
        /// Gets the number of matches returned in this MCP response.
        /// </summary>
        public int ReturnedMatches { get; init; }

        /// <summary>
        /// Gets a value indicating whether search data was available from the query layer.
        /// </summary>
        public bool DataAvailable { get; init; }

        /// <summary>
        /// Gets the deterministic result groups included in the response.
        /// </summary>
        public IReadOnlyList<ArchonMcpSearchResultGroup> Groups { get; init; }
    }
}
