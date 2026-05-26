using Archon.Application.Diff;
using Archon.Application.Rules;
using Archon.Domain.Graph.Metadata;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpHotlist;
using ArchonMcp.McpRules;
using ArchonMcp.McpSnapshotDiff;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies WP015 Work Item 8 read-only rules, hotlist findings, and snapshot diff MCP tools.
    /// </summary>
    public sealed class ArchonMcpRulesHotlistAndSnapshotDiffTests
    {
        /// <summary>
        /// Confirms architecture rule catalog lookup maps filters, returns bounded rules, and exposes unsupported counts/source references as unknowns.
        /// </summary>
        [Fact]
        public async Task GetArchitectureRulesReturnsFilteredReadOnlyRuleCatalog()
        {
            // The fake hotlist service captures catalog queries to prove MCP delegates through the approved application seam.
            FakeHotlistQueryService hotlistService = new(CreateRulePage(3), CreateHotlistPage(1));
            using WebApplication app = BuildRulesHotlistDiffApp(hotlistService, new FakeSnapshotDiffService(CreateDiffResult(changed: true, detailCount: 1)));
            IArchonMcpRulesTool tool = app.Services.GetRequiredService<IArchonMcpRulesTool>();

            object payload = await tool.GetArchitectureRulesAsync(new ArchonMcpArchitectureRulesRequest("ARCH001", "Layering", "High", Enabled: true, SnapshotSelector: "latest", Limit: 2), CancellationToken.None);

            // The response should be a read-only envelope with catalog facts, query filters, truncation, and no mutation-oriented follow-ups.
            ArchonMcpEnvelope<ArchonMcpArchitectureRulesFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpArchitectureRulesFacts>>(payload);
            Assert.Equal(ArchonMcpRulesOperations.GetArchitectureRules, envelope.Operation);
            Assert.Equal("ARCH001", hotlistService.RuleQueries[0].RuleCode);
            Assert.Equal("Layering", hotlistService.RuleQueries[0].Category);
            Assert.True(hotlistService.RuleQueries[0].Enabled);
            Assert.Equal(2, envelope.Facts.Rules.Count);
            Assert.True(envelope.Limits.Truncated);
            Assert.Contains(envelope.Unknowns, unknown => unknown.Kind == "relatedFindingCounts");
            Assert.DoesNotContain(envelope.SuggestedFollowUps, followUp => followUp.Label.Contains("enable", StringComparison.OrdinalIgnoreCase) || followUp.Label.Contains("delete", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Confirms invalid rule catalog requests fail validation before the rule catalog query layer is invoked.
        /// </summary>
        [Fact]
        public async Task GetArchitectureRulesValidationFailureDoesNotInvokeQueryLayer()
        {
            // A whitespace category is client-correctable and should fail before any query call is made.
            FakeHotlistQueryService hotlistService = new(CreateRulePage(1), CreateHotlistPage(1));
            using WebApplication app = BuildRulesHotlistDiffApp(hotlistService, new FakeSnapshotDiffService(CreateDiffResult(changed: true, detailCount: 1)));
            IArchonMcpRulesTool tool = app.Services.GetRequiredService<IArchonMcpRulesTool>();

            object payload = await tool.GetArchitectureRulesAsync(new ArchonMcpArchitectureRulesRequest(RuleCode: null, Category: " ", Severity: null, Enabled: null, SnapshotSelector: "latest", Limit: 1), CancellationToken.None);

            // The validation error and zero captured catalog queries prove malformed filters are not passed downstream.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Validation, error.Error.Category);
            Assert.Empty(hotlistService.RuleQueries);
        }

        /// <summary>
        /// Confirms hotlist finding lookup maps filters, sorting, evidence, affected nodes, unknowns, and truncation.
        /// </summary>
        [Fact]
        public async Task GetHotlistFindingsReturnsSortedEvidenceBackedFindings()
        {
            // The fake hotlist page includes three findings so MCP sorting and limiting behavior are observable.
            FakeHotlistQueryService hotlistService = new(CreateRulePage(1), CreateHotlistPage(3));
            using WebApplication app = BuildRulesHotlistDiffApp(hotlistService, new FakeSnapshotDiffService(CreateDiffResult(changed: true, detailCount: 1)));
            IArchonMcpHotlistTool tool = app.Services.GetRequiredService<IArchonMcpHotlistTool>();

            object payload = await tool.GetHotlistFindingsAsync(CreateHotlistRequest(limit: 2, sortBy: "severity"), CancellationToken.None);

            // The response should expose stable finding identities, affected nodes, evidence references, and deterministic high-severity ordering.
            ArchonMcpEnvelope<ArchonMcpHotlistFindingsFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpHotlistFindingsFacts>>(payload);
            Assert.Equal(ArchonMcpHotlistOperations.GetHotlistFindings, envelope.Operation);
            Assert.Equal("ARCH001", hotlistService.HotlistQueries[0].RuleCode);
            Assert.Equal("High", hotlistService.HotlistQueries[0].Severity);
            Assert.Equal(2, envelope.Facts.Findings.Count);
            Assert.Equal("Critical", envelope.Facts.Findings[0].Severity);
            Assert.Contains(envelope.Evidence, evidence => evidence.StableKey == "evidence://finding/legacy-1");
            Assert.Contains(envelope.Facts.Findings, finding => finding.AffectedNodes.Any(node => node.StableKey == "project://legacy/app"));
            Assert.Contains(envelope.Unknowns, unknown => unknown.Kind == "findingHistoryTimestamps");
            Assert.True(envelope.Limits.Truncated);
        }

        /// <summary>
        /// Confirms disabled hotlist finding requests fail authorization before validation or query execution.
        /// </summary>
        [Fact]
        public async Task DisabledGetHotlistFindingsReturnsForbiddenBeforeQueryLayerIsInvoked()
        {
            // The allow-list intentionally omits the hotlist operation to verify the shared executor remains the first boundary.
            FakeHotlistQueryService hotlistService = new(CreateRulePage(1), CreateHotlistPage(1));
            using WebApplication app = BuildRulesHotlistDiffApp(hotlistService, new FakeSnapshotDiffService(CreateDiffResult(changed: true, detailCount: 1)), allowedOperations: ["archon.health"]);
            IArchonMcpHotlistTool tool = app.Services.GetRequiredService<IArchonMcpHotlistTool>();

            object payload = await tool.GetHotlistFindingsAsync(CreateHotlistRequest(), CancellationToken.None);

            // Forbidden output and no captured queries prove authorization precedes all hotlist query behavior.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Forbidden, error.Error.Category);
            Assert.Empty(hotlistService.HotlistQueries);
        }

        /// <summary>
        /// Confirms snapshot diff supports explicit snapshot comparison with summary counts, details, fingerprints, unknowns, evidence, and truncation.
        /// </summary>
        [Fact]
        public async Task GetSnapshotDiffReturnsSummaryDetailsAndFingerprints()
        {
            // The fake diff service returns three detail rows so MCP limiting and fingerprint mapping can be asserted.
            FakeSnapshotDiffService diffService = new(CreateDiffResult(changed: true, detailCount: 3));
            using WebApplication app = BuildRulesHotlistDiffApp(new FakeHotlistQueryService(CreateRulePage(1), CreateHotlistPage(1)), diffService);
            IArchonMcpSnapshotDiffTool tool = app.Services.GetRequiredService<IArchonMcpSnapshotDiffTool>();

            object payload = await tool.GetSnapshotDiffAsync(CreateExplicitDiffRequest(limit: 2), CancellationToken.None);

            // The response should preserve stable snapshot identities, domain counts, stable detail keys, fingerprints, and evidence references.
            ArchonMcpEnvelope<ArchonMcpSnapshotDiffFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpSnapshotDiffFacts>>(payload);
            Assert.Equal(ArchonMcpSnapshotDiffOperations.GetSnapshotDiff, envelope.Operation);
            Assert.Equal("snapshot://current", diffService.ExplicitQueries[0].CurrentSnapshotStableKey);
            Assert.Equal("snapshot://previous", diffService.ExplicitQueries[0].PreviousSnapshotStableKey);
            Assert.True(envelope.Facts.HasChanges);
            Assert.Equal(2, envelope.Facts.Details.Count);
            Assert.Contains(envelope.Facts.Details, detail => detail.CurrentFingerprint == "fingerprint-current-0");
            Assert.Contains(envelope.Evidence, evidence => evidence.StableKey == "evidence://diff/0");
            Assert.Contains(envelope.Unknowns, unknown => unknown.Kind == "snapshotDiffDetail");
            Assert.True(envelope.Limits.Truncated);
        }

        /// <summary>
        /// Confirms snapshot diff supports latest-to-previous implied comparison while rejecting mixed explicit/latest requests.
        /// </summary>
        [Fact]
        public async Task GetSnapshotDiffSupportsLatestComparableModeAndValidation()
        {
            // Latest mode should call the latest-to-previous service seam; mixed explicit/latest input should fail validation first.
            FakeSnapshotDiffService diffService = new(CreateDiffResult(changed: false, detailCount: 0));
            using WebApplication app = BuildRulesHotlistDiffApp(new FakeHotlistQueryService(CreateRulePage(1), CreateHotlistPage(1)), diffService);
            IArchonMcpSnapshotDiffTool tool = app.Services.GetRequiredService<IArchonMcpSnapshotDiffTool>();

            object latestPayload = await tool.GetSnapshotDiffAsync(CreateLatestDiffRequest(), CancellationToken.None);
            object invalidPayload = await tool.GetSnapshotDiffAsync(CreateLatestDiffRequest(currentSnapshotStableKey: "snapshot://current"), CancellationToken.None);

            // Latest mode should produce a known no-change response and validation should prevent mixed-mode ambiguity.
            ArchonMcpEnvelope<ArchonMcpSnapshotDiffFacts> latestEnvelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpSnapshotDiffFacts>>(latestPayload);
            Assert.False(latestEnvelope.Facts.HasChanges);
            Assert.Contains(latestEnvelope.Warnings, warning => warning.Code == "noChanges");
            Assert.Single(diffService.LatestQueries);
            ArchonMcpErrorResponse invalidError = Assert.IsType<ArchonMcpErrorResponse>(invalidPayload);
            Assert.Equal(ArchonMcpErrorCategory.Validation, invalidError.Error.Category);
            Assert.Single(diffService.LatestQueries);
        }

        /// <summary>
        /// Confirms snapshot diff maps missing snapshots, unavailable scope, and query failures into safe structured errors.
        /// </summary>
        [Fact]
        public async Task GetSnapshotDiffMapsNotFoundUnavailableAndQueryFailuresSafely()
        {
            // Separate fake services exercise the three externally visible failure categories without exposing exception details.
            using WebApplication missingApp = BuildRulesHotlistDiffApp(new FakeHotlistQueryService(CreateRulePage(1), CreateHotlistPage(1)), new FakeSnapshotDiffService(CreateDiffFailure(SnapshotDiffValidationCodes.CurrentSnapshotNotFound)));
            IArchonMcpSnapshotDiffTool missingTool = missingApp.Services.GetRequiredService<IArchonMcpSnapshotDiffTool>();
            object missingPayload = await missingTool.GetSnapshotDiffAsync(CreateExplicitDiffRequest(), CancellationToken.None);

            using WebApplication unavailableApp = BuildRulesHotlistDiffApp(new FakeHotlistQueryService(CreateRulePage(1), CreateHotlistPage(1)), new FakeSnapshotDiffService(CreateDiffFailure(SnapshotDiffValidationCodes.RepositoryNotFound)));
            IArchonMcpSnapshotDiffTool unavailableTool = unavailableApp.Services.GetRequiredService<IArchonMcpSnapshotDiffTool>();
            object unavailablePayload = await unavailableTool.GetSnapshotDiffAsync(CreateExplicitDiffRequest(), CancellationToken.None);

            using WebApplication failingApp = BuildRulesHotlistDiffApp(new FakeHotlistQueryService(CreateRulePage(1), CreateHotlistPage(1)), new FakeSnapshotDiffService(CreateDiffResult(changed: true, detailCount: 1), throwOnCompare: true));
            IArchonMcpSnapshotDiffTool failingTool = failingApp.Services.GetRequiredService<IArchonMcpSnapshotDiffTool>();
            object failingPayload = await failingTool.GetSnapshotDiffAsync(CreateExplicitDiffRequest(), CancellationToken.None);

            // Public errors use coarse categories and omit exception type, stack trace, and snapshot persistence internals.
            Assert.Equal(ArchonMcpErrorCategory.NotFound, Assert.IsType<ArchonMcpErrorResponse>(missingPayload).Error.Category);
            Assert.Equal(ArchonMcpErrorCategory.DependencyUnavailable, Assert.IsType<ArchonMcpErrorResponse>(unavailablePayload).Error.Category);
            ArchonMcpErrorResponse failingError = Assert.IsType<ArchonMcpErrorResponse>(failingPayload);
            Assert.Equal(ArchonMcpErrorCategory.QueryLayerFailure, failingError.Error.Category);
            Assert.DoesNotContain("InvalidOperationException", failingError.Error.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds an MCP host application with fake rule, hotlist, and snapshot diff query services plus configurable security allow-list settings.
        /// </summary>
        /// <param name="hotlistService">The fake hotlist query service registered for rules and finding tests.</param>
        /// <param name="snapshotDiffService">The fake snapshot diff service registered for diff tests.</param>
        /// <param name="allowedOperations">The optional operation allow-list used by security tests.</param>
        /// <returns>A configured web application exposing Work Item 8 MCP services.</returns>
        private static WebApplication BuildRulesHotlistDiffApp(FakeHotlistQueryService hotlistService, FakeSnapshotDiffService snapshotDiffService, string[]? allowedOperations = null)
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
            string[] operations = allowedOperations ?? ["archon.health", ArchonMcpRulesOperations.GetArchitectureRules, ArchonMcpHotlistOperations.GetHotlistFindings, ArchonMcpSnapshotDiffOperations.GetSnapshotDiff];
            for (int index = 0; index < operations.Length; index++)
            {
                args.Add($"Archon:Mcp:Security:AllowedOperations:{index}={operations[index]}");
            }

            return Program.BuildApplication(args.ToArray(), builder =>
            {
                // Replacing query services with fakes keeps assertions independent of persistence implementation details.
                builder.Services.AddSingleton<IHotlistQueryService>(hotlistService);
                builder.Services.AddSingleton<ISnapshotDiffService>(snapshotDiffService);
            });
        }

        /// <summary>
        /// Creates a deterministic hotlist request for MCP handler tests.
        /// </summary>
        /// <param name="limit">The optional maximum number of findings to return.</param>
        /// <param name="sortBy">The optional deterministic sort field.</param>
        /// <param name="searchText">The optional safe text search filter.</param>
        /// <returns>A hotlist finding request.</returns>
        private static ArchonMcpHotlistFindingsRequest CreateHotlistRequest(int? limit = 2, string? sortBy = "severity", string? searchText = null)
        {
            // Stable scope values satisfy common MCP validation and query-layer selector requirements.
            return new ArchonMcpHotlistFindingsRequest(
                "project://legacy/app",
                "ARCH001",
                "Modernization",
                "High",
                "Active",
                "snapshot://current",
                searchText,
                sortBy,
                limit,
                "repository://archon-test",
                "solution://archon-test/main");
        }

        /// <summary>
        /// Creates an explicit snapshot diff request for MCP handler tests.
        /// </summary>
        /// <param name="limit">The optional maximum number of detail rows to return.</param>
        /// <returns>An explicit snapshot diff request.</returns>
        private static ArchonMcpSnapshotDiffRequest CreateExplicitDiffRequest(int? limit = 2)
        {
            // Explicit mode supplies both snapshot stable keys and keeps latest-to-previous behavior disabled.
            return new ArchonMcpSnapshotDiffRequest(
                "snapshot://current",
                "snapshot://previous",
                UseLatestComparableSnapshots: false,
                RepositoryStableKey: null,
                SolutionStableKey: null,
                Domains: ["Nodes", "Findings"],
                ChangeKinds: ["Added", "Changed"],
                ProjectStableKey: "project://legacy/app",
                TargetStableKey: null,
                RecordKind: null,
                Severity: null,
                IncludeDetails: true,
                IncludeUnchangedDetails: false,
                limit);
        }

        /// <summary>
        /// Creates a latest-to-previous snapshot diff request for MCP handler tests.
        /// </summary>
        /// <param name="currentSnapshotStableKey">An optional explicit current snapshot key used to exercise validation.</param>
        /// <returns>A latest-to-previous snapshot diff request.</returns>
        private static ArchonMcpSnapshotDiffRequest CreateLatestDiffRequest(string? currentSnapshotStableKey = null)
        {
            // Latest mode intentionally omits explicit snapshot keys unless a validation scenario supplies one.
            return new ArchonMcpSnapshotDiffRequest(
                currentSnapshotStableKey,
                PreviousSnapshotStableKey: null,
                UseLatestComparableSnapshots: true,
                "repository://archon-test",
                "solution://archon-test/main",
                Domains: ["Nodes"],
                ChangeKinds: ["Changed"],
                ProjectStableKey: null,
                TargetStableKey: null,
                RecordKind: null,
                Severity: null,
                IncludeDetails: true,
                IncludeUnchangedDetails: false,
                Limit: 2);
        }

        /// <summary>
        /// Creates a deterministic page of rule catalog DTOs for tests.
        /// </summary>
        /// <param name="count">The number of rule rows to include before MCP limiting.</param>
        /// <returns>A rule catalog page.</returns>
        private static PagedQueryResult<RuleCatalogItemDto> CreateRulePage(int count)
        {
            // Rules vary by code and category so tests can assert ordering and filter mapping.
            RuleCatalogItemDto[] rules =
            [
                new RuleCatalogItemDto("ARCH001", "1.0.0", "Layering", "Layering", "High", "Active", enabled: true, builtIn: true, ownerScope: null, "Projects must respect onion layering.", ["project", "dependency"]),
                new RuleCatalogItemDto("ARCH002", "1.0.0", "Data Access", "Modernization", "Medium", "Active", enabled: true, builtIn: true, ownerScope: null, "Legacy data access should be reviewed.", ["data-access"]),
                new RuleCatalogItemDto("ARCH003", "1.0.0", "Endpoints", "Api", "Low", "Active", enabled: false, builtIn: true, ownerScope: null, "Endpoints should be discoverable.", ["endpoint"])
            ];
            return new PagedQueryResult<RuleCatalogItemDto>(rules.Take(count), count, skip: 0, take: Math.Max(1, count));
        }

        /// <summary>
        /// Creates a deterministic page of hotlist DTOs for tests.
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
        /// Creates a deterministic successful snapshot diff result for tests.
        /// </summary>
        /// <param name="changed">Indicates whether the summary should include changes.</param>
        /// <param name="detailCount">The number of detail rows to include before MCP limiting.</param>
        /// <returns>A successful snapshot diff result.</returns>
        private static SnapshotDiffResult CreateDiffResult(bool changed, int detailCount)
        {
            // Summaries and details use stable keys and fingerprints so MCP can prove no raw graph identifiers are required.
            SnapshotDiffSummaryDto[] summaries = changed
                ? [new SnapshotDiffSummaryDto("Nodes", AddedCount: 1, RemovedCount: 0, ChangedCount: 2, UnchangedCount: 5)]
                : [new SnapshotDiffSummaryDto("Nodes", AddedCount: 0, RemovedCount: 0, ChangedCount: 0, UnchangedCount: 7)];
            SnapshotDiffItemDto[] details = Enumerable.Range(0, detailCount)
                .Select(index => new SnapshotDiffItemDto(
                    "Nodes",
                    index == 0 ? "Changed" : "Added",
                    $"symbol://legacy/type-{index}",
                    $"LegacyType{index}",
                    "Symbol",
                    "project://legacy/app",
                    [$"project://legacy/app"],
                    Severity: null,
                    PreviousFingerprint: index == 0 ? $"fingerprint-previous-{index}" : null,
                    CurrentFingerprint: $"fingerprint-current-{index}",
                    ChangedFields: ["relationships"],
                    EvidenceStableKeys: [$"evidence://diff/{index}"],
                    HasUnknownData: index == 0,
                    UnknownReason: index == 0 ? "One changed field could not be fully classified." : null))
                .ToArray();
            SnapshotDiffTruncationDto truncation = new(detailCount > 2, detailCount, Math.Min(detailCount, 2), Skip: 0, Take: Math.Max(1, detailCount));
            return new SnapshotDiffResult("snapshot://current", "snapshot://previous", "repository://archon-test", summaries, details, truncation);
        }

        /// <summary>
        /// Creates a deterministic failed snapshot diff result for tests.
        /// </summary>
        /// <param name="code">The validation code to include in the failure.</param>
        /// <returns>A failed snapshot diff result.</returns>
        private static SnapshotDiffResult CreateDiffFailure(string code)
        {
            // The failure carries only public validation information and no exception or persistence details.
            return new SnapshotDiffResult("snapshot://current", "snapshot://previous", [new SnapshotDiffValidationError(code, $"Validation failed with {code}.")]);
        }

        /// <summary>
        /// Provides deterministic rule and hotlist query behavior for MCP tests.
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
                // Rule detail is outside Work Item 8 MCP scope, so the fake returns no detail.
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
                // Finding detail is outside Work Item 8 MCP scope, so the fake returns no detail.
                return Task.FromResult<FindingDetailDto?>(null);
            }

            /// <inheritdoc />
            public Task<FindingHistoryDto?> GetFindingHistoryAsync(string historyKey, CancellationToken cancellationToken)
            {
                // Finding history is outside the current MCP list tool, so the fake returns no history.
                return Task.FromResult<FindingHistoryDto?>(null);
            }

            /// <inheritdoc />
            public Task<SuppressionCommandResult> SuppressFindingAsync(SuppressFindingCommand command, CancellationToken cancellationToken)
            {
                // Suppression is deliberately unsupported by MCP; throwing ensures tests would fail if a handler attempted mutation.
                throw new InvalidOperationException("MCP tests must not invoke suppression mutation.");
            }
        }

        /// <summary>
        /// Provides deterministic snapshot diff behavior for MCP tests.
        /// </summary>
        private sealed class FakeSnapshotDiffService : ISnapshotDiffService
        {
            /// <summary>
            /// Stores the diff result returned by explicit and latest comparison queries.
            /// </summary>
            private readonly SnapshotDiffResult _result;

            /// <summary>
            /// Indicates whether compare calls should throw to exercise safe query-layer failure mapping.
            /// </summary>
            private readonly bool _throwOnCompare;

            /// <summary>
            /// Creates a fake snapshot diff service with deterministic behavior.
            /// </summary>
            /// <param name="result">The result returned by compare operations.</param>
            /// <param name="throwOnCompare">Indicates whether compare operations should throw.</param>
            public FakeSnapshotDiffService(SnapshotDiffResult result, bool throwOnCompare = false)
            {
                // Captured query lists let tests prove validation and authorization stop calls when expected.
                _result = result;
                _throwOnCompare = throwOnCompare;
            }

            /// <summary>
            /// Gets captured explicit snapshot diff queries.
            /// </summary>
            public List<SnapshotDiffQuery> ExplicitQueries { get; } = [];

            /// <summary>
            /// Gets captured latest-to-previous snapshot diff queries.
            /// </summary>
            public List<SnapshotDiffLatestQuery> LatestQueries { get; } = [];

            /// <inheritdoc />
            public Task<SnapshotDiffResult> CompareSnapshotsAsync(SnapshotDiffQuery query, CancellationToken cancellationToken)
            {
                // Explicit queries are captured before returning or throwing deterministic test behavior.
                ExplicitQueries.Add(query);
                if (_throwOnCompare)
                {
                    throw new InvalidOperationException("Unexpected snapshot diff failure that must be hidden from MCP clients.");
                }

                return Task.FromResult(_result);
            }

            /// <inheritdoc />
            public Task<SnapshotDiffResult> CompareLatestToPreviousAsync(SnapshotDiffLatestQuery query, CancellationToken cancellationToken)
            {
                // Latest queries are captured before returning or throwing deterministic test behavior.
                LatestQueries.Add(query);
                if (_throwOnCompare)
                {
                    throw new InvalidOperationException("Unexpected latest snapshot diff failure that must be hidden from MCP clients.");
                }

                return Task.FromResult(_result);
            }
        }
    }
}
