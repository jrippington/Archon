using Xunit;

namespace ArchonApi.Tests
{
    /// <summary>
    /// Verifies the ArchonApi.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonApiProjectReferenceTests
    {
        /// <summary>
        /// Confirms the ArchonApi production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::ArchonApi.Program);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("ArchonApi", markerType.Assembly.GetName().Name);
        }
    }
}
