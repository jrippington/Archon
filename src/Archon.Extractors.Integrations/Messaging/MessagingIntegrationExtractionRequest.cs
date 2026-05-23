using Archon.Domain.Graph.Identity;
using Archon.Roslyn.SemanticModel;

namespace Archon.Extractors.Integrations.Messaging
{
    /// <summary>
    /// Describes one static messaging extraction request for the WP010 messaging detector.
    /// </summary>
    public sealed class MessagingIntegrationExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MessagingIntegrationExtractionRequest" /> record.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving messaging graph facts.</param>
        /// <param name="repositoryRootDirectory">The absolute repository root used to normalize evidence paths and scan local artifacts.</param>
        /// <param name="semanticDocuments">The Roslyn semantic documents that should be inspected for messaging source evidence.</param>
        public MessagingIntegrationExtractionRequest(StableKey snapshotStableKey, string repositoryRootDirectory, IEnumerable<SemanticExtractionRequest> semanticDocuments)
        {
            // The request is validated at construction so downstream analyzers can assume required scoping values are present.
            SnapshotStableKey = snapshotStableKey;
            RepositoryRootDirectory = RequireText(repositoryRootDirectory, nameof(repositoryRootDirectory));
            SemanticDocuments = (semanticDocuments ?? throw new ArgumentNullException(nameof(semanticDocuments))).ToArray();
        }

        /// <summary>
        /// Gets the stable key of the snapshot receiving messaging graph facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the absolute repository root used to normalize evidence paths and scan local artifacts.
        /// </summary>
        public string RepositoryRootDirectory { get; }

        /// <summary>
        /// Gets the Roslyn semantic documents that should be inspected for messaging source evidence.
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
            // Empty roots would make evidence normalization nondeterministic, so reject them at the boundary.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A non-empty value is required.", parameterName);
            }

            return value.Trim();
        }
    }
}
