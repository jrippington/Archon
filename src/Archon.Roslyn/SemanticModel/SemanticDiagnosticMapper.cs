using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Converts Roslyn compiler diagnostics into graph-ready semantic diagnostic facts.
    /// </summary>
    /// <remarks>
    /// The mapper is shared by C# and Visual Basic extraction so diagnostic evidence, severity mapping, and stable metadata are normalized consistently across languages.
    /// </remarks>
    public static class SemanticDiagnosticMapper
    {
        /// <summary>
        /// Creates a semantic diagnostic fact from a Roslyn diagnostic and extraction request.
        /// </summary>
        /// <param name="diagnostic">The Roslyn diagnostic to normalize.</param>
        /// <param name="request">The extraction request that provides repository and syntax-tree context.</param>
        /// <param name="compilerSource">The compiler source label to store with the diagnostic.</param>
        /// <param name="cancellationToken">A token that signals when source text access should stop.</param>
        /// <returns>A normalized semantic diagnostic fact.</returns>
        public static SemanticDiagnosticFact FromDiagnostic(Diagnostic diagnostic, SemanticExtractionRequest request, string compilerSource, CancellationToken cancellationToken = default)
        {
            // Diagnostics can be source-bound or project-wide. Source-bound diagnostics use their Roslyn location; project-wide diagnostics fall back to the document root span.
            ArgumentNullException.ThrowIfNull(diagnostic);
            ArgumentNullException.ThrowIfNull(request);
            Location location = diagnostic.Location;
            TextSpan span = location.IsInSource ? location.SourceSpan : default;
            FileLinePositionSpan lineSpan = location.IsInSource ? location.GetLineSpan() : request.SyntaxTree.GetLineSpan(span, cancellationToken);
            LinePosition start = lineSpan.StartLinePosition;
            LinePosition end = lineSpan.EndLinePosition;
            string repositoryRelativePath = SemanticPathNormalizer.ToRepositoryRelativePath(request.RepositoryRootDirectory, location.IsInSource ? lineSpan.Path : request.DocumentPath);
            (string? preview, string? hash) = SemanticSnippetBuilder.CreateSnippet(request.SyntaxTree, span, cancellationToken);
            SemanticEvidence evidence = new(
                repositoryRelativePath,
                Math.Max(start.Line + 1, 1),
                Math.Max(end.Line + 1, start.Line + 1),
                Math.Max(start.Character + 1, 1),
                Math.Max(end.Character + 1, start.Character + 1),
                diagnostic.Id,
                compilerSource,
                preview ?? diagnostic.GetMessage(),
                hash);
            Dictionary<string, string> metadata = new(StringComparer.Ordinal)
            {
                ["compilerSource"] = compilerSource,
                ["category"] = diagnostic.Descriptor.Category,
                ["warningLevel"] = diagnostic.WarningLevel.ToString()
            };

            return new SemanticDiagnosticFact(diagnostic.Id, MapSeverity(diagnostic.Severity), diagnostic.GetMessage(), compilerSource, evidence, metadata);
        }

        /// <summary>
        /// Maps Roslyn diagnostic severities into the shared semantic severity enum.
        /// </summary>
        /// <param name="severity">The Roslyn diagnostic severity.</param>
        /// <returns>The normalized semantic diagnostic severity.</returns>
        private static SemanticDiagnosticSeverity MapSeverity(DiagnosticSeverity severity)
        {
            // The switch is explicit so future Roslyn severity additions fail as compiler warnings rather than silently defaulting.
            return severity switch
            {
                DiagnosticSeverity.Hidden => SemanticDiagnosticSeverity.Hidden,
                DiagnosticSeverity.Info => SemanticDiagnosticSeverity.Info,
                DiagnosticSeverity.Warning => SemanticDiagnosticSeverity.Warning,
                DiagnosticSeverity.Error => SemanticDiagnosticSeverity.Error,
                _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unsupported Roslyn diagnostic severity.")
            };
        }
    }
}
