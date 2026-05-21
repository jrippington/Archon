namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Represents an additional evidence span that contributed to an already stable semantic fact.
    /// </summary>
    /// <remarks>
    /// Partial declarations and generated companion files can contribute more than one source span to one graph identity. This model preserves each contribution without duplicating the declaration node.
    /// </remarks>
    public sealed class SemanticEvidenceContribution
    {
        /// <summary>
        /// Initializes a new semantic evidence contribution.
        /// </summary>
        /// <param name="factStableKey">The stable key of the declaration or relationship that the evidence contributes to.</param>
        /// <param name="evidence">The additional source evidence span.</param>
        /// <param name="generated">A value indicating whether the contributing source span came from generated code.</param>
        /// <param name="contributionKind">The deterministic classification of the evidence contribution.</param>
        public SemanticEvidenceContribution(string factStableKey, SemanticEvidence evidence, bool generated, string contributionKind)
        {
            // Contributions attach extra source spans to a stable fact while preserving generated-code classification for downstream graph projection.
            FactStableKey = RequireText(factStableKey, nameof(factStableKey));
            Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
            Generated = generated;
            ContributionKind = RequireText(contributionKind, nameof(contributionKind));
        }

        /// <summary>
        /// Gets the stable key of the declaration or relationship that the evidence contributes to.
        /// </summary>
        public string FactStableKey { get; }

        /// <summary>
        /// Gets the additional source evidence span.
        /// </summary>
        public SemanticEvidence Evidence { get; }

        /// <summary>
        /// Gets a value indicating whether the contributing source span came from generated code.
        /// </summary>
        public bool Generated { get; }

        /// <summary>
        /// Gets the deterministic classification of the evidence contribution.
        /// </summary>
        public string ContributionKind { get; }

        /// <summary>
        /// Requires non-empty evidence contribution text.
        /// </summary>
        /// <param name="value">The contribution text supplied by extraction logic.</param>
        /// <param name="parameterName">The source parameter name used in validation failures.</param>
        /// <returns>The trimmed contribution text.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Contributions must identify the owning fact and contribution kind to remain useful after accumulation.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Semantic evidence contribution values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }
    }
}
