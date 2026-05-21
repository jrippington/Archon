namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Represents a graph-ready declaration fact extracted from a Roslyn semantic model.
    /// </summary>
    /// <remarks>
    /// Declaration facts are the language-neutral output of C# and Visual Basic extractors. They can be projected into domain graph nodes without preserving Roslyn compiler object references.
    /// </remarks>
    public sealed class SemanticDeclarationFact
    {
        /// <summary>
        /// Initializes a new semantic declaration fact.
        /// </summary>
        /// <param name="stableKey">The deterministic stable key for the declaration node.</param>
        /// <param name="declarationKind">The declaration category projected into the graph vocabulary.</param>
        /// <param name="sourceLanguage">The source language that produced the fact.</param>
        /// <param name="symbolIdentity">The deterministic symbol identity for the declaration.</param>
        /// <param name="projectContext">The logical project context supplied by the extraction caller.</param>
        /// <param name="parentStableKey">The stable key of the containing declaration, or <see langword="null" /> for root declarations.</param>
        /// <param name="evidence">The source evidence that explains the declaration.</param>
        public SemanticDeclarationFact(
            string stableKey,
            SemanticDeclarationKind declarationKind,
            SourceLanguage sourceLanguage,
            SemanticSymbolIdentity symbolIdentity,
            string projectContext,
            string? parentStableKey,
            SemanticEvidence evidence)
            : this(stableKey, declarationKind, sourceLanguage, symbolIdentity, projectContext, parentStableKey, evidence, SemanticFactConfidence.CompilerResolved, metadata: null)
        {
            // This overload preserves the original Work Item 1 declaration creation path while assigning compiler-resolved confidence.
        }

        /// <summary>
        /// Initializes a new semantic declaration fact with confidence and metadata.
        /// </summary>
        /// <param name="stableKey">The deterministic stable key for the declaration node.</param>
        /// <param name="declarationKind">The declaration category projected into the graph vocabulary.</param>
        /// <param name="sourceLanguage">The source language that produced the fact.</param>
        /// <param name="symbolIdentity">The deterministic symbol identity for the declaration.</param>
        /// <param name="projectContext">The logical project context supplied by the extraction caller.</param>
        /// <param name="parentStableKey">The stable key of the containing declaration, or <see langword="null" /> for root declarations.</param>
        /// <param name="evidence">The source evidence that explains the declaration.</param>
        /// <param name="confidence">The confidence category assigned to the declaration.</param>
        /// <param name="metadata">Supplemental deterministic declaration metadata.</param>
        public SemanticDeclarationFact(
            string stableKey,
            SemanticDeclarationKind declarationKind,
            SourceLanguage sourceLanguage,
            SemanticSymbolIdentity symbolIdentity,
            string projectContext,
            string? parentStableKey,
            SemanticEvidence evidence,
            SemanticFactConfidence confidence,
            IReadOnlyDictionary<string, string>? metadata)
        {
            // The fact captures graph identity, symbol identity, ownership, and evidence in one immutable unit for deterministic accumulation.
            StableKey = RequireText(stableKey, nameof(stableKey));
            DeclarationKind = declarationKind;
            SourceLanguage = sourceLanguage;
            SymbolIdentity = symbolIdentity ?? throw new ArgumentNullException(nameof(symbolIdentity));
            ProjectContext = RequireText(projectContext, nameof(projectContext));
            ParentStableKey = NormalizeOptionalText(parentStableKey);
            Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
            Confidence = confidence;
            Metadata = CopyMetadata(metadata);
        }

        /// <summary>
        /// Gets the deterministic stable key for the declaration node.
        /// </summary>
        public string StableKey { get; }

        /// <summary>
        /// Gets the declaration category projected into the graph vocabulary.
        /// </summary>
        public SemanticDeclarationKind DeclarationKind { get; }

        /// <summary>
        /// Gets the source language that produced the fact.
        /// </summary>
        public SourceLanguage SourceLanguage { get; }

        /// <summary>
        /// Gets the deterministic symbol identity for the declaration.
        /// </summary>
        public SemanticSymbolIdentity SymbolIdentity { get; }

        /// <summary>
        /// Gets the logical project context supplied by the extraction caller.
        /// </summary>
        public string ProjectContext { get; }

        /// <summary>
        /// Gets the stable key of the containing declaration, or <see langword="null" /> for root declarations.
        /// </summary>
        public string? ParentStableKey { get; }

        /// <summary>
        /// Gets the source evidence that explains the declaration.
        /// </summary>
        public SemanticEvidence Evidence { get; }

        /// <summary>
        /// Gets the confidence category assigned to the declaration.
        /// </summary>
        public SemanticFactConfidence Confidence { get; }

        /// <summary>
        /// Gets supplemental deterministic declaration metadata.
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; }

        /// <summary>
        /// Copies declaration metadata into a deterministic read-only dictionary.
        /// </summary>
        /// <param name="metadata">The metadata supplied by extraction logic.</param>
        /// <returns>A read-only metadata dictionary ordered by key.</returns>
        private static IReadOnlyDictionary<string, string> CopyMetadata(IReadOnlyDictionary<string, string>? metadata)
        {
            // Declaration metadata identifies generated and partial source facts, so keys are ordered and blank entries are removed.
            if (metadata is null || metadata.Count == 0)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return metadata
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value.Trim(), StringComparer.Ordinal);
        }

        /// <summary>
        /// Requires non-empty declaration fact text.
        /// </summary>
        /// <param name="value">The fact text supplied by an extractor.</param>
        /// <param name="parameterName">The source parameter name used in validation failures.</param>
        /// <returns>The trimmed fact text.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Declaration facts must have explicit stable keys and project context before they can be accumulated reliably.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Semantic declaration fact values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }

        /// <summary>
        /// Normalizes optional declaration fact text.
        /// </summary>
        /// <param name="value">The optional fact text supplied by an extractor.</param>
        /// <returns>The trimmed text, or <see langword="null" /> when the supplied value is blank.</returns>
        private static string? NormalizeOptionalText(string? value)
        {
            // Optional fact fields should not carry whitespace because stable-key comparisons are exact string comparisons.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
