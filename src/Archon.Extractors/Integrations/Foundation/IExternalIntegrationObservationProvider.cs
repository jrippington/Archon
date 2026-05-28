using Archon.Application.Extraction.Pipeline;

namespace Archon.Extractors.Integrations.Foundation
{
    /// <summary>
    /// Provides deterministic source observations for the external integration stage without performing live external calls.
    /// </summary>
    public interface IExternalIntegrationObservationProvider
    {
        /// <summary>
        /// Collects source observations and diagnostics for the current extraction context.
        /// </summary>
        /// <param name="context">The extraction stage context containing resolved repository input and the shared accumulator.</param>
        /// <param name="cancellationToken">The cancellation token that stops observation collection.</param>
        /// <returns>A batch of observations, warnings, and errors ready for graph projection.</returns>
        Task<ExternalIntegrationObservationBatch> CollectAsync(ExtractionStageContext context, CancellationToken cancellationToken);
    }
}
