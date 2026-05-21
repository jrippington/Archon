using Archon.Api.Extraction;
using Archon.Api.Extraction.Contracts;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Graph.Persistence;
using Archon.Extractors.Projects.Solutions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Archon.Api.Extraction.Tests
{
    /// <summary>
    /// Verifies the WP004 extraction HTTP endpoints translate requests into application-layer start and status behavior.
    /// </summary>
    public sealed class ExtractionEndpointTests : IDisposable
    {
        /// <summary>
        /// Stores temporary repository roots created by endpoint tests for deterministic cleanup.
        /// </summary>
        private readonly List<string> _temporaryDirectories = [];

        /// <summary>
        /// Deletes temporary repository directories created by endpoint tests.
        /// </summary>
        public void Dispose()
        {
            // Endpoint tests use real filesystem paths because validation is part of the public HTTP behavior.
            foreach (string temporaryDirectory in _temporaryDirectories)
            {
                DeleteTemporaryDirectoryWithRetry(temporaryDirectory);
            }
        }

        /// <summary>
        /// Deletes a temporary repository directory while tolerating brief background extraction file handles.
        /// </summary>
        /// <param name="temporaryDirectory">The temporary repository directory created by the test.</param>
        private static void DeleteTemporaryDirectoryWithRetry(string temporaryDirectory)
        {
            // The in-process scheduler can still be finishing solution-file reads after an endpoint returns Accepted, so cleanup retries transient sharing violations.
            const int MaximumAttempts = 5;
            for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                try
                {
                    if (Directory.Exists(temporaryDirectory))
                    {
                        Directory.Delete(temporaryDirectory, recursive: true);
                    }

                    return;
                }
                catch (IOException) when (attempt < MaximumAttempts)
                {
                    // A short backoff lets the background extraction task release its file handle without making normal cleanup slow.
                    Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
                }
                catch (UnauthorizedAccessException) when (attempt < MaximumAttempts)
                {
                    // Windows can report transient file-handle cleanup races as unauthorized access, so retry them the same way.
                    Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
                }
            }
        }

        /// <summary>
        /// Verifies extraction API service registration composes the WP005 project extraction stage instead of the WP004 placeholder stage.
        /// </summary>
        [Fact]
        public void AddArchonExtractionApi_WhenServicesAreBuilt_ShouldRegisterRepositorySolutionExtractionStage()
        {
            // The API module is the existing composition boundary for the extraction pipeline, so this test guards the stage registration path.
            ServiceCollection services = new();

            services.AddArchonExtractionApi();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IExtractionStage stage = Assert.Single(serviceProvider.GetServices<IExtractionStage>());
            Assert.IsType<RepositorySolutionExtractionStage>(stage);
            Assert.Equal("project-repository-solution", stage.StageId);
        }

        /// <summary>
        /// Verifies POST /extractions accepts a valid request and returns a queued run identifier quickly.
        /// </summary>
        /// <returns>A task that completes after the endpoint response is validated.</returns>
        [Fact]
        public async Task PostExtractions_WhenRequestIsValid_ShouldReturnAcceptedRun()
        {
            // The in-memory test host exercises route mapping and JSON serialization without starting Kestrel or Aspire.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln");
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();
            StartExtractionApiRequest request = new(
                repositoryRoot,
                ["CustomerSuite.sln"],
                BranchName: "main",
                CommitSha: "abcdef1234567890",
                RequestedBy: "developer@example.invalid",
                Metadata: new Dictionary<string, string>
                {
                    ["source"] = "api-test"
                });

            HttpResponseMessage response = await client.PostAsJsonAsync("/extractions", request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("runId").GetString()));
            Assert.Equal("Queued", body.RootElement.GetProperty("status").GetString());
            Assert.Equal(1, body.RootElement.GetProperty("submittedRequest").GetProperty("solutionPaths").GetArrayLength());
        }

        /// <summary>
        /// Verifies extraction routes are exposed at the resolved WP004 paths and not behind an accidental common API prefix.
        /// </summary>
        /// <returns>A task that completes after direct and prefixed route responses are validated.</returns>
        [Fact]
        public async Task ExtractionEndpoints_WhenCalledThroughResolvedRoutes_ShouldNotRequireApiPrefix()
        {
            // Route hardening protects the public contract from accidental host-level prefix drift while still using the in-memory test server.
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage directHistoryResponse = await client.GetAsync("/extractions");
            HttpResponseMessage prefixedHistoryResponse = await client.GetAsync("/api/extractions");
            HttpResponseMessage directStatusResponse = await client.GetAsync("/extractions/not-a-run-id");
            HttpResponseMessage prefixedStatusResponse = await client.GetAsync("/api/extractions/not-a-run-id");

            Assert.Equal(HttpStatusCode.OK, directHistoryResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, prefixedHistoryResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, directStatusResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, prefixedStatusResponse.StatusCode);
        }

        /// <summary>
        /// Verifies POST /extractions returns validation details and does not create a run when the request is invalid.
        /// </summary>
        /// <returns>A task that completes after the validation response is validated.</returns>
        [Fact]
        public async Task PostExtractions_WhenRequestIsInvalid_ShouldReturnValidationProblemWithoutCreatingRun()
        {
            // Missing repository root is enough to prove the API returns client errors before accepting a run.
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();
            StartExtractionApiRequest request = new(
                RepositoryRootDirectory: " ",
                SolutionPaths: ["CustomerSuite.sln"],
                BranchName: null,
                CommitSha: null,
                RequestedBy: null,
                Metadata: null);

            HttpResponseMessage response = await client.PostAsJsonAsync("/extractions", request);
            HttpResponseMessage historyResponse = await client.GetAsync("/extractions/00000000-0000-0000-0000-000000000000");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, historyResponse.StatusCode);
            using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            Assert.Contains("RepositoryRootRequired", body.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        }

        /// <summary>
        /// Verifies validation responses and history summaries expose metadata keys without leaking sensitive metadata values.
        /// </summary>
        /// <returns>A task that completes after validation and accepted-run redaction responses are inspected.</returns>
        [Fact]
        public async Task ExtractionEndpoints_WhenMetadataContainsSensitiveValues_ShouldRedactMetadataValues()
        {
            // Metadata value redaction is asserted through both a rejected request and an accepted request summary.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln");
            string secretValue = "Server=localhost;Password=SuperSecretPassword123!;User Id=neo4j";
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();

            StartExtractionApiRequest invalidRequest = new(
                RepositoryRootDirectory: " ",
                SolutionPaths: ["CustomerSuite.sln"],
                BranchName: null,
                CommitSha: null,
                RequestedBy: null,
                Metadata: new Dictionary<string, string>
                {
                    ["connectionString"] = secretValue
                });
            StartExtractionApiRequest validRequest = new(
                repositoryRoot,
                ["CustomerSuite.sln"],
                BranchName: null,
                CommitSha: null,
                RequestedBy: null,
                Metadata: new Dictionary<string, string>
                {
                    ["connectionString"] = secretValue
                });

            HttpResponseMessage invalidResponse = await client.PostAsJsonAsync("/extractions", invalidRequest);
            HttpResponseMessage validResponse = await client.PostAsJsonAsync("/extractions", validRequest);

            string invalidBody = await invalidResponse.Content.ReadAsStringAsync();
            string validBody = await validResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, validResponse.StatusCode);
            Assert.DoesNotContain(secretValue, invalidBody, StringComparison.Ordinal);
            Assert.DoesNotContain(secretValue, validBody, StringComparison.Ordinal);
            Assert.Contains("connectionString", validBody, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies GET /extractions/{runId} returns status for a run created by the start endpoint.
        /// </summary>
        /// <returns>A task that completes after the status response is validated.</returns>
        [Fact]
        public async Task GetExtractionStatus_WhenRunExists_ShouldReturnCurrentRunStatus()
        {
            // Status retrieval uses the run identifier returned by the start endpoint to mimic a polling API consumer.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln");
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();
            HttpResponseMessage startResponse = await client.PostAsJsonAsync(
                "/extractions",
                new StartExtractionApiRequest(repositoryRoot, ["CustomerSuite.sln"], null, null, null, null));
            using JsonDocument startBody = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
            string runId = startBody.RootElement.GetProperty("runId").GetString()!;

            HttpResponseMessage statusResponse = await client.GetAsync($"/extractions/{runId}");

            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
            using JsonDocument statusBody = await JsonDocument.ParseAsync(await statusResponse.Content.ReadAsStreamAsync());
            Assert.Equal(runId, statusBody.RootElement.GetProperty("runId").GetString());
            Assert.Contains(statusBody.RootElement.GetProperty("status").GetString(), new[] { "Queued", "Running", "Completed" });
        }

        /// <summary>
        /// Verifies GET /extractions/{runId} exposes terminal completion details, progress fields, warnings, errors, and snapshot identity.
        /// </summary>
        /// <returns>A task that completes after a completed asynchronous run status response is validated.</returns>
        [Fact]
        public async Task GetExtractionStatus_WhenRunCompletes_ShouldReturnProgressDiagnosticsAndSnapshotIdentity()
        {
            // Polling to completion proves the API status contract exposes the final persistence identity and progress details, not just acceptance state.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln");
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();
            HttpResponseMessage startResponse = await client.PostAsJsonAsync(
                "/extractions",
                new StartExtractionApiRequest(repositoryRoot, ["CustomerSuite.sln"], null, null, null, null));
            using JsonDocument startBody = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
            string runId = startBody.RootElement.GetProperty("runId").GetString()!;

            JsonDocument statusBody = await PollForTerminalStatusAsync(client, runId);

            using (statusBody)
            {
                Assert.Equal(runId, statusBody.RootElement.GetProperty("runId").GetString());
                Assert.Equal("Completed", statusBody.RootElement.GetProperty("status").GetString());
                Assert.True(statusBody.RootElement.TryGetProperty("completedUtc", out JsonElement completedUtc));
                Assert.NotEqual(JsonValueKind.Null, completedUtc.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(statusBody.RootElement.GetProperty("snapshotIdentity").GetString()));

                JsonElement progress = statusBody.RootElement.GetProperty("progress");
                Assert.Equal("Completed", progress.GetProperty("stage").GetString());
                Assert.Equal("Extraction snapshot persisted successfully.", progress.GetProperty("message").GetString());
                Assert.Equal(100, progress.GetProperty("percentage").GetInt32());
                Assert.NotEqual(default, progress.GetProperty("lastUpdatedUtc").GetDateTimeOffset());

                Assert.Empty(statusBody.RootElement.GetProperty("warnings").EnumerateArray());
                Assert.Empty(statusBody.RootElement.GetProperty("errors").EnumerateArray());
            }
        }

        /// <summary>
        /// Verifies GET /extractions/{runId} returns not found for an unknown run identifier.
        /// </summary>
        /// <returns>A task that completes after the not-found response is validated.</returns>
        [Fact]
        public async Task GetExtractionStatus_WhenRunIsMissing_ShouldReturnNotFound()
        {
            // Unknown run identifiers must not throw or leak internals; they translate to a controlled 404 response.
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage response = await client.GetAsync("/extractions/00000000-0000-0000-0000-000000000000");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        /// <summary>
        /// Verifies GET /extractions returns recent extraction runs with deterministic summary fields.
        /// </summary>
        /// <returns>A task that completes after the history response is validated.</returns>
        [Fact]
        public async Task GetExtractions_WhenRunsExist_ShouldReturnRecentRunSummaries()
        {
            // The history endpoint gives API consumers a polling-friendly list without requiring each run id up front.
            string firstRepositoryRoot = CreateRepositoryRoot();
            string secondRepositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(firstRepositoryRoot, "FirstSuite.sln");
            CreateSolutionFile(secondRepositoryRoot, "SecondSuite.sln");
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();
            await client.PostAsJsonAsync("/extractions", new StartExtractionApiRequest(firstRepositoryRoot, ["FirstSuite.sln"], null, null, null, null));
            await client.PostAsJsonAsync("/extractions", new StartExtractionApiRequest(secondRepositoryRoot, ["SecondSuite.sln"], null, null, null, null));

            HttpResponseMessage response = await client.GetAsync("/extractions");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            JsonElement runs = body.RootElement.GetProperty("runs");
            Assert.Equal(2, runs.GetArrayLength());
            Assert.Contains(runs[0].GetProperty("status").GetString(), new[] { "Queued", "Running", "Completed" });
            Assert.Equal(1, runs[0].GetProperty("solutionCount").GetInt32());
            Assert.True(runs[0].GetProperty("warningCount").GetInt32() >= 0);
            Assert.Equal(0, runs[0].GetProperty("errorCount").GetInt32());
            Assert.False(runs[0].GetRawText().Contains("System.", StringComparison.Ordinal));
        }

        /// <summary>
        /// Verifies GET /extractions honors the optional limit query parameter when returning history.
        /// </summary>
        /// <returns>A task that completes after the limited history response is validated.</returns>
        [Fact]
        public async Task GetExtractions_WhenLimitIsProvided_ShouldLimitRecentRunSummaries()
        {
            // Limit support keeps the first history endpoint lightweight while avoiding a full paging contract in this slice.
            string firstRepositoryRoot = CreateRepositoryRoot();
            string secondRepositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(firstRepositoryRoot, "FirstSuite.sln");
            CreateSolutionFile(secondRepositoryRoot, "SecondSuite.sln");
            await using WebApplication app = await CreateApplicationAsync();
            using HttpClient client = app.GetTestClient();
            await client.PostAsJsonAsync("/extractions", new StartExtractionApiRequest(firstRepositoryRoot, ["FirstSuite.sln"], null, null, null, null));
            await client.PostAsJsonAsync("/extractions", new StartExtractionApiRequest(secondRepositoryRoot, ["SecondSuite.sln"], null, null, null, null));

            HttpResponseMessage response = await client.GetAsync("/extractions?limit=1");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            Assert.Single(body.RootElement.GetProperty("runs").EnumerateArray());
        }

        /// <summary>
        /// Verifies accepted-run persistence failures are exposed as controlled diagnostics without stack traces or secret values.
        /// </summary>
        /// <returns>A task that completes after a failed asynchronous run is visible through the status endpoint.</returns>
        [Fact]
        public async Task GetExtractionStatus_WhenAcceptedRunFails_ShouldReturnRedactedFailureDetails()
        {
            // A deterministic failing persistence writer proves runtime failure redaction after the request has already been accepted.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln");
            string secretValue = "Password=RuntimeSecret123!";
            await using WebApplication app = await CreateApplicationAsync(services => services.AddSingleton<IArchitectureSnapshotWriter>(new FailingSnapshotWriter(secretValue)));
            using HttpClient client = app.GetTestClient();
            HttpResponseMessage startResponse = await client.PostAsJsonAsync(
                "/extractions",
                new StartExtractionApiRequest(repositoryRoot, ["CustomerSuite.sln"], null, null, null, null));
            using JsonDocument startBody = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
            string runId = startBody.RootElement.GetProperty("runId").GetString()!;

            JsonDocument statusBody = await PollForTerminalStatusAsync(client, runId);

            using (statusBody)
            {
                string rawStatus = statusBody.RootElement.GetRawText();
                Assert.Equal("Failed", statusBody.RootElement.GetProperty("status").GetString());
                JsonElement error = Assert.Single(statusBody.RootElement.GetProperty("errors").EnumerateArray());
                Assert.Equal("PersistenceUnavailable", error.GetProperty("code").GetString());
                Assert.Equal("SnapshotPersistence", error.GetProperty("stage").GetString());
                Assert.Equal("Diagnostic details were redacted. Review server logs for details.", error.GetProperty("message").GetString());
                Assert.DoesNotContain(secretValue, rawStatus, StringComparison.Ordinal);
                Assert.DoesNotContain("System.", rawStatus, StringComparison.Ordinal);
                Assert.DoesNotContain(" at ", rawStatus, StringComparison.Ordinal);
                Assert.DoesNotContain("StackTrace", rawStatus, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Creates a test web application with extraction services and endpoints mapped.
        /// </summary>
        /// <returns>A started in-memory web application.</returns>
        private static async Task<WebApplication> CreateApplicationAsync(Action<IServiceCollection>? configureServices = null)
        {
            // The module-level host keeps endpoint tests focused on extraction routes rather than unrelated host composition.
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddArchonExtractionApi();
            configureServices?.Invoke(builder.Services);

            WebApplication app = builder.Build();
            app.MapArchonExtractionApi();
            await app.StartAsync();
            return app;
        }

        /// <summary>
        /// Polls the status endpoint until the asynchronous in-process scheduler records a terminal state.
        /// </summary>
        /// <param name="client">The HTTP client connected to the in-memory test server.</param>
        /// <param name="runId">The run identifier returned by the start endpoint.</param>
        /// <returns>The final status response body as a JSON document owned by the caller.</returns>
        private static async Task<JsonDocument> PollForTerminalStatusAsync(HttpClient client, string runId)
        {
            // The in-process scheduler runs on a background task, so polling avoids race-prone fixed sleeps while keeping the test bounded.
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < deadline)
            {
                HttpResponseMessage statusResponse = await client.GetAsync($"/extractions/{runId}");
                Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
                JsonDocument body = await JsonDocument.ParseAsync(await statusResponse.Content.ReadAsStreamAsync());
                string? status = body.RootElement.GetProperty("status").GetString();
                if (string.Equals(status, "Completed", StringComparison.Ordinal) || string.Equals(status, "Failed", StringComparison.Ordinal))
                {
                    return body;
                }

                body.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(25));
            }

            throw new TimeoutException("The extraction run did not reach a terminal status before the test timeout.");
        }

        /// <summary>
        /// Creates a temporary repository root directory for a test request.
        /// </summary>
        /// <returns>The absolute path to the created repository root.</returns>
        private string CreateRepositoryRoot()
        {
            // A unique directory per test prevents path normalization assertions from observing stale files.
            string path = Path.Combine(Path.GetTempPath(), "archon-wp004-api-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            _temporaryDirectories.Add(path);
            return path;
        }

        /// <summary>
        /// Creates an empty placeholder solution file under a repository root.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that should contain the solution file.</param>
        /// <param name="pathParts">The nested path parts ending with the solution file name.</param>
        /// <returns>The absolute path to the created solution file.</returns>
        private static string CreateSolutionFile(string repositoryRoot, params string[] pathParts)
        {
            // The WP005 project extraction stage now reads submitted solution headers, so endpoint fixtures use valid minimal solution content.
            string solutionPath = Path.Combine([repositoryRoot, .. pathParts]);
            Directory.CreateDirectory(Path.GetDirectoryName(solutionPath)!);
            File.WriteAllText(
                solutionPath,
                string.Join(
                    Environment.NewLine,
                    [
                        "Microsoft Visual Studio Solution File, Format Version 12.00",
                        "# Visual Studio Version 17",
                        "Global",
                        "EndGlobal"
                    ]));
            return solutionPath;
        }

        /// <summary>
        /// Provides a deterministic persistence failure for API redaction tests.
        /// </summary>
        private sealed class FailingSnapshotWriter : IArchitectureSnapshotWriter
        {
            /// <summary>
            /// Stores a sensitive value that must never appear in the public API response.
            /// </summary>
            private readonly string _sensitiveValue;

            /// <summary>
            /// Initializes a new instance of the <see cref="FailingSnapshotWriter"/> class.
            /// </summary>
            /// <param name="sensitiveValue">The secret-like value used to prove response redaction.</param>
            public FailingSnapshotWriter(string sensitiveValue)
            {
                // The writer keeps the value only for the test assertion; the returned diagnostic deliberately omits it.
                _sensitiveValue = sensitiveValue;
            }

            /// <summary>
            /// Returns a controlled persistence failure without exposing infrastructure exception details.
            /// </summary>
            /// <param name="snapshot">The assembled snapshot that would normally be persisted.</param>
            /// <param name="cancellationToken">The cancellation token for the simulated persistence operation.</param>
            /// <returns>A failed persistence result with a user-actionable, credential-safe diagnostic.</returns>
            public Task<SnapshotPersistenceResult> WriteSnapshotAsync(ExtractedArchitectureSnapshot snapshot, CancellationToken cancellationToken = default)
            {
                // Touch the sensitive field so the test double models a writer that knows a secret while still returning only safe text.
                Assert.False(string.IsNullOrWhiteSpace(_sensitiveValue));
                SnapshotPersistenceResult result = SnapshotPersistenceResult.Failure(
                    snapshot.SnapshotHeader is null ? null : snapshot.SnapshotHeader.StableKey.ToString(),
                    new PersistenceError(
                        PersistenceStage.SnapshotPersistence,
                        "PersistenceUnavailable",
                        "System.InvalidOperationException: Snapshot persistence failed at Neo4j.Driver with Password=" + _sensitiveValue + " at Infrastructure.Adapter"));
                return Task.FromResult(result);
            }
        }
    }
}
