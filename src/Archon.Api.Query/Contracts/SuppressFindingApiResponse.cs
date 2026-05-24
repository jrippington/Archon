namespace Archon.Api.Query.Contracts
{
    /// <summary>
    /// Represents the controlled response returned after a suppression request is persisted.
    /// </summary>
    public sealed record SuppressFindingApiResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SuppressFindingApiResponse"/> record.
        /// </summary>
        /// <param name="suppressedCount">The number of currently persisted findings updated by the suppression.</param>
        /// <param name="warnings">The non-fatal warnings returned during suppression persistence.</param>
        public SuppressFindingApiResponse(int suppressedCount, IReadOnlyList<string> warnings)
        {
            // The response intentionally returns counts and warnings only, not raw persistence details.
            SuppressedCount = suppressedCount;
            Warnings = warnings;
        }

        /// <summary>Gets the number of currently persisted findings updated by the suppression.</summary>
        public int SuppressedCount { get; init; }

        /// <summary>Gets the non-fatal warnings returned during suppression persistence.</summary>
        public IReadOnlyList<string> Warnings { get; init; }
    }
}
