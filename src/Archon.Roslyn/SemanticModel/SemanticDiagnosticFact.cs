namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Represents a graph-ready compiler diagnostic captured during semantic extraction.
    /// </summary>
    /// <remarks>
    /// Diagnostics are stored separately from warning strings because they carry stable compiler identity, source evidence, severity, and deterministic metadata that downstream graph consumers can query.
    /// </remarks>
    public sealed class SemanticDiagnosticFact
    {
        /// <summary>
        /// Stores an empty metadata dictionary for diagnostics that do not need supplemental fields.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new semantic diagnostic fact.
        /// </summary>
        /// <param name="diagnosticId">The compiler diagnostic identifier such as CS0246 or BC30002.</param>
        /// <param name="severity">The normalized compiler diagnostic severity.</param>
        /// <param name="message">The human-readable compiler diagnostic message.</param>
        /// <param name="compilerSource">The compiler or analyzer source that produced the diagnostic.</param>
        /// <param name="evidence">The source evidence associated with the diagnostic span.</param>
        /// <param name="metadata">Additional deterministic diagnostic metadata.</param>
        public SemanticDiagnosticFact(
            string diagnosticId,
            SemanticDiagnosticSeverity severity,
            string message,
            string compilerSource,
            SemanticEvidence evidence,
            IReadOnlyDictionary<string, string>? metadata)
        {
            // The constructor normalizes all string and metadata fields so diagnostic payloads are safe to compare across extraction runs.
            DiagnosticId = RequireText(diagnosticId, nameof(diagnosticId));
            Severity = severity;
            Message = RequireText(message, nameof(message));
            CompilerSource = RequireText(compilerSource, nameof(compilerSource));
            Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
            Metadata = CopyMetadata(metadata);
        }

        /// <summary>
        /// Gets the compiler diagnostic identifier such as CS0246 or BC30002.
        /// </summary>
        public string DiagnosticId { get; }

        /// <summary>
        /// Gets the normalized compiler diagnostic severity.
        /// </summary>
        public SemanticDiagnosticSeverity Severity { get; }

        /// <summary>
        /// Gets the human-readable compiler diagnostic message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the compiler or analyzer source that produced the diagnostic.
        /// </summary>
        public string CompilerSource { get; }

        /// <summary>
        /// Gets the source evidence associated with the diagnostic span.
        /// </summary>
        public SemanticEvidence Evidence { get; }

        /// <summary>
        /// Gets deterministic supplemental diagnostic metadata.
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; }

        /// <summary>
        /// Copies metadata into a deterministic read-only dictionary.
        /// </summary>
        /// <param name="metadata">The metadata supplied by extraction logic.</param>
        /// <returns>A read-only dictionary ordered by key.</returns>
        private static IReadOnlyDictionary<string, string> CopyMetadata(IReadOnlyDictionary<string, string>? metadata)
        {
            // Metadata may be persisted or asserted directly, so blank entries are removed and keys are ordered.
            if (metadata is null || metadata.Count == 0)
            {
                return EmptyMetadata;
            }

            return metadata
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value.Trim(), StringComparer.Ordinal);
        }

        /// <summary>
        /// Requires non-empty diagnostic text.
        /// </summary>
        /// <param name="value">The diagnostic text supplied by extraction logic.</param>
        /// <param name="parameterName">The source parameter name used in validation failures.</param>
        /// <returns>The trimmed diagnostic text.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Diagnostic records must remain explainable, so required fields cannot be missing.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Semantic diagnostic values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }
    }
}
