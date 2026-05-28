using Archon.Domain.Graph.Identity;
using Archon.Roslyn.SemanticModel;

namespace Archon.Extractors.Configuration
{
    /// <summary>
    /// Represents the inputs required to extract configuration artifacts and source-code configuration usage from one repository context.
    /// </summary>
    public sealed class ModernConfigurationExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ModernConfigurationExtractionRequest"/> class.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that will own emitted graph facts.</param>
        /// <param name="repositoryRootDirectory">The absolute repository root used for configuration artifact discovery and repository-relative evidence paths.</param>
        /// <param name="semanticDocuments">The Roslyn semantic documents that should be inspected for configuration API usage.</param>
        public ModernConfigurationExtractionRequest(StableKey snapshotStableKey, string repositoryRootDirectory, IEnumerable<SemanticExtractionRequest> semanticDocuments)
        {
            // The request validates repository and compiler inputs up front so extraction never emits machine-rooted or ambiguous evidence.
            SnapshotStableKey = snapshotStableKey;
            RepositoryRootDirectory = RequireText(repositoryRootDirectory, nameof(repositoryRootDirectory));
            SemanticDocuments = (semanticDocuments ?? throw new ArgumentNullException(nameof(semanticDocuments))).ToArray();
        }

        /// <summary>
        /// Gets the stable key of the snapshot that will own emitted graph facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the absolute repository root used for configuration artifact discovery and repository-relative evidence paths.
        /// </summary>
        public string RepositoryRootDirectory { get; }

        /// <summary>
        /// Gets the Roslyn semantic documents that should be inspected for configuration API usage.
        /// </summary>
        public IReadOnlyList<SemanticExtractionRequest> SemanticDocuments { get; }

        /// <summary>
        /// Requires non-empty request text before extraction begins.
        /// </summary>
        /// <param name="value">The request text supplied by infrastructure or tests.</param>
        /// <param name="parameterName">The source parameter name used in validation failures.</param>
        /// <returns>The trimmed request text.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Repository paths are evidence inputs, so blank values are rejected at the boundary.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Modern configuration extraction request values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }
    }
}
