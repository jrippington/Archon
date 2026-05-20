using Xunit;

namespace Archon.Api.Query.Tests
{
    /// <summary>
    /// Verifies the Archon.Api.Query.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonApiQueryProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Api.Query production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Api.Query.ArchonApiQueryProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Api.Query", markerType.Assembly.GetName().Name);
        }
    }
}
