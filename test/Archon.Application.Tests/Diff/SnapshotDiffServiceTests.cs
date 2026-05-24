using Archon.Application.Diff;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Xunit;

namespace Archon.Application.Tests.Diff
{
    /// <summary>
    /// Verifies snapshot diff comparison across nodes, edges, findings, and metrics using stable keys and fingerprints.
    /// </summary>
    public sealed class SnapshotDiffServiceTests
    {
        /// <summary>
        /// Confirms every diff domain classifies added, removed, changed, and unchanged records deterministically.
        /// </summary>
        /// <returns>A task that completes after the diff result is asserted.</returns>
        [Fact]
        public async Task CompareSnapshotsAsync_WhenSnapshotsContainAllChangeKinds_ShouldClassifyEveryDomain()
        {
            // The fixture uses identical stable keys with changed fingerprints to prove comparison is content-based and not object-reference based.
            InMemoryArchitectureSnapshotWriter writer = new();
            SnapshotDiffService service = new(writer);
            StableKey repositoryStableKey = new("repository://diff");
            StableKey previousSnapshot = new("snapshot://diff/previous");
            StableKey currentSnapshot = new("snapshot://diff/current");
            StableKey unchangedNode = new("project://src/Diff.Unchanged/Diff.Unchanged.csproj");
            StableKey changedNode = new("project://src/Diff.Changed/Diff.Changed.csproj");
            StableKey removedNode = new("project://src/Diff.Removed/Diff.Removed.csproj");
            StableKey addedNode = new("project://src/Diff.Added/Diff.Added.csproj");
            StableKey edgeSource = new("project://src/Diff.Source/Diff.Source.csproj");
            StableKey edgeTarget = new("project://src/Diff.Target/Diff.Target.csproj");
            ArchitectureNode previousChangedNode = CreateNode(previousSnapshot, changedNode, "Changed previous", "sha256:node-old", UnknownState.Known, "evidence://node-changed-old");
            ArchitectureNode currentChangedNode = CreateNode(currentSnapshot, changedNode, "Changed current", "sha256:node-new", UnknownState.Unknown("Node metadata was partially unavailable."), "evidence://node-changed-new");
            ArchitectureEdge previousChangedEdge = CreateEdge(previousSnapshot, "edge://changed", edgeSource, edgeTarget, "sha256:edge-old", UnknownState.Known, "evidence://edge-old");
            ArchitectureEdge currentChangedEdge = CreateEdge(currentSnapshot, "edge://changed", edgeSource, edgeTarget, "sha256:edge-new", UnknownState.Unknown("Edge evidence was partially unavailable."), "evidence://edge-new");
            FindingRecord previousChangedFinding = CreateFinding(previousSnapshot, "finding://changed", "sha256:finding-old", UnknownState.Known, "evidence://finding-old");
            FindingRecord currentChangedFinding = CreateFinding(currentSnapshot, "finding://changed", "sha256:finding-new", UnknownState.Unknown("Finding evidence was partially unavailable."), "evidence://finding-new");
            MetricRecord previousChangedMetric = CreateMetric(previousSnapshot, "metric://changed", "GraphFanIn", 2, "sha256:metric-old", UnknownState.Known, "evidence://metric-old");
            MetricRecord currentChangedMetric = CreateMetric(currentSnapshot, "metric://changed", "GraphFanIn", 5, "sha256:metric-new", UnknownState.Unknown("Metric input was partially unavailable."), "evidence://metric-new");
            await writer.WriteSnapshotAsync(CreateSnapshot(
                previousSnapshot,
                repositoryStableKey,
                [
                    CreateNode(previousSnapshot, unchangedNode, "Unchanged", "sha256:node-same", UnknownState.Known, "evidence://node-same"),
                    previousChangedNode,
                    CreateNode(previousSnapshot, removedNode, "Removed", "sha256:node-removed", UnknownState.Known, "evidence://node-removed")
                ],
                [
                    CreateEdge(previousSnapshot, "edge://unchanged", edgeSource, edgeTarget, "sha256:edge-same", UnknownState.Known, "evidence://edge-same"),
                    previousChangedEdge,
                    CreateEdge(previousSnapshot, "edge://removed", edgeSource, removedNode, "sha256:edge-removed", UnknownState.Known, "evidence://edge-removed")
                ],
                [
                    CreateFinding(previousSnapshot, "finding://unchanged", "sha256:finding-same", UnknownState.Known, "evidence://finding-same"),
                    previousChangedFinding,
                    CreateFinding(previousSnapshot, "finding://removed", "sha256:finding-removed", UnknownState.Known, "evidence://finding-removed")
                ],
                [
                    CreateMetric(previousSnapshot, "metric://unchanged", "SnapshotNodeCount", 3, "sha256:metric-same", UnknownState.Known, "evidence://metric-same"),
                    previousChangedMetric,
                    CreateMetric(previousSnapshot, "metric://removed", "GraphFanOut", 7, "sha256:metric-removed", UnknownState.Known, "evidence://metric-removed")
                ]), CancellationToken.None);
            await writer.WriteSnapshotAsync(CreateSnapshot(
                currentSnapshot,
                repositoryStableKey,
                [
                    CreateNode(currentSnapshot, unchangedNode, "Unchanged", "sha256:node-same", UnknownState.Known, "evidence://node-same"),
                    currentChangedNode,
                    CreateNode(currentSnapshot, addedNode, "Added", "sha256:node-added", UnknownState.Known, "evidence://node-added")
                ],
                [
                    CreateEdge(currentSnapshot, "edge://unchanged", edgeSource, edgeTarget, "sha256:edge-same", UnknownState.Known, "evidence://edge-same"),
                    currentChangedEdge,
                    CreateEdge(currentSnapshot, "edge://added", addedNode, edgeTarget, "sha256:edge-added", UnknownState.Known, "evidence://edge-added")
                ],
                [
                    CreateFinding(currentSnapshot, "finding://unchanged", "sha256:finding-same", UnknownState.Known, "evidence://finding-same"),
                    currentChangedFinding,
                    CreateFinding(currentSnapshot, "finding://added", "sha256:finding-added", UnknownState.Known, "evidence://finding-added")
                ],
                [
                    CreateMetric(currentSnapshot, "metric://unchanged", "SnapshotNodeCount", 3, "sha256:metric-same", UnknownState.Known, "evidence://metric-same"),
                    currentChangedMetric,
                    CreateMetric(currentSnapshot, "metric://added", "GraphFanOut", 9, "sha256:metric-added", UnknownState.Known, "evidence://metric-added")
                ]), CancellationToken.None);

            SnapshotDiffResult result = await service.CompareSnapshotsAsync(new SnapshotDiffQuery(currentSnapshot.Value, previousSnapshot.Value, null, null, includeUnchangedDetails: true, take: 100), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(currentSnapshot.Value, result.CurrentSnapshotStableKey);
            Assert.Equal(previousSnapshot.Value, result.PreviousSnapshotStableKey);
            Assert.False(result.Truncation.Truncated);
            Assert.All(result.Summaries, summary =>
            {
                Assert.Equal(1, summary.AddedCount);
                Assert.Equal(1, summary.RemovedCount);
                Assert.Equal(1, summary.ChangedCount);
                Assert.Equal(1, summary.UnchangedCount);
            });
            SnapshotDiffItemDto changedNodeItem = Assert.Single(result.Items, item => item.Domain == SnapshotDiffDomains.Nodes && item.ChangeKind == SnapshotDiffChangeKind.Changed);
            Assert.Equal(changedNode.Value, changedNodeItem.StableKey);
            Assert.Equal("sha256:node-old", changedNodeItem.PreviousFingerprint);
            Assert.Equal("sha256:node-new", changedNodeItem.CurrentFingerprint);
            Assert.Contains("fingerprint", changedNodeItem.ChangedFields);
            Assert.Contains("displayName", changedNodeItem.ChangedFields);
            Assert.True(changedNodeItem.HasUnknownData);
            Assert.Contains("Node metadata", changedNodeItem.UnknownReason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("evidence://node-changed-new", Assert.Single(changedNodeItem.EvidenceStableKeys));
            Assert.Single(result.Items, item => item.Domain == SnapshotDiffDomains.Edges && item.ChangeKind == SnapshotDiffChangeKind.Added);
            Assert.Single(result.Items, item => item.Domain == SnapshotDiffDomains.Findings && item.ChangeKind == SnapshotDiffChangeKind.Removed);
            Assert.Single(result.Items, item => item.Domain == SnapshotDiffDomains.Metrics && item.ChangeKind == SnapshotDiffChangeKind.Unchanged);
        }

        /// <summary>
        /// Confirms unchanged record counts are preserved while unchanged details are omitted unless explicitly requested.
        /// </summary>
        /// <returns>A task that completes after unchanged-detail behavior is asserted.</returns>
        [Fact]
        public async Task CompareSnapshotsAsync_WhenUnchangedDetailsAreNotRequested_ShouldReturnCountsWithoutUnchangedItems()
        {
            // The summary still reports unchanged records because dashboards need counts even when detailed unchanged rows are too large.
            InMemoryArchitectureSnapshotWriter writer = new();
            SnapshotDiffService service = new(writer);
            StableKey repositoryStableKey = new("repository://diff-unchanged");
            StableKey previousSnapshot = new("snapshot://diff-unchanged/previous");
            StableKey currentSnapshot = new("snapshot://diff-unchanged/current");
            StableKey nodeStableKey = new("project://src/Diff.Unchanged/Diff.Unchanged.csproj");
            await writer.WriteSnapshotAsync(CreateSnapshot(previousSnapshot, repositoryStableKey, [CreateNode(previousSnapshot, nodeStableKey, "Unchanged", "sha256:node-same", UnknownState.Known, null)], [], [], []), CancellationToken.None);
            await writer.WriteSnapshotAsync(CreateSnapshot(currentSnapshot, repositoryStableKey, [CreateNode(currentSnapshot, nodeStableKey, "Unchanged", "sha256:node-same", UnknownState.Known, null)], [], [], []), CancellationToken.None);

            SnapshotDiffResult result = await service.CompareSnapshotsAsync(new SnapshotDiffQuery(currentSnapshot.Value, previousSnapshot.Value, null, null, includeUnchangedDetails: false, take: 100), CancellationToken.None);

            Assert.True(result.Succeeded);
            SnapshotDiffSummaryDto nodeSummary = Assert.Single(result.Summaries, summary => summary.Domain == SnapshotDiffDomains.Nodes);
            Assert.Equal(1, nodeSummary.UnchangedCount);
            Assert.DoesNotContain(result.Items, item => item.ChangeKind == SnapshotDiffChangeKind.Unchanged);
        }

        /// <summary>
        /// Confirms domain and change-kind filters constrain returned detail rows without changing domain summary counts.
        /// </summary>
        /// <returns>A task that completes after filter behavior is asserted.</returns>
        [Fact]
        public async Task CompareSnapshotsAsync_WhenFiltersAreSupplied_ShouldFilterDetailRows()
        {
            // Summary counts describe the full comparison, while item filters select the detail rows a caller wants to inspect.
            InMemoryArchitectureSnapshotWriter writer = new();
            SnapshotDiffService service = new(writer);
            StableKey repositoryStableKey = new("repository://diff-filters");
            StableKey previousSnapshot = new("snapshot://diff-filters/previous");
            StableKey currentSnapshot = new("snapshot://diff-filters/current");
            StableKey removedNode = new("project://src/Diff.Removed/Diff.Removed.csproj");
            StableKey addedNode = new("project://src/Diff.Added/Diff.Added.csproj");
            await writer.WriteSnapshotAsync(CreateSnapshot(previousSnapshot, repositoryStableKey, [CreateNode(previousSnapshot, removedNode, "Removed", "sha256:node-removed", UnknownState.Known, null)], [], [], []), CancellationToken.None);
            await writer.WriteSnapshotAsync(CreateSnapshot(currentSnapshot, repositoryStableKey, [CreateNode(currentSnapshot, addedNode, "Added", "sha256:node-added", UnknownState.Known, null)], [], [], []), CancellationToken.None);

            SnapshotDiffResult result = await service.CompareSnapshotsAsync(new SnapshotDiffQuery(currentSnapshot.Value, previousSnapshot.Value, [SnapshotDiffDomains.Nodes], [SnapshotDiffChangeKind.Added], includeUnchangedDetails: true, take: 100), CancellationToken.None);

            Assert.True(result.Succeeded);
            SnapshotDiffItemDto item = Assert.Single(result.Items);
            Assert.Equal(SnapshotDiffDomains.Nodes, item.Domain);
            Assert.Equal(SnapshotDiffChangeKind.Added, item.ChangeKind);
            Assert.Equal(addedNode.Value, item.StableKey);
            SnapshotDiffSummaryDto nodeSummary = Assert.Single(result.Summaries, summary => summary.Domain == SnapshotDiffDomains.Nodes);
            Assert.Equal(1, nodeSummary.AddedCount);
            Assert.Equal(1, nodeSummary.RemovedCount);
        }

        /// <summary>
        /// Confirms invalid snapshot identities and incompatible repositories return deterministic validation errors.
        /// </summary>
        /// <returns>A task that completes after validation errors are asserted.</returns>
        [Fact]
        public async Task CompareSnapshotsAsync_WhenSnapshotsAreMissingOrIncompatible_ShouldReturnValidationErrors()
        {
            // Missing and incompatible snapshots are client-correctable conditions, so the service returns structured validation errors instead of throwing.
            InMemoryArchitectureSnapshotWriter writer = new();
            SnapshotDiffService service = new(writer);
            StableKey previousSnapshot = new("snapshot://validation/previous");
            StableKey currentSnapshot = new("snapshot://validation/current");
            await writer.WriteSnapshotAsync(CreateSnapshot(previousSnapshot, new StableKey("repository://one"), [], [], [], []), CancellationToken.None);
            await writer.WriteSnapshotAsync(CreateSnapshot(currentSnapshot, new StableKey("repository://two"), [], [], [], []), CancellationToken.None);

            SnapshotDiffResult missing = await service.CompareSnapshotsAsync(new SnapshotDiffQuery("snapshot://missing", previousSnapshot.Value, null, null, includeUnchangedDetails: false, take: 100), CancellationToken.None);
            SnapshotDiffResult incompatible = await service.CompareSnapshotsAsync(new SnapshotDiffQuery(currentSnapshot.Value, previousSnapshot.Value, null, null, includeUnchangedDetails: false, take: 100), CancellationToken.None);

            Assert.False(missing.Succeeded);
            Assert.Contains(missing.ValidationErrors, error => error.Code == SnapshotDiffValidationCodes.CurrentSnapshotNotFound);
            Assert.False(incompatible.Succeeded);
            Assert.Contains(incompatible.ValidationErrors, error => error.Code == SnapshotDiffValidationCodes.IncompatibleRepository);
        }

        /// <summary>
        /// Confirms truncation metadata reports continuation state when matching detail rows exceed the requested limit.
        /// </summary>
        /// <returns>A task that completes after truncation metadata is asserted.</returns>
        [Fact]
        public async Task CompareSnapshotsAsync_WhenResultExceedsLimit_ShouldReturnTruncationMetadata()
        {
            // Truncation is calculated after filters and unchanged-detail rules so clients can request a deterministic continuation page.
            InMemoryArchitectureSnapshotWriter writer = new();
            SnapshotDiffService service = new(writer);
            StableKey repositoryStableKey = new("repository://diff-truncation");
            StableKey previousSnapshot = new("snapshot://diff-truncation/previous");
            StableKey currentSnapshot = new("snapshot://diff-truncation/current");
            await writer.WriteSnapshotAsync(CreateSnapshot(previousSnapshot, repositoryStableKey, [], [], [], []), CancellationToken.None);
            await writer.WriteSnapshotAsync(CreateSnapshot(
                currentSnapshot,
                repositoryStableKey,
                [
                    CreateNode(currentSnapshot, new StableKey("project://src/A/A.csproj"), "A", "sha256:a", UnknownState.Known, null),
                    CreateNode(currentSnapshot, new StableKey("project://src/B/B.csproj"), "B", "sha256:b", UnknownState.Known, null),
                    CreateNode(currentSnapshot, new StableKey("project://src/C/C.csproj"), "C", "sha256:c", UnknownState.Known, null)
                ],
                [],
                [],
                []), CancellationToken.None);

            SnapshotDiffResult result = await service.CompareSnapshotsAsync(new SnapshotDiffQuery(currentSnapshot.Value, previousSnapshot.Value, [SnapshotDiffDomains.Nodes], null, includeUnchangedDetails: true, skip: 1, take: 1), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.True(result.Truncation.Truncated);
            Assert.Equal(3, result.Truncation.TotalAvailableItems);
            Assert.Equal(1, result.Truncation.ReturnedItems);
            Assert.Equal(1, result.Truncation.Skip);
            Assert.Equal(1, result.Truncation.Take);
            Assert.Equal("project://src/B/B.csproj", Assert.Single(result.Items).StableKey);
        }

        /// <summary>
        /// Creates a deterministic snapshot for diff service tests.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key.</param>
        /// <param name="repositoryStableKey">The repository stable key used for compatibility checks.</param>
        /// <param name="nodes">The snapshot nodes.</param>
        /// <param name="edges">The snapshot edges.</param>
        /// <param name="findings">The snapshot findings.</param>
        /// <param name="metrics">The snapshot metrics.</param>
        /// <returns>An extracted snapshot fixture.</returns>
        private static ExtractedArchitectureSnapshot CreateSnapshot(StableKey snapshotStableKey, StableKey repositoryStableKey, IReadOnlyList<ArchitectureNode> nodes, IReadOnlyList<ArchitectureEdge> edges, IReadOnlyList<FindingRecord> findings, IReadOnlyList<MetricRecord> metrics)
        {
            // The diff service compares only snapshot-owned graph records, so unrelated sections can stay empty for focused tests.
            SnapshotHeader header = new(
                snapshotStableKey,
                repositoryStableKey,
                "main",
                "abcdef",
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 20, 8, 1, 0, TimeSpan.Zero),
                "wp013-diff-tests",
                "Completed",
                warnings: [],
                errors: [],
                GraphMetadata.Empty);
            RepositoryModel repository = new(repositoryStableKey, "Diff", "D:/Repositories/Diff", null, "main", GraphMetadata.Empty);
            return new ExtractedArchitectureSnapshot(header, [repository], [], nodes, edges, [], [], findings, metrics, [], [], []);
        }

        /// <summary>
        /// Creates a deterministic architecture node fixture.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that owns the node.</param>
        /// <param name="stableKey">The node stable key.</param>
        /// <param name="displayName">The node display name.</param>
        /// <param name="fingerprint">The diff fingerprint.</param>
        /// <param name="unknownState">The node unknown-state value.</param>
        /// <param name="evidenceStableKey">The optional primary evidence stable key.</param>
        /// <returns>An architecture node fixture.</returns>
        private static ArchitectureNode CreateNode(StableKey snapshotStableKey, StableKey stableKey, string displayName, string fingerprint, UnknownState unknownState, string? evidenceStableKey)
        {
            // Node fixtures keep metadata simple so changed tests can isolate fingerprint-driven classification.
            StableKey? evidence = string.IsNullOrWhiteSpace(evidenceStableKey) ? null : new StableKey(evidenceStableKey);
            return new ArchitectureNode(
                snapshotStableKey,
                stableKey,
                NodeKind.Project,
                displayName,
                displayName,
                displayName.ToLowerInvariant(),
                "C#",
                null,
                null,
                KnowledgeKind.Fact,
                null,
                null,
                Confidence.Certain,
                unknownState,
                evidence,
                GraphMetadata.Empty,
                new Fingerprint(fingerprint));
        }

        /// <summary>
        /// Creates a deterministic architecture edge fixture.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that owns the edge.</param>
        /// <param name="stableKey">The edge stable key.</param>
        /// <param name="sourceNodeStableKey">The edge source node stable key.</param>
        /// <param name="targetNodeStableKey">The edge target node stable key.</param>
        /// <param name="fingerprint">The diff fingerprint.</param>
        /// <param name="unknownState">The edge unknown-state value.</param>
        /// <param name="evidenceStableKey">The optional primary evidence stable key.</param>
        /// <returns>An architecture edge fixture.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, string stableKey, StableKey sourceNodeStableKey, StableKey targetNodeStableKey, string fingerprint, UnknownState unknownState, string? evidenceStableKey)
        {
            // Edge fixtures represent dependency-like references that can be compared solely by stable key and fingerprint.
            StableKey? evidence = string.IsNullOrWhiteSpace(evidenceStableKey) ? null : new StableKey(evidenceStableKey);
            return new ArchitectureEdge(
                snapshotStableKey,
                new StableKey(stableKey),
                EdgeKind.References,
                sourceNodeStableKey,
                targetNodeStableKey,
                isDirect: true,
                KnowledgeKind.Fact,
                Confidence.Certain,
                unknownState,
                evidence,
                GraphMetadata.Empty,
                new Fingerprint(fingerprint));
        }

        /// <summary>
        /// Creates a deterministic finding fixture.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that owns the finding.</param>
        /// <param name="stableKey">The finding stable key.</param>
        /// <param name="fingerprint">The diff fingerprint.</param>
        /// <param name="unknownState">The finding unknown-state value.</param>
        /// <param name="evidenceStableKey">The evidence stable key.</param>
        /// <returns>A finding fixture.</returns>
        private static FindingRecord CreateFinding(StableKey snapshotStableKey, string stableKey, string fingerprint, UnknownState unknownState, string evidenceStableKey)
        {
            // Finding fixtures include an affected node and evidence so diff output can preserve contributor navigation fields.
            StableKey affectedNode = new("project://src/Diff.Finding/Diff.Finding.csproj");
            return new FindingRecord(
                snapshotStableKey,
                new StableKey(stableKey),
                "ARCHON-DIFF",
                "1.0.0",
                FindingSeverity.High,
                FindingStatus.Open,
                "Diff finding",
                "A diff finding fixture.",
                KnowledgeKind.Fact,
                Confidence.Certain,
                unknownState,
                affectedNode,
                new StableKey(evidenceStableKey),
                snapshotStableKey,
                snapshotStableKey,
                null,
                null,
                [affectedNode],
                [new StableKey(evidenceStableKey)],
                "history://" + stableKey["finding://".Length..],
                GraphMetadata.Empty,
                new Fingerprint(fingerprint));
        }

        /// <summary>
        /// Creates a deterministic metric fixture.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that owns the metric.</param>
        /// <param name="stableKey">The metric stable key.</param>
        /// <param name="metricKind">The metric kind.</param>
        /// <param name="value">The metric numeric value.</param>
        /// <param name="fingerprint">The diff fingerprint.</param>
        /// <param name="unknownState">The metric unknown-state value.</param>
        /// <param name="evidenceStableKey">The optional primary evidence stable key.</param>
        /// <returns>A metric fixture.</returns>
        private static MetricRecord CreateMetric(StableKey snapshotStableKey, string stableKey, string metricKind, decimal value, string fingerprint, UnknownState unknownState, string? evidenceStableKey)
        {
            // Metric fixtures use explicit fingerprints so tests can prove value changes are detected through the normalized comparison field.
            StableKey? evidence = string.IsNullOrWhiteSpace(evidenceStableKey) ? null : new StableKey(evidenceStableKey);
            return new MetricRecord(
                snapshotStableKey,
                new StableKey(stableKey),
                metricKind,
                MetricScopeKind.Node,
                new StableKey("project://src/Diff.Metric/Diff.Metric.csproj"),
                null,
                evidence,
                metricKind,
                value,
                null,
                "count",
                Confidence.Certain,
                unknownState,
                GraphMetadata.Empty,
                new Fingerprint(fingerprint));
        }
    }
}
