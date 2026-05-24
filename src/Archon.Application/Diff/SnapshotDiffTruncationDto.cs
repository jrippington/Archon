namespace Archon.Application.Diff
{
    /// <summary>
    /// Describes whether a snapshot diff detail list was truncated by skip/take bounds.
    /// </summary>
    /// <param name="Truncated">Indicates whether more matching detail rows exist beyond the returned page.</param>
    /// <param name="TotalAvailableItems">The total number of matching detail rows before skip/take is applied.</param>
    /// <param name="ReturnedItems">The number of detail rows returned in this response.</param>
    /// <param name="Skip">The number of matching detail rows skipped before this response.</param>
    /// <param name="Take">The maximum number of detail rows requested for this response.</param>
    public sealed record SnapshotDiffTruncationDto(
        bool Truncated,
        int TotalAvailableItems,
        int ReturnedItems,
        int Skip,
        int Take);
}
