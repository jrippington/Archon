namespace Archon.Application.Management
{
    /// <summary>
    /// Captures a controlled rule enablement state change.
    /// </summary>
    /// <param name="RuleCode">The stable rule code whose enablement state is being changed.</param>
    /// <param name="Version">The exact rule version whose enablement state is being changed.</param>
    /// <param name="Enabled">A value indicating whether the rule version should be enabled.</param>
    /// <param name="Reason">The optional safe reason retained for audit review.</param>
    /// <param name="RequestedBy">The optional actor identity used for audit metadata.</param>
    public sealed record RuleEnablementRequest(string? RuleCode, string? Version, bool Enabled, string? Reason, string? RequestedBy);
}
