namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Represents source evidence captured for a semantic declaration or relationship.
    /// </summary>
    /// <remarks>
    /// Evidence records are intentionally source-focused. They preserve the repository-relative file path, line span, symbol context, and deterministic snippet details needed for later graph projection and developer explanation.
    /// </remarks>
    public sealed class SemanticEvidence
    {
        /// <summary>
        /// Initializes a new semantic evidence record.
        /// </summary>
        /// <param name="repositoryRelativeFilePath">The repository-relative source file path using forward slashes.</param>
        /// <param name="startLine">The one-based inclusive starting line for the evidence span.</param>
        /// <param name="endLine">The one-based inclusive ending line for the evidence span.</param>
        /// <param name="startColumn">The one-based inclusive starting column for the evidence span.</param>
        /// <param name="endColumn">The one-based inclusive ending column for the evidence span.</param>
        /// <param name="symbolName">The symbol name associated with the evidence.</param>
        /// <param name="containingSymbolName">The containing symbol name associated with the evidence, when available.</param>
        /// <param name="snippetPreview">The deterministic human-readable preview of the source span.</param>
        /// <param name="snippetHash">The deterministic hash of the source span.</param>
        public SemanticEvidence(
            string repositoryRelativeFilePath,
            int startLine,
            int endLine,
            int startColumn,
            int endColumn,
            string symbolName,
            string? containingSymbolName,
            string? snippetPreview,
            string? snippetHash)
        {
            // Evidence carries source coordinates independently of Roslyn line-position structs so it can flow through graph-ready contracts.
            RepositoryRelativeFilePath = RequireText(repositoryRelativeFilePath, nameof(repositoryRelativeFilePath));
            StartLine = RequirePositive(startLine, nameof(startLine));
            EndLine = RequirePositive(endLine, nameof(endLine));
            StartColumn = RequirePositive(startColumn, nameof(startColumn));
            EndColumn = RequirePositive(endColumn, nameof(endColumn));
            SymbolName = RequireText(symbolName, nameof(symbolName));
            ContainingSymbolName = NormalizeOptionalText(containingSymbolName);
            SnippetPreview = NormalizeOptionalText(snippetPreview);
            SnippetHash = NormalizeOptionalText(snippetHash);

            if (EndLine < StartLine)
            {
                throw new ArgumentOutOfRangeException(nameof(endLine), endLine, "Evidence end line cannot be earlier than the start line.");
            }
        }

        /// <summary>
        /// Gets the repository-relative source file path using forward slashes.
        /// </summary>
        public string RepositoryRelativeFilePath { get; }

        /// <summary>
        /// Gets the one-based inclusive starting line for the evidence span.
        /// </summary>
        public int StartLine { get; }

        /// <summary>
        /// Gets the one-based inclusive ending line for the evidence span.
        /// </summary>
        public int EndLine { get; }

        /// <summary>
        /// Gets the one-based inclusive starting column for the evidence span.
        /// </summary>
        public int StartColumn { get; }

        /// <summary>
        /// Gets the one-based inclusive ending column for the evidence span.
        /// </summary>
        public int EndColumn { get; }

        /// <summary>
        /// Gets the symbol name associated with the evidence.
        /// </summary>
        public string SymbolName { get; }

        /// <summary>
        /// Gets the containing symbol name associated with the evidence, when available.
        /// </summary>
        public string? ContainingSymbolName { get; }

        /// <summary>
        /// Gets the deterministic human-readable preview of the source span.
        /// </summary>
        public string? SnippetPreview { get; }

        /// <summary>
        /// Gets the deterministic hash of the source span.
        /// </summary>
        public string? SnippetHash { get; }

        /// <summary>
        /// Requires non-empty evidence text.
        /// </summary>
        /// <param name="value">The evidence text supplied by an extractor.</param>
        /// <param name="parameterName">The source parameter name used in validation failures.</param>
        /// <returns>The trimmed evidence text.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Evidence must point to a concrete source artifact and symbol, so required text cannot be blank.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Semantic evidence values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }

        /// <summary>
        /// Requires a positive one-based line or column coordinate.
        /// </summary>
        /// <param name="value">The coordinate value supplied by an extractor.</param>
        /// <param name="parameterName">The source parameter name used in validation failures.</param>
        /// <returns>The validated positive coordinate.</returns>
        private static int RequirePositive(int value, string parameterName)
        {
            // Roslyn line positions are zero-based, but graph evidence uses one-based coordinates for developer readability.
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Evidence coordinates must be positive one-based values.");
            }

            return value;
        }

        /// <summary>
        /// Normalizes optional evidence text while preserving null for unavailable source details.
        /// </summary>
        /// <param name="value">The optional evidence text supplied by an extractor.</param>
        /// <returns>The trimmed text, or <see langword="null" /> when the supplied value is blank.</returns>
        private static string? NormalizeOptionalText(string? value)
        {
            // Optional evidence details should not carry whitespace because deterministic comparisons use exact strings.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
