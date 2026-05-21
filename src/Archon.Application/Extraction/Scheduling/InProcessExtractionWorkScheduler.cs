using Archon.Application.Extraction.Orchestration;
using Archon.Application.Extraction.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Archon.Application.Extraction.Scheduling
{
    /// <summary>
    /// Dispatches accepted extraction work to the application orchestrator on an in-process background task.
    /// </summary>
    public sealed class InProcessExtractionWorkScheduler : IExtractionWorkScheduler
    {
        /// <summary>
        /// Creates a dependency-injection scope for each background orchestration execution.
        /// </summary>
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>
        /// Logs credential-safe scheduling and background execution events.
        /// </summary>
        private readonly ILogger<InProcessExtractionWorkScheduler> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="InProcessExtractionWorkScheduler"/> class.
        /// </summary>
        /// <param name="scopeFactory">The factory used to resolve scoped orchestration dependencies for background execution.</param>
        /// <param name="logger">The logger used for credential-safe scheduling diagnostics.</param>
        public InProcessExtractionWorkScheduler(IServiceScopeFactory scopeFactory, ILogger<InProcessExtractionWorkScheduler> logger)
        {
            // A scope per work item keeps the scheduler replaceable by a durable worker in later work packages.
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Queues accepted extraction work and returns as soon as the local background dispatch has been created.
        /// </summary>
        /// <param name="runId">The accepted run identifier whose work should be orchestrated.</param>
        /// <param name="cancellationToken">The cancellation token for the scheduling request.</param>
        /// <returns>A completed task once the scheduler has accepted the work item.</returns>
        public Task ScheduleAsync(ExtractionRunId runId, CancellationToken cancellationToken)
        {
            // The HTTP start path must not wait for extraction; orchestration continues after this method returns.
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Scheduling extraction run {RunId} for in-process background orchestration.", runId.ToString());
            _ = Task.Run(() => ExecuteInBackgroundAsync(runId), CancellationToken.None);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Resolves the orchestrator in a background scope and executes the accepted run.
        /// </summary>
        /// <param name="runId">The accepted run identifier to execute.</param>
        /// <returns>A task that completes when background orchestration has reached a terminal state.</returns>
        private async Task ExecuteInBackgroundAsync(ExtractionRunId runId)
        {
            // Exceptions are caught so unobserved task failures do not terminate the process or bypass run status failure handling.
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ExtractionOrchestrator orchestrator = scope.ServiceProvider.GetRequiredService<ExtractionOrchestrator>();
                await orchestrator.ExecuteAsync(runId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "In-process extraction scheduling failed unexpectedly for run {RunId}.", runId.ToString());
            }
        }
    }
}
