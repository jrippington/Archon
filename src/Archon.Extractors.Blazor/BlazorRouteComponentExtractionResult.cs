using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.Blazor
{
    /// <summary>
    /// Carries the graph-ready snapshot emitted by the WP011 Blazor route and component extraction slice.
    /// </summary>
    public sealed record BlazorRouteComponentExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BlazorRouteComponentExtractionResult" /> record.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot containing Blazor nodes, relationships, evidence, warnings, and errors.</param>
        public BlazorRouteComponentExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // Results always expose a snapshot so callers can merge the same contract regardless of whether any Blazor files were found.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot containing Blazor nodes, relationships, evidence, warnings, and errors.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }
    }
}