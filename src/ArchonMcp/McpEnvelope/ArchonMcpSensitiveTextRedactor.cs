using System.Text.RegularExpressions;

namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Redacts representative secret-like values from untrusted MCP evidence snippets and metadata text.
    /// </summary>
    public sealed class ArchonMcpSensitiveTextRedactor : IArchonMcpSensitiveTextRedactor
    {
        /// <summary>
        /// Detects common secret key-value assignments in untrusted evidence text.
        /// </summary>
        private static readonly Regex SecretAssignmentPattern = new(
            "(?<key>(password|pwd|secret|token|api[_-]?key|accountkey|connectionstring|certificate|private[_-]?key))\\s*[:=]\\s*[^;\\r\\n\\s]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Detects connection-string password fragments in semicolon-delimited text.
        /// </summary>
        private static readonly Regex ConnectionStringPasswordPattern = new(
            "(?<key>(Password|Pwd))\\s*=\\s*[^;]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <inheritdoc />
        public string? Redact(string? text)
        {
            // Null stays null so mappers can distinguish omitted content from intentionally empty content.
            if (text is null)
            {
                return null;
            }

            // The first pass redacts key-value style secrets such as passwords, tokens, API keys, and account keys.
            string redacted = SecretAssignmentPattern.Replace(text, match => $"{match.Groups["key"].Value}=[redacted]");

            // The second pass redacts connection-string password fragments that use semicolon-separated syntax.
            redacted = ConnectionStringPasswordPattern.Replace(redacted, match => $"{match.Groups["key"].Value}=[redacted]");

            return redacted;
        }
    }
}
