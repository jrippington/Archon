using Xunit;

namespace Archon.Extractors.Wpf.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.Wpf.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsWpfProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.Wpf production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.Wpf.ArchonExtractorsWpfProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.Wpf", markerType.Assembly.GetName().Name);
        }
    }
}
