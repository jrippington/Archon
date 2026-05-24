using Archon.Application.Rules;

namespace Archon.Application.Cycles
{
    /// <summary>
    /// Represents controlled filters and paging options for snapshot dependency cycle queries.
    /// </summary>
    public sealed class CycleQuery
    {
        /// <summary>
        /// Initializes a new snapshot cycle query contract.
        /// </summary>
        /// <param name="snapshotStableKey">The required snapshot stable key whose dependency cycles should be queried.</param>
        /// <param name="nodeStableKey">The optional exact node stable key that must participate in returned cycles.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        public CycleQuery(string? snapshotStableKey, string? nodeStableKey, int? skip, int? take)
        {
            // Query construction validates only the public contract; graph traversal remains inside the application service.
            SnapshotStableKey = string.IsNullOrWhiteSpace(snapshotStableKey) ? throw new ArgumentException("A snapshot stable key is required for cycle queries.", nameof(snapshotStableKey)) : snapshotStableKey.Trim();
            NodeStableKey = string.IsNullOrWhiteSpace(nodeStableKey) ? null : nodeStableKey.Trim();
            Skip = ValidateSkip(skip);
            Take = ValidateTake(take);
        }

        /// <summary>
        /// Gets the required snapshot stable key whose dependency cycles should be queried.
        /// </summary>
        public string SnapshotStableKey { get; }

        /// <summary>
        /// Gets the optional exact node stable key that must participate in returned cycles.
        /// </summary>
        public string? NodeStableKey { get; }

        /// <summary>
        /// Gets the number of sorted records to skip.
        /// </summary>
        public int Skip { get; }

        /// <summary>
        /// Gets the maximum number of sorted records to return.
        /// </summary>
        public int Take { get; }

        /// <summary>
        /// Validates the optional skip value used by a public cycle query.
        /// </summary>
        /// <param name="skip">The optional caller-provided skip value.</param>
        /// <returns>The validated non-negative skip value.</returns>
        private static int ValidateSkip(int? skip)
        {
            // Negative paging values are rejected so clients can correct continuation requests deterministically.
            if (skip.GetValueOrDefault(0) < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(skip), skip, "Skip must be greater than or equal to zero.");
            }

            return skip.GetValueOrDefault(0);
        }

        /// <summary>
        /// Validates the optional take value used by a public cycle query.
        /// </summary>
        /// <param name="take">The optional caller-provided take value.</param>
        /// <returns>The validated page size.</returns>
        private static int ValidateTake(int? take)
        {
            // Cycle traversal can be expensive, so the API contract keeps result pages within the shared WP013 bound.
            int value = take.GetValueOrDefault(QueryPagingOptions.DefaultPageSize);
            if (value < 1 || value > QueryPagingOptions.MaximumPageSize)
            {
                throw new ArgumentOutOfRangeException(nameof(take), take, "Take must be between 1 and 500.");
            }

            return value;
        }
    }
}
