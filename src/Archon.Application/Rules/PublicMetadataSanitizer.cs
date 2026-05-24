using System.Text.Json;
using Archon.Domain.Graph.Metadata;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Sanitizes graph metadata before it is exposed through public query DTOs.
    /// </summary>
    public static class PublicMetadataSanitizer
    {
        /// <summary>
        /// Produces credential-safe metadata for public API and future MCP responses.
        /// </summary>
        /// <param name="metadata">The source metadata value to sanitize.</param>
        /// <returns>Metadata containing only safe lower camel case property names.</returns>
        public static GraphMetadata Sanitize(GraphMetadata metadata)
        {
            // Public query surfaces expose metadata as supplemental diagnostics, never as a secret transport channel.
            using JsonDocument document = JsonDocument.Parse(metadata.ToCanonicalJson());
            Dictionary<string, object?> values = new(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (IsSafeMetadataName(property.Name))
                {
                    values[property.Name] = property.Value.Clone();
                }
            }

            return values.Count == 0 ? GraphMetadata.Empty : GraphMetadata.From(values);
        }

        /// <summary>
        /// Determines whether a metadata property name is safe to expose through public query responses.
        /// </summary>
        /// <param name="name">The metadata property name to inspect.</param>
        /// <returns><see langword="true" /> when the name is lower camel case and not secret-like; otherwise, <see langword="false" />.</returns>
        private static bool IsSafeMetadataName(string name)
        {
            // The deny-list intentionally catches common credential labels while the lower-camel rule excludes internal diagnostic names.
            return !string.IsNullOrWhiteSpace(name)
                && char.IsLower(name[0])
                && !name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("password", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("token", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("connectionString", StringComparison.OrdinalIgnoreCase);
        }
    }
}