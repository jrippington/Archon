namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Classifies how strongly an MCP response, fact, evidence reference, finding, or unknown is supported by persisted Archon data.
    /// </summary>
    /// <remarks>
    /// Confidence is part of the common MCP envelope so AI clients can distinguish established facts from partial, inferred, or
    /// unavailable data without inventing certainty that the query layer did not provide.
    /// </remarks>
    public enum ArchonMcpConfidenceLevel
    {
        /// <summary>
        /// Indicates the response cannot assign a meaningful confidence value from the available persisted data.
        /// </summary>
        Unknown,

        /// <summary>
        /// Indicates persisted data gives weak or incomplete support for the represented statement.
        /// </summary>
        Low,

        /// <summary>
        /// Indicates persisted data gives useful but not definitive support for the represented statement.
        /// </summary>
        Medium,

        /// <summary>
        /// Indicates persisted data gives strong support for the represented statement.
        /// </summary>
        High
    }
}
