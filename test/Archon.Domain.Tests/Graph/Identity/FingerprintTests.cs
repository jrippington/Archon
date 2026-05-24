using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Xunit;

namespace Archon.Domain.Tests.Graph.Identity
{
    /// <summary>
    /// Verifies deterministic fingerprint behavior required for WP002 diff-ready graph facts.
    /// </summary>
    public sealed class FingerprintTests
    {
        /// <summary>
        /// Verifies a fingerprint preserves a valid non-empty external hash value.
        /// </summary>
        [Fact]
        public void FingerprintStoresStableExternalValue()
        {
            // Fingerprint is a value object around a deterministic content hash, not a database or process identity.
            Fingerprint fingerprint = new("sha256:abcdef");

            Assert.Equal("sha256:abcdef", fingerprint.Value);
            Assert.Equal("sha256:abcdef", fingerprint.ToString());
        }

        /// <summary>
        /// Verifies fingerprint construction rejects invalid values.
        /// </summary>
        /// <param name="value">The invalid fingerprint string.</param>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void FingerprintRejectsNullEmptyOrWhitespace(string? value)
        {
            // Blank fingerprints cannot support deterministic diff comparisons.
            Assert.Throws<ArgumentException>(() => new Fingerprint(value));
        }

        /// <summary>
        /// Verifies equivalent logical node content produces identical fingerprints.
        /// </summary>
        [Fact]
        public void NodeFingerprintIsDeterministicForEquivalentLogicalInput()
        {
            // Equivalent metadata built in different insertion orders should produce the same canonical fingerprint input.
            GraphMetadata firstMetadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["routeTemplate"] = "/api/customers/{id}",
                ["httpVerbs"] = new[] { "GET" }
            });
            GraphMetadata secondMetadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["httpVerbs"] = new[] { "GET" },
                ["routeTemplate"] = "/api/customers/{id}"
            });

            Fingerprint first = FingerprintGenerator.ForNode(
                NodeKind.Endpoint,
                "Get Customer",
                "Customer.Api.Controllers.CustomerController.Get(System.Int32)",
                "get customer",
                KnowledgeKind.Fact,
                firstMetadata);
            Fingerprint second = FingerprintGenerator.ForNode(
                NodeKind.Endpoint,
                "Get Customer",
                "Customer.Api.Controllers.CustomerController.Get(System.Int32)",
                "get customer",
                KnowledgeKind.Fact,
                secondMetadata);

            Assert.Equal(first, second);
        }

        /// <summary>
        /// Verifies changed diff-relevant content changes a node fingerprint.
        /// </summary>
        [Fact]
        public void NodeFingerprintChangesWhenDiffRelevantContentChanges()
        {
            // Display name is diff-relevant because it changes what later graph consumers see for the same node identity.
            Fingerprint original = FingerprintGenerator.ForNode(
                NodeKind.Project,
                "Customer.Api",
                "Customer.Api",
                "customer api",
                KnowledgeKind.Fact,
                GraphMetadata.Empty);
            Fingerprint changed = FingerprintGenerator.ForNode(
                NodeKind.Project,
                "Customer.Api v2",
                "Customer.Api",
                "customer api",
                KnowledgeKind.Fact,
                GraphMetadata.Empty);

            Assert.NotEqual(original, changed);
        }

        /// <summary>
        /// Verifies excluded database-local and process-local values do not affect fingerprints when not supplied as diff input.
        /// </summary>
        [Fact]
        public void FingerprintIgnoresValuesNotIncludedAsDiffRelevantInput()
        {
            // The fingerprint helper receives only diff-relevant fields, so database IDs and process IDs remain outside the hash.
            Fingerprint first = FingerprintGenerator.FromInput(FingerprintInput.Create("Node")
                .AddField("stableKey", "project://src/Customer.Api/Customer.Api.csproj")
                .AddField("displayName", "Customer.Api")
                .AddMetadata(GraphMetadata.Empty));
            Fingerprint second = FingerprintGenerator.FromInput(FingerprintInput.Create("Node")
                .AddField("stableKey", "project://src/Customer.Api/Customer.Api.csproj")
                .AddField("displayName", "Customer.Api")
                .AddMetadata(GraphMetadata.Empty));

            Assert.Equal(first, second);
        }

        /// <summary>
        /// Verifies metadata participates in fingerprint input when it is diff-relevant.
        /// </summary>
        [Fact]
        public void FingerprintChangesWhenCanonicalMetadataChanges()
        {
            // Metadata is extraction-specific but still diff-relevant when it describes architecture behavior such as HTTP verbs.
            Fingerprint getOnly = FingerprintGenerator.ForMetric(
                "ProjectEndpointCount",
                MetricScopeKind.Project,
                "Customer.Api",
                GraphMetadata.From(new Dictionary<string, object?>
                {
                    ["httpVerbs"] = new[] { "GET" }
                }));
            Fingerprint getAndPost = FingerprintGenerator.ForMetric(
                "ProjectEndpointCount",
                MetricScopeKind.Project,
                "Customer.Api",
                GraphMetadata.From(new Dictionary<string, object?>
                {
                    ["httpVerbs"] = new[] { "GET", "POST" }
                }));

            Assert.NotEqual(getOnly, getAndPost);
        }

        /// <summary>
        /// Verifies metric fingerprint generation changes when the computed value changes for the same stable metric identity.
        /// </summary>
        [Fact]
        public void MetricFingerprintChangesWhenComputedValueChanges()
        {
            // WP013 metric diffs depend on the stable key remaining fixed while changed values produce a new fingerprint.
            Fingerprint original = FingerprintGenerator.ForMetric(
                "SnapshotNodeCount",
                MetricScopeKind.Snapshot,
                "Snapshot",
                numericValue: 2,
                textValue: null,
                unit: "nodes",
                hasUnknownData: false,
                unknownReason: null,
                GraphMetadata.Empty);
            Fingerprint changed = FingerprintGenerator.ForMetric(
                "SnapshotNodeCount",
                MetricScopeKind.Snapshot,
                "Snapshot",
                numericValue: 3,
                textValue: null,
                unit: "nodes",
                hasUnknownData: false,
                unknownReason: null,
                GraphMetadata.Empty);

            Assert.NotEqual(original, changed);
        }

        /// <summary>
        /// Verifies metric fingerprint generation includes unknown-state context when inputs are incomplete.
        /// </summary>
        [Fact]
        public void MetricFingerprintIncludesUnknownState()
        {
            // Unknown state is part of the persisted metric meaning, so known and incomplete metrics must not hash identically.
            Fingerprint known = FingerprintGenerator.ForMetric(
                "SnapshotNodeCount",
                MetricScopeKind.Snapshot,
                "Snapshot",
                numericValue: 0,
                textValue: null,
                unit: "nodes",
                hasUnknownData: false,
                unknownReason: null,
                GraphMetadata.Empty);
            Fingerprint unknown = FingerprintGenerator.ForMetric(
                "SnapshotNodeCount",
                MetricScopeKind.Snapshot,
                "Snapshot",
                numericValue: 0,
                textValue: null,
                unit: "nodes",
                hasUnknownData: true,
                unknownReason: "No graph facts were available.",
                GraphMetadata.Empty);

            Assert.NotEqual(known, unknown);
        }

        /// <summary>
        /// Verifies fingerprint generation methods exist for every required graph record category.
        /// </summary>
        [Fact]
        public void FingerprintGeneratorSupportsAllRequiredRecordCategories()
        {
            // Work Item 3 provides category-specific helpers before full graph fact records are introduced in later slices.
            Fingerprint node = FingerprintGenerator.ForNode(NodeKind.Project, "Customer.Api", "Customer.Api", "customer api", KnowledgeKind.Fact, GraphMetadata.Empty);
            Fingerprint edge = FingerprintGenerator.ForEdge(EdgeKind.DependsOn, StableKeyGenerator.ForProject("src/Customer.Api/Customer.Api.csproj"), StableKeyGenerator.ForProject("src/Customer.Application/Customer.Application.csproj"), true, KnowledgeKind.Fact, GraphMetadata.Empty);
            Fingerprint evidence = FingerprintGenerator.ForEvidence(EvidenceKind.ProjectFile, "src/Customer.Api/Customer.Api.csproj", 1, 1, "ProjectReference", KnowledgeKind.Fact, GraphMetadata.Empty);
            Fingerprint finding = FingerprintGenerator.ForFinding("ARCHON001", "1.0.0", FindingSeverity.High, FindingStatus.Open, "Unsupported framework", KnowledgeKind.Fact, GraphMetadata.Empty);
            Fingerprint metric = FingerprintGenerator.ForMetric("ProjectCount", MetricScopeKind.Graph, "snapshot://current", numericValue: 1, textValue: null, unit: "projects", hasUnknownData: false, unknownReason: null, GraphMetadata.Empty);
            Fingerprint summary = FingerprintGenerator.ForGeneratedSummary(SummaryKind.Graph, "Architecture Overview", "Markdown", "Current graph summary", GraphMetadata.Empty);

            Assert.All(new[] { node, edge, evidence, finding, metric, summary }, fingerprint => Assert.StartsWith("sha256:", fingerprint.Value, StringComparison.Ordinal));
        }
    }
}
