namespace Archon.Application.Management
{
    /// <summary>
    /// Represents readiness of required query dependencies without exposing sensitive details.
    /// </summary>
    /// <param name="Status">The aggregate readiness status.</param>
    /// <param name="CheckedUtc">The UTC timestamp when readiness was evaluated.</param>
    /// <param name="Dependencies">The sanitized dependency readiness rows.</param>
    /// <param name="Warnings">The safe warnings explaining degraded readiness.</param>
    public sealed record ManagementReadinessResponse(string Status, DateTimeOffset CheckedUtc, IReadOnlyList<DependencyReadinessResponse> Dependencies, IReadOnlyList<string> Warnings);
}
