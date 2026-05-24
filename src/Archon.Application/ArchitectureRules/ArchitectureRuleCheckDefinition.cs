using Archon.Domain.Graph.ControlledValues;

namespace Archon.Application.ArchitectureRules
{
    /// <summary>
    /// Describes one built-in architecture-rule check without embedding organization-specific policy exceptions.
    /// </summary>
    /// <param name="RuleCode">The stable rule/check identity used in results, API filters, and configured catalog matching.</param>
    /// <param name="Category">The controlled category that groups the rule result for API consumers.</param>
    /// <param name="Name">The developer-facing rule name.</param>
    /// <param name="Description">The developer-facing explanation of the generic check.</param>
    public sealed record ArchitectureRuleCheckDefinition(
        string RuleCode,
        RuleCategory Category,
        string Name,
        string Description);
}
