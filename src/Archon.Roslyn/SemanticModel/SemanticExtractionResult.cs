namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Represents the immutable output of semantic document extraction.
    /// </summary>
    /// <remarks>
    /// The result separates declarations, relationships, warnings, and errors so callers can accumulate graph facts even when non-fatal extraction diagnostics are present.
    /// </remarks>
    public sealed class SemanticExtractionResult
    {
        /// <summary>
        /// Initializes a new semantic extraction result.
        /// </summary>
        /// <param name="declarations">The declaration facts extracted from the document.</param>
        /// <param name="relationships">The relationship facts extracted from the document.</param>
        /// <param name="warnings">The non-fatal extraction warnings produced during extraction.</param>
        /// <param name="errors">The fatal extraction errors produced during extraction.</param>
        public SemanticExtractionResult(
            IEnumerable<SemanticDeclarationFact>? declarations,
            IEnumerable<SemanticRelationshipFact>? relationships,
            IEnumerable<string>? warnings,
            IEnumerable<string>? errors)
            : this(declarations, relationships, warnings, errors, diagnostics: null, unknowns: null, evidenceContributions: null)
        {
            // This overload preserves the original extraction result shape for callers that have not yet adopted degraded semantic facts.
        }

        /// <summary>
        /// Initializes a new semantic extraction result with degraded semantic facts.
        /// </summary>
        /// <param name="declarations">The declaration facts extracted from the document.</param>
        /// <param name="relationships">The relationship facts extracted from the document.</param>
        /// <param name="warnings">The non-fatal extraction warnings produced during extraction.</param>
        /// <param name="errors">The fatal extraction errors produced during extraction.</param>
        /// <param name="diagnostics">The compiler diagnostics captured during extraction.</param>
        /// <param name="unknowns">The explicit unknown facts captured during extraction.</param>
        /// <param name="evidenceContributions">The additional evidence contributions captured for merged or generated facts.</param>
        public SemanticExtractionResult(
            IEnumerable<SemanticDeclarationFact>? declarations,
            IEnumerable<SemanticRelationshipFact>? relationships,
            IEnumerable<string>? warnings,
            IEnumerable<string>? errors,
            IEnumerable<SemanticDiagnosticFact>? diagnostics,
            IEnumerable<SemanticUnknownFact>? unknowns,
            IEnumerable<SemanticEvidenceContribution>? evidenceContributions)
        {
            // The constructor copies all sequences to prevent later caller mutation from changing extraction output.
            Declarations = CopyFacts(declarations);
            Relationships = CopyFacts(relationships);
            Warnings = CopyDiagnostics(warnings);
            Errors = CopyDiagnostics(errors);
            Diagnostics = CopyFacts(diagnostics);
            Unknowns = CopyFacts(unknowns);
            EvidenceContributions = CopyFacts(evidenceContributions);
        }

        /// <summary>
        /// Gets the declaration facts extracted from the document.
        /// </summary>
        public IReadOnlyList<SemanticDeclarationFact> Declarations { get; }

        /// <summary>
        /// Gets the relationship facts extracted from the document.
        /// </summary>
        public IReadOnlyList<SemanticRelationshipFact> Relationships { get; }

        /// <summary>
        /// Gets the non-fatal extraction warnings produced during extraction.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Gets the fatal extraction errors produced during extraction.
        /// </summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>
        /// Gets the compiler diagnostics captured during extraction.
        /// </summary>
        public IReadOnlyList<SemanticDiagnosticFact> Diagnostics { get; }

        /// <summary>
        /// Gets the explicit unknown facts captured during extraction.
        /// </summary>
        public IReadOnlyList<SemanticUnknownFact> Unknowns { get; }

        /// <summary>
        /// Gets additional evidence spans that contributed to merged or generated facts.
        /// </summary>
        public IReadOnlyList<SemanticEvidenceContribution> EvidenceContributions { get; }

        /// <summary>
        /// Copies a nullable fact sequence into a read-only array.
        /// </summary>
        /// <typeparam name="TFact">The semantic fact type to copy.</typeparam>
        /// <param name="facts">The nullable fact sequence supplied by extraction logic.</param>
        /// <returns>A read-only list containing the supplied facts, or an empty list when the sequence is null.</returns>
        private static IReadOnlyList<TFact> CopyFacts<TFact>(IEnumerable<TFact>? facts)
        {
            // Arrays are compact immutable snapshots when exposed through IReadOnlyList.
            return facts is null ? [] : facts.ToArray();
        }

        /// <summary>
        /// Copies and normalizes diagnostic messages into a read-only array.
        /// </summary>
        /// <param name="diagnostics">The nullable diagnostic sequence supplied by extraction logic.</param>
        /// <returns>A read-only list containing trimmed non-empty diagnostics.</returns>
        private static IReadOnlyList<string> CopyDiagnostics(IEnumerable<string>? diagnostics)
        {
            // Blank diagnostics do not help callers explain extraction behavior and are omitted.
            return diagnostics is null
                ? []
                : diagnostics.Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic)).Select(diagnostic => diagnostic.Trim()).ToArray();
        }
    }
}
