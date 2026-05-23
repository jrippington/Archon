using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.Integrations.InternalServices
{
    /// <summary>
    /// Contains the partial graph snapshot and diagnostics produced by WP010 internal service correlation.
    /// </summary>
    public sealed class InternalServiceIntegrationExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InternalServiceIntegrationExtractionResult" /> class.
        /// </summary>
        /// <param name="snapshot">The partial architecture snapshot containing internal service correlation facts and diagnostics.</param>
        public InternalServiceIntegrationExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // The result mirrors other WP010 slices so orchestration can merge snapshots without a special internal-service contract.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the partial architecture snapshot containing internal service correlation facts and diagnostics.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }

        /// <summary>
        /// Gets warning diagnostics emitted during internal service correlation.
        /// </summary>
        public IReadOnlyList<string> Warnings => Snapshot.Warnings;

        /// <summary>
        /// Gets error diagnostics emitted during internal service correlation.
        /// </summary>
        public IReadOnlyList<string> Errors => Snapshot.Errors;
    }
}
