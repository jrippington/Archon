using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests
{
    /// <summary>
    /// Verifies the Archon.Infrastructure.Neo4j.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonInfrastructureNeo4jProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Infrastructure.Neo4j production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Infrastructure.Neo4j.ArchonInfrastructureNeo4jProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Infrastructure.Neo4j", markerType.Assembly.GetName().Name);
        }
    }
}
