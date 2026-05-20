using Xunit;

namespace Archon.Extractors.EntityFramework.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.EntityFramework.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsEntityFrameworkProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.EntityFramework production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.EntityFramework.ArchonExtractorsEntityFrameworkProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.EntityFramework", markerType.Assembly.GetName().Name);
        }
    }
}
