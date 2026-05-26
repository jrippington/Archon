namespace Archon.Application.Dashboard
{
    /// <summary>
    /// Represents one explicitly unknown dashboard summary field.
    /// </summary>
    public sealed class DashboardUnknownDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DashboardUnknownDto"/> class.
        /// </summary>
        /// <param name="field">The response field whose value is unknown.</param>
        /// <param name="reason">The reason the value could not be determined from persisted facts.</param>
        public DashboardUnknownDto(string? field, string? reason)
        {
            // Unknowns are field-oriented so callers can distinguish absent facts from unsupported or incomplete extraction input.
            Field = string.IsNullOrWhiteSpace(field) ? "dashboardSummary" : field.Trim();
            Reason = string.IsNullOrWhiteSpace(reason) ? "The value is not available in persisted snapshot facts." : reason.Trim();
        }

        /// <summary>
        /// Gets the response field whose value is unknown.
        /// </summary>
        public string Field { get; }

        /// <summary>
        /// Gets the reason the value could not be determined from persisted facts.
        /// </summary>
        public string Reason { get; }
    }
}