using Xunit;

namespace Archon.Extractors.Hotlist.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.Hotlist.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsHotlistProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.Hotlist production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.Hotlist.ArchonExtractorsHotlistProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.Hotlist", markerType.Assembly.GetName().Name);
        }
    }
}
