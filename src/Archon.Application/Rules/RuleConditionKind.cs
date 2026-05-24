using System.Text.Json.Serialization;
using Archon.Domain.Graph.ControlledValues;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Identifies the supported condition kinds in the WP012 rule detection DSL.
    /// </summary>
    [JsonConverter(typeof(ControlledValueJsonConverterFactory))]
    public sealed class RuleConditionKind : ControlledValue<RuleConditionKind>
    {
        /// <summary>Matches a project target framework against a lifecycle set.</summary>
        public static readonly RuleConditionKind TargetFrameworkMembership = new("target-framework-membership");

        /// <summary>Matches namespace graph facts or namespace metadata.</summary>
        public static readonly RuleConditionKind Namespace = new("namespace");

        /// <summary>Matches symbol graph facts such as type or member names.</summary>
        public static readonly RuleConditionKind Symbol = new("symbol");

        /// <summary>Matches package graph facts or package-use relationships.</summary>
        public static readonly RuleConditionKind Package = new("package");

        /// <summary>Matches repository-relative file path facts or evidence paths.</summary>
        public static readonly RuleConditionKind FilePattern = new("file-pattern");

        /// <summary>Matches compiler-resolved method-call graph facts.</summary>
        public static readonly RuleConditionKind MethodCall = new("method-call");

        /// <summary>Matches attribute usage facts.</summary>
        public static readonly RuleConditionKind Attribute = new("attribute");

        /// <summary>Matches numeric metric values against thresholds.</summary>
        public static readonly RuleConditionKind MetricThreshold = new("metric-threshold");

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleConditionKind"/> class.
        /// </summary>
        /// <param name="value">The stable external string for the condition kind.</param>
        private RuleConditionKind(string value)
            : base(value)
        {
            // Construction registers the condition kind with the shared controlled-value lookup table.
        }
    }
}
