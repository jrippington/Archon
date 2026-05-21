namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Represents a graph-ready relationship fact extracted from semantic declaration structure.
    /// </summary>
    /// <remarks>
    /// Relationship facts keep source and target stable keys explicit so later graph projection can add domain edges without depending on Roslyn compiler object identity.
    /// </remarks>
    public sealed class SemanticRelationshipFact
    {
        /// <summary>
        /// Stores an empty metadata dictionary for relationship facts that do not need supplemental classification fields.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new semantic relationship fact.
        /// </summary>
        /// <param name="stableKey">The deterministic stable key for the relationship.</param>
        /// <param name="relationshipKind">The semantic relationship category projected into graph vocabulary.</param>
        /// <param name="sourceStableKey">The stable key of the source declaration.</param>
        /// <param name="targetStableKey">The stable key of the target declaration.</param>
        /// <param name="evidence">The source evidence that explains the relationship.</param>
        public SemanticRelationshipFact(
            string stableKey,
            SemanticRelationshipKind relationshipKind,
            string sourceStableKey,
            string targetStableKey,
            SemanticEvidence evidence)
            : this(stableKey, relationshipKind, sourceStableKey, targetStableKey, evidence, SemanticFactConfidence.CompilerResolved, sourceSymbolIdentity: null, targetSymbolIdentity: null, metadata: null, unknownReason: null)
        {
            // This overload preserves the Work Item 1 containment creation path while assigning high confidence to compiler-resolved containment facts.
        }

        /// <summary>
        /// Initializes a new semantic relationship fact with endpoint identities, confidence, metadata, and optional unknown-reason context.
        /// </summary>
        /// <param name="stableKey">The deterministic stable key for the relationship.</param>
        /// <param name="relationshipKind">The semantic relationship category projected into graph vocabulary.</param>
        /// <param name="sourceStableKey">The stable key of the source declaration or source symbol surrogate.</param>
        /// <param name="targetStableKey">The stable key of the target declaration or target symbol surrogate.</param>
        /// <param name="evidence">The source evidence that explains the relationship.</param>
        /// <param name="confidence">The confidence category assigned to the relationship.</param>
        /// <param name="sourceSymbolIdentity">The source symbol identity resolved by the extractor, when available.</param>
        /// <param name="targetSymbolIdentity">The target symbol identity resolved by the extractor, when available.</param>
        /// <param name="metadata">Supplemental deterministic relationship metadata used by downstream graph projection.</param>
        /// <param name="unknownReason">The reason the relationship could not be fully resolved, when applicable.</param>
        public SemanticRelationshipFact(
            string stableKey,
            SemanticRelationshipKind relationshipKind,
            string sourceStableKey,
            string targetStableKey,
            SemanticEvidence evidence,
            SemanticFactConfidence confidence,
            SemanticSymbolIdentity? sourceSymbolIdentity,
            SemanticSymbolIdentity? targetSymbolIdentity,
            IReadOnlyDictionary<string, string>? metadata,
            string? unknownReason)
        {
            // Relationships are immutable stable-key triples plus evidence and endpoint identity so duplicate semantic discoveries can be merged deterministically.
            StableKey = RequireText(stableKey, nameof(stableKey));
            RelationshipKind = relationshipKind;
            SourceStableKey = RequireText(sourceStableKey, nameof(sourceStableKey));
            TargetStableKey = RequireText(targetStableKey, nameof(targetStableKey));
            Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
            Confidence = confidence;
            SourceSymbolIdentity = sourceSymbolIdentity;
            TargetSymbolIdentity = targetSymbolIdentity;
            Metadata = CopyMetadata(metadata);
            UnknownReason = NormalizeOptionalText(unknownReason);
        }

        /// <summary>
        /// Gets the deterministic stable key for the relationship.
        /// </summary>
        public string StableKey { get; }

        /// <summary>
        /// Gets the semantic relationship category projected into graph vocabulary.
        /// </summary>
        public SemanticRelationshipKind RelationshipKind { get; }

        /// <summary>
        /// Gets the stable key of the source declaration.
        /// </summary>
        public string SourceStableKey { get; }

        /// <summary>
        /// Gets the stable key of the target declaration.
        /// </summary>
        public string TargetStableKey { get; }

        /// <summary>
        /// Gets the source evidence that explains the relationship.
        /// </summary>
        public SemanticEvidence Evidence { get; }

        /// <summary>
        /// Gets the confidence category assigned to the relationship.
        /// </summary>
        public SemanticFactConfidence Confidence { get; }

        /// <summary>
        /// Gets the source symbol identity resolved by the extractor, when available.
        /// </summary>
        public SemanticSymbolIdentity? SourceSymbolIdentity { get; }

        /// <summary>
        /// Gets the target symbol identity resolved by the extractor, when available.
        /// </summary>
        public SemanticSymbolIdentity? TargetSymbolIdentity { get; }

        /// <summary>
        /// Gets supplemental deterministic metadata used to classify how the relationship was discovered.
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; }

        /// <summary>
        /// Gets the reason the relationship could not be fully resolved, when applicable.
        /// </summary>
        public string? UnknownReason { get; }

        /// <summary>
        /// Copies relationship metadata into a deterministic read-only dictionary.
        /// </summary>
        /// <param name="metadata">The metadata supplied by extraction logic.</param>
        /// <returns>A read-only metadata dictionary ordered by key.</returns>
        private static IReadOnlyDictionary<string, string> CopyMetadata(IReadOnlyDictionary<string, string>? metadata)
        {
            // Metadata participates in assertions and persistence payloads, so keys are ordered and blank entries are removed at the model boundary.
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
        /// Requires non-empty relationship fact text.
        /// </summary>
        /// <param name="value">The fact text supplied by an extractor.</param>
        /// <param name="parameterName">The source parameter name used in validation failures.</param>
        /// <returns>The trimmed fact text.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Relationship endpoints and keys must be present so graph containment remains traversable.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Semantic relationship fact values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }

        /// <summary>
        /// Normalizes optional relationship fact text.
        /// </summary>
        /// <param name="value">The optional relationship fact text supplied by an extractor.</param>
        /// <returns>The trimmed text, or <see langword="null" /> when the supplied value is blank.</returns>
        private static string? NormalizeOptionalText(string? value)
        {
            // Optional text should not preserve whitespace-only values because graph payload comparisons are exact.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
