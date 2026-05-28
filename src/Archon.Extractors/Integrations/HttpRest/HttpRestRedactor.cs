using System.Text.RegularExpressions;

namespace Archon.Extractors.Integrations.HttpRest
{
    /// <summary>
    /// Redacts secret-bearing HTTP and REST source fragments before they can enter metadata, diagnostics, or evidence previews.
    /// </summary>
    public static partial class HttpRestRedactor
    {
        /// <summary>
        /// Redacts secret-like values while preserving safe structural source evidence.
        /// </summary>
        /// <param name="value">The source, diagnostic, metadata, or target value to redact.</param>
        /// <returns>The redacted value, or <see langword="null" /> when the input is absent.</returns>
        public static string? Redact(string? value)
        {
            // Redaction uses broad token-aware patterns because source snippets may contain headers, object initializers, or connection-like endpoint values.
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            string redacted = BearerRegex().Replace(value, "Bearer <redacted>");
            redacted = ApiKeyRegex().Replace(redacted, "$1$2<redacted>");
            redacted = PasswordRegex().Replace(redacted, "$1<redacted>");
            redacted = SecretAssignmentRegex().Replace(redacted, "$1$2<redacted>");
            return redacted;
        }

        /// <summary>
        /// Redacts a potential service target name without removing normal HTTP base-address identities.
        /// </summary>
        /// <param name="targetName">The candidate service target name.</param>
        /// <returns>The redacted target name, or <see langword="null" /> when no target was supplied.</returns>
        public static string? RedactTargetName(string? targetName)
        {
            // Targets may be literal base URLs or logical client names; only obvious secret-bearing tokens are replaced.
            return Redact(targetName);
        }

        /// <summary>
        /// Creates the regular expression that finds bearer-token literals.
        /// </summary>
        /// <returns>A compiled bearer-token regular expression.</returns>
        [GeneratedRegex("Bearer\\s+[-._~+/A-Za-z0-9]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex BearerRegex();

        /// <summary>
        /// Creates the regular expression that finds API-key style header or assignment values.
        /// </summary>
        /// <returns>A compiled API-key regular expression.</returns>
        [GeneratedRegex("(ApiKey|Api-Key|X-Api-Key)([\\\"'\\s,:=]+)([^\\\"'\\s,)]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ApiKeyRegex();

        /// <summary>
        /// Creates the regular expression that finds password values inside connection-like strings.
        /// </summary>
        /// <returns>A compiled password regular expression.</returns>
        [GeneratedRegex("(password\\s*=\\s*)[^;\\\"'\\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex PasswordRegex();

        /// <summary>
        /// Creates the regular expression that finds generic secret and token assignments in source snippets.
        /// </summary>
        /// <returns>A compiled secret-assignment regular expression.</returns>
        [GeneratedRegex("(secret|token|credential)([\\\"'\\s:=]+)([^\\\"'\\s,)]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex SecretAssignmentRegex();
    }
}
