namespace Archon.Extractors.Integrations.Foundation
{
    /// <summary>
    /// Carries observations and diagnostics collected for a WP010 integration stage execution before graph projection.
    /// </summary>
    public sealed class ExternalIntegrationObservationBatch
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalIntegrationObservationBatch" /> class.
        /// </summary>
        /// <param name="Observations">The deterministic source observations to project into integration graph facts.</param>
        /// <param name="Warnings">The non-blocking diagnostic warnings raised while collecting observations.</param>
        /// <param name="Errors">The non-blocking diagnostic errors raised while collecting observations.</param>
        public ExternalIntegrationObservationBatch(IReadOnlyList<ExternalIntegrationObservation> Observations, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors)
        {
            // Defensive copies keep provider output immutable for the downstream foundation extractor.
            this.Observations = Observations?.ToArray() ?? throw new ArgumentNullException(nameof(Observations));
            this.Warnings = Warnings?.Where(static warning => !string.IsNullOrWhiteSpace(warning)).Select(static warning => warning.Trim()).ToArray() ?? throw new ArgumentNullException(nameof(Warnings));
            this.Errors = Errors?.Where(static error => !string.IsNullOrWhiteSpace(error)).Select(static error => error.Trim()).ToArray() ?? throw new ArgumentNullException(nameof(Errors));
        }

        /// <summary>
        /// Gets the deterministic source observations to project into integration graph facts.
        /// </summary>
        public IReadOnlyList<ExternalIntegrationObservation> Observations { get; }

        /// <summary>
        /// Gets the non-blocking diagnostic warnings raised while collecting observations.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Gets the non-blocking diagnostic errors raised while collecting observations.
        /// </summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>
        /// Gets an empty observation batch for safe no-op stage execution.
        /// </summary>
        public static ExternalIntegrationObservationBatch Empty
        {
            get
            {
                // A singleton-like factory property keeps no-op providers explicit and allocation-light enough for tests.
                return new ExternalIntegrationObservationBatch([], [], []);
            }
        }
    }
}
