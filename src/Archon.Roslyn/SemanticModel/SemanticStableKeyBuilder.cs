using System.Security.Cryptography;
using System.Text;

namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Builds deterministic stable keys for semantic declarations and relationships.
    /// </summary>
    /// <remarks>
    /// The builder scopes symbol keys by project context and source language so same-named symbols in different projects or languages remain distinct without using database IDs or absolute developer paths.
    /// </remarks>
    public static class SemanticStableKeyBuilder
    {
        /// <summary>
        /// Builds a declaration stable key for a symbol declaration fact.
        /// </summary>
        /// <param name="declarationKind">The declaration category represented by the key.</param>
        /// <param name="sourceLanguage">The source language that produced the declaration.</param>
        /// <param name="projectContext">The logical project context supplied by the extraction caller.</param>
        /// <param name="symbolIdentity">The symbol identity captured from Roslyn.</param>
        /// <returns>A deterministic stable key for the declaration.</returns>
        public static string ForDeclaration(
            SemanticDeclarationKind declarationKind,
            SourceLanguage sourceLanguage,
            string projectContext,
            SemanticSymbolIdentity symbolIdentity)
        {
            // Declaration keys use readable prefixes plus hashed payloads to keep identities stable even when signatures contain punctuation.
            ArgumentNullException.ThrowIfNull(symbolIdentity);
            string payload = JoinPayload(sourceLanguage.ToString(), projectContext, symbolIdentity.FullyQualifiedName, symbolIdentity.MetadataName);
            return $"semantic-{ToKeyPrefix(declarationKind)}://{HashPayload(payload)}";
        }

        /// <summary>
        /// Builds a relationship stable key for a semantic relationship fact.
        /// </summary>
        /// <param name="relationshipKind">The semantic relationship category represented by the key.</param>
        /// <param name="sourceStableKey">The stable key of the source declaration.</param>
        /// <param name="targetStableKey">The stable key of the target declaration.</param>
        /// <returns>A deterministic stable key for the relationship.</returns>
        public static string ForRelationship(
            SemanticRelationshipKind relationshipKind,
            string sourceStableKey,
            string targetStableKey)
        {
            // This overload preserves declaration-containment key behavior when no relationship source qualifier is required.
            return ForRelationship(relationshipKind, sourceStableKey, targetStableKey, relationshipSource: null);
        }

        /// <summary>
        /// Builds a relationship stable key for a semantic relationship fact and deterministic discovery qualifier.
        /// </summary>
        /// <param name="relationshipKind">The semantic relationship category represented by the key.</param>
        /// <param name="sourceStableKey">The stable key of the source declaration or source symbol surrogate.</param>
        /// <param name="targetStableKey">The stable key of the target declaration or target symbol surrogate.</param>
        /// <param name="relationshipSource">The deterministic relationship-source qualifier used to keep distinct dependency meanings separate.</param>
        /// <returns>A deterministic stable key for the relationship.</returns>
        public static string ForRelationship(
            SemanticRelationshipKind relationshipKind,
            string sourceStableKey,
            string targetStableKey,
            string? relationshipSource)
        {
            // Relationship keys are endpoint-derived and can include a source qualifier so duplicate discoveries collapse without merging distinct dependency categories.
            string payload = JoinPayload(relationshipKind.ToString(), sourceStableKey, targetStableKey, string.IsNullOrWhiteSpace(relationshipSource) ? "default" : relationshipSource);
            return $"semantic-relationship://{relationshipKind.ToString().ToLowerInvariant()}/{HashPayload(payload)}";
        }

        /// <summary>
        /// Builds a symbol reference stable key for a relationship endpoint that may not have a source declaration fact in the analyzed repository.
        /// </summary>
        /// <param name="sourceLanguage">The source language that produced the relationship.</param>
        /// <param name="projectContext">The logical project context supplied by the extraction caller.</param>
        /// <param name="symbolIdentity">The symbol identity captured from Roslyn.</param>
        /// <returns>A deterministic stable key for the referenced symbol endpoint.</returns>
        public static string ForSymbolReference(
            SourceLanguage sourceLanguage,
            string projectContext,
            SemanticSymbolIdentity symbolIdentity)
        {
            // Symbol references let relationship targets remain deterministic even before every target is represented as a declaration node.
            ArgumentNullException.ThrowIfNull(symbolIdentity);
            string payload = JoinPayload(sourceLanguage.ToString(), projectContext, symbolIdentity.FullyQualifiedName, symbolIdentity.MetadataName);
            return $"semantic-symbol-reference://{HashPayload(payload)}";
        }

        /// <summary>
        /// Builds a stable evidence key for a declaration source span.
        /// </summary>
        /// <param name="repositoryRelativeFilePath">The repository-relative source file path.</param>
        /// <param name="startLine">The one-based evidence start line.</param>
        /// <param name="endLine">The one-based evidence end line.</param>
        /// <param name="symbolIdentity">The symbol identity associated with the evidence.</param>
        /// <returns>A deterministic stable key for the evidence span.</returns>
        public static string ForEvidence(
            string repositoryRelativeFilePath,
            int startLine,
            int endLine,
            SemanticSymbolIdentity symbolIdentity)
        {
            // Evidence keys include source span and symbol identity so two declarations on the same line still remain distinct.
            ArgumentNullException.ThrowIfNull(symbolIdentity);
            string payload = JoinPayload(repositoryRelativeFilePath, startLine.ToString(), endLine.ToString(), symbolIdentity.FullyQualifiedName, symbolIdentity.MetadataName);
            return $"semantic-evidence://{HashPayload(payload)}";
        }

        /// <summary>
        /// Builds a stable key for a compiler diagnostic fact.
        /// </summary>
        /// <param name="diagnosticId">The compiler diagnostic identifier such as CS0246 or BC30002.</param>
        /// <param name="evidence">The evidence span associated with the diagnostic.</param>
        /// <returns>A deterministic stable key for the diagnostic fact.</returns>
        public static string ForDiagnostic(string diagnosticId, SemanticEvidence evidence)
        {
            // Diagnostic keys include source location and compiler ID so repeated degraded compilations produce stable diagnostic identities.
            ArgumentNullException.ThrowIfNull(evidence);
            string payload = JoinPayload(diagnosticId, evidence.RepositoryRelativeFilePath, evidence.StartLine.ToString(), evidence.StartColumn.ToString(), evidence.EndLine.ToString(), evidence.EndColumn.ToString());
            return $"semantic-diagnostic://{HashPayload(payload)}";
        }

        /// <summary>
        /// Builds a stable key for an unknown semantic fact.
        /// </summary>
        /// <param name="sourceLanguage">The source language that produced the unknown.</param>
        /// <param name="projectContext">The logical project context supplied by the extraction caller.</param>
        /// <param name="reason">The reason the semantic fact could not be fully resolved.</param>
        /// <param name="evidence">The evidence span associated with the unknown.</param>
        /// <param name="description">The deterministic description of the unknown condition.</param>
        /// <returns>A deterministic stable key for the unknown fact.</returns>
        public static string ForUnknown(SourceLanguage sourceLanguage, string projectContext, SemanticUnknownReason reason, SemanticEvidence evidence, string description)
        {
            // Unknown keys are scoped by language, project, reason, evidence, and description so distinct semantic gaps remain queryable.
            ArgumentNullException.ThrowIfNull(evidence);
            string payload = JoinPayload(sourceLanguage.ToString(), projectContext, reason.ToString(), evidence.RepositoryRelativeFilePath, evidence.StartLine.ToString(), evidence.StartColumn.ToString(), description);
            return $"semantic-unknown://{HashPayload(payload)}";
        }

        /// <summary>
        /// Joins stable-key payload segments with explicit length prefixes before hashing.
        /// </summary>
        /// <param name="segments">The payload segments to join.</param>
        /// <returns>A canonical payload string that cannot be confused by embedded delimiters.</returns>
        private static string JoinPayload(params string?[] segments)
        {
            // Length-prefixing avoids ambiguity when source symbols contain delimiter characters.
            StringBuilder builder = new();
            foreach (string? segment in segments)
            {
                string value = RequireText(segment, nameof(segments));
                builder.Append(value.Length);
                builder.Append(':');
                builder.Append(value);
                builder.Append('|');
            }

            return builder.ToString();
        }

        /// <summary>
        /// Hashes canonical payload text as lowercase SHA-256 hex.
        /// </summary>
        /// <param name="payload">The canonical payload text to hash.</param>
        /// <returns>The lowercase SHA-256 hex hash.</returns>
        private static string HashPayload(string payload)
        {
            // Hashing keeps stable keys compact while preserving deterministic equality for identical semantic input.
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Converts declaration kinds into stable readable key prefixes.
        /// </summary>
        /// <param name="declarationKind">The declaration kind to convert.</param>
        /// <returns>The lowercase stable key prefix segment.</returns>
        private static string ToKeyPrefix(SemanticDeclarationKind declarationKind)
        {
            // Explicit mapping protects key prefixes from future enum renames or formatting changes.
            return declarationKind switch
            {
                SemanticDeclarationKind.Namespace => "namespace",
                SemanticDeclarationKind.Type => "type",
                SemanticDeclarationKind.Method => "method",
                SemanticDeclarationKind.Property => "property",
                SemanticDeclarationKind.Field => "field",
                _ => throw new ArgumentOutOfRangeException(nameof(declarationKind), declarationKind, "Unsupported semantic declaration kind.")
            };
        }

        /// <summary>
        /// Requires non-empty stable-key payload text.
        /// </summary>
        /// <param name="value">The payload text supplied by extraction logic.</param>
        /// <param name="parameterName">The source parameter name used in validation failures.</param>
        /// <returns>The trimmed payload text.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Stable keys cannot include missing segments because that would collapse different semantic facts together.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Stable-key payload segments cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }
    }
}
