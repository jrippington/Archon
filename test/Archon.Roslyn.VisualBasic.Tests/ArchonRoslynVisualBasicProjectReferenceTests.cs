using Xunit;

namespace Archon.Roslyn.VisualBasic.Tests
{
    /// <summary>
    /// Verifies the Archon.Roslyn.VisualBasic.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonRoslynVisualBasicProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Roslyn.VisualBasic production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Roslyn.VisualBasic.ArchonRoslynVisualBasicProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Roslyn.VisualBasic", markerType.Assembly.GetName().Name);
        }
    }
}
