using Archon.Extractors.Integrations.HttpRest;

namespace Archon.Extractors.Integrations.Messaging
{
    /// <summary>
    /// Redacts secret-like messaging evidence before it can be stored in metadata, diagnostics, or evidence previews.
    /// </summary>
    internal static class MessagingIntegrationRedactor
    {
        /// <summary>
        /// Redacts a source-visible messaging value while preserving enough shape for deterministic diagnostics.
        /// </summary>
        /// <param name="value">The source-visible value to redact.</param>
        /// <returns>The redacted value, or <see langword="null" /> when the input is <see langword="null" />.</returns>
        public static string? Redact(string? value)
        {
            // Messaging connection strings and broker URIs use many of the same secret patterns as HTTP clients, so reuse the shared redaction guard first.
            string? redacted = HttpRestRedactor.Redact(value);
            if (redacted is null)
            {
                return null;
            }

            return redacted.Contains("SharedAccessKey", StringComparison.OrdinalIgnoreCase)
                || redacted.Contains("Endpoint=sb://", StringComparison.OrdinalIgnoreCase)
                || redacted.Contains("password=", StringComparison.OrdinalIgnoreCase)
                ? "<redacted messaging secret>"
                : redacted;
        }
    }
}
