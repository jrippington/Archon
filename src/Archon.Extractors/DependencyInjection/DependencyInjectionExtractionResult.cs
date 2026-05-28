using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.DependencyInjection
{
    /// <summary>
    /// Represents the graph snapshot contributions and diagnostics produced by dependency-injection extraction.
    /// </summary>
    /// <remarks>
    /// The result wraps the shared application snapshot contract so callers can merge dependency-injection facts without learning an extractor-specific graph model.
    /// </remarks>
    public sealed class DependencyInjectionExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DependencyInjectionExtractionResult"/> class.
        /// </summary>
        /// <param name="snapshot">The shared architecture snapshot containing nodes, edges, evidence, warnings, and errors emitted by the extractor.</param>
        public DependencyInjectionExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // The extractor result is immutable at the boundary because the accumulator has already normalized duplicates and ordering.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the shared architecture snapshot containing dependency-injection graph contributions.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }

        /// <summary>
        /// Gets the non-fatal warnings emitted during dependency-injection extraction.
        /// </summary>
        public IReadOnlyList<string> Warnings
        {
            get
            {
                // Warnings are exposed directly for focused tests and orchestration diagnostics.
                return Snapshot.Warnings;
            }
        }

        /// <summary>
        /// Gets the fatal errors emitted during dependency-injection extraction.
        /// </summary>
        public IReadOnlyList<string> Errors
        {
            get
            {
                // Errors are exposed directly for focused tests and orchestration diagnostics.
                return Snapshot.Errors;
            }
        }
    }
}
