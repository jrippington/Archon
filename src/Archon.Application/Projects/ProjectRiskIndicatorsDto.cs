namespace Archon.Application.Projects
{
    /// <summary>
    /// Summarizes risk indicators associated with one project in the selected snapshot.
    /// </summary>
    /// <param name="HasHotlistFindings">A value indicating whether hotlist findings target the project.</param>
    /// <param name="HotlistCount">The number of hotlist findings targeting the project.</param>
    /// <param name="HighestSeverity">The highest known finding severity targeting the project.</param>
    /// <param name="HasUnknownData">A value indicating whether any project fact reports unknown data.</param>
    /// <param name="UnknownReason">The safe reason unknown data is present when available.</param>
    public sealed record ProjectRiskIndicatorsDto(bool HasHotlistFindings, int HotlistCount, string? HighestSeverity, bool HasUnknownData, string? UnknownReason)
    {
        // Risk indicators are derived from stable graph facts and never from internal persistence labels or arbitrary query text.
    }
}
