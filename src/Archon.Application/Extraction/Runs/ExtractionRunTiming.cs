namespace Archon.Application.Extraction.Runs
{
    /// <summary>
    /// Represents one measured extraction run step duration for status diagnostics.
    /// </summary>
    /// <param name="Stage">The major stage or pipeline stage identifier that was measured.</param>
    /// <param name="ElapsedMilliseconds">The elapsed duration in milliseconds.</param>
    /// <param name="CompletedUtc">The UTC timestamp when the measured step completed.</param>
    public sealed record ExtractionRunTiming
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExtractionRunTiming"/> record.
        /// </summary>
        /// <param name="stage">The major stage, pipeline stage, or scoped diagnostic stage identifier that was measured.</param>
        /// <param name="elapsedMilliseconds">The elapsed duration in milliseconds; negative values are normalized to zero.</param>
        /// <param name="completedUtc">The UTC timestamp when the measured step completed.</param>
        public ExtractionRunTiming(string stage, long elapsedMilliseconds, DateTimeOffset completedUtc)
        {
            // Timing records are shared by top-level run timings and nested persistence diagnostics, so the constructor centralizes validation.
            if (string.IsNullOrWhiteSpace(stage))
            {
                throw new ArgumentException("Timing stage names must be non-empty.", nameof(stage));
            }

            Stage = stage.Trim();
            ElapsedMilliseconds = Math.Max(0, elapsedMilliseconds);
            CompletedUtc = completedUtc.ToUniversalTime();
        }

        /// <summary>
        /// Gets the major stage, pipeline stage, or scoped diagnostic stage identifier that was measured.
        /// </summary>
        public string Stage { get; }

        /// <summary>
        /// Gets the elapsed duration in milliseconds.
        /// </summary>
        public long ElapsedMilliseconds { get; }

        /// <summary>
        /// Gets the UTC timestamp when the measured step completed.
        /// </summary>
        public DateTimeOffset CompletedUtc { get; }
    }
}
