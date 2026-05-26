using Archon.Application.Hotspots;
using Archon.Application.Rules;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpResources;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies WP015 Work Item 9 resource URI parsing, authorization, current snapshot resolution, and bounded current resources.
    /// </summary>
    public sealed class ArchonMcpResourceTests
    {
        /// <summary>
        /// Confirms current snapshot resources resolve an explicit repository scope and return bounded snapshot context.
        /// </summary>
        [Fact]
        public async Task ReadCurrentSnapshotResourceReturnsSnapshotIdentityAndCounts()
        {
            // The resolver has one unambiguous current snapshot so the resource can prove explicit current selection succeeds.
            FakeCurrentSnapshotProvider snapshotProvider = new([CreateSnapshot("snapshot://current", "repository://archon-test", "solution://archon-test/main", completedOffsetMinutes: 1)]);
            using WebApplication app = BuildResourceApp(snapshotProvider, new FakeHotlistQueryService(CreateRulePage(1), CreateHotlistPage(1)), new FakeHotspotQueryService(CreateHotspotPage(1)));
            IArchonMcpResourceDispatcher dispatcher = app.Services.GetRequiredService<IArchonMcpResourceDispatcher>();

            object payload = await dispatcher.ReadResourceAsync("archon://snapshot/current?repository=repository%3A%2F%2Farchon-test&solution=solution%3A%2F%2Farchon-test%2Fmain", CancellationToken.None);

            // Snapshot resource output should identify the selected snapshot without exposing raw persistence or filesystem details.
            ArchonMcpEnvelope<ArchonMcpCurrentSnapshotResourceFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpCurrentSnapshotResourceFacts>>(payload);
            Assert.Equal(ArchonMcpResourceOperations.ReadResource, envelope.Operation);
            Assert.Equal("archon://snapshot/current", envelope.Facts.ResourceUri);
            Assert.Equal("snapshot://current", envelope.Facts.SnapshotStableKey);
            Assert.Equal("repository://archon-test", envelope.Facts.RepositoryStableKey);
            Assert.Equal("solution://archon-test/main", envelope.Facts.SolutionStableKeys.Single());
            Assert.Equal(3, envelope.Facts.NodeCount);
            Assert.Equal(2, envelope.Facts.FindingCount);
            Assert.False(envelope.Limits.Truncated);
        }

        /// <summary>
        /// Confirms resource URI parsing rejects malformed schemes, unsupported families, and unsafe duplicate parameters before query execution.
        /// </summary>
        [Theory]
        [InlineData("http://snapshot/current?repository=repository%3A%2F%2Farchon-test")]
        [InlineData("archon://unknown/current?repository=repository%3A%2F%2Farchon-test")]
        [InlineData("archon://snapshot/current?repository=repository%3A%2F%2Fone&repository=repository%3A%2F%2Ftwo")]
        [InlineData("archon://snapshot/current?repository=")]
        public async Task ReadResourceRejectsMalformedUnsupportedAndAmbiguousUris(string uri)
        {
            // The fake services capture calls, so validation failures can prove query dependencies are not invoked.
            FakeCurrentSnapshotProvider snapshotProvider = new([CreateSnapshot("snapshot://current", "repository://archon-test", "solution://archon-test/main", completedOffsetMinutes: 1)]);
            FakeHotlistQueryService hotlistService = new(CreateRulePage(1), CreateHotlistPage(1));
            FakeHotspotQueryService hotspotService = new(CreateHotspotPage(1));
            using WebApplication app = BuildResourceApp(snapshotProvider, hotlistService, hotspotService);
            IArchonMcpResourceDispatcher dispatcher = app.Services.GetRequiredService<IArchonMcpResourceDispatcher>();

            object payload = await dispatcher.ReadResourceAsync(uri, CancellationToken.None);

            // Validation or unsupported-operation errors should be structured and should not reach current snapshot or list query seams.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.True(error.Error.Category is ArchonMcpErrorCategory.Validation or ArchonMcpErrorCategory.UnsupportedOperation);
            Assert.Empty(snapshotProvider.Requests);
            Assert.Empty(hotlistService.RuleQueries);
            Assert.Empty(hotlistService.HotlistQueries);
            Assert.Empty(hotspotService.Queries);
        }

        /// <summary>
        /// Confirms disabled resource reads fail authorization before parsing, validation, current selection, or query execution.
        /// </summary>
        [Fact]
        public async Task DisabledResourceReadReturnsForbiddenBeforeDependenciesAreInvoked()
        {
            // The allow-list intentionally omits resource reads to verify shared MCP security remains the first boundary.
            FakeCurrentSnapshotProvider snapshotProvider = new([CreateSnapshot("snapshot://current", "repository://archon-test", "solution://archon-test/main", completedOffsetMinutes: 1)]);
            FakeHotlistQueryService hotlistService = new(CreateRulePage(1), CreateHotlistPage(1));
            FakeHotspotQueryService hotspotService = new(CreateHotspotPage(1));
            using WebApplication app = BuildResourceApp(snapshotProvider, hotlistService, hotspotService, allowedOperations: ["archon.health"]);
            IArchonMcpResourceDispatcher dispatcher = app.Services.GetRequiredService<IArchonMcpResourceDispatcher>();

            object payload = await dispatcher.ReadResourceAsync("archon://rules/current?repository=repository%3A%2F%2Farchon-test", CancellationToken.None);

            // Forbidden output and empty captures prove authorization precedes resource parsing and all query seams.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Forbidden, error.Error.Category);
            Assert.Empty(snapshotProvider.Requests);
            Assert.Empty(hotlistService.RuleQueries);
            Assert.Empty(hotlistService.HotlistQueries);
            Assert.Empty(hotspotService.Queries);
        }

        /// <summary>
        /// Confirms current snapshot selection returns a structured ambiguity error when explicit repository scope still has tied current snapshots.
        /// </summary>
        [Fact]
        public async Task ReadResourceReturnsAmbiguousWhenCurrentSnapshotSelectionIsTied()
        {
            // Two snapshots with identical completed and started timestamps create an intentionally ambiguous current selection.
            DateTimeOffset timestamp = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
            FakeCurrentSnapshotProvider snapshotProvider = new([
                CreateSnapshot("snapshot://current-a", "repository://archon-test", "solution://archon-test/main", timestamp, timestamp),
                CreateSnapshot("snapshot://current-b", "repository://archon-test", "solution://archon-test/main", timestamp, timestamp)
            ]);
            FakeHotlistQueryService hotlistService = new(CreateRulePage(1), CreateHotlistPage(1));
            using WebApplication app = BuildResourceApp(snapshotProvider, hotlistService, new FakeHotspotQueryService(CreateHotspotPage(1)));
            IArchonMcpResourceDispatcher dispatcher = app.Services.GetRequiredService<IArchonMcpResourceDispatcher>();

            object payload = await dispatcher.ReadResourceAsync("archon://hotlist/current?repository=repository%3A%2F%2Farchon-test&solution=solution%3A%2F%2Farchon-test%2Fmain", CancellationToken.None);

            // Ambiguous current selection should stop before the hotlist query because no single snapshot scope is safe to infer.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Ambiguous, error.Error.Category);
            Assert.Single(snapshotProvider.Requests);
            Assert.Empty(hotlistService.HotlistQueries);
        }

        /// <summary>
        /// Confirms missing repository or solution scopes map to not-found current resource errors.
        /// </summary>
        [Fact]
        public async Task ReadResourceReturnsNotFoundWhenCurrentSnapshotScopeDoesNotExist()
        {
            // The resolver contains a different repository so a scoped current resource read cannot silently choose unrelated data.
            FakeCurrentSnapshotProvider snapshotProvider = new([CreateSnapshot("snapshot://current", "repository://other", "solution://other/main", completedOffsetMinutes: 1)]);
            using WebApplication app = BuildResourceApp(snapshotProvider, new FakeHotlistQueryService(CreateRulePage(1), CreateHotlistPage(1)), new FakeHotspotQueryService(CreateHotspotPage(1)));
            IArchonMcpResourceDispatcher dispatcher = app.Services.GetRequiredService<IArchonMcpResourceDispatcher>();

            object payload = await dispatcher.ReadResourceAsync("archon://snapshot/current?repository=repository%3A%2F%2Farchon-test", CancellationToken.None);

            // The not-found category tells clients the selected current scope has no data without exposing persistence internals.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.NotFound, error.Error.Category);
            Assert.Single(snapshotProvider.Requests);
        }

        /// <summary>
        /// Confirms rules, hotlist, and hotspots current resources return bounded structured facts through approved query seams.
        /// </summary>
        [Fact]
        public async Task ReadCurrentListResourcesReturnBoundedStructuredContent()
        {
            // Three rule, finding, and hotspot rows make MCP limit truncation observable for all current list resources.
            FakeCurrentSnapshotProvider snapshotProvider = new([CreateSnapshot("snapshot://current", "repository://archon-test", "solution://archon-test/main", completedOffsetMinutes: 1)]);
            FakeHotlistQueryService hotlistService = new(CreateRulePage(3), CreateHotlistPage(3));
            FakeHotspotQueryService hotspotService = new(CreateHotspotPage(3));
            using WebApplication app = BuildResourceApp(snapshotProvider, hotlistService, hotspotService);
            IArchonMcpResourceDispatcher dispatcher = app.Services.GetRequiredService<IArchonMcpResourceDispatcher>();

            object rulesPayload = await dispatcher.ReadResourceAsync("archon://rules/current?repository=repository%3A%2F%2Farchon-test&limit=2", CancellationToken.None);
            object hotlistPayload = await dispatcher.ReadResourceAsync("archon://hotlist/current?repository=repository%3A%2F%2Farchon-test&limit=2", CancellationToken.None);
            object hotspotsPayload = await dispatcher.ReadResourceAsync("archon://hotspots/current?repository=repository%3A%2F%2Farchon-test&limit=2", CancellationToken.None);

            // Each resource family has a specific fact shape, stable snapshot identity, bounded item count, and truncation warning.
            ArchonMcpEnvelope<ArchonMcpRulesCurrentResourceFacts> rules = Assert.IsType<ArchonMcpEnvelope<ArchonMcpRulesCurrentResourceFacts>>(rulesPayload);
            ArchonMcpEnvelope<ArchonMcpHotlistCurrentResourceFacts> hotlist = Assert.IsType<ArchonMcpEnvelope<ArchonMcpHotlistCurrentResourceFacts>>(hotlistPayload);
            ArchonMcpEnvelope<ArchonMcpHotspotsCurrentResourceFacts> hotspots = Assert.IsType<ArchonMcpEnvelope<ArchonMcpHotspotsCurrentResourceFacts>>(hotspotsPayload);
            Assert.Equal(2, rules.Facts.Rules.Count);
            Assert.Equal(2, hotlist.Facts.Findings.Count);
            Assert.Equal(2, hotspots.Facts.Hotspots.Count);
            Assert.True(rules.Limits.Truncated);
            Assert.True(hotlist.Limits.Truncated);
            Assert.True(hotspots.Limits.Truncated);
            Assert.Equal("snapshot://current", hotlistService.HotlistQueries[0].SnapshotStableKey);
            Assert.Equal("snapshot://current", hotspotService.Queries[0].SnapshotStableKey);
            Assert.Contains(hotlist.Evidence, evidence => evidence.StableKey == "evidence://finding/legacy-1");
            Assert.Contains(hotspots.Evidence, evidence => evidence.StableKey == "evidence://hotspot/2");
        }

        /// <summary>
        /// Builds an MCP host application with fake current snapshot, rule, hotlist, and hotspot query services plus configurable security settings.
        /// </summary>
        /// <param name="snapshotProvider">The fake current snapshot provider used by resource tests.</param>
        /// <param name="hotlistService">The fake hotlist query service used by rule and finding resources.</param>
        /// <param name="hotspotService">The fake hotspot query service used by hotspot resources.</param>
        /// <param name="allowedOperations">The optional operation allow-list used by authorization tests.</param>
        /// <returns>A configured web application exposing Work Item 9 MCP resource services.</returns>
        private static WebApplication BuildResourceApp(FakeCurrentSnapshotProvider snapshotProvider, FakeHotlistQueryService hotlistService, FakeHotspotQueryService hotspotService, string[]? allowedOperations = null)
        {
            // Tests use production MCP composition and replace only approved application/query seams.
            List<string> args =
            [
                "Archon:Mcp:Security:RequireAuthenticatedCaller=true",
                "Archon:Mcp:Security:TestCallerId=developer-1",
                "Archon:Mcp:Limits:MaxResultCount=2",
                "Archon:Mcp:Limits:MaxEvidenceCount=2",
                "Archon:Mcp:Limits:MaxTraversalDepth=3"
            ];
            string[] operations = allowedOperations ?? ["archon.health", ArchonMcpResourceOperations.ReadResource];
            for (int index = 0; index < operations.Length; index++)
            {
                args.Add($"Archon:Mcp:Security:AllowedOperations:{index}={operations[index]}");
            }

            return Program.BuildApplication(args.ToArray(), builder =>
            {
                // Replacing query services with fakes keeps assertions independent of persistence implementation details.
                builder.Services.AddSingleton<IArchonMcpCurrentSnapshotProvider>(snapshotProvider);
                builder.Services.AddSingleton<IHotlistQueryService>(hotlistService);
                builder.Services.AddSingleton<IHotspotQueryService>(hotspotService);
            });
        }

        /// <summary>
        /// Creates deterministic current snapshot context for resource tests using relative minute offsets from a fixed timestamp.
        /// </summary>
        /// <param name="stableKey">The snapshot stable key.</param>
        /// <param name="repositoryStableKey">The repository stable key.</param>
        /// <param name="solutionStableKey">The solution stable key included in the snapshot.</param>
        /// <param name="completedOffsetMinutes">The completed timestamp offset used for deterministic latest selection.</param>
        /// <returns>A current snapshot context.</returns>
        private static ArchonMcpCurrentSnapshotContext CreateSnapshot(string stableKey, string repositoryStableKey, string solutionStableKey, int completedOffsetMinutes)
        {
            // Fixed timestamps avoid clock-dependent ordering in tests.
            DateTimeOffset startedUtc = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
            DateTimeOffset completedUtc = startedUtc.AddMinutes(completedOffsetMinutes);
            return CreateSnapshot(stableKey, repositoryStableKey, solutionStableKey, startedUtc, completedUtc);
        }

        /// <summary>
        /// Creates deterministic current snapshot context for resource tests using explicit timestamps.
        /// </summary>
        /// <param name="stableKey">The snapshot stable key.</param>
        /// <param name="repositoryStableKey">The repository stable key.</param>
        /// <param name="solutionStableKey">The solution stable key included in the snapshot.</param>
        /// <param name="startedUtc">The snapshot started timestamp.</param>
        /// <param name="completedUtc">The snapshot completed timestamp.</param>
        /// <returns>A current snapshot context.</returns>
        private static ArchonMcpCurrentSnapshotContext CreateSnapshot(string stableKey, string repositoryStableKey, string solutionStableKey, DateTimeOffset startedUtc, DateTimeOffset? completedUtc)
        {
            // Counts are intentionally non-zero so the snapshot resource can prove it maps summary context.
            return new ArchonMcpCurrentSnapshotContext(stableKey, repositoryStableKey, [solutionStableKey], "main", "abcdef1", startedUtc, completedUtc, "Completed", NodeCount: 3, EdgeCount: 4, RuleCount: 5, FindingCount: 2, MetricCount: 6, EvidenceCount: 7, WarningCount: 1, ErrorCount: 0);
        }

        /// <summary>
        /// Creates a deterministic page of rule catalog DTOs for resource tests.
        /// </summary>
        /// <param name="count">The number of rule rows to include before MCP limiting.</param>
        /// <returns>A rule catalog page.</returns>
        private static PagedQueryResult<RuleCatalogItemDto> CreateRulePage(int count)
        {
            // Rules vary by code and category so tests can assert ordering and limit behavior.
            RuleCatalogItemDto[] rules =
            [
                new RuleCatalogItemDto("ARCH001", "1.0.0", "Layering", "Layering", "High", "Active", enabled: true, builtIn: true, ownerScope: null, "Projects must respect onion layering.", ["project", "dependency"]),
                new RuleCatalogItemDto("ARCH002", "1.0.0", "Data Access", "Modernization", "Medium", "Active", enabled: true, builtIn: true, ownerScope: null, "Legacy data access should be reviewed.", ["data-access"]),
                new RuleCatalogItemDto("ARCH003", "1.0.0", "Endpoints", "Api", "Low", "Active", enabled: false, builtIn: true, ownerScope: null, "Endpoints should be discoverable.", ["endpoint"])
            ];
            return new PagedQueryResult<RuleCatalogItemDto>(rules.Take(count), count, skip: 0, take: Math.Max(1, count));
        }

        /// <summary>
        /// Creates a deterministic page of hotlist DTOs for resource tests.
        /// </summary>
        /// <param name="count">The number of finding rows to include before MCP limiting.</param>
        /// <returns>A hotlist finding page.</returns>
        private static PagedQueryResult<HotlistItemDto> CreateHotlistPage(int count)
        {
            // Findings include safe affected-node and evidence references without raw snippets or mutation metadata.
            HotlistItemDto[] findings =
            [
                new HotlistItemDto("snapshot://current", "finding://legacy/critical", "history://legacy/critical", "ARCH001", "1.0.0", "Critical legacy boundary", "A critical legacy boundary finding requires investigation.", "Critical", "Active", 0.92m, "Modernization", [new AffectedNodeReferenceDto("project://legacy/app", "Legacy App", "Project", "project://legacy/app")], [new FindingEvidenceReferenceDto("evidence://finding/legacy-1", "Legacy boundary evidence")], hasUnknownData: true, "Some dependency context was unresolved."),
                new HotlistItemDto("snapshot://current", "finding://legacy/high", "history://legacy/high", "ARCH001", "1.0.0", "High legacy data access", "High risk legacy data-access finding.", "High", "Active", 0.81m, "Modernization", [new AffectedNodeReferenceDto("symbol://legacy/repository", "LegacyRepository", "Symbol", "project://legacy/app")], [new FindingEvidenceReferenceDto("evidence://finding/legacy-2", "Legacy data access evidence")], hasUnknownData: false, null),
                new HotlistItemDto("snapshot://current", "finding://api/low", "history://api/low", "ARCH003", "1.0.0", "Endpoint documentation", "Endpoint documentation needs review.", "Low", "Active", 0.74m, "Api", [new AffectedNodeReferenceDto("endpoint://api/orders", "Orders endpoint", "Endpoint", "project://legacy/app")], [new FindingEvidenceReferenceDto("evidence://finding/api-1", "API evidence")], hasUnknownData: false, null)
            ];
            return new PagedQueryResult<HotlistItemDto>(findings.Take(count), count, skip: 0, take: Math.Max(1, count));
        }

        /// <summary>
        /// Creates a deterministic page of hotspot DTOs for resource tests.
        /// </summary>
        /// <param name="count">The number of hotspot rows to include before MCP limiting.</param>
        /// <returns>A hotspot page.</returns>
        private static PagedQueryResult<HotspotItemDto> CreateHotspotPage(int count)
        {
            // Hotspots include contributing references and evidence stable keys without raw snippets.
            HotspotItemDto[] hotspots =
            [
                new HotspotItemDto("snapshot://current", "hotspot://legacy/1", "Modernization", "project://legacy/app", "Project", "Legacy App", 92.5m, 1, ["metric://legacy/complexity"], ["finding://legacy/critical"], ["evidence://hotspot/1"], 0.91m, true, "Some contributing metrics are partial.", Archon.Domain.Graph.Metadata.GraphMetadata.Empty, "fingerprint-hotspot-1"),
                new HotspotItemDto("snapshot://current", "hotspot://legacy/2", "DataAccess", "symbol://legacy/repository", "Symbol", "LegacyRepository", 74.0m, 2, ["metric://legacy/data-access"], ["finding://legacy/high"], ["evidence://hotspot/2"], 0.82m, false, null, Archon.Domain.Graph.Metadata.GraphMetadata.Empty, "fingerprint-hotspot-2"),
                new HotspotItemDto("snapshot://current", "hotspot://api/3", "Api", "endpoint://api/orders", "Endpoint", "Orders endpoint", 33.0m, 3, ["metric://api/endpoint"], ["finding://api/low"], ["evidence://hotspot/3"], 0.73m, false, null, Archon.Domain.Graph.Metadata.GraphMetadata.Empty, "fingerprint-hotspot-3")
            ];
            return new PagedQueryResult<HotspotItemDto>(hotspots.Take(count), count, skip: 0, take: Math.Max(1, count));
        }

        /// <summary>
        /// Provides deterministic current snapshot resolution behavior for MCP resource tests.
        /// </summary>
        private sealed class FakeCurrentSnapshotProvider : IArchonMcpCurrentSnapshotProvider
        {
            /// <summary>
            /// Stores snapshots returned by the provider.
            /// </summary>
            private readonly IReadOnlyList<ArchonMcpCurrentSnapshotContext> _snapshots;

            /// <summary>
            /// Creates a fake current snapshot provider with deterministic snapshot contexts.
            /// </summary>
            /// <param name="snapshots">The snapshot contexts available to current resource selection.</param>
            public FakeCurrentSnapshotProvider(IReadOnlyList<ArchonMcpCurrentSnapshotContext> snapshots)
            {
                // Captured request lists let tests prove validation and authorization stop provider calls when expected.
                _snapshots = snapshots;
            }

            /// <summary>
            /// Gets captured current snapshot requests.
            /// </summary>
            public List<ArchonMcpCurrentSnapshotRequest> Requests { get; } = [];

            /// <inheritdoc />
            public Task<ArchonMcpCurrentSnapshotResolution> ResolveCurrentSnapshotAsync(ArchonMcpCurrentSnapshotRequest request, CancellationToken cancellationToken)
            {
                // Current selection mirrors production ordering and reports ties as ambiguity rather than inventing a current snapshot.
                Requests.Add(request);
                IReadOnlyList<ArchonMcpCurrentSnapshotContext> scoped = _snapshots
                    .Where(snapshot => StringComparer.Ordinal.Equals(snapshot.RepositoryStableKey, request.RepositoryStableKey))
                    .Where(snapshot => request.SolutionStableKey is null || snapshot.SolutionStableKeys.Contains(request.SolutionStableKey, StringComparer.Ordinal))
                    .ToArray();
                if (scoped.Count == 0)
                {
                    return Task.FromResult(ArchonMcpCurrentSnapshotResolution.NotFound("No current snapshot matched the requested scope."));
                }

                DateTimeOffset bestCompleted = scoped.Max(snapshot => snapshot.CompletedUtc ?? snapshot.StartedUtc);
                ArchonMcpCurrentSnapshotContext[] tied = scoped.Where(snapshot => (snapshot.CompletedUtc ?? snapshot.StartedUtc) == bestCompleted).ToArray();
                if (tied.Length > 1)
                {
                    return Task.FromResult(ArchonMcpCurrentSnapshotResolution.Ambiguous(tied.Select(snapshot => snapshot.SnapshotStableKey).ToArray(), "Current snapshot selection matched multiple snapshots with the same completion timestamp."));
                }

                return Task.FromResult(ArchonMcpCurrentSnapshotResolution.Success(tied[0]));
            }
        }

        /// <summary>
        /// Provides deterministic rule and hotlist query behavior for MCP resource tests.
        /// </summary>
        private sealed class FakeHotlistQueryService : IHotlistQueryService
        {
            /// <summary>
            /// Stores the rule page returned by catalog queries.
            /// </summary>
            private readonly PagedQueryResult<RuleCatalogItemDto> _rulePage;

            /// <summary>
            /// Stores the hotlist page returned by finding queries.
            /// </summary>
            private readonly PagedQueryResult<HotlistItemDto> _hotlistPage;

            /// <summary>
            /// Creates a fake hotlist query service with deterministic pages.
            /// </summary>
            /// <param name="rulePage">The rule catalog page to return.</param>
            /// <param name="hotlistPage">The hotlist finding page to return.</param>
            public FakeHotlistQueryService(PagedQueryResult<RuleCatalogItemDto> rulePage, PagedQueryResult<HotlistItemDto> hotlistPage)
            {
                // Captured query lists let tests prove validation and authorization stop calls when expected.
                _rulePage = rulePage;
                _hotlistPage = hotlistPage;
            }

            /// <summary>
            /// Gets captured rule catalog queries.
            /// </summary>
            public List<RuleCatalogQuery> RuleQueries { get; } = [];

            /// <summary>
            /// Gets captured hotlist finding queries.
            /// </summary>
            public List<HotlistQuery> HotlistQueries { get; } = [];

            /// <inheritdoc />
            public Task<PagedQueryResult<RuleCatalogItemDto>> ListRulesAsync(RuleCatalogQuery query, CancellationToken cancellationToken)
            {
                // Rule queries are captured before returning the deterministic page.
                RuleQueries.Add(query);
                return Task.FromResult(_rulePage);
            }

            /// <inheritdoc />
            public Task<RuleDetailDto?> GetRuleAsync(string ruleCode, string version, CancellationToken cancellationToken)
            {
                // Rule detail is outside current resource scope, so the fake returns no detail.
                return Task.FromResult<RuleDetailDto?>(null);
            }

            /// <inheritdoc />
            public Task<PagedQueryResult<HotlistItemDto>> ListHotlistAsync(HotlistQuery query, CancellationToken cancellationToken)
            {
                // Hotlist queries are captured before returning the deterministic page.
                HotlistQueries.Add(query);
                return Task.FromResult(_hotlistPage);
            }

            /// <inheritdoc />
            public Task<FindingDetailDto?> GetFindingAsync(string snapshotStableKey, string findingStableKey, CancellationToken cancellationToken)
            {
                // Finding detail is outside current resource scope, so the fake returns no detail.
                return Task.FromResult<FindingDetailDto?>(null);
            }

            /// <inheritdoc />
            public Task<FindingHistoryDto?> GetFindingHistoryAsync(string historyKey, CancellationToken cancellationToken)
            {
                // Finding history is outside current resource scope, so the fake returns no history.
                return Task.FromResult<FindingHistoryDto?>(null);
            }

            /// <inheritdoc />
            public Task<SuppressionCommandResult> SuppressFindingAsync(SuppressFindingCommand command, CancellationToken cancellationToken)
            {
                // Suppression is deliberately unsupported by MCP; throwing ensures tests fail if a resource attempted mutation.
                throw new InvalidOperationException("MCP resource tests must not invoke suppression mutation.");
            }
        }

        /// <summary>
        /// Provides deterministic hotspot query behavior for MCP resource tests.
        /// </summary>
        private sealed class FakeHotspotQueryService : IHotspotQueryService
        {
            /// <summary>
            /// Stores the hotspot page returned by queries.
            /// </summary>
            private readonly PagedQueryResult<HotspotItemDto> _page;

            /// <summary>
            /// Creates a fake hotspot query service with a deterministic page.
            /// </summary>
            /// <param name="page">The hotspot page to return.</param>
            public FakeHotspotQueryService(PagedQueryResult<HotspotItemDto> page)
            {
                // Captured query lists let tests prove validation and authorization stop calls when expected.
                _page = page;
            }

            /// <summary>
            /// Gets captured hotspot queries.
            /// </summary>
            public List<HotspotQuery> Queries { get; } = [];

            /// <inheritdoc />
            public Task<PagedQueryResult<HotspotItemDto>> ListHotspotsAsync(HotspotQuery query, CancellationToken cancellationToken)
            {
                // Hotspot queries are captured before returning the deterministic page.
                Queries.Add(query);
                return Task.FromResult(_page);
            }
        }
    }
}
