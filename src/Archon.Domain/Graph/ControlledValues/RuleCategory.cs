using System.Text.Json.Serialization;

namespace Archon.Domain.Graph.ControlledValues
{
    /// <summary>
    /// Identifies the category of a rule that can produce architecture findings.
    /// </summary>
    [JsonConverter(typeof(ControlledValueJsonConverterFactory))]
    public sealed class RuleCategory : ControlledValue<RuleCategory>
    {
        /// <summary>Represents lifecycle and support-status rules.</summary>
        public static readonly RuleCategory Lifecycle = new("Lifecycle");
        /// <summary>Represents obsolete API usage rules.</summary>
        public static readonly RuleCategory ObsoleteApi = new("ObsoleteApi");
        /// <summary>Represents legacy technology detection rules.</summary>
        public static readonly RuleCategory LegacyTechnology = new("LegacyTechnology");
        /// <summary>Represents security-sensitive usage rules.</summary>
        public static readonly RuleCategory SecuritySensitive = new("SecuritySensitive");
        /// <summary>Represents data-access architecture rules.</summary>
        public static readonly RuleCategory DataAccess = new("DataAccess");
        /// <summary>Represents configuration architecture rules.</summary>
        public static readonly RuleCategory Configuration = new("Configuration");
        /// <summary>Represents architecture layering rules.</summary>
        public static readonly RuleCategory ArchitectureLayering = new("ArchitectureLayering");
        /// <summary>Represents dependency risk rules.</summary>
        public static readonly RuleCategory DependencyRisk = new("DependencyRisk");
        /// <summary>Represents modernization blocker rules.</summary>
        public static readonly RuleCategory ModernizationBlocker = new("ModernizationBlocker");
        /// <summary>Represents organization-specific rules.</summary>
        public static readonly RuleCategory OrganisationSpecific = new("OrganisationSpecific");

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleCategory"/> class.
        /// </summary>
        /// <param name="value">The stable external string for the rule category.</param>
        private RuleCategory(string value)
            : base(value)
        {
            // Construction registers the rule category with the shared controlled-value lookup table.
        }
    }
}
