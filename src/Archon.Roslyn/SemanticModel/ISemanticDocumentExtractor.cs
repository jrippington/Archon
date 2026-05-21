namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Defines the language-specific operation that extracts semantic facts from one Roslyn document.
    /// </summary>
    /// <remarks>
    /// The interface belongs to the shared Roslyn layer so infrastructure and orchestration code can invoke C# and Visual Basic extractors through the same contract.
    /// </remarks>
    public interface ISemanticDocumentExtractor
    {
        /// <summary>
        /// Extracts declaration and relationship facts from one semantic document request.
        /// </summary>
        /// <param name="request">The document, semantic model, and repository context to analyze.</param>
        /// <param name="cancellationToken">A token that signals when extraction should stop before additional semantic work is performed.</param>
        /// <returns>The semantic extraction result containing graph-ready facts and diagnostics.</returns>
        SemanticExtractionResult Extract(SemanticExtractionRequest request, CancellationToken cancellationToken = default);
    }
}
