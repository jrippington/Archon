using Xunit;

namespace Archon.Extractors.Tests.Integrations
{
    /// <summary>
    /// Verifies the consolidated integration extractor test category can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsIntegrationsProjectReferenceTests
    {
        /// <summary>
        /// Confirms the production integration extractor project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test references a known production marker type so missing project references fail at compile time.
            Type markerType = typeof(global::Archon.Extractors.Integrations.ArchonExtractorsIntegrationsProjectMarker);

            // Assembly names are stable project identities and should match the production project name.
            Assert.Equal("Archon.Extractors", markerType.Assembly.GetName().Name);
        }
    }
}
