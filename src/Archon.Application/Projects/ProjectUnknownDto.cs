namespace Archon.Application.Projects
{
    /// <summary>
    /// Represents one project query field whose data is explicitly unknown or unavailable.
    /// </summary>
    /// <param name="Field">The response field or section that contains unknown data.</param>
    /// <param name="Reason">The safe reason the data is unknown.</param>
    public sealed record ProjectUnknownDto(string Field, string Reason)
    {
        // Unknown metadata distinguishes unavailable extracted facts from a meaningful false, zero, or empty result.
    }
}
