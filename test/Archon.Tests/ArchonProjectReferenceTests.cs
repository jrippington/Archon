using Xunit;

namespace Archon.Tests
{
    /// <summary>
    /// Verifies the Archon.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Program);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon", markerType.Assembly.GetName().Name);
        }
    }
}
