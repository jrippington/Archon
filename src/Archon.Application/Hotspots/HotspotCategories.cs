namespace Archon.Application.Hotspots
{
    /// <summary>
    /// Provides stable hotspot category names used by scoring, APIs, tests, and documentation.
    /// </summary>
    public static class HotspotCategories
    {
        /// <summary>
        /// Identifies a node that is depended on by many other nodes.
        /// </summary>
        public const string HighFanIn = "HighFanIn";

        /// <summary>
        /// Identifies a node that depends on many other nodes.
        /// </summary>
        public const string HighFanOut = "HighFanOut";

        /// <summary>
        /// Identifies a shared library or shared project that has broad incoming dependency pressure.
        /// </summary>
        public const string SharedLibrary = "SharedLibrary";

        /// <summary>
        /// Identifies a snapshot with data-access facts spread across several projects.
        /// </summary>
        public const string DataAccessSpread = "DataAccessSpread";

        /// <summary>
        /// Identifies a snapshot where database table identities are shared by more than one project.
        /// </summary>
        public const string SharedTableUsage = "SharedTableUsage";

        /// <summary>
        /// Identifies an architecture node with many open hotlist findings.
        /// </summary>
        public const string HotlistFindingConcentration = "HotlistFindingConcentration";

        /// <summary>
        /// Identifies an architecture node that reaches a deep dependency path.
        /// </summary>
        public const string DependencyDepth = "DependencyDepth";

        /// <summary>
        /// Identifies an architecture node with many unique transitive dependencies.
        /// </summary>
        public const string TransitiveDependencyCount = "TransitiveDependencyCount";

        /// <summary>
        /// Identifies an architecture node that participates in dependency cycles.
        /// </summary>
        public const string CycleParticipation = "CycleParticipation";
    }
}
