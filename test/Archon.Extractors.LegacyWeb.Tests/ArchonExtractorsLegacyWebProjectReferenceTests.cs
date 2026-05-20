using Xunit;

namespace Archon.Extractors.LegacyWeb.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.LegacyWeb.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsLegacyWebProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.LegacyWeb production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.LegacyWeb.ArchonExtractorsLegacyWebProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.LegacyWeb", markerType.Assembly.GetName().Name);
        }
    }
}
