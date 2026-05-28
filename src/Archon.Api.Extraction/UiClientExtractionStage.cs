using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Pipeline;
using Archon.Extractors.Ui;
using Microsoft.Extensions.Logging;

namespace Archon.Api.Extraction
{
    /// <summary>
    /// Coordinates every UI/client .NET UI/client framework extractor through one API-triggered extraction stage.
    /// </summary>
    /// <remarks>
    /// The stage is an orchestration adapter. It delegates framework-specific static analysis to the existing Blazor, Razor, Windows Forms, WPF, WinUI, .NET MAUI, and Avalonia stage adapters, then lets the shared accumulation contract deduplicate graph facts by stable key without performing persistence or creating product UI artifacts.
    /// </remarks>
    public sealed class UiClientExtractionStage : IExtractionStage
    {
        /// <summary>
        /// Stores the deterministic framework stage adapters in the order used for unified UI/client extraction.
        /// </summary>
        private readonly IReadOnlyList<IExtractionStage> _frameworkStages;

        /// <summary>
        /// Stores the logger used for credential-safe unified UI/client orchestration diagnostics.
        /// </summary>
        private readonly ILogger<UiClientExtractionStage> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="UiClientExtractionStage" /> class.
        /// </summary>
        /// <param name="blazorStage">The Blazor route and component stage that contributes `.razor` UI facts.</param>
        /// <param name="razorStage">The Razor Pages and MVC Razor stage that contributes `.cshtml` UI facts.</param>
        /// <param name="winFormsStage">The Windows Forms stage that contributes designer, resource, control, and event facts.</param>
        /// <param name="wpfStage">The WPF XAML stage that contributes windows, pages, resources, bindings, commands, and navigation facts.</param>
        /// <param name="winUiStage">The WinUI XAML stage that contributes modern Windows desktop UI and packaging facts.</param>
        /// <param name="mauiStage">The .NET MAUI XAML stage that contributes Shell, page, platform-head, handler, and navigation facts.</param>
        /// <param name="avaloniaStage">The Avalonia AXAML stage that contributes cross-platform desktop, view-locator, and ReactiveUI facts.</param>
        /// <param name="logger">The logger used for stage start, per-framework completion, cancellation, and diagnostic summaries.</param>
        public UiClientExtractionStage(
            BlazorRouteComponentExtractionStage blazorStage,
            RazorPageViewExtractionStage razorStage,
            WinFormsStaticUiExtractionStage winFormsStage,
            WpfXamlExtractionStage wpfStage,
            WinUiXamlExtractionStage winUiStage,
            MauiXamlExtractionStage mauiStage,
            AvaloniaAxamlExtractionStage avaloniaStage,
            ILogger<UiClientExtractionStage> logger)
        {
            // The constructor fixes framework ordering once so API composition exposes one stable UI/client stage while preserving each framework adapter's existing behavior.
            _frameworkStages =
            [
                blazorStage ?? throw new ArgumentNullException(nameof(blazorStage)),
                razorStage ?? throw new ArgumentNullException(nameof(razorStage)),
                winFormsStage ?? throw new ArgumentNullException(nameof(winFormsStage)),
                wpfStage ?? throw new ArgumentNullException(nameof(wpfStage)),
                winUiStage ?? throw new ArgumentNullException(nameof(winUiStage)),
                mauiStage ?? throw new ArgumentNullException(nameof(mauiStage)),
                avaloniaStage ?? throw new ArgumentNullException(nameof(avaloniaStage))
            ];
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the stable stage identifier used by pipeline ordering, progress reporting, and diagnostics.
        /// </summary>
        public string StageId => "wp011-ui-client";

        /// <summary>
        /// Executes every UI/client UI/client framework adapter against the shared extraction accumulation.
        /// </summary>
        /// <param name="context">The pipeline context containing resolved repository input, accepted run state, and shared accumulation.</param>
        /// <param name="cancellationToken">The cancellation token that stops unified UI/client extraction before or between framework adapters.</param>
        /// <returns>A successful stage result when all framework adapters complete without a controlled blocking failure.</returns>
        public async Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // The unified flow intentionally runs framework adapters sequentially so later contributors observe the same deterministic accumulator state as earlier individual-stage implementations.
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Starting unified UI/client UI/client extraction for run {RunId} with {FrameworkStageCount} framework stage(s).",
                context.Run.RunId.ToString(),
                _frameworkStages.Count);

