using System.Xml.Linq;
using Xunit;

namespace Archon.Tests
{
    /// <summary>
    /// Verifies the Aspire AppHost composition through static metadata checks that never start distributed resources.
    /// </summary>
    /// <remarks>
    /// Work Item 3 requires automated validation to avoid running the AppHost as a long-running process. These tests inspect
    /// project and source metadata so they can prove the intended composition contract without requiring Docker or Neo4j startup.
    /// </remarks>
    public sealed class AppHostCompositionMetadataTests
    {
        /// <summary>
        /// Confirms the AppHost project keeps the required Aspire SDK and references the API and MCP host projects.
        /// </summary>
        [Fact]
        public void AppHostProjectReferencesRequiredHostProjects()
        {
            // The repository root is located from the test output directory so the check works across developer machines.
            string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
            string appHostProjectPath = Path.Combine(repositoryRoot, "src", "Archon", "Archon.csproj");

            XDocument project = XDocument.Load(appHostProjectPath);
            string? sdk = project.Root?.Attribute("Sdk")?.Value;
            string[] references = project.Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty)
                .ToArray();

            Assert.Equal("Aspire.AppHost.Sdk/13.3.3", sdk);
            Assert.Contains("..\\ArchonApi\\ArchonApi.csproj", references);
            Assert.Contains("..\\ArchonMcp\\ArchonMcp.csproj", references);
        }

        /// <summary>
        /// Confirms the AppHost source composes Neo4j, the API host, and the MCP host without composing Discovery UI.
        /// </summary>
        [Fact]
        public void AppHostSourceDeclaresExpectedResourcesOnly()
        {
            // Static source inspection avoids the blocking AppHost execution path while still detecting accidental composition drift.
            string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
            string programPath = Path.Combine(repositoryRoot, "src", "Archon", "Program.cs");
            string source = File.ReadAllText(programPath);

            Assert.Contains("DistributedApplication.CreateBuilder", source);
            Assert.Contains("AddContainer(\"neo4j\"", source);
            Assert.Contains("AddParameter(\"neo4j-username\")", source);
            Assert.Contains("AddParameter(\"neo4j-password\", secret: true)", source);
            Assert.Contains("ReferenceExpression.Create($\"{neo4jUsername}/{neo4jPassword}\")", source);
            Assert.Contains("WithEnvironment(\"NEO4J_AUTH\"", source);
            Assert.Contains("WithEnvironment(\"NEO4J_server_http_advertised__address\", \"localhost:7474\")", source);
            Assert.Contains("WithEnvironment(\"NEO4J_server_bolt_advertised__address\", \"localhost:7687\")", source);
            Assert.Contains("WithVolume(\"archon-neo4j-data\", \"/data\")", source);
            Assert.Contains("WithVolume(\"archon-neo4j-logs\", \"/logs\")", source);
            Assert.Contains("WithVolume(\"archon-neo4j-import\", \"/var/lib/neo4j/import\")", source);
            Assert.Contains("WithVolume(\"archon-neo4j-plugins\", \"/plugins\")", source);
            Assert.Contains("WithHttpEndpoint(port: 7474, targetPort: 7474, name: \"browser\")", source);
            Assert.Contains("WithEndpoint(port: 7687, targetPort: 7687, scheme: \"tcp\", name: \"bolt\")", source);
            Assert.DoesNotContain("WithEnvironment(\"NEO4J_AUTH\", \"none\")", source, StringComparison.Ordinal);
            Assert.Contains("AddProject<Projects.ArchonApi>(\"ArchonApi\")", source);
            Assert.Contains("AddProject<Projects.ArchonMcp>(\"ArchonMcp\")", source);
            Assert.Contains("WithHttpHealthCheck(\"/health\")", source);
            Assert.DoesNotContain("ArchonUi", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AddProject<Projects.ArchonUi>", source, StringComparison.Ordinal);
        }

        /// <summary>
        /// Confirms the solution does not contain a Discovery UI project during WP001.
        /// </summary>
        [Fact]
        public void SolutionDoesNotContainDiscoveryUiProject()
        {
            // The AppHost must not compose UI, and the solution should not contain UI project files for WP001 either.
            string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);

            Assert.False(Directory.Exists(Path.Combine(repositoryRoot, "src", "ArchonUi")));
            Assert.False(Directory.Exists(Path.Combine(repositoryRoot, "test", "ArchonUi.Tests")));
        }

        /// <summary>
        /// Finds the repository root by walking upward until the root solution file is present.
        /// </summary>
        /// <param name="startDirectory">The directory where the upward search should begin.</param>
        /// <returns>The absolute repository root path containing `Archon.slnx`.</returns>
        private static string FindRepositoryRoot(string startDirectory)
        {
            // Walking upward from the test output directory keeps the test independent from a developer's absolute clone path.
            DirectoryInfo? directory = new(startDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Archon.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Unable to locate the repository root containing Archon.slnx.");
        }
    }
}
