namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents one bounded page of controlled query results.
    /// </summary>
    /// <typeparam name="TItem">The result item type carried by the page.</typeparam>
    public sealed class PagedQueryResult<TItem>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PagedQueryResult{TItem}"/> class.
        /// </summary>
        /// <param name="items">The ordered result items returned for the current page.</param>
        /// <param name="totalCount">The total number of matching records before paging.</param>
        /// <param name="skip">The number of sorted records skipped before this page.</param>
        /// <param name="take">The maximum number of sorted records requested for this page.</param>
        public PagedQueryResult(IEnumerable<TItem> items, int totalCount, int skip, int take)
        {
            // The envelope preserves paging facts alongside items so API consumers can build deterministic continuation requests.
            ArgumentNullException.ThrowIfNull(items);
            Items = items.ToArray();
            TotalCount = Math.Max(0, totalCount);
            Skip = Math.Max(0, skip);
            Take = Math.Max(1, take);
        }

        /// <summary>
        /// Gets the ordered result items returned for the current page.
        /// </summary>
        public IReadOnlyList<TItem> Items { get; }

        /// <summary>
        /// Gets the total number of matching records before paging.
        /// </summary>
        public int TotalCount { get; }

        /// <summary>
        /// Gets the number of sorted records skipped before this page.
        /// </summary>
        public int Skip { get; }

        /// <summary>
        /// Gets the maximum number of sorted records requested for this page.
        /// </summary>
        public int Take { get; }
    }
}
