using Xunit;

namespace Archon.Extractors.Blazor.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.Blazor.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsBlazorProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.Blazor production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.Blazor.ArchonExtractorsBlazorProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.Blazor", markerType.Assembly.GetName().Name);
        }
    }
}
