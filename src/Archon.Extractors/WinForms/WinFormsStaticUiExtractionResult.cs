using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.WinForms
{
    /// <summary>
    /// Carries the graph-ready snapshot emitted by the static UI extraction Windows Forms static UI extraction slice.
    /// </summary>
    public sealed record WinFormsStaticUiExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WinFormsStaticUiExtractionResult" /> record.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot containing Windows Forms nodes, relationships, evidence, warnings, and errors.</param>
        public WinFormsStaticUiExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // Results always expose a snapshot so API stages can merge the same contract whether or not Windows Forms artifacts were found.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the extracted architecture snapshot containing Windows Forms nodes, relationships, evidence, warnings, and errors.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }
    }
}
