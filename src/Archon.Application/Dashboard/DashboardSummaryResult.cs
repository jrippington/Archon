namespace Archon.Application.Dashboard
{
    /// <summary>
    /// Represents either a successful dashboard summary or deterministic validation errors.
    /// </summary>
    public sealed class DashboardSummaryResult
    {
        /// <summary>
        /// Initializes a new successful instance of the <see cref="DashboardSummaryResult"/> class.
        /// </summary>
        /// <param name="summary">The successful dashboard summary.</param>
        public DashboardSummaryResult(DashboardSummaryDto summary)
        {
            // A successful result carries exactly one summary and no validation errors.
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a new unsuccessful instance of the <see cref="DashboardSummaryResult"/> class.
        /// </summary>
        /// <param name="validationErrors">The deterministic validation errors that prevented summary creation.</param>
        public DashboardSummaryResult(IEnumerable<DashboardSummaryValidationError> validationErrors)
        {
            // Validation errors are copied and exposed without exception details so the API can produce a safe error shape.
            Summary = null;
            ValidationErrors = validationErrors?.ToArray() ?? [];
        }

        /// <summary>
        /// Gets a value indicating whether the dashboard summary request succeeded.
        /// </summary>
        public bool Succeeded
        {
            get
            {
                // Success is determined by the presence of a summary so callers cannot ignore validation errors accidentally.
                return Summary is not null;
            }
        }

        /// <summary>
        /// Gets the successful dashboard summary when <see cref="Succeeded"/> is true.
        /// </summary>
        public DashboardSummaryDto? Summary { get; }

        /// <summary>
        /// Gets the deterministic validation errors that prevented summary creation.
        /// </summary>
        public IReadOnlyList<DashboardSummaryValidationError> ValidationErrors { get; }
    }
}