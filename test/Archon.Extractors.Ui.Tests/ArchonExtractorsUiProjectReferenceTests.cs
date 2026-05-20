using Xunit;

namespace Archon.Extractors.Ui.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.Ui.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsUiProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.Ui production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.Ui.ArchonExtractorsUiProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.Ui", markerType.Assembly.GetName().Name);
        }
    }
}
