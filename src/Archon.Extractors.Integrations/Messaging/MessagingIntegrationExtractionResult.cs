using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.Integrations.Messaging
{
    /// <summary>
    /// Carries the partial architecture snapshot produced by the WP010 messaging detector.
    /// </summary>
    public sealed class MessagingIntegrationExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MessagingIntegrationExtractionResult" /> record.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot containing messaging graph facts, warnings, and errors.</param>
        public MessagingIntegrationExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // The snapshot is the single result payload consumed by tests and future orchestration code.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot containing messaging graph facts, warnings, and errors.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }

        /// <summary>
        /// Gets the warning diagnostics emitted during messaging extraction.
        /// </summary>
        public IReadOnlyList<string> Warnings => Snapshot.Warnings;

        /// <summary>
        /// Gets the error diagnostics emitted during messaging extraction.
        /// </summary>
        public IReadOnlyList<string> Errors => Snapshot.Errors;
    }
}
