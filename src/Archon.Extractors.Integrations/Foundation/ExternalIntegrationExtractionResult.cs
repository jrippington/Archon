using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.Integrations.Foundation
{
    /// <summary>
    /// Carries graph facts and diagnostics produced by one WP010 external integration extraction pass.
    /// </summary>
    public sealed class ExternalIntegrationExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalIntegrationExtractionResult" /> class.
        /// </summary>
        /// <param name="Snapshot">The extracted architecture snapshot containing integration graph contributions and diagnostics.</param>
        public ExternalIntegrationExtractionResult(ExtractedArchitectureSnapshot Snapshot)
        {
            // A result without a snapshot would lose diagnostics, so the snapshot is required even for no-op execution.
            this.Snapshot = Snapshot ?? throw new ArgumentNullException(nameof(Snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot containing integration graph contributions and diagnostics.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }
    }
}
