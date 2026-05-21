namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Represents a graph-ready unknown semantic outcome produced when static extraction cannot resolve a target or semantic form.
    /// </summary>
    /// <remarks>
    /// Unknown facts are deliberate first-class records. They prevent unresolved, dynamic, reflection-based, late-bound, or unsupported source patterns from disappearing silently from architecture analysis.
    /// </remarks>
    public sealed class SemanticUnknownFact
    {
        /// <summary>
        /// Stores an empty metadata dictionary for unknown facts that do not need supplemental fields.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new semantic unknown fact.
        /// </summary>
        /// <param name="stableKey">The deterministic stable key for the unknown fact.</param>
        /// <param name="reason">The normalized reason semantic extraction could not resolve the fact.</param>
        /// <param name="sourceLanguage">The source language that produced the unknown.</param>
        /// <param name="projectContext">The logical project context supplied by the extraction caller.</param>
        /// <param name="sourceSymbolIdentity">The source symbol identity that owns the unknown, when available.</param>
        /// <param name="description">The human-readable unknown description.</param>
        /// <param name="evidence">The source evidence associated with the unknown.</param>
        /// <param name="confidence">The confidence category assigned to the unknown.</param>
        /// <param name="metadata">Additional deterministic unknown metadata.</param>
        public SemanticUnknownFact(
            string stableKey,
            SemanticUnknownReason reason,
            SourceLanguage sourceLanguage,
            string projectContext,
            SemanticSymbolIdentity? sourceSymbolIdentity,
            string description,
            SemanticEvidence evidence,
            SemanticFactConfidence confidence,
            IReadOnlyDictionary<string, string>? metadata)
        {
            // Unknowns still need stable identity, evidence, and source context so consumers can query and explain degraded semantic extraction.
            StableKey = RequireText(stableKey, nameof(stableKey));
            Reason = reason;
            SourceLanguage = sourceLanguage;
            ProjectContext = RequireText(projectContext, nameof(projectContext));
            SourceSymbolIdentity = sourceSymbolIdentity;
            Description = RequireText(description, nameof(description));
            Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
            Confidence = confidence;
            Metadata = CopyMetadata(metadata);
        }

        /// <summary>
        /// Gets the deterministic stable key for the unknown fact.
        /// </summary>
        public string StableKey { get; }

        /// <summary>
        /// Gets the normalized reason semantic extraction could not resolve the fact.
        /// </summary>
        public SemanticUnknownReason Reason { get; }

        /// <summary>
        /// Gets the source language that produced the unknown.
        /// </summary>
        public SourceLanguage SourceLanguage { get; }

        /// <summary>
        /// Gets the logical project context supplied by the extraction caller.
        /// </summary>
        public string ProjectContext { get; }

        /// <summary>
        /// Gets the source symbol identity that owns the unknown, when available.
        /// </summary>
        public SemanticSymbolIdentity? SourceSymbolIdentity { get; }

        /// <summary>
        /// Gets the human-readable unknown description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the source evidence associated with the unknown.
        /// </summary>
        public SemanticEvidence Evidence { get; }

        /// <summary>
        /// Gets the confidence category assigned to the unknown.
        /// </summary>
        public SemanticFactConfidence Confidence { get; }

        /// <summary>
        /// Gets deterministic supplemental unknown metadata.
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; }

        /// <summary>
        /// Copies metadata into a deterministic read-only dictionary.
        /// </summary>
        /// <param name="metadata">The metadata supplied by extraction logic.</param>
        /// <returns>A read-only dictionary ordered by key.</returns>
        private static IReadOnlyDictionary<string, string> CopyMetadata(IReadOnlyDictionary<string, string>? metadata)
        {
            // Metadata may identify the syntax operation or compiler candidate reason, so it is normalized at the model boundary.
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
        /// Requires non-empty unknown fact text.
        /// </summary>
        /// <param name="value">The unknown text supplied by extraction logic.</param>
        /// <param name="parameterName">The source parameter name used in validation failures.</param>
        /// <returns>The trimmed unknown text.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Unknown records are only useful when their identity and description can be explained to a contributor.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Semantic unknown values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }
    }
}
