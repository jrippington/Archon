using Archon.Domain.Graph.Identity;
using Archon.Roslyn.SemanticModel;

namespace Archon.Extractors.AspNet.MinimalApis
{
    /// <summary>
    /// Carries the snapshot scope and semantic source documents used by the ASP.NET Core minimal API endpoint extractor.
    /// </summary>
    public sealed class MinimalApiEndpointExtractionRequest
    {
        /// <summary>
        /// Initializes a request after validating the required snapshot and document collection inputs.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted endpoint graph facts.</param>
        /// <param name="semanticDocuments">The Roslyn semantic documents loaded from submitted C# projects.</param>
        public MinimalApiEndpointExtractionRequest(StableKey snapshotStableKey, IReadOnlyList<SemanticExtractionRequest> semanticDocuments)
        {
            // A snapshot scope and a concrete document list are required so endpoint facts never float outside an accepted extraction run.
            ArgumentNullException.ThrowIfNull(semanticDocuments);
            SnapshotStableKey = snapshotStableKey;
            SemanticDocuments = semanticDocuments;
        }

        /// <summary>
        /// Gets the stable key of the snapshot that receives extracted endpoint graph facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the Roslyn semantic documents loaded from submitted C# projects.
        /// </summary>
        public IReadOnlyList<SemanticExtractionRequest> SemanticDocuments { get; }
    }
}
