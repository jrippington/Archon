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
        /// Confirms dashboard summary returns the common WP014 envelope for a latest snapshot selection.
        /// </summary>
        /// <returns>A task that completes after the dashboard summary response is asserted.</returns>
        [Fact]
        public async Task DashboardSummaryEndpoint_WhenLatestSnapshotExists_ShouldReturnCommonEnvelopeWithStableSummary()
        {
            // The dashboard route proves the WP014 common envelope over a real in-memory application snapshot without exposing graph-store IDs.
            StableKey repositoryStableKey = new("repository://dashboard-api");
            StableKey solutionStableKey = new("solution://dashboard-api/main");
            StableKey olderSnapshotStableKey = new("snapshot://dashboard-api/2026-05-20T080000Z");
            StableKey latestSnapshotStableKey = new("snapshot://dashboard-api/2026-05-21T080000Z");
            StableKey apiProjectStableKey = new("project://src/Dashboard.Api/Dashboard.Api.csproj");
            StableKey workerProjectStableKey = new("project://src/Dashboard.Worker/Dashboard.Worker.csproj");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateDashboardSnapshot(olderSnapshotStableKey, repositoryStableKey, solutionStableKey, [CreateProjectNode(olderSnapshotStableKey, apiProjectStableKey, "Dashboard.Api.csproj", CreateProjectMetadata("Api"), "sha256:dashboard-api-old")], [], []), CancellationToken.None);
                await writer.WriteSnapshotAsync(CreateDashboardSnapshot(latestSnapshotStableKey, repositoryStableKey, solutionStableKey,
                    [
                        CreateProjectNode(latestSnapshotStableKey, apiProjectStableKey, "Dashboard.Api.csproj", CreateProjectMetadata("Api"), "sha256:dashboard-api-new"),
                        CreateProjectNode(latestSnapshotStableKey, workerProjectStableKey, "Dashboard.Worker.csproj", CreateProjectMetadata("Worker"), "sha256:dashboard-worker-new"),
                        CreateEndpointNode(latestSnapshotStableKey, new StableKey("endpoint://dashboard-api/weather"), apiProjectStableKey)
                    ],
                    [CreateMetric(latestSnapshotStableKey.Value, "metric://dashboard-api/fan-in", "GraphFanIn", 9, MetricScopeKind.Node, apiProjectStableKey, "edges")],
                    [CreateFinding(latestSnapshotStableKey.Value, "finding://dashboard-api/hotlist", "history://dashboard-api/hotlist", "ARCHON-DASH", "1.0.0", FindingSeverity.High, FindingStatus.Open, apiProjectStableKey.Value, "evidence://dashboard-api/hotlist")]), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/dashboard-summary?repositoryStableKey=repository%3A%2F%2Fdashboard-api&solutionStableKey=solution%3A%2F%2Fdashboard-api%2Fmain&snapshotStableKey=latest");

            using (body)
            {
                Assert.Equal("repository://dashboard-api", body.RootElement.GetProperty("scope").GetProperty("repositoryStableKey").GetString());
                Assert.Equal("solution://dashboard-api/main", body.RootElement.GetProperty("scope").GetProperty("solutionStableKey").GetString());
                Assert.Equal(latestSnapshotStableKey.Value, body.RootElement.GetProperty("snapshot").GetProperty("snapshotStableKey").GetString());
                Assert.True(body.RootElement.GetProperty("snapshot").GetProperty("resolvedAsLatest").GetBoolean());
                Assert.False(body.RootElement.GetProperty("page").GetProperty("isPaged").GetBoolean());
                JsonElement data = body.RootElement.GetProperty("data");
                JsonElement counts = data.GetProperty("counts");
                Assert.Equal(2, counts.GetProperty("projectCount").GetInt32());
                Assert.Equal(2, counts.GetProperty("cSharpProjectCount").GetInt32());
                Assert.Equal(1, counts.GetProperty("apiCount").GetInt32());
                Assert.Equal(1, counts.GetProperty("workerCount").GetInt32());
                Assert.Equal(1, counts.GetProperty("endpointCount").GetInt32());
                Assert.Equal(1, counts.GetProperty("hotlistFindingCount").GetInt32());
                JsonElement firstHotspot = data.GetProperty("topHotspots").EnumerateArray().First();
                Assert.Equal(apiProjectStableKey.Value, firstHotspot.GetProperty("targetStableKey").GetString());
                Assert.StartsWith("hotspot://", firstHotspot.GetProperty("stableKey").GetString(), StringComparison.Ordinal);
                Assert.Contains(data.GetProperty("latestChanges").EnumerateArray(), change => change.GetProperty("domain").GetString() == "Nodes" && change.GetProperty("changeKind").GetString() == "Added");
                Assert.DoesNotContain("neo4j", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms dashboard summary validation rejects malformed snapshot selectors with safe problem details.
        /// </summary>
        /// <returns>A task that completes after the validation response is asserted.</returns>
        [Fact]
        public async Task DashboardSummaryEndpoint_WhenSnapshotSelectorIsInvalid_ShouldReturnValidationProblem()
        {
            // Invalid selectors fail before graph lookup so callers receive a deterministic field-like problem shape.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.GetAsync("/dashboard-summary?repositoryStableKey=repository%3A%2F%2Fdashboard-api&snapshotStableKey=not-a-snapshot");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("SnapshotSelectorInvalid", body, StringComparison.Ordinal);
            Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
        }

        /// <summary>
        /// Confirms dashboard summary validation requires an explicit repository scope.
        /// </summary>
        /// <returns>A task that completes after the missing-scope validation response is asserted.</returns>
        [Fact]
        public async Task DashboardSummaryEndpoint_WhenRepositoryScopeIsMissing_ShouldReturnValidationProblem()
        {
            // The endpoint requires repository scope so latest resolution cannot accidentally scan every persisted repository.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.GetAsync("/dashboard-summary?snapshotStableKey=latest");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("RepositoryStableKeyRequired", body, StringComparison.Ordinal);
        }

        /// <summary>
        /// Confirms dashboard summary exposes warnings and unknowns when optional summary inputs are unavailable.
        /// </summary>
        /// <returns>A task that completes after warning and unknown response sections are asserted.</returns>
        [Fact]
        public async Task DashboardSummaryEndpoint_WhenOptionalDataIsMissing_ShouldReturnWarningsAndUnknowns()
        {
            // A valid snapshot without metrics, findings, or a previous snapshot should report partial summary data explicitly.
            StableKey repositoryStableKey = new("repository://dashboard-missing-optional");
            StableKey snapshotStableKey = new("snapshot://dashboard-missing-optional/current");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateDashboardSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey: null, [CreateProjectNode(snapshotStableKey, new StableKey("project://src/Missing.Optional/Missing.Optional.csproj"))], [], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/dashboard-summary?repositoryStableKey=repository%3A%2F%2Fdashboard-missing-optional&snapshotStableKey=snapshot%3A%2F%2Fdashboard-missing-optional%2Fcurrent");

            using (body)
            {
                JsonElement warnings = body.RootElement.GetProperty("warnings");
                JsonElement unknowns = body.RootElement.GetProperty("unknowns");
                Assert.True(warnings.GetArrayLength() >= 2);
                Assert.Contains(unknowns.EnumerateArray(), unknown => unknown.GetProperty("field").GetString() == "topHotspots");
                Assert.Contains(unknowns.EnumerateArray(), unknown => unknown.GetProperty("field").GetString() == "latestChanges");
                Assert.Empty(body.RootElement.GetProperty("data").GetProperty("topHotspots").EnumerateArray());
            }
        }

        /// <summary>
        /// Confirms missing dashboard summary scopes use the safe public error shape rather than implementation details.
        /// </summary>
        /// <returns>A task that completes after the safe validation response is asserted.</returns>
        [Fact]
        public async Task DashboardSummaryEndpoint_WhenRepositoryIsUnknown_ShouldReturnSafeErrorShape()
        {
            // Unknown repositories are client-correctable selector problems and should not reveal persistence adapter details.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.GetAsync("/dashboard-summary?repositoryStableKey=repository%3A%2F%2Funknown&snapshotStableKey=latest");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("RepositoryNotFound", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Neo4j", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms the project catalogue endpoint returns stable project rows with scope, snapshot, paging, aggregate counts, risk, and evidence metadata.
        /// </summary>
        /// <returns>A task that completes after the project catalogue response is asserted.</returns>
        [Fact]
        public async Task ProjectCatalogueEndpoint_WhenProjectsExist_ShouldReturnFilteredSortedPagedEnvelope()
        {
            // The catalogue route exercises the WP014 project query slice over in-memory snapshot facts without exposing persistence-local graph IDs.
            StableKey repositoryStableKey = new("repository://project-catalogue-api");
            StableKey solutionStableKey = new("solution://project-catalogue-api/main");
            StableKey snapshotStableKey = new("snapshot://project-catalogue-api/current");
            StableKey apiProjectStableKey = new("project://src/Catalogue.Api/Catalogue.Api.csproj");
            StableKey workerProjectStableKey = new("project://src/Catalogue.Worker/Catalogue.Worker.csproj");
            StableKey packageStableKey = new("package://Newtonsoft.Json");
            StableKey endpointStableKey = new("endpoint://catalogue-api/weather");
            StableKey evidenceStableKey = new("evidence://catalogue-api/project");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode apiProject = CreateProjectNode(snapshotStableKey, apiProjectStableKey, "Catalogue.Api.csproj", CreateDetailedProjectMetadata("Api", "Api", "net10.0", true, "src/Catalogue.Api/Catalogue.Api.csproj"), "sha256:catalogue-api");
                ArchitectureNode workerProject = CreateProjectNode(snapshotStableKey, workerProjectStableKey, "Catalogue.Worker.csproj", CreateDetailedProjectMetadata("Worker", "Worker", "net10.0", true, "src/Catalogue.Worker/Catalogue.Worker.csproj"), "sha256:catalogue-worker");
                ArchitectureNode packageNode = CreatePackageNode(snapshotStableKey, packageStableKey, "Newtonsoft.Json");
                ArchitectureNode endpointNode = CreateEndpointNode(snapshotStableKey, endpointStableKey, apiProjectStableKey);
                ArchitectureNode dbContextNode = CreateOwnedNode(snapshotStableKey, new StableKey("dbcontext://catalogue-api/app"), NodeKind.DbContext, "AppDbContext", apiProjectStableKey);
                ArchitectureEdge dependencyEdge = CreateEdge(snapshotStableKey, "edge://catalogue-api/references-worker", EdgeKind.References, apiProjectStableKey, workerProjectStableKey, evidenceStableKey.Value);
                ArchitectureEdge packageEdge = CreateEdge(snapshotStableKey, "edge://catalogue-api/package", EdgeKind.UsesPackage, apiProjectStableKey, packageStableKey, null);
                FindingRecord finding = CreateFinding(snapshotStableKey.Value, "finding://catalogue-api/risk", "history://catalogue-api/risk", "ARCHON-PROJECT", "1.0.0", FindingSeverity.High, FindingStatus.Open, apiProjectStableKey.Value, evidenceStableKey.Value);
                EvidenceRecord evidence = CreateEvidence(snapshotStableKey, evidenceStableKey, "src/Catalogue.Api/Catalogue.Api.csproj");
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey, [apiProject, workerProject, packageNode, endpointNode, dbContextNode], [dependencyEdge, packageEdge], [evidence], [finding]), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/projects?repositoryStableKey=repository%3A%2F%2Fproject-catalogue-api&solutionStableKey=solution%3A%2F%2Fproject-catalogue-api%2Fmain&snapshotStableKey=latest&search=Catalogue&language=C%23&projectType=Api&targetFramework=net10.0&hasDataAccess=true&hasRisk=true&sort=hotlistCount&descending=true&skip=0&take=1");

            using (body)
            {
                Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
                Assert.Equal(0, body.RootElement.GetProperty("skip").GetInt32());
                Assert.Equal(1, body.RootElement.GetProperty("take").GetInt32());
                Assert.Equal(repositoryStableKey.Value, body.RootElement.GetProperty("scope").GetProperty("repositoryStableKey").GetString());
                Assert.Equal(solutionStableKey.Value, body.RootElement.GetProperty("scope").GetProperty("solutionStableKey").GetString());
                Assert.Equal(snapshotStableKey.Value, body.RootElement.GetProperty("snapshot").GetProperty("snapshotStableKey").GetString());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(apiProjectStableKey.Value, item.GetProperty("stableKey").GetString());
                Assert.Equal("src/Catalogue.Api/Catalogue.Api.csproj", item.GetProperty("path").GetString());
                Assert.Equal("C#", item.GetProperty("language").GetString());
                Assert.Equal("Api", item.GetProperty("projectType").GetString());
                Assert.Equal("net10.0", item.GetProperty("targetFramework").GetString());
                Assert.True(item.GetProperty("isSdkStyle").GetBoolean());
                Assert.Equal(1, item.GetProperty("dependencyCount").GetInt32());
                Assert.Equal(1, item.GetProperty("packageCount").GetInt32());
                Assert.Equal(1, item.GetProperty("endpointCount").GetInt32());
                Assert.Equal(1, item.GetProperty("hotlistCount").GetInt32());
                Assert.Contains(item.GetProperty("dataAccessIndicators").EnumerateArray(), indicator => indicator.GetString() == "DbContext");
                Assert.True(item.GetProperty("riskIndicators").GetProperty("hasHotlistFindings").GetBoolean());
                Assert.Contains(evidenceStableKey.Value, item.GetProperty("evidenceStableKeys").EnumerateArray().Select(static evidence => evidence.GetString()));
                Assert.DoesNotContain("neo4j", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms project detail lookup by stable key returns full stable detail sections, unknowns, and evidence references without source snippets.
        /// </summary>
        /// <returns>A task that completes after the project detail response is asserted.</returns>
        [Fact]
        public async Task ProjectDetailEndpoint_WhenProjectStableKeyExists_ShouldReturnDetailEnvelope()
        {
            // Stable-key lookup proves consumers can follow catalogue links to a bounded project detail response.
            StableKey repositoryStableKey = new("repository://project-detail-api");
            StableKey snapshotStableKey = new("snapshot://project-detail-api/current");
            StableKey projectStableKey = new("project://src/Detail.Api/Detail.Api.csproj");
            StableKey dependencyStableKey = new("project://src/Detail.Domain/Detail.Domain.csproj");
            StableKey evidenceStableKey = new("evidence://detail-api/project");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode project = CreateProjectNode(snapshotStableKey, projectStableKey, "Detail.Api.csproj", CreateDetailedProjectMetadata("Api", "Api", "net10.0", true, "src/Detail.Api/Detail.Api.csproj"), "sha256:detail-api");
                ArchitectureNode dependency = CreateProjectNode(snapshotStableKey, dependencyStableKey, "Detail.Domain.csproj", CreateDetailedProjectMetadata("Domain", "Library", "net10.0", true, "src/Detail.Domain/Detail.Domain.csproj"), "sha256:detail-domain");
                ArchitectureNode endpoint = CreateEndpointNode(snapshotStableKey, new StableKey("endpoint://detail-api/weather"), projectStableKey);
                ArchitectureNode config = CreateOwnedNode(snapshotStableKey, new StableKey("config://detail-api/ConnectionStrings.Main"), NodeKind.ConfigurationKey, "ConnectionStrings:Main", projectStableKey);
                ArchitectureEdge reference = CreateEdge(snapshotStableKey, "edge://detail-api/references-domain", EdgeKind.References, projectStableKey, dependencyStableKey, evidenceStableKey.Value);
                FindingRecord finding = CreateFinding(snapshotStableKey.Value, "finding://detail-api/risk", "history://detail-api/risk", "ARCHON-PROJECT", "1.0.0", FindingSeverity.Medium, FindingStatus.Open, projectStableKey.Value, evidenceStableKey.Value);
                EvidenceRecord evidence = CreateEvidence(snapshotStableKey, evidenceStableKey, "src/Detail.Api/Detail.Api.csproj");
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey: null, [project, dependency, endpoint, config], [reference], [evidence], [finding]), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/projects/detail?repositoryStableKey=repository%3A%2F%2Fproject-detail-api&snapshotStableKey=snapshot%3A%2F%2Fproject-detail-api%2Fcurrent&projectStableKey=project%3A%2F%2Fsrc%2FDetail.Api%2FDetail.Api.csproj");

            using (body)
            {
                JsonElement data = body.RootElement.GetProperty("data");
                Assert.Equal(projectStableKey.Value, data.GetProperty("summary").GetProperty("stableKey").GetString());
                Assert.Contains(data.GetProperty("responsibilities").EnumerateArray(), responsibility => responsibility.GetProperty("name").GetString() == "Api");
                JsonElement evidence = Assert.Single(data.GetProperty("evidence").EnumerateArray());
                Assert.Equal(evidenceStableKey.Value, evidence.GetProperty("stableKey").GetString());
                Assert.Equal("ProjectFile", evidence.GetProperty("evidenceKind").GetString());
                Assert.False(evidence.TryGetProperty("snippetPreview", out _));
                Assert.Contains(dependencyStableKey.Value, data.GetProperty("references").EnumerateArray().Select(static reference => reference.GetString()));
                Assert.Contains("GET /weather", data.GetProperty("endpoints").EnumerateArray().Select(static endpoint => endpoint.GetString()));
                Assert.Contains("ConnectionStrings:Main", data.GetProperty("configurationKeys").EnumerateArray().Select(static key => key.GetString()));
                Assert.Contains("finding://detail-api/risk", data.GetProperty("hotlistFindings").EnumerateArray().Select(static finding => finding.GetString()));
                Assert.Equal(1, data.GetProperty("scopedGraphSummary").GetProperty("outgoingDependencyCount").GetInt32());
                Assert.Contains(body.RootElement.GetProperty("unknowns").EnumerateArray(), unknown => unknown.GetProperty("field").GetString() == "metrics");
            }
        }

        /// <summary>
        /// Confirms project detail lookup by display name succeeds only when the name is unambiguous.
        /// </summary>
        /// <returns>A task that completes after the project-name detail response is asserted.</returns>
        [Fact]
        public async Task ProjectDetailEndpoint_WhenProjectNameIsUnambiguous_ShouldReturnDetail()
        {
            // Name lookup is useful for callers that only have a project display name, but the service must still resolve to exactly one project.
            StableKey repositoryStableKey = new("repository://project-name-api");
            StableKey snapshotStableKey = new("snapshot://project-name-api/current");
            StableKey projectStableKey = new("project://src/Named.Api/Named.Api.csproj");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode project = CreateProjectNode(snapshotStableKey, projectStableKey, "Named.Api.csproj", CreateDetailedProjectMetadata("Api", "Api", "net10.0", true, "src/Named.Api/Named.Api.csproj"), "sha256:named-api");
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey: null, [project], [], [], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/projects/detail?repositoryStableKey=repository%3A%2F%2Fproject-name-api&snapshotStableKey=latest&projectName=Named.Api.csproj");

            using (body)
            {
                Assert.Equal(projectStableKey.Value, body.RootElement.GetProperty("data").GetProperty("summary").GetProperty("stableKey").GetString());
                Assert.True(body.RootElement.GetProperty("snapshot").GetProperty("resolvedAsLatest").GetBoolean());
            }
        }

        /// <summary>
        /// Confirms ambiguous project-name lookup returns a conflict response with stable disambiguation options.
        /// </summary>
        /// <returns>A task that completes after the ambiguous-name conflict response is asserted.</returns>
        [Fact]
        public async Task ProjectDetailEndpoint_WhenProjectNameIsAmbiguous_ShouldReturnConflictWithOptions()
        {
            // Ambiguous name lookup must not choose arbitrarily because project names are not durable public identities.
            StableKey repositoryStableKey = new("repository://project-ambiguous-api");
            StableKey snapshotStableKey = new("snapshot://project-ambiguous-api/current");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode firstProject = CreateProjectNode(snapshotStableKey, new StableKey("project://src/One/Shared.csproj"), "Shared.csproj", CreateDetailedProjectMetadata("Library", "Library", "net10.0", true, "src/One/Shared.csproj"), "sha256:shared-one");
                ArchitectureNode secondProject = CreateProjectNode(snapshotStableKey, new StableKey("project://src/Two/Shared.csproj"), "Shared.csproj", CreateDetailedProjectMetadata("Library", "Library", "net10.0", true, "src/Two/Shared.csproj"), "sha256:shared-two");
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey: null, [firstProject, secondProject], [], [], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.GetAsync("/projects/detail?repositoryStableKey=repository%3A%2F%2Fproject-ambiguous-api&snapshotStableKey=latest&projectName=Shared.csproj");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("ProjectNameAmbiguous", body, StringComparison.Ordinal);
            Assert.Contains("project://src/One/Shared.csproj", body, StringComparison.Ordinal);
            Assert.Contains("project://src/Two/Shared.csproj", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Neo4j", body, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms project catalogue validation uses safe problem details for malformed or missing scope input.
        /// </summary>
        /// <returns>A task that completes after the validation response is asserted.</returns>
        [Fact]
        public async Task ProjectCatalogueEndpoint_WhenScopeIsInvalid_ShouldReturnValidationProblem()
        {
            // Scope validation runs before graph lookup so invalid callers receive deterministic field-like problem details.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.GetAsync("/projects?snapshotStableKey=not-a-snapshot&take=5");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("RepositoryStableKeyRequired", body, StringComparison.Ordinal);
            Assert.Contains("SnapshotSelectorInvalid", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms direct dependency and dependent endpoints return stable edges, related nodes, and evidence references.
        /// </summary>
        /// <returns>A task that completes after direct traversal responses are asserted.</returns>
        [Fact]
        public async Task GraphTraversalEndpoints_WhenDirectRelationshipsExist_ShouldReturnStableEdgesAndEvidence()
        {
            // Direct traversal exercises both outgoing dependencies and incoming dependents over the same persisted edge without exposing store-local IDs.
            StableKey repositoryStableKey = new("repository://traversal-direct-api");
            StableKey snapshotStableKey = new("snapshot://traversal-direct-api/current");
            StableKey apiProjectStableKey = new("project://src/Traversal.Api/Traversal.Api.csproj");
            StableKey domainProjectStableKey = new("project://src/Traversal.Domain/Traversal.Domain.csproj");
            StableKey evidenceStableKey = new("evidence://traversal-direct/reference");
            ArchitectureEdge reference = CreateEdge(snapshotStableKey, "edge://traversal-direct/api-domain", EdgeKind.References, apiProjectStableKey, domainProjectStableKey, evidenceStableKey.Value);
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(
                    snapshotStableKey,
                    repositoryStableKey,
                    solutionStableKey: null,
                    [CreateProjectNode(snapshotStableKey, apiProjectStableKey), CreateProjectNode(snapshotStableKey, domainProjectStableKey)],
                    [reference],
                    [CreateEvidence(snapshotStableKey, evidenceStableKey, "src/Traversal.Api/Traversal.Api.csproj")],
                    []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument dependenciesBody = await GetJsonAsync(client, "/dependencies/direct?repositoryStableKey=repository%3A%2F%2Ftraversal-direct-api&snapshotStableKey=latest&nodeStableKey=project%3A%2F%2Fsrc%2FTraversal.Api%2FTraversal.Api.csproj&take=10");
            JsonDocument dependentsBody = await GetJsonAsync(client, "/dependents/direct?repositoryStableKey=repository%3A%2F%2Ftraversal-direct-api&snapshotStableKey=latest&nodeStableKey=project%3A%2F%2Fsrc%2FTraversal.Domain%2FTraversal.Domain.csproj&take=10");

            using (dependenciesBody)
            using (dependentsBody)
            {
                JsonElement dependencyData = dependenciesBody.RootElement.GetProperty("data");
                Assert.Equal("DirectDependencies", dependencyData.GetProperty("mode").GetString());
                Assert.Equal("Outgoing", dependencyData.GetProperty("direction").GetString());
                JsonElement dependencyEdge = Assert.Single(dependencyData.GetProperty("edges").EnumerateArray());
                Assert.Equal(reference.StableKey.Value, dependencyEdge.GetProperty("stableKey").GetString());
                Assert.Equal(EdgeKind.References.Value, dependencyEdge.GetProperty("kind").GetString());
                Assert.Equal(apiProjectStableKey.Value, dependencyEdge.GetProperty("sourceNodeStableKey").GetString());
                Assert.Equal(domainProjectStableKey.Value, dependencyEdge.GetProperty("targetNodeStableKey").GetString());
                Assert.Equal(evidenceStableKey.Value, Assert.Single(dependencyEdge.GetProperty("evidenceStableKeys").EnumerateArray()).GetString());
                Assert.Contains(domainProjectStableKey.Value, dependencyData.GetProperty("nodes").EnumerateArray().Select(static node => node.GetProperty("stableKey").GetString()));
                Assert.False(dependenciesBody.RootElement.GetProperty("truncation").GetProperty("truncated").GetBoolean());
                JsonElement dependentEdge = Assert.Single(dependentsBody.RootElement.GetProperty("data").GetProperty("edges").EnumerateArray());
                Assert.Equal(reference.StableKey.Value, dependentEdge.GetProperty("stableKey").GetString());
                Assert.DoesNotContain("neo4j", dependenciesBody.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms transitive traversal enforces depth, edge-kind filters, and result-size truncation metadata.
        /// </summary>
        /// <returns>A task that completes after transitive traversal and validation responses are asserted.</returns>
        [Fact]
        public async Task GraphTraversalEndpoints_WhenTransitiveTraversalIsRequested_ShouldApplyBoundsFiltersAndTruncation()
        {
            // Transitive traversal should return only approved edge kinds and clearly report when the response is cut down by the take limit.
            StableKey repositoryStableKey = new("repository://traversal-transitive-api");
            StableKey snapshotStableKey = new("snapshot://traversal-transitive-api/current");
            StableKey rootProjectStableKey = new("project://src/Traversal.Root/Traversal.Root.csproj");
            StableKey firstProjectStableKey = new("project://src/Traversal.First/Traversal.First.csproj");
            StableKey secondProjectStableKey = new("project://src/Traversal.Second/Traversal.Second.csproj");
            StableKey packageStableKey = new("package://Traversal.Package");
            ArchitectureEdge firstReference = CreateEdge(snapshotStableKey, "edge://traversal-transitive/root-first", EdgeKind.References, rootProjectStableKey, firstProjectStableKey, "evidence://traversal-transitive/root-first");
            ArchitectureEdge secondReference = CreateEdge(snapshotStableKey, "edge://traversal-transitive/first-second", EdgeKind.References, firstProjectStableKey, secondProjectStableKey, "evidence://traversal-transitive/first-second");
            ArchitectureEdge packageUse = CreateEdge(snapshotStableKey, "edge://traversal-transitive/root-package", EdgeKind.UsesPackage, rootProjectStableKey, packageStableKey, null);
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(
                    snapshotStableKey,
                    repositoryStableKey,
                    solutionStableKey: null,
                    [CreateProjectNode(snapshotStableKey, rootProjectStableKey), CreateProjectNode(snapshotStableKey, firstProjectStableKey), CreateProjectNode(snapshotStableKey, secondProjectStableKey), CreatePackageNode(snapshotStableKey, packageStableKey, "Traversal.Package")],
                    [firstReference, secondReference, packageUse],
                    [],
                    []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument filteredBody = await GetJsonAsync(client, "/dependencies/transitive?repositoryStableKey=repository%3A%2F%2Ftraversal-transitive-api&snapshotStableKey=latest&nodeStableKey=project%3A%2F%2Fsrc%2FTraversal.Root%2FTraversal.Root.csproj&depth=2&edgeKinds=REFERENCES&take=10");
            JsonDocument truncatedBody = await GetJsonAsync(client, "/graph-neighbourhood?repositoryStableKey=repository%3A%2F%2Ftraversal-transitive-api&snapshotStableKey=latest&nodeStableKey=project%3A%2F%2Fsrc%2FTraversal.Root%2FTraversal.Root.csproj&direction=Outgoing&depth=2&take=1");
            HttpResponseMessage invalidDepthResponse = await client.GetAsync("/graph-neighbourhood?repositoryStableKey=repository%3A%2F%2Ftraversal-transitive-api&snapshotStableKey=latest&nodeStableKey=project%3A%2F%2Fsrc%2FTraversal.Root%2FTraversal.Root.csproj&depth=7");
            string invalidDepthBody = await invalidDepthResponse.Content.ReadAsStringAsync();

            using (filteredBody)
            using (truncatedBody)
            {
                JsonElement filteredData = filteredBody.RootElement.GetProperty("data");
                Assert.Equal("TransitiveDependencies", filteredData.GetProperty("mode").GetString());
                Assert.Equal(2, filteredData.GetProperty("edges").GetArrayLength());
                Assert.All(filteredData.GetProperty("edges").EnumerateArray(), edge => Assert.Equal(EdgeKind.References.Value, edge.GetProperty("kind").GetString()));
                Assert.DoesNotContain(packageUse.StableKey.Value, filteredData.GetProperty("edges").EnumerateArray().Select(static edge => edge.GetProperty("stableKey").GetString()));
                Assert.True(truncatedBody.RootElement.GetProperty("truncation").GetProperty("truncated").GetBoolean());
                Assert.Equal(1, truncatedBody.RootElement.GetProperty("truncation").GetProperty("limit").GetInt32());
                Assert.Contains(truncatedBody.RootElement.GetProperty("warnings").EnumerateArray(), warning => warning.GetProperty("code").GetString() == "TraversalTruncated");
                Assert.Equal(HttpStatusCode.BadRequest, invalidDepthResponse.StatusCode);
                Assert.Contains("DepthInvalid", invalidDepthBody, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Confirms dependency path queries return stable node and edge sequences when a bounded path exists.
        /// </summary>
        /// <returns>A task that completes after the path-found response is asserted.</returns>
        [Fact]
        public async Task DependencyPathEndpoint_WhenPathExists_ShouldReturnOrderedStableNodeAndEdgePath()
        {
            // Path search returns edge order and node order so clients can render a path without reconstructing graph adjacency themselves.
            StableKey repositoryStableKey = new("repository://dependency-path-api");
            StableKey snapshotStableKey = new("snapshot://dependency-path-api/current");
            StableKey apiProjectStableKey = new("project://src/Path.Api/Path.Api.csproj");
            StableKey servicesProjectStableKey = new("project://src/Path.Services/Path.Services.csproj");
            StableKey domainProjectStableKey = new("project://src/Path.Domain/Path.Domain.csproj");
            ArchitectureEdge apiServices = CreateEdge(snapshotStableKey, "edge://dependency-path/api-services", EdgeKind.References, apiProjectStableKey, servicesProjectStableKey, "evidence://dependency-path/api-services");
            ArchitectureEdge servicesDomain = CreateEdge(snapshotStableKey, "edge://dependency-path/services-domain", EdgeKind.References, servicesProjectStableKey, domainProjectStableKey, "evidence://dependency-path/services-domain");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(
                    snapshotStableKey,
                    repositoryStableKey,
                    solutionStableKey: null,
                    [CreateProjectNode(snapshotStableKey, apiProjectStableKey), CreateProjectNode(snapshotStableKey, servicesProjectStableKey), CreateProjectNode(snapshotStableKey, domainProjectStableKey)],
                    [apiServices, servicesDomain],
                    [],
                    []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/dependency-path?repositoryStableKey=repository%3A%2F%2Fdependency-path-api&snapshotStableKey=latest&sourceNodeStableKey=project%3A%2F%2Fsrc%2FPath.Api%2FPath.Api.csproj&targetNodeStableKey=project%3A%2F%2Fsrc%2FPath.Domain%2FPath.Domain.csproj&depth=3&edgeKinds=REFERENCES");

            using (body)
            {
                JsonElement data = body.RootElement.GetProperty("data");
                Assert.True(data.GetProperty("pathFound").GetBoolean());
                Assert.False(data.GetProperty("unavailable").GetBoolean());
                Assert.Equal(new[] { apiProjectStableKey.Value, servicesProjectStableKey.Value, domainProjectStableKey.Value }, data.GetProperty("nodes").EnumerateArray().Select(static node => node.GetProperty("stableKey").GetString()).ToArray());
                Assert.Equal(new[] { apiServices.StableKey.Value, servicesDomain.StableKey.Value }, data.GetProperty("edges").EnumerateArray().Select(static edge => edge.GetProperty("stableKey").GetString()).ToArray());
                Assert.Null(data.GetProperty("reason").GetString());
            }
        }

        /// <summary>
        /// Confirms dependency path queries distinguish no-path data from unavailable graph data.
        /// </summary>
        /// <returns>A task that completes after no-path and unavailable path responses are asserted.</returns>
        [Fact]
        public async Task DependencyPathEndpoint_WhenPathIsMissingOrUnavailable_ShouldReturnDistinctDataStates()
        {
            // A valid graph with disconnected nodes is a no-path result, while a snapshot with no edges reports unavailable path data.
            StableKey noPathRepositoryStableKey = new("repository://dependency-no-path-api");
            StableKey noPathSnapshotStableKey = new("snapshot://dependency-no-path-api/current");
            StableKey unavailableRepositoryStableKey = new("repository://dependency-unavailable-api");
            StableKey unavailableSnapshotStableKey = new("snapshot://dependency-unavailable-api/current");
            StableKey firstProjectStableKey = new("project://src/NoPath.First/NoPath.First.csproj");
            StableKey secondProjectStableKey = new("project://src/NoPath.Second/NoPath.Second.csproj");
            StableKey thirdProjectStableKey = new("project://src/NoPath.Third/NoPath.Third.csproj");
            ArchitectureEdge disconnectedEdge = CreateEdge(noPathSnapshotStableKey, "edge://dependency-no-path/first-third", EdgeKind.References, firstProjectStableKey, thirdProjectStableKey, null);
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(
                    noPathSnapshotStableKey,
                    noPathRepositoryStableKey,
                    solutionStableKey: null,
                    [CreateProjectNode(noPathSnapshotStableKey, firstProjectStableKey), CreateProjectNode(noPathSnapshotStableKey, secondProjectStableKey), CreateProjectNode(noPathSnapshotStableKey, thirdProjectStableKey)],
                    [disconnectedEdge],
                    [],
                    []), CancellationToken.None);
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(
                    unavailableSnapshotStableKey,
                    unavailableRepositoryStableKey,
                    solutionStableKey: null,
                    [CreateProjectNode(unavailableSnapshotStableKey, firstProjectStableKey), CreateProjectNode(unavailableSnapshotStableKey, secondProjectStableKey)],
                    [],
                    [],
                    []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument noPathBody = await GetJsonAsync(client, "/dependency-path?repositoryStableKey=repository%3A%2F%2Fdependency-no-path-api&snapshotStableKey=latest&sourceNodeStableKey=project%3A%2F%2Fsrc%2FNoPath.First%2FNoPath.First.csproj&targetNodeStableKey=project%3A%2F%2Fsrc%2FNoPath.Second%2FNoPath.Second.csproj&depth=2");
            JsonDocument unavailableBody = await GetJsonAsync(client, "/dependency-path?repositoryStableKey=repository%3A%2F%2Fdependency-unavailable-api&snapshotStableKey=latest&sourceNodeStableKey=project%3A%2F%2Fsrc%2FNoPath.First%2FNoPath.First.csproj&targetNodeStableKey=project%3A%2F%2Fsrc%2FNoPath.Second%2FNoPath.Second.csproj&depth=2");

            using (noPathBody)
            using (unavailableBody)
            {
                JsonElement noPathData = noPathBody.RootElement.GetProperty("data");
                Assert.False(noPathData.GetProperty("pathFound").GetBoolean());
                Assert.False(noPathData.GetProperty("unavailable").GetBoolean());
                Assert.Contains("No dependency path", noPathData.GetProperty("reason").GetString(), StringComparison.Ordinal);
                Assert.Empty(noPathBody.RootElement.GetProperty("unknowns").EnumerateArray());
                JsonElement unavailableData = unavailableBody.RootElement.GetProperty("data");
                Assert.False(unavailableData.GetProperty("pathFound").GetBoolean());
                Assert.True(unavailableData.GetProperty("unavailable").GetBoolean());
                Assert.Contains("unavailable", unavailableData.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains(unavailableBody.RootElement.GetProperty("unknowns").EnumerateArray(), unknown => unknown.GetProperty("field").GetString() == "dependencyPath");
            }
        }

        /// <summary>
        /// Confirms graph-neighbourhood validation rejects unsupported edge-kind filters safely.
        /// </summary>
        /// <returns>A task that completes after the unsupported edge-kind validation response is asserted.</returns>
        [Fact]
        public async Task GraphNeighbourhoodEndpoint_WhenEdgeKindIsUnsupported_ShouldReturnValidationProblem()
        {
            // Unsupported edge kinds are rejected before graph exploration so callers cannot use the endpoint as an arbitrary traversal language.
            StableKey repositoryStableKey = new("repository://graph-neighbourhood-validation-api");
            StableKey snapshotStableKey = new("snapshot://graph-neighbourhood-validation-api/current");
            StableKey projectStableKey = new("project://src/Any/Any.csproj");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey: null, [CreateProjectNode(snapshotStableKey, projectStableKey)], [], [], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.GetAsync("/graph-neighbourhood?repositoryStableKey=repository%3A%2F%2Fgraph-neighbourhood-validation-api&snapshotStableKey=latest&nodeStableKey=project%3A%2F%2Fsrc%2FAny%2FAny.csproj&edgeKinds=NOT_A_KIND");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("EdgeKindUnsupported", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms symbol search returns filtered, sorted, stable semantic symbol rows with source context and unknown metadata.
        /// </summary>
        /// <returns>A task that completes after the symbol search response is asserted.</returns>
        [Fact]
        public async Task SymbolSearchEndpoint_WhenSymbolsExist_ShouldReturnFilteredSortedPagedEnvelope()
        {
            // The search route exercises the WP014 symbol query slice over persisted semantic nodes without exposing store-local graph IDs.
            StableKey repositoryStableKey = new("repository://symbol-search-api");
            StableKey solutionStableKey = new("solution://symbol-search-api/main");
            StableKey snapshotStableKey = new("snapshot://symbol-search-api/current");
            StableKey projectStableKey = new("project://src/Symbols.Api/Symbols.Api.csproj");
            StableKey typeStableKey = new("symbol://Symbols.Api/Controllers/WeatherController");
            StableKey methodStableKey = new("symbol://Symbols.Api/Controllers/WeatherController/GetForecast");
            StableKey evidenceStableKey = new("evidence://symbol-search/get-forecast");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode project = CreateProjectNode(snapshotStableKey, projectStableKey, "Symbols.Api.csproj", CreateDetailedProjectMetadata("Api", "Api", "net10.0", true, "src/Symbols.Api/Symbols.Api.csproj"));
                ArchitectureNode type = CreateSymbolNode(snapshotStableKey, typeStableKey, NodeKind.Type, "WeatherController", "Symbols.Api.Controllers.WeatherController", projectStableKey, parentStableKey: null, CreateSymbolMetadata("Symbols.Api.Controllers", null), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode method = CreateSymbolNode(snapshotStableKey, methodStableKey, NodeKind.Method, "GetForecast", "Symbols.Api.Controllers.WeatherController.GetForecast", projectStableKey, typeStableKey, CreateSymbolMetadata("Symbols.Api.Controllers", "Symbols.Api.Controllers.WeatherController"), evidenceStableKey, Confidence.High, UnknownState.Unknown("Generic type argument binding was partially unresolved."));
                EvidenceRecord evidence = CreateSymbolEvidence(snapshotStableKey, evidenceStableKey, "src/Symbols.Api/Controllers/WeatherController.cs", 12, 18, "GetForecast", "WeatherController", "return Forecast();");
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey, [project, type, method], [], [evidence], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/symbols?repositoryStableKey=repository%3A%2F%2Fsymbol-search-api&solutionStableKey=solution%3A%2F%2Fsymbol-search-api%2Fmain&snapshotStableKey=latest&searchText=Forecast&projectStableKey=project%3A%2F%2Fsrc%2FSymbols.Api%2FSymbols.Api.csproj&kind=Method&namespaceName=Symbols.Api.Controllers&containingType=Symbols.Api.Controllers.WeatherController&language=C%23&sort=confidence&descending=true&skip=0&take=5");

            using (body)
            {
                Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
                Assert.Equal(repositoryStableKey.Value, body.RootElement.GetProperty("scope").GetProperty("repositoryStableKey").GetString());
                Assert.Equal(solutionStableKey.Value, body.RootElement.GetProperty("scope").GetProperty("solutionStableKey").GetString());
                Assert.Equal(snapshotStableKey.Value, body.RootElement.GetProperty("snapshot").GetProperty("snapshotStableKey").GetString());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(methodStableKey.Value, item.GetProperty("stableKey").GetString());
                Assert.Equal("GetForecast", item.GetProperty("name").GetString());
                Assert.Equal("Symbols.Api.Controllers.WeatherController.GetForecast", item.GetProperty("fullyQualifiedName").GetString());
                Assert.Equal("Method", item.GetProperty("kind").GetString());
                Assert.Equal(projectStableKey.Value, item.GetProperty("containingProjectStableKey").GetString());
                Assert.Equal("Symbols.Api.Controllers", item.GetProperty("namespace").GetString());
                Assert.Equal("Symbols.Api.Controllers.WeatherController", item.GetProperty("containingType").GetString());
                Assert.Equal("src/Symbols.Api/Controllers/WeatherController.cs", item.GetProperty("sourceContext").GetProperty("filePath").GetString());
                Assert.True(item.GetProperty("hasUnknownData").GetBoolean());
                Assert.Contains("partially unresolved", item.GetProperty("unknownReason").GetString(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("neo4j", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms symbol detail lookup by stable key returns semantic evidence and related relationships.
        /// </summary>
        /// <returns>A task that completes after the symbol detail response is asserted.</returns>
        [Fact]
        public async Task SymbolDetailEndpoint_WhenSymbolStableKeyExists_ShouldReturnDetailEnvelope()
        {
            // Stable-key lookup proves consumers can follow search links to a bounded symbol detail response with evidence and relationships.
            StableKey repositoryStableKey = new("repository://symbol-detail-api");
            StableKey snapshotStableKey = new("snapshot://symbol-detail-api/current");
            StableKey projectStableKey = new("project://src/SymbolDetail.Api/SymbolDetail.Api.csproj");
            StableKey callerStableKey = new("symbol://SymbolDetail.Api/Controllers/WeatherController/Get");
            StableKey targetStableKey = new("symbol://SymbolDetail.Application/WeatherService/GetForecast");
            StableKey evidenceStableKey = new("evidence://symbol-detail/call");
            ArchitectureEdge callEdge = CreateSymbolEdge(snapshotStableKey, "edge://symbol-detail/controller-service", EdgeKind.Calls, callerStableKey, targetStableKey, evidenceStableKey.Value);
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode caller = CreateSymbolNode(snapshotStableKey, callerStableKey, NodeKind.Method, "Get", "SymbolDetail.Api.Controllers.WeatherController.Get", projectStableKey, parentStableKey: null, CreateSymbolMetadata("SymbolDetail.Api.Controllers", "SymbolDetail.Api.Controllers.WeatherController"), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode target = CreateSymbolNode(snapshotStableKey, targetStableKey, NodeKind.Method, "GetForecast", "SymbolDetail.Application.WeatherService.GetForecast", projectStableKey, parentStableKey: null, CreateSymbolMetadata("SymbolDetail.Application", "SymbolDetail.Application.WeatherService"), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                EvidenceRecord evidence = CreateSymbolEvidence(snapshotStableKey, evidenceStableKey, "src/SymbolDetail.Api/Controllers/WeatherController.cs", 20, 21, "Get", "WeatherController", "return service.GetForecast();");
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey: null, [CreateProjectNode(snapshotStableKey, projectStableKey), caller, target], [callEdge], [evidence], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/symbols/detail?repositoryStableKey=repository%3A%2F%2Fsymbol-detail-api&snapshotStableKey=latest&symbolStableKey=symbol%3A%2F%2FSymbolDetail.Api%2FControllers%2FWeatherController%2FGet");

            using (body)
            {
                JsonElement data = body.RootElement.GetProperty("data");
                Assert.Equal(callerStableKey.Value, data.GetProperty("summary").GetProperty("stableKey").GetString());
                JsonElement evidence = Assert.Single(data.GetProperty("evidence").EnumerateArray());
                Assert.Equal(evidenceStableKey.Value, evidence.GetProperty("stableKey").GetString());
                Assert.Equal("src/SymbolDetail.Api/Controllers/WeatherController.cs", evidence.GetProperty("filePath").GetString());
                JsonElement relationship = Assert.Single(data.GetProperty("relationships").EnumerateArray());
                Assert.Equal(callEdge.StableKey.Value, relationship.GetProperty("stableKey").GetString());
                Assert.Equal(EdgeKind.Calls.Value, relationship.GetProperty("kind").GetString());
                Assert.Equal(targetStableKey.Value, relationship.GetProperty("targetSymbolStableKey").GetString());
                Assert.Empty(body.RootElement.GetProperty("unknowns").EnumerateArray());
            }
        }

        /// <summary>
        /// Confirms symbol usage queries return bounded evidence snippets for referencing or calling relationships.
        /// </summary>
        /// <returns>A task that completes after the symbol usage response is asserted.</returns>
        [Fact]
        public async Task SymbolUsageEndpoint_WhenUsagesExist_ShouldReturnEvidenceAndBoundSnippetPreview()
        {
            // Usage evidence is source text and must be bounded before it is serialized in the public API response.
            StableKey repositoryStableKey = new("repository://symbol-usage-api");
            StableKey snapshotStableKey = new("snapshot://symbol-usage-api/current");
            StableKey projectStableKey = new("project://src/SymbolUsage.Api/SymbolUsage.Api.csproj");
            StableKey callerStableKey = new("symbol://SymbolUsage.Api/Caller/Run");
            StableKey targetStableKey = new("symbol://SymbolUsage.Core/Target/Execute");
            StableKey evidenceStableKey = new("evidence://symbol-usage/call");
            string longSnippet = new('x', 240);
            ArchitectureEdge callEdge = CreateSymbolEdge(snapshotStableKey, "edge://symbol-usage/caller-target", EdgeKind.Calls, callerStableKey, targetStableKey, evidenceStableKey.Value);
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode caller = CreateSymbolNode(snapshotStableKey, callerStableKey, NodeKind.Method, "Run", "SymbolUsage.Api.Caller.Run", projectStableKey, parentStableKey: null, CreateSymbolMetadata("SymbolUsage.Api", "SymbolUsage.Api.Caller"), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode target = CreateSymbolNode(snapshotStableKey, targetStableKey, NodeKind.Method, "Execute", "SymbolUsage.Core.Target.Execute", projectStableKey, parentStableKey: null, CreateSymbolMetadata("SymbolUsage.Core", "SymbolUsage.Core.Target"), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                EvidenceRecord evidence = CreateSymbolEvidence(snapshotStableKey, evidenceStableKey, "src/SymbolUsage.Api/Caller.cs", 42, 43, "Run", "Caller", longSnippet);
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey: null, [CreateProjectNode(snapshotStableKey, projectStableKey), caller, target], [callEdge], [evidence], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/symbols/usages?repositoryStableKey=repository%3A%2F%2Fsymbol-usage-api&snapshotStableKey=latest&symbolStableKey=symbol%3A%2F%2FSymbolUsage.Core%2FTarget%2FExecute&direction=Incoming&take=5");

            using (body)
            {
                Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement usage = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(callEdge.StableKey.Value, usage.GetProperty("usageStableKey").GetString());
                Assert.Equal(EdgeKind.Calls.Value, usage.GetProperty("usageKind").GetString());
                Assert.Equal(callerStableKey.Value, usage.GetProperty("sourceSymbolStableKey").GetString());
                Assert.Equal(targetStableKey.Value, usage.GetProperty("targetSymbolStableKey").GetString());
                Assert.Equal("src/SymbolUsage.Api/Caller.cs", usage.GetProperty("filePath").GetString());
                Assert.Equal(42, usage.GetProperty("startLine").GetInt32());
                string snippetPreview = usage.GetProperty("snippetPreview").GetString()!;
                Assert.Equal(160, snippetPreview.Length);
                Assert.Equal(evidenceStableKey.Value, Assert.Single(usage.GetProperty("evidenceStableKeys").EnumerateArray()).GetString());
            }
        }

        /// <summary>
        /// Confirms unresolved symbol facts are represented as unknowns instead of implied completeness.
        /// </summary>
        /// <returns>A task that completes after the unresolved symbol response is asserted.</returns>
        [Fact]
        public async Task SymbolDetailEndpoint_WhenSymbolIsUnresolved_ShouldReturnUnknownMetadata()
        {
            // Unknown semantic facts remain successful query data so clients can distinguish unresolved extraction from missing symbols.
            StableKey repositoryStableKey = new("repository://symbol-unresolved-api");
            StableKey snapshotStableKey = new("snapshot://symbol-unresolved-api/current");
            StableKey projectStableKey = new("project://src/SymbolUnresolved.Api/SymbolUnresolved.Api.csproj");
            StableKey unresolvedStableKey = new("symbol://SymbolUnresolved.Api/MissingType/UnresolvedCall");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode unresolved = CreateSymbolNode(snapshotStableKey, unresolvedStableKey, NodeKind.Method, "UnresolvedCall", "SymbolUnresolved.Api.MissingType.UnresolvedCall", projectStableKey, parentStableKey: null, CreateSymbolMetadata("SymbolUnresolved.Api", "SymbolUnresolved.Api.MissingType"), evidenceStableKey: null, Confidence.Low, UnknownState.Unknown("Roslyn could not resolve the target symbol from available compilation references."));
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey: null, [CreateProjectNode(snapshotStableKey, projectStableKey), unresolved], [], [], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/symbols/detail?repositoryStableKey=repository%3A%2F%2Fsymbol-unresolved-api&snapshotStableKey=latest&searchText=UnresolvedCall");

            using (body)
            {
                JsonElement data = body.RootElement.GetProperty("data");
                Assert.Equal(unresolvedStableKey.Value, data.GetProperty("summary").GetProperty("stableKey").GetString());
                Assert.True(data.GetProperty("summary").GetProperty("hasUnknownData").GetBoolean());
                Assert.Empty(data.GetProperty("evidence").EnumerateArray());
                Assert.Contains(body.RootElement.GetProperty("unknowns").EnumerateArray(), unknown => unknown.GetProperty("field").GetString() == "symbol");
                Assert.Contains(body.RootElement.GetProperty("unknowns").EnumerateArray(), unknown => unknown.GetProperty("field").GetString() == "evidence");
            }
        }

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
        /// Confirms rule catalog list filters can select an exact versioned critical security-sensitive rule.
        /// </summary>
        /// <returns>A task that completes after the filtered rule catalog response is asserted.</returns>
        [Fact]
        public async Task RuleCatalogEndpoint_WhenExactCriticalRuleFiltersAreSupplied_ShouldReturnMatchingVersionedRule()
        {
            // Work Item 8 requires rule catalog consumers to filter by rule code, version, category, severity, and enabled state without reading raw catalog storage.
            RuleCatalogEntry securityRule = CreateRule("ARCHON-SECURITY-CRITICAL", "2.1.0", RuleCategory.SecuritySensitive, FindingSeverity.Critical, enabled: true, builtIn: true, ownerScope: "Archon");
            RuleCatalogEntry disabledRule = CreateRule("ARCHON-SECURITY-CRITICAL", "2.0.0", RuleCategory.SecuritySensitive, FindingSeverity.High, enabled: false, builtIn: true, ownerScope: "Archon");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IRuleCatalogStore catalog = services.GetRequiredService<IRuleCatalogStore>();
                await catalog.UpsertRulesAsync([securityRule, disabledRule], CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/rules?ruleCode=ARCHON-SECURITY-CRITICAL&version=2.1.0&category=SecuritySensitive&severity=Critical&enabled=true&builtIn=true&ownerScope=Archon&take=5");

            using (body)
            {
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("ARCHON-SECURITY-CRITICAL", item.GetProperty("ruleCode").GetString());
                Assert.Equal("2.1.0", item.GetProperty("version").GetString());
                Assert.Equal("SecuritySensitive", item.GetProperty("category").GetString());
                Assert.Equal("Critical", item.GetProperty("severity").GetString());
                Assert.True(item.GetProperty("enabled").GetBoolean());
                Assert.DoesNotContain("neo4j", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms hotlist finding filters cover critical-only, modernization indicators, technology, status, rule code, project, affected node, evidence, and unknown-state fields.
        /// </summary>
        /// <returns>A task that completes after the filtered hotlist response is asserted.</returns>
        [Fact]
        public async Task HotlistEndpoint_WhenWorkItem8FiltersAreSupplied_ShouldReturnMatchingEvidenceBackedFinding()
        {
            // The fixture uses safe metadata indicators to prove the public filter set can isolate modernization findings without accepting arbitrary predicates.
            RuleCatalogEntry rule = CreateRule("ARCHON-WP014-HOTLIST", "1.0.0", RuleCategory.SecuritySensitive, FindingSeverity.Critical, enabled: true, builtIn: true, ownerScope: "Archon");
            FindingRecord matchingFinding = CreateFindingWithAnalysisMetadata("snapshot://wp014-hotlist", "finding://wp014-hotlist/match", "history://wp014-hotlist/match", rule.RuleCode, rule.Version, FindingSeverity.Critical, FindingStatus.Open, "project://src/Legacy.Data/Legacy.Data.csproj", "evidence://wp014-hotlist/match");
            FindingRecord otherFinding = CreateFinding("snapshot://wp014-hotlist", "finding://wp014-hotlist/other", "history://wp014-hotlist/other", rule.RuleCode, rule.Version, FindingSeverity.High, FindingStatus.Resolved, "project://src/Modern.Data/Modern.Data.csproj", "evidence://wp014-hotlist/other");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                await services.GetRequiredService<IRuleCatalogStore>().UpsertRulesAsync([rule], CancellationToken.None);
                await services.GetRequiredService<IFindingStore>().UpsertFindingsAsync([matchingFinding, otherFinding], CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/hotlist?snapshotStableKey=snapshot%3A%2F%2Fwp014-hotlist&criticalOnly=true&legacyDataAccess=true&outOfSupport=true&securitySensitive=true&frameworkOnly=true&technology=LINQ%20to%20SQL&severity=Critical&status=Open&ruleCode=ARCHON-WP014-HOTLIST&projectStableKey=project%3A%2F%2Fsrc%2FLegacy.Data%2FLegacy.Data.csproj&affectedNodeStableKey=project%3A%2F%2Fsrc%2FLegacy.Data%2FLegacy.Data.csproj&take=5");

            using (body)
            {
                Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("finding://wp014-hotlist/match", item.GetProperty("stableKey").GetString());
                Assert.Equal("Critical", item.GetProperty("severity").GetString());
                Assert.Equal("Open", item.GetProperty("status").GetString());
                Assert.Equal("ARCHON-WP014-HOTLIST", item.GetProperty("ruleCode").GetString());
                Assert.Equal("evidence://wp014-hotlist/match", Assert.Single(item.GetProperty("evidenceReferences").EnumerateArray()).GetProperty("stableKey").GetString());
                Assert.False(item.GetProperty("hasUnknownData").GetBoolean());
                Assert.DoesNotContain("Neo4j", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
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
        /// Confirms latest snapshot diff resolves the newest comparable snapshots and applies project, target, kind, and severity filters.
        /// </summary>
        /// <returns>A task that completes after the latest-to-previous diff response is asserted.</returns>
        [Fact]
        public async Task SnapshotDiffLatestEndpoint_WhenComparableSnapshotsExist_ShouldApplyMcpReadyFilters()
        {
            // Latest-to-previous comparison proves future MCP callers do not need direct graph access to compare recent extraction output.
            StableKey repositoryStableKey = new("repository://snapshot-diff-latest-api");
            StableKey previousSnapshot = new("snapshot://snapshot-diff-latest-api/previous");
            StableKey currentSnapshot = new("snapshot://snapshot-diff-latest-api/current");
            StableKey projectStableKey = new("project://src/DiffLatest.Api/DiffLatest.Api.csproj");
            StableKey otherProjectStableKey = new("project://src/DiffLatest.Other/DiffLatest.Other.csproj");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                FindingRecord previousFinding = CreateFinding(previousSnapshot.Value, "finding://diff-latest/security-previous", "history://diff-latest/security", "ARCHON-SEC", "1.0.0", FindingSeverity.High, FindingStatus.Open, projectStableKey.Value, "evidence://diff-latest/security");
                FindingRecord currentFinding = CreateFinding(currentSnapshot.Value, "finding://diff-latest/security", "history://diff-latest/security", "ARCHON-SEC", "1.0.0", FindingSeverity.Critical, FindingStatus.Open, projectStableKey.Value, "evidence://diff-latest/security");
                FindingRecord unrelatedFinding = CreateFinding(currentSnapshot.Value, "finding://diff-latest/other", "history://diff-latest/other", "ARCHON-OTHER", "1.0.0", FindingSeverity.Critical, FindingStatus.Open, otherProjectStableKey.Value, "evidence://diff-latest/other");
                await writer.WriteSnapshotAsync(CreateDiffSnapshot(previousSnapshot, repositoryStableKey, [CreateProjectNode(previousSnapshot, projectStableKey, "DiffLatest.Api.csproj", GraphMetadata.Empty, "sha256:project-previous")], [], [previousFinding], []), CancellationToken.None);
                await writer.WriteSnapshotAsync(CreateDiffSnapshot(currentSnapshot, repositoryStableKey, [CreateProjectNode(currentSnapshot, projectStableKey, "DiffLatest.Api.csproj", GraphMetadata.Empty, "sha256:project-current")], [], [currentFinding, unrelatedFinding], [], new DateTimeOffset(2026, 5, 21, 8, 1, 0, TimeSpan.Zero)), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/snapshot-diff/latest?repositoryStableKey=repository%3A%2F%2Fsnapshot-diff-latest-api&domains=Findings&changeKinds=Added&projectStableKey=project%3A%2F%2Fsrc%2FDiffLatest.Api%2FDiffLatest.Api.csproj&targetStableKey=project%3A%2F%2Fsrc%2FDiffLatest.Api%2FDiffLatest.Api.csproj&recordKind=ARCHON-SEC&severity=Critical&take=5");

            using (body)
            {
                Assert.True(body.RootElement.GetProperty("succeeded").GetBoolean());
                Assert.Equal(currentSnapshot.Value, body.RootElement.GetProperty("currentSnapshotStableKey").GetString());
                Assert.Equal(previousSnapshot.Value, body.RootElement.GetProperty("previousSnapshotStableKey").GetString());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(SnapshotDiffDomains.Findings, item.GetProperty("domain").GetString());
                Assert.Equal(SnapshotDiffChangeKind.Added, item.GetProperty("changeKind").GetString());
                Assert.Equal("finding://diff-latest/security", item.GetProperty("stableKey").GetString());
                Assert.Equal(projectStableKey.Value, item.GetProperty("projectStableKey").GetString());
                Assert.Equal("Critical", item.GetProperty("severity").GetString());
                Assert.Contains(projectStableKey.Value, item.GetProperty("targetStableKeys").EnumerateArray().Select(static value => value.GetString()));
                Assert.DoesNotContain("Neo4j", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms cross-domain search returns stable result families, evidence, unknowns, and deterministic follow-up affordances.
        /// </summary>
        /// <returns>A task that completes after the search response is asserted.</returns>
        [Fact]
        public async Task SearchEndpoint_WhenSupportedRecordsMatch_ShouldReturnMcpReadyResultFamilies()
        {
            // Search covers representative WP015 dependency families without exposing direct Neo4j access or unbounded source expansion.
            StableKey repositoryStableKey = new("repository://search-api");
            StableKey snapshotStableKey = new("snapshot://search-api/current");
            StableKey projectStableKey = new("project://src/Search.Api/Search.Api.csproj");
            StableKey symbolStableKey = new("symbol://search-api/SearchController/GetOrders");
            StableKey endpointStableKey = new("endpoint://search-api/orders");
            StableKey factStableKey = new("dbcontext://search-api/orders");
            StableKey evidenceStableKey = new("evidence://search-api/orders");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode project = CreateProjectNode(snapshotStableKey, projectStableKey, "Orders Search.Api.csproj", CreateDetailedProjectMetadata("Api", "Api", "net10.0", true, "src/Search.Api/Search.Api.csproj"), "sha256:search-project");
                ArchitectureNode symbol = CreateSymbolNode(snapshotStableKey, symbolStableKey, NodeKind.Method, "GetOrders", "Search.Api.Controllers.SearchController.GetOrders", projectStableKey, null, CreateSymbolMetadata("Search.Api.Controllers", "SearchController"), evidenceStableKey, Confidence.High, UnknownState.Unknown("Generic overload resolution was incomplete."));
                ArchitectureNode endpoint = CreateRuntimeNode(snapshotStableKey, endpointStableKey, NodeKind.Endpoint, "GET /orders", "GET /orders", projectStableKey, CreateEndpointMetadata("GET", "/orders", "SearchController", "GetOrders", null, null, "Authorize"), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode fact = CreateFactNode(snapshotStableKey, factStableKey, NodeKind.DbContext, "OrdersDbContext", "Search.Api.OrdersDbContext", projectStableKey, CreateDataAccessMetadata("EFCore", ["Read"]), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                EvidenceRecord evidence = CreateDetailedEvidence(snapshotStableKey, evidenceStableKey, "src/Search.Api/Controllers/SearchController.cs", "public IActionResult GetOrders() => Ok();", UnknownState.Known);
                FindingRecord finding = CreateFinding(snapshotStableKey.Value, "finding://search-api/orders", "history://search-api/orders", "ARCHON-SEARCH", "1.0.0", FindingSeverity.High, FindingStatus.Open, projectStableKey.Value, evidenceStableKey.Value);
                MetricRecord metric = CreateMetric(snapshotStableKey.Value, "metric://search-api/orders-fan-in", "GraphFanIn", 7, MetricScopeKind.Node, projectStableKey, "edges");
                await writer.WriteSnapshotAsync(CreateEvidenceSnapshot(snapshotStableKey, repositoryStableKey, [project, symbol, endpoint, fact], [], [evidence], [finding], [metric], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/search?repositoryStableKey=repository%3A%2F%2Fsearch-api&snapshotStableKey=latest&searchText=Orders&resultKinds=Project,Symbol,RuntimeEndpoint,Fact,Evidence,Finding,Metric&take=20");

            using (body)
            {
                Assert.Equal(snapshotStableKey.Value, body.RootElement.GetProperty("snapshot").GetProperty("snapshotStableKey").GetString());
                Assert.True(body.RootElement.GetProperty("snapshot").GetProperty("resolvedAsLatest").GetBoolean());
                Assert.True(body.RootElement.GetProperty("totalCount").GetInt32() >= 7);
                string[] resultKinds = body.RootElement.GetProperty("items").EnumerateArray().Select(static item => item.GetProperty("resultKind").GetString()!).ToArray();
                Assert.Contains("Project", resultKinds);
                Assert.Contains("Symbol", resultKinds);
                Assert.Contains("RuntimeEndpoint", resultKinds);
                Assert.Contains("Fact", resultKinds);
                Assert.Contains("Evidence", resultKinds);
                Assert.Contains("Finding", resultKinds);
                Assert.Contains("Metric", resultKinds);
                JsonElement symbolItem = body.RootElement.GetProperty("items").EnumerateArray().First(item => item.GetProperty("resultKind").GetString() == "Symbol");
                Assert.Equal(symbolStableKey.Value, symbolItem.GetProperty("stableKey").GetString());
                Assert.Contains(evidenceStableKey.Value, symbolItem.GetProperty("evidenceStableKeys").EnumerateArray().Select(static value => value.GetString()));
                Assert.True(symbolItem.GetProperty("hasUnknownData").GetBoolean());
                JsonElement followUp = Assert.Single(symbolItem.GetProperty("followUps").EnumerateArray());
                Assert.Equal("/symbols/detail", followUp.GetProperty("route").GetString());
                Assert.Equal(symbolStableKey.Value, followUp.GetProperty("parameters").GetProperty("symbolStableKey").GetString());
                Assert.DoesNotContain("neo4j", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms cross-domain search validates unsupported result kinds and missing search text with safe problem details.
        /// </summary>
        /// <returns>A task that completes after the validation response is asserted.</returns>
        [Fact]
        public async Task SearchEndpoint_WhenRequestIsInvalid_ShouldReturnValidationProblem()
        {
            // Search validation is contract-level so automated clients can repair inputs without parsing exception text.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.GetAsync("/search?repositoryStableKey=repository%3A%2F%2Fsearch-api&resultKinds=Project,RawCypher");
            JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

            using (body)
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("SearchTextRequired", out JsonElement searchTextErrors));
                Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("UnsupportedResultKind", out JsonElement resultKindErrors));
                Assert.NotEmpty(searchTextErrors.EnumerateArray());
                Assert.Contains(resultKindErrors.EnumerateArray().Select(static error => error.GetString()), static message => message?.Contains("RawCypher", StringComparison.Ordinal) == true);
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
        /// Confirms evidence detail returns source coordinates, bounded snippet metadata, related finding/rule context, unknown reason, and secret-safe metadata.
        /// </summary>
        /// <returns>A task that completes after the evidence detail response is asserted.</returns>
        [Fact]
        public async Task EvidenceDetailEndpoint_WhenEvidenceExists_ShouldReturnBoundedSecretSafeDetailEnvelope()
        {
            // Evidence detail proves claims can be drilled down through stable evidence identities without reading or expanding source files.
            StableKey repositoryStableKey = new("repository://evidence-api");
            StableKey snapshotStableKey = new("snapshot://evidence-api/current");
            StableKey projectStableKey = new("project://src/Evidence.Api/Evidence.Api.csproj");
            StableKey evidenceStableKey = new("evidence://evidence-api/controller");
            string longSnippet = "public class EvidenceController { " + new string('x', 300) + " }";
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode project = CreateEvidenceProjectNode(snapshotStableKey, projectStableKey, evidenceStableKey);
                EvidenceRecord evidence = CreateDetailedEvidence(snapshotStableKey, evidenceStableKey, "src/Evidence.Api/Controllers/EvidenceController.cs", longSnippet, UnknownState.Unknown("Symbol binding was partially inferred from generated source."));
                FindingRecord finding = CreateFinding(snapshotStableKey.Value, "finding://evidence-api/controller", "history://evidence-api/controller", "ARCHON-EVIDENCE", "1.0.0", FindingSeverity.High, FindingStatus.Open, projectStableKey.Value, evidenceStableKey.Value);
                RuleDefinition rule = CreateRuleDefinition("ARCHON-EVIDENCE", "1.0.0", "Evidence-backed controller rule");
                await writer.WriteSnapshotAsync(CreateEvidenceSnapshot(snapshotStableKey, repositoryStableKey, [project], [], [evidence], [finding], [], [rule]), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/evidence/detail?repositoryStableKey=repository%3A%2F%2Fevidence-api&snapshotStableKey=latest&evidenceStableKey=evidence%3A%2F%2Fevidence-api%2Fcontroller");

            using (body)
            {
                Assert.Equal(repositoryStableKey.Value, body.RootElement.GetProperty("scope").GetProperty("repositoryStableKey").GetString());
                Assert.Equal(snapshotStableKey.Value, body.RootElement.GetProperty("snapshot").GetProperty("snapshotStableKey").GetString());
                JsonElement data = body.RootElement.GetProperty("data");
                Assert.Equal(evidenceStableKey.Value, data.GetProperty("stableKey").GetString());
                Assert.Equal("src/Evidence.Api/Controllers/EvidenceController.cs", data.GetProperty("filePath").GetString());
                Assert.Equal(10, data.GetProperty("startLine").GetInt32());
                Assert.Equal(14, data.GetProperty("endLine").GetInt32());
                Assert.Equal("EvidenceController", data.GetProperty("symbolName").GetString());
                JsonElement snippet = data.GetProperty("snippetPreview");
                Assert.True(snippet.GetProperty("truncated").GetBoolean());
                Assert.Equal(240, snippet.GetProperty("returnedLength").GetInt32());
                Assert.Equal(240, snippet.GetProperty("limit").GetInt32());
                Assert.True(data.GetProperty("unknownReason").GetProperty("hasUnknownData").GetBoolean());
                Assert.Contains("partially inferred", data.GetProperty("unknownReason").GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains(data.GetProperty("findingContext").EnumerateArray(), finding => finding.GetProperty("stableKey").GetString() == "finding://evidence-api/controller");
                Assert.Contains(data.GetProperty("ruleContext").EnumerateArray(), rule => rule.GetProperty("stableKey").GetString() == "rule://ARCHON-EVIDENCE/1.0.0");
                Assert.False(data.GetProperty("metadata").GetProperty("isEmpty").GetBoolean());
                Assert.DoesNotContain("secret", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("neo4j", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms related-evidence lookup follows explicit node, edge, finding, metric, and rule relationships through stable keys.
        /// </summary>
        /// <returns>A task that completes after related-evidence relationship traversal is asserted.</returns>
        [Fact]
        public async Task RelatedEvidenceEndpoint_WhenRelatedRecordExists_ShouldReturnEvidencePage()
        {
            // Related-evidence lookup is verified through a finding so the API follows supporting evidence keys rather than source text or persistence IDs.
            StableKey repositoryStableKey = new("repository://related-evidence-api");
            StableKey snapshotStableKey = new("snapshot://related-evidence-api/current");
            StableKey projectStableKey = new("project://src/Related.Api/Related.Api.csproj");
            StableKey evidenceStableKey = new("evidence://related-evidence-api/finding");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode project = CreateEvidenceProjectNode(snapshotStableKey, projectStableKey, evidenceStableKey);
                EvidenceRecord evidence = CreateDetailedEvidence(snapshotStableKey, evidenceStableKey, "src/Related.Api/Related.cs", "public sealed class Related { }", UnknownState.Known);
                FindingRecord finding = CreateFinding(snapshotStableKey.Value, "finding://related-evidence-api/finding", "history://related-evidence-api/finding", "ARCHON-RELATED", "1.0.0", FindingSeverity.Medium, FindingStatus.Open, projectStableKey.Value, evidenceStableKey.Value);
                MetricRecord metric = CreateEvidenceMetric(snapshotStableKey, new StableKey("metric://related-evidence-api/fan-in"), projectStableKey, evidenceStableKey);
                await writer.WriteSnapshotAsync(CreateEvidenceSnapshot(snapshotStableKey, repositoryStableKey, [project], [], [evidence], [finding], [metric], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/evidence/related?repositoryStableKey=repository%3A%2F%2Frelated-evidence-api&snapshotStableKey=snapshot%3A%2F%2Frelated-evidence-api%2Fcurrent&relatedStableKey=finding%3A%2F%2Frelated-evidence-api%2Ffinding&relatedKind=Finding&take=5");

            using (body)
            {
                Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
                Assert.Equal(0, body.RootElement.GetProperty("skip").GetInt32());
                Assert.Equal(5, body.RootElement.GetProperty("take").GetInt32());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(evidenceStableKey.Value, item.GetProperty("stableKey").GetString());
                Assert.Contains(item.GetProperty("relatedRecords").EnumerateArray(), record => record.GetProperty("kind").GetString() == "Finding");
                Assert.Contains(item.GetProperty("relatedRecords").EnumerateArray(), record => record.GetProperty("kind").GetString() == "Metric");
                Assert.DoesNotContain("neo4j", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms evidence detail redacts secret-like snippet previews instead of echoing source text that may contain credentials.
        /// </summary>
        /// <returns>A task that completes after snippet redaction is asserted.</returns>
        [Fact]
        public async Task EvidenceDetailEndpoint_WhenSnippetLooksSecret_ShouldRedactPreview()
        {
            // Evidence snippet previews are untrusted source text, so secret-like markers trigger full preview redaction.
            StableKey repositoryStableKey = new("repository://secret-evidence-api");
            StableKey snapshotStableKey = new("snapshot://secret-evidence-api/current");
            StableKey evidenceStableKey = new("evidence://secret-evidence-api/config");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                EvidenceRecord evidence = CreateDetailedEvidence(snapshotStableKey, evidenceStableKey, "src/Secret.Api/appsettings.json", "\"Password\":\"super-secret-value\"", UnknownState.Known);
                await writer.WriteSnapshotAsync(CreateEvidenceSnapshot(snapshotStableKey, repositoryStableKey, [], [], [evidence], [], [], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/evidence/detail?repositoryStableKey=repository%3A%2F%2Fsecret-evidence-api&snapshotStableKey=current&evidenceStableKey=evidence%3A%2F%2Fsecret-evidence-api%2Fconfig");

            using (body)
            {
                JsonElement snippet = body.RootElement.GetProperty("data").GetProperty("snippetPreview");
                Assert.True(snippet.GetProperty("redacted").GetBoolean());
                Assert.Equal("[redacted secret-like evidence preview]", snippet.GetProperty("text").GetString());
                Assert.DoesNotContain("super-secret-value", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms evidence endpoints use deterministic safe validation problem responses for missing and unknown evidence inputs.
        /// </summary>
        /// <returns>A task that completes after evidence validation responses are asserted.</returns>
        [Fact]
        public async Task EvidenceEndpoints_WhenRequestIsInvalid_ShouldReturnSafeValidationProblems()
        {
            // Invalid evidence requests should be client-correctable and must not expose implementation exception details.
            StableKey repositoryStableKey = new("repository://missing-evidence-api");
            StableKey snapshotStableKey = new("snapshot://missing-evidence-api/current");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateEvidenceSnapshot(snapshotStableKey, repositoryStableKey, [], [], [], [], [], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage missingKey = await client.GetAsync("/evidence/detail?repositoryStableKey=repository%3A%2F%2Fmissing-evidence-api&snapshotStableKey=current");
            HttpResponseMessage unknownKey = await client.GetAsync("/evidence/detail?repositoryStableKey=repository%3A%2F%2Fmissing-evidence-api&snapshotStableKey=current&evidenceStableKey=evidence%3A%2F%2Fmissing-evidence-api%2Funknown");
            HttpResponseMessage invalidPaging = await client.GetAsync("/evidence/related?repositoryStableKey=repository%3A%2F%2Fmissing-evidence-api&snapshotStableKey=current&relatedStableKey=finding%3A%2F%2Fmissing&take=0");

            string missingBody = await missingKey.Content.ReadAsStringAsync();
            string unknownBody = await unknownKey.Content.ReadAsStringAsync();
            string invalidPagingBody = await invalidPaging.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, unknownKey.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, invalidPaging.StatusCode);
            Assert.Contains("EvidenceStableKeyRequired", missingBody, StringComparison.Ordinal);
            Assert.Contains("EvidenceNotFound", unknownBody, StringComparison.Ordinal);
            Assert.Contains("TakeInvalid", invalidPagingBody, StringComparison.Ordinal);
            Assert.DoesNotContain("System.", missingBody + unknownBody + invalidPagingBody, StringComparison.Ordinal);
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
        /// Confirms runtime endpoint queries expose filtered endpoint facts with controller, handler, authorization, dependency, data, configuration, and evidence metadata.
        /// </summary>
        /// <returns>A task that completes after runtime endpoint response fields are asserted.</returns>
        [Fact]
        public async Task RuntimeEndpointEndpoint_WhenEndpointFactsExist_ShouldReturnFilteredRuntimeEnvelope()
        {
            // Runtime endpoint lookup proves WP014 can expose API behavior through stable DTOs without adding Discovery UI assets or graph-store IDs.
            StableKey repositoryStableKey = new("repository://runtime-endpoint-api");
            StableKey snapshotStableKey = new("snapshot://runtime-endpoint-api/current");
            StableKey projectStableKey = new("project://src/Runtime.Api/Runtime.Api.csproj");
            StableKey endpointStableKey = new("endpoint://runtime-api/orders/{id}");
            StableKey controllerStableKey = new("controller://runtime-api/OrdersController");
            StableKey dbContextStableKey = new("dbcontext://runtime-api/orders");
            StableKey configStableKey = new("config://runtime-api/ConnectionStrings.Orders");
            StableKey evidenceStableKey = new("evidence://runtime-api/orders-endpoint");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode project = CreateProjectNode(snapshotStableKey, projectStableKey, "Runtime.Api.csproj", CreateDetailedProjectMetadata("Api", "Api", "net10.0", true, "src/Runtime.Api/Runtime.Api.csproj"));
                ArchitectureNode endpoint = CreateRuntimeNode(snapshotStableKey, endpointStableKey, NodeKind.Endpoint, "GET /orders/{id}", "GET /orders/{id}", projectStableKey, CreateEndpointMetadata("GET", "/orders/{id}", "OrdersController", "GetOrder", "OrderRequest", "OrderResponse", "Authorize"), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode controller = CreateRuntimeNode(snapshotStableKey, controllerStableKey, NodeKind.Controller, "OrdersController", "Runtime.Api.Controllers.OrdersController", projectStableKey, CreateControllerMetadata(), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode dbContext = CreateOwnedNode(snapshotStableKey, dbContextStableKey, NodeKind.DbContext, "OrdersDbContext", projectStableKey);
                ArchitectureNode config = CreateOwnedNode(snapshotStableKey, configStableKey, NodeKind.ConfigurationKey, "ConnectionStrings:Orders", projectStableKey);
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(
                    snapshotStableKey,
                    repositoryStableKey,
                    solutionStableKey: null,
                    [project, endpoint, controller, dbContext, config],
                    [
                        CreateEdge(snapshotStableKey, "edge://runtime-api/controller-exposes-endpoint", EdgeKind.Exposes, controllerStableKey, endpointStableKey, evidenceStableKey.Value),
                        CreateEdge(snapshotStableKey, "edge://runtime-api/endpoint-uses-db", EdgeKind.UsesDbContext, endpointStableKey, dbContextStableKey, evidenceStableKey.Value),
                        CreateEdge(snapshotStableKey, "edge://runtime-api/endpoint-uses-config", EdgeKind.UsesConfig, endpointStableKey, configStableKey, evidenceStableKey.Value)
                    ],
                    [CreateSymbolEvidence(snapshotStableKey, evidenceStableKey, "src/Runtime.Api/Controllers/OrdersController.cs", 12, 20, "GetOrder", "OrdersController", "[Authorize] public OrderResponse GetOrder(OrderRequest request)")],
                    []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/runtime/endpoints?repositoryStableKey=repository%3A%2F%2Fruntime-endpoint-api&snapshotStableKey=latest&httpMethod=GET&route=orders&projectStableKey=project%3A%2F%2Fsrc%2FRuntime.Api%2FRuntime.Api.csproj&controllerOrHandler=Orders&authorization=Authorize&sort=route&take=10");

            using (body)
            {
                Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(endpointStableKey.Value, item.GetProperty("stableKey").GetString());
                Assert.Equal("GET", item.GetProperty("httpMethod").GetString());
                Assert.Equal("/orders/{id}", item.GetProperty("route").GetString());
                Assert.Equal("OrdersController", item.GetProperty("controllerName").GetString());
                Assert.Equal("GetOrder", item.GetProperty("actionName").GetString());
                Assert.Equal("OrderRequest", item.GetProperty("requestDto").GetString());
                Assert.Equal("OrderResponse", item.GetProperty("responseDto").GetString());
                Assert.Contains("Authorize", item.GetProperty("authorizationAttributes").EnumerateArray().Select(static value => value.GetString()));
                Assert.Contains("DbContext", item.GetProperty("dataAccess").EnumerateArray().Select(static value => value.GetString()));
                Assert.Contains("ConnectionStrings:Orders", item.GetProperty("configurationKeys").EnumerateArray().Select(static value => value.GetString()));
                Assert.Contains(evidenceStableKey.Value, item.GetProperty("evidenceStableKeys").EnumerateArray().Select(static value => value.GetString()));
                Assert.DoesNotContain("neo4j", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms controller or handler lookup returns detail and validation problems use safe machine-readable codes.
        /// </summary>
        /// <returns>A task that completes after controller detail and validation responses are asserted.</returns>
        [Fact]
        public async Task RuntimeControllerEndpoint_WhenControllerExistsOrIdentityMissing_ShouldReturnDetailOrValidationProblem()
        {
            // Controller detail lookup is separate from endpoint list output so clients can inspect persisted controller facts directly when available.
            StableKey repositoryStableKey = new("repository://runtime-controller-api");
            StableKey snapshotStableKey = new("snapshot://runtime-controller-api/current");
            StableKey projectStableKey = new("project://src/Runtime.Controller.Api/Runtime.Controller.Api.csproj");
            StableKey endpointStableKey = new("endpoint://runtime-controller-api/orders");
            StableKey controllerStableKey = new("controller://runtime-controller-api/OrdersController");
            StableKey evidenceStableKey = new("evidence://runtime-controller-api/orders-controller");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode project = CreateProjectNode(snapshotStableKey, projectStableKey, "Runtime.Controller.Api.csproj", CreateDetailedProjectMetadata("Api", "Api", "net10.0", true, "src/Runtime.Controller.Api/Runtime.Controller.Api.csproj"));
                ArchitectureNode endpoint = CreateRuntimeNode(snapshotStableKey, endpointStableKey, NodeKind.Endpoint, "GET /orders", "GET /orders", projectStableKey, CreateEndpointMetadata("GET", "/orders", "OrdersController", "ListOrders", null, "OrderResponse", "Authorize"), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode controller = CreateRuntimeNode(snapshotStableKey, controllerStableKey, NodeKind.Controller, "OrdersController", "Runtime.Controller.Api.Controllers.OrdersController", projectStableKey, CreateControllerMetadata(), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey: null, [project, endpoint, controller], [CreateEdge(snapshotStableKey, "edge://runtime-controller-api/controller-endpoint", EdgeKind.Exposes, controllerStableKey, endpointStableKey, evidenceStableKey.Value)], [CreateSymbolEvidence(snapshotStableKey, evidenceStableKey, "src/Runtime.Controller.Api/Controllers/OrdersController.cs", 4, 18, "OrdersController", "Runtime.Controller.Api", "public sealed class OrdersController")], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument detailBody = await GetJsonAsync(client, "/runtime/controllers?repositoryStableKey=repository%3A%2F%2Fruntime-controller-api&snapshotStableKey=latest&stableKey=controller%3A%2F%2Fruntime-controller-api%2FOrdersController");
            HttpResponseMessage invalidResponse = await client.GetAsync("/runtime/controllers?repositoryStableKey=repository%3A%2F%2Fruntime-controller-api&snapshotStableKey=latest");
            string invalidBody = await invalidResponse.Content.ReadAsStringAsync();

            using (detailBody)
            {
                Assert.Equal(controllerStableKey.Value, detailBody.RootElement.GetProperty("data").GetProperty("stableKey").GetString());
                JsonElement endpoint = Assert.Single(detailBody.RootElement.GetProperty("data").GetProperty("endpoints").EnumerateArray());
                Assert.Equal(endpointStableKey.Value, endpoint.GetProperty("stableKey").GetString());
                JsonElement evidence = Assert.Single(detailBody.RootElement.GetProperty("data").GetProperty("evidence").EnumerateArray());
                Assert.Equal(evidenceStableKey.Value, evidence.GetProperty("stableKey").GetString());
                Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
                Assert.Contains("ControllerOrHandlerIdentityRequired", invalidBody, StringComparison.Ordinal);
                Assert.DoesNotContain("Exception", invalidBody, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms runtime entry-point queries expose API and worker host styles with deterministic scope and filters.
        /// </summary>
        /// <returns>A task that completes after runtime entry-point response fields are asserted.</returns>
        [Fact]
        public async Task RuntimeEntryPointEndpoint_WhenRuntimeProjectsExist_ShouldReturnFilteredEntryPoints()
        {
            // Entry-point queries are project-backed in the current snapshot seam and use runtime kind filters rather than host-specific UI pages.
            StableKey repositoryStableKey = new("repository://runtime-entry-api");
            StableKey snapshotStableKey = new("snapshot://runtime-entry-api/current");
            StableKey apiProjectStableKey = new("project://src/Runtime.Entry.Api/Runtime.Entry.Api.csproj");
            StableKey workerProjectStableKey = new("project://src/Runtime.Entry.Worker/Runtime.Entry.Worker.csproj");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode apiProject = CreateProjectNode(snapshotStableKey, apiProjectStableKey, "Runtime.Entry.Api.csproj", CreateRuntimeProjectMetadata("Api", "Program.Main", "src/Runtime.Entry.Api/Runtime.Entry.Api.csproj"));
                ArchitectureNode workerProject = CreateProjectNode(snapshotStableKey, workerProjectStableKey, "Runtime.Entry.Worker.csproj", CreateRuntimeProjectMetadata("Worker", "Program.Main", "src/Runtime.Entry.Worker/Runtime.Entry.Worker.csproj"));
                ArchitectureNode endpoint = CreateRuntimeNode(snapshotStableKey, new StableKey("endpoint://runtime-entry-api/health"), NodeKind.Endpoint, "GET /health", "GET /health", apiProjectStableKey, CreateEndpointMetadata("GET", "/health", null, "Health", null, "HealthResponse", null), evidenceStableKey: null, Confidence.Certain, UnknownState.Known);
                ArchitectureNode hostedService = CreateRuntimeNode(snapshotStableKey, new StableKey("hostedservice://runtime-entry-worker/PollingWorker"), NodeKind.HostedService, "PollingWorker", "Runtime.Entry.Worker.PollingWorker", workerProjectStableKey, CreateHostedServiceMetadata("BackgroundService"), evidenceStableKey: null, Confidence.Certain, UnknownState.Known);
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey: null, [apiProject, workerProject, endpoint, hostedService], [], [], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument apiBody = await GetJsonAsync(client, "/runtime/entry-points?repositoryStableKey=repository%3A%2F%2Fruntime-entry-api&snapshotStableKey=latest&runtimeKind=Api&take=10");
            JsonDocument workerBody = await GetJsonAsync(client, "/runtime/entry-points?repositoryStableKey=repository%3A%2F%2Fruntime-entry-api&snapshotStableKey=latest&runtimeKind=Worker&projectStableKey=project%3A%2F%2Fsrc%2FRuntime.Entry.Worker%2FRuntime.Entry.Worker.csproj&take=10");

            using (apiBody)
            using (workerBody)
            {
                JsonElement apiItem = Assert.Single(apiBody.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("Api", apiItem.GetProperty("runtimeKind").GetString());
                Assert.Contains("endpoint://runtime-entry-api/health", apiItem.GetProperty("endpointStableKeys").EnumerateArray().Select(static value => value.GetString()));
                JsonElement workerItem = Assert.Single(workerBody.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("Worker", workerItem.GetProperty("runtimeKind").GetString());
                Assert.Contains("hostedservice://runtime-entry-worker/PollingWorker", workerItem.GetProperty("hostedServices").EnumerateArray().Select(static value => value.GetString()));
            }
        }

        /// <summary>
        /// Confirms worker queries expose hosted services, queue/topic consumers, scheduled jobs, data access, integrations, configuration, evidence, and partial-runtime unknowns.
        /// </summary>
        /// <returns>A task that completes after worker response fields are asserted.</returns>
        [Fact]
        public async Task RuntimeWorkersEndpoint_WhenWorkerFactsExist_ShouldReturnConsumersSchedulesEvidenceAndUnknowns()
        {
            // Worker lookup exercises non-HTTP runtime facts and validates that partial extraction remains visible through unknown metadata.
            StableKey repositoryStableKey = new("repository://runtime-worker-api");
            StableKey snapshotStableKey = new("snapshot://runtime-worker-api/current");
            StableKey projectStableKey = new("project://src/Runtime.Worker/Runtime.Worker.csproj");
            StableKey hostedServiceStableKey = new("hostedservice://runtime-worker/OrderWorker");
            StableKey queueStableKey = new("queue://orders-incoming");
            StableKey scheduledJobStableKey = new("job://runtime-worker/nightly-rebuild");
            StableKey dbContextStableKey = new("dbcontext://runtime-worker/orders");
            StableKey integrationStableKey = new("external://billing-service");
            StableKey configStableKey = new("config://runtime-worker/QueueName");
            StableKey evidenceStableKey = new("evidence://runtime-worker/order-worker");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode project = CreateProjectNode(snapshotStableKey, projectStableKey, "Runtime.Worker.csproj", CreateRuntimeProjectMetadata("Worker", "Program.Main", "src/Runtime.Worker/Runtime.Worker.csproj"));
                ArchitectureNode hostedService = CreateRuntimeNode(snapshotStableKey, hostedServiceStableKey, NodeKind.HostedService, "OrderWorker", "Runtime.Worker.OrderWorker", projectStableKey, CreateHostedServiceMetadata("BackgroundService"), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode queue = CreateRuntimeNode(snapshotStableKey, queueStableKey, NodeKind.Queue, "orders-incoming", "orders-incoming", projectStableKey, CreateQueueMetadata("AzureServiceBus"), evidenceStableKey, Confidence.Medium, UnknownState.Unknown("Queue name was inferred from configuration and may be incomplete."));
                ArchitectureNode scheduledJob = CreateRuntimeNode(snapshotStableKey, scheduledJobStableKey, NodeKind.Method, "NightlyRebuild", "Runtime.Worker.OrderWorker.NightlyRebuild", projectStableKey, CreateScheduledJobMetadata("0 0 * * *"), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode dbContext = CreateOwnedNode(snapshotStableKey, dbContextStableKey, NodeKind.DbContext, "OrdersDbContext", projectStableKey);
                ArchitectureNode integration = CreateOwnedNode(snapshotStableKey, integrationStableKey, NodeKind.ExternalService, "BillingService", projectStableKey);
                ArchitectureNode config = CreateOwnedNode(snapshotStableKey, configStableKey, NodeKind.ConfigurationKey, "Queues:Orders", projectStableKey);
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(
                    snapshotStableKey,
                    repositoryStableKey,
                    solutionStableKey: null,
                    [project, hostedService, queue, scheduledJob, dbContext, integration, config],
                    [
                        CreateEdge(snapshotStableKey, "edge://runtime-worker/handles-queue", EdgeKind.Handles, hostedServiceStableKey, queueStableKey, evidenceStableKey.Value),
                        CreateEdge(snapshotStableKey, "edge://runtime-worker/handles-schedule", EdgeKind.Handles, hostedServiceStableKey, scheduledJobStableKey, evidenceStableKey.Value),
                        CreateEdge(snapshotStableKey, "edge://runtime-worker/uses-db", EdgeKind.UsesDbContext, hostedServiceStableKey, dbContextStableKey, evidenceStableKey.Value),
                        CreateEdge(snapshotStableKey, "edge://runtime-worker/calls-billing", EdgeKind.CallsExternalService, hostedServiceStableKey, integrationStableKey, evidenceStableKey.Value),
                        CreateEdge(snapshotStableKey, "edge://runtime-worker/uses-config", EdgeKind.UsesConfig, hostedServiceStableKey, configStableKey, evidenceStableKey.Value)
                    ],
                    [CreateSymbolEvidence(snapshotStableKey, evidenceStableKey, "src/Runtime.Worker/OrderWorker.cs", 8, 42, "ExecuteAsync", "OrderWorker", "protected override Task ExecuteAsync(CancellationToken stoppingToken)")],
                    []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/runtime/workers?repositoryStableKey=repository%3A%2F%2Fruntime-worker-api&snapshotStableKey=latest&projectStableKey=project%3A%2F%2Fsrc%2FRuntime.Worker%2FRuntime.Worker.csproj&workerKind=BackgroundService&queueOrTopic=orders&scheduledJob=Nightly&take=10");

            using (body)
            {
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(hostedServiceStableKey.Value, item.GetProperty("stableKey").GetString());
                Assert.Equal("BackgroundService", item.GetProperty("workerKind").GetString());
                Assert.Contains(hostedServiceStableKey.Value, item.GetProperty("hostedServices").EnumerateArray().Select(static value => value.GetString()));
                JsonElement consumer = Assert.Single(item.GetProperty("queueConsumers").EnumerateArray());
                Assert.Equal(queueStableKey.Value, consumer.GetProperty("stableKey").GetString());
                Assert.Equal("Queue", consumer.GetProperty("kind").GetString());
                Assert.Equal("AzureServiceBus", consumer.GetProperty("transportKind").GetString());
                JsonElement scheduledJob = Assert.Single(item.GetProperty("scheduledJobs").EnumerateArray());
                Assert.Equal(scheduledJobStableKey.Value, scheduledJob.GetProperty("stableKey").GetString());
                Assert.Equal("0 0 * * *", scheduledJob.GetProperty("schedule").GetString());
                Assert.Contains("DbContext", item.GetProperty("dataAccess").EnumerateArray().Select(static value => value.GetString()));
                Assert.Contains("BillingService", item.GetProperty("integrations").EnumerateArray().Select(static value => value.GetString()));
                Assert.Contains("Queues:Orders", item.GetProperty("configurationKeys").EnumerateArray().Select(static value => value.GetString()));
                Assert.Equal(evidenceStableKey.Value, Assert.Single(item.GetProperty("evidence").EnumerateArray()).GetProperty("stableKey").GetString());
                Assert.True(item.GetProperty("hasUnknownData").GetBoolean());
                Assert.Contains(body.RootElement.GetProperty("unknowns").EnumerateArray(), unknown => unknown.GetProperty("field").GetString() == "queueConsumers");
                Assert.DoesNotContain("Neo4j", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms data-access fact queries expose EF, LINQ to SQL, ADO.NET, raw SQL, entity, table, operation, usage-site, evidence, confidence, and unknown metadata.
        /// </summary>
        /// <returns>A task that completes after data-access fact response fields are asserted.</returns>
        [Fact]
        public async Task FactQueryDataAccessEndpoint_WhenFactsExist_ShouldReturnFilteredDataAccessEnvelope()
        {
            // Data-access lookup proves WP014 can expose persistence technology facts without exposing graph-store internals or unsafe SQL text.
            StableKey repositoryStableKey = new("repository://fact-data-access-api");
            StableKey snapshotStableKey = new("snapshot://fact-data-access-api/current");
            StableKey projectStableKey = new("project://src/Facts.Data/Facts.Data.csproj");
            StableKey dbContextStableKey = new("dbcontext://facts-data/OrdersDbContext");
            StableKey tableStableKey = new("table://facts-data/dbo.Orders");
            StableKey storedProcedureStableKey = new("storedprocedure://facts-data/dbo.GetOrders");
            StableKey usageStableKey = new("method://facts-data/OrderRepository.LoadAsync");
            StableKey evidenceStableKey = new("evidence://facts-data/orders-dbcontext");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode project = CreateProjectNode(snapshotStableKey, projectStableKey, "Facts.Data.csproj", CreateDetailedProjectMetadata("Infrastructure", "Library", "net10.0", true, "src/Facts.Data/Facts.Data.csproj"));
                ArchitectureNode dbContext = CreateFactNode(snapshotStableKey, dbContextStableKey, NodeKind.DbContext, "OrdersDbContext", "Facts.Data.OrdersDbContext", projectStableKey, CreateDataAccessMetadata("EFCore", ["SubmitChanges", "ExecuteQuery", "ExecuteCommand"]), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode table = CreateFactNode(snapshotStableKey, tableStableKey, NodeKind.DatabaseTable, "dbo.Orders", "dbo.Orders", projectStableKey, CreateDataAccessMetadata("Table", []), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode storedProcedure = CreateFactNode(snapshotStableKey, storedProcedureStableKey, NodeKind.StoredProcedure, "dbo.GetOrders", "dbo.GetOrders", projectStableKey, CreateDataAccessMetadata("StoredProcedure", []), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode usageSite = CreateFactNode(snapshotStableKey, usageStableKey, NodeKind.Method, "LoadAsync", "Facts.Data.OrderRepository.LoadAsync", projectStableKey, GraphMetadata.Empty, evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode linqToSql = CreateFactNode(snapshotStableKey, new StableKey("linqtosql://facts-data/LegacyDataContext"), NodeKind.LinqToSqlDataContext, "LegacyDataContext", "Facts.Data.LegacyDataContext", projectStableKey, CreateDataAccessMetadata("LinqToSql", ["SubmitChanges"]), evidenceStableKey, Confidence.Medium, UnknownState.Unknown("LINQ to SQL mapping was inferred from generated designer metadata."));
                ArchitectureNode adoNet = CreateFactNode(snapshotStableKey, new StableKey("adonet://facts-data/SqlCommand"), NodeKind.SqlScript, "SELECT * FROM Orders", "Facts.Data.SqlCommand", projectStableKey, CreateDataAccessMetadata("AdoNet", ["ExecuteCommand"]), evidenceStableKey, Confidence.Medium, UnknownState.Known);
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(
                    snapshotStableKey,
                    repositoryStableKey,
                    solutionStableKey: null,
                    [project, dbContext, table, storedProcedure, usageSite, linqToSql, adoNet],
                    [
                        CreateEdge(snapshotStableKey, "edge://facts-data/dbcontext-table", EdgeKind.MapsTable, dbContextStableKey, tableStableKey, evidenceStableKey.Value),
                        CreateEdge(snapshotStableKey, "edge://facts-data/dbcontext-procedure", EdgeKind.CallsStoredProcedure, dbContextStableKey, storedProcedureStableKey, evidenceStableKey.Value),
                        CreateEdge(snapshotStableKey, "edge://facts-data/usage-dbcontext", EdgeKind.UsesDbContext, usageStableKey, dbContextStableKey, evidenceStableKey.Value)
                    ],
                    [CreateSymbolEvidence(snapshotStableKey, evidenceStableKey, "src/Facts.Data/OrdersDbContext.cs", 6, 24, "OrdersDbContext", "Facts.Data", "public sealed class OrdersDbContext : DbContext")],
                    []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/data-access?repositoryStableKey=repository%3A%2F%2Ffact-data-access-api&snapshotStableKey=latest&family=EFCore&projectStableKey=project%3A%2F%2Fsrc%2FFacts.Data%2FFacts.Data.csproj&usageSite=OrderRepository&table=Orders&storedProcedure=GetOrders&take=10");

            using (body)
            {
                Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(dbContextStableKey.Value, item.GetProperty("stableKey").GetString());
                Assert.Equal("EFCore", item.GetProperty("family").GetString());
                Assert.Equal(tableStableKey.Value, item.GetProperty("tableStableKey").GetString());
                Assert.Equal(storedProcedureStableKey.Value, item.GetProperty("storedProcedureStableKey").GetString());
                Assert.Contains(usageStableKey.Value, item.GetProperty("usageSites").EnumerateArray().Select(static value => value.GetString()));
                Assert.Contains("SubmitChanges", item.GetProperty("operations").EnumerateArray().Select(static value => value.GetString()));
                Assert.Contains("ExecuteQuery", item.GetProperty("operations").EnumerateArray().Select(static value => value.GetString()));
                Assert.Contains("ExecuteCommand", item.GetProperty("operations").EnumerateArray().Select(static value => value.GetString()));
                Assert.Contains(evidenceStableKey.Value, item.GetProperty("evidenceStableKeys").EnumerateArray().Select(static value => value.GetString()));
                Assert.Equal(1m, item.GetProperty("confidence").GetDecimal());
                Assert.False(item.GetProperty("hasUnknownData").GetBoolean());
                Assert.DoesNotContain("Neo4j", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms configuration fact queries filter safe metadata and never expose secret values or connection strings.
        /// </summary>
        /// <returns>A task that completes after configuration response fields and redaction behavior are asserted.</returns>
        [Fact]
        public async Task FactQueryConfigurationEndpoint_WhenSecretLikeKeyExists_ShouldReturnSafeMetadataOnly()
        {
            // Configuration lookup returns key names and value-availability flags while omitting the actual sensitive value from JSON.
            StableKey repositoryStableKey = new("repository://fact-configuration-api");
            StableKey snapshotStableKey = new("snapshot://fact-configuration-api/current");
            StableKey projectStableKey = new("project://src/Facts.Configuration/Facts.Configuration.csproj");
            StableKey configurationStableKey = new("config://facts-configuration/ConnectionStrings.Main");
            StableKey consumerStableKey = new("method://facts-configuration/Program.Configure");
            StableKey evidenceStableKey = new("evidence://facts-configuration/appsettings");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode project = CreateProjectNode(snapshotStableKey, projectStableKey, "Facts.Configuration.csproj", CreateDetailedProjectMetadata("Api", "Api", "net10.0", true, "src/Facts.Configuration/Facts.Configuration.csproj"));
                ArchitectureNode configuration = CreateFactNode(snapshotStableKey, configurationStableKey, NodeKind.ConfigurationKey, "ConnectionStrings:Main", "ConnectionStrings:Main", projectStableKey, CreateConfigurationMetadata("Json", "Production", "Server=tcp://secret.example;Password=ShouldNotAppear"), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode consumer = CreateFactNode(snapshotStableKey, consumerStableKey, NodeKind.Method, "Configure", "Facts.Configuration.Program.Configure", projectStableKey, GraphMetadata.Empty, evidenceStableKey, Confidence.Certain, UnknownState.Known);
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey: null, [project, configuration, consumer], [CreateEdge(snapshotStableKey, "edge://facts-configuration/consumer-config", EdgeKind.UsesConfig, consumerStableKey, configurationStableKey, evidenceStableKey.Value)], [CreateSymbolEvidence(snapshotStableKey, evidenceStableKey, "src/Facts.Configuration/appsettings.json", 1, 8, "ConnectionStrings:Main", "appsettings", "\"ConnectionStrings\": { \"Main\": \"<redacted>\" }")], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/configuration?repositoryStableKey=repository%3A%2F%2Ffact-configuration-api&snapshotStableKey=latest&configurationKey=ConnectionStrings&projectStableKey=project%3A%2F%2Fsrc%2FFacts.Configuration%2FFacts.Configuration.csproj&consumerStableKey=method%3A%2F%2Ffacts-configuration%2FProgram.Configure&provider=Json&environment=Production&sourceFile=appsettings&take=10");

            using (body)
            {
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(configurationStableKey.Value, item.GetProperty("stableKey").GetString());
                Assert.Equal("ConnectionStrings:Main", item.GetProperty("key").GetString());
                Assert.True(item.GetProperty("valueAvailable").GetBoolean());
                Assert.True(item.GetProperty("secretLike").GetBoolean());
                Assert.Contains(consumerStableKey.Value, item.GetProperty("consumerStableKeys").EnumerateArray().Select(static value => value.GetString()));
                Assert.Contains("Json", item.GetProperty("providers").EnumerateArray().Select(static value => value.GetString()));
                Assert.Contains("src/Facts.Configuration/appsettings.json", item.GetProperty("sourceFiles").EnumerateArray().Select(static value => value.GetString()));
                string json = body.RootElement.ToString();
                Assert.DoesNotContain("ShouldNotAppear", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Server=tcp", json, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms integration fact queries expose safe hosts, protocols, clients, configuration keys, and omit credentials or URL secrets.
        /// </summary>
        /// <returns>A task that completes after integration response fields and redaction behavior are asserted.</returns>
        [Fact]
        public async Task FactQueryIntegrationEndpoint_WhenIntegrationExists_ShouldReturnSecretSafeTargetMetadata()
        {
            // Integration lookup strips path and credential details while retaining the safe host/service identity needed by consumers.
            StableKey repositoryStableKey = new("repository://fact-integration-api");
            StableKey snapshotStableKey = new("snapshot://fact-integration-api/current");
            StableKey projectStableKey = new("project://src/Facts.Integration/Facts.Integration.csproj");
            StableKey integrationStableKey = new("external://billing.example.invalid");
            StableKey configurationStableKey = new("config://facts-integration/Billing.Endpoint");
            StableKey consumerStableKey = new("method://facts-integration/BillingClient.SendAsync");
            StableKey evidenceStableKey = new("evidence://facts-integration/billing-client");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode project = CreateProjectNode(snapshotStableKey, projectStableKey, "Facts.Integration.csproj", CreateDetailedProjectMetadata("Infrastructure", "Library", "net10.0", true, "src/Facts.Integration/Facts.Integration.csproj"));
                ArchitectureNode integration = CreateFactNode(snapshotStableKey, integrationStableKey, NodeKind.ExternalService, "BillingService", "BillingService", projectStableKey, CreateIntegrationMetadata("HTTP", "https://user:ShouldNotAppear@billing.example.invalid/api/orders?token=hidden", "HttpClient"), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode configuration = CreateFactNode(snapshotStableKey, configurationStableKey, NodeKind.ConfigurationKey, "Billing:Endpoint", "Billing:Endpoint", projectStableKey, CreateConfigurationMetadata("Json", "Production", "https://billing.example.invalid"), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode consumer = CreateFactNode(snapshotStableKey, consumerStableKey, NodeKind.Method, "SendAsync", "Facts.Integration.BillingClient.SendAsync", projectStableKey, GraphMetadata.Empty, evidenceStableKey, Confidence.Certain, UnknownState.Known);
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey: null, [project, integration, configuration, consumer], [CreateEdge(snapshotStableKey, "edge://facts-integration/consumer-service", EdgeKind.CallsExternalService, consumerStableKey, integrationStableKey, evidenceStableKey.Value), CreateEdge(snapshotStableKey, "edge://facts-integration/service-config", EdgeKind.UsesConfig, integrationStableKey, configurationStableKey, evidenceStableKey.Value)], [CreateSymbolEvidence(snapshotStableKey, evidenceStableKey, "src/Facts.Integration/BillingClient.cs", 10, 30, "SendAsync", "BillingClient", "await httpClient.SendAsync(request)")], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/integrations?repositoryStableKey=repository%3A%2F%2Ffact-integration-api&snapshotStableKey=latest&projectStableKey=project%3A%2F%2Fsrc%2FFacts.Integration%2FFacts.Integration.csproj&integrationKind=HTTP&endpointHost=billing.example.invalid&protocol=HTTP&clientType=HttpClient&configurationKey=Billing&take=10");

            using (body)
            {
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(integrationStableKey.Value, item.GetProperty("stableKey").GetString());
                Assert.Equal("BillingService", item.GetProperty("name").GetString());
                Assert.Equal("billing.example.invalid", item.GetProperty("endpointHost").GetString());
                Assert.Equal("HTTP", item.GetProperty("protocol").GetString());
                Assert.Equal("HttpClient", item.GetProperty("clientType").GetString());
                Assert.Contains("Billing:Endpoint", item.GetProperty("configurationKeys").EnumerateArray().Select(static value => value.GetString()));
                Assert.Contains(consumerStableKey.Value, item.GetProperty("consumerStableKeys").EnumerateArray().Select(static value => value.GetString()));
                string json = body.RootElement.ToString();
                Assert.DoesNotContain("ShouldNotAppear", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("token=hidden", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("user:", json, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms UI-technology fact queries expose backend UI graph facts without creating Discovery UI behavior.
        /// </summary>
        /// <returns>A task that completes after UI-technology response fields are asserted.</returns>
        [Fact]
        public async Task FactQueryUiTechnologiesEndpoint_WhenUiFactsExist_ShouldReturnBackendFactDataOnly()
        {
            // UI-technology lookup exposes extracted Blazor/Razor/etc. facts as query data and does not depend on frontend assets.
            StableKey repositoryStableKey = new("repository://fact-ui-api");
            StableKey snapshotStableKey = new("snapshot://fact-ui-api/current");
            StableKey projectStableKey = new("project://src/Facts.Blazor/Facts.Blazor.csproj");
            StableKey componentStableKey = new("uicomponent://facts-blazor/Pages/Orders.razor");
            StableKey routeStableKey = new("uiroute://facts-blazor/orders");
            StableKey evidenceStableKey = new("evidence://facts-ui/orders-component");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                ArchitectureNode project = CreateProjectNode(snapshotStableKey, projectStableKey, "Facts.Blazor.csproj", CreateUiProjectMetadata("Blazor", "src/Facts.Blazor/Facts.Blazor.csproj"));
                ArchitectureNode component = CreateFactNode(snapshotStableKey, componentStableKey, NodeKind.UiComponent, "Orders", "Facts.Blazor.Pages.Orders", projectStableKey, CreateUiFactMetadata("Blazor", "/orders"), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                ArchitectureNode route = CreateFactNode(snapshotStableKey, routeStableKey, NodeKind.UiRoute, "/orders", "/orders", projectStableKey, CreateUiFactMetadata("Blazor", "/orders"), evidenceStableKey, Confidence.Certain, UnknownState.Known);
                await writer.WriteSnapshotAsync(CreateProjectSnapshot(snapshotStableKey, repositoryStableKey, solutionStableKey: null, [project, component, route], [CreateEdge(snapshotStableKey, "edge://facts-ui/component-route", EdgeKind.DeclaresUiRoute, componentStableKey, routeStableKey, evidenceStableKey.Value)], [CreateSymbolEvidence(snapshotStableKey, evidenceStableKey, "src/Facts.Blazor/Pages/Orders.razor", 1, 12, "Orders", "Facts.Blazor.Pages", "@page \"/orders\"")], []), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/ui-technologies?repositoryStableKey=repository%3A%2F%2Ffact-ui-api&snapshotStableKey=latest&technology=Blazor&projectStableKey=project%3A%2F%2Fsrc%2FFacts.Blazor%2FFacts.Blazor.csproj&route=orders&component=Orders&take=10");

            using (body)
            {
                JsonElement[] items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
                Assert.Equal(2, items.Length);
                JsonElement item = items.Single(item => item.GetProperty("stableKey").GetString() == componentStableKey.Value);
                Assert.Equal("Blazor", item.GetProperty("technology").GetString());
                Assert.Equal("UiComponent", item.GetProperty("factKind").GetString());
                Assert.Equal("/orders", item.GetProperty("route").GetString());
                Assert.Contains(routeStableKey.Value, item.GetProperty("relatedStableKeys").EnumerateArray().Select(static value => value.GetString()));
                Assert.Contains(evidenceStableKey.Value, item.GetProperty("evidenceStableKeys").EnumerateArray().Select(static value => value.GetString()));
                Assert.False(item.GetProperty("hasUnknownData").GetBoolean());
                Assert.Contains(items, candidate => candidate.GetProperty("stableKey").GetString() == routeStableKey.Value && candidate.GetProperty("factKind").GetString() == "UiRoute");
                Assert.DoesNotContain("Discovery", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms WP014 fact-query endpoints return deterministic validation problems for invalid paging and unsupported family filters.
        /// </summary>
        /// <returns>A task that completes after fact-query validation responses are asserted.</returns>
        [Fact]
        public async Task FactQueryEndpoints_WhenRequestIsInvalid_ShouldReturnValidationProblems()
        {
            // Invalid fact-query options fail before snapshot lookup so clients receive stable client-correctable problem details.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            (string Uri, string ExpectedKey)[] requests =
            [
                ("/data-access?repositoryStableKey=repository%3A%2F%2Fmissing&family=Unsupported&take=10", "FactFamilyUnsupported"),
                ("/configuration?repositoryStableKey=repository%3A%2F%2Fmissing&skip=-1", "SkipInvalid"),
                ("/integrations?repositoryStableKey=repository%3A%2F%2Fmissing&take=0", "TakeInvalid"),
                ("/ui-technologies?snapshotStableKey=latest", "RepositoryStableKeyRequired")
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
        /// Creates a deterministic finding fixture with Work Item 8 analysis-output filter metadata.
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
        /// <returns>A validated finding record fixture with safe analysis filter metadata.</returns>
        private static FindingRecord CreateFindingWithAnalysisMetadata(string snapshotStableKey, string stableKey, string historyKey, string ruleCode, string ruleVersion, FindingSeverity severity, FindingStatus status, string nodeStableKey, string evidenceStableKey)
        {
            // The metadata mirrors persisted analysis-output indicators so the HTTP hotlist filter contract is exercised without arbitrary metadata predicates.
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
                    ["legacyDataAccess"] = true,
                    ["outOfSupport"] = true,
                    ["securitySensitive"] = true,
                    ["frameworkOnly"] = true,
                    ["technology"] = "LINQ to SQL",
                    ["technologyFamily"] = "LegacyDataAccess"
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
        private static Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot CreateDiffSnapshot(StableKey snapshotStableKey, StableKey repositoryStableKey, IReadOnlyList<ArchitectureNode> nodes, IReadOnlyList<ArchitectureEdge> edges, IReadOnlyList<FindingRecord> findings, IReadOnlyList<MetricRecord> metrics, DateTimeOffset? completedUtc = null)
        {
            // Diff endpoint tests compare complete persisted snapshots without needing source extraction or Neo4j.
            SnapshotHeader header = new(
                snapshotStableKey,
                repositoryStableKey,
                "main",
                "abcdef",
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                completedUtc ?? new DateTimeOffset(2026, 5, 20, 8, 1, 0, TimeSpan.Zero),
                "wp013-diff-api-tests",
                "Completed",
                warnings: [],
                errors: [],
                GraphMetadata.Empty);
            RepositoryModel repository = new(repositoryStableKey, "SnapshotDiffApi", "D:/Repositories/SnapshotDiffApi", null, "main", GraphMetadata.Empty);
            return new Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot(header, [repository], [], nodes, edges, [], [], findings, metrics, [], [], []);
        }

        /// <summary>
        /// Creates a deterministic extracted snapshot containing dashboard summary inputs.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the dashboard snapshot.</param>
        /// <param name="repositoryStableKey">The repository stable key used for scope resolution.</param>
        /// <param name="solutionStableKey">The optional solution stable key used for scope resolution.</param>
        /// <param name="nodes">The architecture nodes available to dashboard counts.</param>
        /// <param name="metrics">The metric records available to hotspot enrichment.</param>
        /// <param name="findings">The finding records available to hotlist counts and hotspot enrichment.</param>
        /// <returns>An extracted architecture snapshot containing dashboard summary inputs.</returns>
        private static Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot CreateDashboardSnapshot(StableKey snapshotStableKey, StableKey repositoryStableKey, StableKey? solutionStableKey, IReadOnlyList<ArchitectureNode> nodes, IReadOnlyList<MetricRecord> metrics, IReadOnlyList<FindingRecord> findings)
        {
            // Dashboard endpoint tests need repository, optional solution, nodes, metrics, and findings without launching extraction.
            SnapshotHeader header = new(
                snapshotStableKey,
                repositoryStableKey,
                "main",
                "abcdef",
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                snapshotStableKey.Value.Contains("05-21", StringComparison.Ordinal) || snapshotStableKey.Value.Contains("current", StringComparison.Ordinal)
                    ? new DateTimeOffset(2026, 5, 21, 8, 1, 0, TimeSpan.Zero)
                    : new DateTimeOffset(2026, 5, 20, 8, 1, 0, TimeSpan.Zero),
                "wp014-dashboard-api-tests",
                "Completed",
                warnings: [],
                errors: [],
                GraphMetadata.Empty);
            RepositoryModel repository = new(repositoryStableKey, "DashboardApi", "D:/Repositories/DashboardApi", null, "main", GraphMetadata.Empty);
            SolutionModel[] solutions = solutionStableKey is null
                ? []
                : [new SolutionModel(repositoryStableKey, solutionStableKey.Value, "DashboardApi.slnx", RepositoryRelativePath.Parse("DashboardApi.slnx"), GraphMetadata.Empty)];
            return new Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot(header, [repository], solutions, nodes, [], [], [], findings, metrics, [], [], []);
        }

        /// <summary>
        /// Creates a deterministic extracted snapshot containing project query inputs.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the project query snapshot.</param>
        /// <param name="repositoryStableKey">The repository stable key used for scope resolution.</param>
        /// <param name="solutionStableKey">The optional solution stable key used for scope resolution.</param>
        /// <param name="nodes">The architecture nodes available to project queries.</param>
        /// <param name="edges">The architecture edges available to project queries.</param>
        /// <param name="evidence">The evidence records available to project detail responses.</param>
        /// <param name="findings">The findings available to project risk indicators.</param>
        /// <returns>An extracted architecture snapshot containing project query inputs.</returns>
        private static Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot CreateProjectSnapshot(StableKey snapshotStableKey, StableKey repositoryStableKey, StableKey? solutionStableKey, IReadOnlyList<ArchitectureNode> nodes, IReadOnlyList<ArchitectureEdge> edges, IReadOnlyList<EvidenceRecord> evidence, IReadOnlyList<FindingRecord> findings)
        {
            // Project endpoint tests need repository, optional solution, nodes, edges, evidence, and findings without launching extraction.
            SnapshotHeader header = new(
                snapshotStableKey,
                repositoryStableKey,
                "main",
                "abcdef",
                new DateTimeOffset(2026, 5, 22, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 22, 8, 1, 0, TimeSpan.Zero),
                "wp014-project-api-tests",
                "Completed",
                warnings: [],
                errors: [],
                GraphMetadata.Empty);
            RepositoryModel repository = new(repositoryStableKey, "ProjectQueryApi", "D:/Repositories/ProjectQueryApi", null, "main", GraphMetadata.Empty);
            SolutionModel[] solutions = solutionStableKey is null
                ? []
                : [new SolutionModel(repositoryStableKey, solutionStableKey.Value, "ProjectQueryApi.slnx", RepositoryRelativePath.Parse("ProjectQueryApi.slnx"), GraphMetadata.Empty)];
            return new Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot(header, [repository], solutions, nodes, edges, evidence, [], findings, [], [], [], []);
        }

        /// <summary>
        /// Creates a deterministic extracted snapshot containing evidence drill-down inputs.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the evidence query snapshot.</param>
        /// <param name="repositoryStableKey">The repository stable key used for scope resolution.</param>
        /// <param name="nodes">The architecture nodes available to evidence relationship lookup.</param>
        /// <param name="edges">The architecture edges available to evidence relationship lookup.</param>
        /// <param name="evidence">The evidence records available to evidence detail responses.</param>
        /// <param name="findings">The findings available to evidence context projection.</param>
        /// <param name="metrics">The metrics available to evidence relationship lookup.</param>
        /// <param name="rules">The rule definitions available to evidence rule context projection.</param>
        /// <returns>An extracted architecture snapshot containing evidence query inputs.</returns>
        private static Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot CreateEvidenceSnapshot(StableKey snapshotStableKey, StableKey repositoryStableKey, IReadOnlyList<ArchitectureNode> nodes, IReadOnlyList<ArchitectureEdge> edges, IReadOnlyList<EvidenceRecord> evidence, IReadOnlyList<FindingRecord> findings, IReadOnlyList<MetricRecord> metrics, IReadOnlyList<RuleDefinition> rules)
        {
            // Evidence endpoint tests use the same in-memory snapshot writer as prior WP014 slices while adding evidence and rule context.
            SnapshotHeader header = new(
                snapshotStableKey,
                repositoryStableKey,
                "main",
                "abcdef",
                new DateTimeOffset(2026, 5, 24, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 24, 8, 1, 0, TimeSpan.Zero),
                "wp014-evidence-api-tests",
                "Completed",
                warnings: [],
                errors: [],
                GraphMetadata.Empty);
            RepositoryModel repository = new(repositoryStableKey, "EvidenceQueryApi", "D:/Repositories/EvidenceQueryApi", null, "main", GraphMetadata.Empty);
            return new Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot(header, [repository], [], nodes, edges, evidence, rules, findings, metrics, [], [], []);
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
        /// Creates a deterministic endpoint node for dashboard summary endpoint fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the endpoint node.</param>
        /// <param name="endpointStableKey">The stable key that identifies the endpoint node.</param>
        /// <param name="projectStableKey">The stable key of the owning project.</param>
        /// <returns>An architecture node suitable for dashboard endpoint count fixtures.</returns>
        private static ArchitectureNode CreateEndpointNode(StableKey snapshotStableKey, StableKey endpointStableKey, StableKey projectStableKey)
        {
            // Endpoint nodes use the normalized Endpoint kind so dashboard counts do not need route-specific metadata.
            return new ArchitectureNode(
                snapshotStableKey,
                endpointStableKey,
                NodeKind.Endpoint,
                "GET /weather",
                "GET /weather",
                "get /weather",
                "C#",
                projectStableKey,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey: null,
                GraphMetadata.Empty,
                new Fingerprint("sha256:endpoint-weather"));
        }

        /// <summary>
        /// Creates a deterministic non-project node owned by a project for project detail fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the node.</param>
        /// <param name="nodeStableKey">The stable key that identifies the owned node.</param>
        /// <param name="nodeKind">The controlled node kind to assign.</param>
        /// <param name="displayName">The display name to expose for the node.</param>
        /// <param name="projectStableKey">The stable key of the owning project.</param>
        /// <returns>An architecture node suitable for project detail fixtures.</returns>
        private static ArchitectureNode CreateOwnedNode(StableKey snapshotStableKey, StableKey nodeStableKey, NodeKind nodeKind, string displayName, StableKey projectStableKey)
        {
            // Owned nodes allow project detail tests to verify endpoints, configuration keys, and data-access indicators without source parsing.
            return new ArchitectureNode(
                snapshotStableKey,
                nodeStableKey,
                nodeKind,
                displayName,
                displayName,
                displayName.ToLowerInvariant(),
                "C#",
                projectStableKey,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey: null,
                GraphMetadata.Empty,
                FingerprintGenerator.ForNode(nodeKind, displayName, displayName, displayName.ToLowerInvariant(), KnowledgeKind.Fact, GraphMetadata.Empty));
        }

        /// <summary>
        /// Creates a deterministic semantic symbol node for WP014 symbol endpoint fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the symbol.</param>
        /// <param name="nodeStableKey">The stable key that identifies the symbol node.</param>
        /// <param name="nodeKind">The controlled semantic symbol node kind.</param>
        /// <param name="displayName">The display name to expose for the symbol.</param>
        /// <param name="qualifiedName">The fully qualified symbol name.</param>
        /// <param name="projectStableKey">The stable key of the owning project.</param>
        /// <param name="parentStableKey">The optional parent symbol stable key.</param>
        /// <param name="metadata">The deterministic symbol metadata.</param>
        /// <param name="evidenceStableKey">The optional primary evidence stable key for the symbol.</param>
        /// <param name="confidence">The confidence assigned to the symbol fact.</param>
        /// <param name="unknownState">The unknown-state metadata assigned to the symbol fact.</param>
        /// <returns>An architecture node suitable for symbol endpoint fixtures.</returns>
        private static ArchitectureNode CreateSymbolNode(StableKey snapshotStableKey, StableKey nodeStableKey, NodeKind nodeKind, string displayName, string qualifiedName, StableKey projectStableKey, StableKey? parentStableKey, GraphMetadata metadata, StableKey? evidenceStableKey, Confidence confidence, UnknownState unknownState)
        {
            // Symbol fixtures mirror Roslyn semantic facts while allowing tests to model unresolved data and source evidence explicitly.
            return new ArchitectureNode(
                snapshotStableKey,
                nodeStableKey,
                nodeKind,
                displayName,
                qualifiedName,
                qualifiedName.ToLowerInvariant(),
                "C#",
                projectStableKey,
                parentStableKey,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                confidence,
                unknownState,
                evidenceStableKey,
                metadata,
                FingerprintGenerator.ForNode(nodeKind, displayName, qualifiedName, qualifiedName.ToLowerInvariant(), KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a deterministic package node for project catalogue fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the package node.</param>
        /// <param name="packageStableKey">The stable key that identifies the package node.</param>
        /// <param name="displayName">The package display name.</param>
        /// <returns>An architecture node suitable for package count fixtures.</returns>
        private static ArchitectureNode CreatePackageNode(StableKey snapshotStableKey, StableKey packageStableKey, string displayName)
        {
            // Package nodes are target nodes for USES_PACKAGE edges in project catalogue aggregate counts.
            return new ArchitectureNode(
                snapshotStableKey,
                packageStableKey,
                NodeKind.Package,
                displayName,
                displayName,
                displayName.ToLowerInvariant(),
                null,
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
        /// Creates deterministic project metadata for dashboard summary classification counts.
        /// </summary>
        /// <param name="applicationType">The application type classification to expose through metadata.</param>
        /// <returns>Graph metadata containing dashboard-relevant project classification data.</returns>
        private static GraphMetadata CreateProjectMetadata(string applicationType)
        {
            // The application.type metadata key mirrors current extraction metadata used by application classification.
            return GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["architecture.layer"] = "Project",
                ["application.type"] = applicationType
            });
        }

        /// <summary>
        /// Creates deterministic project metadata for project query classification and filtering tests.
        /// </summary>
        /// <param name="projectType">The project type classification.</param>
        /// <param name="applicationType">The application type classification.</param>
        /// <param name="targetFramework">The target framework moniker.</param>
        /// <param name="isSdkStyle">Indicates whether the project uses SDK-style project format.</param>
        /// <param name="path">The repository-relative project path.</param>
        /// <returns>Graph metadata containing project-query-relevant classification data.</returns>
        private static GraphMetadata CreateDetailedProjectMetadata(string projectType, string applicationType, string targetFramework, bool isSdkStyle, string path)
        {
            // Metadata keys intentionally use safe lower-camel names so public query responses can expose sanitized supplemental metadata.
            return GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["architecture.layer"] = projectType,
                ["application.type"] = applicationType,
                ["project.type"] = projectType,
                ["targetFramework"] = targetFramework,
                ["isSdkStyle"] = isSdkStyle,
                ["path"] = path,
                ["secretToken"] = "ShouldNotAppear"
            });
        }

        /// <summary>
        /// Creates deterministic metadata for semantic symbol query fixtures.
        /// </summary>
        /// <param name="namespaceName">The namespace associated with the symbol.</param>
        /// <param name="containingType">The optional containing type associated with the symbol.</param>
        /// <returns>Graph metadata containing symbol-query-relevant semantic classification data.</returns>
        private static GraphMetadata CreateSymbolMetadata(string namespaceName, string? containingType)
        {
            // Symbol metadata uses fixed safe property names so query filters can avoid parsing every qualified name.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["namespace"] = namespaceName
            };
            if (!string.IsNullOrWhiteSpace(containingType))
            {
                values["containingType"] = containingType;
            }

            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Creates deterministic endpoint metadata for runtime endpoint query fixtures.
        /// </summary>
        /// <param name="httpMethod">The HTTP method to expose through endpoint metadata.</param>
        /// <param name="route">The route template to expose through endpoint metadata.</param>
        /// <param name="controller">The optional controller name.</param>
        /// <param name="action">The optional action or handler name.</param>
        /// <param name="requestDto">The optional request DTO name.</param>
        /// <param name="responseDto">The optional response DTO name.</param>
        /// <param name="authorization">The optional authorization attribute name.</param>
        /// <returns>Graph metadata containing runtime endpoint classification data.</returns>
        private static GraphMetadata CreateEndpointMetadata(string httpMethod, string route, string? controller, string? action, string? requestDto, string? responseDto, string? authorization)
        {
            // Metadata keys mirror runtime extractor output so endpoint tests exercise query mapping rather than custom test-only shapes.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["httpMethod"] = httpMethod,
                ["route"] = route
            };
            AddMetadataIfPresent(values, "controllerName", controller);
            AddMetadataIfPresent(values, "actionName", action);
            AddMetadataIfPresent(values, "requestDto", requestDto);
            AddMetadataIfPresent(values, "responseDto", responseDto);
            AddMetadataIfPresent(values, "authorizationAttributes", authorization);
            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Creates deterministic metadata for controller detail query fixtures.
        /// </summary>
        /// <returns>Graph metadata containing controller runtime-role data.</returns>
        private static GraphMetadata CreateControllerMetadata()
        {
            // Controller metadata marks the node as handler-capable while keeping supplemental data safe for public response sanitation.
            return GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["runtimeRole"] = "Handler",
                ["safeContext"] = "controller"
            });
        }

        /// <summary>
        /// Creates deterministic project metadata for runtime entry-point query fixtures.
        /// </summary>
        /// <param name="applicationType">The runtime application type classification.</param>
        /// <param name="entryPoint">The entry method or bootstrap artifact.</param>
        /// <param name="path">The repository-relative project path.</param>
        /// <returns>Graph metadata containing runtime project classification data.</returns>
        private static GraphMetadata CreateRuntimeProjectMetadata(string applicationType, string entryPoint, string path)
        {
            // Runtime project metadata combines existing project-query fields with entry-point-specific classification.
            return GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["architecture.layer"] = applicationType,
                ["application.type"] = applicationType,
                ["project.type"] = applicationType,
                ["targetFramework"] = "net10.0",
                ["isSdkStyle"] = true,
                ["path"] = path,
                ["entryPoint"] = entryPoint
            });
        }

        /// <summary>
        /// Creates deterministic hosted-service metadata for worker query fixtures.
        /// </summary>
        /// <param name="runtimeKind">The hosted-service runtime kind.</param>
        /// <returns>Graph metadata containing worker classification data.</returns>
        private static GraphMetadata CreateHostedServiceMetadata(string runtimeKind)
        {
            // Hosted-service metadata distinguishes generic hosted services from BackgroundService-derived workers.
            return GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["runtimeKind"] = runtimeKind,
                ["baseType"] = runtimeKind
            });
        }

        /// <summary>
        /// Creates deterministic queue metadata for worker query fixtures.
        /// </summary>
        /// <param name="transportKind">The messaging transport hint.</param>
        /// <returns>Graph metadata containing queue-consumer classification data.</returns>
        private static GraphMetadata CreateQueueMetadata(string transportKind)
        {
            // Queue metadata exposes transport and detection hints without carrying connection strings or credentials.
            return GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["transportKind"] = transportKind,
                ["detectionMode"] = "TestFixture"
            });
        }

        /// <summary>
        /// Creates deterministic scheduled-job metadata for worker query fixtures.
        /// </summary>
        /// <param name="schedule">The safe schedule expression or description.</param>
        /// <returns>Graph metadata containing scheduled-job classification data.</returns>
        private static GraphMetadata CreateScheduledJobMetadata(string schedule)
        {
            // Scheduled-job metadata is a safe description of timer-driven behavior rather than executable scheduler configuration.
            return GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["runtimeKind"] = "ScheduledJob",
                ["schedule"] = schedule
            });
        }

        /// <summary>
        /// Creates deterministic metadata for data-access fact query fixtures.
        /// </summary>
        /// <param name="family">The data-access family classification.</param>
        /// <param name="operations">The safe operation names associated with the fact.</param>
        /// <returns>Graph metadata containing data-access classification data.</returns>
        private static GraphMetadata CreateDataAccessMetadata(string family, IReadOnlyList<string> operations)
        {
            // Metadata mirrors extractor-owned family and operation hints while including a secret-like value to prove public sanitation.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["dataAccessFamily"] = family,
                ["safeContext"] = "visible",
                ["secretToken"] = "ShouldNotAppear"
            };
            if (operations.Count > 0)
            {
                values["operations"] = operations.ToArray();
            }

            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Creates deterministic metadata for configuration usage fixtures.
        /// </summary>
        /// <param name="provider">The configuration provider classification.</param>
        /// <param name="environment">The environment associated with the key.</param>
        /// <param name="value">The sensitive value that should never appear in public responses.</param>
        /// <returns>Graph metadata containing configuration classification data.</returns>
        private static GraphMetadata CreateConfigurationMetadata(string provider, string environment, string value)
        {
            // The raw value is present only to verify that the query layer reports value availability without serializing the value itself.
            return GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["provider"] = provider,
                ["environment"] = environment,
                ["value"] = value,
                ["safeContext"] = "visible",
                ["secretToken"] = "ShouldNotAppear"
            });
        }

        /// <summary>
        /// Creates deterministic metadata for external integration fixtures.
        /// </summary>
        /// <param name="protocol">The integration protocol classification.</param>
        /// <param name="url">The unsafe URL that should be reduced to a safe host.</param>
        /// <param name="clientType">The integration client type.</param>
        /// <returns>Graph metadata containing integration classification data.</returns>
        private static GraphMetadata CreateIntegrationMetadata(string protocol, string url, string clientType)
        {
            // The endpoint URL intentionally contains credentials and query text so tests can verify safe host reduction.
            return GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["integrationKind"] = protocol,
                ["url"] = url,
                ["protocol"] = protocol,
                ["clientType"] = clientType,
                ["safeContext"] = "visible",
                ["secretToken"] = "ShouldNotAppear"
            });
        }

        /// <summary>
        /// Creates deterministic project metadata for UI-technology fixtures.
        /// </summary>
        /// <param name="technology">The UI technology associated with the project.</param>
        /// <param name="path">The repository-relative project path.</param>
        /// <returns>Graph metadata containing UI project classification data.</returns>
        private static GraphMetadata CreateUiProjectMetadata(string technology, string path)
        {
            // UI project metadata uses safe public keys so API responses can explain backend UI facts without adding frontend assets.
            return GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["architecture.layer"] = "UI",
                ["application.type"] = technology,
                ["project.type"] = technology,
                ["targetFramework"] = "net10.0",
                ["isSdkStyle"] = true,
                ["path"] = path,
                ["uiTechnology"] = technology
            });
        }

        /// <summary>
        /// Creates deterministic metadata for UI-technology fact fixtures.
        /// </summary>
        /// <param name="technology">The UI technology associated with the fact.</param>
        /// <param name="route">The route or view path associated with the fact.</param>
        /// <returns>Graph metadata containing UI fact classification data.</returns>
        private static GraphMetadata CreateUiFactMetadata(string technology, string route)
        {
            // UI fact metadata keeps route and technology explicit for fixed query filters.
            return GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["uiTechnology"] = technology,
                ["route"] = route,
                ["safeContext"] = "visible"
            });
        }

        /// <summary>
        /// Adds optional metadata to a mutable metadata dictionary when a value is available.
        /// </summary>
        /// <param name="values">The mutable metadata dictionary.</param>
        /// <param name="key">The metadata key to add.</param>
        /// <param name="value">The optional metadata value.</param>
        private static void AddMetadataIfPresent(Dictionary<string, object?> values, string key, string? value)
        {
            // Optional metadata should be omitted rather than serialized as blank values that filters might misinterpret.
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value;
            }
        }

        /// <summary>
        /// Creates a deterministic runtime node for endpoint, controller, worker, queue, topic, and scheduled-job query fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the node.</param>
        /// <param name="nodeStableKey">The stable key that identifies the runtime node.</param>
        /// <param name="nodeKind">The controlled runtime node kind.</param>
        /// <param name="displayName">The display name to expose for the runtime node.</param>
        /// <param name="qualifiedName">The qualified name or logical runtime identity.</param>
        /// <param name="projectStableKey">The stable key of the owning project.</param>
        /// <param name="metadata">The deterministic runtime metadata.</param>
        /// <param name="evidenceStableKey">The optional primary evidence stable key for the runtime node.</param>
        /// <param name="confidence">The confidence assigned to the runtime fact.</param>
        /// <param name="unknownState">The unknown-state metadata assigned to the runtime fact.</param>
        /// <returns>An architecture node suitable for runtime endpoint fixtures.</returns>
        private static ArchitectureNode CreateRuntimeNode(StableKey snapshotStableKey, StableKey nodeStableKey, NodeKind nodeKind, string displayName, string qualifiedName, StableKey projectStableKey, GraphMetadata metadata, StableKey? evidenceStableKey, Confidence confidence, UnknownState unknownState)
        {
            // Runtime fixtures mirror extracted graph facts while letting tests model evidence and incomplete runtime discovery explicitly.
            return new ArchitectureNode(
                snapshotStableKey,
                nodeStableKey,
                nodeKind,
                displayName,
                qualifiedName,
                qualifiedName.ToLowerInvariant(),
                "C#",
                projectStableKey,
                projectStableKey,
                unknownState.HasUnknownData ? KnowledgeKind.Unknown : KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                confidence,
                unknownState,
                evidenceStableKey,
                metadata,
                FingerprintGenerator.ForNode(nodeKind, displayName, qualifiedName, qualifiedName.ToLowerInvariant(), unknownState.HasUnknownData ? KnowledgeKind.Unknown : KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a deterministic fact node for data-access, configuration, integration, and UI-technology query fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the node.</param>
        /// <param name="nodeStableKey">The stable key that identifies the fact node.</param>
        /// <param name="nodeKind">The controlled graph node kind.</param>
        /// <param name="displayName">The display name to expose for the fact.</param>
        /// <param name="qualifiedName">The qualified name or logical fact identity.</param>
        /// <param name="projectStableKey">The stable key of the owning project.</param>
        /// <param name="metadata">The deterministic fact metadata.</param>
        /// <param name="evidenceStableKey">The optional primary evidence stable key for the fact node.</param>
        /// <param name="confidence">The confidence assigned to the fact.</param>
        /// <param name="unknownState">The unknown-state metadata assigned to the fact.</param>
        /// <returns>An architecture node suitable for fact-query fixtures.</returns>
        private static ArchitectureNode CreateFactNode(StableKey snapshotStableKey, StableKey nodeStableKey, NodeKind nodeKind, string displayName, string qualifiedName, StableKey projectStableKey, GraphMetadata metadata, StableKey? evidenceStableKey, Confidence confidence, UnknownState unknownState)
        {
            // Fact fixtures share the production node model so endpoint tests exercise application mapping rather than test-only contracts.
            return new ArchitectureNode(
                snapshotStableKey,
                nodeStableKey,
                nodeKind,
                displayName,
                qualifiedName,
                qualifiedName.ToLowerInvariant(),
                "C#",
                projectStableKey,
                projectStableKey,
                unknownState.HasUnknownData ? KnowledgeKind.Unknown : KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                confidence,
                unknownState,
                evidenceStableKey,
                metadata,
                FingerprintGenerator.ForNode(nodeKind, displayName, qualifiedName, qualifiedName.ToLowerInvariant(), unknownState.HasUnknownData ? KnowledgeKind.Unknown : KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a deterministic evidence record for project query fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the evidence.</param>
        /// <param name="evidenceStableKey">The stable key that identifies the evidence.</param>
        /// <param name="filePath">The repository-relative file path associated with the evidence.</param>
        /// <returns>An evidence record suitable for project detail fixtures.</returns>
        private static EvidenceRecord CreateEvidence(StableKey snapshotStableKey, StableKey evidenceStableKey, string filePath)
        {
            // Evidence fixtures include a snippet preview to prove the project detail API exposes references rather than source snippets.
            return new EvidenceRecord(
                snapshotStableKey,
                evidenceStableKey,
                EvidenceKind.ProjectFile,
                RepositoryRelativePath.Parse(filePath),
                startLine: 1,
                endLine: 20,
                symbolName: null,
                containingSymbol: null,
                snippetHash: "sha256:evidence",
                snippetPreview: "<Project Sdk=\"Microsoft.NET.Sdk.Web\">",
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                GraphMetadata.Empty,
                new Fingerprint("sha256:evidence-" + Math.Abs(StringComparer.Ordinal.GetHashCode(evidenceStableKey.Value)).ToString("x", System.Globalization.CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Creates a deterministic source evidence record for symbol query fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the evidence.</param>
        /// <param name="evidenceStableKey">The stable key that identifies the evidence.</param>
        /// <param name="filePath">The repository-relative file path associated with the evidence.</param>
        /// <param name="startLine">The starting source line for the evidence.</param>
        /// <param name="endLine">The ending source line for the evidence.</param>
        /// <param name="symbolName">The symbol name associated with the evidence.</param>
        /// <param name="containingSymbol">The containing symbol associated with the evidence.</param>
        /// <param name="snippetPreview">The snippet preview used to verify safe public bounds.</param>
        /// <returns>An evidence record suitable for symbol endpoint fixtures.</returns>
        private static EvidenceRecord CreateSymbolEvidence(StableKey snapshotStableKey, StableKey evidenceStableKey, string filePath, int startLine, int endLine, string symbolName, string containingSymbol, string snippetPreview)
        {
            // Symbol evidence includes source coordinates and bounded preview text so endpoint tests can verify safe evidence projection.
            return new EvidenceRecord(
                snapshotStableKey,
                evidenceStableKey,
                EvidenceKind.CompilerSymbol,
                RepositoryRelativePath.Parse(filePath),
                startLine,
                endLine,
                symbolName,
                containingSymbol,
                "sha256:symbol-evidence",
                snippetPreview,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                GraphMetadata.Empty,
                new Fingerprint("sha256:symbol-evidence-" + Math.Abs(StringComparer.Ordinal.GetHashCode(evidenceStableKey.Value)).ToString("x", System.Globalization.CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Creates a deterministic evidence record with detailed source context for evidence endpoint fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the evidence.</param>
        /// <param name="evidenceStableKey">The stable key that identifies the evidence.</param>
        /// <param name="filePath">The repository-relative file path associated with the evidence.</param>
        /// <param name="snippetPreview">The persisted snippet preview used to verify bounds and redaction.</param>
        /// <param name="unknownState">The unknown-state metadata assigned to the evidence.</param>
        /// <returns>An evidence record suitable for evidence endpoint fixtures.</returns>
        private static EvidenceRecord CreateDetailedEvidence(StableKey snapshotStableKey, StableKey evidenceStableKey, string filePath, string snippetPreview, UnknownState unknownState)
        {
            // Detailed evidence fixtures carry safe and secret-like metadata so endpoint tests can verify public sanitation.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["safeContext"] = "visible",
                ["secretToken"] = "ShouldNotAppear"
            });
            return new EvidenceRecord(
                snapshotStableKey,
                evidenceStableKey,
                EvidenceKind.CompilerSymbol,
                RepositoryRelativePath.Parse(filePath),
                startLine: 10,
                endLine: 14,
                symbolName: "EvidenceController",
                containingSymbol: "Evidence.Api.Controllers",
                snippetHash: "sha256:detailed-evidence",
                snippetPreview: snippetPreview,
                unknownState.HasUnknownData ? KnowledgeKind.Unknown : KnowledgeKind.Fact,
                Confidence.High,
                unknownState,
                metadata,
                new Fingerprint("sha256:detailed-evidence-" + Math.Abs(StringComparer.Ordinal.GetHashCode(evidenceStableKey.Value)).ToString("x", System.Globalization.CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Creates a deterministic project node that points at primary evidence for evidence relationship tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the project node.</param>
        /// <param name="projectStableKey">The stable key that identifies the project node.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key explaining the node.</param>
        /// <returns>An architecture node suitable for evidence relationship endpoint fixtures.</returns>
        private static ArchitectureNode CreateEvidenceProjectNode(StableKey snapshotStableKey, StableKey projectStableKey, StableKey evidenceStableKey)
        {
            // Evidence project nodes verify that related-evidence lookup can find node primary-evidence links.
            GraphMetadata metadata = CreateDetailedProjectMetadata("Api", "Api", "net10.0", true, "src/Evidence.Api/Evidence.Api.csproj");
            return new ArchitectureNode(
                snapshotStableKey,
                projectStableKey,
                NodeKind.Project,
                "Evidence.Api.csproj",
                "Evidence.Api.csproj",
                "evidence.api.csproj",
                "C#",
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                evidenceStableKey,
                metadata,
                FingerprintGenerator.ForNode(NodeKind.Project, "Evidence.Api.csproj", "Evidence.Api.csproj", "evidence.api.csproj", KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a deterministic metric record that points at primary evidence for evidence relationship tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the metric.</param>
        /// <param name="metricStableKey">The stable key that identifies the metric.</param>
        /// <param name="nodeStableKey">The node stable key scoped by the metric.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key explaining the metric.</param>
        /// <returns>A metric record suitable for evidence relationship endpoint fixtures.</returns>
        private static MetricRecord CreateEvidenceMetric(StableKey snapshotStableKey, StableKey metricStableKey, StableKey nodeStableKey, StableKey evidenceStableKey)
        {
            // Metrics participate in related evidence lookup through their primary evidence stable key.
            return new MetricRecord(
                snapshotStableKey,
                metricStableKey,
                "GraphFanIn",
                MetricScopeKind.Node,
                nodeStableKey,
                edgeStableKey: null,
                evidenceStableKey,
                "Graph fan in",
                numericValue: 3,
                textValue: null,
                unit: "edges",
                Confidence.Certain,
                UnknownState.Known,
                GraphMetadata.Empty,
                new Fingerprint("sha256:evidence-metric"));
        }

        /// <summary>
        /// Creates a deterministic rule definition for evidence rule-context fixtures.
        /// </summary>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="version">The rule version.</param>
        /// <param name="name">The developer-facing rule name.</param>
        /// <returns>A rule definition suitable for evidence endpoint fixtures.</returns>
        private static RuleDefinition CreateRuleDefinition(string ruleCode, string version, string name)
        {
            // Rule definitions let evidence detail responses display rule context through finding rule identities.
            return new RuleDefinition(
                ruleCode,
                name,
                RuleCategory.ArchitectureLayering,
                FindingSeverity.High,
                FindingStatus.Open,
                enabled: true,
                version,
                "Evidence endpoint test rule.",
                "{}",
                sourceUrls: [],
                isBuiltIn: true,
                ownerScope: null,
                GraphMetadata.Empty);
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
        /// Creates a deterministic semantic relationship edge for symbol endpoint fixtures.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the edge.</param>
        /// <param name="edgeStableKey">The stable key that identifies the edge.</param>
        /// <param name="edgeKind">The semantic relationship kind.</param>
        /// <param name="sourceNodeStableKey">The source symbol stable key.</param>
        /// <param name="targetNodeStableKey">The target symbol stable key.</param>
        /// <param name="evidenceStableKey">The evidence stable key explaining the semantic relationship.</param>
        /// <returns>An architecture edge suitable for symbol endpoint fixtures.</returns>
        private static ArchitectureEdge CreateSymbolEdge(StableKey snapshotStableKey, string edgeStableKey, EdgeKind edgeKind, StableKey sourceNodeStableKey, StableKey targetNodeStableKey, string evidenceStableKey)
        {
            // Symbol edges keep their primary evidence so usage endpoints can expose source location and bounded snippet context.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["semantic.relationship"] = edgeKind.Value
            });
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
                new StableKey(evidenceStableKey),
                metadata,
                FingerprintGenerator.ForEdge(edgeKind, sourceNodeStableKey, targetNodeStableKey, true, KnowledgeKind.Fact, metadata));
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
