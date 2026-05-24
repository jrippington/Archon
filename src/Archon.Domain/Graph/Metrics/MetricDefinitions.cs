using Archon.Domain.Graph.ControlledValues;

namespace Archon.Domain.Graph.Metrics
{
    /// <summary>
    /// Provides the central registry of stable metric definitions introduced by Archon work packages.
    /// </summary>
    public static class MetricDefinitions
    {
        /// <summary>
        /// Gets the first WP013 metric, which counts architecture nodes owned by one extracted snapshot.
        /// </summary>
        public static MetricDefinition SnapshotNodeCount { get; } = new(
            "SnapshotNodeCount",
            "Snapshot node count",
            MetricScopeKind.Snapshot,
            "nodes");

        /// <summary>
        /// Gets the project metric that counts project-reference edges targeting one project.
        /// </summary>
        public static MetricDefinition ProjectIncomingReferenceCount { get; } = new(
            "ProjectIncomingReferenceCount",
            "Project incoming reference count",
            MetricScopeKind.Project,
            "references");

        /// <summary>
        /// Gets the project metric that counts project-reference edges originating from one project.
        /// </summary>
        public static MetricDefinition ProjectOutgoingReferenceCount { get; } = new(
            "ProjectOutgoingReferenceCount",
            "Project outgoing reference count",
            MetricScopeKind.Project,
            "references");

        /// <summary>
        /// Gets the project metric that counts package dependencies visible in project metadata.
        /// </summary>
        public static MetricDefinition ProjectPackageCount { get; } = new(
            "ProjectPackageCount",
            "Project package count",
            MetricScopeKind.Project,
            "packages");

        /// <summary>
        /// Gets the project metric that counts public type nodes owned by one project.
        /// </summary>
        public static MetricDefinition ProjectPublicTypeCount { get; } = new(
            "ProjectPublicTypeCount",
            "Project public type count",
            MetricScopeKind.Project,
            "types");

        /// <summary>
        /// Gets the project metric that counts endpoint nodes owned by one project.
        /// </summary>
        public static MetricDefinition ProjectEndpointCount { get; } = new(
            "ProjectEndpointCount",
            "Project endpoint count",
            MetricScopeKind.Project,
            "endpoints");

        /// <summary>
        /// Gets the project metric that counts data-access nodes owned by one project.
        /// </summary>
        public static MetricDefinition ProjectDataAccessCount { get; } = new(
            "ProjectDataAccessCount",
            "Project data-access count",
            MetricScopeKind.Project,
            "facts");

        /// <summary>
        /// Gets the project metric that counts open hotlist findings associated with one project.
        /// </summary>
        public static MetricDefinition ProjectHotlistFindingCount { get; } = new(
            "ProjectHotlistFindingCount",
            "Project hotlist finding count",
            MetricScopeKind.Project,
            "findings");

        /// <summary>
        /// Gets the project metric that categorizes target-framework age or risk.
        /// </summary>
        public static MetricDefinition ProjectTargetFrameworkRisk { get; } = new(
            "ProjectTargetFrameworkRisk",
            "Project target framework risk",
            MetricScopeKind.Project,
            "risk");

        /// <summary>
        /// Gets the graph metric that counts dependency edges targeting one architecture node.
        /// </summary>
        public static MetricDefinition GraphFanIn { get; } = new(
            "GraphFanIn",
            "Graph fan-in",
            MetricScopeKind.Node,
            "edges");

        /// <summary>
        /// Gets the graph metric that counts dependency edges originating from one architecture node.
        /// </summary>
        public static MetricDefinition GraphFanOut { get; } = new(
            "GraphFanOut",
            "Graph fan-out",
            MetricScopeKind.Node,
            "edges");

        /// <summary>
        /// Gets the graph metric that normalizes direct fan-in and fan-out against the largest possible directed degree.
        /// </summary>
        public static MetricDefinition GraphDegreeCentrality { get; } = new(
            "GraphDegreeCentrality",
            "Graph degree centrality",
            MetricScopeKind.Node,
            "ratio");

        /// <summary>
        /// Gets the graph metric that records the deepest bounded dependency path reachable from one architecture node.
        /// </summary>
        public static MetricDefinition GraphDependencyDepth { get; } = new(
            "GraphDependencyDepth",
            "Graph dependency depth",
            MetricScopeKind.Node,
            "hops");

