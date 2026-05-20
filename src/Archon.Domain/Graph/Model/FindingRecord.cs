using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;

namespace Archon.Domain.Graph.Model
{
    /// <summary>
    /// Represents a snapshot-scoped architecture concern, usually produced by applying a versioned rule to graph facts.
    /// </summary>
    public sealed class FindingRecord
    {
        /// <summary>
        /// Initializes a validated finding record model.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the finding.</param>
        /// <param name="stableKey">The deterministic stable key that identifies the finding within the snapshot contract.</param>
        /// <param name="ruleCode">The rule code that produced or classifies the finding.</param>
        /// <param name="ruleVersion">The rule version that produced or classifies the finding.</param>
        /// <param name="severity">The controlled finding severity.</param>
        /// <param name="status">The controlled finding status.</param>
        /// <param name="title">The developer-facing finding title.</param>
        /// <param name="description">The developer-facing finding description.</param>
        /// <param name="knowledgeKind">The knowledge classification that explains how Archon knows the finding is valid.</param>
        /// <param name="confidence">The normalized confidence assigned to the finding.</param>
        /// <param name="primaryNodeStableKey">The optional primary node stable key associated with the finding.</param>
        /// <param name="primaryEvidenceStableKey">The optional primary evidence stable key explaining the finding.</param>
        /// <param name="firstSeenSnapshotStableKey">The optional stable key of the first snapshot where the finding was seen.</param>
        /// <param name="latestSeenSnapshotStableKey">The optional stable key of the latest snapshot where the finding was seen.</param>
        /// <param name="suppressionReason">The optional reason this finding is suppressed.</param>
        /// <param name="suppressedBy">The optional actor or process that suppressed this finding.</param>
        /// <param name="metadata">Deterministic metadata for finding details that are not normalized fields.</param>
        /// <param name="fingerprint">The deterministic fingerprint for diff-relevant finding content.</param>
        public FindingRecord(
            StableKey snapshotStableKey,
            StableKey stableKey,
            string? ruleCode,
            string? ruleVersion,
            FindingSeverity severity,
            FindingStatus status,
            string? title,
            string? description,
            KnowledgeKind knowledgeKind,
            Confidence confidence,
            StableKey? primaryNodeStableKey,
            StableKey? primaryEvidenceStableKey,
            StableKey? firstSeenSnapshotStableKey,
            StableKey? latestSeenSnapshotStableKey,
            string? suppressionReason,
            string? suppressedBy,
            GraphMetadata metadata,
            Fingerprint fingerprint)
        {
            // Finding construction keeps rule identity, confidence, status, and evidence linkage explicit for historical explanation.
            ArgumentNullException.ThrowIfNull(severity);
            ArgumentNullException.ThrowIfNull(status);
            ArgumentNullException.ThrowIfNull(knowledgeKind);
            ArgumentNullException.ThrowIfNull(metadata);
            GraphFactValidation.RequireUnknownReasonWhenNeeded(knowledgeKind, UnknownState.Known, nameof(FindingRecord));

            SnapshotStableKey = snapshotStableKey;
            StableKey = stableKey;
            RuleCode = GraphFactValidation.RequiredString(ruleCode, nameof(ruleCode));
            RuleVersion = GraphFactValidation.RequiredString(ruleVersion, nameof(ruleVersion));
            Severity = severity;
            Status = status;
            Title = GraphFactValidation.RequiredString(title, nameof(title));
            Description = GraphFactValidation.RequiredString(description, nameof(description));
            KnowledgeKind = knowledgeKind;
            Confidence = confidence;
            UnknownState = UnknownState.Known;
            PrimaryNodeStableKey = primaryNodeStableKey;
            PrimaryEvidenceStableKey = primaryEvidenceStableKey;
            FirstSeenSnapshotStableKey = firstSeenSnapshotStableKey;
            LatestSeenSnapshotStableKey = latestSeenSnapshotStableKey;
            SuppressionReason = GraphFactValidation.OptionalString(suppressionReason);
            SuppressedBy = GraphFactValidation.OptionalString(suppressedBy);
            Metadata = metadata;
            Fingerprint = fingerprint;
        }

