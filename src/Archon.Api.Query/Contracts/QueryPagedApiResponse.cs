namespace Archon.Api.Query.Contracts
{
    /// <summary>
    /// Represents a stable paged response envelope for WP014 query API endpoints that need scope and snapshot metadata.
    /// </summary>
    /// <typeparam name="TItem">The response item type carried by the page.</typeparam>
    public sealed record QueryPagedApiResponse<TItem>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QueryPagedApiResponse{TItem}"/> record.
        /// </summary>
        /// <param name="items">The ordered items returned in this page.</param>
        /// <param name="totalCount">The total matching record count before paging.</param>
        /// <param name="skip">The number of sorted records skipped before this page.</param>
        /// <param name="take">The maximum number of sorted records requested for this page.</param>
        /// <param name="scope">The repository, solution, or target scope that was applied to the response.</param>
        /// <param name="snapshot">The snapshot metadata that explains which persisted graph state produced the response.</param>
        /// <param name="truncation">The response-size metadata for bounded result sections.</param>
        /// <param name="warnings">The safe warnings that explain partial or degraded response content.</param>
        /// <param name="unknowns">The explicit unknown fields that distinguish unavailable data from false or zero values.</param>
        /// <param name="request">The request metadata available at the HTTP boundary.</param>
        public QueryPagedApiResponse(
            IReadOnlyList<TItem> items,
            int totalCount,
            int skip,
            int take,
            QueryScopeMetadataResponse scope,
            QuerySnapshotMetadataResponse snapshot,
            QueryTruncationMetadataResponse truncation,
            IEnumerable<QueryWarningResponse>? warnings,
            IEnumerable<QueryUnknownResponse>? unknowns,
            QueryRequestMetadataResponse request)
        {
            // The paged envelope mirrors the non-paged WP014 envelope while adding stable continuation metadata for catalogue queries.
            Items = items ?? throw new ArgumentNullException(nameof(items));
            TotalCount = Math.Max(0, totalCount);
            Skip = Math.Max(0, skip);
            Take = Math.Max(1, take);
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Truncation = truncation ?? throw new ArgumentNullException(nameof(truncation));
            Warnings = warnings?.ToArray() ?? [];
            Unknowns = unknowns?.ToArray() ?? [];
            Request = request ?? throw new ArgumentNullException(nameof(request));
        }

        /// <summary>
        /// Gets the ordered items returned in this page.
        /// </summary>
        public IReadOnlyList<TItem> Items { get; init; }

        /// <summary>
        /// Gets the total matching record count before paging.
        /// </summary>
        public int TotalCount { get; init; }

        /// <summary>
        /// Gets the number of sorted records skipped before this page.
        /// </summary>
        public int Skip { get; init; }

        /// <summary>
        /// Gets the maximum number of sorted records requested for this page.
        /// </summary>
        public int Take { get; init; }

        /// <summary>
        /// Gets the repository, solution, or target scope that was applied to the response.
        /// </summary>
        public QueryScopeMetadataResponse Scope { get; init; }

        /// <summary>
        /// Gets the snapshot metadata that explains which persisted graph state produced the response.
        /// </summary>
        public QuerySnapshotMetadataResponse Snapshot { get; init; }

        /// <summary>
        /// Gets the response-size metadata for bounded result sections.
        /// </summary>
        public QueryTruncationMetadataResponse Truncation { get; init; }

        /// <summary>
        /// Gets the safe warnings that explain partial or degraded response content.
        /// </summary>
        public IReadOnlyList<QueryWarningResponse> Warnings { get; init; }

        /// <summary>
        /// Gets the explicit unknown fields that distinguish unavailable data from false or zero values.
        /// </summary>
        public IReadOnlyList<QueryUnknownResponse> Unknowns { get; init; }

        /// <summary>
        /// Gets the request metadata available at the HTTP boundary.
        /// </summary>
        public QueryRequestMetadataResponse Request { get; init; }
    }
}
