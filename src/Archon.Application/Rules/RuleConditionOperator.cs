using System.Text.Json.Serialization;
using Archon.Domain.Graph.ControlledValues;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Identifies the supported comparison operators in the WP012 rule detection DSL.
    /// </summary>
    [JsonConverter(typeof(ControlledValueJsonConverterFactory))]
    public sealed class RuleConditionOperator : ControlledValue<RuleConditionOperator>
    {
        /// <summary>Requires the left value to equal the right value.</summary>
        public static readonly RuleConditionOperator Equal = new("Equal");

        /// <summary>Requires the left value to differ from the right value.</summary>
        public static readonly RuleConditionOperator NotEqual = new("NotEqual");

        /// <summary>Requires the left numeric value to be greater than the right numeric value.</summary>
        public static readonly RuleConditionOperator GreaterThan = new("GreaterThan");

        /// <summary>Requires the left numeric value to be greater than or equal to the right numeric value.</summary>
        public static readonly RuleConditionOperator GreaterThanOrEqual = new("GreaterThanOrEqual");

        /// <summary>Requires the left numeric value to be less than the right numeric value.</summary>
        public static readonly RuleConditionOperator LessThan = new("LessThan");

        /// <summary>Requires the left numeric value to be less than or equal to the right numeric value.</summary>
        public static readonly RuleConditionOperator LessThanOrEqual = new("LessThanOrEqual");

        /// <summary>Requires the left value to appear in the right value set.</summary>
        public static readonly RuleConditionOperator In = new("In");

        /// <summary>Requires the left value not to appear in the right value set.</summary>
        public static readonly RuleConditionOperator NotIn = new("NotIn");

        /// <summary>Requires the left string value to contain the right string fragment.</summary>
        public static readonly RuleConditionOperator Contains = new("Contains");

        /// <summary>Requires the left string value to start with the right string prefix.</summary>
        public static readonly RuleConditionOperator StartsWith = new("StartsWith");

        /// <summary>Requires the left string value to end with the right string suffix.</summary>
        public static readonly RuleConditionOperator EndsWith = new("EndsWith");

        /// <summary>Requires the left string value to match a bounded pattern.</summary>
        public static readonly RuleConditionOperator MatchesPattern = new("MatchesPattern");

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleConditionOperator"/> class.
        /// </summary>
        /// <param name="value">The stable external string for the condition operator.</param>
        private RuleConditionOperator(string value)
            : base(value)
        {
            // Construction registers the operator with the shared controlled-value lookup table.
        }
    }
}
