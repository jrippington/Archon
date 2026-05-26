namespace ArchonMcp.McpDependencies
{
    /// <summary>
    /// Provides stable MCP operation names for read-only dependency traversal tools.
    /// </summary>
    public static class ArchonMcpDependencyOperations
    {
        /// <summary>
        /// Identifies the outgoing dependency traversal tool in catalog, authorization, audit, and response envelopes.
        /// </summary>
        public const string GetDependencies = "archon.get_dependencies";

        /// <summary>
        /// Identifies the incoming dependent traversal tool in catalog, authorization, audit, and response envelopes.
        /// </summary>
        public const string GetDependents = "archon.get_dependents";
    }
}
