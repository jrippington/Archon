using Microsoft.Extensions.Logging;

namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Writes sanitized MCP audit events to the host logging pipeline.
    /// </summary>
    internal sealed class LoggingArchonMcpAuditSink : IArchonMcpAuditSink
    {
        /// <summary>
        /// Stores the logger used for structured sanitized audit events.
        /// </summary>
        private readonly ILogger<LoggingArchonMcpAuditSink> _logger;

        /// <summary>
        /// Creates a logging audit sink.
        /// </summary>
        /// <param name="logger">The logger that receives sanitized audit metadata.</param>
        public LoggingArchonMcpAuditSink(ILogger<LoggingArchonMcpAuditSink> logger)
        {
            // The sink depends on ILogger abstractions so hosts can route audit events through configured providers.
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public void Record(ArchonMcpAuditEvent auditEvent)
        {
            // Only sanitized metadata is logged; raw parameters, evidence snippets, tokens, and credentials never reach this sink.
            ArgumentNullException.ThrowIfNull(auditEvent);
            _logger.LogInformation(
                "Archon MCP audit: operation {OperationName}, caller {CallerId}, status {Status}, truncated {Truncated}, error {ErrorCategory}, durationMs {DurationMilliseconds}, safeParameters {SafeParameters}",
                auditEvent.OperationName,
                auditEvent.CallerId ?? "anonymous",
                auditEvent.Status,
                auditEvent.Truncated,
                auditEvent.ErrorCategory?.ToString() ?? "none",
                auditEvent.Duration.TotalMilliseconds,
                auditEvent.SafeParameters);
        }
    }
}
