namespace Archon.Application.Hotspots
{
    /// <summary>
    /// Defines the numeric thresholds that convert metric and finding counts into hotspot records.
    /// </summary>
    /// <param name="HighFanIn">The minimum graph fan-in value that creates a high fan-in hotspot.</param>
    /// <param name="HighFanOut">The minimum graph fan-out value that creates a high fan-out hotspot.</param>
    /// <param name="SharedLibraryFanIn">The minimum graph fan-in value that classifies a node as shared-library risk.</param>
    /// <param name="DataAccessSpread">The minimum data-access project spread value that creates a modernization hotspot.</param>
    /// <param name="SharedTableUsage">The minimum shared table usage count that creates a modernization hotspot.</param>
    /// <param name="HotlistFindingConcentration">The minimum open finding count on one node that creates a finding-concentration hotspot.</param>
    /// <param name="DependencyDepth">The minimum dependency depth value that creates a depth hotspot.</param>
    /// <param name="TransitiveDependencyCount">The minimum transitive dependency count that creates a transitive-dependency hotspot.</param>
    /// <param name="CycleParticipation">The minimum cycle participation count that creates a cycle hotspot.</param>
    public sealed record HotspotThresholds(
        decimal HighFanIn,
        decimal HighFanOut,
        decimal SharedLibraryFanIn,
        decimal DataAccessSpread,
        decimal SharedTableUsage,
        decimal HotlistFindingConcentration,
        decimal DependencyDepth,
        decimal TransitiveDependencyCount,
        decimal CycleParticipation)
    {
        /// <summary>
        /// Gets the documented default thresholds for WP013 hotspot detection.
        /// </summary>
        public static HotspotThresholds Default { get; } = new(
            HighFanIn: 5,
            HighFanOut: 5,
            SharedLibraryFanIn: 8,
            DataAccessSpread: 3,
            SharedTableUsage: 2,
            HotlistFindingConcentration: 3,
            DependencyDepth: 4,
            TransitiveDependencyCount: 10,
            CycleParticipation: 1);
    }
}
