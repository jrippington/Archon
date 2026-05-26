namespace Archon.Application.Management
{
    /// <summary>
    /// Represents one extraction run in the safe management API shape.
    /// </summary>
    /// <param name="RunId">The stable public extraction run identifier.</param>
    /// <param name="Status">The lifecycle status of the extraction run.</param>
    /// <param name="StartedUtc">The UTC timestamp when the run was accepted.</param>
    /// <param name="CompletedUtc">The optional UTC timestamp when the run reached a terminal state.</param>
    /// <param name="Stage">The safe current progress stage.</param>
    /// <param name="Message">The safe current progress message.</param>
    /// <param name="Percentage">The current progress percentage.</param>
    /// <param name="WarningCount">The number of warnings recorded for the run.</param>
    /// <param name="ErrorCount">The number of errors recorded for the run.</param>
    /// <param name="SnapshotStableKey">The optional produced snapshot stable identity.</param>
    /// <param name="SolutionPaths">The submitted solution paths retained for operational history.</param>
    /// <param name="MetadataKeys">The submitted metadata keys retained without exposing metadata values.</param>
    public sealed record ExtractionRunHistoryItemResponse(
        string RunId,
        string Status,
        DateTimeOffset StartedUtc,
        DateTimeOffset? CompletedUtc,
        string Stage,
        string Message,
        int Percentage,
        int WarningCount,
        int ErrorCount,
        string? SnapshotStableKey,
        IReadOnlyList<string> SolutionPaths,
        IReadOnlyList<string> MetadataKeys);
}
