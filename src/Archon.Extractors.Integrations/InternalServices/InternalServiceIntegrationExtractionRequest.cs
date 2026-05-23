using Archon.Domain.Graph.Identity;
using Archon.Roslyn.SemanticModel;

namespace Archon.Extractors.Integrations.InternalServices
{
    /// <summary>
    /// Represents the snapshot, repository, Roslyn semantic documents, and endpoint facts required for WP010 internal service correlation.
    /// </summary>
    public sealed class InternalServiceIntegrationExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InternalServiceIntegrationExtractionRequest" /> class.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the extraction snapshot receiving correlated internal service facts.</param>
        /// <param name="repositoryRootDirectory">The absolute repository root used to normalize source evidence paths.</param>
        /// <param name="semanticDocuments">The Roslyn semantic documents that should be inspected for internal client call evidence.</param>
        /// <param name="endpoints">The deterministic endpoint facts from earlier extraction slices that may own internal service routes.</param>
        public InternalServiceIntegrationExtractionRequest(StableKey snapshotStableKey, string repositoryRootDirectory, IEnumerable<SemanticExtractionRequest> semanticDocuments, IEnumerable<InternalServiceEndpointFact> endpoints)
        {
            // The request keeps prior runtime facts beside source evidence so correlation can require both client-side and provider-side proof.
            SnapshotStableKey = snapshotStableKey;
            RepositoryRootDirectory = RequireText(repositoryRootDirectory, nameof(repositoryRootDirectory));
            SemanticDocuments = (semanticDocuments ?? throw new ArgumentNullException(nameof(semanticDocuments))).ToArray();
            Endpoints = (endpoints ?? throw new ArgumentNullException(nameof(endpoints))).ToArray();
        }

        /// <summary>
        /// Gets the stable key of the extraction snapshot receiving correlated internal service facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the absolute repository root used to normalize source evidence paths.
        /// </summary>
        public string RepositoryRootDirectory { get; }

        /// <summary>
        /// Gets the Roslyn semantic documents inspected for internal service call evidence.
        /// </summary>
        public IReadOnlyList<SemanticExtractionRequest> SemanticDocuments { get; }

        /// <summary>
        /// Gets the deterministic endpoint facts available for internal ownership correlation.
        /// </summary>
        public IReadOnlyList<InternalServiceEndpointFact> Endpoints { get; }

        /// <summary>
        /// Requires non-empty boundary text before internal service analysis begins.
        /// </summary>
        /// <param name="value">The candidate request value.</param>
        /// <param name="parameterName">The parameter name used when reporting validation failures.</param>
        /// <returns>The trimmed non-empty request value.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Repository roots are evidence-normalization inputs, so blank values would make correlation output ambiguous.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Internal service integration extraction request values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }
    }
}