        /// <summary>
        /// Initializes a validated finding record model with an explicit unknown-state value.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the finding.</param>
        /// <param name="stableKey">The deterministic stable key that identifies the finding within the snapshot contract.</param>
        /// <param name="ruleCode">The rule code that produced or classifies the finding.</param>
        /// <param name="ruleVersion">The rule version that produced or classifies the finding.</param>
        /// <param name="severity">The controlled finding severity.</param>
        /// <param name="status">The controlled finding status.</param>
        /// <param name="title">The developer-facing finding title.</param>
        /// <param name="description">The developer-facing finding description.</param>
        /// <param name="knowledgeKind">The knowledge classification that explains how Archon knows the finding is valid.</param>
        /// <param name="confidence">The normalized confidence assigned to the finding.</param>
        /// <param name="unknownState">The explicit unknown-state representation for the finding.</param>
        /// <param name="primaryNodeStableKey">The optional primary node stable key associated with the finding.</param>
        /// <param name="primaryEvidenceStableKey">The optional primary evidence stable key explaining the finding.</param>
        /// <param name="firstSeenSnapshotStableKey">The optional stable key of the first snapshot where the finding was seen.</param>
        /// <param name="latestSeenSnapshotStableKey">The optional stable key of the latest snapshot where the finding was seen.</param>
        /// <param name="suppressionReason">The optional reason this finding is suppressed.</param>
        /// <param name="suppressedBy">The optional actor or process that suppressed this finding.</param>
        /// <param name="metadata">Deterministic metadata for finding details that are not normalized fields.</param>
        /// <param name="fingerprint">The deterministic fingerprint for diff-relevant finding content.</param>
        public FindingRecord(
            StableKey snapshotStableKey,
            StableKey stableKey,
            string? ruleCode,
            string? ruleVersion,
            FindingSeverity severity,
            FindingStatus status,
            string? title,
            string? description,
            KnowledgeKind knowledgeKind,
            Confidence confidence,
            UnknownState unknownState,
            StableKey? primaryNodeStableKey,
            StableKey? primaryEvidenceStableKey,
            StableKey? firstSeenSnapshotStableKey,
            StableKey? latestSeenSnapshotStableKey,
            string? suppressionReason,
            string? suppressedBy,
            GraphMetadata metadata,
            Fingerprint fingerprint)
        {
            // This overload lets extractors model unknown finding data explicitly when rule output itself is incomplete.
            ArgumentNullException.ThrowIfNull(severity);
            ArgumentNullException.ThrowIfNull(status);
            ArgumentNullException.ThrowIfNull(knowledgeKind);
            ArgumentNullException.ThrowIfNull(unknownState);
            ArgumentNullException.ThrowIfNull(metadata);
            GraphFactValidation.RequireUnknownReasonWhenNeeded(knowledgeKind, unknownState, nameof(FindingRecord));

            SnapshotStableKey = snapshotStableKey;
            StableKey = stableKey;
            RuleCode = GraphFactValidation.RequiredString(ruleCode, nameof(ruleCode));
            RuleVersion = GraphFactValidation.RequiredString(ruleVersion, nameof(ruleVersion));
            Severity = severity;
            Status = status;
            Title = GraphFactValidation.RequiredString(title, nameof(title));
            Description = GraphFactValidation.RequiredString(description, nameof(description));
            KnowledgeKind = knowledgeKind;
            Confidence = confidence;
            UnknownState = unknownState;
            PrimaryNodeStableKey = primaryNodeStableKey;
            PrimaryEvidenceStableKey = primaryEvidenceStableKey;
            FirstSeenSnapshotStableKey = firstSeenSnapshotStableKey;
            LatestSeenSnapshotStableKey = latestSeenSnapshotStableKey;
            SuppressionReason = GraphFactValidation.OptionalString(suppressionReason);
            SuppressedBy = GraphFactValidation.OptionalString(suppressedBy);
            Metadata = metadata;
            Fingerprint = fingerprint;
        }

        /// <summary>
        /// Gets the stable key of the snapshot that scopes the finding.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the deterministic stable key that identifies the finding within the snapshot contract.
        /// </summary>
        public StableKey StableKey { get; }

        /// <summary>
        /// Gets the rule code that produced or classifies the finding.
        /// </summary>
        public string RuleCode { get; }

        /// <summary>
        /// Gets the rule version that produced or classifies the finding.
        /// </summary>
        public string RuleVersion { get; }

        /// <summary>
        /// Gets the controlled finding severity.
        /// </summary>
        public FindingSeverity Severity { get; }

        /// <summary>
        /// Gets the controlled finding status.
        /// </summary>
        public FindingStatus Status { get; }

        /// <summary>
        /// Gets the developer-facing finding title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Gets the developer-facing finding description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the knowledge classification that explains how Archon knows the finding is valid.
        /// </summary>
        public KnowledgeKind KnowledgeKind { get; }

        /// <summary>
        /// Gets the normalized confidence assigned to the finding.
        /// </summary>
        public Confidence Confidence { get; }

        /// <summary>
        /// Gets the explicit unknown-state representation for the finding.
        /// </summary>
        public UnknownState UnknownState { get; }

        /// <summary>
        /// Gets the optional primary node stable key associated with the finding.
        /// </summary>
        public StableKey? PrimaryNodeStableKey { get; }

        /// <summary>
        /// Gets the optional primary evidence stable key explaining the finding.
        /// </summary>
        public StableKey? PrimaryEvidenceStableKey { get; }

        /// <summary>
        /// Gets the optional stable key of the first snapshot where the finding was seen.
        /// </summary>
        public StableKey? FirstSeenSnapshotStableKey { get; }

        /// <summary>
        /// Gets the optional stable key of the latest snapshot where the finding was seen.
        /// </summary>
        public StableKey? LatestSeenSnapshotStableKey { get; }

        /// <summary>
        /// Gets the optional reason this finding is suppressed.
        /// </summary>
        public string? SuppressionReason { get; }

        /// <summary>
        /// Gets the optional actor or process that suppressed this finding.
        /// </summary>
        public string? SuppressedBy { get; }

        /// <summary>
        /// Gets deterministic metadata for finding details that are not normalized fields.
        /// </summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Gets the deterministic fingerprint for diff-relevant finding content.
        /// </summary>
        public Fingerprint Fingerprint { get; }
    }
}
