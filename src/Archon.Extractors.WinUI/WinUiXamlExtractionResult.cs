using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.WinUI
{
    /// <summary>
    /// Carries the graph-ready snapshot emitted by the WP011 WinUI XAML and packaging extraction slice.
    /// </summary>
    public sealed record WinUiXamlExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WinUiXamlExtractionResult" /> record.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot containing WinUI nodes, relationships, evidence, warnings, and errors.</param>
        public WinUiXamlExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // Results always expose a snapshot so API stages can merge the same contract whether or not WinUI artifacts were found.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot containing WinUI nodes, relationships, evidence, warnings, and errors.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }
    }
}
