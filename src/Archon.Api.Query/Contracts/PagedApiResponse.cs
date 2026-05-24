namespace Archon.Api.Query.Contracts
{
    /// <summary>
    /// Represents a stable paged API response envelope for controlled query endpoints.
    /// </summary>
    /// <typeparam name="TItem">The response item type carried by this page.</typeparam>
    public sealed record PagedApiResponse<TItem>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PagedApiResponse{TItem}"/> record.
        /// </summary>
        /// <param name="items">The ordered items returned in this page.</param>
        /// <param name="totalCount">The total matching record count before paging.</param>
        /// <param name="skip">The number of sorted records skipped before this page.</param>
        /// <param name="take">The maximum number of sorted records requested for this page.</param>
        public PagedApiResponse(IReadOnlyList<TItem> items, int totalCount, int skip, int take)
        {
            // The response mirrors the application paging envelope but uses API-specific records for OpenAPI metadata.
            Items = items;
            TotalCount = totalCount;
            Skip = skip;
            Take = take;
        }

        /// <summary>Gets the ordered items returned in this page.</summary>
        public IReadOnlyList<TItem> Items { get; init; }

        /// <summary>Gets the total matching record count before paging.</summary>
        public int TotalCount { get; init; }

        /// <summary>Gets the number of sorted records skipped before this page.</summary>
        public int Skip { get; init; }

        /// <summary>Gets the maximum number of sorted records requested for this page.</summary>
        public int Take { get; init; }
    }
}
