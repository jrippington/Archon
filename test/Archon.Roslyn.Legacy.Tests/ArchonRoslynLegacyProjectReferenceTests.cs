using Xunit;

namespace Archon.Roslyn.Legacy.Tests
{
    /// <summary>
    /// Verifies the Archon.Roslyn.Legacy.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonRoslynLegacyProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Roslyn.Legacy production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Roslyn.Legacy.ArchonRoslynLegacyProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Roslyn.Legacy", markerType.Assembly.GetName().Name);
        }
    }
}
