namespace Archon.Api.Query.Contracts
{
    /// <summary>
    /// Represents one response field whose value is explicitly unknown.
    /// </summary>
    public sealed record QueryUnknownResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QueryUnknownResponse"/> record.
        /// </summary>
        /// <param name="field">The response field whose value is unknown.</param>
        /// <param name="reason">The safe reason the value is unknown.</param>
        public QueryUnknownResponse(string field, string reason)
        {
            // Unknowns are first-class envelope data so clients do not mistake omitted optional sections for complete absence.
            Field = field;
            Reason = reason;
        }

        /// <summary>
        /// Gets the response field whose value is unknown.
        /// </summary>
        public string Field { get; init; }

        /// <summary>
        /// Gets the safe reason the value is unknown.
        /// </summary>
        public string Reason { get; init; }
    }
}