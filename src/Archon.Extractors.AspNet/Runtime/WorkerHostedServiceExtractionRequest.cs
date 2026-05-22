using Archon.Application.Extraction.Contracts;
using Archon.Domain.Graph.Identity;
using Archon.Roslyn.SemanticModel;

namespace Archon.Extractors.AspNet.Runtime
{
    /// <summary>
    /// Carries the snapshot scope, C# semantic documents, and optional prior graph facts used by worker and hosted-service runtime extraction.
    /// </summary>
    /// <remarks>
    /// The request keeps source analysis and correlation inputs explicit. Source documents provide static worker-service evidence, while the prior snapshot lets the extractor correlate hosted-service facts with dependency-injection registrations already emitted by earlier pipeline stages.
    /// </remarks>
    public sealed class WorkerHostedServiceExtractionRequest
    {
        /// <summary>
        /// Initializes a request after validating the snapshot scope and source document collection.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted worker and hosted-service graph facts.</param>
        /// <param name="semanticDocuments">The C# Roslyn semantic documents loaded from submitted target repository projects.</param>
        /// <param name="priorSnapshot">The optional already-accumulated snapshot used to correlate runtime facts with WP007 dependency-injection facts.</param>
        public WorkerHostedServiceExtractionRequest(StableKey snapshotStableKey, IReadOnlyList<SemanticExtractionRequest> semanticDocuments, ExtractedArchitectureSnapshot? priorSnapshot = null)
        {
            // The extractor must stay scoped to accepted API input, so callers provide the exact document set and optional accumulated graph state.
            ArgumentNullException.ThrowIfNull(semanticDocuments);
            SnapshotStableKey = snapshotStableKey;
            SemanticDocuments = semanticDocuments;
            PriorSnapshot = priorSnapshot;
        }

        /// <summary>
        /// Gets the stable key of the snapshot that receives extracted worker and hosted-service graph facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the C# Roslyn semantic documents loaded from submitted target repository projects.
        /// </summary>
        public IReadOnlyList<SemanticExtractionRequest> SemanticDocuments { get; }

        /// <summary>
        /// Gets the optional already-accumulated snapshot used to correlate runtime facts with WP007 dependency-injection facts.
        /// </summary>
        public ExtractedArchitectureSnapshot? PriorSnapshot { get; }
    }
}
