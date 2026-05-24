using Archon.Domain.Graph.Metadata;

namespace Archon.Application.ArchitectureRules
{
    /// <summary>
    /// Describes one public architecture-rule result returned by controlled query APIs.
    /// </summary>
    /// <param name="SnapshotStableKey">The stable key of the snapshot that scopes the result.</param>
    /// <param name="StableKey">The deterministic architecture-rule result stable key.</param>
    /// <param name="RuleCode">The stable rule/check identity.</param>
    /// <param name="RuleName">The developer-facing rule/check name.</param>
    /// <param name="Category">The controlled rule category.</param>
    /// <param name="Status">The stable result status.</param>
    /// <param name="TargetStableKey">The stable key of the primary result target.</param>
    /// <param name="TargetKind">The public target kind such as Project, Controller, or Node.</param>
    /// <param name="DisplayName">The optional developer-facing target display name.</param>
    /// <param name="Description">The developer-facing result description.</param>
    /// <param name="ContributingMetricStableKeys">The metric stable keys that contributed to the result.</param>
    /// <param name="ContributingEdgeStableKeys">The architecture edge stable keys that contributed to the result.</param>
    /// <param name="ContributingFindingStableKeys">The finding stable keys that contributed to the result.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys that explain contributing graph facts.</param>
    /// <param name="Confidence">The normalized result confidence.</param>
    /// <param name="HasUnknownData">A value indicating whether the result contains unknown-state context.</param>
    /// <param name="UnknownReason">The reason unknown data is present when available.</param>
    /// <param name="Metadata">The sanitized deterministic result metadata.</param>
    /// <param name="Fingerprint">The deterministic result fingerprint.</param>
    public sealed record ArchitectureRuleItemDto(
        string SnapshotStableKey,
        string StableKey,
        string RuleCode,
        string RuleName,
        string Category,
        string Status,
        string TargetStableKey,
        string TargetKind,
        string? DisplayName,
        string Description,
        IReadOnlyList<string> ContributingMetricStableKeys,
        IReadOnlyList<string> ContributingEdgeStableKeys,
        IReadOnlyList<string> ContributingFindingStableKeys,
        IReadOnlyList<string> EvidenceStableKeys,
        decimal Confidence,
        bool HasUnknownData,
        string? UnknownReason,
        GraphMetadata Metadata,
        string Fingerprint);
}
