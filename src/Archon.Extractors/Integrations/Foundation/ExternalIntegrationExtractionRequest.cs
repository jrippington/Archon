using Archon.Domain.Graph.Identity;

namespace Archon.Extractors.Integrations.Foundation
{
    /// <summary>
    /// Defines the graph-projection input for one external integration extraction pass.
    /// </summary>
    public sealed class ExternalIntegrationExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalIntegrationExtractionRequest" /> class.
        /// </summary>
        /// <param name="SnapshotStableKey">The stable key of the extraction snapshot receiving integration facts.</param>
        /// <param name="RepositoryRootDirectory">The analyzed repository root used only for repository-relative path normalization.</param>
        /// <param name="Observations">The deterministic source observations to project into graph facts.</param>
        public ExternalIntegrationExtractionRequest(StableKey SnapshotStableKey, string RepositoryRootDirectory, IReadOnlyList<ExternalIntegrationObservation> Observations)
        {
            // The foundation extractor must be runnable as a no-op, so an empty observation list is valid but null input is not.
            ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRootDirectory);
            this.SnapshotStableKey = SnapshotStableKey;
            this.RepositoryRootDirectory = RepositoryRootDirectory;
            this.Observations = Observations?.ToArray() ?? throw new ArgumentNullException(nameof(Observations));
        }

        /// <summary>
        /// Gets the stable key of the extraction snapshot receiving integration facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the analyzed repository root used only for repository-relative path normalization.
        /// </summary>
        public string RepositoryRootDirectory { get; }

        /// <summary>
        /// Gets the deterministic source observations to project into graph facts.
        /// </summary>
        public IReadOnlyList<ExternalIntegrationObservation> Observations { get; }
    }
}
