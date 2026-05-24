namespace Archon.Application.Cycles
{
    /// <summary>
    /// Represents the bounded result of dependency cycle detection for one snapshot graph.
    /// </summary>
    public sealed class CycleDetectionResult
    {
        /// <summary>
        /// Initializes a new dependency cycle detection result.
        /// </summary>
        /// <param name="cycles">The deterministic page of detected cycle records.</param>
        /// <param name="totalCount">The total canonical cycle count before result-limit truncation.</param>
        /// <param name="resultLimit">The maximum number of cycle records requested by the caller.</param>
        /// <param name="hasTruncatedResults">A value indicating whether more canonical cycles existed than were returned.</param>
        public CycleDetectionResult(IReadOnlyList<CycleRecord> cycles, int totalCount, int resultLimit, bool hasTruncatedResults)
        {
            // The result separates total canonical count from returned records so APIs can expose truncation without ad hoc metadata parsing.
            ArgumentNullException.ThrowIfNull(cycles);
            Cycles = cycles.ToArray();
            TotalCount = Math.Max(0, totalCount);
            ResultLimit = Math.Max(1, resultLimit);
            HasTruncatedResults = hasTruncatedResults;
        }

        /// <summary>
        /// Gets the deterministic page of detected cycle records.
        /// </summary>
        public IReadOnlyList<CycleRecord> Cycles { get; }

        /// <summary>
        /// Gets the total canonical cycle count before result-limit truncation.
        /// </summary>
        public int TotalCount { get; }

        /// <summary>
        /// Gets the maximum number of cycle records requested by the caller.
        /// </summary>
        public int ResultLimit { get; }

        /// <summary>
        /// Gets a value indicating whether more canonical cycles existed than were returned.
        /// </summary>
        public bool HasTruncatedResults { get; }
    }
}
