namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Authorizes MCP operations before handlers invoke application/query dependencies.
    /// </summary>
    public interface IArchonMcpOperationAuthorizer
    {
        /// <summary>
        /// Authorizes a requested operation for the supplied caller context.
        /// </summary>
        /// <param name="operationName">The stable MCP operation name being requested.</param>
        /// <param name="callerContext">The provider-neutral caller identity available for the request.</param>
        /// <returns>The authorization decision that determines whether execution may continue.</returns>
        ArchonMcpAuthorizationDecision Authorize(string operationName, ArchonMcpCallerContext callerContext);
    }
}
