using Archon.Application.Extraction.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Runs WP012 rule catalog loading, catalog persistence, and enabled-rule evaluation as an extraction pipeline stage.
    /// </summary>
    public sealed class RuleEvaluationExtractionStage : IExtractionStage
    {
        /// <summary>
        /// Coordinates the application-level rule integration workflow.
        /// </summary>
        private readonly RuleExtractionIntegrationService _integrationService;

        /// <summary>
        /// Logs credential-safe stage execution diagnostics.
        /// </summary>
        private readonly ILogger<RuleEvaluationExtractionStage> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleEvaluationExtractionStage"/> class.
        /// </summary>
        /// <param name="integrationService">The application service that performs rule loading, persistence, and evaluation.</param>
        public RuleEvaluationExtractionStage(RuleExtractionIntegrationService integrationService)
            : this(integrationService, NullLogger<RuleEvaluationExtractionStage>.Instance)
        {
            // This overload supports focused tests that do not need structured logging.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleEvaluationExtractionStage"/> class.
        /// </summary>
        /// <param name="integrationService">The application service that performs rule loading, persistence, and evaluation.</param>
        /// <param name="logger">The logger used for credential-safe stage diagnostics.</param>
        public RuleEvaluationExtractionStage(RuleExtractionIntegrationService integrationService, ILogger<RuleEvaluationExtractionStage> logger)
        {
            // The stage itself remains thin so rule-evaluation logic stays in the application service rather than host composition.
            _integrationService = integrationService ?? throw new ArgumentNullException(nameof(integrationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the stable extraction stage identifier used by pipeline diagnostics and tests.
        /// </summary>
        public string StageId
        {
            get
            {
                // The identifier is intentionally explicit so run diagnostics can distinguish rule evaluation from extractor stages.
                return "wp012-rule-catalog-evaluation";
            }
        }

        /// <summary>
        /// Executes rule catalog persistence and evaluation after earlier extraction stages have populated graph facts.
        /// </summary>
        /// <param name="context">The extraction stage context containing accumulated graph facts and run state.</param>
        /// <param name="cancellationToken">The cancellation token flowing through loading, persistence, and evaluation.</param>
        /// <returns>A successful stage result or a controlled blocking result for invalid built-in catalog content.</returns>
        public async Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // Invalid catalogs are blocking because built-in rule failures must not be silently ignored during extraction initialization.
            ArgumentNullException.ThrowIfNull(context);
            try
            {
                RuleExtractionIntegrationResult result = await _integrationService.LoadPersistAndEvaluateAsync(context.Accumulation, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "Completed WP012 rule stage for run {RunId} with {LoadedRuleCount} loaded rules and {MatchCount} matches.",
                    context.Run.RunId.ToString(),
                    result.LoadedRuleCount,
                    result.MatchCount);
                return ExtractionStageResult.Success();
            }
            catch (RuleCatalogValidationException exception)
            {
                string message = $"Rule catalog validation failed before extraction rule evaluation: {string.Join("; ", exception.Diagnostics.Select(static diagnostic => diagnostic.Message))}";
                _logger.LogWarning("Rule catalog validation stopped WP012 rule evaluation for run {RunId}.", context.Run.RunId.ToString());
                return ExtractionStageResult.BlockingError(message);
            }
        }
    }
}
