using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.AspNet.MinimalApis
{
    /// <summary>
    /// Represents graph-ready snapshot contributions produced by ASP.NET Core minimal API endpoint extraction.
    /// </summary>
    public sealed class MinimalApiEndpointExtractionResult
    {
        /// <summary>
        /// Initializes a result after validating the snapshot contribution payload.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot section containing endpoint nodes, relationships, evidence, and diagnostics.</param>
        public MinimalApiEndpointExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // Result construction keeps extractor callers from accidentally treating a missing snapshot as an empty successful extraction.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot section containing endpoint nodes, relationships, evidence, and diagnostics.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }
    }
}
