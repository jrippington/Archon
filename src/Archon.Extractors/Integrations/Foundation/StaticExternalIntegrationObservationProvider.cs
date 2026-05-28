using Archon.Application.Extraction.Pipeline;

namespace Archon.Extractors.Integrations.Foundation
{
    /// <summary>
    /// Provides a deterministic in-memory observation batch for tests and controlled orchestration seams.
    /// </summary>
    public sealed class StaticExternalIntegrationObservationProvider : IExternalIntegrationObservationProvider
    {
        /// <summary>
        /// Stores the immutable observation batch returned by this provider.
        /// </summary>
        private readonly ExternalIntegrationObservationBatch _batch;

        /// <summary>
        /// Initializes a new instance of the <see cref="StaticExternalIntegrationObservationProvider" /> class.
        /// </summary>
        /// <param name="observations">The deterministic observations returned by this provider.</param>
        public StaticExternalIntegrationObservationProvider(IReadOnlyList<ExternalIntegrationObservation> observations)
            : this(observations, [], [])
        {
            // The convenience constructor supports tests that need observations without diagnostics.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StaticExternalIntegrationObservationProvider" /> class with diagnostics.
        /// </summary>
        /// <param name="observations">The deterministic observations returned by this provider.</param>
        /// <param name="warnings">The warnings returned by this provider.</param>
        /// <param name="errors">The errors returned by this provider.</param>
        public StaticExternalIntegrationObservationProvider(IReadOnlyList<ExternalIntegrationObservation> observations, IReadOnlyList<string> warnings, IReadOnlyList<string> errors)
        {
            // The immutable batch prevents tests from mutating provider output after construction.
            _batch = new ExternalIntegrationObservationBatch(observations, warnings, errors);
        }

        /// <summary>
        /// Returns the configured observation batch after honoring cancellation.
        /// </summary>
        /// <param name="context">The extraction stage context; validated to keep provider contract behavior consistent.</param>
        /// <param name="cancellationToken">The cancellation token that stops collection before returning.</param>
        /// <returns>The configured observation batch.</returns>
        public Task<ExternalIntegrationObservationBatch> CollectAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // Static collection is intentionally synchronous but still observes the same cancellation contract as concrete providers.
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_batch);
        }
    }
}
