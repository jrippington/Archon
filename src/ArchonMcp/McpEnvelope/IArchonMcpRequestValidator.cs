namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Validates common MCP request fields before operation handlers invoke application/query dependencies.
    /// </summary>
    public interface IArchonMcpRequestValidator
    {
        /// <summary>
        /// Validates the supplied common MCP request fields.
        /// </summary>
        /// <param name="request">The request fields to validate.</param>
        /// <returns>A validation result containing all detected safe failures.</returns>
        ArchonMcpValidationResult Validate(ArchonMcpValidationRequest request);
    }
}
