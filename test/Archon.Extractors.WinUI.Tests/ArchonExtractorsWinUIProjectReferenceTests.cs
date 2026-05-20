using Xunit;

namespace Archon.Extractors.WinUI.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.WinUI.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsWinUIProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.WinUI production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.WinUI.ArchonExtractorsWinUIProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.WinUI", markerType.Assembly.GetName().Name);
        }
    }
}
