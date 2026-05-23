using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.Razor
{
    /// <summary>
    /// Carries the graph-ready snapshot emitted by the WP011 Razor Pages and MVC Razor extraction slice.
    /// </summary>
    public sealed record RazorPageViewExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RazorPageViewExtractionResult" /> record.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot containing Razor nodes, relationships, evidence, warnings, and errors.</param>
        public RazorPageViewExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // Results always expose a snapshot so callers can merge a consistent contract even when no `.cshtml` files were found.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot containing Razor nodes, relationships, evidence, warnings, and errors.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }
    }
}