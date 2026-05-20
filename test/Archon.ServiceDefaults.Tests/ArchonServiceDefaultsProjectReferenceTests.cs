using Xunit;

namespace Archon.ServiceDefaults.Tests
{
    /// <summary>
    /// Verifies the Archon.ServiceDefaults.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonServiceDefaultsProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.ServiceDefaults production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.ServiceDefaults.ArchonServiceDefaultsProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.ServiceDefaults", markerType.Assembly.GetName().Name);
        }
    }
}
