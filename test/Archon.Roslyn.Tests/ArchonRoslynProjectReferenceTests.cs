using Xunit;

namespace Archon.Roslyn.Tests
{
    /// <summary>
    /// Verifies the Archon.Roslyn.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonRoslynProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Roslyn production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Roslyn.ArchonRoslynProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Roslyn", markerType.Assembly.GetName().Name);
        }
    }
}
