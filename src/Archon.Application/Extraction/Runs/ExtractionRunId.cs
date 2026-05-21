namespace Archon.Application.Extraction.Runs
{
    /// <summary>
    /// Represents the stable public identifier for an extraction run.
    /// </summary>
    /// <param name="Value">The underlying globally unique run identifier value.</param>
    public readonly record struct ExtractionRunId(Guid Value)
    {
        /// <summary>
        /// Creates a new extraction run identifier for an accepted request.
        /// </summary>
        /// <returns>A new non-empty extraction run identifier.</returns>
        public static ExtractionRunId New()
        {
            // Run identifiers are intentionally opaque so API consumers cannot infer filesystem or persistence details.
            return new ExtractionRunId(Guid.NewGuid());
        }

        /// <summary>
        /// Parses a text value into an extraction run identifier when possible.
        /// </summary>
        /// <param name="value">The candidate identifier text supplied by a caller.</param>
        /// <param name="runId">The parsed run identifier when parsing succeeds.</param>
        /// <returns><see langword="true"/> when the text contains a valid GUID; otherwise <see langword="false"/>.</returns>
        public static bool TryParse(string? value, out ExtractionRunId runId)
        {
            // Route translation uses this helper to keep invalid identifiers from throwing in endpoint handlers.
            if (Guid.TryParse(value, out Guid guid))
            {
                runId = new ExtractionRunId(guid);
                return true;
            }

            runId = default;
            return false;
        }

        /// <summary>
        /// Formats the run identifier using the canonical GUID representation.
        /// </summary>
        /// <returns>The canonical run identifier text.</returns>
        public override string ToString()
        {
            // The D format is stable and easy for API clients to copy into status URLs.
            return Value.ToString("D");
        }
    }
}
