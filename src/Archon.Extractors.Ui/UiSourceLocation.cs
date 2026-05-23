namespace Archon.Extractors.Ui
{
    /// <summary>
    /// Describes the repository-relative source location and snippet that support a UI extraction fact.
    /// </summary>
    public sealed record UiSourceLocation
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UiSourceLocation" /> record.
        /// </summary>
        /// <param name="relativePath">The repository-relative artifact path that contains the evidence.</param>
        /// <param name="startLine">The one-based starting line for the evidence span.</param>
        /// <param name="endLine">The one-based ending line for the evidence span.</param>
        /// <param name="snippet">The source or markup snippet that should be hashed and previewed after redaction.</param>
        public UiSourceLocation(string relativePath, int? startLine, int? endLine, string snippet)
        {
            // UI evidence must be tied to a repository artifact and a snippet so previews and stable hashes remain explainable.
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException("UI source locations require a repository-relative path.", nameof(relativePath));
            }

            if (string.IsNullOrWhiteSpace(snippet))
            {
                throw new ArgumentException("UI source locations require a non-empty source snippet.", nameof(snippet));
            }

            if (startLine.HasValue && startLine.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(startLine), startLine.Value, "Evidence start lines are one-based and must be positive.");
            }

            if (endLine.HasValue && endLine.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(endLine), endLine.Value, "Evidence end lines are one-based and must be positive.");
            }

            if (startLine.HasValue && endLine.HasValue && endLine.Value < startLine.Value)
            {
                throw new ArgumentException("Evidence end lines must be greater than or equal to start lines.", nameof(endLine));
            }

            RelativePath = relativePath.Trim();
            StartLine = startLine;
            EndLine = endLine;
            Snippet = snippet.Trim();
        }

        /// <summary>
        /// Gets the repository-relative artifact path that contains the evidence.
        /// </summary>
        public string RelativePath { get; }

        /// <summary>
        /// Gets the one-based starting line for the evidence span.
        /// </summary>
        public int? StartLine { get; }

        /// <summary>
        /// Gets the one-based ending line for the evidence span.
        /// </summary>
        public int? EndLine { get; }

        /// <summary>
        /// Gets the source or markup snippet that should be hashed and previewed after redaction.
        /// </summary>
        public string Snippet { get; }
    }
}