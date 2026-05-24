using System.Text.Json.Serialization;
using Archon.Domain.Graph.ControlledValues;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Identifies how a rule detection group combines condition and nested-group operands.
    /// </summary>
    [JsonConverter(typeof(ControlledValueJsonConverterFactory))]
    public sealed class RuleDetectionMatch : ControlledValue<RuleDetectionMatch>
    {
        /// <summary>Requires every operand in the detection group to match.</summary>
        public static readonly RuleDetectionMatch MatchAll = new("all");

        /// <summary>Requires at least one operand in the detection group to match.</summary>
        public static readonly RuleDetectionMatch MatchAny = new("any");

        /// <summary>Requires no operand in the detection group to match.</summary>
        public static readonly RuleDetectionMatch MatchNone = new("none");

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleDetectionMatch"/> class.
        /// </summary>
        /// <param name="value">The stable external string for the match mode.</param>
        private RuleDetectionMatch(string value)
            : base(value)
        {
            // Construction registers the match mode with the shared controlled-value lookup table.
        }
    }
}
