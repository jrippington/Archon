namespace Archon.Extractors.Ui
{
    /// <summary>
    /// Describes how many UI extraction facts and diagnostics were added after stable-keyed snapshot accumulation.
    /// </summary>
    /// <param name="NodeDelta">The number of architecture nodes added after stable-key deduplication.</param>
    /// <param name="EdgeDelta">The number of architecture edges added after stable-key deduplication.</param>
    /// <param name="EvidenceDelta">The number of evidence records added after stable-key deduplication.</param>
    /// <param name="WarningDelta">The number of warning diagnostics appended by the merge operation.</param>
    /// <param name="ErrorDelta">The number of error diagnostics appended by the merge operation.</param>
    public sealed record UiSnapshotMergeSummary(
        int NodeDelta,
        int EdgeDelta,
        int EvidenceDelta,
        int WarningDelta,
        int ErrorDelta)
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UiSnapshotMergeSummary" /> record.
        /// </summary>
        /// <remarks>
        /// The positional record constructor stores already-computed deltas. The values are counts rather than graph facts so no additional validation beyond normal integer storage is required.
        /// </remarks>
        public UiSnapshotMergeSummary()
            : this(0, 0, 0, 0, 0)
        {
            // This parameterless constructor supports tests or serializers that need an explicit empty summary instance.
        }
    }
}
