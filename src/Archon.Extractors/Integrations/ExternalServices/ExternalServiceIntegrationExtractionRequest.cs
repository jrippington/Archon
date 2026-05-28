using Archon.Domain.Graph.Identity;
using Archon.Roslyn.SemanticModel;

namespace Archon.Extractors.Integrations.ExternalServices
{
    /// <summary>
    /// Describes one static storage, SMTP/email, and payment-provider extraction request for the external-service detector.
    /// </summary>
    public sealed class ExternalServiceIntegrationExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalServiceIntegrationExtractionRequest" /> class.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving storage, email, and payment graph facts.</param>
        /// <param name="repositoryRootDirectory">The absolute repository root used to normalize evidence paths and scan local configuration artifacts.</param>
        /// <param name="semanticDocuments">The Roslyn semantic documents that should be inspected for storage, email, and payment source evidence.</param>
        public ExternalServiceIntegrationExtractionRequest(StableKey snapshotStableKey, string repositoryRootDirectory, IEnumerable<SemanticExtractionRequest> semanticDocuments)
        {
            // The request validates immutable extraction scope once so detector code can focus on source traversal.
            SnapshotStableKey = snapshotStableKey;
            RepositoryRootDirectory = RequireText(repositoryRootDirectory, nameof(repositoryRootDirectory));
            SemanticDocuments = (semanticDocuments ?? throw new ArgumentNullException(nameof(semanticDocuments))).ToArray();
        }

        /// <summary>
        /// Gets the stable key of the snapshot receiving storage, email, and payment graph facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the absolute repository root used to normalize evidence paths and scan local configuration artifacts.
        /// </summary>
        public string RepositoryRootDirectory { get; }

        /// <summary>
        /// Gets the Roslyn semantic documents that should be inspected for storage, email, and payment source evidence.
        /// </summary>
        public IReadOnlyList<SemanticExtractionRequest> SemanticDocuments { get; }

        /// <summary>
        /// Validates that a required text value is present and trims surrounding whitespace.
        /// </summary>
        /// <param name="value">The text value to validate.</param>
        /// <param name="parameterName">The name of the parameter being validated.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Empty repository roots would make evidence normalization and artifact scanning nondeterministic.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A non-empty value is required.", parameterName);
            }

            return value.Trim();
        }
    }
}
