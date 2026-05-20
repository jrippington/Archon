using Xunit;

namespace Archon.Extractors.DataAccess.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.DataAccess.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsDataAccessProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.DataAccess production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.DataAccess.ArchonExtractorsDataAccessProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.DataAccess", markerType.Assembly.GetName().Name);
        }
    }
}
