namespace Archon.Api.Query.Contracts
{
    /// <summary>
    /// Describes a query response that is intentionally not paged.
    /// </summary>
    public sealed record QueryNonPagedMetadataResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QueryNonPagedMetadataResponse"/> record.
        /// </summary>
        /// <param name="isPaged">Indicates whether this response supports item paging.</param>
        /// <param name="description">The safe explanation of why paging is or is not present.</param>
        public QueryNonPagedMetadataResponse(bool isPaged, string description)
        {
            // Non-paged metadata is explicit so clients do not interpret missing page fields as a serialization mistake.
            IsPaged = isPaged;
            Description = description;
        }

        /// <summary>
        /// Gets a value indicating whether this response supports item paging.
        /// </summary>
        public bool IsPaged { get; init; }

        /// <summary>
        /// Gets the safe explanation of why paging is or is not present.
        /// </summary>
        public string Description { get; init; }

        /// <summary>
        /// Creates the standard metadata value for a single non-paged summary response.
        /// </summary>
        /// <returns>A non-paged metadata response for summary endpoints.</returns>
        public static QueryNonPagedMetadataResponse Summary()
        {
            // Summary endpoints return one aggregate object instead of a continuation page.
            return new QueryNonPagedMetadataResponse(false, "This summary endpoint returns one aggregate response for the selected scope.");
        }
    }
}