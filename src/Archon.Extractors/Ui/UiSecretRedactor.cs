using System.Text.RegularExpressions;

namespace Archon.Extractors.Ui
{
    /// <summary>
    /// Redacts secret-like UI markup and code snippets before they are stored as evidence previews.
    /// </summary>
    public static partial class UiSecretRedactor
    {
        /// <summary>
        /// Replaces values assigned to secret-like names with a fixed token.
        /// </summary>
        /// <param name="text">The source text that may contain secret-like assignments.</param>
        /// <returns>The text with secret-like assignment values replaced by <c>[REDACTED]</c>.</returns>
        public static string Redact(string? text)
        {
            // A deterministic replacement token keeps evidence previews useful while preventing obvious secret literals from being persisted.
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string redacted = SecretAssignmentRegex().Replace(text, match => string.Concat(match.Groups["prefix"].Value, "[REDACTED]", match.Groups["suffix"].Value));
            return ConnectionStringSecretRegex().Replace(redacted, match => string.Concat(match.Groups["prefix"].Value, "[REDACTED]", match.Groups["suffix"].Value));
        }

        /// <summary>
        /// Creates the compiled regular expression that recognizes common secret-bearing assignment names.
        /// </summary>
        /// <returns>A regex that captures the assignment prefix, sensitive value, and original suffix quote.</returns>
        [GeneratedRegex("(?<prefix>(?:password|passwd|pwd|secret|token|apikey|api_key|connectionstring)\\s*=\\s*[\\\"'])(?<value>[^\\\"']*)(?<suffix>[\\\"'])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex SecretAssignmentRegex();

        /// <summary>
        /// Creates the compiled regular expression that recognizes password-like key/value segments inside connection strings.
        /// </summary>
        /// <returns>A regex that captures the connection-string segment prefix, sensitive value, and segment suffix.</returns>
        [GeneratedRegex("(?<prefix>(?:Password|Pwd)\\s*=\\s*)(?<value>[^;\\\"']+)(?<suffix>;?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ConnectionStringSecretRegex();
    }
}