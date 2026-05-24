using Archon.Domain.Graph.ControlledValues;

namespace Archon.Application.ArchitectureRules
{
    /// <summary>
    /// Provides the stable identities and metadata for WP013 built-in architecture-rule checks.
    /// </summary>
    public static class ArchitectureRuleChecks
    {
        /// <summary>
        /// Identifies the generic check that flags domain projects referencing infrastructure projects.
        /// </summary>
        public const string DomainReferencesInfrastructure = "ARCHON-ARCH-DOMAIN-REFERENCES-INFRASTRUCTURE";

        /// <summary>
        /// Identifies the generic check that flags domain projects referencing web projects.
        /// </summary>
        public const string DomainReferencesWeb = "ARCHON-ARCH-DOMAIN-REFERENCES-WEB";

        /// <summary>
        /// Identifies the generic check that flags web projects referenced by non-web projects.
        /// </summary>
        public const string WebReferencedByNonWeb = "ARCHON-ARCH-WEB-REFERENCED-BY-NON-WEB";

        /// <summary>
        /// Identifies the configurable check that flags application projects using LINQ to SQL directly.
        /// </summary>
        public const string ApplicationUsesLinqToSqlDirectly = "ARCHON-ARCH-APPLICATION-LINQ-TO-SQL-DIRECT";

        /// <summary>
        /// Identifies the configurable check that flags controllers using DataContext directly.
        /// </summary>
        public const string ControllerUsesDataContextDirectly = "ARCHON-ARCH-CONTROLLER-DATACONTEXT-DIRECT";

        /// <summary>
        /// Identifies the generic check that records unknown state when a worker appears to require queue or topic dependencies but none were observed.
        /// </summary>
        public const string WorkerMissingQueueOrTopicDependency = "ARCHON-ARCH-WORKER-MISSING-MESSAGING-DEPENDENCY";

        /// <summary>
        /// Identifies the metric-dependent check that requires review before changing high fan-in shared libraries.
        /// </summary>
        public const string SharedLibraryHighFanInReview = "ARCHON-ARCH-SHARED-LIBRARY-HIGH-FAN-IN-REVIEW";

        /// <summary>
        /// Gets all built-in rule descriptors in deterministic order.
        /// </summary>
        public static IReadOnlyList<ArchitectureRuleCheckDefinition> All { get; } =
        [
            new ArchitectureRuleCheckDefinition(DomainReferencesInfrastructure, RuleCategory.ArchitectureLayering, "Domain project references infrastructure", "Domain projects should remain inward and must not reference infrastructure projects directly."),
            new ArchitectureRuleCheckDefinition(DomainReferencesWeb, RuleCategory.ArchitectureLayering, "Domain project references web", "Domain projects should remain inward and must not reference web or host projects directly."),
            new ArchitectureRuleCheckDefinition(WebReferencedByNonWeb, RuleCategory.ArchitectureLayering, "Web project is referenced by a non-web project", "Web and host projects are composition roots and should not become dependencies of lower-level projects."),
            new ArchitectureRuleCheckDefinition(ApplicationUsesLinqToSqlDirectly, RuleCategory.DataAccess, "Application project uses LINQ to SQL directly", "Application projects should not use LINQ to SQL directly unless an explicit catalog or runtime option allows that legacy dependency."),
            new ArchitectureRuleCheckDefinition(ControllerUsesDataContextDirectly, RuleCategory.DataAccess, "Controller uses DataContext directly", "Controllers should not use DataContext directly unless an explicit catalog or runtime option allows that legacy dependency."),
            new ArchitectureRuleCheckDefinition(WorkerMissingQueueOrTopicDependency, RuleCategory.DependencyRisk, "Worker has no observed queue or topic dependency", "Worker projects with messaging evidence should expose a queue or topic dependency so runtime coupling is visible."),
            new ArchitectureRuleCheckDefinition(SharedLibraryHighFanInReview, RuleCategory.DependencyRisk, "Shared library has high fan-in", "Shared libraries with high fan-in should be reviewed before change because many projects depend on them.")
        ];

        /// <summary>
        /// Finds a built-in definition by stable rule code.
        /// </summary>
        /// <param name="ruleCode">The stable rule code to find.</param>
        /// <returns>The matching definition, or <see langword="null"/> when no built-in definition exists for the supplied code.</returns>
        public static ArchitectureRuleCheckDefinition? Find(string ruleCode)
        {
            // Rule codes are compared ordinally because they are stable machine-readable identifiers, not culture-sensitive labels.
            return All.FirstOrDefault(definition => StringComparer.Ordinal.Equals(definition.RuleCode, ruleCode));
        }
    }
}
