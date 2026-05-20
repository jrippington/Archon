using Xunit;

namespace Archon.Api.Extraction.Tests
{
    /// <summary>
    /// Verifies the Archon.Api.Extraction.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonApiExtractionProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Api.Extraction production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Api.Extraction.ArchonApiExtractionProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Api.Extraction", markerType.Assembly.GetName().Name);
        }
    }
}
