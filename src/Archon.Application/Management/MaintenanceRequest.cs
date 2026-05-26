namespace Archon.Application.Management
{
    /// <summary>
    /// Captures a controlled maintenance operation request.
    /// </summary>
    /// <param name="Operation">The supported maintenance operation name.</param>
    /// <param name="DryRun">A value indicating whether the operation should validate without changing state.</param>
    /// <param name="RequestedBy">The optional actor identity used for audit metadata.</param>
    public sealed record MaintenanceRequest(string? Operation, bool DryRun, string? RequestedBy);
}
