using Archon.Application.Extraction.Contracts;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Cycles
{
    /// <summary>
    /// Detects deterministic dependency cycles from accumulated architecture nodes and dependency edges.
    /// </summary>
    public sealed class DependencyCycleDetector
    {
        /// <summary>
        /// Stores the default maximum cycle path length explored by detection.
        /// </summary>
        public const int DefaultMaxDepth = 12;

        /// <summary>
        /// Stores the default maximum number of canonical cycle records returned to callers.
        /// </summary>
        public const int DefaultResultLimit = 100;

        /// <summary>
        /// Stores the dependency edge kinds that participate in cycle detection.
        /// </summary>
        private static readonly HashSet<EdgeKind> s_dependencyEdgeKinds =
        [
            EdgeKind.References,
            EdgeKind.Calls,
            EdgeKind.Implements,
            EdgeKind.Inherits,
            EdgeKind.Injects,
            EdgeKind.Exposes,
            EdgeKind.Handles,
            EdgeKind.UsesConfig,
            EdgeKind.UsesDbContext,
            EdgeKind.UsesLinqToSqlContext,
            EdgeKind.MapsEntity,
            EdgeKind.MapsTable,
            EdgeKind.MapsColumn,
            EdgeKind.ReadsTable,
            EdgeKind.WritesTable,
            EdgeKind.CallsStoredProcedure,
            EdgeKind.ExecutesRawSql,
            EdgeKind.CallsExternalService,
            EdgeKind.UsesPackage,
            EdgeKind.DeclaresEndpoint,
            EdgeKind.DeclaresComponent,
            EdgeKind.DeclaresUiRoute,
            EdgeKind.UsesComponent,
            EdgeKind.UsesLayout,
            EdgeKind.UsesControl,
            EdgeKind.UsesUiResource,
            EdgeKind.UsesStyle,
            EdgeKind.BindsTo,
            EdgeKind.UsesCommand,
            EdgeKind.UsesViewModel,
            EdgeKind.NavigatesTo,
            EdgeKind.HandlesUiEvent,
            EdgeKind.CallsApi,
            EdgeKind.RegisteredAsService,
            EdgeKind.DependsOn
        ];

        /// <summary>
        /// Detects canonical dependency cycles in one extracted architecture snapshot.
        /// </summary>
        /// <param name="snapshot">The extracted snapshot whose nodes and dependency edges should be inspected.</param>
        /// <param name="maxDepth">The optional maximum cycle path length to traverse.</param>
        /// <param name="resultLimit">The optional maximum number of canonical cycles to return.</param>
        /// <returns>A deterministic bounded cycle detection result.</returns>
        public CycleDetectionResult DetectCycles(ExtractedArchitectureSnapshot snapshot, int? maxDepth = null, int? resultLimit = null)
        {
            // Detection reads already accumulated graph facts; it never rescans source files or queries persistence directly.
            ArgumentNullException.ThrowIfNull(snapshot);
            StableKey snapshotStableKey = snapshot.SnapshotHeader?.StableKey ?? new StableKey("snapshot://unknown");
            int boundedDepth = Math.Clamp(maxDepth ?? DefaultMaxDepth, 2, 64);
            int boundedLimit = Math.Clamp(resultLimit ?? DefaultResultLimit, 1, 500);
            CycleGraph graph = CycleGraph.FromSnapshot(snapshot);
            Dictionary<string, CycleCandidate> candidates = new(StringComparer.Ordinal);
            foreach (StableKey startNode in graph.NodeStableKeys)
            {
                // Depth-first traversal is bounded and uses stable edge ordering so equivalent graphs produce equivalent candidate paths.
                TraverseFromStart(graph, startNode, startNode, [], [], boundedDepth, candidates);
            }

            CycleCandidate[] orderedCandidates = candidates.Values
                .OrderBy(static candidate => candidate.CanonicalNodePathKey, StringComparer.Ordinal)
                .ThenBy(static candidate => candidate.CanonicalEdgePathKey, StringComparer.Ordinal)
                .ToArray();
            bool truncated = orderedCandidates.Length > boundedLimit;
            CycleRecord[] records = orderedCandidates
                .Take(boundedLimit)
                .Select(candidate => CreateCycleRecord(snapshotStableKey, candidate, boundedDepth, boundedLimit, truncated))
                .ToArray();
            return new CycleDetectionResult(records, orderedCandidates.Length, boundedLimit, truncated);
        }

        /// <summary>
        /// Counts how many returned canonical cycles each node participates in.
        /// </summary>
        /// <param name="result">The cycle detection result to inspect.</param>
        /// <returns>A dictionary keyed by node stable key with participation counts.</returns>
        public static IReadOnlyDictionary<StableKey, int> CountParticipation(CycleDetectionResult result)
        {
            // Participation ignores the repeated closing node so each unique node contributes once per returned canonical cycle.
            ArgumentNullException.ThrowIfNull(result);
            Dictionary<StableKey, int> counts = [];
            foreach (CycleRecord cycle in result.Cycles)
            {
                foreach (StableKey nodeStableKey in cycle.NodeStableKeys.Take(cycle.NodeStableKeys.Count - 1).Distinct())
                {
                    counts[nodeStableKey] = counts.TryGetValue(nodeStableKey, out int count) ? count + 1 : 1;
                }
            }

            return counts;
        }

        /// <summary>
        /// Traverses deterministic outbound edges from a start node and records canonical cycles when traversal returns to the start.
        /// </summary>
        /// <param name="graph">The normalized dependency graph.</param>
        /// <param name="startNode">The stable key where this traversal started.</param>
        /// <param name="currentNode">The stable key currently being expanded.</param>
        /// <param name="pathNodes">The path nodes visited after the start node.</param>
        /// <param name="pathEdges">The path edges used to reach the current node.</param>
        /// <param name="maxDepth">The maximum permitted cycle hop count.</param>
        /// <param name="candidates">The canonical candidate dictionary being populated.</param>
        private static void TraverseFromStart(CycleGraph graph, StableKey startNode, StableKey currentNode, IReadOnlyList<StableKey> pathNodes, IReadOnlyList<ArchitectureEdge> pathEdges, int maxDepth, Dictionary<string, CycleCandidate> candidates)
        {
            // The path is copied on expansion to keep recursion side-effect free and easy to reason about.
            if (pathEdges.Count >= maxDepth)
            {
                return;
            }

            foreach (ArchitectureEdge edge in graph.GetOutgoingEdges(currentNode))
            {
                if (edge.TargetNodeStableKey == startNode && pathEdges.Count > 0)
                {
                    StableKey[] cycleNodes = [startNode, .. pathNodes, startNode];
                    ArchitectureEdge[] cycleEdges = [.. pathEdges, edge];
                    CycleCandidate candidate = CycleCandidate.From(cycleNodes, cycleEdges);
                    candidates.TryAdd(candidate.CanonicalKey, candidate);
                    continue;
                }

                if (pathNodes.Contains(edge.TargetNodeStableKey) || edge.TargetNodeStableKey.Value.CompareTo(startNode.Value, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                TraverseFromStart(
                    graph,
                    startNode,
                    edge.TargetNodeStableKey,
                    [.. pathNodes, edge.TargetNodeStableKey],
                    [.. pathEdges, edge],
                    maxDepth,
                    candidates);
            }
        }

        /// <summary>
        /// Creates one public cycle record from a canonical cycle candidate.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the cycle.</param>
        /// <param name="candidate">The canonical cycle candidate.</param>
        /// <param name="maxDepth">The traversal depth limit used by detection.</param>
        /// <param name="resultLimit">The result limit used by detection.</param>
        /// <param name="truncated">A value indicating whether the overall result set was truncated.</param>
        /// <returns>A deterministic cycle record.</returns>
        private static CycleRecord CreateCycleRecord(StableKey snapshotStableKey, CycleCandidate candidate, int maxDepth, int resultLimit, bool truncated)
        {
            // Stable keys and fingerprints are derived from canonical path strings, not from traversal start node, so rotations compare equal.
            StableKey stableKey = new($"cycle://{snapshotStableKey.Value}/{candidate.CanonicalHash.Value[7..]}");
            StableKey[] evidenceStableKeys = candidate.Edges
                .Select(static edge => edge.PrimaryEvidenceStableKey)
                .Where(static stableKey => stableKey.HasValue)
                .Select(static stableKey => stableKey!.Value)
                .Distinct()
                .OrderBy(static stableKey => stableKey.Value, StringComparer.Ordinal)
                .ToArray();
            decimal confidenceValue = candidate.Edges.Count == 0 ? 1m : candidate.Edges.Min(static edge => edge.Confidence.Value);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["calculationSource"] = "accumulatedDependencyGraph",
                ["canonicalNodePath"] = candidate.CanonicalNodePathKey,
                ["canonicalEdgePath"] = candidate.CanonicalEdgePathKey,
                ["canonicalization"] = "rotation-minimum-node-path",
                ["dependencyEdgeKinds"] = s_dependencyEdgeKinds.Select(static edgeKind => edgeKind.Value).OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
                ["maxDepth"] = maxDepth,
                ["resultLimit"] = resultLimit,
                ["truncated"] = truncated
            });
            Fingerprint fingerprint = FingerprintGenerator.FromInput(FingerprintInput.Create("Cycle")
                .AddField("nodePath", candidate.CanonicalNodePathKey)
                .AddField("edgePath", candidate.CanonicalEdgePathKey)
                .AddField("evidence", string.Join("|", evidenceStableKeys.Select(static stableKey => stableKey.Value)))
                .AddField("truncated", truncated)
                .AddMetadata(metadata));
            return new CycleRecord(
                snapshotStableKey,
                stableKey,
                candidate.Nodes,
                candidate.Edges.Select(static edge => edge.StableKey).ToArray(),
                evidenceStableKeys,
                new Confidence(confidenceValue),
                UnknownState.Known,
                truncated,
                metadata,
                fingerprint);
        }

        /// <summary>
        /// Carries a normalized dependency graph for cycle traversal.
        /// </summary>
        /// <param name="NodeStableKeys">All known node stable keys in deterministic order.</param>
        /// <param name="OutgoingEdgesBySource">Dependency edges grouped by source node stable key.</param>
        private sealed record CycleGraph(IReadOnlyList<StableKey> NodeStableKeys, IReadOnlyDictionary<StableKey, IReadOnlyList<ArchitectureEdge>> OutgoingEdgesBySource)
        {
            /// <summary>
            /// Creates the normalized dependency graph from an extracted snapshot.
            /// </summary>
            /// <param name="snapshot">The snapshot whose graph facts should be normalized.</param>
            /// <returns>A deterministic cycle graph.</returns>
            internal static CycleGraph FromSnapshot(ExtractedArchitectureSnapshot snapshot)
            {
                // Only dependency edge kinds are included; containment and support relationships do not represent architecture cycles.
                HashSet<StableKey> nodeKeys = snapshot.Nodes.Select(static node => node.StableKey).ToHashSet();
                ArchitectureEdge[] dependencyEdges = snapshot.Edges
                    .Where(static edge => s_dependencyEdgeKinds.Contains(edge.EdgeKind))
                    .Where(edge => nodeKeys.Contains(edge.SourceNodeStableKey) && nodeKeys.Contains(edge.TargetNodeStableKey))
                    .OrderBy(static edge => edge.SourceNodeStableKey.Value, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.TargetNodeStableKey.Value, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.EdgeKind.Value, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.StableKey.Value, StringComparer.Ordinal)
                    .ToArray();
                Dictionary<StableKey, IReadOnlyList<ArchitectureEdge>> outgoing = dependencyEdges
                    .GroupBy(static edge => edge.SourceNodeStableKey)
                    .ToDictionary(static group => group.Key, static group => (IReadOnlyList<ArchitectureEdge>)group.ToArray());
                return new CycleGraph(nodeKeys.OrderBy(static stableKey => stableKey.Value, StringComparer.Ordinal).ToArray(), outgoing);
            }

            /// <summary>
            /// Gets deterministic outgoing dependency edges for one source node.
            /// </summary>
            /// <param name="sourceNodeStableKey">The source node stable key to expand.</param>
            /// <returns>The source node's outgoing dependency edges, or an empty list.</returns>
            internal IReadOnlyList<ArchitectureEdge> GetOutgoingEdges(StableKey sourceNodeStableKey)
            {
                // The graph stores edge groups in stable order during construction, so traversal can enumerate directly.
                return OutgoingEdgesBySource.TryGetValue(sourceNodeStableKey, out IReadOnlyList<ArchitectureEdge>? edges) ? edges : [];
            }
        }

        /// <summary>
        /// Carries a canonical dependency cycle candidate before it is projected into a public record.
        /// </summary>
        /// <param name="Nodes">The canonical closed node path.</param>
        /// <param name="Edges">The canonical edge path matching the node hops.</param>
        /// <param name="CanonicalKey">The duplicate-removal key for this canonical cycle.</param>
        /// <param name="CanonicalNodePathKey">The string form of the canonical node path.</param>
        /// <param name="CanonicalEdgePathKey">The string form of the canonical edge path.</param>
        /// <param name="CanonicalHash">The deterministic hash derived from canonical path strings.</param>
        private sealed record CycleCandidate(IReadOnlyList<StableKey> Nodes, IReadOnlyList<ArchitectureEdge> Edges, string CanonicalKey, string CanonicalNodePathKey, string CanonicalEdgePathKey, Fingerprint CanonicalHash)
        {
            /// <summary>
            /// Creates a canonical candidate by rotating a closed cycle to its deterministic minimum representation.
            /// </summary>
            /// <param name="closedNodes">The closed cycle node path from traversal.</param>
            /// <param name="edges">The edge path from traversal.</param>
            /// <returns>A canonical cycle candidate.</returns>
            internal static CycleCandidate From(IReadOnlyList<StableKey> closedNodes, IReadOnlyList<ArchitectureEdge> edges)
            {
                // The closing node is omitted during rotation and restored afterward so every rotation of the same cycle shares one key.
                StableKey[] openNodes = closedNodes.Take(closedNodes.Count - 1).ToArray();
                int rotationStart = Enumerable.Range(0, openNodes.Length)
                    .OrderBy(index => openNodes[index].Value, StringComparer.Ordinal)
                    .ThenBy(index => BuildRotatedPathKey(openNodes, edges, index), StringComparer.Ordinal)
                    .First();
                StableKey[] canonicalOpenNodes = Rotate(openNodes, rotationStart);
                ArchitectureEdge[] canonicalEdges = Rotate(edges.ToArray(), rotationStart);
                StableKey[] canonicalClosedNodes = [.. canonicalOpenNodes, canonicalOpenNodes[0]];
                string nodePathKey = string.Join("|", canonicalClosedNodes.Select(static stableKey => stableKey.Value));
                string edgePathKey = string.Join("|", canonicalEdges.Select(static edge => edge.StableKey.Value));
                string canonicalKey = string.Concat(nodePathKey, "::", edgePathKey);
                Fingerprint hash = FingerprintGenerator.FromInput(FingerprintInput.Create("CycleIdentity")
                    .AddField("nodePath", nodePathKey)
                    .AddField("edgePath", edgePathKey));
                return new CycleCandidate(canonicalClosedNodes, canonicalEdges, canonicalKey, nodePathKey, edgePathKey, hash);
            }

            /// <summary>
            /// Builds a sortable key for one possible cycle rotation.
            /// </summary>
            /// <param name="nodes">The open node path to rotate.</param>
            /// <param name="edges">The edge path to rotate with the nodes.</param>
            /// <param name="startIndex">The candidate rotation start index.</param>
            /// <returns>A stable sortable path key.</returns>
            private static string BuildRotatedPathKey(IReadOnlyList<StableKey> nodes, IReadOnlyList<ArchitectureEdge> edges, int startIndex)
            {
                // Including edge keys breaks ties when parallel edges connect the same node path.
                StableKey[] rotatedNodes = Rotate(nodes.ToArray(), startIndex);
                ArchitectureEdge[] rotatedEdges = Rotate(edges.ToArray(), startIndex);
                return string.Concat(
                    string.Join("|", rotatedNodes.Select(static stableKey => stableKey.Value)),
                    "::",
                    string.Join("|", rotatedEdges.Select(static edge => edge.StableKey.Value)));
            }

            /// <summary>
            /// Rotates an array by a deterministic start index.
            /// </summary>
            /// <typeparam name="TItem">The item type being rotated.</typeparam>
            /// <param name="items">The item array to rotate.</param>
            /// <param name="startIndex">The zero-based item index that should become first.</param>
            /// <returns>The rotated item array.</returns>
            private static TItem[] Rotate<TItem>(TItem[] items, int startIndex)
            {
                // Cycle rotation wraps around the end of the path while preserving the relative hop order.
                TItem[] rotated = new TItem[items.Length];
                for (int offset = 0; offset < items.Length; offset++)
                {
                    rotated[offset] = items[(startIndex + offset) % items.Length];
                }

                return rotated;
            }
        }
    }
}
