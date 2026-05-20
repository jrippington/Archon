using Xunit;

namespace Archon.Extractors.LinqToSql.Tests
{
    /// <summary>
    /// Verifies the Archon.Extractors.LinqToSql.Tests skeleton can load its corresponding production assembly.
    /// </summary>
    public sealed class ArchonExtractorsLinqToSqlProjectReferenceTests
    {
        /// <summary>
        /// Confirms the Archon.Extractors.LinqToSql production project is referenced and exposes loadable assembly metadata.
        /// </summary>
        [Fact]
        public void ProductionAssemblyCanBeLoaded()
        {
            // The test uses a known production type so project reference failures surface as compile errors before runtime.
            Type markerType = typeof(global::Archon.Extractors.LinqToSql.ArchonExtractorsLinqToSqlProjectMarker);

            // Assembly names are deterministic project identities and should match the production project name.
            Assert.Equal("Archon.Extractors.LinqToSql", markerType.Assembly.GetName().Name);
        }
    }
}
