using Xunit;

namespace Archon.Extractors.AspNet.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.AspNet.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsAspNetProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.AspNet production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.AspNet.ArchonExtractorsAspNetProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.AspNet", markerType.Assembly.GetName().Name);
        }
    }
}
