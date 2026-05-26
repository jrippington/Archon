namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Classifies structured MCP failures into stable categories for clients and audit logging.
    /// </summary>
    public enum ArchonMcpErrorCategory
    {
        /// <summary>
        /// Indicates request validation failed before query-layer execution.
        /// </summary>
        Validation,

        /// <summary>
        /// Indicates the requested operation is not registered or is not supported.
        /// </summary>
        UnsupportedOperation,

        /// <summary>
        /// Indicates the requested stable key, snapshot, project, symbol, rule, finding, or diff input was not found.
        /// </summary>
        NotFound,

        /// <summary>
        /// Indicates the request matched multiple candidates and must be narrowed before execution can continue.
        /// </summary>
        Ambiguous,

        /// <summary>
        /// Indicates caller authentication was required or missing.
        /// </summary>
        Unauthorized,

        /// <summary>
        /// Indicates the caller is authenticated but not allowed to perform the requested operation.
        /// </summary>
        Forbidden,

        /// <summary>
        /// Indicates a required dependency or query capability is unavailable.
        /// </summary>
        DependencyUnavailable,

        /// <summary>
        /// Indicates the application/query layer failed and the failure was safely mapped for MCP clients.
        /// </summary>
        QueryLayerFailure,

        /// <summary>
        /// Indicates an unexpected server-side failure occurred without exposing implementation details.
        /// </summary>
        ServerError
    }
}
