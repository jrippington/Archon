namespace Archon.Api.Query.Contracts
{
    /// <summary>
    /// Represents a safe machine-readable error shape for documented query API failures.
    /// </summary>
    public sealed record QueryErrorResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QueryErrorResponse"/> record.
        /// </summary>
        /// <param name="code">The stable machine-readable error code.</param>
        /// <param name="message">The safe developer-facing error message.</param>
        /// <param name="traceIdentifier">The optional ASP.NET Core trace identifier for support correlation.</param>
        public QueryErrorResponse(string code, string message, string? traceIdentifier)
        {
            // The error contract deliberately omits stack traces, exception type names, database details, and source snippets.
            Code = code;
            Message = message;
            TraceIdentifier = traceIdentifier;
        }

        /// <summary>
        /// Gets the stable machine-readable error code.
        /// </summary>
        public string Code { get; init; }

        /// <summary>
        /// Gets the safe developer-facing error message.
        /// </summary>
        public string Message { get; init; }

        /// <summary>
        /// Gets the optional ASP.NET Core trace identifier for support correlation.
        /// </summary>
        public string? TraceIdentifier { get; init; }
    }
}