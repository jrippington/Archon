using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;

namespace Archon.Domain.Graph.Model
{
    /// <summary>
    /// Represents an extracted architecture concept within one snapshot, such as a project, type, endpoint, UI route, or data-access artifact.
    /// </summary>
    public sealed class ArchitectureNode
    {
        /// <summary>
        /// Initializes a validated architecture node model.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the node.</param>
        /// <param name="stableKey">The deterministic stable key that identifies the node within the snapshot contract.</param>
        /// <param name="nodeKind">The controlled node kind that classifies the architecture concept.</param>
        /// <param name="displayName">The developer-facing display name for the node.</param>
        /// <param name="qualifiedName">The optional fully qualified symbol or domain name for the node.</param>
        /// <param name="searchName">The normalized search text used to find the node.</param>
        /// <param name="language">The optional programming or artifact language associated with the node.</param>
        /// <param name="projectStableKey">The optional stable key of the project that owns the node.</param>
        /// <param name="parentNodeStableKey">The optional stable key of the parent architecture node.</param>
        /// <param name="knowledgeKind">The knowledge classification that explains how Archon knows the node exists.</param>
        /// <param name="ownership">The optional ownership value produced by extraction or annotation.</param>
        /// <param name="externalCategory">The optional external category for imported or third-party concepts.</param>
        /// <param name="confidence">The normalized confidence assigned to the node.</param>
        /// <param name="unknownState">The explicit unknown-state representation for the node.</param>
        /// <param name="primaryEvidenceStableKey">The optional stable key of the primary evidence record explaining the node.</param>
        /// <param name="metadata">Deterministic metadata for node details that are not normalized fields.</param>
        /// <param name="fingerprint">The deterministic fingerprint for diff-relevant node content.</param>
        public ArchitectureNode(
            StableKey snapshotStableKey,
            StableKey stableKey,
            NodeKind nodeKind,
            string? displayName,
            string? qualifiedName,
            string? searchName,
            string? language,
            StableKey? projectStableKey,
            StableKey? parentNodeStableKey,
            KnowledgeKind knowledgeKind,
            string? ownership,
            string? externalCategory,
            Confidence confidence,
            UnknownState unknownState,
            StableKey? primaryEvidenceStableKey,
            GraphMetadata metadata,
            Fingerprint fingerprint)
        {
            // Node construction enforces the evidence-first contract fields that must be reliable before persistence exists.
            ArgumentNullException.ThrowIfNull(nodeKind);
            ArgumentNullException.ThrowIfNull(knowledgeKind);
            ArgumentNullException.ThrowIfNull(unknownState);
            ArgumentNullException.ThrowIfNull(metadata);
            GraphFactValidation.RequireUnknownReasonWhenNeeded(knowledgeKind, unknownState, nameof(ArchitectureNode));

            SnapshotStableKey = snapshotStableKey;
            StableKey = stableKey;
            NodeKind = nodeKind;
            DisplayName = GraphFactValidation.RequiredString(displayName, nameof(displayName));
            QualifiedName = GraphFactValidation.OptionalString(qualifiedName);
            SearchName = GraphFactValidation.RequiredString(searchName, nameof(searchName));
            Language = GraphFactValidation.OptionalString(language);
            ProjectStableKey = projectStableKey;
            ParentNodeStableKey = parentNodeStableKey;
            KnowledgeKind = knowledgeKind;
            Ownership = GraphFactValidation.OptionalString(ownership);
            ExternalCategory = GraphFactValidation.OptionalString(externalCategory);
            Confidence = confidence;
            UnknownState = unknownState;
            PrimaryEvidenceStableKey = primaryEvidenceStableKey;
            Metadata = metadata;
            Fingerprint = fingerprint;
        }

        /// <summary>
        /// Gets the stable key of the snapshot that scopes the node.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the deterministic stable key that identifies the node within the snapshot contract.
        /// </summary>
        public StableKey StableKey { get; }

        /// <summary>
        /// Gets the controlled node kind that classifies the architecture concept.
        /// </summary>
        public NodeKind NodeKind { get; }

        /// <summary>
        /// Gets the developer-facing display name for the node.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets the optional fully qualified symbol or domain name for the node.
        /// </summary>
        public string? QualifiedName { get; }

        /// <summary>
        /// Gets the normalized search text used to find the node.
        /// </summary>
        public string SearchName { get; }

        /// <summary>
        /// Gets the optional programming or artifact language associated with the node.
        /// </summary>
        public string? Language { get; }

        /// <summary>
        /// Gets the optional stable key of the project that owns the node.
        /// </summary>
        public StableKey? ProjectStableKey { get; }

        /// <summary>
        /// Gets the optional stable key of the parent architecture node.
        /// </summary>
        public StableKey? ParentNodeStableKey { get; }

        /// <summary>
        /// Gets the knowledge classification that explains how Archon knows the node exists.
        /// </summary>
        public KnowledgeKind KnowledgeKind { get; }

        /// <summary>
        /// Gets the optional ownership value produced by extraction or annotation.
        /// </summary>
        public string? Ownership { get; }

        /// <summary>
        /// Gets the optional external category for imported or third-party concepts.
        /// </summary>
        public string? ExternalCategory { get; }

        /// <summary>
        /// Gets the normalized confidence assigned to the node.
        /// </summary>
        public Confidence Confidence { get; }

        /// <summary>
        /// Gets the explicit unknown-state representation for the node.
        /// </summary>
        public UnknownState UnknownState { get; }

        /// <summary>
        /// Gets the optional stable key of the primary evidence record explaining the node.
        /// </summary>
        public StableKey? PrimaryEvidenceStableKey { get; }

        /// <summary>
        /// Gets deterministic metadata for node details that are not normalized fields.
        /// </summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Gets the deterministic fingerprint for diff-relevant node content.
        /// </summary>
        public Fingerprint Fingerprint { get; }
    }
}
