using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Archon.Application.Extraction.Pipeline
{
    /// <summary>
    /// Executes extraction stages sequentially against one shared accumulation model.
    /// </summary>
    public sealed class ExtractionPipelineRunner
    {
        /// <summary>
        /// Stores deterministic stages in the exact order supplied by dependency injection or tests.
        /// </summary>
        private readonly IReadOnlyList<IExtractionStage> _stages;

        /// <summary>
        /// Logs credential-safe pipeline execution events.
        /// </summary>
        private readonly ILogger<ExtractionPipelineRunner> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtractionPipelineRunner"/> class.
        /// </summary>
        /// <param name="stages">The deterministic extraction stages to execute sequentially.</param>
        /// <param name="logger">The logger used for credential-safe pipeline diagnostics.</param>
        public ExtractionPipelineRunner(IEnumerable<IExtractionStage> stages, ILogger<ExtractionPipelineRunner> logger)
        {
            // Stages are copied once so pipeline execution cannot observe later collection mutation by the caller.
            ArgumentNullException.ThrowIfNull(stages);
            _stages = stages.ToArray();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Executes the configured stages sequentially for one accepted extraction run.
        /// </summary>
        /// <param name="resolvedInput">The normalized input that has already passed start request validation.</param>
        /// <param name="run">The accepted run that scopes this pipeline execution.</param>
        /// <param name="cancellationToken">The cancellation token that can stop execution between stages or inside a stage.</param>
        /// <returns>The pipeline result including accumulation, executed stages, and optional failed stage.</returns>
        public async Task<ExtractionPipelineResult> ExecuteAsync(
            ResolvedExtractionInput resolvedInput,
            ExtractionRun run,
            CancellationToken cancellationToken)
        {
            // One accumulation instance is shared by all stages so later stages can see earlier contributions when needed.
            ArgumentNullException.ThrowIfNull(resolvedInput);
            ArgumentNullException.ThrowIfNull(run);

            ArchitectureSnapshotAccumulator accumulation = new();
            ExtractionStageContext context = new(resolvedInput, run, accumulation);
            List<string> executedStageIds = [];
            List<ExtractionRunTiming> stageTimings = [];

            foreach (IExtractionStage stage in _stages)
            {
                // Cancellation is checked between stages to avoid starting new work after the caller requests shutdown.
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(stage);
                ArgumentException.ThrowIfNullOrWhiteSpace(stage.StageId);

                _logger.LogInformation("Starting extraction stage {StageId} for run {RunId}.", stage.StageId, run.RunId.ToString());
                executedStageIds.Add(stage.StageId);
                Stopwatch stopwatch = Stopwatch.StartNew();
                ExtractionStageResult stageResult = await stage.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                stageTimings.Add(new ExtractionRunTiming(stage.StageId, stopwatch.ElapsedMilliseconds, DateTimeOffset.UtcNow));

                if (stageResult.HasBlockingError)
                {
                    // Controlled blocking failures are preserved as accumulation diagnostics so status and persistence can report them safely.
                    string errorMessage = stageResult.ErrorMessage ?? $"Extraction stage {stage.StageId} failed.";
                    accumulation.AddError(errorMessage);
                    _logger.LogWarning(
                        "Extraction stage {StageId} stopped run {RunId} with a controlled blocking error.",
                        stage.StageId,
                        run.RunId.ToString());
                    return new ExtractionPipelineResult(
                        Succeeded: false,
                        accumulation,
                        executedStageIds.ToArray(),
                        stageTimings.ToArray(),
                        FailedStageId: stage.StageId);
                }

                _logger.LogInformation("Completed extraction stage {StageId} for run {RunId}.", stage.StageId, run.RunId.ToString());
            }

            return new ExtractionPipelineResult(
                Succeeded: true,
                accumulation,
                executedStageIds.ToArray(),
                stageTimings.ToArray(),
                FailedStageId: null);
        }

    }
}
