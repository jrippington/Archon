namespace Archon.Application.Diff
{
    /// <summary>
    /// Represents one client-correctable validation issue found while preparing a snapshot diff.
    /// </summary>
    /// <param name="Code">The deterministic validation code suitable for API problem-details keys.</param>
    /// <param name="Message">The developer-facing validation message.</param>
    public sealed record SnapshotDiffValidationError(string Code, string Message);
}
