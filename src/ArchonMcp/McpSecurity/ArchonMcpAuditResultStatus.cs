namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Classifies the final status of an audited MCP operation.
    /// </summary>
    public enum ArchonMcpAuditResultStatus
    {
        /// <summary>
        /// Indicates the operation completed successfully.
        /// </summary>
        Succeeded,

        /// <summary>
        /// Indicates the operation was denied before handler or query-layer execution.
        /// </summary>
        Denied,

        /// <summary>
        /// Indicates the operation failed after execution began and was mapped to a safe error.
        /// </summary>
        Failed
    }
}
