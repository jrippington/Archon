using System.Text.Json;
using Archon.Application.Rules;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Metadata;

namespace Archon.Infrastructure.Neo4j.Tests.Persistence
{
    /// <summary>
    /// Creates validated rule catalog entries for Neo4j persistence tests.
    /// </summary>
    internal static class RuleCatalogTestEntryFactory
    {
        /// <summary>
        /// Creates a representative lifecycle rule catalog entry.
        /// </summary>
        /// <param name="ruleCode">The stable rule code to assign.</param>
        /// <param name="version">The exact rule version to assign.</param>
        /// <param name="enabled">A value indicating whether the rule is enabled for evaluation.</param>
        /// <returns>A validated rule catalog entry suitable for persistence tests.</returns>
        internal static RuleCatalogEntry Create(string ruleCode, string version, bool enabled)
        {
            // The entry is built directly so infrastructure tests do not need filesystem rule fixtures for mapper and Cypher behavior.
            return new RuleCatalogEntry(
                ruleCode,
                "Neo4j persistence test rule",
                RuleCategory.Lifecycle,
                FindingSeverity.High,
                RuleFindingStatus.OutOfSupport,
                enabled,
                version,
                "Flags legacy target frameworks.",
                $"{{\"ruleCode\":\"{ruleCode}\",\"version\":\"{version}\"}}",
                new[] { "https://example.invalid/rules" },
                true,
                "Archon",
                new[] { "Legacy target frameworks increase risk." },
                new[] { "Project target framework metadata must be available." },
                new[] { "Plan migration." },
                new[] { "lifecycle" },
                GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["ruleFamily"] = "lifecycle"
                }),
                new RuleDetectionGroup(
                    [NodeKind.Project],
                    RuleDetectionMatch.MatchAll,
                    new[] { new RuleCondition(
                        RuleConditionKind.TargetFrameworkMembership,
                        RuleConditionOperator.Equal,
                        CreateConditionPayload("net48")) },
                    Array.Empty<RuleDetectionGroup>()),
                "rules/test.json");
        }

        /// <summary>
        /// Creates a cloned JSON payload for a target framework condition.
        /// </summary>
        /// <param name="value">The target framework value to include in the payload.</param>
        /// <returns>A JSON element payload suitable for a rule condition.</returns>
        private static JsonElement CreateConditionPayload(string value)
        {
            // RuleCondition owns a cloned payload, so the short-lived document can be disposed immediately after construction.
            using JsonDocument document = JsonDocument.Parse($"{{\"value\":\"{value}\"}}");
            return document.RootElement.Clone();
        }
    }
}
