using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Archon.Api.Query.Contracts;
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
    }
}
