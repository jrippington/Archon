using Xunit;

namespace Archon.Domain.Tests
{
    /// <summary>
    /// Verifies the Archon.Domain.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonDomainProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Domain production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Domain.ArchonDomainProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Domain", markerType.Assembly.GetName().Name);
        }
    }
}
