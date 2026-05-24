using Archon.Domain.Graph.ControlledValues;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents a validated boolean detection group from a rule definition.
    /// </summary>
    public sealed class RuleDetectionGroup
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleDetectionGroup"/> class.
        /// </summary>
        /// <param name="nodeKinds">The optional candidate node kinds that constrain this group.</param>
        /// <param name="match">The boolean match mode for the group operands.</param>
        /// <param name="conditions">The condition operands directly owned by this group.</param>
        /// <param name="groups">The nested detection-group operands directly owned by this group.</param>
        public RuleDetectionGroup(
            IEnumerable<NodeKind> nodeKinds,
            RuleDetectionMatch match,
            IEnumerable<RuleCondition> conditions,
            IEnumerable<RuleDetectionGroup> groups)
        {
            // Copying inputs into arrays gives later evaluators deterministic immutable group state.
            NodeKinds = (nodeKinds ?? throw new ArgumentNullException(nameof(nodeKinds))).ToArray();
            Match = match ?? throw new ArgumentNullException(nameof(match));
            Conditions = (conditions ?? throw new ArgumentNullException(nameof(conditions))).ToArray();
            Groups = (groups ?? throw new ArgumentNullException(nameof(groups))).ToArray();
        }

        /// <summary>
        /// Gets the optional candidate node kinds that constrain this group.
        /// </summary>
        public IReadOnlyList<NodeKind> NodeKinds { get; }

        /// <summary>
        /// Gets the boolean match mode for the group operands.
        /// </summary>
        public RuleDetectionMatch Match { get; }

        /// <summary>
        /// Gets the condition operands directly owned by this group.
        /// </summary>
        public IReadOnlyList<RuleCondition> Conditions { get; }

        /// <summary>
        /// Gets the nested detection-group operands directly owned by this group.
        /// </summary>
        public IReadOnlyList<RuleDetectionGroup> Groups { get; }
    }
}
