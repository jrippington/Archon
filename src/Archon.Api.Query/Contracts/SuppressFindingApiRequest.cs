using System.Text.Json;

namespace Archon.Api.Query.Contracts
{
    /// <summary>
    /// Represents the JSON body accepted by the controlled finding suppression endpoint.
    /// </summary>
    public sealed record SuppressFindingApiRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SuppressFindingApiRequest"/> record.
        /// </summary>
        /// <param name="findingHistoryKey">The finding history key that should receive the suppression overlay.</param>
        /// <param name="ruleCode">The rule code associated with the suppression target.</param>
        /// <param name="ruleVersion">The rule version associated with the suppression target.</param>
        /// <param name="primaryNodeStableKey">The primary node stable key associated with the suppression target.</param>
        /// <param name="reason">The human-readable suppression reason.</param>
        /// <param name="suppressedBy">The actor or process applying suppression.</param>
        /// <param name="metadata">Optional lower camel case metadata for suppression context.</param>
        public SuppressFindingApiRequest(
            string? findingHistoryKey,
            string? ruleCode,
            string? ruleVersion,
            string? primaryNodeStableKey,
            string? reason,
            string? suppressedBy,
            IReadOnlyDictionary<string, JsonElement>? metadata)
        {
            // Nullable values are preserved so application validation can return stable validation problem responses.
            FindingHistoryKey = findingHistoryKey;
            RuleCode = ruleCode;
            RuleVersion = ruleVersion;
            PrimaryNodeStableKey = primaryNodeStableKey;
            Reason = reason;
            SuppressedBy = suppressedBy;
            Metadata = metadata;
        }

        /// <summary>Gets the finding history key that should receive the suppression overlay.</summary>
        public string? FindingHistoryKey { get; init; }

        /// <summary>Gets the rule code associated with the suppression target.</summary>
        public string? RuleCode { get; init; }

        /// <summary>Gets the rule version associated with the suppression target.</summary>
        public string? RuleVersion { get; init; }

        /// <summary>Gets the primary node stable key associated with the suppression target.</summary>
        public string? PrimaryNodeStableKey { get; init; }

        /// <summary>Gets the human-readable suppression reason.</summary>
        public string? Reason { get; init; }

        /// <summary>Gets the actor or process applying suppression.</summary>
        public string? SuppressedBy { get; init; }

        /// <summary>Gets optional lower camel case metadata for suppression context.</summary>
        public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
    }
}
