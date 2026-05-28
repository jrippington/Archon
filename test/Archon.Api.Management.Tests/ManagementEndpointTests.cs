using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Archon.Api.Management;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Runs;
using Archon.Application.Graph.Persistence;
using Archon.Application.Management;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Archon.Api.Management.Tests
{
    /// <summary>
    /// Verifies WP014 controlled management endpoints expose safe operational behavior without arbitrary mutation surfaces.
    /// </summary>
    public sealed class ManagementEndpointTests
    {
        /// <summary>
        /// Confirms repository registration accepts required metadata and does not start extraction run history.
        /// </summary>
        /// <returns>A task that completes after the repository registration response is asserted.</returns>
        [Fact]
        public async Task RegisterRepositoryEndpoint_WhenRequestIsValid_ShouldStoreRepositoryWithoutStartingExtraction()
        {
            // Repository registration is metadata-only; the test also checks run history remains empty after registration.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();
            RegisterRepositoryRequest request = new(
                "repository://management-api",
                "Management Api",
                "D:/src/management-api",
                "https://example.invalid/management-api.git",
                "main",
                new Dictionary<string, string> { ["owner"] = "platform" },
                "tester");

            JsonDocument body = await PostJsonAsync(client, "/management/repositories", request);
            JsonDocument runs = await GetJsonAsync(client, "/management/runs?take=10");

            using (body)
            using (runs)
            {
                Assert.Equal("repository://management-api", body.RootElement.GetProperty("repositoryStableKey").GetString());
                Assert.Equal("platform", body.RootElement.GetProperty("metadata").GetProperty("owner").GetString());
                Assert.Equal("tester", body.RootElement.GetProperty("audit").GetProperty("requestedBy").GetString());
                Assert.Equal(0, runs.RootElement.GetProperty("totalCount").GetInt32());
            }
        }

        /// <summary>
        /// Confirms solution registration requires an existing repository and rejects path traversal shapes.
        /// </summary>
        /// <returns>A task that completes after solution validation responses are asserted.</returns>
        [Fact]
        public async Task RegisterSolutionEndpoint_WhenPathEscapesRepository_ShouldReturnValidationProblem()
        {
            // Unsafe path shapes are rejected before any solution registration state is created.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();
            await PostJsonAsync(client, "/management/repositories", new RegisterRepositoryRequest("repository://solution-policy", "Policy", "D:/src/policy", null, null, null, "tester"));

            HttpResponseMessage response = await client.PostAsJsonAsync("/management/solutions", new RegisterSolutionRequest("repository://solution-policy", "solution://solution-policy/main", "Policy", "../outside.sln", null, "tester"));
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("SolutionPathOutsideRepositoryRoot", body, StringComparison.Ordinal);
            Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
        }

        /// <summary>
        /// Confirms metadata updates accept only approved fields and reject arbitrary mutation attempts safely.
        /// </summary>
        /// <returns>A task that completes after metadata validation is asserted.</returns>
        [Fact]
        public async Task UpdateMetadataEndpoint_WhenMetadataContainsArbitraryField_ShouldReturnValidationProblem()
        {
            // The allowlist prevents callers from treating metadata updates as arbitrary graph-property mutation.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.PatchAsJsonAsync("/management/metadata", new UpdateMetadataRequest("repository", "repository://metadata-policy", new Dictionary<string, string> { ["cypher"] = "MATCH (n) DETACH DELETE n" }, "tester"));
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("MetadataFieldNotAllowed", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Neo4j", body, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms snapshot lifecycle listing filters by repository, status, and commit metadata using stable identities.
        /// </summary>
        /// <returns>A task that completes after snapshot lifecycle response assertions.</returns>
        [Fact]
        public async Task SnapshotLifecycleEndpoint_WhenSnapshotsExist_ShouldReturnFilteredRows()
        {
            // Snapshot lifecycle is read from application snapshot state and excludes persistence-local identifiers.
            StableKey repositoryStableKey = new("repository://snapshot-lifecycle");
            StableKey solutionStableKey = new("solution://snapshot-lifecycle/main");
            StableKey snapshotStableKey = new("snapshot://snapshot-lifecycle/2026-05-20T080000Z");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateSnapshot(repositoryStableKey, solutionStableKey, snapshotStableKey, "Completed", "abc123", DateTimeOffset.Parse("2026-05-20T08:00:00Z")), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/management/snapshots?repositoryStableKey=repository%3A%2F%2Fsnapshot-lifecycle&status=Completed&commitSha=abc123&take=10");

            using (body)
            {
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(snapshotStableKey.Value, item.GetProperty("snapshotStableKey").GetString());
                Assert.Equal(solutionStableKey.Value, item.GetProperty("solutionStableKey").GetString());
                Assert.DoesNotContain("neo4j", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms delete-one snapshot removes the target lifecycle row through the management API and returns safe counts.
        /// </summary>
        /// <returns>A task that completes after deletion and follow-up lifecycle query assertions finish.</returns>
        [Fact]
        public async Task DeleteSnapshotEndpoint_WhenSnapshotExists_ShouldDeleteSnapshotAndReturnSafeCounts()
        {
            // The endpoint uses URL-encoded stable keys because snapshot identities commonly include scheme separators and slashes.
            StableKey repositoryStableKey = new("repository://delete-api");
            StableKey solutionStableKey = new("solution://delete-api/main");
            StableKey snapshotStableKey = new("snapshot://delete-api/current");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateSnapshot(repositoryStableKey, solutionStableKey, snapshotStableKey, "Completed", "delete", DateTimeOffset.Parse("2026-05-20T08:00:00Z")), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument deletion = await DeleteJsonAsync(client, $"/management/snapshots/{Uri.EscapeDataString(snapshotStableKey.Value)}?requestedBy=operator");
            JsonDocument remaining = await GetJsonAsync(client, "/management/snapshots?repositoryStableKey=repository%3A%2F%2Fdelete-api&take=10");

            using (deletion)
            using (remaining)
            {
                Assert.True(deletion.RootElement.GetProperty("deleted").GetBoolean());
                Assert.Equal(snapshotStableKey.Value, deletion.RootElement.GetProperty("snapshotStableKey").GetString());
                Assert.Equal(1, deletion.RootElement.GetProperty("deletedSnapshotCount").GetInt32());
                Assert.Equal("operator", deletion.RootElement.GetProperty("audit").GetProperty("requestedBy").GetString());
                Assert.Equal(0, remaining.RootElement.GetProperty("totalCount").GetInt32());
                Assert.DoesNotContain("MATCH", deletion.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Neo4j", deletion.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms delete-one snapshot returns a validation problem when the target stable key is missing.
        /// </summary>
        /// <returns>A task that completes after the not-found response is asserted.</returns>
        [Fact]
        public async Task DeleteSnapshotEndpoint_WhenSnapshotDoesNotExist_ShouldReturnValidationProblem()
        {
            // Missing snapshots are represented as safe validation problems rather than leaking storage-specific not-found details.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.DeleteAsync($"/management/snapshots/{Uri.EscapeDataString("snapshot://missing")}");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("SnapshotNotFound", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms delete-one snapshot rejects invalid stable key input before deletion storage is invoked.
        /// </summary>
        /// <returns>A task that completes after validation response assertions finish.</returns>
        [Fact]
        public async Task DeleteSnapshotEndpoint_WhenStableKeyIsInvalid_ShouldReturnValidationProblem()
        {
            // Stable-key validation prevents callers from using arbitrary text as a destructive mutation target.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.DeleteAsync("/management/snapshots/not-a-stable-key");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("StableKeyInvalid", body, StringComparison.Ordinal);
            Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
        }

        /// <summary>
        /// Confirms delete-all snapshot cleanup requires explicit confirmation before any snapshot is removed.
        /// </summary>
        /// <returns>A task that completes after validation and preserved lifecycle data are asserted.</returns>
        [Fact]
        public async Task DeleteAllSnapshotsEndpoint_WhenConfirmationIsMissing_ShouldReturnValidationProblemWithoutDeleting()
        {
            // Missing confirmation prevents accidental global cleanup and leaves existing lifecycle rows available.
            StableKey repositoryStableKey = new("repository://delete-all-missing-api");
            StableKey solutionStableKey = new("solution://delete-all-missing-api/main");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateSnapshot(repositoryStableKey, solutionStableKey, new StableKey("snapshot://delete-all-missing-api/current"), "Completed", "delete-all", DateTimeOffset.Parse("2026-05-20T08:00:00Z")), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.PostAsJsonAsync("/management/snapshots/delete-all", new DeleteAllSnapshotsRequest(null, "operator"));
            string body = await response.Content.ReadAsStringAsync();
            JsonDocument remaining = await GetJsonAsync(client, "/management/snapshots?repositoryStableKey=repository%3A%2F%2Fdelete-all-missing-api&take=10");

            using (remaining)
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Contains("DeleteAllSnapshotsConfirmationRequired", body, StringComparison.Ordinal);
                Assert.Equal(1, remaining.RootElement.GetProperty("totalCount").GetInt32());
                Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms delete-all snapshot cleanup rejects unsupported dry-run and scoped-filter attempts as invalid contract members.
        /// </summary>
        /// <returns>A task that completes after unsupported input response assertions finish.</returns>
        [Fact]
        public async Task DeleteAllSnapshotsEndpoint_WhenDryRunOrFilterIsSubmitted_ShouldReturnValidationProblem()
        {
            // The endpoint contract intentionally omits dry-run and scoped filters, so explicit unsupported fields are rejected before service execution.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();
            using StringContent content = new("{\"confirmation\":\"delete-all-snapshots\",\"dryRun\":true,\"repositoryStableKey\":\"repository://unsupported\"}", System.Text.Encoding.UTF8, "application/json");

            HttpResponseMessage response = await client.PostAsync("/management/snapshots/delete-all", content);
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("DeleteAllSnapshotsUnsupportedField", body, StringComparison.Ordinal);
            Assert.DoesNotContain("MATCH", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Neo4j", body, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms delete-all snapshot cleanup removes every snapshot through the management API and returns safe aggregate counts.
        /// </summary>
        /// <returns>A task that completes after deletion and follow-up lifecycle query assertions finish.</returns>
        [Fact]
        public async Task DeleteAllSnapshotsEndpoint_WhenConfirmationIsValid_ShouldDeleteAllSnapshotsAndReturnSafeCounts()
        {
            // The global cleanup endpoint deletes all in-scope snapshots in the fallback store and reports only aggregate public counts.
            StableKey repositoryStableKey = new("repository://delete-all-api");
            StableKey solutionStableKey = new("solution://delete-all-api/main");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateSnapshot(repositoryStableKey, solutionStableKey, new StableKey("snapshot://delete-all-api/one"), "Completed", "one", DateTimeOffset.Parse("2026-05-20T08:00:00Z")), CancellationToken.None);
                await writer.WriteSnapshotAsync(CreateSnapshot(repositoryStableKey, solutionStableKey, new StableKey("snapshot://delete-all-api/two"), "Completed", "two", DateTimeOffset.Parse("2026-05-21T08:00:00Z")), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument deletion = await PostJsonAsync(client, "/management/snapshots/delete-all", new DeleteAllSnapshotsRequest("delete-all-snapshots", "operator"));
            JsonDocument remaining = await GetJsonAsync(client, "/management/snapshots?repositoryStableKey=repository%3A%2F%2Fdelete-all-api&take=10");

            using (deletion)
            using (remaining)
            {
                Assert.Equal(2, deletion.RootElement.GetProperty("deletedSnapshotCount").GetInt32());
                Assert.Equal("operator", deletion.RootElement.GetProperty("audit").GetProperty("requestedBy").GetString());
                Assert.Equal(0, remaining.RootElement.GetProperty("totalCount").GetInt32());
                Assert.DoesNotContain("MATCH", deletion.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Neo4j", deletion.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms retention validates scope and returns dry-run candidates without deleting lifecycle data.
        /// </summary>
        /// <returns>A task that completes after retention response assertions.</returns>
        [Fact]
        public async Task RetentionEndpoint_WhenDryRunRequested_ShouldReturnCandidatesWithoutDeletion()
        {
            // Retention preserves latest snapshots and reports older candidates inside the requested repository scope.
            StableKey repositoryStableKey = new("repository://retention-api");
            StableKey solutionStableKey = new("solution://retention-api/main");
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IArchitectureSnapshotWriter writer = services.GetRequiredService<IArchitectureSnapshotWriter>();
                await writer.WriteSnapshotAsync(CreateSnapshot(repositoryStableKey, solutionStableKey, new StableKey("snapshot://retention-api/old"), "Completed", "old", DateTimeOffset.Parse("2026-05-18T08:00:00Z")), CancellationToken.None);
                await writer.WriteSnapshotAsync(CreateSnapshot(repositoryStableKey, solutionStableKey, new StableKey("snapshot://retention-api/new"), "Completed", "new", DateTimeOffset.Parse("2026-05-20T08:00:00Z")), CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await PostJsonAsync(client, "/management/retention", new RetentionRequest(repositoryStableKey.Value, solutionStableKey.Value, 1, DateTimeOffset.Parse("2026-05-19T00:00:00Z"), true, "operator"));

            using (body)
            {
                Assert.True(body.RootElement.GetProperty("dryRun").GetBoolean());
                JsonElement candidate = Assert.Single(body.RootElement.GetProperty("candidateSnapshotStableKeys").EnumerateArray());
                Assert.Equal("snapshot://retention-api/old", candidate.GetString());
                Assert.Empty(body.RootElement.GetProperty("deletedSnapshotStableKeys").EnumerateArray());
            }
        }

        /// <summary>
        /// Confirms extraction run history exposes safe run metadata, counts, and produced snapshot identity.
        /// </summary>
        /// <returns>A task that completes after extraction run-history assertions.</returns>
        [Fact]
        public async Task RunHistoryEndpoint_WhenRunsExist_ShouldReturnSafeOperationalRows()
        {
            // The response exposes metadata keys and warning/error counts rather than arbitrary metadata values or stack traces.
            await using WebApplication app = await CreateApplicationAsync(async services =>
            {
                IExtractionRunHistory history = services.GetRequiredService<IExtractionRunHistory>();
                ExtractionRun run = new(
                    ExtractionRunId.New(),
                    ExtractionRunStatus.Completed,
                    new ExtractionRunRequestSummary("D:/src/run-history", ["Archon.slnx"], "main", "abc123", "tester", ["ticket"]),
                    DateTimeOffset.Parse("2026-05-20T08:00:00Z"),
                    DateTimeOffset.Parse("2026-05-20T08:05:00Z"),
                    new ExtractionRunProgress("Completed", "Snapshot persisted.", 100, DateTimeOffset.Parse("2026-05-20T08:05:00Z")),
                    [new ExtractionRunWarning("RunWarning", "warning", "Completed", DateTimeOffset.Parse("2026-05-20T08:05:00Z"))],
                    [],
                    [],
                    "snapshot://run-history/current");
                await history.UpdateAsync(run, CancellationToken.None);
            });
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await GetJsonAsync(client, "/management/runs?status=Completed&take=5");

            using (body)
            {
                JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("Completed", item.GetProperty("status").GetString());
                Assert.Equal("snapshot://run-history/current", item.GetProperty("snapshotStableKey").GetString());
                Assert.Equal(1, item.GetProperty("warningCount").GetInt32());
                Assert.Contains(item.GetProperty("metadataKeys").EnumerateArray(), key => key.GetString() == "ticket");
            }
        }

        /// <summary>
        /// Confirms rule enablement accepts a rule code and version without editing rule definition files.
        /// </summary>
        /// <returns>A task that completes after rule enablement response assertions.</returns>
        [Fact]
        public async Task RuleEnablementEndpoint_WhenRequestIsValid_ShouldReturnAuditReadyState()
        {
            // Enablement is an overlay with audit metadata rather than a mutation of the persisted rule catalog definition.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            JsonDocument body = await PutJsonAsync(client, "/management/rules/enablement", new RuleEnablementRequest("ARCHON-001", "1.0.0", false, "Temporarily disabled in local validation.", "operator"));

            using (body)
            {
                Assert.Equal("ARCHON-001", body.RootElement.GetProperty("ruleCode").GetString());
                Assert.False(body.RootElement.GetProperty("enabled").GetBoolean());
                Assert.Equal("operator", body.RootElement.GetProperty("audit").GetProperty("requestedBy").GetString());
            }
        }

        /// <summary>
        /// Confirms unsupported maintenance operations fail safely and cannot act as arbitrary mutation commands.
        /// </summary>
        /// <returns>A task that completes after maintenance validation assertions.</returns>
        [Fact]
        public async Task MaintenanceEndpoint_WhenOperationIsUnsupported_ShouldReturnValidationProblem()
        {
            // Raw operational command text is not accepted unless it matches the explicit maintenance allowlist.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.PostAsJsonAsync("/management/maintenance", new MaintenanceRequest("MATCH (n) DETACH DELETE n", false, "tester"));
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("MaintenanceOperationUnsupported", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms health and readiness responses are sanitized for monitoring consumers.
        /// </summary>
        /// <returns>A task that completes after health and readiness responses are asserted.</returns>
        [Fact]
        public async Task HealthAndReadinessEndpoints_WhenCalled_ShouldReturnSanitizedStatus()
        {
            // Monitoring endpoints should reveal public dependency names and states, not secrets or infrastructure connection details.
            await using WebApplication app = await CreateApplicationAsync(_ => Task.CompletedTask);
            using HttpClient client = app.GetTestClient();

            JsonDocument health = await GetJsonAsync(client, "/health");
            JsonDocument readiness = await GetJsonAsync(client, "/ready");

            using (health)
            using (readiness)
            {
                Assert.Equal("Healthy", health.RootElement.GetProperty("status").GetString());
                Assert.True(readiness.RootElement.TryGetProperty("dependencies", out JsonElement dependencies));
                Assert.Contains(dependencies.EnumerateArray(), dependency => dependency.GetProperty("name").GetString() == "rule-catalog");
                JsonElement snapshotLifecycle = Assert.Single(dependencies.EnumerateArray(), dependency => dependency.GetProperty("name").GetString() == "snapshot-lifecycle");
                Assert.Equal("Ready", snapshotLifecycle.GetProperty("status").GetString());
                string combined = health.RootElement.ToString() + readiness.RootElement.ToString();
                Assert.DoesNotContain("password", combined, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("bolt://", combined, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("in-memory", combined, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Creates a management API test host and runs optional service seeding before HTTP requests are sent.
        /// </summary>
        /// <param name="seedAsync">The optional asynchronous seed operation that receives the built service provider.</param>
        /// <returns>The started web application configured with management endpoints.</returns>
        private static async Task<WebApplication> CreateApplicationAsync(Func<IServiceProvider, Task> seedAsync)
        {
            // Test hosts use the same public service and route extension methods that production composition will call.
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddArchonManagementApi();
            WebApplication app = builder.Build();
            app.MapArchonManagementApi();
            await seedAsync(app.Services).ConfigureAwait(false);
            await app.StartAsync().ConfigureAwait(false);
            return app;
        }

        /// <summary>
        /// Sends a GET request and parses the response body as JSON after confirming success.
        /// </summary>
        /// <param name="client">The HTTP client used to call the test host.</param>
        /// <param name="uri">The request URI to send.</param>
        /// <returns>The parsed JSON response body.</returns>
        private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string uri)
        {
            // Success is asserted before parsing so tests fail at the correct behavioral boundary.
            using HttpResponseMessage response = await client.GetAsync(uri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        /// <summary>
        /// Sends a POST request and parses the response body as JSON after confirming success.
        /// </summary>
        /// <typeparam name="TRequest">The request body type to serialize.</typeparam>
        /// <param name="client">The HTTP client used to call the test host.</param>
        /// <param name="uri">The request URI to send.</param>
        /// <param name="request">The request body to serialize as JSON.</param>
        /// <returns>The parsed JSON response body.</returns>
        private static async Task<JsonDocument> PostJsonAsync<TRequest>(HttpClient client, string uri, TRequest request)
        {
            // The helper keeps tests focused on contract behavior instead of repeated HTTP boilerplate.
            using HttpResponseMessage response = await client.PostAsJsonAsync(uri, request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        /// <summary>
        /// Sends a PUT request and parses the response body as JSON after confirming success.
        /// </summary>
        /// <typeparam name="TRequest">The request body type to serialize.</typeparam>
        /// <param name="client">The HTTP client used to call the test host.</param>
        /// <param name="uri">The request URI to send.</param>
        /// <param name="request">The request body to serialize as JSON.</param>
        /// <returns>The parsed JSON response body.</returns>
        private static async Task<JsonDocument> PutJsonAsync<TRequest>(HttpClient client, string uri, TRequest request)
        {
            // PUT is used for idempotent rule enablement overlays in the management API contract.
            using HttpResponseMessage response = await client.PutAsJsonAsync(uri, request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        /// <summary>
        /// Sends a DELETE request and parses the response body as JSON after confirming success.
        /// </summary>
        /// <param name="client">The HTTP client used to call the test host.</param>
        /// <param name="uri">The request URI to send.</param>
        /// <returns>The parsed JSON response body.</returns>
        private static async Task<JsonDocument> DeleteJsonAsync(HttpClient client, string uri)
        {
            // DELETE is used for destructive snapshot cleanup and the helper keeps success assertions consistent with other endpoint tests.
            using HttpResponseMessage response = await client.DeleteAsync(uri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        /// <summary>
        /// Creates a minimal extracted snapshot for lifecycle and retention endpoint tests.
        /// </summary>
        /// <param name="repositoryStableKey">The stable repository identity for the snapshot.</param>
        /// <param name="solutionStableKey">The stable solution identity for the snapshot.</param>
        /// <param name="snapshotStableKey">The stable snapshot identity.</param>
        /// <param name="status">The lifecycle status recorded on the snapshot.</param>
        /// <param name="commitSha">The source-control commit SHA recorded on the snapshot.</param>
        /// <param name="startedUtc">The UTC timestamp when snapshot extraction started.</param>
        /// <returns>The extracted architecture snapshot used by the in-memory writer.</returns>
        private static ExtractedArchitectureSnapshot CreateSnapshot(StableKey repositoryStableKey, StableKey solutionStableKey, StableKey snapshotStableKey, string status, string commitSha, DateTimeOffset startedUtc)
        {
            // Only repository, solution, and snapshot header sections are needed for lifecycle tests.
            RepositoryModel repository = new(repositoryStableKey, "Management Repository", "D:/src/management", null, "main", GraphMetadata.Empty);
            SolutionModel solution = new(repositoryStableKey, solutionStableKey, "Management.slnx", RepositoryRelativePath.Parse("Management.slnx"), GraphMetadata.Empty);
            SnapshotHeader header = new(snapshotStableKey, repositoryStableKey, "main", commitSha, startedUtc, startedUtc.AddMinutes(2), "test", status, [], [], GraphMetadata.Empty);
            return new ExtractedArchitectureSnapshot(header, [repository], [solution], [], [], [], [], [], [], [], [], []);
        }
    }
}
