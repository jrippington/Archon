using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.AspNet.Runtime
{
    /// <summary>
    /// Represents graph-ready snapshot contributions produced by non-HTTP runtime consumer extraction.
    /// </summary>
    public sealed class NonHttpRuntimeConsumerExtractionResult
    {
        /// <summary>
        /// Initializes a result after validating the snapshot contribution payload.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot section containing queue, topic, scheduled-job, host-loop, relationship, evidence, warning, and diagnostic facts.</param>
        public NonHttpRuntimeConsumerExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // Result construction rejects null so stage orchestration cannot confuse missing output with an intentionally empty extraction result.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot section containing non-HTTP runtime consumer facts.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }
    }
}
