using Archon.Application.Cycles;
using Archon.Application.Extraction.Contracts;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Xunit;

namespace Archon.Application.Tests.Cycles
{
    /// <summary>
    /// Verifies WP013 dependency cycle detection over stable architecture graph identities.
    /// </summary>
    public sealed class DependencyCycleDetectorTests
    {
        /// <summary>
        /// Verifies project-reference and configured dependency edge cycles are detected with stable path, evidence, and identity fields.
        /// </summary>
        [Fact]
        public void DetectCycles_WhenProjectAndArchitectureCyclesExist_ShouldReturnStablePathsAndEvidence()
        {
            // The fixture combines a project cycle and a package/use cycle so the detector proves it is not limited to project references.
            StableKey snapshotStableKey = new("snapshot://cycles");
            StableKey apiProjectKey = new("project://src/Cycles.Api/Cycles.Api.csproj");
            StableKey appProjectKey = new("project://src/Cycles.Application/Cycles.Application.csproj");
            StableKey domainProjectKey = new("project://src/Cycles.Domain/Cycles.Domain.csproj");
            StableKey packageKey = new("package://Legacy.Framework");
            ExtractedArchitectureSnapshot snapshot = CreateSnapshot(
                snapshotStableKey,
                [
                    CreateProjectNode(snapshotStableKey, apiProjectKey, "Cycles.Api"),
                    CreateProjectNode(snapshotStableKey, appProjectKey, "Cycles.Application"),
                    CreateProjectNode(snapshotStableKey, domainProjectKey, "Cycles.Domain"),
                    CreatePackageNode(snapshotStableKey, packageKey, "Legacy.Framework")
                ],
                [
                    CreateEdge(snapshotStableKey, "edge://cycles/api-app", EdgeKind.References, apiProjectKey, appProjectKey, "evidence://cycles/api-app"),
                    CreateEdge(snapshotStableKey, "edge://cycles/app-domain", EdgeKind.References, appProjectKey, domainProjectKey, "evidence://cycles/app-domain"),
                    CreateEdge(snapshotStableKey, "edge://cycles/domain-api", EdgeKind.References, domainProjectKey, apiProjectKey, "evidence://cycles/domain-api"),
                    CreateEdge(snapshotStableKey, "edge://cycles/api-package", EdgeKind.UsesPackage, apiProjectKey, packageKey, "evidence://cycles/api-package"),
                    CreateEdge(snapshotStableKey, "edge://cycles/package-api", EdgeKind.DependsOn, packageKey, apiProjectKey, "evidence://cycles/package-api")
                ]);
            DependencyCycleDetector detector = new();

            CycleDetectionResult result = detector.DetectCycles(snapshot, maxDepth: 8, resultLimit: 10);

            Assert.False(result.HasTruncatedResults);
            Assert.Equal(2, result.Cycles.Count);
            CycleRecord projectCycle = Assert.Single(result.Cycles, cycle => cycle.NodeStableKeys.Count == 4);
            Assert.Equal([apiProjectKey.Value, appProjectKey.Value, domainProjectKey.Value, apiProjectKey.Value], projectCycle.NodeStableKeys.Select(static stableKey => stableKey.Value).ToArray());
            Assert.Equal(["edge://cycles/api-app", "edge://cycles/app-domain", "edge://cycles/domain-api"], projectCycle.EdgeStableKeys.Select(static stableKey => stableKey.Value).ToArray());
            Assert.Equal(["evidence://cycles/api-app", "evidence://cycles/app-domain", "evidence://cycles/domain-api"], projectCycle.EvidenceStableKeys.Select(static stableKey => stableKey.Value).ToArray());
            Assert.Equal(Confidence.Certain, projectCycle.Confidence);
            Assert.False(projectCycle.UnknownState.HasUnknownData);
            Assert.StartsWith("cycle://snapshot://cycles/", projectCycle.StableKey.Value, StringComparison.Ordinal);
            Assert.StartsWith("sha256:", projectCycle.Fingerprint.Value, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies rotating the same cycle through different traversal start nodes still produces one canonical cycle record.
        /// </summary>
        [Fact]
        public void DetectCycles_WhenSameCycleIsReachableFromEachNode_ShouldRemoveDuplicateRotations()
        {
            // A three-node directed cycle is discoverable from all three nodes, so canonicalization must collapse those rotations.
            StableKey snapshotStableKey = new("snapshot://cycle-rotation");
            StableKey firstKey = new("project://A/A.csproj");
            StableKey secondKey = new("project://B/B.csproj");
            StableKey thirdKey = new("project://C/C.csproj");
            ExtractedArchitectureSnapshot snapshot = CreateSnapshot(
                snapshotStableKey,
                [
                    CreateProjectNode(snapshotStableKey, firstKey, "A"),
                    CreateProjectNode(snapshotStableKey, secondKey, "B"),
                    CreateProjectNode(snapshotStableKey, thirdKey, "C")
                ],
                [
                    CreateEdge(snapshotStableKey, "edge://rotation/a-b", EdgeKind.References, firstKey, secondKey, null),
                    CreateEdge(snapshotStableKey, "edge://rotation/b-c", EdgeKind.References, secondKey, thirdKey, null),
                    CreateEdge(snapshotStableKey, "edge://rotation/c-a", EdgeKind.References, thirdKey, firstKey, null)
                ]);
            DependencyCycleDetector detector = new();

            CycleDetectionResult result = detector.DetectCycles(snapshot, maxDepth: 8, resultLimit: 10);

            CycleRecord cycle = Assert.Single(result.Cycles);
            Assert.Equal([firstKey.Value, secondKey.Value, thirdKey.Value, firstKey.Value], cycle.NodeStableKeys.Select(static stableKey => stableKey.Value).ToArray());
            Assert.Equal(["edge://rotation/a-b", "edge://rotation/b-c", "edge://rotation/c-a"], cycle.EdgeStableKeys.Select(static stableKey => stableKey.Value).ToArray());
        }

        /// <summary>
        /// Verifies result limits produce deterministic truncation metadata while returning the earliest canonical cycles.
        /// </summary>
        [Fact]
        public void DetectCycles_WhenResultLimitIsReached_ShouldMarkReturnedCyclesAsTruncated()
        {
            // Two independent two-node cycles with a limit of one prove ordering and truncation without depending on a large fixture.
            StableKey snapshotStableKey = new("snapshot://cycle-limit");
            StableKey firstKey = new("project://A/A.csproj");
            StableKey secondKey = new("project://B/B.csproj");
            StableKey thirdKey = new("project://C/C.csproj");
            StableKey fourthKey = new("project://D/D.csproj");
            ExtractedArchitectureSnapshot snapshot = CreateSnapshot(
                snapshotStableKey,
                [
                    CreateProjectNode(snapshotStableKey, firstKey, "A"),
                    CreateProjectNode(snapshotStableKey, secondKey, "B"),
                    CreateProjectNode(snapshotStableKey, thirdKey, "C"),
                    CreateProjectNode(snapshotStableKey, fourthKey, "D")
                ],
                [
                    CreateEdge(snapshotStableKey, "edge://limit/a-b", EdgeKind.References, firstKey, secondKey, null),
                    CreateEdge(snapshotStableKey, "edge://limit/b-a", EdgeKind.References, secondKey, firstKey, null),
                    CreateEdge(snapshotStableKey, "edge://limit/c-d", EdgeKind.References, thirdKey, fourthKey, null),
                    CreateEdge(snapshotStableKey, "edge://limit/d-c", EdgeKind.References, fourthKey, thirdKey, null)
                ]);
            DependencyCycleDetector detector = new();

            CycleDetectionResult result = detector.DetectCycles(snapshot, maxDepth: 8, resultLimit: 1);

            CycleRecord cycle = Assert.Single(result.Cycles);
            Assert.True(result.HasTruncatedResults);
            Assert.True(cycle.Truncated);
            Assert.Contains("\"resultLimit\":1", cycle.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Equal([firstKey.Value, secondKey.Value, firstKey.Value], cycle.NodeStableKeys.Select(static stableKey => stableKey.Value).ToArray());
        }

        /// <summary>
        /// Creates an extracted snapshot fixture containing only the graph sections required by cycle detection.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key that scopes all fixture graph facts.</param>
        /// <param name="nodes">The architecture nodes participating in the test graph.</param>
        /// <param name="edges">The architecture edges participating in the test graph.</param>
        /// <returns>A snapshot containing the supplied graph facts.</returns>
        private static ExtractedArchitectureSnapshot CreateSnapshot(StableKey snapshotStableKey, IReadOnlyList<ArchitectureNode> nodes, IReadOnlyList<ArchitectureEdge> edges)
        {
            // The detector only reads the header, nodes, and edges, so other snapshot sections stay empty for focused unit coverage.
            StableKey repositoryStableKey = new("repository://cycles");
            SnapshotHeader header = new(
                snapshotStableKey,
                repositoryStableKey,
                "main",
                "abcdef",
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 20, 8, 1, 0, TimeSpan.Zero),
                "wp013-cycle-tests",
                "Completed",
                warnings: [],
                errors: [],
                GraphMetadata.Empty);
            RepositoryModel repository = new(repositoryStableKey, "Cycles", "D:/Repositories/Cycles", null, "main", GraphMetadata.Empty);
            return new ExtractedArchitectureSnapshot(header, [repository], [], nodes, edges, [], [], [], [], [], [], []);
        }

        /// <summary>
        /// Creates a deterministic project node for cycle detector fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the node.</param>
        /// <param name="nodeStableKey">The stable key that identifies the project node.</param>
        /// <param name="displayName">The project display name.</param>
        /// <returns>An architecture node suitable for cycle detection fixtures.</returns>
        private static ArchitectureNode CreateProjectNode(StableKey snapshotStableKey, StableKey nodeStableKey, string displayName)
        {
            // Project cycle tests use project nodes because project references are the primary dependency cycle source.
            return new ArchitectureNode(
                snapshotStableKey,
                nodeStableKey,
                NodeKind.Project,
                displayName,
                displayName,
                displayName.ToLowerInvariant(),
                "C#",
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey: null,
                GraphMetadata.Empty,
                FingerprintGenerator.ForNode(NodeKind.Project, displayName, displayName, displayName.ToLowerInvariant(), KnowledgeKind.Fact, GraphMetadata.Empty));
        }

        /// <summary>
        /// Creates a deterministic package node for configured non-project dependency cycle fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the package node.</param>
        /// <param name="nodeStableKey">The stable key that identifies the package node.</param>
        /// <param name="displayName">The package display name.</param>
        /// <returns>An architecture node suitable for cycle detection fixtures.</returns>
        private static ArchitectureNode CreatePackageNode(StableKey snapshotStableKey, StableKey nodeStableKey, string displayName)
        {
            // Package nodes prove configured dependency cycles can include non-project architecture identities.
            return new ArchitectureNode(
                snapshotStableKey,
                nodeStableKey,
                NodeKind.Package,
                displayName,
                displayName,
                displayName.ToLowerInvariant(),
                language: null,
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey: null,
                GraphMetadata.Empty,
                FingerprintGenerator.ForNode(NodeKind.Package, displayName, displayName, displayName.ToLowerInvariant(), KnowledgeKind.Fact, GraphMetadata.Empty));
        }

        /// <summary>
        /// Creates a deterministic architecture edge for cycle detector fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the edge.</param>
        /// <param name="edgeStableKey">The stable key that identifies the edge.</param>
        /// <param name="edgeKind">The dependency edge kind to assign.</param>
        /// <param name="sourceNodeStableKey">The source node stable key.</param>
        /// <param name="targetNodeStableKey">The target node stable key.</param>
        /// <param name="evidenceStableKey">The optional evidence stable key explaining the edge.</param>
        /// <returns>An architecture edge suitable for cycle detection fixtures.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, string edgeStableKey, EdgeKind edgeKind, StableKey sourceNodeStableKey, StableKey targetNodeStableKey, string? evidenceStableKey)
        {
            // Edge fixtures populate primary evidence when requested so detector evidence propagation can be asserted.
            StableKey? evidence = string.IsNullOrWhiteSpace(evidenceStableKey) ? null : new StableKey(evidenceStableKey);
            return new ArchitectureEdge(
                snapshotStableKey,
                new StableKey(edgeStableKey),
                edgeKind,
                sourceNodeStableKey,
                targetNodeStableKey,
                isDirect: true,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                evidence,
                GraphMetadata.Empty,
                FingerprintGenerator.ForEdge(edgeKind, sourceNodeStableKey, targetNodeStableKey, true, KnowledgeKind.Fact, GraphMetadata.Empty));
        }
    }
}
