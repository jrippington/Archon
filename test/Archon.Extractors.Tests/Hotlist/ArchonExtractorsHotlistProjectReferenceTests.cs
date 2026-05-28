using Xunit;

namespace Archon.Extractors.Tests.Hotlist
{
    /// <summary>
    /// Verifies that the consolidated extractor test assembly can load the migrated Hotlist category from the consolidated production assembly.
    /// </summary>
    /// <remarks>
    /// The test preserves the original project-reference smoke-test intent while asserting the new assembly identity introduced
    /// by the extractor consolidation work.
    /// </remarks>
    public sealed class ArchonExtractorsHotlistProjectReferenceTests
    {
        /// <summary>
        /// Confirms the migrated Hotlist marker is available from the consolidated production assembly.
        /// </summary>
        /// <remarks>
        /// The scenario uses a known production type so project reference failures surface as compile errors before runtime.
        /// It then checks both the physical assembly name and the marker's canonical project-name contract.
        /// </remarks>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // Resolve the marker through the migrated production namespace to prove the category is compiled into the consolidated assembly.
            Type markerType = typeof(global::Archon.Extractors.Hotlist.ArchonExtractorsHotlistProjectMarker);

            // Assembly names are deterministic project identities and should now match the consolidated production project name.
            Assert.Equal("Archon.Extractors", markerType.Assembly.GetName().Name);

            // The marker contract should report the same consolidated project identity that runtime discovery sees.
            Archon.Extractors.Hotlist.ArchonExtractorsHotlistProjectMarker marker = new();
            Assert.Equal("Archon.Extractors", marker.GetProjectName());
        }
    }
}