            foreach (IExtractionStage frameworkStage in _frameworkStages)
            {
                // Each adapter owns framework-specific warnings and errors; this coordinator only stops when an adapter reports a controlled blocking failure.
                cancellationToken.ThrowIfCancellationRequested();
                ExtractedArchitectureSnapshot snapshotBeforeStage = context.Accumulation.ToSnapshot();

                _logger.LogInformation(
                    "Starting UI/client framework stage {FrameworkStageId} for run {RunId}.",
                    frameworkStage.StageId,
                    context.Run.RunId.ToString());

                ExtractionStageResult result = await frameworkStage.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                ExtractedArchitectureSnapshot snapshotAfterStage = context.Accumulation.ToSnapshot();
                UiSnapshotMergeSummary mergeSummary = CreateMergeSummary(snapshotBeforeStage, snapshotAfterStage);

                if (result.HasBlockingError)
                {
                    // Controlled failures are returned to the pipeline runner so it can apply the established blocking-error diagnostic path.
                    _logger.LogWarning(
                        "UI/client framework stage {FrameworkStageId} stopped unified UI/client extraction for run {RunId} with a controlled blocking error.",
                        frameworkStage.StageId,
                        context.Run.RunId.ToString());
                    return result;
                }

                _logger.LogInformation(
                    "Completed UI/client framework stage {FrameworkStageId} for run {RunId}; added {NodeDelta} node(s), {EdgeDelta} edge(s), {EvidenceDelta} evidence record(s), {WarningDelta} warning(s), and {ErrorDelta} error(s) after stable-key deduplication.",
                    frameworkStage.StageId,
                    context.Run.RunId.ToString(),
                    mergeSummary.NodeDelta,
                    mergeSummary.EdgeDelta,
                    mergeSummary.EvidenceDelta,
                    mergeSummary.WarningDelta,
                    mergeSummary.ErrorDelta);
            }

            ExtractedArchitectureSnapshot finalSnapshot = context.Accumulation.ToSnapshot();
            _logger.LogInformation(
                "Completed unified UI/client UI/client extraction for run {RunId}; accumulated {NodeCount} node(s), {EdgeCount} edge(s), {EvidenceCount} evidence record(s), {WarningCount} warning(s), and {ErrorCount} error(s).",
                context.Run.RunId.ToString(),
                finalSnapshot.Nodes.Count,
                finalSnapshot.Edges.Count,
                finalSnapshot.Evidence.Count,
                finalSnapshot.Warnings.Count,
                finalSnapshot.Errors.Count);

            return ExtractionStageResult.Success();
        }

        /// <summary>
        /// Creates a stable-key deduplication summary from two snapshots around a framework stage execution.
        /// </summary>
        /// <param name="before">The snapshot captured before the framework stage ran.</param>
        /// <param name="after">The snapshot captured after the framework stage ran.</param>
        /// <returns>A contribution summary that uses the same delta shape as shared UI merge helpers.</returns>
        private static UiSnapshotMergeSummary CreateMergeSummary(ExtractedArchitectureSnapshot before, ExtractedArchitectureSnapshot after)
        {
            // The framework adapters merge through the shared helper, so count deltas here are used only for the unified coordinator's aggregate logging.
            ArgumentNullException.ThrowIfNull(before);
            ArgumentNullException.ThrowIfNull(after);

            return new UiSnapshotMergeSummary(
                after.Nodes.Count - before.Nodes.Count,
                after.Edges.Count - before.Edges.Count,
                after.Evidence.Count - before.Evidence.Count,
                after.Warnings.Count - before.Warnings.Count,
                after.Errors.Count - before.Errors.Count);
        }
    }
}
