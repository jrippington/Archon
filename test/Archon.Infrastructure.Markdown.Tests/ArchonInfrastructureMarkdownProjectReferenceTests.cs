using Xunit;

namespace Archon.Infrastructure.Markdown.Tests
{
    /// <summary>
    /// Verifies the Archon.Infrastructure.Markdown.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonInfrastructureMarkdownProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Infrastructure.Markdown production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Infrastructure.Markdown.ArchonInfrastructureMarkdownProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Infrastructure.Markdown", markerType.Assembly.GetName().Name);
        }
    }
}
