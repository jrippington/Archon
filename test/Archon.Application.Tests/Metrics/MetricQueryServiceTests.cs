using Archon.Application.Metrics;
using Archon.Application.Rules;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Xunit;

namespace Archon.Application.Tests.Metrics
{
    /// <summary>
    /// Verifies application-level snapshot metric query mapping and controlled filtering behavior.
    /// </summary>
    public sealed class MetricQueryServiceTests
    {
        /// <summary>
        /// Verifies listed metrics are mapped to stable public DTOs with secret-like metadata removed.
        /// </summary>
        /// <returns>A task that completes after service output is asserted.</returns>
        [Fact]
        public async Task ListMetricsAsync_WhenStoreReturnsMetrics_ShouldReturnSanitizedMetricDtos()
        {
            // The in-memory store fixture includes a secret-like metadata property to prove API-facing service output is sanitized.
            MetricRecord metric = CreateMetric("snapshot://metrics", "metric://metrics/node-count", "SnapshotNodeCount", MetricScopeKind.Snapshot, numericValue: 3);
            MetricQueryService service = new(new StubMetricQueryStore([metric]));

            PagedQueryResult<MetricItemDto> result = await service.ListMetricsAsync(new MetricQuery("snapshot://metrics", null, null, 0, 10), CancellationToken.None);
            MetricItemDto item = Assert.Single(result.Items);

            Assert.Equal(1, result.TotalCount);
            Assert.Equal("snapshot://metrics", item.SnapshotStableKey);
            Assert.Equal("metric://metrics/node-count", item.StableKey);
            Assert.Equal("SnapshotNodeCount", item.MetricKind);
            Assert.Equal("Snapshot", item.ScopeKind);
            Assert.Equal(3, item.NumericValue);
            Assert.Equal("nodes", item.Unit);
            Assert.False(item.HasUnknownData);
            Assert.False(item.Metadata.ToCanonicalJson().Contains("password", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("sha256:metric", item.Fingerprint);
        }

        /// <summary>
        /// Verifies project metric queries carry the project stable-key filter to the query store.
        /// </summary>
        /// <returns>A task that completes after the captured filter is asserted.</returns>
        [Fact]
        public async Task ListMetricsAsync_WhenProjectFilterIsSupplied_ShouldPassProjectStableKeyToStore()
        {
            // The service should preserve the Work Item 2 project filter rather than applying partial DTO-only filtering.
            StableKey projectStableKey = new("project://src/Metrics.Api/Metrics.Api.csproj");
            MetricRecord metric = CreateMetric("snapshot://metrics", "metric://metrics/project-package", "ProjectPackageCount", MetricScopeKind.Project, numericValue: 4, projectStableKey);
            StubMetricQueryStore store = new([metric]);
            MetricQueryService service = new(store);

            PagedQueryResult<MetricItemDto> result = await service.ListMetricsAsync(new MetricQuery("snapshot://metrics", "ProjectPackageCount", "Project", projectStableKey.Value, 0, 10), CancellationToken.None);
            MetricItemDto item = Assert.Single(result.Items);

            Assert.Equal(projectStableKey.Value, store.LastQuery?.ProjectStableKey);
            Assert.Equal(projectStableKey.Value, item.NodeStableKey);
        }

        /// <summary>
        /// Creates a deterministic metric record for query service tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the metric.</param>
        /// <param name="metricStableKey">The stable key that identifies the metric.</param>
        /// <param name="metricKind">The metric kind.</param>
        /// <param name="scopeKind">The metric scope kind.</param>
        /// <param name="numericValue">The numeric metric value.</param>
        /// <returns>A metric record suitable for service mapping assertions.</returns>
        private static MetricRecord CreateMetric(string snapshotStableKey, string metricStableKey, string metricKind, MetricScopeKind scopeKind, decimal numericValue, StableKey? projectStableKey = null)
        {
            // Metric metadata contains both safe and secret-like names so sanitation can be verified without infrastructure.
            return new MetricRecord(
                new StableKey(snapshotStableKey),
                new StableKey(metricStableKey),
                metricKind,
                scopeKind,
                nodeStableKey: projectStableKey,
                edgeStableKey: null,
                primaryEvidenceStableKey: null,
                "Snapshot node count",
                numericValue,
                textValue: null,
                "nodes",
                Confidence.Certain,
                UnknownState.Known,
                GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["calculationSource"] = "test",
                    ["passwordHint"] = "hidden"
                }),
                new Fingerprint("sha256:metric"));
        }

        /// <summary>
        /// Provides deterministic metric query results for application service tests.
        /// </summary>
        private sealed class StubMetricQueryStore : IMetricQueryStore
        {
            /// <summary>
            /// Stores the metrics returned by the stub store.
            /// </summary>
            private readonly IReadOnlyList<MetricRecord> _metrics;

            /// <summary>
            /// Gets the most recent controlled metric query received by the stub.
            /// </summary>
            internal MetricQuery? LastQuery { get; private set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="StubMetricQueryStore"/> class.
            /// </summary>
            /// <param name="metrics">The metrics returned by query calls.</param>
            internal StubMetricQueryStore(IReadOnlyList<MetricRecord> metrics)
            {
                // The stub avoids persistence dependencies while preserving the application query contract.
                _metrics = metrics;
            }

            /// <summary>
            /// Retrieves the stub metric page for the supplied query.
            /// </summary>
            /// <param name="query">The controlled metric query.</param>
            /// <param name="cancellationToken">The cancellation token for the query.</param>
            /// <returns>A completed task containing the stub metric page.</returns>
            public Task<PagedQueryResult<MetricRecord>> QueryMetricsAsync(MetricQuery query, CancellationToken cancellationToken)
            {
                // The stub applies the snapshot filter so tests exercise the required query identity field.
                cancellationToken.ThrowIfCancellationRequested();
                LastQuery = query;
                MetricRecord[] matches = _metrics
                    .Where(metric => StringComparer.Ordinal.Equals(metric.SnapshotStableKey.Value, query.SnapshotStableKey))
                    .Where(metric => query.ProjectStableKey is null || StringComparer.Ordinal.Equals(metric.NodeStableKey?.Value, query.ProjectStableKey))
                    .ToArray();
                return Task.FromResult(new PagedQueryResult<MetricRecord>(matches, matches.Length, query.Skip, query.Take));
            }
        }
    }
}
