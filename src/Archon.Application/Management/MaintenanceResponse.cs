namespace Archon.Application.Management
{
    /// <summary>
    /// Represents the explicit outcome of a controlled maintenance operation.
    /// </summary>
    /// <param name="Operation">The normalized supported maintenance operation name.</param>
    /// <param name="DryRun">A value indicating whether the operation only validated planned work.</param>
    /// <param name="Outcome">The safe outcome summary for the maintenance request.</param>
    /// <param name="Warnings">The safe warnings emitted by the maintenance operation.</param>
    /// <param name="Errors">The safe errors emitted by the maintenance operation.</param>
    /// <param name="Audit">The audit-ready metadata for the maintenance action.</param>
    public sealed record MaintenanceResponse(string Operation, bool DryRun, string Outcome, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors, AuditMetadataResponse Audit);
}
