namespace Archon.Api.Query.Contracts
{
    /// <summary>
    /// Represents the common non-paged response envelope for WP014 query API endpoints.
    /// </summary>
    /// <typeparam name="TData">The endpoint-specific data payload carried by the envelope.</typeparam>
    public sealed record QueryApiResponse<TData>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QueryApiResponse{TData}"/> record.
        /// </summary>
        /// <param name="data">The endpoint-specific data payload.</param>
        /// <param name="scope">The repository, solution, or target scope that was applied to the response.</param>
        /// <param name="snapshot">The snapshot metadata that explains which persisted graph state produced the response.</param>
        /// <param name="page">The non-paged metadata that makes the absence of pagination explicit.</param>
        /// <param name="truncation">The response-size metadata for bounded result sections.</param>
        /// <param name="warnings">The safe warnings that explain partial or degraded response content.</param>
        /// <param name="unknowns">The explicit unknown fields that distinguish unavailable data from false or zero values.</param>
        /// <param name="request">The request metadata available at the HTTP boundary.</param>
        public QueryApiResponse(
            TData data,
            QueryScopeMetadataResponse scope,
            QuerySnapshotMetadataResponse snapshot,
            QueryNonPagedMetadataResponse page,
            QueryTruncationMetadataResponse truncation,
            IEnumerable<QueryWarningResponse>? warnings,
            IEnumerable<QueryUnknownResponse>? unknowns,
            QueryRequestMetadataResponse request)
        {
            // The envelope copies collection sections so callers receive stable metadata for the serialized response lifetime.
            Data = data;
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Truncation = truncation ?? throw new ArgumentNullException(nameof(truncation));
            Warnings = warnings?.ToArray() ?? [];
            Unknowns = unknowns?.ToArray() ?? [];
            Request = request ?? throw new ArgumentNullException(nameof(request));
        }

        /// <summary>
        /// Gets the endpoint-specific data payload.
        /// </summary>
        public TData Data { get; init; }

        /// <summary>
        /// Gets the repository, solution, or target scope that was applied to the response.
        /// </summary>
        public QueryScopeMetadataResponse Scope { get; init; }

        /// <summary>
        /// Gets the snapshot metadata that explains which persisted graph state produced the response.
        /// </summary>
        public QuerySnapshotMetadataResponse Snapshot { get; init; }

        /// <summary>
        /// Gets the non-paged metadata that makes the absence of pagination explicit.
        /// </summary>
        public QueryNonPagedMetadataResponse Page { get; init; }

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