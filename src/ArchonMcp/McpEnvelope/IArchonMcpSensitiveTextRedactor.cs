namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Redacts secret-like or unsafe text before content is returned through MCP envelopes.
    /// </summary>
    public interface IArchonMcpSensitiveTextRedactor
    {
        /// <summary>
        /// Redacts secret-like values from untrusted evidence or metadata text.
        /// </summary>
        /// <param name="text">The untrusted text to redact.</param>
        /// <returns>Redacted text, or <see langword="null" /> when the input was <see langword="null" />.</returns>
        string? Redact(string? text);
    }
}
