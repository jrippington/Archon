using Xunit;

namespace Archon.Application.Tests
{
    /// <summary>
    /// Verifies the Archon.Application.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonApplicationProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Application production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Application.ArchonApplicationProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Application", markerType.Assembly.GetName().Name);
        }
    }
}
