using System.Runtime.CompilerServices;

// The infrastructure test project receives access to focused internal seams, such as Neo4j persistence batching, without exposing those
// seams as production APIs outside the infrastructure adapter.
[assembly: InternalsVisibleTo("Archon.Infrastructure.Neo4j.Tests")]
