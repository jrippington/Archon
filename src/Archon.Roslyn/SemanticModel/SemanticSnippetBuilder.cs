using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Builds deterministic snippet previews and hashes for semantic source evidence.
    /// </summary>
    /// <remarks>
    /// Snippets are intentionally small and deterministic. They help contributors recognize the declaration that produced a fact without storing entire source files in graph evidence.
    /// </remarks>
    public static class SemanticSnippetBuilder
    {
        /// <summary>
        /// Defines the maximum number of characters retained in a snippet preview.
        /// </summary>
        public const int DefaultPreviewLimit = 160;

        /// <summary>
        /// Creates a deterministic source snippet for the supplied syntax node.
        /// </summary>
        /// <param name="syntaxTree">The syntax tree that owns the node span.</param>
        /// <param name="span">The source span to preview and hash.</param>
        /// <param name="cancellationToken">A token that signals when source text retrieval should stop.</param>
        /// <returns>A tuple containing the preview and hash, or null values when source text is unavailable.</returns>
        public static (string? Preview, string? Hash) CreateSnippet(SyntaxTree syntaxTree, TextSpan span, CancellationToken cancellationToken = default)
        {
            // Roslyn can provide source text for normal documents and in-memory fixtures; unavailable text is represented safely as null details.
            ArgumentNullException.ThrowIfNull(syntaxTree);
            SourceText sourceText = syntaxTree.GetText(cancellationToken);
            TextSpan safeSpan = ClampSpan(span, sourceText.Length);
            if (safeSpan.Length == 0)
            {
                return (null, null);
            }

            string snippet = sourceText.ToString(safeSpan);
            return (CreatePreview(snippet, DefaultPreviewLimit), CreateHash(snippet));
        }

        /// <summary>
        /// Creates a normalized snippet preview constrained to the supplied character limit.
        /// </summary>
        /// <param name="snippet">The source snippet to normalize.</param>
        /// <param name="previewLimit">The maximum number of preview characters to retain.</param>
        /// <returns>The normalized preview, or <see langword="null" /> when the snippet is blank.</returns>
        public static string? CreatePreview(string? snippet, int previewLimit = DefaultPreviewLimit)
        {
            // Whitespace normalization keeps previews stable across indentation changes that do not alter the declaration text itself.
            if (previewLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(previewLimit), previewLimit, "Preview limit must be positive.");
            }

            string normalized = NormalizeWhitespace(snippet);
            if (normalized.Length == 0)
            {
                return null;
            }

            return normalized.Length <= previewLimit ? normalized : normalized[..previewLimit].TrimEnd();
        }

        /// <summary>
        /// Creates a deterministic SHA-256 hash for the supplied source snippet.
        /// </summary>
        /// <param name="snippet">The source snippet to hash.</param>
        /// <returns>The prefixed lowercase SHA-256 hash, or <see langword="null" /> when the snippet is blank.</returns>
        public static string? CreateHash(string? snippet)
        {
            // The hash uses the exact snippet text rather than the preview so evidence comparison can detect source changes inside the span.
            if (string.IsNullOrWhiteSpace(snippet))
            {
                return null;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(snippet);
            byte[] hash = SHA256.HashData(bytes);
            return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
        }

        /// <summary>
        /// Clamps a requested source span to the available source text length.
        /// </summary>
        /// <param name="span">The requested source span.</param>
        /// <param name="sourceLength">The available source text length.</param>
        /// <returns>A safe source span inside the available text.</returns>
        private static TextSpan ClampSpan(TextSpan span, int sourceLength)
        {
            // Defensive clamping protects evidence creation from malformed or synthetic spans.
            int start = Math.Clamp(span.Start, 0, sourceLength);
            int end = Math.Clamp(span.End, start, sourceLength);
            return TextSpan.FromBounds(start, end);
        }

        /// <summary>
        /// Collapses snippet whitespace into single spaces for compact previews.
        /// </summary>
        /// <param name="snippet">The source snippet to normalize.</param>
        /// <returns>The whitespace-normalized snippet preview text.</returns>
        private static string NormalizeWhitespace(string? snippet)
        {
            // A small loop avoids culture-sensitive regular expression behavior and keeps preview formatting deterministic.
            if (string.IsNullOrWhiteSpace(snippet))
            {
                return string.Empty;
            }

            StringBuilder builder = new(snippet.Length);
            bool previousWasWhitespace = false;
            foreach (char character in snippet.Trim())
            {
                if (char.IsWhiteSpace(character))
                {
                    if (!previousWasWhitespace)
                    {
                        builder.Append(' ');
                    }

                    previousWasWhitespace = true;
                }
                else
                {
                    builder.Append(character);
                    previousWasWhitespace = false;
                }
            }

            return builder.ToString();
        }
    }
}
