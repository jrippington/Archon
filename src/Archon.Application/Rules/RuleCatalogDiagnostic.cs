namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents one deterministic diagnostic produced while loading or validating rule catalog files.
    /// </summary>
    public sealed class RuleCatalogDiagnostic
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleCatalogDiagnostic"/> class.
        /// </summary>
        /// <param name="code">The stable machine-readable diagnostic code.</param>
        /// <param name="message">The developer-facing diagnostic message.</param>
        /// <param name="filePath">The optional rule file path associated with the diagnostic.</param>
        /// <param name="path">The optional JSON contract path associated with the diagnostic.</param>
        /// <param name="lineNumber">The optional one-based JSON line number when parse or reader context provides it.</param>
        /// <param name="bytePositionInLine">The optional zero-based byte position in the JSON line when parse or reader context provides it.</param>
        public RuleCatalogDiagnostic(
            string code,
            string message,
            string? filePath = null,
            string? path = null,
            long? lineNumber = null,
            long? bytePositionInLine = null)
        {
            // Diagnostics are immutable so validation output remains safe to expose across application and test boundaries.
            Code = string.IsNullOrWhiteSpace(code) ? throw new ArgumentException("A rule catalog diagnostic code is required.", nameof(code)) : code.Trim();
            Message = string.IsNullOrWhiteSpace(message) ? throw new ArgumentException("A rule catalog diagnostic message is required.", nameof(message)) : message.Trim();
            FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath.Trim();
            Path = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            LineNumber = lineNumber;
            BytePositionInLine = bytePositionInLine;
        }

        /// <summary>
        /// Gets the stable machine-readable diagnostic code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the developer-facing diagnostic message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the optional rule file path associated with the diagnostic.
        /// </summary>
        public string? FilePath { get; }

        /// <summary>
        /// Gets the optional JSON contract path associated with the diagnostic.
        /// </summary>
        public string? Path { get; }

        /// <summary>
        /// Gets the optional one-based JSON line number when parse or reader context provides it.
        /// </summary>
        public long? LineNumber { get; }

        /// <summary>
        /// Gets the optional zero-based byte position in the JSON line when parse or reader context provides it.
        /// </summary>
        public long? BytePositionInLine { get; }

        /// <summary>
        /// Formats the diagnostic for exception messages, logs, and assertion output.
        /// </summary>
        /// <returns>A deterministic single-line diagnostic string.</returns>
        public override string ToString()
        {
            // The formatted value keeps path and location context stable while omitting absent optional fields.
            string location = FilePath is null ? string.Empty : FilePath;
            if (LineNumber.HasValue)
            {
                location += $":line {LineNumber.Value}";
            }

            if (BytePositionInLine.HasValue)
            {
                location += $":byte {BytePositionInLine.Value}";
            }

            string path = Path is null ? string.Empty : $" [{Path}]";
            string prefix = string.IsNullOrEmpty(location) ? Code : $"{location}: {Code}";
            return $"{prefix}{path}: {Message}";
        }
    }
}
