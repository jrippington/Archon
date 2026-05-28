namespace Archon.Extractors.Integrations.InternalServices
{
    /// <summary>
    /// Redacts secret-bearing text before internal service correlation values reach graph metadata, evidence, diagnostics, logs, or tests.
    /// </summary>
    internal static class InternalServiceIntegrationRedactor
    {
        /// <summary>
        /// Redacts known secret-bearing markers from a candidate value.
        /// </summary>
        /// <param name="value">The candidate value to sanitize.</param>
        /// <returns>The original value when safe, or a redacted placeholder when secret-like text is present.</returns>
        public static string Redact(string value)
        {
            // Internal routing evidence may appear near credentials in client setup, so use conservative substring checks before persistence.
            if (value.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || value.Contains("password", StringComparison.OrdinalIgnoreCase)
                || value.Contains("token", StringComparison.OrdinalIgnoreCase)
                || value.Contains("apikey", StringComparison.OrdinalIgnoreCase)
                || value.Contains("api-key", StringComparison.OrdinalIgnoreCase)
                || value.Contains("authorization", StringComparison.OrdinalIgnoreCase))
            {
                return "<redacted internal service integration value>";
            }

            return value.Trim();
        }
    }
}
