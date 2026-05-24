using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Metadata;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents one validated rule catalog entry loaded from copied runtime JSON content.
    /// </summary>
    public sealed class RuleCatalogEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleCatalogEntry"/> class.
        /// </summary>
        /// <param name="ruleCode">The stable rule code that identifies the catalog entry across files and runs.</param>
        /// <param name="name">The human-readable rule name.</param>
        /// <param name="category">The controlled rule category.</param>
        /// <param name="severity">The default finding severity.</param>
        /// <param name="defaultStatus">The default finding status assigned by the rule.</param>
        /// <param name="enabled">Indicates whether the rule is enabled for evaluation.</param>
        /// <param name="version">The semantic rule version that preserves historical finding explainability.</param>
        /// <param name="description">The human-readable rule description.</param>
        /// <param name="definitionJson">The normalized source JSON used to explain the loaded rule.</param>
        /// <param name="sourceUrls">The optional explanatory source URLs.</param>
        /// <param name="isBuiltIn">Indicates whether the rule is shipped as built-in Archon content.</param>
        /// <param name="ownerScope">The optional owner scope for organization-specific ownership.</param>
        /// <param name="impact">The optional impact statements authored with the rule.</param>
        /// <param name="evidenceRequirements">The optional evidence requirements authored with the rule.</param>
        /// <param name="recommendedActions">The optional recommended actions authored with the rule.</param>
        /// <param name="tags">The optional lower-level tags authored with the rule.</param>
        /// <param name="metadata">The deterministic metadata object for extension fields.</param>
        /// <param name="detection">The validated detection DSL root group.</param>
        /// <param name="sourceFilePath">The runtime file path that contributed the rule.</param>
        public RuleCatalogEntry(
            string ruleCode,
            string name,
            RuleCategory category,
            FindingSeverity severity,
            RuleFindingStatus defaultStatus,
            bool enabled,
            string version,
            string description,
            string definitionJson,
            IEnumerable<string> sourceUrls,
            bool isBuiltIn,
            string? ownerScope,
            IEnumerable<string> impact,
            IEnumerable<string> evidenceRequirements,
            IEnumerable<string> recommendedActions,
            IEnumerable<string> tags,
            GraphMetadata metadata,
            RuleDetectionGroup detection,
            string sourceFilePath)
        {
            // Constructor validation protects downstream catalog consumers from partially populated entries.
            RuleCode = RequireText(ruleCode, nameof(ruleCode));
            Name = RequireText(name, nameof(name));
            Category = category ?? throw new ArgumentNullException(nameof(category));
            Severity = severity ?? throw new ArgumentNullException(nameof(severity));
            DefaultStatus = defaultStatus ?? throw new ArgumentNullException(nameof(defaultStatus));
            Enabled = enabled;
            Version = RequireText(version, nameof(version));
            Description = RequireText(description, nameof(description));
            DefinitionJson = RequireText(definitionJson, nameof(definitionJson));
            SourceUrls = NormalizeTextList(sourceUrls);
            IsBuiltIn = isBuiltIn;
            OwnerScope = string.IsNullOrWhiteSpace(ownerScope) ? null : ownerScope.Trim();
            Impact = NormalizeTextList(impact);
            EvidenceRequirements = NormalizeTextList(evidenceRequirements);
            RecommendedActions = NormalizeTextList(recommendedActions);
            Tags = NormalizeTextList(tags);
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Detection = detection ?? throw new ArgumentNullException(nameof(detection));
            SourceFilePath = RequireText(sourceFilePath, nameof(sourceFilePath));
        }

        /// <summary>
        /// Gets the stable rule code that identifies the catalog entry across files and runs.
        /// </summary>
        public string RuleCode { get; }

        /// <summary>
        /// Gets the human-readable rule name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the controlled rule category.
        /// </summary>
        public RuleCategory Category { get; }

        /// <summary>
        /// Gets the default finding severity.
        /// </summary>
        public FindingSeverity Severity { get; }

        /// <summary>
        /// Gets the default finding status assigned by the rule.
        /// </summary>
        public RuleFindingStatus DefaultStatus { get; }

        /// <summary>
        /// Gets a value indicating whether the rule is enabled for evaluation.
        /// </summary>
        public bool Enabled { get; }

        /// <summary>
        /// Gets a value indicating whether the rule should be selected by the evaluator.
        /// </summary>
        public bool AvailableForEvaluation
        {
            get
            {
                // Work Item 1 only gates evaluator availability on enabled state; later slices may add runtime availability filters.
                return Enabled;
            }
        }

        /// <summary>
        /// Gets the semantic rule version that preserves historical finding explainability.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Gets the human-readable rule description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the normalized source JSON used to explain the loaded rule.
        /// </summary>
        public string DefinitionJson { get; }

        /// <summary>
        /// Gets the optional explanatory source URLs.
        /// </summary>
        public IReadOnlyList<string> SourceUrls { get; }

        /// <summary>
        /// Gets a value indicating whether the rule is shipped as built-in Archon content.
        /// </summary>
        public bool IsBuiltIn { get; }

        /// <summary>
        /// Gets the optional owner scope for organization-specific ownership.
        /// </summary>
        public string? OwnerScope { get; }

        /// <summary>
        /// Gets the optional impact statements authored with the rule.
        /// </summary>
        public IReadOnlyList<string> Impact { get; }

        /// <summary>
        /// Gets the optional evidence requirements authored with the rule.
        /// </summary>
        public IReadOnlyList<string> EvidenceRequirements { get; }

        /// <summary>
        /// Gets the optional recommended actions authored with the rule.
        /// </summary>
        public IReadOnlyList<string> RecommendedActions { get; }

        /// <summary>
        /// Gets the optional lower-level tags authored with the rule.
        /// </summary>
        public IReadOnlyList<string> Tags { get; }

        /// <summary>
        /// Gets the deterministic metadata object for extension fields.
        /// </summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Gets the validated detection DSL root group.
        /// </summary>
        public RuleDetectionGroup Detection { get; }

        /// <summary>
        /// Gets the runtime file path that contributed the rule.
        /// </summary>
        public string SourceFilePath { get; }

        /// <summary>
        /// Requires a non-empty string and returns its trimmed value.
        /// </summary>
        /// <param name="value">The candidate value.</param>
        /// <param name="parameterName">The parameter name used when reporting invalid input.</param>
        /// <returns>The trimmed value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Catalog entries should never expose blank identity or explanatory strings after validation succeeds.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }

        /// <summary>
        /// Normalizes optional authored text sequences into deterministic immutable lists.
        /// </summary>
        /// <param name="values">The values to normalize.</param>
        /// <returns>An immutable list of trimmed non-empty values.</returns>
        private static IReadOnlyList<string> NormalizeTextList(IEnumerable<string> values)
        {
            // Blank optional entries are ignored so catalog consumers do not need to filter presentation noise repeatedly.
            return values.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value.Trim()).ToArray();
        }
    }
}
