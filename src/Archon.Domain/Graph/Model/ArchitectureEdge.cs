using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;

namespace Archon.Domain.Graph.Model
{
    /// <summary>
    /// Represents an extracted relationship between two architecture nodes within one snapshot.
    /// </summary>
    public sealed class ArchitectureEdge
    {
        /// <summary>
        /// Initializes a validated architecture edge model.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the edge.</param>
        /// <param name="stableKey">The deterministic stable key that identifies the edge within the snapshot contract.</param>
        /// <param name="edgeKind">The controlled edge kind that classifies the relationship.</param>
        /// <param name="sourceNodeStableKey">The stable key of the source architecture node.</param>
        /// <param name="targetNodeStableKey">The stable key of the target architecture node.</param>
        /// <param name="isDirect">A value indicating whether the edge is directly observed rather than indirectly inferred.</param>
        /// <param name="knowledgeKind">The knowledge classification that explains how Archon knows the edge exists.</param>
        /// <param name="confidence">The normalized confidence assigned to the edge.</param>
        /// <param name="unknownState">The explicit unknown-state representation for the edge.</param>
        /// <param name="primaryEvidenceStableKey">The optional stable key of the primary evidence record explaining the edge.</param>
        /// <param name="metadata">Deterministic metadata for edge details that are not normalized fields.</param>
        /// <param name="fingerprint">The deterministic fingerprint for diff-relevant edge content.</param>
        /// <exception cref="ArgumentException">Thrown when the source or target stable key is missing.</exception>
        public ArchitectureEdge(
            StableKey snapshotStableKey,
            StableKey stableKey,
            EdgeKind edgeKind,
            StableKey sourceNodeStableKey,
            StableKey targetNodeStableKey,
            bool isDirect,
            KnowledgeKind knowledgeKind,
            Confidence confidence,
            UnknownState unknownState,
            StableKey? primaryEvidenceStableKey,
            GraphMetadata metadata,
            Fingerprint fingerprint)
        {
            // Edge construction validates endpoints before the fact can participate in graph traversal or persistence.
            ArgumentNullException.ThrowIfNull(edgeKind);
            ArgumentNullException.ThrowIfNull(knowledgeKind);
            ArgumentNullException.ThrowIfNull(unknownState);
            ArgumentNullException.ThrowIfNull(metadata);
            GraphFactValidation.RequireStableKey(sourceNodeStableKey, nameof(sourceNodeStableKey));
            GraphFactValidation.RequireStableKey(targetNodeStableKey, nameof(targetNodeStableKey));
            GraphFactValidation.RequireUnknownReasonWhenNeeded(knowledgeKind, unknownState, nameof(ArchitectureEdge));

            SnapshotStableKey = snapshotStableKey;
            StableKey = stableKey;
            EdgeKind = edgeKind;
            SourceNodeStableKey = sourceNodeStableKey;
            TargetNodeStableKey = targetNodeStableKey;
            IsDirect = isDirect;
            KnowledgeKind = knowledgeKind;
            Confidence = confidence;
            UnknownState = unknownState;
            PrimaryEvidenceStableKey = primaryEvidenceStableKey;
            Metadata = metadata;
            Fingerprint = fingerprint;
        }

        /// <summary>
        /// Gets the stable key of the snapshot that scopes the edge.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the deterministic stable key that identifies the edge within the snapshot contract.
        /// </summary>
        public StableKey StableKey { get; }

        /// <summary>
        /// Gets the controlled edge kind that classifies the relationship.
        /// </summary>
        public EdgeKind EdgeKind { get; }

        /// <summary>
        /// Gets the stable key of the source architecture node.
        /// </summary>
        public StableKey SourceNodeStableKey { get; }

        /// <summary>
        /// Gets the stable key of the target architecture node.
        /// </summary>
        public StableKey TargetNodeStableKey { get; }

        /// <summary>
        /// Gets a value indicating whether the edge is directly observed rather than indirectly inferred.
        /// </summary>
        public bool IsDirect { get; }

        /// <summary>
        /// Gets the knowledge classification that explains how Archon knows the edge exists.
        /// </summary>
        public KnowledgeKind KnowledgeKind { get; }

        /// <summary>
        /// Gets the normalized confidence assigned to the edge.
        /// </summary>
        public Confidence Confidence { get; }

        /// <summary>
        /// Gets the explicit unknown-state representation for the edge.
        /// </summary>
        public UnknownState UnknownState { get; }

        /// <summary>
        /// Gets the optional stable key of the primary evidence record explaining the edge.
        /// </summary>
        public StableKey? PrimaryEvidenceStableKey { get; }

        /// <summary>
        /// Gets deterministic metadata for edge details that are not normalized fields.
        /// </summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Gets the deterministic fingerprint for diff-relevant edge content.
        /// </summary>
        public Fingerprint Fingerprint { get; }
    }
}
