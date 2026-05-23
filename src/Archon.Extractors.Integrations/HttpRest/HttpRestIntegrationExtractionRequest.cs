using Archon.Domain.Graph.Identity;
using Archon.Roslyn.SemanticModel;

namespace Archon.Extractors.Integrations.HttpRest
{
    /// <summary>
    /// Represents the snapshot, repository, and Roslyn semantic inputs required for one WP010 HTTP and REST integration extraction pass.
    /// </summary>
    public sealed class HttpRestIntegrationExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HttpRestIntegrationExtractionRequest" /> class.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the extraction snapshot receiving HTTP and REST integration facts.</param>
        /// <param name="repositoryRootDirectory">The absolute repository root used to normalize source evidence paths.</param>
        /// <param name="semanticDocuments">The Roslyn semantic documents that should be inspected for deterministic HTTP and REST evidence.</param>
        public HttpRestIntegrationExtractionRequest(StableKey snapshotStableKey, string repositoryRootDirectory, IEnumerable<SemanticExtractionRequest> semanticDocuments)
        {
            // The request captures immutable extraction inputs so static analysis cannot accidentally mix documents from different repository roots.
            SnapshotStableKey = snapshotStableKey;
            RepositoryRootDirectory = RequireText(repositoryRootDirectory, nameof(repositoryRootDirectory));
            SemanticDocuments = (semanticDocuments ?? throw new ArgumentNullException(nameof(semanticDocuments))).ToArray();
        }

        /// <summary>
        /// Gets the stable key of the extraction snapshot receiving HTTP and REST integration facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the absolute repository root used to normalize source evidence paths.
        /// </summary>
        public string RepositoryRootDirectory { get; }

        /// <summary>
        /// Gets the Roslyn semantic documents that should be inspected for deterministic HTTP and REST evidence.
        /// </summary>
        public IReadOnlyList<SemanticExtractionRequest> SemanticDocuments { get; }

        /// <summary>
        /// Requires non-empty boundary text before analysis begins.
        /// </summary>
        /// <param name="value">The candidate request value.</param>
        /// <param name="parameterName">The parameter name used when reporting validation failures.</param>
        /// <returns>The trimmed non-empty request value.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Repository paths are stable-key and evidence inputs, so blank values would make emitted facts ambiguous.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("HTTP and REST integration extraction request values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }
    }
}
