using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.Integrations.HttpRest
{
    /// <summary>
    /// Contains the extracted graph snapshot and diagnostics produced by the HTTP and REST integration extractor.
    /// </summary>
    public sealed class HttpRestIntegrationExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HttpRestIntegrationExtractionResult" /> class.
        /// </summary>
        /// <param name="snapshot">The partial architecture snapshot containing HTTP and REST integration facts and diagnostics.</param>
        public HttpRestIntegrationExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // The result is intentionally a thin wrapper so callers can consume the same snapshot contract used by other Archon extractor slices.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the partial architecture snapshot containing HTTP and REST integration facts and diagnostics.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }

        /// <summary>
        /// Gets warning diagnostics emitted during HTTP and REST integration extraction.
        /// </summary>
        public IReadOnlyList<string> Warnings => Snapshot.Warnings;

        /// <summary>
        /// Gets error diagnostics emitted during HTTP and REST integration extraction.
        /// </summary>
        public IReadOnlyList<string> Errors => Snapshot.Errors;
    }
}
