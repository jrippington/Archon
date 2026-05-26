namespace Archon.Application.Management
{
    /// <summary>
    /// Represents the current enablement state for one rule code and version.
    /// </summary>
    /// <param name="RuleCode">The stable rule code whose enablement state was changed.</param>
    /// <param name="Version">The exact rule version whose enablement state was changed.</param>
    /// <param name="Enabled">A value indicating whether the rule version is enabled.</param>
    /// <param name="Reason">The optional safe reason retained for audit review.</param>
    /// <param name="Audit">The audit-ready metadata for the rule enablement action.</param>
    public sealed record RuleEnablementResponse(string RuleCode, string Version, bool Enabled, string? Reason, AuditMetadataResponse Audit);
}
