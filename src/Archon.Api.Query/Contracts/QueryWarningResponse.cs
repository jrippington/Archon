namespace Archon.Api.Query.Contracts
{
    /// <summary>
    /// Represents a safe non-fatal warning returned inside a query response envelope.
    /// </summary>
    public sealed record QueryWarningResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QueryWarningResponse"/> record.
        /// </summary>
        /// <param name="code">The stable warning code.</param>
        /// <param name="message">The safe warning message.</param>
        public QueryWarningResponse(string code, string message)
        {
            // Warning messages must be safe to serialize because they cross the public API boundary.
            Code = code;
            Message = message;
        }

        /// <summary>
        /// Gets the stable warning code.
        /// </summary>
        public string Code { get; init; }

        /// <summary>
        /// Gets the safe warning message.
        /// </summary>
        public string Message { get; init; }
    }
}