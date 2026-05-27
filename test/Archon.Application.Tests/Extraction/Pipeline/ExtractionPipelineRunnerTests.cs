using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Requests;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Archon.Application.Tests.Extraction.Pipeline
{
    /// <summary>
    /// Verifies the WP004 extraction pipeline runner executes deterministic stages and records progress-compatible diagnostics.
    /// </summary>
    public sealed class ExtractionPipelineRunnerTests
    {
        /// <summary>
        /// Verifies stages execute in the order supplied to the pipeline runner.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenStagesSucceed_ShouldRunStagesInDeterministicOrder()
        {
            // The runner is sequential by design for WP004 so future extractors can reason about accumulation order and progress updates.
            List<string> executionOrder = [];
            ResolvedExtractionInput input = CreateResolvedInput();
            ExtractionRun run = CreateRun(input);
            ExtractionPipelineRunner runner = new(
                [
                    new RecordingExtractionStage("stage-a", executionOrder, shouldBlock: false),
                    new RecordingExtractionStage("stage-b", executionOrder, shouldBlock: false)
                ],
                NullLogger<ExtractionPipelineRunner>.Instance);

            ExtractionPipelineResult result = await runner.ExecuteAsync(input, run, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(["stage-a", "stage-b"], executionOrder);
            Assert.Empty(result.Accumulation.ToSnapshot().Errors);
        }

        /// <summary>
        /// Verifies a blocking stage error stops later stages and records the error in the accumulation output.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenStageReturnsBlockingError_ShouldStopBeforeLaterStages()
        {
            // Blocking errors represent failures that make later extraction facts unreliable, so the runner must stop immediately.
            List<string> executionOrder = [];
            ResolvedExtractionInput input = CreateResolvedInput();
            ExtractionRun run = CreateRun(input);
            ExtractionPipelineRunner runner = new(
                [
                    new RecordingExtractionStage("stage-a", executionOrder, shouldBlock: true),
                    new RecordingExtractionStage("stage-b", executionOrder, shouldBlock: false)
                ],
                NullLogger<ExtractionPipelineRunner>.Instance);

            ExtractionPipelineResult result = await runner.ExecuteAsync(input, run, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(["stage-a"], executionOrder);
            Assert.Contains("stage-a failed with a blocking test error.", result.Accumulation.ToSnapshot().Errors);
        }

        /// <summary>
        /// Verifies warning diagnostics remain non-blocking and are retained in the pipeline accumulation.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenStageReturnsWarning_ShouldContinueAndRetainWarning()
        {
            // Non-blocking warnings explain degraded extraction confidence without preventing later stages from contributing facts.
            List<string> executionOrder = [];
            ResolvedExtractionInput input = CreateResolvedInput();
            ExtractionRun run = CreateRun(input);
            ExtractionPipelineRunner runner = new(
                [
                    new WarningExtractionStage("warning-stage", executionOrder),
                    new RecordingExtractionStage("stage-b", executionOrder, shouldBlock: false)
                ],
                NullLogger<ExtractionPipelineRunner>.Instance);

            ExtractionPipelineResult result = await runner.ExecuteAsync(input, run, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(["warning-stage", "stage-b"], executionOrder);
            Assert.Contains("warning-stage emitted a non-blocking test warning.", result.Accumulation.ToSnapshot().Warnings);
        }

        /// <summary>
        /// Creates a normalized resolved extraction input suitable for pipeline tests.
        /// </summary>
        /// <returns>A resolved extraction input with one explicit solution path.</returns>
        private static ResolvedExtractionInput CreateResolvedInput()
        {
            // Tests avoid filesystem access because the pipeline receives already validated and normalized input from earlier slices.
            return new ResolvedExtractionInput(
                "D:/Repositories/CustomerSuite/",
                ["D:/Repositories/CustomerSuite/CustomerSuite.sln"],
                BranchName: "main",
                CommitSha: "abcdef1234567890",
                RequestedBy: "developer@example.invalid",
                Metadata: new Dictionary<string, string>
                {
                    ["source"] = "pipeline-test"
                });
        }

        /// <summary>
        /// Creates an accepted run associated with the supplied resolved input.
        /// </summary>
        /// <param name="input">The normalized input represented by the run summary.</param>
        /// <returns>A queued extraction run for pipeline execution tests.</returns>
        private static ExtractionRun CreateRun(ResolvedExtractionInput input)
        {
            // The run supplies identity and lifecycle context without requiring the full start application service.
            return new ExtractionRun(
                ExtractionRunId.New(),
                ExtractionRunStatus.Queued,
                new ExtractionRunRequestSummary(
                    input.RepositoryRootDirectory,
                    input.SolutionPaths,
                    input.BranchName,
                    input.CommitSha,
                    input.RequestedBy,
                    input.Metadata.Keys.ToArray()),
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                completedUtc: null,
                new ExtractionRunProgress("Queued", "Queued for test execution.", 0, new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero)),
                warnings: null,
                errors: null,
                timings: null,
                snapshotIdentity: null);
        }

        /// <summary>
        /// Records its execution and optionally returns a blocking test error.
        /// </summary>
        private sealed class RecordingExtractionStage : IExtractionStage
        {
            /// <summary>
            /// Stores the stage identifier used for ordering assertions and diagnostics.
            /// </summary>
            private readonly string _stageId;

            /// <summary>
            /// Stores the shared execution-order list that tests assert after pipeline execution.
            /// </summary>
            private readonly List<string> _executionOrder;

            /// <summary>
            /// Indicates whether this stage should return a blocking test error.
            /// </summary>
            private readonly bool _shouldBlock;

            /// <summary>
            /// Initializes a new instance of the <see cref="RecordingExtractionStage"/> class.
            /// </summary>
            /// <param name="stageId">The stable stage identifier returned by the test stage.</param>
            /// <param name="executionOrder">The mutable execution-order list shared by the test.</param>
            /// <param name="shouldBlock">Whether the stage should return a blocking error.</param>
            internal RecordingExtractionStage(string stageId, List<string> executionOrder, bool shouldBlock)
            {
                // The test stage is intentionally tiny but still mirrors the real stage contract.
                _stageId = stageId;
                _executionOrder = executionOrder;
                _shouldBlock = shouldBlock;
            }

            /// <summary>
            /// Gets the stable stage identifier used by the pipeline runner.
            /// </summary>
            public string StageId => _stageId;

            /// <summary>
            /// Records execution and optionally returns a blocking error contribution.
            /// </summary>
            /// <param name="context">The stage context supplied by the pipeline runner.</param>
            /// <param name="cancellationToken">The cancellation token for the stage execution.</param>
            /// <returns>The stage result describing success or blocking failure.</returns>
            public Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
            {
                // The method appends the stage id before returning so order assertions prove actual invocation order.
                cancellationToken.ThrowIfCancellationRequested();
                _executionOrder.Add(_stageId);

                return Task.FromResult(_shouldBlock
                    ? ExtractionStageResult.BlockingError($"{_stageId} failed with a blocking test error.")
                    : ExtractionStageResult.Success());
            }
        }

        /// <summary>
        /// Records execution and contributes a non-blocking warning to the shared accumulation.
        /// </summary>
        private sealed class WarningExtractionStage : IExtractionStage
        {
            /// <summary>
            /// Stores the stage identifier used for ordering assertions and diagnostics.
            /// </summary>
            private readonly string _stageId;

            /// <summary>
            /// Stores the shared execution-order list that tests assert after pipeline execution.
            /// </summary>
            private readonly List<string> _executionOrder;

            /// <summary>
            /// Initializes a new instance of the <see cref="WarningExtractionStage"/> class.
            /// </summary>
            /// <param name="stageId">The stable stage identifier returned by the test stage.</param>
            /// <param name="executionOrder">The mutable execution-order list shared by the test.</param>
            internal WarningExtractionStage(string stageId, List<string> executionOrder)
            {
                // The test stage writes directly to accumulation to prove warning retention across successful execution.
                _stageId = stageId;
                _executionOrder = executionOrder;
            }

            /// <summary>
            /// Gets the stable stage identifier used by the pipeline runner.
            /// </summary>
            public string StageId => _stageId;

            /// <summary>
            /// Records execution and contributes a non-blocking warning diagnostic.
            /// </summary>
            /// <param name="context">The stage context supplied by the pipeline runner.</param>
            /// <param name="cancellationToken">The cancellation token for the stage execution.</param>
            /// <returns>A successful stage result after the warning is recorded.</returns>
            public Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
            {
                // The warning remains non-blocking because it does not make later placeholder stages unsafe.
                cancellationToken.ThrowIfCancellationRequested();
                _executionOrder.Add(_stageId);
                context.Accumulation.AddWarning($"{_stageId} emitted a non-blocking test warning.");
                return Task.FromResult(ExtractionStageResult.Success());
            }
        }
    }
}
