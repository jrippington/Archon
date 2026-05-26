namespace Archon.Application.Management
{
    /// <summary>
    /// Represents local health status without infrastructure secrets or connection strings.
    /// </summary>
    /// <param name="Status">The aggregate health status for the management module.</param>
    /// <param name="CheckedUtc">The UTC timestamp when health was evaluated.</param>
    /// <param name="Checks">The safe health checks that contributed to the aggregate status.</param>
    /// <param name="Warnings">The safe warnings explaining degraded but locally usable conditions.</param>
    public sealed record ManagementHealthResponse(string Status, DateTimeOffset CheckedUtc, IReadOnlyList<string> Checks, IReadOnlyList<string> Warnings);
}
