using Xunit;

namespace Archon.Extractors.Maui.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.Maui.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsMauiProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.Maui production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.Maui.ArchonExtractorsMauiProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.Maui", markerType.Assembly.GetName().Name);
        }
    }
}
