using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.Wpf
{
    /// <summary>
    /// Carries the graph-ready snapshot emitted by the WP011 WPF XAML extraction slice.
    /// </summary>
    public sealed record WpfXamlExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WpfXamlExtractionResult" /> record.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot containing WPF nodes, relationships, evidence, warnings, and errors.</param>
        public WpfXamlExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // Results always expose a snapshot so API stages can merge the same contract whether or not WPF artifacts were found.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot containing WPF nodes, relationships, evidence, warnings, and errors.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }
    }
}
