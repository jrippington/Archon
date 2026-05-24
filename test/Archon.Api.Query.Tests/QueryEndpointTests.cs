using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Archon.Api.Query.Contracts;
using Archon.Application.ArchitectureRules;
using Archon.Application.Cycles;
using Archon.Application.Diff;
using Archon.Application.Graph.Persistence;
using Archon.Application.Hotspots;
using Archon.Application.Metrics;
using Archon.Application.Rules;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Archon.Api.Query.Tests
{
    /// <summary>
    /// Verifies WP012 query endpoints expose controlled rule catalog, hotlist, finding detail, history, and suppression behavior.
    /// </summary>
    public sealed class QueryEndpointTests
    {
        /// <summary>
        /// Confirms the rule catalog list and detail endpoints return controlled DTOs with filters and without raw graph access.
        /// </summary>
        /// <returns>A task that completes after HTTP responses are asserted.</returns>
        [Fact]
        public async Task RuleCatalogEndpoints_WhenRulesExist_ShouldListFilterAndReturnDetail()
        {
            // The in-memory test host exercises route mapping, DI registration, and JSON contracts without starting Kestrel or Aspire.
            RuleCatalogEntry firstRule = CreateRule("ARCHON-RULE-A", "1.0.0", RuleCategory.Lifecycle, FindingSeverity.High, enabled: true, builtIn: true, ownerScope: "Archon");
            RuleCatalogEntry secondRule = CreateRule("ARCHON-RULE-B", "2.0.0", RuleCategory.SecuritySensitive, FindingSeverity.Critical, enabled: false, builtIn: false, ownerScope: "TeamA");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IRuleCatalogStore catalog = services.GetRequiredService<IRuleCatalogStore>();
                await catalog.UpsertRulesAsync([firstRule, secondRule], CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument listBody = await GetJsonAsync(client, "/rules?category=Lifecycle&enabled=true&take=5");
            JsonDocument detailBody = await GetJsonAsync(client, "/rules/ARCHON-RULE-A/1.0.0");

            using (listBody)
            using (detailBody)
            {
                Assert.Equal(1, listBody.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement item = Assert.Single(listBody.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("ARCHON-RULE-A", item.GetProperty("ruleCode").GetString());
                Assert.Equal("Lifecycle", item.GetProperty("category").GetString());
                Assert.Equal("High", item.GetProperty("severity").GetString());
                Assert.True(item.GetProperty("enabled").GetBoolean());
                Assert.Equal("ARCHON-RULE-A", detailBody.RootElement.GetProperty("item").GetProperty("ruleCode").GetString());
                Assert.Equal("Flags a modernization concern.", detailBody.RootElement.GetProperty("description").GetString());
                Assert.False(detailBody.RootElement.GetProperty("metadata").GetProperty("isEmpty").GetBoolean());
            }
        }

        /// <summary>
        /// Confirms snapshot hotspots endpoint returns stable DTOs with score, rank, contribution fields, and controlled filters.
        /// </summary>
        /// <returns>A task that completes after the hotspot response is asserted.</returns>
        [Fact]
        public async Task HotspotsEndpoint_WhenHotspotsExist_ShouldReturnFilteredStableHotspotDtos()
        {
            // Hotspots are derived from persisted snapshot metrics and graph nodes rather than raw graph query text.
            StableKey snapshotStableKey = new("snapshot://hotspot-api");
            StableKey projectStableKey = new("project://src/Hotspot.Shared/Hotspot.Shared.csproj");
            MetricRecord fanInMetric = CreateMetric(snapshotStableKey.Value, "metric://hotspot-api/fan-in", "GraphFanIn", 9, MetricScopeKind.Node, projectStableKey, "edges");
            MetricRecord fanOutMetric = CreateMetric(snapshotStableKey.Value, "metric://hotspot-api/fan-out", "GraphFanOut", 6, MetricScopeKind.Node, projectStableKey, "edges");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateHotspotSnapshot(snapshotStableKey, [CreateProjectNode(snapshotStableKey, projectStableKey)], [fanInMetric, fanOutMetric], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/snapshot-hotspots?snapshotStableKey=snapshot%3A%2F%2Fhotspot-api&category=HighFanIn&targetStableKey=project%3A%2F%2Fsrc%2FHotspot.Shared%2FHotspot.Shared.csproj&take=5");

            using (body)
            {
                Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("snapshot://hotspot-api", item.GetProperty("snapshotStableKey").GetString());
                Assert.StartsWith("hotspot://snapshot://hotspot-api/HighFanIn/", item.GetProperty("stableKey").GetString(), StringComparison.Ordinal);
                Assert.Equal("HighFanIn", item.GetProperty("category").GetString());
                Assert.Equal(projectStableKey.Value, item.GetProperty("targetStableKey").GetString());
                Assert.Equal("Project", item.GetProperty("targetKind").GetString());
                Assert.Equal("Hotspot.Shared.csproj", item.GetProperty("displayName").GetString());
                Assert.Equal(9, item.GetProperty("score").GetDecimal());
                Assert.Equal(1, item.GetProperty("rank").GetInt32());
                string metricStableKey = Assert.Single(item.GetProperty("contributingMetricStableKeys").EnumerateArray()).GetString()!;
                Assert.Equal(fanInMetric.StableKey.Value, metricStableKey);
                Assert.Empty(item.GetProperty("contributingFindingStableKeys").EnumerateArray());
                Assert.False(item.GetProperty("hasUnknownData").GetBoolean());
                Assert.StartsWith("sha256:", item.GetProperty("fingerprint").GetString(), StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Confirms snapshot hotspots endpoint returns validation problems for missing required snapshot identity.
        /// </summary>
        /// <returns>A task that completes after the validation response is asserted.</returns>
        [Fact]
        public async Task HotspotsEndpoint_WhenSnapshotKeyIsMissing_ShouldReturnValidationProblem()
        {
            // The endpoint requires explicit snapshot scope so callers cannot request unbounded hotspot evaluation.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.GetAsync("/snapshot-hotspots?take=5");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        /// <summary>
        /// Confirms architecture-rule results endpoint returns stable DTOs with contribution fields and controlled filters.
        /// </summary>
        /// <returns>A task that completes after the architecture-rule response is asserted.</returns>
        [Fact]
        public async Task ArchitectureRulesEndpoint_WhenRuleResultsExist_ShouldReturnFilteredStableDtos()
        {
            // The endpoint evaluates persisted snapshot graph facts through fixed filters rather than accepting arbitrary graph predicates.
            StableKey snapshotStableKey = new("snapshot://architecture-rule-api");
            StableKey domainKey = new("project://src/Api.Domain/Api.Domain.csproj");
            StableKey infrastructureKey = new("project://src/Api.Infrastructure/Api.Infrastructure.csproj");
            ArchitectureEdge edge = CreateEdge(snapshotStableKey, "edge://architecture-rule-api/domain-infra", EdgeKind.References, domainKey, infrastructureKey, "evidence://architecture-rule-api/domain-infra");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateArchitectureRuleSnapshot(
                    snapshotStableKey,
                    [CreateProjectNode(snapshotStableKey, domainKey), CreateProjectNode(snapshotStableKey, infrastructureKey)],
                    [edge],
                    [],
                    []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/snapshot-architecture-rules?snapshotStableKey=snapshot%3A%2F%2Farchitecture-rule-api&category=ArchitectureLayering&status=Violation&targetStableKey=project%3A%2F%2Fsrc%2FApi.Domain%2FApi.Domain.csproj&take=5");

            using (body)
            {
                Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("snapshot://architecture-rule-api", item.GetProperty("snapshotStableKey").GetString());
                Assert.StartsWith("architecture-rule://snapshot://architecture-rule-api/", item.GetProperty("stableKey").GetString(), StringComparison.Ordinal);
                Assert.Equal(ArchitectureRuleChecks.DomainReferencesInfrastructure, item.GetProperty("ruleCode").GetString());
                Assert.Equal("ArchitectureLayering", item.GetProperty("category").GetString());
                Assert.Equal("Violation", item.GetProperty("status").GetString());
                Assert.Equal(domainKey.Value, item.GetProperty("targetStableKey").GetString());
                Assert.Equal("Project", item.GetProperty("targetKind").GetString());
                Assert.Equal("Api.Domain.csproj", item.GetProperty("displayName").GetString());
                Assert.Equal(edge.StableKey.Value, Assert.Single(item.GetProperty("contributingEdgeStableKeys").EnumerateArray()).GetString());
                Assert.Equal(edge.PrimaryEvidenceStableKey!.Value.Value, Assert.Single(item.GetProperty("evidenceStableKeys").EnumerateArray()).GetString());
                Assert.Empty(item.GetProperty("contributingMetricStableKeys").EnumerateArray());
                Assert.Empty(item.GetProperty("contributingFindingStableKeys").EnumerateArray());
                Assert.Equal(1m, item.GetProperty("confidence").GetDecimal());
                Assert.False(item.GetProperty("hasUnknownData").GetBoolean());
                Assert.StartsWith("sha256:", item.GetProperty("fingerprint").GetString(), StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Confirms architecture-rule endpoint returns unknown-state fields for checks that cannot prove required runtime dependencies.
        /// </summary>
        /// <returns>A task that completes after the unknown-state architecture-rule response is asserted.</returns>
        [Fact]
        public async Task ArchitectureRulesEndpoint_WhenWorkerMessagingEvidenceIsIncomplete_ShouldReturnUnknownStateFields()
        {
            // Worker messaging uses an unknown result when metadata indicates messaging should exist but no queue or topic dependency edge was observed.
            StableKey snapshotStableKey = new("snapshot://architecture-rule-worker-api");
            StableKey workerKey = new("project://src/Import.Worker/Import.Worker.csproj");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateArchitectureRuleSnapshot(
                    snapshotStableKey,
                    [CreateProjectNode(snapshotStableKey, workerKey, "Import.Worker", GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["architecture.layer"] = "Worker",
                        ["runtime.messagingExpected"] = true
                    }))],
                    [],
                    [],
                    []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/snapshot-architecture-rules?snapshotStableKey=snapshot%3A%2F%2Farchitecture-rule-worker-api&ruleCategory=DependencyRisk&status=Unknown&take=5");

            using (body)
            {
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(ArchitectureRuleChecks.WorkerMissingQueueOrTopicDependency, item.GetProperty("ruleCode").GetString());
                Assert.Equal("Unknown", item.GetProperty("status").GetString());
                Assert.True(item.GetProperty("hasUnknownData").GetBoolean());
                Assert.Contains("queue or topic", item.GetProperty("unknownReason").GetString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms architecture-rule endpoint validates required snapshot identity before evaluating rule results.
        /// </summary>
        /// <returns>A task that completes after the validation response is asserted.</returns>
        [Fact]
        public async Task ArchitectureRulesEndpoint_WhenSnapshotKeyIsMissing_ShouldReturnValidationProblem()
        {
            // The endpoint requires explicit snapshot scope to avoid evaluating every persisted diagnostic snapshot accidentally.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.GetAsync("/snapshot-architecture-rules?take=5");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        /// <summary>
        /// Confirms snapshot diff endpoint returns stable public identities, summaries, filters, and truncation metadata.
        /// </summary>
        /// <returns>A task that completes after the snapshot diff response is asserted.</returns>
        [Fact]
        public async Task SnapshotDiffEndpoint_WhenSnapshotsAreComparable_ShouldReturnFilteredDiffDtos()
        {
            // The endpoint writes two snapshots and compares them through stable keys so HTTP behavior proves the public diff contract.
            StableKey repositoryStableKey = new("repository://snapshot-diff-api");
            StableKey previousSnapshot = new("snapshot://snapshot-diff-api/previous");
            StableKey currentSnapshot = new("snapshot://snapshot-diff-api/current");
            StableKey changedNode = new("project://src/Diff.Api/Diff.Api.csproj");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateDiffSnapshot(
                    previousSnapshot,
                    repositoryStableKey,
                    [CreateProjectNode(previousSnapshot, changedNode, "Diff.Api.csproj", GraphMetadata.Empty, "sha256:previous-node")],
                    [],
                    [],
                    []), CancellationToken.None);
                await writer.WriteSnapshotAsync(CreateDiffSnapshot(
                    currentSnapshot,
                    repositoryStableKey,
                    [
                        CreateProjectNode(currentSnapshot, changedNode, "Diff.Api.csproj", GraphMetadata.Empty, "sha256:current-node"),
                        CreateProjectNode(currentSnapshot, new StableKey("project://src/Diff.Added/Diff.Added.csproj"), "Diff.Added.csproj", GraphMetadata.Empty, "sha256:added-node")
                    ],
                    [],
                    [],
                    []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/snapshot-diff?currentSnapshotStableKey=snapshot%3A%2F%2Fsnapshot-diff-api%2Fcurrent&previousSnapshotStableKey=snapshot%3A%2F%2Fsnapshot-diff-api%2Fprevious&domains=Nodes&changeKinds=Changed&includeUnchangedDetails=false&take=5");

            using (body)
            {
                Assert.Equal("snapshot://snapshot-diff-api/current", body.RootElement.GetProperty("currentSnapshotStableKey").GetString());
                Assert.Equal("snapshot://snapshot-diff-api/previous", body.RootElement.GetProperty("previousSnapshotStableKey").GetString());
                Assert.True(body.RootElement.GetProperty("succeeded").GetBoolean());
                Assert.False(body.RootElement.GetProperty("truncation").GetProperty("truncated").GetBoolean());
                JsonElement nodeSummary = Assert.Single(body.RootElement.GetProperty("summaries").EnumerateArray());
                Assert.Equal(SnapshotDiffDomains.Nodes, nodeSummary.GetProperty("domain").GetString());
                Assert.Equal(1, nodeSummary.GetProperty("addedCount").GetInt32());
                Assert.Equal(1, nodeSummary.GetProperty("changedCount").GetInt32());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(SnapshotDiffDomains.Nodes, item.GetProperty("domain").GetString());
                Assert.Equal(SnapshotDiffChangeKind.Changed, item.GetProperty("changeKind").GetString());
                Assert.Equal(changedNode.Value, item.GetProperty("stableKey").GetString());
                Assert.Equal("sha256:previous-node", item.GetProperty("previousFingerprint").GetString());
                Assert.Equal("sha256:current-node", item.GetProperty("currentFingerprint").GetString());
                Assert.Contains("fingerprint", item.GetProperty("changedFields").EnumerateArray().Select(static field => field.GetString()));
            }
        }

        /// <summary>
        /// Confirms snapshot diff endpoint converts missing and incompatible snapshots into validation problem responses.
        /// </summary>
        /// <returns>A task that completes after validation responses are asserted.</returns>
        [Fact]
        public async Task SnapshotDiffEndpoint_WhenRequestIsInvalid_ShouldReturnValidationProblem()
        {
            // Validation is asserted through HTTP so callers receive problem details instead of application exceptions.
            StableKey previousSnapshot = new("snapshot://snapshot-diff-validation/previous");
            StableKey currentSnapshot = new("snapshot://snapshot-diff-validation/current");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateDiffSnapshot(previousSnapshot, new StableKey("repository://one"), [], [], [], []), CancellationToken.None);
                await writer.WriteSnapshotAsync(CreateDiffSnapshot(currentSnapshot, new StableKey("repository://two"), [], [], [], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage missingResponse = await client.GetAsync("/snapshot-diff?previousSnapshotStableKey=snapshot%3A%2F%2Fsnapshot-diff-validation%2Fprevious");
            HttpResponseMessage incompatibleResponse = await client.GetAsync("/snapshot-diff?currentSnapshotStableKey=snapshot%3A%2F%2Fsnapshot-diff-validation%2Fcurrent&previousSnapshotStableKey=snapshot%3A%2F%2Fsnapshot-diff-validation%2Fprevious");

            Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, incompatibleResponse.StatusCode);
        }

        /// <summary>
        /// Confirms snapshot diff endpoint supports deterministic continuation metadata when detail rows are truncated.
        /// </summary>
        /// <returns>A task that completes after truncation response fields are asserted.</returns>
        [Fact]
        public async Task SnapshotDiffEndpoint_WhenResultIsTruncated_ShouldReturnContinuationMetadata()
        {
            // The request asks for the second sorted added node so continuation metadata and deterministic ordering are verified together.
            StableKey repositoryStableKey = new("repository://snapshot-diff-truncation");
            StableKey previousSnapshot = new("snapshot://snapshot-diff-truncation/previous");
            StableKey currentSnapshot = new("snapshot://snapshot-diff-truncation/current");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateDiffSnapshot(previousSnapshot, repositoryStableKey, [], [], [], []), CancellationToken.None);
                await writer.WriteSnapshotAsync(CreateDiffSnapshot(
                    currentSnapshot,
                    repositoryStableKey,
                    [
                        CreateProjectNode(currentSnapshot, new StableKey("project://src/A/A.csproj"), "A.csproj", GraphMetadata.Empty, "sha256:a"),
                        CreateProjectNode(currentSnapshot, new StableKey("project://src/B/B.csproj"), "B.csproj", GraphMetadata.Empty, "sha256:b")
                    ],
                    [],
                    [],
                    []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/snapshot-diff?currentSnapshotStableKey=snapshot%3A%2F%2Fsnapshot-diff-truncation%2Fcurrent&previousSnapshotStableKey=snapshot%3A%2F%2Fsnapshot-diff-truncation%2Fprevious&domains=Nodes&skip=1&take=1");

            using (body)
            {
                Assert.True(body.RootElement.GetProperty("truncation").GetProperty("truncated").GetBoolean());
                Assert.Equal(2, body.RootElement.GetProperty("truncation").GetProperty("totalAvailableItems").GetInt32());
                Assert.Equal(1, body.RootElement.GetProperty("truncation").GetProperty("skip").GetInt32());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("project://src/B/B.csproj", item.GetProperty("stableKey").GetString());
            }
        }

        /// <summary>
        /// Confirms every WP013 paged endpoint rejects invalid paging values through deterministic validation-problem responses.
        /// </summary>
        /// <returns>A task that completes after each endpoint response is asserted.</returns>
        [Fact]
        public async Task Wp013Endpoints_WhenPagingIsInvalid_ShouldReturnValidationProblems()
        {
            // Invalid paging should be client-correctable instead of silently clamped so MCP consumers can repair requests predictably.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            (string Uri, string ExpectedKey)[] requests =
            [
                ("/snapshot-metrics?snapshotStableKey=snapshot%3A%2F%2Fmissing&skip=-1", "skip"),
                ("/snapshot-cycles?snapshotStableKey=snapshot%3A%2F%2Fmissing&take=0", "take"),
                ("/snapshot-hotspots?snapshotStableKey=snapshot%3A%2F%2Fmissing&take=501", "take"),
                ("/snapshot-architecture-rules?snapshotStableKey=snapshot%3A%2F%2Fmissing&skip=-5", "skip"),
                ("/snapshot-diff?currentSnapshotStableKey=snapshot%3A%2F%2Fcurrent&previousSnapshotStableKey=snapshot%3A%2F%2Fprevious&take=0", "TakeInvalid")
            ];

            foreach ((string uri, string expectedKey) in requests)
            {
                HttpResponseMessage response = await client.GetAsync(uri);
                JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

                using (body)
                {
                    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                    Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(expectedKey, out JsonElement errors), "Expected validation key '" + expectedKey + "' was not present in response: " + body.RootElement.ToString());
                    Assert.NotEmpty(errors.EnumerateArray());
                }
            }
        }

        /// <summary>
        /// Confirms WP013 item responses expose consistent stable identity, confidence, unknown-state, evidence, metadata, and fingerprint fields.
        /// </summary>
        /// <returns>A task that completes after representative endpoint responses are asserted.</returns>
        [Fact]
        public async Task Wp013Endpoints_WhenItemsAreReturned_ShouldExposeConsistentMachineReadableFields()
        {
            // The representative fixture covers all non-diff WP013 list endpoints so field conventions remain stable for future MCP tools.
            StableKey snapshotStableKey = new("snapshot://wp013-consistency");
            StableKey apiNodeKey = new("project://src/Consistency.Api/Consistency.Api.csproj");
            StableKey domainNodeKey = new("project://src/Consistency.Domain/Consistency.Domain.csproj");
            StableKey infraNodeKey = new("project://src/Consistency.Infrastructure/Consistency.Infrastructure.csproj");
            MetricRecord metric = CreateMetricWithMetadata(snapshotStableKey.Value, "metric://wp013-consistency/fan-in", "GraphFanIn", 9, MetricScopeKind.Node, apiNodeKey, "edges");
            ArchitectureEdge edge = CreateEdge(snapshotStableKey, "edge://wp013-consistency/api-domain", EdgeKind.References, apiNodeKey, domainNodeKey, "evidence://wp013-consistency/api-domain");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateArchitectureRuleSnapshot(
                    snapshotStableKey,
                    [CreateProjectNode(snapshotStableKey, apiNodeKey), CreateProjectNode(snapshotStableKey, domainNodeKey), CreateProjectNode(snapshotStableKey, infraNodeKey)],
                    [
                        edge,
                        CreateEdge(snapshotStableKey, "edge://wp013-consistency/domain-api", EdgeKind.References, domainNodeKey, apiNodeKey, "evidence://wp013-consistency/domain-api"),
                        CreateEdge(snapshotStableKey, "edge://wp013-consistency/domain-infra", EdgeKind.References, domainNodeKey, infraNodeKey, "evidence://wp013-consistency/domain-infra")
                    ],
                    [metric],
                    []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument metricsBody = await GetJsonAsync(client, "/snapshot-metrics?snapshotStableKey=snapshot%3A%2F%2Fwp013-consistency&metricKind=GraphFanIn&take=5");
            JsonDocument cyclesBody = await GetJsonAsync(client, "/snapshot-cycles?snapshotStableKey=snapshot%3A%2F%2Fwp013-consistency&take=5");
            JsonDocument hotspotsBody = await GetJsonAsync(client, "/snapshot-hotspots?snapshotStableKey=snapshot%3A%2F%2Fwp013-consistency&category=HighFanIn&take=5");
            JsonDocument architectureRulesBody = await GetJsonAsync(client, "/snapshot-architecture-rules?snapshotStableKey=snapshot%3A%2F%2Fwp013-consistency&category=ArchitectureLayering&take=5");

            using (metricsBody)
            using (cyclesBody)
            using (hotspotsBody)
            using (architectureRulesBody)
            {
                AssertCommonItemFields(Assert.Single(metricsBody.RootElement.GetProperty("items").EnumerateArray()), "metric://", expectEvidenceArray: false, requireSafeMetadataValue: false);
                AssertCommonItemFields(Assert.Single(cyclesBody.RootElement.GetProperty("items").EnumerateArray()), "cycle://", expectEvidenceArray: true, requireSafeMetadataValue: false);
                AssertCommonItemFields(Assert.Single(hotspotsBody.RootElement.GetProperty("items").EnumerateArray()), "hotspot://", expectEvidenceArray: true, requireSafeMetadataValue: false);
                AssertCommonItemFields(Assert.Single(architectureRulesBody.RootElement.GetProperty("items").EnumerateArray()), "architecture-rule://", expectEvidenceArray: true, requireSafeMetadataValue: false);
            }
        }

        /// <summary>
        /// Confirms snapshot diff rejects unsupported change kinds before reporting snapshot lookup failures.
        /// </summary>
        /// <returns>A task that completes after the validation response is asserted.</returns>
        [Fact]
        public async Task SnapshotDiffEndpoint_WhenChangeKindIsUnsupported_ShouldReturnDeterministicValidationCode()
        {
            // Unsupported controlled filters should appear in problem details even when the requested snapshots are also missing.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.GetAsync("/snapshot-diff?currentSnapshotStableKey=snapshot%3A%2F%2Fcurrent&previousSnapshotStableKey=snapshot%3A%2F%2Fprevious&changeKinds=Added,Renamed");
            JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

            using (body)
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("UnsupportedChangeKind", out JsonElement errors));
                Assert.Contains(errors.EnumerateArray().Select(static error => error.GetString()), static message => message?.Contains("Renamed", StringComparison.Ordinal) == true);
            }
        }

        /// <summary>
        /// Creates a deterministic extracted snapshot containing graph nodes, metrics, and findings for hotspot endpoint tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot to create.</param>
        /// <param name="nodes">The graph nodes available to hotspot display-name resolution.</param>
        /// <param name="metrics">The metrics available to hotspot scoring.</param>
        /// <param name="findings">The findings available to hotspot concentration scoring.</param>
        /// <returns>An extracted architecture snapshot containing the supplied hotspot inputs.</returns>
        private static Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot CreateHotspotSnapshot(StableKey snapshotStableKey, IReadOnlyList<ArchitectureNode> nodes, IReadOnlyList<MetricRecord> metrics, IReadOnlyList<FindingRecord> findings)
        {
            // Hotspot query tests need nodes for display names plus metrics and findings for scoring input.
            StableKey repositoryStableKey = new("repository://hotspot-api");
            SnapshotHeader header = new(
                snapshotStableKey,
                repositoryStableKey,
                "main",
                "abcdef",
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 20, 8, 1, 0, TimeSpan.Zero),
                "wp013-hotspot-api-tests",
                "Completed",
                warnings: [],
                errors: [],
                GraphMetadata.Empty);
            RepositoryModel repository = new(repositoryStableKey, "HotspotApi", "D:/Repositories/HotspotApi", null, "main", GraphMetadata.Empty);
            return new Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot(header, [repository], [], nodes, [], [], [], findings, metrics, [], [], []);
        }

        /// <summary>
        /// Confirms snapshot metrics endpoint filters project-scoped metrics by project stable key and returns target identity fields.
        /// </summary>
        /// <returns>A task that completes after the project metric response is asserted.</returns>
        [Fact]
        public async Task MetricsEndpoint_WhenProjectFilterIsSupplied_ShouldReturnMatchingProjectMetrics()
        {
            // The query-string endpoint is used because both snapshot and project stable keys contain slash-like separators.
            StableKey projectStableKey = new("project://src/Metrics.Api/Metrics.Api.csproj");
            MetricRecord metric = CreateMetric("snapshot://metrics", "metric://snapshot://metrics/ProjectPackageCount/project://src/Metrics.Api/Metrics.Api.csproj", "ProjectPackageCount", 3, MetricScopeKind.Project, projectStableKey, "packages");
            MetricRecord otherMetric = CreateMetric("snapshot://metrics", "metric://snapshot://metrics/ProjectPackageCount/project://src/Metrics.Worker/Metrics.Worker.csproj", "ProjectPackageCount", 1, MetricScopeKind.Project, new StableKey("project://src/Metrics.Worker/Metrics.Worker.csproj"), "packages");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateMetricSnapshot("snapshot://metrics", [metric, otherMetric]), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/snapshot-metrics?snapshotStableKey=snapshot%3A%2F%2Fmetrics&metricKind=ProjectPackageCount&scopeKind=Project&projectStableKey=project%3A%2F%2Fsrc%2FMetrics.Api%2FMetrics.Api.csproj");

            using (body)
            {
                Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("ProjectPackageCount", item.GetProperty("metricKind").GetString());
                Assert.Equal("Project", item.GetProperty("scopeKind").GetString());
                Assert.Equal(projectStableKey.Value, item.GetProperty("nodeStableKey").GetString());
                Assert.Equal(3, item.GetProperty("numericValue").GetDecimal());
                Assert.Equal("packages", item.GetProperty("unit").GetString());
            }
        }

        /// <summary>
        /// Confirms snapshot metrics endpoint filters graph node-scoped metrics by architecture node stable key.
        /// </summary>
        /// <returns>A task that completes after the graph metric response is asserted.</returns>
        [Fact]
        public async Task MetricsEndpoint_WhenGraphNodeFilterIsSupplied_ShouldReturnMatchingGraphMetrics()
        {
            // Graph metrics reuse the stable node-target filter so API consumers can query one architecture node without raw graph access.
            StableKey apiNodeStableKey = new("project://src/Graph.Api/Graph.Api.csproj");
            MetricRecord metric = CreateMetric("snapshot://graph-metrics", "metric://snapshot://graph-metrics/GraphFanOut/project://src/Graph.Api/Graph.Api.csproj", "GraphFanOut", 2, MetricScopeKind.Node, apiNodeStableKey, "edges");
            MetricRecord otherMetric = CreateMetric("snapshot://graph-metrics", "metric://snapshot://graph-metrics/GraphFanOut/project://src/Graph.Domain/Graph.Domain.csproj", "GraphFanOut", 0, MetricScopeKind.Node, new StableKey("project://src/Graph.Domain/Graph.Domain.csproj"), "edges");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateMetricSnapshot("snapshot://graph-metrics", [metric, otherMetric]), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/snapshot-metrics?snapshotStableKey=snapshot%3A%2F%2Fgraph-metrics&metricKind=GraphFanOut&scopeKind=Node&projectStableKey=project%3A%2F%2Fsrc%2FGraph.Api%2FGraph.Api.csproj&take=5");

            using (body)
            {
                Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("GraphFanOut", item.GetProperty("metricKind").GetString());
                Assert.Equal("Node", item.GetProperty("scopeKind").GetString());
                Assert.Equal(apiNodeStableKey.Value, item.GetProperty("nodeStableKey").GetString());
                Assert.Equal(2, item.GetProperty("numericValue").GetDecimal());
                Assert.Equal("edges", item.GetProperty("unit").GetString());
            }
        }

        /// <summary>
        /// Confirms snapshot metrics endpoint exposes modernization metrics through the same stable filters and response fields as other metrics.
        /// </summary>
        /// <returns>A task that completes after the modernization metric response is asserted.</returns>
        [Fact]
        public async Task MetricsEndpoint_WhenModernizationMetricFilterIsSupplied_ShouldReturnMatchingModernizationMetrics()
        {
            // Modernization metrics are persisted as ordinary snapshot-owned metric records, so API filtering should not require a separate endpoint.
            StableKey projectStableKey = new("project://src/LegacyWeb/LegacyWeb.csproj");
            MetricRecord metric = CreateMetric("snapshot://modernization-metrics", "metric://snapshot://modernization-metrics/ModernizationOutOfSupportTargetCount/project://src/LegacyWeb/LegacyWeb.csproj", "ModernizationOutOfSupportTargetCount", 1, MetricScopeKind.Project, projectStableKey, "targets");
            MetricRecord otherMetric = CreateMetric("snapshot://modernization-metrics", "metric://snapshot://modernization-metrics/ModernizationOutOfSupportTargetCount/project://src/Current/Current.csproj", "ModernizationOutOfSupportTargetCount", 0, MetricScopeKind.Project, new StableKey("project://src/Current/Current.csproj"), "targets");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateMetricSnapshot("snapshot://modernization-metrics", [metric, otherMetric]), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/snapshot-metrics?snapshotStableKey=snapshot%3A%2F%2Fmodernization-metrics&metricKind=ModernizationOutOfSupportTargetCount&scopeKind=Project&projectStableKey=project%3A%2F%2Fsrc%2FLegacyWeb%2FLegacyWeb.csproj&take=5");

            using (body)
            {
                Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("ModernizationOutOfSupportTargetCount", item.GetProperty("metricKind").GetString());
                Assert.Equal("Project", item.GetProperty("scopeKind").GetString());
                Assert.Equal(projectStableKey.Value, item.GetProperty("nodeStableKey").GetString());
                Assert.Equal(1, item.GetProperty("numericValue").GetDecimal());
                Assert.Equal("targets", item.GetProperty("unit").GetString());
                Assert.Equal("sha256:metric-ModernizationOutOfSupportTargetCount", item.GetProperty("fingerprint").GetString());
            }
        }

        /// <summary>
        /// Confirms cycles endpoint returns deterministic cycle paths, evidence, truncation state, and stable public identities.
        /// </summary>
        /// <returns>A task that completes after the cycle response is asserted.</returns>
        [Fact]
        public async Task CyclesEndpoint_WhenCyclesExist_ShouldReturnStableCycleDtos()
        {
            // Cycles are queried from persisted snapshot graph facts and exposed through controlled filters rather than raw graph access.
            StableKey snapshotStableKey = new("snapshot://cycle-api");
            StableKey apiNodeKey = new("project://src/Cycle.Api/Cycle.Api.csproj");
            StableKey appNodeKey = new("project://src/Cycle.Application/Cycle.Application.csproj");
            StableKey domainNodeKey = new("project://src/Cycle.Domain/Cycle.Domain.csproj");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateCycleSnapshot(
                    snapshotStableKey,
                    [apiNodeKey, appNodeKey, domainNodeKey],
                    [
                        CreateEdge(snapshotStableKey, "edge://cycle-api/api-app", EdgeKind.References, apiNodeKey, appNodeKey, "evidence://cycle-api/api-app"),
                        CreateEdge(snapshotStableKey, "edge://cycle-api/app-domain", EdgeKind.References, appNodeKey, domainNodeKey, "evidence://cycle-api/app-domain"),
                        CreateEdge(snapshotStableKey, "edge://cycle-api/domain-api", EdgeKind.References, domainNodeKey, apiNodeKey, "evidence://cycle-api/domain-api")
                    ]), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/snapshot-cycles?snapshotStableKey=snapshot%3A%2F%2Fcycle-api&nodeStableKey=project%3A%2F%2Fsrc%2FCycle.Api%2FCycle.Api.csproj&take=5");

            using (body)
            {
                Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("snapshot://cycle-api", item.GetProperty("snapshotStableKey").GetString());
                Assert.StartsWith("cycle://snapshot://cycle-api/", item.GetProperty("stableKey").GetString(), StringComparison.Ordinal);
                string[] nodeStableKeys = item.GetProperty("nodeStableKeys").EnumerateArray().Select(static element => element.GetString()!).ToArray();
                string[] edgeStableKeys = item.GetProperty("edgeStableKeys").EnumerateArray().Select(static element => element.GetString()!).ToArray();
                string[] evidenceStableKeys = item.GetProperty("evidenceStableKeys").EnumerateArray().Select(static element => element.GetString()!).ToArray();
                Assert.Equal(new[] { apiNodeKey.Value, appNodeKey.Value, domainNodeKey.Value, apiNodeKey.Value }, nodeStableKeys);
                Assert.Equal(new[] { "edge://cycle-api/api-app", "edge://cycle-api/app-domain", "edge://cycle-api/domain-api" }, edgeStableKeys);
                Assert.Equal(new[] { "evidence://cycle-api/api-app", "evidence://cycle-api/app-domain", "evidence://cycle-api/domain-api" }, evidenceStableKeys);
                Assert.False(item.GetProperty("hasUnknownData").GetBoolean());
                Assert.False(item.GetProperty("truncated").GetBoolean());
                Assert.Equal(1m, item.GetProperty("confidence").GetDecimal());
                Assert.StartsWith("sha256:", item.GetProperty("fingerprint").GetString(), StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Confirms snapshot metrics endpoint returns persisted metrics with stable public identities and deterministic filters.
        /// </summary>
        /// <returns>A task that completes after the metrics response is asserted.</returns>
        [Fact]
        public async Task MetricsEndpoint_WhenMetricsExist_ShouldReturnFilteredSnapshotMetrics()
        {
            // The test writes through the snapshot writer so the query path proves metrics are persisted snapshot-owned outputs.
            MetricRecord metric = CreateMetric("snapshot://metrics", "metric://snapshot://metrics/SnapshotNodeCount/Snapshot", "SnapshotNodeCount", 2);
            MetricRecord otherMetric = CreateMetric("snapshot://metrics", "metric://snapshot://metrics/OtherMetric/Snapshot", "OtherMetric", 5);
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateMetricSnapshot("snapshot://metrics", [metric, otherMetric]), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/snapshot-metrics?snapshotStableKey=snapshot%3A%2F%2Fmetrics&metricKind=SnapshotNodeCount&scopeKind=Snapshot&take=5");

            using (body)
            {
                Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("snapshot://metrics", item.GetProperty("snapshotStableKey").GetString());
                Assert.Equal("metric://snapshot://metrics/SnapshotNodeCount/Snapshot", item.GetProperty("stableKey").GetString());
                Assert.Equal("SnapshotNodeCount", item.GetProperty("metricKind").GetString());
                Assert.Equal("Snapshot", item.GetProperty("scopeKind").GetString());
                Assert.Equal(2, item.GetProperty("numericValue").GetDecimal());
                Assert.Equal("nodes", item.GetProperty("unit").GetString());
                Assert.Equal("sha256:metric-SnapshotNodeCount", item.GetProperty("fingerprint").GetString());
            }
        }

        /// <summary>
        /// Confirms hotlist filtering, paging, deterministic ordering, finding detail, and history endpoints return controlled finding DTOs.
        /// </summary>
        /// <returns>A task that completes after HTTP responses are asserted.</returns>
        [Fact]
        public async Task FindingEndpoints_WhenFindingsExist_ShouldReturnHotlistDetailAndHistory()
        {
            // Fixture data uses two findings in one snapshot so category, severity, affected-node, and paging behavior can be verified together.
            RuleCatalogEntry rule = CreateRule("ARCHON-HOTLIST", "1.0.0", RuleCategory.Lifecycle, FindingSeverity.High, enabled: true, builtIn: true, ownerScope: "Archon");
            FindingRecord finding = CreateFinding("snapshot://one", "finding://one", "history://one", rule.RuleCode, rule.Version, FindingSeverity.High, FindingStatus.Open, "project://Customer.Api", "evidence://one");
            FindingRecord otherFinding = CreateFinding("snapshot://one", "finding://two", "history://two", rule.RuleCode, rule.Version, FindingSeverity.Low, FindingStatus.Acknowledged, "project://Customer.Worker", "evidence://two");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                await services.GetRequiredService<IRuleCatalogStore>().UpsertRulesAsync([rule], CancellationToken.None);
                await services.GetRequiredService<IFindingStore>().UpsertFindingsAsync([finding, otherFinding], CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument hotlistBody = await GetJsonAsync(client, "/hotlist?snapshotStableKey=snapshot%3A%2F%2Fone&severity=High&affectedNodeStableKey=project%3A%2F%2FCustomer.Api&take=1");
            JsonDocument detailBody = await GetJsonAsync(client, "/findings/detail?snapshotStableKey=snapshot%3A%2F%2Fone&findingStableKey=finding%3A%2F%2Fone");
            JsonDocument historyBody = await GetJsonAsync(client, "/finding-history?historyKey=history%3A%2F%2Fone");

            using (hotlistBody)
            using (detailBody)
            using (historyBody)
            {
                Assert.Equal(1, hotlistBody.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement hotlistItem = Assert.Single(hotlistBody.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("finding://one", hotlistItem.GetProperty("stableKey").GetString());
                Assert.Equal("High", hotlistItem.GetProperty("severity").GetString());
                Assert.Equal("project://Customer.Api", Assert.Single(hotlistItem.GetProperty("affectedNodes").EnumerateArray()).GetProperty("stableKey").GetString());
                Assert.Equal("evidence://one", Assert.Single(hotlistItem.GetProperty("evidenceReferences").EnumerateArray()).GetProperty("stableKey").GetString());
                Assert.Equal("finding://one", detailBody.RootElement.GetProperty("item").GetProperty("stableKey").GetString());
                Assert.False(detailBody.RootElement.GetProperty("metadata").ToString().Contains("password", StringComparison.OrdinalIgnoreCase));
                Assert.Equal("history://one", historyBody.RootElement.GetProperty("historyKey").GetString());
                Assert.Equal("snapshot://one", historyBody.RootElement.GetProperty("firstSeenSnapshotStableKey").GetString());
                Assert.Single(historyBody.RootElement.GetProperty("records").EnumerateArray());
            }
        }

        /// <summary>
        /// Confirms suppression endpoint validates required fields and applies valid suppression overlays without deleting findings.
        /// </summary>
        /// <returns>A task that completes after suppression responses and updated finding detail are asserted.</returns>
        [Fact]
        public async Task SuppressionEndpoint_WhenRequestIsValidOrInvalid_ShouldReturnExpectedResponses()
        {
            // Suppression is tested through HTTP so validation problem shaping and persistence update behavior are both covered.
            RuleCatalogEntry rule = CreateRule("ARCHON-SUPPRESS", "1.0.0", RuleCategory.SecuritySensitive, FindingSeverity.Critical, enabled: true, builtIn: true, ownerScope: "Archon");
            FindingRecord finding = CreateFinding("snapshot://suppress", "finding://suppress", "history://suppress", rule.RuleCode, rule.Version, FindingSeverity.Critical, FindingStatus.Open, "project://Secure.Api", "evidence://secret");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                await services.GetRequiredService<IRuleCatalogStore>().UpsertRulesAsync([rule], CancellationToken.None);
                await services.GetRequiredService<IFindingStore>().UpsertFindingsAsync([finding], CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage invalidResponse = await client.PostAsJsonAsync("/findings/suppressions", new SuppressFindingApiRequest(null, rule.RuleCode, rule.Version, finding.PrimaryNodeStableKey!.Value.Value, " ", " ", null));
            HttpResponseMessage validResponse = await client.PostAsJsonAsync("/findings/suppressions", new SuppressFindingApiRequest(finding.HistoryKey, rule.RuleCode, rule.Version, finding.PrimaryNodeStableKey!.Value.Value, "Accepted for migration window.", "architect@example.invalid", new Dictionary<string, JsonElement>()));
            JsonDocument detailBody = await GetJsonAsync(client, "/findings/detail?snapshotStableKey=snapshot%3A%2F%2Fsuppress&findingStableKey=finding%3A%2F%2Fsuppress");

            using (detailBody)
            {
                Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
                Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
                Assert.Equal("Suppressed", detailBody.RootElement.GetProperty("item").GetProperty("status").GetString());
                Assert.Equal("Accepted for migration window.", detailBody.RootElement.GetProperty("suppressionReason").GetString());
                Assert.Equal("architect@example.invalid", detailBody.RootElement.GetProperty("suppressedBy").GetString());
            }
        }

        /// <summary>
        /// Confirms missing rule and finding identities return not found instead of leaking exceptions.
        /// </summary>
        /// <returns>A task that completes after not-found responses are asserted.</returns>
        [Fact]
        public async Task QueryEndpoints_WhenRecordsAreMissing_ShouldReturnNotFound()
        {
            // Missing records are normal client outcomes and should not surface stack traces or infrastructure details.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage ruleResponse = await client.GetAsync("/rules/MISSING/1.0.0");
            HttpResponseMessage findingResponse = await client.GetAsync("/findings/snapshot%3A%2F%2Fmissing/finding%3A%2F%2Fmissing");
            HttpResponseMessage historyResponse = await client.GetAsync("/findings/history/history%3A%2F%2Fmissing");

            Assert.Equal(HttpStatusCode.NotFound, ruleResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, findingResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, historyResponse.StatusCode);
        }

        /// <summary>
        /// Creates and starts an in-memory query API application for endpoint tests.
        /// </summary>
        /// <param name="seedAsync">The asynchronous callback that seeds application stores before the test sends requests.</param>
        /// <returns>A started test application.</returns>
        private static async Task<WebApplication> CreateApplicationAsync(Func<IServiceProvider, Task> seedAsync)
        {
            // TestServer hosts the real minimal endpoints without binding sockets or starting the Aspire AppHost.
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddArchonQueryApi();
            WebApplication app = builder.Build();
            app.MapArchonQueryApi();
            await seedAsync(app.Services).ConfigureAwait(false);
            await app.StartAsync().ConfigureAwait(false);
            return app;
        }

        /// <summary>
        /// Sends a GET request and parses a successful JSON response.
        /// </summary>
        /// <param name="client">The test HTTP client.</param>
        /// <param name="requestUri">The request URI to send.</param>
        /// <returns>The parsed JSON response document.</returns>
        private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string requestUri)
        {
            // Centralizing response assertion keeps individual tests focused on endpoint-specific JSON fields.
            HttpResponseMessage response = await client.GetAsync(requestUri).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }

        /// <summary>
        /// Asserts common WP013 machine-readable item fields that API and MCP consumers rely on.
        /// </summary>
        /// <param name="item">The JSON item to inspect.</param>
        /// <param name="stableKeyPrefix">The stable-key scheme expected for the item type.</param>
        /// <param name="expectEvidenceArray">Indicates whether the item should expose an evidenceStableKeys array.</param>
        /// <param name="requireSafeMetadataValue">Indicates whether the test fixture expects a safe metadata value to survive sanitation.</param>
        private static void AssertCommonItemFields(JsonElement item, string stableKeyPrefix, bool expectEvidenceArray, bool requireSafeMetadataValue)
        {
            // This helper intentionally checks conventions rather than endpoint-specific business values.
            Assert.Equal("snapshot://wp013-consistency", item.GetProperty("snapshotStableKey").GetString());
            Assert.StartsWith(stableKeyPrefix, item.GetProperty("stableKey").GetString(), StringComparison.Ordinal);
            Assert.True(item.TryGetProperty("confidence", out JsonElement confidence), "A WP013 item should expose confidence.");
            Assert.InRange(confidence.GetDecimal(), 0m, 1m);
            Assert.True(item.TryGetProperty("hasUnknownData", out JsonElement hasUnknownData), "A WP013 item should expose hasUnknownData.");
            Assert.Equal(JsonValueKind.False, hasUnknownData.ValueKind);
            Assert.True(item.TryGetProperty("unknownReason", out JsonElement unknownReason), "A WP013 item should expose unknownReason even when it is null.");
            Assert.Equal(JsonValueKind.Null, unknownReason.ValueKind);
            Assert.StartsWith("sha256:", item.GetProperty("fingerprint").GetString(), StringComparison.Ordinal);

            if (expectEvidenceArray)
            {
                Assert.True(item.TryGetProperty("evidenceStableKeys", out JsonElement evidenceStableKeys), "Evidence-bearing WP013 items should expose evidenceStableKeys.");
                Assert.Equal(JsonValueKind.Array, evidenceStableKeys.ValueKind);
            }

            string metadataJson = item.GetProperty("metadata").ToString();
            if (requireSafeMetadataValue)
            {
                Assert.Contains("safe", metadataJson, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("secret", metadataJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", metadataJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", metadataJson, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a deterministic rule catalog fixture for query endpoint tests.
        /// </summary>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="version">The exact rule version.</param>
        /// <param name="category">The rule category.</param>
        /// <param name="severity">The default finding severity.</param>
        /// <param name="enabled">Indicates whether the rule is enabled.</param>
        /// <param name="builtIn">Indicates whether the rule is built in.</param>
        /// <param name="ownerScope">The optional owner scope.</param>
        /// <returns>A validated rule catalog entry fixture.</returns>
        private static RuleCatalogEntry CreateRule(string ruleCode, string version, RuleCategory category, FindingSeverity severity, bool enabled, bool builtIn, string? ownerScope)
        {
            // Rule fixtures use a valid detection group even though endpoint tests read catalog data rather than evaluating rules.
            return new RuleCatalogEntry(
                ruleCode,
                "Modernization rule " + ruleCode,
                category,
                severity,
                RuleFindingStatus.Legacy,
                enabled,
                version,
                "Flags a modernization concern.",
                "{\"ruleCode\":\"" + ruleCode + "\"}",
                ["https://example.invalid/rules/" + ruleCode],
                builtIn,
                ownerScope,
                ["Migration impact."],
                ["Project evidence."],
                ["Plan remediation."],
                ["wp012", ruleCode.ToLowerInvariant()],
                GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["ruleFamily"] = "apiQueryTests"
                }),
                new RuleDetectionGroup([NodeKind.Project], RuleDetectionMatch.MatchAll, [], []),
                "rules/" + ruleCode + ".json");
        }

        /// <summary>
        /// Creates a deterministic finding fixture for query endpoint tests.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key.</param>
        /// <param name="stableKey">The finding stable key.</param>
        /// <param name="historyKey">The finding history key.</param>
        /// <param name="ruleCode">The rule code that classified the finding.</param>
        /// <param name="ruleVersion">The rule version that classified the finding.</param>
        /// <param name="severity">The finding severity.</param>
        /// <param name="status">The finding status.</param>
        /// <param name="nodeStableKey">The primary affected node stable key.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key.</param>
        /// <returns>A validated finding record fixture.</returns>
        private static FindingRecord CreateFinding(string snapshotStableKey, string stableKey, string historyKey, string ruleCode, string ruleVersion, FindingSeverity severity, FindingStatus status, string nodeStableKey, string evidenceStableKey)
        {
            // Metadata includes a secret-like field to prove public detail responses redact metadata names that could reveal sensitive values.
            return new FindingRecord(
                new StableKey(snapshotStableKey),
                new StableKey(stableKey),
                ruleCode,
                ruleVersion,
                severity,
                status,
                "Modernization finding",
                "A modernization concern was found.",
                KnowledgeKind.Inference,
                new Confidence(0.85m),
                UnknownState.Known,
                new StableKey(nodeStableKey),
                new StableKey(evidenceStableKey),
                new StableKey(snapshotStableKey),
                new StableKey(snapshotStableKey),
                null,
                null,
                [new StableKey(nodeStableKey)],
                [new StableKey(evidenceStableKey)],
                historyKey,
                GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["projectStableKey"] = nodeStableKey,
                    ["passwordHint"] = "ShouldNotAppear"
                }),
                new Fingerprint("sha256:" + Math.Abs(StringComparer.Ordinal.GetHashCode(stableKey)).ToString("x", System.Globalization.CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Creates a deterministic extracted snapshot containing metric records for endpoint tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot to create.</param>
        /// <param name="metrics">The metrics owned by the snapshot.</param>
        /// <returns>An extracted architecture snapshot containing the supplied metrics.</returns>
        private static Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot CreateMetricSnapshot(string snapshotStableKey, IReadOnlyList<MetricRecord> metrics)
        {
            // The query endpoint only needs metrics and a snapshot header, so other graph sections remain empty for focused testing.
            StableKey repositoryStableKey = new("repository://metrics");
            SnapshotHeader header = new(
                new StableKey(snapshotStableKey),
                repositoryStableKey,
                "main",
                "abcdef",
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 20, 8, 1, 0, TimeSpan.Zero),
                "wp013-api-tests",
                "Completed",
                warnings: [],
                errors: [],
                GraphMetadata.Empty);
            RepositoryModel repository = new(repositoryStableKey, "Metrics", "D:/Repositories/Metrics", null, "main", GraphMetadata.Empty);
            return new Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot(header, [repository], [], [], [], [], [], [], metrics, [], [], []);
        }

        /// <summary>
        /// Creates a deterministic extracted snapshot containing dependency graph facts for cycle endpoint tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot to create.</param>
        /// <param name="nodeStableKeys">The project node stable keys participating in the graph.</param>
        /// <param name="edges">The dependency edges participating in the graph.</param>
        /// <returns>An extracted architecture snapshot containing the supplied cycle graph facts.</returns>
        private static Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot CreateCycleSnapshot(StableKey snapshotStableKey, IReadOnlyList<StableKey> nodeStableKeys, IReadOnlyList<ArchitectureEdge> edges)
        {
            // Cycle query tests require persisted nodes and edges so the application cycle service exercises the same graph shape as extraction.
            StableKey repositoryStableKey = new("repository://cycle-api");
            SnapshotHeader header = new(
                snapshotStableKey,
                repositoryStableKey,
                "main",
                "abcdef",
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 20, 8, 1, 0, TimeSpan.Zero),
                "wp013-cycle-api-tests",
                "Completed",
                warnings: [],
                errors: [],
                GraphMetadata.Empty);
            RepositoryModel repository = new(repositoryStableKey, "CycleApi", "D:/Repositories/CycleApi", null, "main", GraphMetadata.Empty);
            ArchitectureNode[] nodes = nodeStableKeys
                .Select(stableKey => CreateProjectNode(snapshotStableKey, stableKey))
                .ToArray();
            return new Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot(header, [repository], [], nodes, edges, [], [], [], [], [], [], []);
        }

        /// <summary>
        /// Creates a deterministic extracted snapshot containing graph facts for architecture-rule endpoint tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot to create.</param>
        /// <param name="nodes">The architecture nodes available to rule evaluation.</param>
        /// <param name="edges">The dependency edges available to rule evaluation.</param>
        /// <param name="metrics">The metric records available to metric-dependent checks.</param>
        /// <param name="findings">The finding records available to contribution projection.</param>
        /// <returns>An extracted architecture snapshot containing architecture-rule inputs.</returns>
        private static Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot CreateArchitectureRuleSnapshot(StableKey snapshotStableKey, IReadOnlyList<ArchitectureNode> nodes, IReadOnlyList<ArchitectureEdge> edges, IReadOnlyList<MetricRecord> metrics, IReadOnlyList<FindingRecord> findings)
        {
            // Architecture-rule query tests use the same in-memory snapshot writer pattern as metrics, cycles, and hotspots.
            StableKey repositoryStableKey = new("repository://architecture-rule-api");
            SnapshotHeader header = new(
                snapshotStableKey,
                repositoryStableKey,
                "main",
                "abcdef",
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 20, 8, 1, 0, TimeSpan.Zero),
                "wp013-architecture-rule-api-tests",
                "Completed",
                warnings: [],
                errors: [],
                GraphMetadata.Empty);
            RepositoryModel repository = new(repositoryStableKey, "ArchitectureRuleApi", "D:/Repositories/ArchitectureRuleApi", null, "main", GraphMetadata.Empty);
            return new Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot(header, [repository], [], nodes, edges, [], [], findings, metrics, [], [], []);
        }

        /// <summary>
        /// Creates a deterministic extracted snapshot containing graph facts for diff endpoint tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot to create.</param>
        /// <param name="repositoryStableKey">The repository stable key used for compatibility validation.</param>
        /// <param name="nodes">The architecture nodes available to diff comparison.</param>
        /// <param name="edges">The architecture edges available to diff comparison.</param>
        /// <param name="findings">The findings available to diff comparison.</param>
        /// <param name="metrics">The metrics available to diff comparison.</param>
        /// <returns>An extracted architecture snapshot containing diff inputs.</returns>
        private static Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot CreateDiffSnapshot(StableKey snapshotStableKey, StableKey repositoryStableKey, IReadOnlyList<ArchitectureNode> nodes, IReadOnlyList<ArchitectureEdge> edges, IReadOnlyList<FindingRecord> findings, IReadOnlyList<MetricRecord> metrics)
        {
            // Diff endpoint tests compare complete persisted snapshots without needing source extraction or Neo4j.
            SnapshotHeader header = new(
                snapshotStableKey,
                repositoryStableKey,
                "main",
                "abcdef",
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 20, 8, 1, 0, TimeSpan.Zero),
                "wp013-diff-api-tests",
                "Completed",
                warnings: [],
                errors: [],
                GraphMetadata.Empty);
            RepositoryModel repository = new(repositoryStableKey, "SnapshotDiffApi", "D:/Repositories/SnapshotDiffApi", null, "main", GraphMetadata.Empty);
            return new Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot(header, [repository], [], nodes, edges, [], [], findings, metrics, [], [], []);
        }

        /// <summary>
        /// Creates a deterministic project node for cycle endpoint fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the node.</param>
        /// <param name="nodeStableKey">The stable key that identifies the project node.</param>
        /// <returns>An architecture node suitable for cycle endpoint fixtures.</returns>
        private static ArchitectureNode CreateProjectNode(StableKey snapshotStableKey, StableKey nodeStableKey)
        {
            // The display name is derived from the stable key so tests remain concise while preserving valid node fields.
            string displayName = nodeStableKey.Value[(nodeStableKey.Value.LastIndexOf('/') + 1)..];
            string layer = displayName.Contains("Domain", StringComparison.OrdinalIgnoreCase)
                ? "Domain"
                : displayName.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase)
                    ? "Infrastructure"
                    : displayName.Contains("Web", StringComparison.OrdinalIgnoreCase)
                        ? "Web"
                        : "Project";
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["architecture.layer"] = layer
            });
            return CreateProjectNode(snapshotStableKey, nodeStableKey, displayName, metadata);
        }

        /// <summary>
        /// Creates a deterministic project node with explicit metadata for endpoint fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the node.</param>
        /// <param name="nodeStableKey">The stable key that identifies the project node.</param>
        /// <param name="displayName">The display name to expose for the node.</param>
        /// <param name="metadata">The deterministic node metadata.</param>
        /// <returns>An architecture node suitable for endpoint fixtures.</returns>
        private static ArchitectureNode CreateProjectNode(StableKey snapshotStableKey, StableKey nodeStableKey, string displayName, GraphMetadata metadata)
        {
            // Most endpoint fixtures should derive fingerprints from the same generator as production extraction output.
            return CreateProjectNode(snapshotStableKey, nodeStableKey, displayName, metadata, FingerprintGenerator.ForNode(NodeKind.Project, displayName, displayName, displayName.ToLowerInvariant(), KnowledgeKind.Fact, metadata).Value);
        }

        /// <summary>
        /// Creates a deterministic project node with an explicit fingerprint for diff endpoint fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the node.</param>
        /// <param name="nodeStableKey">The stable key that identifies the project node.</param>
        /// <param name="displayName">The display name to expose for the node.</param>
        /// <param name="metadata">The deterministic node metadata.</param>
        /// <param name="fingerprint">The explicit diff fingerprint.</param>
        /// <returns>An architecture node suitable for endpoint fixtures.</returns>
        private static ArchitectureNode CreateProjectNode(StableKey snapshotStableKey, StableKey nodeStableKey, string displayName, GraphMetadata metadata, string fingerprint)
        {
            // Explicit metadata lets architecture-rule endpoint tests model layers and runtime flags without creating source projects.
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
                metadata,
                new Fingerprint(fingerprint));
        }

        /// <summary>
        /// Creates a deterministic dependency edge for cycle endpoint fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the edge.</param>
        /// <param name="edgeStableKey">The stable key that identifies the edge.</param>
        /// <param name="edgeKind">The dependency edge kind.</param>
        /// <param name="sourceNodeStableKey">The source node stable key.</param>
        /// <param name="targetNodeStableKey">The target node stable key.</param>
        /// <param name="evidenceStableKey">The optional evidence stable key explaining the edge.</param>
        /// <returns>An architecture edge suitable for cycle endpoint fixtures.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, string edgeStableKey, EdgeKind edgeKind, StableKey sourceNodeStableKey, StableKey targetNodeStableKey, string? evidenceStableKey)
        {
            // The edge fixture mirrors extraction output closely enough for controlled query behavior without requiring source parsing.
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

        /// <summary>
        /// Creates a deterministic metric record for endpoint tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the metric.</param>
        /// <param name="stableKey">The stable key that identifies the metric.</param>
        /// <param name="metricKind">The metric kind.</param>
        /// <param name="numericValue">The numeric metric value.</param>
        /// <returns>A metric record suitable for in-memory endpoint tests.</returns>
        private static MetricRecord CreateMetric(string snapshotStableKey, string stableKey, string metricKind, decimal numericValue, MetricScopeKind? scopeKind = null, StableKey? nodeStableKey = null, string? unit = "nodes")
        {
            // Endpoint fixtures can represent both snapshot and project metrics so filtering can be verified through HTTP.
            return new MetricRecord(
                new StableKey(snapshotStableKey),
                new StableKey(stableKey),
                metricKind,
                scopeKind ?? MetricScopeKind.Snapshot,
                nodeStableKey,
                edgeStableKey: null,
                primaryEvidenceStableKey: null,
                "Snapshot node count",
                numericValue,
                textValue: null,
                unit,
                Confidence.Certain,
                UnknownState.Known,
                GraphMetadata.Empty,
                new Fingerprint("sha256:metric-" + metricKind));
        }

        /// <summary>
        /// Creates a deterministic metric record with both safe and secret-like metadata for endpoint safety tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the metric.</param>
        /// <param name="stableKey">The stable key that identifies the metric.</param>
        /// <param name="metricKind">The metric kind.</param>
        /// <param name="numericValue">The numeric metric value.</param>
        /// <param name="scopeKind">The metric scope kind.</param>
        /// <param name="nodeStableKey">The target node stable key.</param>
        /// <param name="unit">The metric unit.</param>
        /// <returns>A metric record suitable for metadata sanitation endpoint tests.</returns>
        private static MetricRecord CreateMetricWithMetadata(string snapshotStableKey, string stableKey, string metricKind, decimal numericValue, MetricScopeKind scopeKind, StableKey nodeStableKey, string unit)
        {
            // Secret-like metadata should be removed before public JSON responses are serialized.
            return new MetricRecord(
                new StableKey(snapshotStableKey),
                new StableKey(stableKey),
                metricKind,
                scopeKind,
                nodeStableKey,
                edgeStableKey: null,
                primaryEvidenceStableKey: null,
                "Graph fan in",
                numericValue,
                textValue: null,
                unit,
                Confidence.Certain,
                UnknownState.Known,
                GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["safeContext"] = "visible",
                    ["secretToken"] = "hidden"
                }),
                new Fingerprint("sha256:metric-" + metricKind));
        }
    }
}
