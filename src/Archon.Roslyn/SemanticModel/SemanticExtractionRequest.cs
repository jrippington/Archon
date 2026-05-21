using Microsoft.CodeAnalysis;

namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Represents the language-neutral inputs required to extract semantic facts from one Roslyn document.
    /// </summary>
    /// <remarks>
    /// The request keeps workspace loading outside the shared extractor contracts. Infrastructure can supply compilations, semantic models, documents, and repository context, while language extractors focus on symbol projection.
    /// </remarks>
    public sealed class SemanticExtractionRequest
    {
        /// <summary>
        /// Initializes a new semantic extraction request.
        /// </summary>
        /// <param name="repositoryRootDirectory">The absolute repository root used only to derive repository-relative evidence paths.</param>
        /// <param name="projectContext">The logical project context used to scope stable symbol keys.</param>
        /// <param name="documentPath">The source document path associated with the syntax tree.</param>
        /// <param name="syntaxTree">The Roslyn syntax tree to inspect.</param>
        /// <param name="semanticModel">The Roslyn semantic model for the syntax tree.</param>
        public SemanticExtractionRequest(
            string repositoryRootDirectory,
            string projectContext,
            string documentPath,
            SyntaxTree syntaxTree,
            Microsoft.CodeAnalysis.SemanticModel semanticModel)
        {
            // The request validates the compiler inputs together so extractors cannot accidentally combine a tree with the wrong semantic model.
            RepositoryRootDirectory = RequireText(repositoryRootDirectory, nameof(repositoryRootDirectory));
            ProjectContext = RequireText(projectContext, nameof(projectContext));
            DocumentPath = RequireText(documentPath, nameof(documentPath));
            SyntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
            SemanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));

            if (!ReferenceEquals(SyntaxTree, SemanticModel.SyntaxTree))
            {
                throw new ArgumentException("The semantic model must belong to the supplied syntax tree.", nameof(semanticModel));
            }
        }

        /// <summary>
        /// Gets the absolute repository root used only to derive repository-relative evidence paths.
        /// </summary>
        public string RepositoryRootDirectory { get; }

        /// <summary>
        /// Gets the logical project context used to scope stable symbol keys.
        /// </summary>
        public string ProjectContext { get; }

        /// <summary>
        /// Gets the source document path associated with the syntax tree.
        /// </summary>
        public string DocumentPath { get; }

        /// <summary>
        /// Gets the Roslyn syntax tree to inspect.
        /// </summary>
        public SyntaxTree SyntaxTree { get; }

        /// <summary>
        /// Gets the Roslyn semantic model for the syntax tree.
        /// </summary>
        public Microsoft.CodeAnalysis.SemanticModel SemanticModel { get; }

        /// <summary>
        /// Requires non-empty request text before extraction begins.
        /// </summary>
        /// <param name="value">The request text supplied by infrastructure or tests.</param>
        /// <param name="parameterName">The source parameter name used in validation failures.</param>
        /// <returns>The trimmed request text.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Request context fields become evidence and stable-key inputs, so blank values are rejected at the boundary.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Semantic extraction request values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }
    }
}
