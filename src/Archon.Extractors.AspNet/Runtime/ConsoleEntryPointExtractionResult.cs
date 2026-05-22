using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.AspNet.Runtime
{
    /// <summary>
    /// Represents graph-ready snapshot contributions produced by console entry-point runtime extraction.
    /// </summary>
    public sealed class ConsoleEntryPointExtractionResult
    {
        /// <summary>
        /// Initializes a result after validating the snapshot contribution payload.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot section containing project, type, method, evidence, relationship, and diagnostic facts.</param>
        public ConsoleEntryPointExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // Result construction treats a missing snapshot as a caller error rather than silently converting it to a successful empty extraction.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot section containing project, type, method, evidence, relationship, and diagnostic facts.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }
    }
}
