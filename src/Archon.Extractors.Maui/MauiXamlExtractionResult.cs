using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.Maui
{
    /// <summary>
    /// Carries the graph-ready snapshot emitted by the WP011 .NET MAUI XAML extraction slice.
    /// </summary>
    public sealed record MauiXamlExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MauiXamlExtractionResult" /> record.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot containing MAUI nodes, relationships, evidence, warnings, and errors.</param>
        public MauiXamlExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // Results always expose a snapshot so API stages can merge the same contract whether or not MAUI artifacts were found.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot containing MAUI nodes, relationships, evidence, warnings, and errors.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }
    }
}
