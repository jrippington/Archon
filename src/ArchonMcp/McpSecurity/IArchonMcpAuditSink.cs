namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Receives sanitized MCP audit events for logging or test inspection.
    /// </summary>
    public interface IArchonMcpAuditSink
    {
        /// <summary>
        /// Records a sanitized MCP audit event.
        /// </summary>
        /// <param name="auditEvent">The audit event that contains only safe normalized metadata.</param>
        void Record(ArchonMcpAuditEvent auditEvent);
    }
}
