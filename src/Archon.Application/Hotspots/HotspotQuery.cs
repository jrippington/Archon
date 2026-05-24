using Archon.Application.Rules;

namespace Archon.Application.Hotspots
{
    /// <summary>
    /// Represents controlled filters and paging options for snapshot hotspot queries.
    /// </summary>
    public sealed class HotspotQuery
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HotspotQuery"/> class.
        /// </summary>
        /// <param name="snapshotStableKey">The required snapshot stable key whose hotspots should be queried.</param>
        /// <param name="targetStableKey">The optional exact hotspot target stable-key filter.</param>
        /// <param name="category">The optional exact hotspot category filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        public HotspotQuery(string? snapshotStableKey, string? targetStableKey, string? category, int? skip, int? take)
        {
            // Query construction validates only controlled contract fields; the query service owns scoring and ordering behavior.
            SnapshotStableKey = string.IsNullOrWhiteSpace(snapshotStableKey) ? throw new ArgumentException("A snapshot stable key is required for hotspot queries.", nameof(snapshotStableKey)) : snapshotStableKey.Trim();
            TargetStableKey = string.IsNullOrWhiteSpace(targetStableKey) ? null : targetStableKey.Trim();
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
            Skip = ValidateSkip(skip);
            Take = ValidateTake(take);
        }

        /// <summary>
        /// Gets the required snapshot stable key whose hotspots should be queried.
        /// </summary>
        public string SnapshotStableKey { get; }

        /// <summary>
        /// Gets the optional exact hotspot target stable-key filter.
        /// </summary>
        public string? TargetStableKey { get; }

        /// <summary>
        /// Gets the optional exact hotspot category filter.
        /// </summary>
        public string? Category { get; }

        /// <summary>
        /// Gets the number of sorted records to skip.
        /// </summary>
        public int Skip { get; }

        /// <summary>
        /// Gets the maximum number of sorted records to return.
        /// </summary>
        public int Take { get; }

        /// <summary>
        /// Validates the optional skip value used by a public hotspot query.
        /// </summary>
        /// <param name="skip">The optional caller-provided skip value.</param>
        /// <returns>The validated non-negative skip value.</returns>
        private static int ValidateSkip(int? skip)
        {
            // Hotspot ranking is deterministic only when callers request a valid continuation position.
            if (skip.GetValueOrDefault(0) < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(skip), skip, "Skip must be greater than or equal to zero.");
            }

            return skip.GetValueOrDefault(0);
        }

        /// <summary>
        /// Validates the optional take value used by a public hotspot query.
        /// </summary>
        /// <param name="take">The optional caller-provided take value.</param>
        /// <returns>The validated page size.</returns>
        private static int ValidateTake(int? take)
        {
            // Bounded hotspot pages protect callers from accidentally materializing every ranked result.
            int value = take.GetValueOrDefault(QueryPagingOptions.DefaultPageSize);
            if (value < 1 || value > QueryPagingOptions.MaximumPageSize)
            {
                throw new ArgumentOutOfRangeException(nameof(take), take, "Take must be between 1 and 500.");
            }

            return value;
        }
    }
}
