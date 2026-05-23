using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.Integrations.Rpc
{
    /// <summary>
    /// Contains the extracted graph snapshot and diagnostics produced by the WP010 RPC and generated-client integration extractor.
    /// </summary>
    public sealed class RpcGeneratedClientIntegrationExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RpcGeneratedClientIntegrationExtractionResult" /> class.
        /// </summary>
        /// <param name="snapshot">The partial architecture snapshot containing RPC and generated-client integration facts and diagnostics.</param>
        public RpcGeneratedClientIntegrationExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // The result mirrors other integration extractor slices so orchestration and tests can consume the shared snapshot contract consistently.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the partial architecture snapshot containing RPC and generated-client integration facts and diagnostics.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }

        /// <summary>
        /// Gets warning diagnostics emitted during RPC and generated-client integration extraction.
        /// </summary>
        public IReadOnlyList<string> Warnings => Snapshot.Warnings;

        /// <summary>
        /// Gets error diagnostics emitted during RPC and generated-client integration extraction.
        /// </summary>
        public IReadOnlyList<string> Errors => Snapshot.Errors;
    }
}
