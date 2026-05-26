using ArchonMcp.McpEnvelope;

namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Normalizes MCP request parameters into safe audit metadata.
    /// </summary>
    public sealed class ArchonMcpAuditParameterNormalizer
    {
        /// <summary>
        /// Stores parameter-name fragments that indicate the value should be fully redacted from audit records.
        /// </summary>
        private static readonly string[] SensitiveNameFragments =
        [
            "password",
            "pwd",
            "secret",
            "token",
            "apikey",
            "api_key",
            "api-key",
            "connectionstring",
            "connection_string",
            "connection-string",
            "credential",
            "certificate",
            "privatekey",
            "private_key",
            "private-key"
        ];

        /// <summary>
        /// Stores the text redactor used for non-sensitive parameter names whose values may still contain secret-like text.
        /// </summary>
        private readonly IArchonMcpSensitiveTextRedactor _redactor;

        /// <summary>
        /// Creates a safe audit parameter normalizer.
        /// </summary>
        /// <param name="redactor">The redactor used to sanitize parameter values that are safe to retain after normalization.</param>
        public ArchonMcpAuditParameterNormalizer(IArchonMcpSensitiveTextRedactor redactor)
        {
            // The normalizer composes with the shared redactor so audit and response mapping use the same secret vocabulary.
            _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        }

        /// <summary>
        /// Normalizes raw MCP parameters into sorted safe string values for audit logging.
        /// </summary>
        /// <param name="parameters">The raw request parameters supplied to the MCP operation.</param>
        /// <returns>A sorted dictionary containing only safe parameter values.</returns>
        public IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? parameters)
        {
            // Null parameters normalize to an empty dictionary so audit events have a predictable shape.
            if (parameters is null || parameters.Count == 0)
            {
                return new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            SortedDictionary<string, string> normalized = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> parameter in parameters)
            {
                // Blank parameter names cannot be matched safely, so skip them rather than logging ambiguous metadata.
                if (string.IsNullOrWhiteSpace(parameter.Key))
                {
                    continue;
                }

                string safeKey = parameter.Key.Trim();
                normalized[safeKey] = IsSensitiveName(safeKey)
                    ? "[redacted]"
                    : _redactor.Redact(parameter.Value) ?? string.Empty;
            }

            return normalized;
        }

        /// <summary>
        /// Determines whether a parameter name represents a sensitive value that must be fully redacted.
        /// </summary>
        /// <param name="parameterName">The request parameter name to inspect.</param>
        /// <returns><see langword="true" /> when the parameter value must be redacted; otherwise, <see langword="false" />.</returns>
        private static bool IsSensitiveName(string parameterName)
        {
            // The comparison ignores common separators so equivalent spellings such as api-key and apiKey are treated consistently.
            string compactName = parameterName.Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal);
            return SensitiveNameFragments.Any(fragment =>
            {
                string compactFragment = fragment.Replace("-", string.Empty, StringComparison.Ordinal)
                    .Replace("_", string.Empty, StringComparison.Ordinal);
                return compactName.Contains(compactFragment, StringComparison.OrdinalIgnoreCase);
            });
        }
    }
}
