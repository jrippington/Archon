namespace Archon.Application.Dashboard
{
    /// <summary>
    /// Represents one deterministic validation error for a dashboard summary query.
    /// </summary>
    public sealed class DashboardSummaryValidationError
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DashboardSummaryValidationError"/> class.
        /// </summary>
        /// <param name="code">The stable machine-readable validation code.</param>
        /// <param name="message">The safe developer-facing validation message.</param>
        public DashboardSummaryValidationError(string? code, string? message)
        {
            // Validation errors cross the API boundary, so only explicit codes and safe messages are retained.
            Code = string.IsNullOrWhiteSpace(code) ? DashboardSummaryValidationCodes.SnapshotSelectorInvalid : code.Trim();
            Message = string.IsNullOrWhiteSpace(message) ? "The dashboard summary request is invalid." : message.Trim();
        }

        /// <summary>
        /// Gets the stable machine-readable validation code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the safe developer-facing validation message.
        /// </summary>
        public string Message { get; }
    }
}