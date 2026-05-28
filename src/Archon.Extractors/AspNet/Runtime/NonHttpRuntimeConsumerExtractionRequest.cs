using Archon.Domain.Graph.Identity;
using Archon.Roslyn.SemanticModel;

namespace Archon.Extractors.AspNet.Runtime
{
    /// <summary>
    /// Carries the snapshot scope and C# semantic documents used by non-HTTP runtime consumer extraction.
    /// </summary>
    /// <remarks>
    /// Work Item 6 extraction is intentionally static. The request provides the exact semantic document set loaded from submitted target repository projects so the extractor can detect scheduled jobs, message consumers, service-style hosts, and host loops without scanning arbitrary directories or executing target code.
    /// </remarks>
    public sealed class NonHttpRuntimeConsumerExtractionRequest
    {
        /// <summary>
        /// Initializes a request after validating the source document collection.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted non-HTTP runtime consumer graph facts.</param>
        /// <param name="semanticDocuments">The C# Roslyn semantic documents loaded from submitted target repository projects.</param>
        public NonHttpRuntimeConsumerExtractionRequest(StableKey snapshotStableKey, IReadOnlyList<SemanticExtractionRequest> semanticDocuments)
        {
            // The extractor is scoped by caller-provided accepted extraction input; a null document list would make that boundary ambiguous.
            ArgumentNullException.ThrowIfNull(semanticDocuments);
            SnapshotStableKey = snapshotStableKey;
            SemanticDocuments = semanticDocuments;
        }

        /// <summary>
        /// Gets the stable key of the snapshot that receives extracted non-HTTP runtime consumer graph facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the C# Roslyn semantic documents loaded from submitted target repository projects.
        /// </summary>
        public IReadOnlyList<SemanticExtractionRequest> SemanticDocuments { get; }
    }
}
