using Archon.Application.Extraction.Requests;
using Archon.Application.Extraction.Runs;
using Archon.Application.Extraction.Scheduling;
using Archon.Application.Extraction.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Archon.Application.Tests.Extraction.Requests
{
    /// <summary>
    /// Verifies the first WP004 application slice that validates extraction start requests, creates accepted runs, schedules work, and exposes status.
    /// </summary>
    public sealed class StartExtractionApplicationServiceTests : IDisposable
    {
        /// <summary>
        /// Stores temporary repository roots created by tests so each scenario can clean up filesystem state deterministically.
        /// </summary>
        private readonly List<string> _temporaryDirectories = [];

        /// <summary>
        /// Releases temporary repository directories created for validation scenarios.
        /// </summary>
        public void Dispose()
        {
            // Tests create real directories and solution files because path validation must prove filesystem behavior.
            foreach (string temporaryDirectory in _temporaryDirectories)
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies a valid request creates a run, preserves normalized solution paths, and schedules asynchronous work through the seam.
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenRequestIsValid_ShouldCreateRunAndScheduleWork()
        {
            // A valid request uses a repository root with an explicit relative solution path that resolves inside the root.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "src", "CustomerSuite.sln");
            RecordingExtractionWorkScheduler scheduler = new();
            InMemoryExtractionRunHistory runHistory = new();
            StartExtractionApplicationService service = CreateService(runHistory, scheduler);
            StartExtractionRequest request = new(
                repositoryRoot,
                [Path.Combine("src", "CustomerSuite.sln")],
                BranchName: "main",
                CommitSha: "abcdef1234567890",
                RequestedBy: "developer@example.invalid",
                Metadata: new Dictionary<string, string>
                {
                    ["source"] = "unit-test"
                });

            StartExtractionResult result = await service.StartAsync(request, CancellationToken.None);

            Assert.True(result.Accepted);
            Assert.NotNull(result.Run);
            Assert.Empty(result.ValidationErrors);
            Assert.Equal(ExtractionRunStatus.Queued, result.Run.Status);
            Assert.Equal(Path.GetFullPath(solutionPath), result.Run.SubmittedRequest.SolutionPaths.Single());
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot)), Path.TrimEndingDirectorySeparator(result.Run.SubmittedRequest.RepositoryRootDirectory));
            Assert.Single(scheduler.ScheduledRunIds);
            Assert.Equal(result.Run.RunId, scheduler.ScheduledRunIds.Single());
        }

        /// <summary>
        /// Verifies missing repository roots are rejected before any run record or scheduled work is created.
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenRepositoryRootIsMissing_ShouldRejectWithoutCreatingRun()
        {
            // Missing repository root is a pre-acceptance validation error and must not create operational run state.
            RecordingExtractionWorkScheduler scheduler = new();
            InMemoryExtractionRunHistory runHistory = new();
            StartExtractionApplicationService service = CreateService(runHistory, scheduler);
            StartExtractionRequest request = new(
                RepositoryRootDirectory: "   ",
                SolutionPaths: ["CustomerSuite.sln"],
                BranchName: null,
                CommitSha: null,
                RequestedBy: null,
                Metadata: null);

            StartExtractionResult result = await service.StartAsync(request, CancellationToken.None);

            Assert.False(result.Accepted);
            Assert.Null(result.Run);
            Assert.Contains(result.ValidationErrors, error => error.Code == StartExtractionValidationErrorCodes.RepositoryRootRequired);
            Assert.Empty(await runHistory.GetRecentAsync(10, CancellationToken.None));
            Assert.Empty(scheduler.ScheduledRunIds);
        }

        /// <summary>
        /// Verifies empty solution lists are rejected before any run record or scheduled work is created.
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenSolutionListIsEmpty_ShouldRejectWithoutCreatingRun()
        {
            // The API contract requires explicit solutions and never scans the repository to infer them.
            string repositoryRoot = CreateRepositoryRoot();
            RecordingExtractionWorkScheduler scheduler = new();
            InMemoryExtractionRunHistory runHistory = new();
            StartExtractionApplicationService service = CreateService(runHistory, scheduler);
            StartExtractionRequest request = new(
                repositoryRoot,
                [],
                BranchName: null,
                CommitSha: null,
                RequestedBy: null,
                Metadata: null);

            StartExtractionResult result = await service.StartAsync(request, CancellationToken.None);

            Assert.False(result.Accepted);
            Assert.Null(result.Run);
            Assert.Contains(result.ValidationErrors, error => error.Code == StartExtractionValidationErrorCodes.SolutionPathRequired);
            Assert.Empty(await runHistory.GetRecentAsync(10, CancellationToken.None));
            Assert.Empty(scheduler.ScheduledRunIds);
        }

        /// <summary>
        /// Verifies non-existent repository roots produce a validation error without creating run state.
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenRepositoryRootDoesNotExist_ShouldRejectWithoutCreatingRun()
        {
            // A syntactically valid but missing root is still rejected because execution would otherwise analyze an unknown location.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-missing-" + Guid.NewGuid().ToString("N"));
            RecordingExtractionWorkScheduler scheduler = new();
            InMemoryExtractionRunHistory runHistory = new();
            StartExtractionApplicationService service = CreateService(runHistory, scheduler);
            StartExtractionRequest request = new(
                repositoryRoot,
                ["CustomerSuite.sln"],
                BranchName: null,
                CommitSha: null,
                RequestedBy: null,
                Metadata: null);

            StartExtractionResult result = await service.StartAsync(request, CancellationToken.None);

            Assert.False(result.Accepted);
            Assert.Contains(result.ValidationErrors, error => error.Code == StartExtractionValidationErrorCodes.RepositoryRootNotFound);
            Assert.Empty(await runHistory.GetRecentAsync(10, CancellationToken.None));
            Assert.Empty(scheduler.ScheduledRunIds);
        }

        /// <summary>
        /// Verifies non-existent solution paths are rejected after relative paths are resolved against the repository root.
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenSolutionFileDoesNotExist_ShouldRejectWithoutCreatingRun()
        {
            // Missing solution files are rejected before later Roslyn loading can observe them.
            string repositoryRoot = CreateRepositoryRoot();
            RecordingExtractionWorkScheduler scheduler = new();
            InMemoryExtractionRunHistory runHistory = new();
            StartExtractionApplicationService service = CreateService(runHistory, scheduler);
            StartExtractionRequest request = new(
                repositoryRoot,
                [Path.Combine("src", "Missing.sln")],
                BranchName: null,
                CommitSha: null,
                RequestedBy: null,
                Metadata: null);

            StartExtractionResult result = await service.StartAsync(request, CancellationToken.None);

            Assert.False(result.Accepted);
            Assert.Contains(result.ValidationErrors, error => error.Code == StartExtractionValidationErrorCodes.SolutionPathNotFound);
            Assert.Empty(await runHistory.GetRecentAsync(10, CancellationToken.None));
            Assert.Empty(scheduler.ScheduledRunIds);
        }

        /// <summary>
        /// Verifies solution paths with unsupported extensions are rejected even when the file exists.
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenSolutionExtensionIsInvalid_ShouldRejectWithoutCreatingRun()
        {
            // WP004 only accepts .sln files until another supported solution format is explicitly documented.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "src", "CustomerSuite.txt");
            RecordingExtractionWorkScheduler scheduler = new();
            InMemoryExtractionRunHistory runHistory = new();
            StartExtractionApplicationService service = CreateService(runHistory, scheduler);
            StartExtractionRequest request = new(
                repositoryRoot,
                [Path.Combine("src", "CustomerSuite.txt")],
                BranchName: null,
                CommitSha: null,
                RequestedBy: null,
                Metadata: null);

            StartExtractionResult result = await service.StartAsync(request, CancellationToken.None);

            Assert.False(result.Accepted);
            Assert.Contains(result.ValidationErrors, error => error.Code == StartExtractionValidationErrorCodes.SolutionPathExtensionInvalid);
            Assert.Empty(await runHistory.GetRecentAsync(10, CancellationToken.None));
            Assert.Empty(scheduler.ScheduledRunIds);
        }

        /// <summary>
        /// Verifies duplicate solution paths are rejected after normalization collapses relative and absolute representations.
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenSolutionPathsNormalizeToDuplicate_ShouldRejectWithoutCreatingRun()
        {
            // Duplicate detection compares normalized absolute paths so callers cannot submit the same solution twice with different spelling.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "src", "CustomerSuite.sln");
            RecordingExtractionWorkScheduler scheduler = new();
            InMemoryExtractionRunHistory runHistory = new();
            StartExtractionApplicationService service = CreateService(runHistory, scheduler);
            StartExtractionRequest request = new(
                repositoryRoot,
                [Path.Combine("src", "CustomerSuite.sln"), solutionPath],
                BranchName: null,
                CommitSha: null,
                RequestedBy: null,
                Metadata: null);

            StartExtractionResult result = await service.StartAsync(request, CancellationToken.None);

            Assert.False(result.Accepted);
            Assert.Contains(result.ValidationErrors, error => error.Code == StartExtractionValidationErrorCodes.SolutionPathDuplicate);
            Assert.Empty(await runHistory.GetRecentAsync(10, CancellationToken.None));
            Assert.Empty(scheduler.ScheduledRunIds);
        }

        /// <summary>
        /// Verifies solution paths that resolve outside the submitted repository root are rejected.
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenSolutionPathIsOutsideRepositoryRoot_ShouldRejectWithoutCreatingRun()
        {
            // Outside-root rejection prevents a request from escaping the repository boundary through absolute paths.
            string repositoryRoot = CreateRepositoryRoot();
            string outsideRoot = CreateRepositoryRoot();
            string outsideSolutionPath = CreateSolutionFile(outsideRoot, "OtherSuite.sln");
            RecordingExtractionWorkScheduler scheduler = new();
            InMemoryExtractionRunHistory runHistory = new();
            StartExtractionApplicationService service = CreateService(runHistory, scheduler);
            StartExtractionRequest request = new(
                repositoryRoot,
                [outsideSolutionPath],
                BranchName: null,
                CommitSha: null,
                RequestedBy: null,
                Metadata: null);

            StartExtractionResult result = await service.StartAsync(request, CancellationToken.None);

            Assert.False(result.Accepted);
            Assert.Contains(result.ValidationErrors, error => error.Code == StartExtractionValidationErrorCodes.SolutionPathOutsideRepositoryRoot);
            Assert.Empty(await runHistory.GetRecentAsync(10, CancellationToken.None));
            Assert.Empty(scheduler.ScheduledRunIds);
        }

        /// <summary>
        /// Verifies status retrieval returns the current run when a run identifier exists in history.
        /// </summary>
        [Fact]
        public async Task GetStatusAsync_WhenRunExists_ShouldReturnRun()
        {
            // Status retrieval is the polling seam for the first asynchronous API slice.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln");
            InMemoryExtractionRunHistory runHistory = new();
            StartExtractionApplicationService service = CreateService(runHistory, new RecordingExtractionWorkScheduler());
            StartExtractionResult startResult = await service.StartAsync(
                new StartExtractionRequest(repositoryRoot, ["CustomerSuite.sln"], null, null, null, null),
                CancellationToken.None);

            ExtractionRun? status = await service.GetStatusAsync(startResult.Run!.RunId, CancellationToken.None);

            Assert.NotNull(status);
            Assert.Equal(startResult.Run.RunId, status.RunId);
            Assert.Equal(ExtractionRunStatus.Queued, status.Status);
        }

        /// <summary>
        /// Verifies status retrieval returns null when no run exists for the requested identifier.
        /// </summary>
        [Fact]
        public async Task GetStatusAsync_WhenRunIsMissing_ShouldReturnNull()
        {
            // Missing status lookup stays non-throwing so API translation can return a controlled not-found response.
            StartExtractionApplicationService service = CreateService(new InMemoryExtractionRunHistory(), new RecordingExtractionWorkScheduler());

            ExtractionRun? status = await service.GetStatusAsync(ExtractionRunId.New(), CancellationToken.None);

            Assert.Null(status);
        }

        /// <summary>
        /// Verifies recent run retrieval returns accepted runs in deterministic newest-first order.
        /// </summary>
        [Fact]
        public async Task GetRecentRunsAsync_WhenRunsExist_ShouldReturnNewestRunsFirst()
        {
            // The history query is the application surface used by GET /extractions, so ordering must remain stable for pollers and tests.
            string olderRepositoryRoot = CreateRepositoryRoot();
            string newerRepositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(olderRepositoryRoot, "OlderSuite.sln");
            CreateSolutionFile(newerRepositoryRoot, "NewerSuite.sln");
            InMemoryExtractionRunHistory runHistory = new();
            StartExtractionRequestValidator validator = new();
            await runHistory.CreateAsync(
                validator.Validate(new StartExtractionRequest(olderRepositoryRoot, ["OlderSuite.sln"], null, null, null, null)).ResolvedInput!,
                new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero),
                CancellationToken.None);
            ExtractionRun newerRun = await runHistory.CreateAsync(
                validator.Validate(new StartExtractionRequest(newerRepositoryRoot, ["NewerSuite.sln"], null, null, null, null)).ResolvedInput!,
                new DateTimeOffset(2025, 1, 2, 12, 0, 0, TimeSpan.Zero),
                CancellationToken.None);
            StartExtractionApplicationService service = CreateService(runHistory, new RecordingExtractionWorkScheduler());

            IReadOnlyList<ExtractionRun> recentRuns = await service.GetRecentRunsAsync(limit: 1, CancellationToken.None);

            Assert.Single(recentRuns);
            Assert.Equal(newerRun.RunId, recentRuns[0].RunId);
        }

        /// <summary>
        /// Verifies progress, warning, and error updates are visible through status retrieval after a run is accepted.
        /// </summary>
        [Fact]
        public async Task UpdateRunProgressAsync_WhenRunExists_ShouldExposeProgressWarningAndErrorThroughStatus()
        {
            // Progress updates are the seam future orchestration will use while the API polls the same operational run state.
            string repositoryRoot = CreateRepositoryRoot();
            CreateSolutionFile(repositoryRoot, "CustomerSuite.sln");
            StartExtractionApplicationService service = CreateService(new InMemoryExtractionRunHistory(), new RecordingExtractionWorkScheduler());
            StartExtractionResult startResult = await service.StartAsync(
                new StartExtractionRequest(repositoryRoot, ["CustomerSuite.sln"], null, null, null, null),
                CancellationToken.None);
            DateTimeOffset progressUpdatedUtc = new(2025, 1, 3, 12, 0, 0, TimeSpan.Zero);

            bool updated = await service.UpdateRunProgressAsync(
                startResult.Run!.RunId,
                ExtractionRunStatus.Running,
                new ExtractionRunProgress("Resolving", "Resolving submitted solution paths.", 25, progressUpdatedUtc),
                warnings: [new ExtractionRunWarning("Validation.Warning", "A non-blocking warning was recorded.", "Resolving", progressUpdatedUtc)],
                errors: [new ExtractionRunError("Extraction.Error", "A controlled error was recorded.", "Resolving", progressUpdatedUtc)],
                completedUtc: null,
                snapshotIdentity: null,
                CancellationToken.None);

            ExtractionRun? status = await service.GetStatusAsync(startResult.Run.RunId, CancellationToken.None);
            Assert.True(updated);
            Assert.NotNull(status);
            Assert.Equal(ExtractionRunStatus.Running, status.Status);
            Assert.Equal("Resolving", status.Progress.Stage);
            Assert.Equal(25, status.Progress.Percentage);
            Assert.Single(status.Warnings);
            Assert.Single(status.Errors);
        }

        /// <summary>
        /// Verifies progress updates return false when the requested run does not exist.
        /// </summary>
        [Fact]
        public async Task UpdateRunProgressAsync_WhenRunIsMissing_ShouldReturnFalse()
        {
            // Missing progress updates are non-throwing so future background workers can convert them into controlled diagnostics.
            StartExtractionApplicationService service = CreateService(new InMemoryExtractionRunHistory(), new RecordingExtractionWorkScheduler());

            bool updated = await service.UpdateRunProgressAsync(
                ExtractionRunId.New(),
                ExtractionRunStatus.Running,
                new ExtractionRunProgress("Missing", "No run exists for this update.", null, DateTimeOffset.UtcNow),
                warnings: null,
                errors: null,
                completedUtc: null,
                snapshotIdentity: null,
                CancellationToken.None);

            Assert.False(updated);
        }

        /// <summary>
        /// Creates a service instance with real validation and caller-supplied operational seams.
        /// </summary>
        /// <param name="runHistory">The run-history store used to verify run creation behavior.</param>
        /// <param name="scheduler">The scheduler seam used to verify accepted work dispatch.</param>
        /// <returns>A configured start extraction application service.</returns>
        private static StartExtractionApplicationService CreateService(
            IExtractionRunHistory runHistory,
            IExtractionWorkScheduler scheduler)
        {
            // Tests use the production validator with in-memory seams so validation and acceptance behavior are verified together.
            return new StartExtractionApplicationService(
                new StartExtractionRequestValidator(),
                runHistory,
                scheduler,
                NullLogger<StartExtractionApplicationService>.Instance);
        }

        /// <summary>
        /// Creates a temporary repository root directory for a test scenario.
        /// </summary>
        /// <returns>The absolute path to the created repository root.</returns>
        private string CreateRepositoryRoot()
        {
            // Each test receives an isolated root to keep path validation deterministic and side-effect free.
            string path = Path.Combine(Path.GetTempPath(), "archon-wp004-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            _temporaryDirectories.Add(path);
            return path;
        }

        /// <summary>
        /// Creates a placeholder solution file inside a repository root or nested path.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that owns the solution file.</param>
        /// <param name="pathParts">The nested path segments ending with the solution file name.</param>
        /// <returns>The absolute path to the created solution file.</returns>
        private static string CreateSolutionFile(string repositoryRoot, params string[] pathParts)
        {
            // The validator only needs existence and extension for Work Item 1, so an empty file is sufficient.
            string solutionPath = Path.Combine([repositoryRoot, .. pathParts]);
            Directory.CreateDirectory(Path.GetDirectoryName(solutionPath)!);
            File.WriteAllText(solutionPath, string.Empty);
            return solutionPath;
        }

        /// <summary>
        /// Records scheduled run identifiers without executing background work.
        /// </summary>
        private sealed class RecordingExtractionWorkScheduler : IExtractionWorkScheduler
        {
            /// <summary>
            /// Stores scheduled run identifiers in call order for assertions.
            /// </summary>
            private readonly List<ExtractionRunId> _scheduledRunIds = [];

            /// <summary>
            /// Gets the run identifiers scheduled by the service under test.
            /// </summary>
            internal IReadOnlyList<ExtractionRunId> ScheduledRunIds => _scheduledRunIds;

            /// <summary>
            /// Records the accepted run identifier that would be dispatched to asynchronous extraction work.
            /// </summary>
            /// <param name="runId">The accepted run identifier to schedule.</param>
            /// <param name="cancellationToken">The cancellation token for the scheduling request.</param>
            /// <returns>A completed task after the identifier is recorded.</returns>
            public Task ScheduleAsync(ExtractionRunId runId, CancellationToken cancellationToken)
            {
                // The recording scheduler proves scheduling happened without running future orchestration behavior in Work Item 1.
                _scheduledRunIds.Add(runId);
                return Task.CompletedTask;
            }
        }
    }
}
