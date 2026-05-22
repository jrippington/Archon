using Archon.Domain.Graph.Identity;
using Archon.Roslyn.SemanticModel;

namespace Archon.Extractors.DataAccess.LinqToSql
{
    /// <summary>
    /// Represents the repository-scoped inputs required to statically extract LINQ to SQL DBML model facts.
    /// </summary>
    public sealed class LinqToSqlDbmlExtractionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LinqToSqlDbmlExtractionRequest" /> class.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that will own emitted graph facts.</param>
        /// <param name="repositoryRootDirectory">The absolute repository root used for DBML file discovery and repository-relative evidence paths.</param>
        /// <param name="semanticDocuments">The optional Roslyn semantic documents used for generated designer and source-usage extraction.</param>
        public LinqToSqlDbmlExtractionRequest(StableKey snapshotStableKey, string repositoryRootDirectory, IEnumerable<SemanticExtractionRequest>? semanticDocuments = null)
        {
            // The request validates evidence-scoping inputs once so extractor logic can focus on deterministic file discovery and parsing.
            SnapshotStableKey = snapshotStableKey;
            RepositoryRootDirectory = RequireText(repositoryRootDirectory, nameof(repositoryRootDirectory));
            SemanticDocuments = semanticDocuments?.ToArray() ?? [];
        }

        /// <summary>
        /// Gets the stable key of the snapshot that will own emitted graph facts.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the absolute repository root used for DBML file discovery and repository-relative evidence paths.
        /// </summary>
        public string RepositoryRootDirectory { get; }

        /// <summary>
        /// Gets the Roslyn semantic documents used for generated designer and source-usage extraction.
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
                throw new ArgumentException("LINQ to SQL DBML extraction request values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }
    }
}
