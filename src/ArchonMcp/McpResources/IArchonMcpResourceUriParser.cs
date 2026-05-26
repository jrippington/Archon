namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Parses and validates Archon MCP resource URI text before query execution.
    /// </summary>
    public interface IArchonMcpResourceUriParser
    {
        /// <summary>
        /// Parses the supplied resource URI into a validated request or safe MCP error.
        /// </summary>
        /// <param name="uri">The resource URI text supplied by the MCP client.</param>
        /// <returns>A parse result that either contains a request or a structured MCP error.</returns>
        ArchonMcpResourceParseResult Parse(string? uri);
    }
}
