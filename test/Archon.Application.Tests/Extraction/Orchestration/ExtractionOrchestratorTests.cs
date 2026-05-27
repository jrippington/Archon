using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Orchestration;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Requests;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Application.Extraction.Snapshots;
using Archon.Application.Extraction.Validation;
using Archon.Application.Graph.Persistence;
using Archon.Domain.Graph.ControlledValues;
using Archon.Extractors.Projects.Solutions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Archon.Application.Tests.Extraction.Orchestration
{
    /// <summary>
    /// Verifies asynchronous extraction orchestration updates run lifecycle state and hands complete snapshots to persistence.
    /// </summary>
    public sealed class ExtractionOrchestratorTests : IDisposable
    {
        /// <summary>
        /// Tracks temporary repository roots created for start-path validation.
        /// </summary>
        private readonly List<string> _temporaryDirectories = [];

        /// <summary>
        /// Deletes temporary repository directories created by orchestration tests.
        /// </summary>
        public void Dispose()
        {
            // Tests create real paths only for the start validation boundary; orchestration itself uses resolved input from run history.
            foreach (string temporaryDirectory in _temporaryDirectories)
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies successful orchestration runs the pipeline, persists the full snapshot, and records completion only after persistence succeeds.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenPersistenceSucceeds_ShouldPersistSnapshotAndCompleteRun()
        {
            // The success path proves the full Work Item 4 order from queued run through running progress, pipeline, assembly, persistence, and completion.
            InMemoryExtractionRunHistory runHistory = new();
            ResolvedExtractionInput input = CreateResolvedInput();
            ExtractionRun run = await runHistory.CreateAsync(input, new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero), CancellationToken.None);
            RecordingStage stage = new("placeholder-test", warning: "Placeholder warning retained.", blockingError: null);
            RecordingSnapshotWriter writer = RecordingSnapshotWriter.Success("snapshot://persisted");
            ExtractionOrchestrator orchestrator = CreateOrchestrator(runHistory, [stage], writer);

            bool executed = await orchestrator.ExecuteAsync(run.RunId, CancellationToken.None);

            ExtractionRun completedRun = (await runHistory.GetAsync(run.RunId, CancellationToken.None))!;
            Assert.True(executed);
            Assert.True(stage.Executed);
            Assert.NotNull(writer.WrittenSnapshot);
            Assert.Equal(1, writer.WriteCount);
            Assert.Equal(ExtractionRunStatus.Completed, completedRun.Status);
            Assert.Equal("snapshot://persisted", completedRun.SnapshotIdentity);
            Assert.Contains(completedRun.Warnings, warning => warning.Message == "Placeholder warning retained.");
            Assert.Empty(completedRun.Errors);
            Assert.NotNull(writer.WrittenSnapshot.SnapshotHeader);
            Assert.Single(writer.WrittenSnapshot.Repositories);
            Assert.Single(writer.WrittenSnapshot.Solutions);
            Assert.Empty(writer.WrittenSnapshot.Nodes);
            Assert.Empty(writer.WrittenSnapshot.Edges);
            Assert.Empty(writer.WrittenSnapshot.Evidence);
            Assert.Empty(writer.WrittenSnapshot.Rules);
            Assert.Empty(writer.WrittenSnapshot.Findings);
            Assert.Empty(writer.WrittenSnapshot.Metrics);
            Assert.Empty(writer.WrittenSnapshot.GeneratedSummaries);
            Assert.Contains("Placeholder warning retained.", writer.WrittenSnapshot.Warnings);
            Assert.Empty(writer.WrittenSnapshot.Errors);
        }

        /// <summary>
        /// Verifies successful persistence diagnostics survive the in-memory lifecycle path without replacing top-level timings.
        /// </summary>
        /// <returns>A task that completes after completed-run diagnostics and timings have been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenPersistenceSucceedsWithDiagnostics_ShouldExposeDiagnosticsWithoutFlatteningDetailedTimings()
        {
            // The in-memory run history is the smallest status path for WP016: the writer returns diagnostics, orchestration attaches them, and status retrieval reads them back.
            InMemoryExtractionRunHistory runHistory = new();
            ResolvedExtractionInput input = CreateResolvedInput();
            ExtractionRun run = await runHistory.CreateAsync(input, DateTimeOffset.UtcNow, CancellationToken.None);
            RecordingSnapshotWriter writer = RecordingSnapshotWriter.Success(
                "snapshot://diagnostics-success",
                CreatePersistenceDiagnostics(
                    completed: true,
                    [
                        new ExtractionRunTiming("Persistence.PrepareSnapshot", 12, new DateTimeOffset(2026, 5, 27, 8, 0, 1, TimeSpan.Zero)),
                        new ExtractionRunTiming("Persistence.WriteWarnings", 34, new DateTimeOffset(2026, 5, 27, 8, 0, 2, TimeSpan.Zero)),
                        new ExtractionRunTiming("Persistence.Commit", 56, new DateTimeOffset(2026, 5, 27, 8, 0, 3, TimeSpan.Zero)),
                        new ExtractionRunTiming("Persistence.Total", 102, new DateTimeOffset(2026, 5, 27, 8, 0, 4, TimeSpan.Zero))
                    ]));
            ExtractionOrchestrator orchestrator = CreateOrchestrator(runHistory, [new RecordingStage("stage", null, null)], writer);

            bool executed = await orchestrator.ExecuteAsync(run.RunId, CancellationToken.None);

            ExtractionRun completedRun = (await runHistory.GetAsync(run.RunId, CancellationToken.None))!;
            Assert.True(executed);
            Assert.Equal(ExtractionRunStatus.Completed, completedRun.Status);
            Assert.NotNull(completedRun.PersistenceDiagnostics);
            Assert.True(completedRun.PersistenceDiagnostics.Completed);
            Assert.Equal(4, completedRun.PersistenceDiagnostics.Timings.Count);
            Assert.Equal("Persistence.PrepareSnapshot", completedRun.PersistenceDiagnostics.Timings[0].Stage);
            Assert.Equal(1, completedRun.PersistenceDiagnostics.Counts.RepositoryCount);
            Assert.Equal(0, completedRun.PersistenceDiagnostics.Counts.ProjectCount);
            Assert.Equal(0, completedRun.PersistenceDiagnostics.Counts.WarningCount);
            Assert.Contains(completedRun.Timings, timing => timing.Stage == "Persistence");
            Assert.DoesNotContain(completedRun.Timings, timing => timing.Stage == "Persistence.PrepareSnapshot");
            Assert.DoesNotContain(completedRun.Timings, timing => timing.Stage == "Persistence.Total");
        }

        /// <summary>
        /// Verifies orchestration hands WP005 repository and solution graph contributions to snapshot persistence through the existing application path.
        /// </summary>
        /// <returns>A task that completes after the persisted snapshot shape has been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenProjectExtractionStageRuns_ShouldPersistRepositorySolutionFactsAndEvidence()
        {
            // This test protects the end-to-end handoff shape without starting the Aspire AppHost or writing to Neo4j.
            InMemoryExtractionRunHistory runHistory = new();
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "CustomerSuite.sln", "Customer.Api", "Customer.Api.csproj");
            CreateProjectFile(repositoryRoot, "Customer.Api.csproj");
            ResolvedExtractionInput input = new(
                repositoryRoot,
                [solutionPath],
                BranchName: "main",
                CommitSha: "abcdef1234567890",
                RequestedBy: "developer@example.invalid",
                Metadata: new Dictionary<string, string>
                {
                    ["source"] = "wp005-orchestration-test"
                });
            ExtractionRun run = await runHistory.CreateAsync(input, new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero), CancellationToken.None);
            RecordingSnapshotWriter writer = RecordingSnapshotWriter.Success("snapshot://wp005-persisted");
            ExtractionOrchestrator orchestrator = CreateOrchestrator(runHistory, [new RepositorySolutionExtractionStage()], writer);

            bool executed = await orchestrator.ExecuteAsync(run.RunId, CancellationToken.None);

            ExtractedArchitectureSnapshot snapshot = writer.WrittenSnapshot!;
            Assert.True(executed);
            Assert.Equal(1, writer.WriteCount);
            Assert.NotNull(snapshot.SnapshotHeader);
            Assert.Single(snapshot.Repositories);
            Assert.Single(snapshot.Solutions);
            Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Repository);
            Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Solution && node.StableKey.Value == "solution://CustomerSuite.sln");
            Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Contains && edge.TargetNodeStableKey.Value == "solution://CustomerSuite.sln");
            Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "CustomerSuite.sln" && evidence.SymbolName == "Customer.Api");
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies controlled pipeline failures stop persistence and mark the accepted run as failed.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenPipelineReturnsBlockingError_ShouldFailRunWithoutPersistence()
        {
            // Pipeline blocking errors are controlled failures and must be visible through run status without writing partial snapshots.
            InMemoryExtractionRunHistory runHistory = new();
            ResolvedExtractionInput input = CreateResolvedInput();
            ExtractionRun run = await runHistory.CreateAsync(input, DateTimeOffset.UtcNow, CancellationToken.None);
            RecordingStage stage = new("failing-stage", warning: null, blockingError: "The placeholder pipeline failed safely.");
            RecordingSnapshotWriter writer = RecordingSnapshotWriter.Success("snapshot://should-not-write");
            ExtractionOrchestrator orchestrator = CreateOrchestrator(runHistory, [stage], writer);

            bool executed = await orchestrator.ExecuteAsync(run.RunId, CancellationToken.None);

            ExtractionRun failedRun = (await runHistory.GetAsync(run.RunId, CancellationToken.None))!;
            Assert.False(executed);
            Assert.Null(writer.WrittenSnapshot);
            Assert.Equal(0, writer.WriteCount);
            Assert.Equal(ExtractionRunStatus.Failed, failedRun.Status);
            Assert.Contains(failedRun.Errors, error => error.Stage == "failing-stage" && error.Message == "The placeholder pipeline failed safely.");
        }

        /// <summary>
        /// Verifies persistence failures mark the run failed and expose the persistence diagnostic without reporting completion early.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenPersistenceFails_ShouldRecordFailedStatusAndError()
        {
            // Persistence failure happens after assembly, so the run must remain failed rather than completed even though a full snapshot existed.
            InMemoryExtractionRunHistory runHistory = new();
            ResolvedExtractionInput input = CreateResolvedInput();
            ExtractionRun run = await runHistory.CreateAsync(input, DateTimeOffset.UtcNow, CancellationToken.None);
            RecordingSnapshotWriter writer = RecordingSnapshotWriter.Failure("snapshot://failed", "Persistence write failed safely.");
            ExtractionOrchestrator orchestrator = CreateOrchestrator(runHistory, [new RecordingStage("stage", null, null)], writer);

            bool executed = await orchestrator.ExecuteAsync(run.RunId, CancellationToken.None);

            ExtractionRun failedRun = (await runHistory.GetAsync(run.RunId, CancellationToken.None))!;
            Assert.False(executed);
            Assert.NotNull(writer.WrittenSnapshot);
            Assert.Equal(1, writer.WriteCount);
            Assert.Equal(ExtractionRunStatus.Failed, failedRun.Status);
            Assert.Null(failedRun.SnapshotIdentity);
            Assert.Contains(failedRun.Errors, error => error.Stage == "SnapshotPersistence" && error.Message == "Persistence write failed safely.");
        }

        /// <summary>
        /// Verifies failed persistence can retain partial diagnostics without reporting completion.
        /// </summary>
        /// <returns>A task that completes after failed-run diagnostics have been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenPersistenceFailsWithPartialDiagnostics_ShouldRetainDiagnosticsAndRemainFailed()
        {
            // Partial diagnostics are most useful on failures, so orchestration must attach them before exposing the terminal failed state.
            InMemoryExtractionRunHistory runHistory = new();
            ResolvedExtractionInput input = CreateResolvedInput();
            ExtractionRun run = await runHistory.CreateAsync(input, DateTimeOffset.UtcNow, CancellationToken.None);
            RecordingSnapshotWriter writer = RecordingSnapshotWriter.Failure(
                "snapshot://failed-diagnostics",
                "Persistence write failed safely.",
                CreatePersistenceDiagnostics(
                    completed: false,
                    [
                        new ExtractionRunTiming("Persistence.PrepareSnapshot", 11, new DateTimeOffset(2026, 5, 27, 9, 0, 1, TimeSpan.Zero)),
                        new ExtractionRunTiming("Persistence.WriteWarnings", 22, new DateTimeOffset(2026, 5, 27, 9, 0, 2, TimeSpan.Zero))
                    ]));
            ExtractionOrchestrator orchestrator = CreateOrchestrator(runHistory, [new RecordingStage("stage", null, null)], writer);

            bool executed = await orchestrator.ExecuteAsync(run.RunId, CancellationToken.None);

            ExtractionRun failedRun = (await runHistory.GetAsync(run.RunId, CancellationToken.None))!;
            Assert.False(executed);
            Assert.Equal(ExtractionRunStatus.Failed, failedRun.Status);
            Assert.Null(failedRun.SnapshotIdentity);
            Assert.NotNull(failedRun.PersistenceDiagnostics);
            Assert.False(failedRun.PersistenceDiagnostics.Completed);
            Assert.Equal(2, failedRun.PersistenceDiagnostics.Timings.Count);
            Assert.Contains(failedRun.Errors, error => error.Stage == "SnapshotPersistence");
        }

        /// <summary>
        /// Verifies persistence warnings returned with diagnostics append to existing run warnings and do not remove detailed timings.
        /// </summary>
        /// <returns>A task that completes after warning merge and diagnostic preservation have been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenPersistenceSucceedsWithWarningsAndDiagnostics_ShouldMergeWarningsAndRetainDiagnostics()
        {
            // Pipeline and persistence warnings travel through different seams; the terminal update must append both without replacing diagnostics.
            InMemoryExtractionRunHistory runHistory = new();
            ResolvedExtractionInput input = CreateResolvedInput();
            ExtractionRun run = await runHistory.CreateAsync(input, DateTimeOffset.UtcNow, CancellationToken.None);
            ExtractionRunPersistenceDiagnostics diagnostics = CreatePersistenceDiagnostics(
                completed: true,
                [
                    new ExtractionRunTiming("Persistence.PrepareSnapshot", 11, new DateTimeOffset(2026, 5, 27, 10, 0, 1, TimeSpan.Zero)),
                    new ExtractionRunTiming("Persistence.Commit", 22, new DateTimeOffset(2026, 5, 27, 10, 0, 2, TimeSpan.Zero)),
                    new ExtractionRunTiming("Persistence.Total", 33, new DateTimeOffset(2026, 5, 27, 10, 0, 3, TimeSpan.Zero))
                ]);
            RecordingSnapshotWriter writer = RecordingSnapshotWriter.Success(
                "snapshot://warning-diagnostics-success",
                diagnostics,
                [new PersistenceWarning(PersistenceStage.SnapshotPersistence, "DiagnosticTimingWarning", "A diagnostic timing could not be recorded safely.")]);
            ExtractionOrchestrator orchestrator = CreateOrchestrator(runHistory, [new RecordingStage("stage", "Pipeline warning retained.", null)], writer);

            bool executed = await orchestrator.ExecuteAsync(run.RunId, CancellationToken.None);

            ExtractionRun completedRun = (await runHistory.GetAsync(run.RunId, CancellationToken.None))!;
            Assert.True(executed);
            Assert.Equal(ExtractionRunStatus.Completed, completedRun.Status);
            Assert.Contains(completedRun.Warnings, warning => warning.Code == "PipelineWarning" && warning.Message == "Pipeline warning retained.");
            Assert.Contains(completedRun.Warnings, warning => warning.Code == "DiagnosticTimingWarning" && warning.Message == "A diagnostic timing could not be recorded safely.");
            Assert.NotNull(completedRun.PersistenceDiagnostics);
            Assert.True(completedRun.PersistenceDiagnostics.Completed);
            Assert.Equal(3, completedRun.PersistenceDiagnostics.Timings.Count);
            Assert.Contains(completedRun.Timings, timing => timing.Stage == "Persistence");
            Assert.Contains(completedRun.Timings, timing => timing.Stage == "Total");
            Assert.DoesNotContain(completedRun.Errors, error => error.Message.Contains("diagnostic", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Verifies failed persistence without collected sub-stage diagnostics remains compatible and still records top-level persistence progress.
        /// </summary>
        /// <returns>A task that completes after no-diagnostic failure compatibility has been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenPersistenceFailsBeforeDiagnosticsExist_ShouldFailWithoutDiagnosticSection()
        {
            // Some older writers or early-failure adapters cannot provide diagnostics; the lifecycle must still expose a safe failed run.
            InMemoryExtractionRunHistory runHistory = new();
            ResolvedExtractionInput input = CreateResolvedInput();
            ExtractionRun run = await runHistory.CreateAsync(input, DateTimeOffset.UtcNow, CancellationToken.None);
            RecordingSnapshotWriter writer = RecordingSnapshotWriter.Failure("snapshot://failed-without-diagnostics", "Persistence failed before diagnostic capture started.");
            ExtractionOrchestrator orchestrator = CreateOrchestrator(runHistory, [new RecordingStage("stage", null, null)], writer);

            bool executed = await orchestrator.ExecuteAsync(run.RunId, CancellationToken.None);

            ExtractionRun failedRun = (await runHistory.GetAsync(run.RunId, CancellationToken.None))!;
            Assert.False(executed);
            Assert.Equal(ExtractionRunStatus.Failed, failedRun.Status);
            Assert.Equal("Persistence", failedRun.Progress.Stage);
            Assert.Equal("Snapshot persistence failed.", failedRun.Progress.Message);
            Assert.Equal(100, failedRun.Progress.Percentage);
            Assert.Null(failedRun.PersistenceDiagnostics);
            Assert.Contains(failedRun.Timings, timing => timing.Stage == "Persistence");
            Assert.Contains(failedRun.Timings, timing => timing.Stage == "Total");
            Assert.Contains(failedRun.Errors, error => error.Stage == "SnapshotPersistence" && error.Message == "Persistence failed before diagnostic capture started.");
        }

        /// <summary>
        /// Verifies older or manually seeded run records without persistence diagnostics remain readable through run history.
        /// </summary>
        /// <returns>A task that completes after a diagnostics-free status record has been asserted.</returns>
        [Fact]
        public async Task GetAsync_WhenRunHasNoPersistenceDiagnostics_ShouldReturnReadableRunWithoutDiagnostics()
        {
            // Compatibility requires the optional diagnostics section to stay absent for runs created before WP016 data existed.
            InMemoryExtractionRunHistory runHistory = new();
            ExtractionRun run = await runHistory.CreateAsync(CreateResolvedInput(), DateTimeOffset.UtcNow, CancellationToken.None);

            ExtractionRun? status = await runHistory.GetAsync(run.RunId, CancellationToken.None);

            Assert.NotNull(status);
            Assert.Null(status.PersistenceDiagnostics);
            Assert.Empty(status.Timings);
        }

        /// <summary>
        /// Verifies unexpected exceptions are converted into controlled failed run diagnostics.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenStageThrows_ShouldRecordControlledFailureWithoutStackTrace()
        {
            // Unexpected exceptions must not leak stack traces through run status, but the failing stage remains identifiable.
            InMemoryExtractionRunHistory runHistory = new();
            ResolvedExtractionInput input = CreateResolvedInput();
            ExtractionRun run = await runHistory.CreateAsync(input, DateTimeOffset.UtcNow, CancellationToken.None);
            ThrowingStage stage = new();
            RecordingSnapshotWriter writer = RecordingSnapshotWriter.Success("snapshot://should-not-write");
            ExtractionOrchestrator orchestrator = CreateOrchestrator(runHistory, [stage], writer);

            bool executed = await orchestrator.ExecuteAsync(run.RunId, CancellationToken.None);

            ExtractionRun failedRun = (await runHistory.GetAsync(run.RunId, CancellationToken.None))!;
            Assert.False(executed);
            Assert.Null(writer.WrittenSnapshot);
            Assert.Equal(0, writer.WriteCount);
            Assert.Equal(ExtractionRunStatus.Failed, failedRun.Status);
            ExtractionRunError error = Assert.Single(failedRun.Errors);
            Assert.Equal("UnexpectedFailure", error.Code);
            Assert.Equal("Orchestration", error.Stage);
            Assert.DoesNotContain(" at ", error.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies validation failures do not create accepted runs and therefore cannot reach persistence.
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenValidationFails_ShouldNotInvokePersistence()
        {
            // This regression protects the boundary that orchestration only starts after the start request has been accepted.
            InMemoryExtractionRunHistory runHistory = new();
            RecordingSnapshotWriter writer = RecordingSnapshotWriter.Success("snapshot://not-used");
            StartExtractionApplicationService startService = new(
                new StartExtractionRequestValidator(),
                runHistory,
                new RecordingScheduler(),
                NullLogger<StartExtractionApplicationService>.Instance);

            StartExtractionResult result = await startService.StartAsync(
                new StartExtractionRequest(" ", ["Missing.sln"], null, null, null, null),
                CancellationToken.None);

            Assert.False(result.Accepted);
            Assert.Null(result.Run);
            Assert.Null(writer.WrittenSnapshot);
            Assert.Equal(0, writer.WriteCount);
            Assert.Empty(await runHistory.GetRecentAsync(10, CancellationToken.None));
        }

        /// <summary>
        /// Creates an orchestrator using real assembly and caller-supplied test doubles for stages and persistence.
        /// </summary>
        /// <param name="runHistory">The run history store shared by the orchestrator and assertions.</param>
        /// <param name="stages">The stages to execute in the pipeline.</param>
        /// <param name="writer">The persistence writer test double.</param>
        /// <returns>A configured extraction orchestrator.</returns>
        private static ExtractionOrchestrator CreateOrchestrator(
            IExtractionRunHistory runHistory,
            IReadOnlyList<IExtractionStage> stages,
            IArchitectureSnapshotWriter writer)
        {
            // The production pipeline runner and assembler are used so tests cover the real Work Item 4 application path.
            return new ExtractionOrchestrator(
                runHistory,
                new ExtractionPipelineRunner(stages, NullLogger<ExtractionPipelineRunner>.Instance),
                new ExtractionSnapshotAssembler(),
                writer,
                NullLogger<ExtractionOrchestrator>.Instance);
        }

        /// <summary>
        /// Creates normalized resolved input for orchestration tests.
        /// </summary>
        /// <returns>A resolved input with one repository and one solution.</returns>
        private static ResolvedExtractionInput CreateResolvedInput()
        {
            // Orchestration tests consume already-accepted run context and do not need additional filesystem validation.
            return new ResolvedExtractionInput(
                "D:/Repositories/CustomerSuite/",
                ["D:/Repositories/CustomerSuite/CustomerSuite.sln"],
                "main",
                "abcdef1234567890",
                "developer@example.invalid",
                new Dictionary<string, string>
                {
                    ["source"] = "orchestration-test"
                });
        }

        /// <summary>
        /// Creates an isolated temporary repository root for orchestration tests that need real submitted files.
        /// </summary>
        /// <returns>The absolute temporary repository root path.</returns>
        private string CreateRepositoryRoot()
        {
            // Temporary repository roots let the real WP005 stage read solution files while keeping tests isolated and disposable.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-wp005-application-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repositoryRoot);
            _temporaryDirectories.Add(repositoryRoot);
            return repositoryRoot;
        }

        /// <summary>
        /// Creates a minimal Visual Studio solution file under a temporary repository root.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that should contain the solution file.</param>
        /// <param name="relativeSolutionPath">The repository-relative solution path to write.</param>
        /// <param name="projectName">The project name declared by the solution file.</param>
        /// <param name="projectPath">The project path declared by the solution file.</param>
        /// <returns>The absolute solution path written to disk.</returns>
        private static string CreateSolutionFile(string repositoryRoot, string relativeSolutionPath, string projectName, string projectPath)
        {
            // Orchestration tests use real solution content so the real WP005 stage can parse evidence before persistence.
            string solutionPath = Path.Combine(repositoryRoot, relativeSolutionPath);
            Directory.CreateDirectory(Path.GetDirectoryName(solutionPath)!);
            File.WriteAllText(
                solutionPath,
                string.Join(
                    Environment.NewLine,
                    [
                        "Microsoft Visual Studio Solution File, Format Version 12.00",
                        "# Visual Studio Version 17",
                        $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{projectName}\", \"{projectPath}\", \"{{33333333-3333-3333-3333-333333333333}}\"",
                        "EndProject",
                        "Global",
                        "EndGlobal"
                    ]));
            return solutionPath;
        }

        /// <summary>
        /// Creates a minimal SDK-style project file for orchestration fixtures that exercise the real project extraction stage.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that contains the project file.</param>
        /// <param name="relativeProjectPath">The repository-relative project path to write.</param>
        private static void CreateProjectFile(string repositoryRoot, string relativeProjectPath)
        {
            // The real WP005 stage now reads supported project declarations, so orchestration fixtures need matching project files.
            string projectPath = Path.Combine(repositoryRoot, relativeProjectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(
                projectPath,
                string.Join(
                    Environment.NewLine,
                    [
                        "<Project Sdk=\"Microsoft.NET.Sdk\">",
                        "  <PropertyGroup>",
                        "    <TargetFramework>net10.0</TargetFramework>",
                        "  </PropertyGroup>",
                        "</Project>"
                    ]));
        }

        /// <summary>
        /// Records scheduled run identifiers without executing orchestration.
        /// </summary>
        private sealed class RecordingScheduler : Archon.Application.Extraction.Scheduling.IExtractionWorkScheduler
        {
            /// <summary>
            /// Accepts a scheduled run id without side effects.
            /// </summary>
            /// <param name="runId">The accepted run id.</param>
            /// <param name="cancellationToken">The cancellation token for scheduling.</param>
            /// <returns>A completed scheduling task.</returns>
            public Task ScheduleAsync(ExtractionRunId runId, CancellationToken cancellationToken)
            {
                // Validation-failure tests assert this scheduler is never called through absence of accepted runs.
                cancellationToken.ThrowIfCancellationRequested();
                _ = runId;
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Provides a configurable extraction stage for orchestration tests.
        /// </summary>
        private sealed class RecordingStage : IExtractionStage
        {
            /// <summary>
            /// Stores the optional warning emitted by the stage.
            /// </summary>
            private readonly string? _warning;

            /// <summary>
            /// Stores the optional blocking error emitted by the stage.
            /// </summary>
            private readonly string? _blockingError;

            /// <summary>
            /// Initializes a new instance of the <see cref="RecordingStage"/> class.
            /// </summary>
            /// <param name="stageId">The stable stage identifier.</param>
            /// <param name="warning">The optional warning to add to accumulation.</param>
            /// <param name="blockingError">The optional blocking error to return.</param>
            internal RecordingStage(string stageId, string? warning, string? blockingError)
            {
                // This stage keeps orchestration tests focused on lifecycle behavior rather than stage internals.
                StageId = stageId;
                _warning = warning;
                _blockingError = blockingError;
            }

            /// <summary>
            /// Gets a value indicating whether the stage was executed.
            /// </summary>
            internal bool Executed { get; private set; }

            /// <summary>
            /// Gets the stable stage identifier.
            /// </summary>
            public string StageId { get; }

            /// <summary>
            /// Adds configured diagnostics to accumulation and returns the configured stage result.
            /// </summary>
            /// <param name="context">The stage execution context.</param>
            /// <param name="cancellationToken">The cancellation token for stage execution.</param>
            /// <returns>A successful or blocking stage result.</returns>
            public Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
            {
                // Recording execution before returning lets tests prove the orchestrator invoked the pipeline.
                cancellationToken.ThrowIfCancellationRequested();
                Executed = true;

                if (!string.IsNullOrWhiteSpace(_warning))
                {
                    context.Accumulation.AddWarning(_warning);
                }

                return Task.FromResult(string.IsNullOrWhiteSpace(_blockingError)
                    ? ExtractionStageResult.Success()
                    : ExtractionStageResult.BlockingError(_blockingError));
            }
        }

        /// <summary>
        /// Throws during execution to verify unexpected exception handling.
        /// </summary>
        private sealed class ThrowingStage : IExtractionStage
        {
            /// <summary>
            /// Gets the stable stage identifier.
            /// </summary>
            public string StageId => "throwing-stage";

            /// <summary>
            /// Throws a deterministic exception for failure-handling tests.
            /// </summary>
            /// <param name="context">The stage execution context.</param>
            /// <param name="cancellationToken">The cancellation token for stage execution.</param>
            /// <returns>This method never returns successfully.</returns>
            public Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
            {
                // The message is intentionally sensitive-looking so assertions can prove the API-safe error does not echo raw details.
                _ = context;
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Sensitive connection string Server=secret should not leak.");
            }
        }

        /// <summary>
        /// Records snapshots handed to persistence and returns configured persistence results.
        /// </summary>
        private sealed class RecordingSnapshotWriter : IArchitectureSnapshotWriter
        {
            /// <summary>
            /// Stores the result returned from persistence calls.
            /// </summary>
            private readonly SnapshotPersistenceResult _result;

            /// <summary>
            /// Initializes a new instance of the <see cref="RecordingSnapshotWriter"/> class.
            /// </summary>
            /// <param name="result">The persistence result to return.</param>
            private RecordingSnapshotWriter(SnapshotPersistenceResult result)
            {
                // The writer keeps the snapshot for assertions and avoids any real Neo4j dependency.
                _result = result;
            }

            /// <summary>
            /// Gets the snapshot most recently handed to persistence.
            /// </summary>
            internal ExtractedArchitectureSnapshot? WrittenSnapshot { get; private set; }

            /// <summary>
            /// Gets the number of times the orchestrator invoked the persistence writer.
            /// </summary>
            internal int WriteCount { get; private set; }

            /// <summary>
            /// Creates a writer that returns successful persistence.
            /// </summary>
            /// <param name="snapshotStableKey">The stable snapshot identity to report.</param>
            /// <param name="diagnostics">The optional persistence diagnostics to return with the simulated success result.</param>
            /// <param name="warnings">The optional persistence warnings to return with the simulated success result.</param>
            /// <returns>A successful recording writer.</returns>
            internal static RecordingSnapshotWriter Success(
                string snapshotStableKey,
                ExtractionRunPersistenceDiagnostics? diagnostics = null,
                IReadOnlyList<PersistenceWarning>? warnings = null)
            {
                // Counts match the minimal placeholder snapshot shape used by orchestration tests.
                return new RecordingSnapshotWriter(SnapshotPersistenceResult.Success(
                    snapshotStableKey,
                    new SnapshotPersistenceCounts(1, 1, 1, 0, 0, 0, 1, 0, 0, 0),
                    warnings,
                    diagnostics: diagnostics));
            }

            /// <summary>
            /// Creates a writer that returns failed persistence.
            /// </summary>
            /// <param name="snapshotStableKey">The stable snapshot identity being persisted.</param>
            /// <param name="message">The safe persistence error message.</param>
            /// <param name="diagnostics">The optional partial persistence diagnostics to return with the simulated failure result.</param>
            /// <returns>A failing recording writer.</returns>
            internal static RecordingSnapshotWriter Failure(string snapshotStableKey, string message, ExtractionRunPersistenceDiagnostics? diagnostics = null)
            {
                // The failure result mirrors infrastructure adapters translating database failures into application-owned diagnostics.
                return new RecordingSnapshotWriter(SnapshotPersistenceResult.Failure(
                    snapshotStableKey,
                    new PersistenceError(PersistenceStage.SnapshotPersistence, "PersistenceFailed", message),
                    diagnostics: diagnostics));
            }

            /// <summary>
            /// Records the snapshot and returns the configured persistence result.
            /// </summary>
            /// <param name="snapshot">The assembled snapshot to record.</param>
            /// <param name="cancellationToken">The cancellation token for persistence.</param>
            /// <returns>The configured persistence result.</returns>
            public Task<SnapshotPersistenceResult> WriteSnapshotAsync(ExtractedArchitectureSnapshot snapshot, CancellationToken cancellationToken = default)
            {
                // Recording the exact snapshot proves orchestration hands off the generalized contract rather than a projection.
                cancellationToken.ThrowIfCancellationRequested();
                WriteCount++;
                WrittenSnapshot = snapshot;
                return Task.FromResult(_result);
            }
        }

        /// <summary>
        /// Creates persistence diagnostics with stable counts for orchestration status tests.
        /// </summary>
        /// <param name="completed">A value indicating whether the diagnostics represent a completed persistence attempt.</param>
        /// <param name="timings">The ordered persistence sub-stage timings to expose.</param>
        /// <returns>A persistence diagnostic container for a test persistence result.</returns>
        private static ExtractionRunPersistenceDiagnostics CreatePersistenceDiagnostics(bool completed, IReadOnlyList<ExtractionRunTiming> timings)
        {
            // The count values intentionally include zeroes so tests protect the known-empty collection convention from regressing to null.
            return new ExtractionRunPersistenceDiagnostics(
                timings,
                new ExtractionRunPersistenceCounts(
                    RepositoryCount: 1,
                    SolutionCount: 1,
                    ProjectCount: 0,
                    FileCount: 0,
                    NodeCount: 0,
                    RelationshipCount: 0,
                    EvidenceCount: 0,
                    FindingCount: 0,
                    WarningCount: 0,
                    ErrorCount: 0,
                    MetricCount: 0,
                    GeneratedSummaryCount: 0,
                    MetadataEntryCount: null,
                    PersistenceOperationCount: 0,
                    PersistenceBatchCount: 0,
                    SerializedPayloadBytes: null),
                completed);
        }
    }
}
