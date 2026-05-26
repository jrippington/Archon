namespace Archon.Application.Dashboard
{
    /// <summary>
    /// Represents a non-fatal dashboard summary warning that should be visible to API consumers.
    /// </summary>
    public sealed class DashboardWarningDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DashboardWarningDto"/> class.
        /// </summary>
        /// <param name="code">The stable warning code.</param>
        /// <param name="message">The safe warning message explaining incomplete summary data.</param>
        public DashboardWarningDto(string? code, string? message)
        {
            // Warnings are intentionally concise because they are carried inside every successful dashboard envelope.
            Code = string.IsNullOrWhiteSpace(code) ? "DashboardSummaryWarning" : code.Trim();
            Message = string.IsNullOrWhiteSpace(message) ? "Dashboard summary data is partial." : message.Trim();
        }

        /// <summary>
        /// Gets the stable warning code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the safe warning message explaining incomplete summary data.
        /// </summary>
        public string Message { get; }
    }
}