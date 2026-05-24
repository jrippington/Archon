using Archon.Domain.Graph.Metadata;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents a controlled API/application command to suppress a finding history target.
    /// </summary>
    public sealed class SuppressFindingCommand
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SuppressFindingCommand"/> class.
        /// </summary>
        /// <param name="findingHistoryKey">The finding history key that receives the suppression overlay.</param>
        /// <param name="ruleCode">The rule code associated with the suppression target.</param>
        /// <param name="ruleVersion">The rule version associated with the suppression target.</param>
        /// <param name="primaryNodeStableKey">The primary node stable key associated with the suppression target.</param>
        /// <param name="reason">The human-readable suppression reason.</param>
        /// <param name="suppressedBy">The actor or process applying the suppression.</param>
        /// <param name="metadata">The optional lower camel case suppression metadata.</param>
        public SuppressFindingCommand(
            string? findingHistoryKey,
            string? ruleCode,
            string? ruleVersion,
            string? primaryNodeStableKey,
            string? reason,
            string? suppressedBy,
            GraphMetadata? metadata)
        {
            // The command preserves raw nullable text so validation can return stable errors rather than throwing at the API boundary.
            FindingHistoryKey = findingHistoryKey;
            RuleCode = ruleCode;
            RuleVersion = ruleVersion;
            PrimaryNodeStableKey = primaryNodeStableKey;
            Reason = reason;
            SuppressedBy = suppressedBy;
            Metadata = metadata ?? GraphMetadata.Empty;
        }

        /// <summary>Gets the finding history key that receives the suppression overlay.</summary>
        public string? FindingHistoryKey { get; }

        /// <summary>Gets the rule code associated with the suppression target.</summary>
        public string? RuleCode { get; }

        /// <summary>Gets the rule version associated with the suppression target.</summary>
        public string? RuleVersion { get; }

        /// <summary>Gets the primary node stable key associated with the suppression target.</summary>
        public string? PrimaryNodeStableKey { get; }

        /// <summary>Gets the human-readable suppression reason.</summary>
        public string? Reason { get; }

        /// <summary>Gets the actor or process applying the suppression.</summary>
        public string? SuppressedBy { get; }

        /// <summary>Gets the optional lower camel case suppression metadata.</summary>
        public GraphMetadata Metadata { get; }
    }
}
