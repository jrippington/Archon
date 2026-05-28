using Xunit;

namespace Archon.Extractors.Tests.EntityFramework
{
    /// <summary>
    /// Verifies the Archon.Extractors.Tests EntityFramework category can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsEntityFrameworkProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.EntityFramework category is available from the consolidated production project and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.EntityFramework.ArchonExtractorsEntityFrameworkProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors", markerType.Assembly.GetName().Name);
        }
    }
}
