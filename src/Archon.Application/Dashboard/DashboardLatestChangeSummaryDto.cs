namespace Archon.Application.Dashboard
{
    /// <summary>
    /// Represents a compact latest-change row in the dashboard summary.
    /// </summary>
    public sealed class DashboardLatestChangeSummaryDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DashboardLatestChangeSummaryDto"/> class.
        /// </summary>
        /// <param name="domain">The diff domain that changed.</param>
        /// <param name="changeKind">The kind of change reported for the domain.</param>
        /// <param name="count">The number of records reported for the change kind.</param>
        public DashboardLatestChangeSummaryDto(string? domain, string? changeKind, int count)
        {
            // Latest-change rows summarize the newest comparable previous snapshot without exposing per-record persistence identifiers.
            Domain = domain ?? string.Empty;
            ChangeKind = changeKind ?? string.Empty;
            Count = Math.Max(0, count);
        }

        /// <summary>
        /// Gets the diff domain that changed.
        /// </summary>
        public string Domain { get; }

        /// <summary>
        /// Gets the kind of change reported for the domain.
        /// </summary>
        public string ChangeKind { get; }

        /// <summary>
        /// Gets the number of records reported for the change kind.
        /// </summary>
        public int Count { get; }
    }
}