namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Provides the provider-neutral caller context used by MCP authorization and audit logging.
    /// </summary>
    public interface IArchonMcpCallerContextProvider
    {
        /// <summary>
        /// Gets the current MCP caller context without exposing provider-specific tokens or credentials.
        /// </summary>
        /// <returns>The current caller context.</returns>
        ArchonMcpCallerContext GetCurrentCaller();
    }
}
