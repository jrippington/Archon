namespace Archon.Application.Management
{
    /// <summary>
    /// Represents audit-ready metadata attached to accepted controlled management actions.
    /// </summary>
    /// <param name="RequestedBy">The normalized actor identity associated with the management action.</param>
    /// <param name="RequestedUtc">The UTC timestamp when the application accepted the action.</param>
    /// <param name="CorrelationId">The generated correlation identity for tracing one management action.</param>
    public sealed record AuditMetadataResponse(string RequestedBy, DateTimeOffset RequestedUtc, string CorrelationId);
}
