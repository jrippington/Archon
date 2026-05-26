namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Enumerates possible outcomes of current snapshot resolution for MCP resources.
    /// </summary>
    public enum ArchonMcpCurrentSnapshotResolutionKind
    {
        /// <summary>
        /// Indicates a single current snapshot was selected.
        /// </summary>
        Success,

        /// <summary>
        /// Indicates no snapshot matched the requested repository or solution scope.
        /// </summary>
        NotFound,

        /// <summary>
        /// Indicates multiple snapshots tied for current selection and the caller must disambiguate later.
        /// </summary>
        Ambiguous
    }
}
