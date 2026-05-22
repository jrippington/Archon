using Archon.Domain.Graph.Identity;
using Archon.Roslyn.SemanticModel;

namespace Archon.Extractors.AspNet.Runtime
{
    /// <summary>
    /// Carries the snapshot scope and semantic source documents used by console entry-point runtime extraction.
    /// </summary>
    /// <remarks>
    /// The request is intentionally language-neutral because Work Item 4 must inspect C# and VB.NET documents through the shared Roslyn semantic request contract while keeping workspace loading outside the extractor.
    /// </remarks>
    public sealed class ConsoleEntryPointExtractionRequest
    {
        /// <summary>
        /// Initializes a request after validating the snapshot scope and submitted semantic documents.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted console entry-point graph facts.</param>
        /// <param name="semanticDocuments">The Roslyn semantic documents loaded from submitted target repository projects.</param>
        public ConsoleEntryPointExtractionRequest(StableKey snapshotStableKey, IReadOnlyList<SemanticExtractionRequest> semanticDocuments)
        {
            // A concrete document collection keeps extraction scoped to the accepted API input and prevents arbitrary repository scanning.
            ArgumentNullException.ThrowIfNull(semanticDocuments);
            SnapshotStableKey = snapshotStableKey;
            SemanticDocuments = semanticDocuments;
        }

        /// <summary>
        /// Gets the stable key of the snapshot that receives extracted console entry-point graph facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the Roslyn semantic documents loaded from submitted target repository projects.
        /// </summary>
        public IReadOnlyList<SemanticExtractionRequest> SemanticDocuments { get; }
    }
}
