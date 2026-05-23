using Archon.Application.Extraction.Pipeline;

namespace Archon.Extractors.Integrations.Foundation
{
    /// <summary>
    /// Provides an empty WP010 observation batch so the foundation extraction stage can participate safely before detector work items are added.
    /// </summary>
    public sealed class NoOpExternalIntegrationObservationProvider : IExternalIntegrationObservationProvider
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NoOpExternalIntegrationObservationProvider" /> class.
        /// </summary>
        public NoOpExternalIntegrationObservationProvider()
        {
            // The no-op provider has no dependencies because it intentionally performs no repository scanning or external access.
        }

        /// <summary>
        /// Returns an empty observation batch after honoring cancellation.
        /// </summary>
        /// <param name="context">The extraction stage context; validated to keep provider contract behavior consistent.</param>
        /// <param name="cancellationToken">The cancellation token that stops no-op collection before returning.</param>
        /// <returns>An empty observation batch.</returns>
        public Task<ExternalIntegrationObservationBatch> CollectAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // No-op collection still validates context and cancellation so it behaves like future concrete providers.
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ExternalIntegrationObservationBatch.Empty);
        }
    }
}
