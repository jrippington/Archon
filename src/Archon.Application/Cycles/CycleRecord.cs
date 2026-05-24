using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Cycles
{
    /// <summary>
    /// Represents one deterministic dependency cycle detected from stable architecture graph identities.
    /// </summary>
    public sealed class CycleRecord
    {
        /// <summary>
        /// Initializes a validated dependency cycle record.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the detected cycle.</param>
        /// <param name="stableKey">The deterministic stable key identifying this canonical cycle within the snapshot.</param>
        /// <param name="nodeStableKeys">The cycle node path in order, including the repeated first node as the final path item.</param>
        /// <param name="edgeStableKeys">The stable edge keys in the same traversal order as the cycle path hops.</param>
        /// <param name="evidenceStableKeys">The stable evidence keys contributed by cycle edges, in deterministic path order without duplicates.</param>
        /// <param name="confidence">The confidence assigned to the detected cycle from its contributing edges.</param>
        /// <param name="unknownState">The explicit unknown-state information for bounded or incomplete detection.</param>
        /// <param name="truncated">A value indicating whether result limits made this cycle response part of a truncated result set.</param>
        /// <param name="metadata">Deterministic metadata describing canonicalization, traversal, and truncation behavior.</param>
        /// <param name="fingerprint">The deterministic fingerprint for diff-relevant cycle content.</param>
        public CycleRecord(
            StableKey snapshotStableKey,
            StableKey stableKey,
            IReadOnlyList<StableKey> nodeStableKeys,
            IReadOnlyList<StableKey> edgeStableKeys,
            IReadOnlyList<StableKey> evidenceStableKeys,
            Confidence confidence,
            UnknownState unknownState,
            bool truncated,
            GraphMetadata metadata,
            Fingerprint fingerprint)
        {
            // A valid directed cycle must contain at least two unique hops and must close by repeating its first node.
            ArgumentNullException.ThrowIfNull(nodeStableKeys);
            ArgumentNullException.ThrowIfNull(edgeStableKeys);
            ArgumentNullException.ThrowIfNull(evidenceStableKeys);
            ArgumentNullException.ThrowIfNull(metadata);
            if (nodeStableKeys.Count < 3)
            {
                throw new ArgumentException("Cycle records require at least two nodes plus the repeated starting node.", nameof(nodeStableKeys));
            }

            if (edgeStableKeys.Count != nodeStableKeys.Count - 1)
            {
                throw new ArgumentException("Cycle records require exactly one edge stable key per cycle hop.", nameof(edgeStableKeys));
            }

            if (nodeStableKeys[0] != nodeStableKeys[^1])
            {
                throw new ArgumentException("Cycle node paths must close by repeating the first node as the final node.", nameof(nodeStableKeys));
            }

            SnapshotStableKey = snapshotStableKey;
            StableKey = stableKey;
            NodeStableKeys = nodeStableKeys.ToArray();
            EdgeStableKeys = edgeStableKeys.ToArray();
            EvidenceStableKeys = evidenceStableKeys.ToArray();
            Confidence = confidence;
            UnknownState = unknownState;
            Truncated = truncated;
            Metadata = metadata;
            Fingerprint = fingerprint;
        }

        /// <summary>
        /// Gets the stable key of the snapshot that owns the detected cycle.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the deterministic stable key identifying this canonical cycle within the snapshot.
        /// </summary>
        public StableKey StableKey { get; }

        /// <summary>
        /// Gets the cycle node path in order, including the repeated first node as the final path item.
        /// </summary>
        public IReadOnlyList<StableKey> NodeStableKeys { get; }

        /// <summary>
        /// Gets the stable edge keys in the same traversal order as the cycle path hops.
        /// </summary>
        public IReadOnlyList<StableKey> EdgeStableKeys { get; }

        /// <summary>
        /// Gets the stable evidence keys contributed by cycle edges, in deterministic path order without duplicates.
        /// </summary>
        public IReadOnlyList<StableKey> EvidenceStableKeys { get; }

        /// <summary>
        /// Gets the confidence assigned to the detected cycle from its contributing edges.
        /// </summary>
        public Confidence Confidence { get; }

        /// <summary>
        /// Gets the explicit unknown-state information for bounded or incomplete detection.
        /// </summary>
        public UnknownState UnknownState { get; }

        /// <summary>
        /// Gets a value indicating whether result limits made this cycle response part of a truncated result set.
        /// </summary>
        public bool Truncated { get; }

        /// <summary>
        /// Gets deterministic metadata describing canonicalization, traversal, and truncation behavior.
        /// </summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Gets the deterministic fingerprint for diff-relevant cycle content.
        /// </summary>
        public Fingerprint Fingerprint { get; }
    }
}
