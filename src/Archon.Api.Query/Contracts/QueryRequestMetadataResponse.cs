namespace Archon.Api.Query.Contracts
{
    /// <summary>
    /// Describes request correlation metadata that is safe to echo in a query response envelope.
    /// </summary>
    public sealed record QueryRequestMetadataResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QueryRequestMetadataResponse"/> record.
        /// </summary>
        /// <param name="traceIdentifier">The ASP.NET Core trace identifier for the request.</param>
        /// <param name="correlationId">The optional caller-supplied correlation identifier when available.</param>
        public QueryRequestMetadataResponse(string traceIdentifier, string? correlationId)
        {
            // Request metadata intentionally excludes query values and stable keys so logs and envelopes do not become a secret side channel.
            TraceIdentifier = traceIdentifier;
            CorrelationId = correlationId;
        }

        /// <summary>
        /// Gets the ASP.NET Core trace identifier for the request.
        /// </summary>
        public string TraceIdentifier { get; init; }

        /// <summary>
        /// Gets the optional caller-supplied correlation identifier when available.
        /// </summary>
        public string? CorrelationId { get; init; }
    }
}