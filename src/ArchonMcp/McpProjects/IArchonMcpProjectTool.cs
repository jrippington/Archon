namespace ArchonMcp.McpProjects
{
    /// <summary>
    /// Defines the read-only MCP tool contract for describing one project through approved query-layer abstractions.
    /// </summary>
    public interface IArchonMcpProjectTool
    {
        /// <summary>
        /// Describes one project by stable key or unambiguous display name.
        /// </summary>
        /// <param name="request">The project description request containing scope and project identity.</param>
        /// <param name="cancellationToken">The token that can cancel query-layer work before response mapping completes.</param>
        /// <returns>A project description envelope or a structured MCP error response.</returns>
        Task<object> DescribeProjectAsync(ArchonMcpDescribeProjectRequest request, CancellationToken cancellationToken);
    }
}
