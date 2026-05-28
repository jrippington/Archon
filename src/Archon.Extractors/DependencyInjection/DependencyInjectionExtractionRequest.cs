using Archon.Domain.Graph.Identity;
using Archon.Roslyn.SemanticModel;

namespace Archon.Extractors.DependencyInjection
{
    /// <summary>
    /// Provides the snapshot and Roslyn document context required by dependency-injection extraction.
    /// </summary>
    /// <remarks>
    /// The request deliberately stays small for the dependency-injection extraction slice. It supplies one semantic document and the snapshot stable key that scopes graph facts, while later work items can extend orchestration without replacing this entry model.
    /// </remarks>
    public sealed class DependencyInjectionExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DependencyInjectionExtractionRequest"/> class.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the extraction snapshot that should own emitted graph facts.</param>
        /// <param name="semanticDocument">The Roslyn semantic document context used to inspect C# registration calls and source evidence.</param>
        public DependencyInjectionExtractionRequest(StableKey snapshotStableKey, SemanticExtractionRequest semanticDocument)
        {
            // The request keeps graph identity and semantic analysis input together so the extractor cannot emit unscoped facts.
            if (string.IsNullOrWhiteSpace(snapshotStableKey.Value))
            {
                throw new ArgumentException("Dependency-injection extraction requires a non-empty snapshot stable key.", nameof(snapshotStableKey));
            }

            SnapshotStableKey = snapshotStableKey;
            SemanticDocument = semanticDocument ?? throw new ArgumentNullException(nameof(semanticDocument));
        }

        /// <summary>
        /// Gets the stable key of the extraction snapshot that should own emitted graph facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the Roslyn semantic document context used to inspect C# registration calls and source evidence.
        /// </summary>
        public SemanticExtractionRequest SemanticDocument { get; }
    }
}
