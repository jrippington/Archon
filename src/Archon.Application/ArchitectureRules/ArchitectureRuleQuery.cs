using Archon.Application.Rules;

namespace Archon.Application.ArchitectureRules
{
    /// <summary>
    /// Represents controlled filters and paging options for snapshot architecture-rule result queries.
    /// </summary>
    public sealed class ArchitectureRuleQuery
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchitectureRuleQuery"/> class.
        /// </summary>
        /// <param name="snapshotStableKey">The required snapshot stable key whose rule results should be queried.</param>
        /// <param name="category">The optional exact rule category filter.</param>
        /// <param name="status">The optional exact result status filter.</param>
        /// <param name="targetStableKey">The optional exact result target stable-key filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        public ArchitectureRuleQuery(string? snapshotStableKey, string? category, string? status, string? targetStableKey, int? skip, int? take)
        {
            // Query construction validates the fixed public contract while evaluation logic remains owned by the application service.
            SnapshotStableKey = string.IsNullOrWhiteSpace(snapshotStableKey) ? throw new ArgumentException("A snapshot stable key is required for architecture-rule queries.", nameof(snapshotStableKey)) : snapshotStableKey.Trim();
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
            Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
            TargetStableKey = string.IsNullOrWhiteSpace(targetStableKey) ? null : targetStableKey.Trim();
            Skip = ValidateSkip(skip);
            Take = ValidateTake(take);
        }

        /// <summary>
        /// Gets the required snapshot stable key whose rule results should be queried.
        /// </summary>
        public string SnapshotStableKey { get; }

        /// <summary>
        /// Gets the optional exact rule category filter.
        /// </summary>
        public string? Category { get; }

        /// <summary>
        /// Gets the optional exact result status filter.
        /// </summary>
        public string? Status { get; }

        /// <summary>
        /// Gets the optional exact result target stable-key filter.
        /// </summary>
        public string? TargetStableKey { get; }

        /// <summary>
        /// Gets the number of sorted records to skip.
        /// </summary>
        public int Skip { get; }

        /// <summary>
        /// Gets the maximum number of sorted records to return.
        /// </summary>
        public int Take { get; }

        /// <summary>
        /// Validates the optional skip value used by a public architecture-rule query.
        /// </summary>
        /// <param name="skip">The optional caller-provided skip value.</param>
        /// <returns>The validated non-negative skip value.</returns>
        private static int ValidateSkip(int? skip)
        {
            // Negative paging would create ambiguous continuation behavior, so the contract reports it as a deterministic error.
            if (skip.GetValueOrDefault(0) < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(skip), skip, "Skip must be greater than or equal to zero.");
            }

            return skip.GetValueOrDefault(0);
        }

        /// <summary>
        /// Validates the optional take value used by a public architecture-rule query.
        /// </summary>
        /// <param name="take">The optional caller-provided take value.</param>
        /// <returns>The validated page size.</returns>
        private static int ValidateTake(int? take)
        {
            // Rule result pages use the shared WP013 maximum to remain consistent with metrics, cycles, and hotspots.
            int value = take.GetValueOrDefault(QueryPagingOptions.DefaultPageSize);
            if (value < 1 || value > QueryPagingOptions.MaximumPageSize)
            {
                throw new ArgumentOutOfRangeException(nameof(take), take, "Take must be between 1 and 500.");
            }

            return value;
        }
    }
}
