using Xunit;

namespace Archon.Extractors.AdoNet.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.AdoNet.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsAdoNetProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.AdoNet production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.AdoNet.ArchonExtractorsAdoNetProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.AdoNet", markerType.Assembly.GetName().Name);
        }
    }
}
