using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.DataAccess.LinqToSql
{
    /// <summary>
    /// Represents graph snapshot contributions and diagnostics produced by LINQ to SQL DBML model extraction.
    /// </summary>
    public sealed class LinqToSqlDbmlExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LinqToSqlDbmlExtractionResult" /> class.
        /// </summary>
        /// <param name="snapshot">The shared architecture snapshot containing DBML graph contributions and diagnostics.</param>
        public LinqToSqlDbmlExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // The result wraps the shared snapshot contract so pipeline stages can merge DBML facts without a separate persistence path.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the shared architecture snapshot containing DBML graph contributions.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }

        /// <summary>
        /// Gets the non-fatal warnings emitted during DBML extraction.
        /// </summary>
        public IReadOnlyList<string> Warnings
        {
            get
            {
                // Warnings are exposed directly for focused tests and stage diagnostics.
                return Snapshot.Warnings;
            }
        }

        /// <summary>
        /// Gets the fatal errors emitted during DBML extraction.
        /// </summary>
        public IReadOnlyList<string> Errors
        {
            get
            {
                // Errors mirror the shared snapshot error stream without adding extractor-specific wrapping.
                return Snapshot.Errors;
            }
        }
    }
}