        /// <summary>
        /// Gets the graph metric that counts unique transitive dependency nodes reachable from one architecture node.
        /// </summary>
        public static MetricDefinition GraphTransitiveDependencyCount { get; } = new(
            "GraphTransitiveDependencyCount",
            "Graph transitive dependency count",
            MetricScopeKind.Node,
            "nodes");

        /// <summary>
        /// Gets the graph metric that counts the unique direct incoming and outgoing dependency neighbours for one architecture node.
        /// </summary>
        public static MetricDefinition GraphNeighbourhoodSize { get; } = new(
            "GraphNeighbourhoodSize",
            "Graph neighbourhood size",
            MetricScopeKind.Node,
            "nodes");

        /// <summary>
        /// Gets the graph metric reserved for future cycle participation calculation.
        /// </summary>
        public static MetricDefinition GraphCycleParticipation { get; } = new(
            "GraphCycleParticipation",
            "Graph cycle participation",
            MetricScopeKind.Node,
            "state");

        /// <summary>
        /// Gets the modernization metric that counts deterministic legacy technology facts at a supported rollup scope.
        /// </summary>
        public static MetricDefinition ModernizationLegacyTechnologyCount { get; } = new(
            "ModernizationLegacyTechnologyCount",
            "Modernization legacy technology count",
            MetricScopeKind.Snapshot,
            "facts");

        /// <summary>
        /// Gets the modernization metric that counts security-sensitive findings at a supported rollup scope.
        /// </summary>
        public static MetricDefinition ModernizationSecuritySensitiveFindingCount { get; } = new(
            "ModernizationSecuritySensitiveFindingCount",
            "Modernization security-sensitive finding count",
            MetricScopeKind.Snapshot,
            "findings");

        /// <summary>
        /// Gets the modernization metric that counts out-of-support project target frameworks at a supported rollup scope.
        /// </summary>
        public static MetricDefinition ModernizationOutOfSupportTargetCount { get; } = new(
            "ModernizationOutOfSupportTargetCount",
            "Modernization out-of-support target count",
            MetricScopeKind.Snapshot,
            "targets");

        /// <summary>
        /// Gets the modernization metric that counts framework-only dependency signals at a supported rollup scope.
        /// </summary>
        public static MetricDefinition ModernizationFrameworkOnlyDependencyCount { get; } = new(
            "ModernizationFrameworkOnlyDependencyCount",
            "Modernization framework-only dependency count",
            MetricScopeKind.Snapshot,
            "dependencies");

        /// <summary>
        /// Gets the modernization metric that counts distinct projects containing data-access facts for a rollup scope.
        /// </summary>
        public static MetricDefinition ModernizationDataAccessSpread { get; } = new(
            "ModernizationDataAccessSpread",
            "Modernization data-access spread",
            MetricScopeKind.Snapshot,
            "projects");

        /// <summary>
        /// Gets the modernization metric that counts database table identities shared by more than one project.
        /// </summary>
        public static MetricDefinition ModernizationSharedTableUsageCount { get; } = new(
            "ModernizationSharedTableUsageCount",
            "Modernization shared table usage count",
            MetricScopeKind.Snapshot,
            "tables");

        /// <summary>
        /// Gets all currently registered metric definitions in deterministic declaration order.
        /// </summary>
        public static IReadOnlyList<MetricDefinition> All { get; } =
        [
            SnapshotNodeCount,
            ProjectIncomingReferenceCount,
            ProjectOutgoingReferenceCount,
            ProjectPackageCount,
            ProjectPublicTypeCount,
            ProjectEndpointCount,
            ProjectDataAccessCount,
            ProjectHotlistFindingCount,
            ProjectTargetFrameworkRisk,
            GraphFanIn,
            GraphFanOut,
            GraphDegreeCentrality,
            GraphDependencyDepth,
            GraphTransitiveDependencyCount,
            GraphNeighbourhoodSize,
            GraphCycleParticipation,
            ModernizationLegacyTechnologyCount,
            ModernizationSecuritySensitiveFindingCount,
            ModernizationOutOfSupportTargetCount,
            ModernizationFrameworkOnlyDependencyCount,
            ModernizationDataAccessSpread,
            ModernizationSharedTableUsageCount
        ];
    }
}
