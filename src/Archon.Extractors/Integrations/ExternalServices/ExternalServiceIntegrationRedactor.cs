using Archon.Extractors.Integrations.HttpRest;

namespace Archon.Extractors.Integrations.ExternalServices
{
    /// <summary>
    /// Redacts secret-like storage, SMTP/email, and payment evidence before it can be stored in graph output or diagnostics.
    /// </summary>
    internal static class ExternalServiceIntegrationRedactor
    {
        /// <summary>
        /// Redacts a source-visible or configuration-visible value while preserving non-secret shape for deterministic explanation.
        /// </summary>
        /// <param name="value">The candidate value to sanitize before graph projection.</param>
        /// <returns>The sanitized value, or <see langword="null" /> when the input is <see langword="null" />.</returns>
        public static string? Redact(string? value)
        {
            // HTTP/REST redaction already covers common bearer, API key, and URL credential patterns; this slice adds storage, SMTP, and payment-specific guards.
            string? redacted = HttpRestRedactor.Redact(value);
            if (redacted is null)
            {
                return null;
            }

            if (ContainsStorageSecret(redacted))
            {
                return "<redacted storage secret>";
            }

            if (ContainsEmailSecret(redacted))
            {
                return "<redacted email secret>";
            }

            if (ContainsPaymentSecret(redacted))
            {
                return "<redacted payment secret>";
            }

            return redacted;
        }

        /// <summary>
        /// Determines whether a value resembles a storage connection string, account key, or shared-access signature.
        /// </summary>
        /// <param name="value">The sanitized value to inspect.</param>
        /// <returns><see langword="true" /> when the value should be replaced with the storage redaction marker; otherwise, <see langword="false" />.</returns>
        private static bool ContainsStorageSecret(string value)
        {
            // Storage connection strings can expose long-lived account keys and SAS tokens, so redact the entire value when these markers appear.
            return value.Contains("DefaultEndpointsProtocol", StringComparison.OrdinalIgnoreCase)
                || value.Contains("AccountKey", StringComparison.OrdinalIgnoreCase)
                || value.Contains("SharedAccessSignature", StringComparison.OrdinalIgnoreCase)
                || value.Contains("sig=", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a value resembles SMTP credentials or email message payload details.
        /// </summary>
        /// <param name="value">The sanitized value to inspect.</param>
        /// <returns><see langword="true" /> when the value should be replaced with the email redaction marker; otherwise, <see langword="false" />.</returns>
        private static bool ContainsEmailSecret(string value)
        {
            // SMTP passwords and recipient payloads are not required for topology analysis and must not be persisted.
            return value.Contains("smtp-secret", StringComparison.OrdinalIgnoreCase)
                || value.Contains("password", StringComparison.OrdinalIgnoreCase)
                || value.Contains("NetworkCredential", StringComparison.OrdinalIgnoreCase)
                || value.Contains("customer@example", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a value resembles payment credentials, tokens, card data, or customer payment identifiers.
        /// </summary>
        /// <param name="value">The sanitized value to inspect.</param>
        /// <returns><see langword="true" /> when the value should be replaced with the payment redaction marker; otherwise, <see langword="false" />.</returns>
        private static bool ContainsPaymentSecret(string value)
        {
            // Payment fixtures and real code often carry API keys, tokens, card numbers, and customer IDs close to call sites, so any marker triggers whole-value redaction.
            return value.Contains("sk_test_", StringComparison.OrdinalIgnoreCase)
                || value.Contains("sk_live_", StringComparison.OrdinalIgnoreCase)
                || value.Contains("tok_", StringComparison.OrdinalIgnoreCase)
                || value.Contains("card", StringComparison.OrdinalIgnoreCase)
                || value.Contains("4242424242424242", StringComparison.OrdinalIgnoreCase)
                || value.Contains("cus_", StringComparison.OrdinalIgnoreCase)
                || value.Contains("payment", StringComparison.OrdinalIgnoreCase) && value.Contains("secret", StringComparison.OrdinalIgnoreCase);
        }
    }
}
