using Xunit;

namespace Archon.Api.Management.Tests
{
    /// <summary>
    /// Verifies the Archon.Api.Management.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonApiManagementProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Api.Management production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Api.Management.ArchonApiManagementProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Api.Management", markerType.Assembly.GetName().Name);
        }
    }
}
