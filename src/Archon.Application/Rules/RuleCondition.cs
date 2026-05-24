using System.Text.Json;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents one validated condition in a rule detection group.
    /// </summary>
    public sealed class RuleCondition
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleCondition"/> class.
        /// </summary>
        /// <param name="kind">The supported condition kind.</param>
        /// <param name="operator">The supported condition operator.</param>
        /// <param name="payload">The immutable condition payload copied from the rule file.</param>
        public RuleCondition(RuleConditionKind kind, RuleConditionOperator @operator, JsonElement payload)
        {
            // The payload is cloned because JsonDocument ownership ends after parsing completes.
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            Operator = @operator ?? throw new ArgumentNullException(nameof(@operator));
            Payload = payload.Clone();
        }

        /// <summary>
        /// Gets the supported condition kind.
        /// </summary>
        public RuleConditionKind Kind { get; }

        /// <summary>
        /// Gets the supported condition operator.
        /// </summary>
        public RuleConditionOperator Operator { get; }

        /// <summary>
        /// Gets the immutable condition payload copied from the rule file.
        /// </summary>
        public JsonElement Payload { get; }
    }
}
