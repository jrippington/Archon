using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.Avalonia
{
    /// <summary>
    /// Carries the graph-ready snapshot emitted by the WP011 Avalonia AXAML extraction slice.
    /// </summary>
    public sealed record AvaloniaAxamlExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AvaloniaAxamlExtractionResult" /> record.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot containing Avalonia nodes, relationships, evidence, warnings, and errors.</param>
        public AvaloniaAxamlExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // Results always expose a snapshot so API stages can merge the same contract whether or not Avalonia artifacts were found.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot containing Avalonia nodes, relationships, evidence, warnings, and errors.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }
    }
}
