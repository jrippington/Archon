using Xunit;

namespace Archon.Extractors.Razor.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.Razor.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsRazorProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.Razor production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.Razor.ArchonExtractorsRazorProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.Razor", markerType.Assembly.GetName().Name);
        }
    }
}
