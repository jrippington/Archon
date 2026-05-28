using System.Security.Cryptography;
using System.Text;
using Archon.Domain.Graph.Identity;

namespace Archon.Extractors.Ui
{
    /// <summary>
    /// Builds deterministic stable keys for UI artifacts that are not yet represented by specialized domain key helpers.
    /// </summary>
    public static class UiStableKeyBuilder
    {
        /// <summary>
        /// Creates a stable key for a UI graph fact from normalized logical identity parts.
        /// </summary>
        /// <param name="prefix">The stable-key prefix that identifies the UI fact category.</param>
        /// <param name="parts">The normalized identity parts that distinguish the fact within the prefix.</param>
        /// <returns>A deterministic stable key with a readable prefix and hashed payload.</returns>
        public static StableKey Create(string prefix, params string?[] parts)
        {
            // UI keys use a hashed payload so routes, paths, component names, and source locations can combine without creating invalid URI characters.
            if (string.IsNullOrWhiteSpace(prefix))
            {
                throw new ArgumentException("UI stable-key prefixes cannot be null, empty, or whitespace.", nameof(prefix));
            }

            string normalizedPayload = string.Join("\u001F", parts.Select(NormalizePart));
            byte[] payloadBytes = Encoding.UTF8.GetBytes(normalizedPayload);
            string hash = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();
            return new StableKey(string.Concat(prefix.Trim(), hash));
        }

        /// <summary>
        /// Hashes source text or metadata payloads for evidence snippets and compact metadata identities.
        /// </summary>
        /// <param name="parts">The text parts that should contribute to the hash.</param>
        /// <returns>A lowercase SHA-256 hash string without a URI prefix.</returns>
        public static string Hash(params string?[] parts)
        {
            // Evidence snippet hashes intentionally omit a prefix because the EvidenceRecord.SnippetHash field stores only the digest value.
            string normalizedPayload = string.Join("\u001F", parts.Select(NormalizePart));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPayload))).ToLowerInvariant();
        }

        /// <summary>
        /// Normalizes one stable-key or hash component into a deterministic non-null text segment.
        /// </summary>
        /// <param name="part">The candidate component supplied by an extractor.</param>
        /// <returns>A trimmed text segment, or an explicit empty marker when the component is absent.</returns>
        private static string NormalizePart(string? part)
        {
            // An explicit marker prevents missing values from collapsing adjacent separators and changing identity shape ambiguously.
            return string.IsNullOrWhiteSpace(part) ? "<empty>" : part.Trim().Replace('\\', '/');
        }
    }
}