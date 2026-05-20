using Xunit;

namespace Archon.Extractors.Avalonia.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.Avalonia.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsAvaloniaProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.Avalonia production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.Avalonia.ArchonExtractorsAvaloniaProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.Avalonia", markerType.Assembly.GetName().Name);
        }
    }
}
