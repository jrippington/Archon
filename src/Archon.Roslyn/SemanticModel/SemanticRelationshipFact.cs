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
        {
            // Relationships are immutable stable-key triples plus evidence so duplicate containment paths can be merged deterministically.
            StableKey = RequireText(stableKey, nameof(stableKey));
            RelationshipKind = relationshipKind;
            SourceStableKey = RequireText(sourceStableKey, nameof(sourceStableKey));
            TargetStableKey = RequireText(targetStableKey, nameof(targetStableKey));
            Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
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
    }
}
