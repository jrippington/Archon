using System.Security.Cryptography;
using System.Text;

namespace Archon.Extractors.Projects.Evidence
{
    /// <summary>
    /// Represents a concise evidence snippet extracted from a source artifact without retaining complete file contents.
    /// </summary>
    /// <param name="Hash">The deterministic SHA-256 hash of the normalized snippet text.</param>
    /// <param name="Preview">The short human-readable snippet preview stored on evidence records.</param>
    internal sealed record SourceSnippet(string Hash, string Preview)
    {
        /// <summary>
        /// Creates a snippet hash and preview from source text that supports one graph fact.
        /// </summary>
        /// <param name="text">The source text to summarize.</param>
        /// <returns>A deterministic snippet containing a hash and bounded preview.</returns>
        internal static SourceSnippet FromText(string? text)
        {
            // Snippets intentionally summarize only the fact-bearing XML line or source fragment so evidence does not store full file content.
            string normalized = string.IsNullOrWhiteSpace(text) ? string.Empty : NormalizeWhitespace(text);
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            string hash = string.Concat("sha256:", Convert.ToHexString(bytes).ToLowerInvariant());
            string preview = normalized.Length <= 160 ? normalized : string.Concat(normalized.AsSpan(0, 157), "...");
            return new SourceSnippet(hash, preview);
        }

        /// <summary>
        /// Normalizes whitespace in a snippet while preserving readable XML token order.
        /// </summary>
        /// <param name="text">The raw source text to normalize.</param>
        /// <returns>A single-line snippet preview input.</returns>
        private static string NormalizeWhitespace(string text)
        {
            // Collapsing whitespace keeps equivalent indentation from changing evidence hashes and previews.
            StringBuilder builder = new(text.Length);
            bool previousWasWhitespace = false;

            foreach (char character in text.Trim())
            {
                if (char.IsWhiteSpace(character))
                {
                    if (!previousWasWhitespace)
                    {
                        builder.Append(' ');
                        previousWasWhitespace = true;
                    }

                    continue;
                }

                builder.Append(character);
                previousWasWhitespace = false;
            }

            return builder.ToString();
        }
    }
}
