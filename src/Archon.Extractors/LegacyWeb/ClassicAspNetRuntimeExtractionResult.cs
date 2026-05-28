using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.LegacyWeb
{
    /// <summary>
    /// Represents graph-ready snapshot contributions produced by classic ASP.NET runtime extraction.
    /// </summary>
    public sealed class ClassicAspNetRuntimeExtractionResult
    {
        /// <summary>
        /// Initializes a result after validating the snapshot contribution payload.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot section containing classic runtime nodes, relationships, evidence, and diagnostics.</param>
        public ClassicAspNetRuntimeExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // A non-null snapshot keeps callers from mistaking a missing extraction result for a successful empty contribution.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot section containing classic runtime nodes, relationships, evidence, and diagnostics.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }
    }
}
