using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.Integrations.ExternalServices
{
    /// <summary>
    /// Carries the partial architecture snapshot produced by the WP010 storage, SMTP/email, and payment-provider detector.
    /// </summary>
    public sealed class ExternalServiceIntegrationExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalServiceIntegrationExtractionResult" /> class.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot containing external-service graph facts, warnings, and errors.</param>
        public ExternalServiceIntegrationExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // The snapshot is the single result payload consumed by tests and future orchestration code.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot containing storage, email, and payment graph facts.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }

        /// <summary>
        /// Gets the warning diagnostics emitted during storage, email, and payment extraction.
        /// </summary>
        public IReadOnlyList<string> Warnings => Snapshot.Warnings;

        /// <summary>
        /// Gets the error diagnostics emitted during storage, email, and payment extraction.
        /// </summary>
        public IReadOnlyList<string> Errors => Snapshot.Errors;
    }
}
