using Xunit;

namespace Archon.Infrastructure.Roslyn.Tests
{
    /// <summary>
    /// Verifies the Archon.Infrastructure.Roslyn.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonInfrastructureRoslynProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Infrastructure.Roslyn production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Infrastructure.Roslyn.ArchonInfrastructureRoslynProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Infrastructure.Roslyn", markerType.Assembly.GetName().Name);
        }
    }
}
