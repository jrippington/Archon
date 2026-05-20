using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies the ArchonMcp.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonMcpProjectReferenceTests
    {
        /// <summary>
        /// Confirms the ArchonMcp production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::ArchonMcp.Program);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("ArchonMcp", markerType.Assembly.GetName().Name);
        }
    }
}
