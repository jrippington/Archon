using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.AspNet.Runtime
{
    /// <summary>
    /// Represents graph-ready snapshot contributions produced by worker and hosted-service runtime extraction.
    /// </summary>
    public sealed class WorkerHostedServiceExtractionResult
    {
        /// <summary>
        /// Initializes a result after validating the snapshot contribution payload.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot section containing hosted-service nodes, relationships, evidence, warnings, and diagnostics.</param>
        public WorkerHostedServiceExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // Result construction rejects a null snapshot so callers cannot accidentally treat missing extraction output as successful empty output.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot section containing hosted-service nodes, relationships, evidence, warnings, and diagnostics.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }
    }
}
