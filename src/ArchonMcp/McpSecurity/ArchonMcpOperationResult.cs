namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Carries the success flag and safe payload returned by the MCP operation security pipeline.
    /// </summary>
    public sealed record ArchonMcpOperationResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpOperationResult" /> record.
        /// </summary>
        /// <param name="succeeded">Indicates whether the operation completed successfully.</param>
        /// <param name="payload">The success envelope or structured error payload returned to the caller.</param>
        public ArchonMcpOperationResult(bool succeeded, object payload)
        {
            // The executor returns object payloads because concrete tool response types vary while security handling is common.
            Succeeded = succeeded;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        /// <summary>
        /// Gets a value indicating whether the operation completed successfully.
        /// </summary>
        public bool Succeeded { get; init; }

        /// <summary>
        /// Gets the success envelope or structured error payload returned to the caller.
        /// </summary>
        public object Payload { get; init; }
    }
}
