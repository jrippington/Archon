namespace Archon.Application.Extraction.Runs
{
    /// <summary>
    /// Describes the current progress point for a run as exposed through status polling.
    /// </summary>
    /// <param name="Stage">The current lifecycle or workflow stage name.</param>
    /// <param name="Message">The credential-safe human-readable progress message.</param>
    /// <param name="Percentage">The optional progress percentage when the current stage can calculate one.</param>
    /// <param name="LastUpdatedUtc">The UTC timestamp when this progress value was last updated.</param>
    public sealed record ExtractionRunProgress(
        string Stage,
        string Message,
        int? Percentage,
        DateTimeOffset LastUpdatedUtc);
}
