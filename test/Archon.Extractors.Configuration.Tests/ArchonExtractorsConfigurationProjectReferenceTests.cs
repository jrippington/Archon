using Xunit;

namespace Archon.Extractors.Configuration.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.Configuration.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsConfigurationProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.Configuration production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.Configuration.ArchonExtractorsConfigurationProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.Configuration", markerType.Assembly.GetName().Name);
        }
    }
}
