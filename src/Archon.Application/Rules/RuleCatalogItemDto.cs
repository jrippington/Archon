namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents a stable public rule catalog list item for API and future MCP consumers.
    /// </summary>
    public sealed class RuleCatalogItemDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleCatalogItemDto"/> class.
        /// </summary>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="version">The exact rule version.</param>
        /// <param name="name">The human-readable rule name.</param>
        /// <param name="category">The rule category string.</param>
        /// <param name="severity">The default finding severity string.</param>
        /// <param name="defaultStatus">The default finding status string authored by the rule.</param>
        /// <param name="enabled">Indicates whether the rule is enabled for evaluation.</param>
        /// <param name="builtIn">Indicates whether the rule ships as built-in Archon content.</param>
        /// <param name="ownerScope">The optional owner scope for organization-specific rules.</param>
        /// <param name="summary">The short description suitable for catalog lists.</param>
        /// <param name="tags">The stable tag values returned for controlled filtering and display.</param>
        public RuleCatalogItemDto(
            string ruleCode,
            string version,
            string name,
            string category,
            string severity,
            string defaultStatus,
            bool enabled,
            bool builtIn,
            string? ownerScope,
            string summary,
            IEnumerable<string> tags)
        {
            // DTO construction keeps public query shape independent from mutable domain/catalog implementation details.
            RuleCode = RequireText(ruleCode, nameof(ruleCode));
            Version = RequireText(version, nameof(version));
            Name = RequireText(name, nameof(name));
            Category = RequireText(category, nameof(category));
            Severity = RequireText(severity, nameof(severity));
            DefaultStatus = RequireText(defaultStatus, nameof(defaultStatus));
            Enabled = enabled;
            BuiltIn = builtIn;
            OwnerScope = string.IsNullOrWhiteSpace(ownerScope) ? null : ownerScope.Trim();
            Summary = RequireText(summary, nameof(summary));
            Tags = tags.Where(static tag => !string.IsNullOrWhiteSpace(tag)).Select(static tag => tag.Trim()).OrderBy(static tag => tag, StringComparer.Ordinal).ToArray();
        }

        /// <summary>Gets the stable rule code.</summary>
        public string RuleCode { get; }

        /// <summary>Gets the exact rule version.</summary>
        public string Version { get; }

        /// <summary>Gets the human-readable rule name.</summary>
        public string Name { get; }

        /// <summary>Gets the rule category string.</summary>
        public string Category { get; }

        /// <summary>Gets the default finding severity string.</summary>
        public string Severity { get; }

        /// <summary>Gets the default finding status string authored by the rule.</summary>
        public string DefaultStatus { get; }

        /// <summary>Gets a value indicating whether the rule is enabled for evaluation.</summary>
        public bool Enabled { get; }

        /// <summary>Gets a value indicating whether the rule ships as built-in Archon content.</summary>
        public bool BuiltIn { get; }

        /// <summary>Gets the optional owner scope for organization-specific rules.</summary>
        public string? OwnerScope { get; }

        /// <summary>Gets the short description suitable for catalog lists.</summary>
        public string Summary { get; }

        /// <summary>Gets the stable tag values returned for controlled filtering and display.</summary>
        public IReadOnlyList<string> Tags { get; }

        /// <summary>
        /// Requires a non-empty text value for a public query DTO field.
        /// </summary>
        /// <param name="value">The candidate field value.</param>
        /// <param name="parameterName">The parameter name used for invalid input reporting.</param>
        /// <returns>The trimmed field value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Public query records require explicit identities and display strings so consumers do not need fallback heuristics.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }
}
