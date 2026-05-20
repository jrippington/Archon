using Xunit;

namespace Archon.Extractors.WinForms.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.WinForms.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsWinFormsProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.WinForms production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.WinForms.ArchonExtractorsWinFormsProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.WinForms", markerType.Assembly.GetName().Name);
        }
    }
}
