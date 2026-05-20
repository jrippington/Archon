using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Metadata;

namespace Archon.Domain.Graph.Model
{
    /// <summary>
    /// Represents a versioned architecture rule catalog entry without coupling rule evaluation or storage to WP002.
    /// </summary>
    public sealed class RuleDefinition
    {
        /// <summary>
        /// Initializes a validated rule definition model.
        /// </summary>
        /// <param name="ruleCode">The stable rule code that identifies the catalog entry.</param>
        /// <param name="name">The developer-facing rule name.</param>
        /// <param name="category">The controlled rule category.</param>
        /// <param name="severity">The default severity assigned to findings from the rule.</param>
        /// <param name="defaultStatus">The default status assigned to findings from the rule.</param>
        /// <param name="enabled">A value indicating whether the rule is enabled by default.</param>
        /// <param name="version">The rule version that preserves historical finding explainability.</param>
        /// <param name="description">The developer-facing rule description.</param>
        /// <param name="definitionJson">The serialized rule definition payload.</param>
        /// <param name="sourceUrls">The source URLs that explain or justify the rule.</param>
        /// <param name="isBuiltIn">A value indicating whether the rule is built into Archon.</param>
        /// <param name="ownerScope">The optional owner scope for organization-specific rule ownership.</param>
        /// <param name="metadata">Deterministic metadata for rule details that are not normalized fields.</param>
        public RuleDefinition(
            string? ruleCode,
            string? name,
            RuleCategory category,
            FindingSeverity severity,
            FindingStatus defaultStatus,
            bool enabled,
            string? version,
            string? description,
            string? definitionJson,
            IEnumerable<string>? sourceUrls,
            bool isBuiltIn,
            string? ownerScope,
            GraphMetadata metadata)
        {
            // Rule definitions retain catalog identity and default finding behavior while deferring rule loading and execution.
            ArgumentNullException.ThrowIfNull(category);
            ArgumentNullException.ThrowIfNull(severity);
            ArgumentNullException.ThrowIfNull(defaultStatus);
            ArgumentNullException.ThrowIfNull(metadata);

            RuleCode = GraphFactValidation.RequiredString(ruleCode, nameof(ruleCode));
            Name = GraphFactValidation.RequiredString(name, nameof(name));
            Category = category;
            Severity = severity;
            DefaultStatus = defaultStatus;
            Enabled = enabled;
            Version = GraphFactValidation.RequiredString(version, nameof(version));
            Description = GraphFactValidation.RequiredString(description, nameof(description));
            DefinitionJson = GraphFactValidation.RequiredString(definitionJson, nameof(definitionJson));
            SourceUrls = NormalizeSourceUrls(sourceUrls);
            IsBuiltIn = isBuiltIn;
            OwnerScope = GraphFactValidation.OptionalString(ownerScope);
            Metadata = metadata;
        }

        /// <summary>
        /// Gets the stable rule code that identifies the catalog entry.
        /// </summary>
        public string RuleCode { get; }

        /// <summary>
        /// Gets the developer-facing rule name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the controlled rule category.
        /// </summary>
        public RuleCategory Category { get; }

        /// <summary>
        /// Gets the default severity assigned to findings from the rule.
        /// </summary>
        public FindingSeverity Severity { get; }

        /// <summary>
        /// Gets the default status assigned to findings from the rule.
        /// </summary>
        public FindingStatus DefaultStatus { get; }

        /// <summary>
        /// Gets a value indicating whether the rule is enabled by default.
        /// </summary>
        public bool Enabled { get; }

        /// <summary>
        /// Gets the rule version that preserves historical finding explainability.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Gets the developer-facing rule description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the serialized rule definition payload.
        /// </summary>
        public string DefinitionJson { get; }

        /// <summary>
        /// Gets the source URLs that explain or justify the rule.
        /// </summary>
        public IReadOnlyList<string> SourceUrls { get; }

        /// <summary>
        /// Gets a value indicating whether the rule is built into Archon.
        /// </summary>
        public bool IsBuiltIn { get; }

        /// <summary>
        /// Gets the optional owner scope for organization-specific rule ownership.
        /// </summary>
        public string? OwnerScope { get; }

        /// <summary>
        /// Gets deterministic metadata for rule details that are not normalized fields.
        /// </summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Normalizes optional rule source URLs into an immutable read-only list.
        /// </summary>
        /// <param name="sourceUrls">The source URLs to normalize.</param>
        /// <returns>A read-only list of trimmed non-empty source URLs.</returns>
        private static IReadOnlyList<string> NormalizeSourceUrls(IEnumerable<string>? sourceUrls)
        {
            // Source URLs are explanatory references, so blank entries are ignored rather than stored as noisy data.
            return sourceUrls is null
                ? []
                : sourceUrls.Where(sourceUrl => !string.IsNullOrWhiteSpace(sourceUrl)).Select(sourceUrl => sourceUrl.Trim()).ToArray();
        }
    }
}
