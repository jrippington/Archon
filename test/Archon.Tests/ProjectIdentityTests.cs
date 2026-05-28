using Xunit;

namespace Archon.Tests
{
    /// <summary>
    /// Verifies stable project identity and expected current project presence.
    /// </summary>
    /// <remarks>
    /// Stable identity is important because Archon later analyzes projects across different developer machines and CI agents.
    /// Repository-root-relative paths keep those identities deterministic even when absolute clone paths differ.
    /// </remarks>
    public sealed class ProjectIdentityTests
    {
        /// <summary>
        /// Lists the production projects that should remain discoverable through repository-relative identities.
        /// </summary>
        private static readonly string[] ExpectedProductionProjects =
        {
            "Archon",
            "Archon.ServiceDefaults",
            "ArchonApi",
            "ArchonMcp",
            "Archon.Domain",
            "Archon.Application",
            "Archon.Api.Extraction",
            "Archon.Api.Query",
            "Archon.Api.Management",
            "Archon.Roslyn",
            "Archon.Roslyn.CSharp",
            "Archon.Roslyn.VisualBasic",
            "Archon.Roslyn.Legacy",
            "Archon.Extractors",
            "Archon.Infrastructure.Roslyn",
            "Archon.Infrastructure.Neo4j",
            "Archon.Infrastructure.Markdown"
        };

        /// <summary>
        /// Confirms all required production and test projects exist with deterministic normalized identities.
        /// </summary>
        [Fact]
        public void ExpectedProjectsHaveRepositoryRelativeIdentities()
        {
            // The catalog discovers actual files so the test fails when a future change removes, renames, or re-splits required projects.
            ProjectCatalog catalog = ProjectCatalog.Create();

            foreach (string projectName in ExpectedProductionProjects)
            {
                ProjectDescriptor productionProject = catalog.GetProjectByName(projectName);
                ProjectDescriptor testProject = catalog.GetProjectByName($"{projectName}.Tests");

                Assert.Equal($"src/{projectName}/{projectName}.csproj", productionProject.Identity);
                Assert.Equal($"test/{projectName}.Tests/{projectName}.Tests.csproj", testProject.Identity);
            }
        }

        /// <summary>
        /// Confirms normalized identities never include machine-specific absolute path fragments or Windows separators.
        /// </summary>
        [Fact]
        public void ProjectIdentitiesAreMachineIndependent()
        {
            // Deterministic identities must be portable because architecture analysis should not depend on clone location.
            ProjectCatalog catalog = ProjectCatalog.Create();

            foreach (ProjectDescriptor project in catalog.Projects)
            {
                Assert.DoesNotContain(catalog.RepositoryRoot, project.Identity, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain('\\', project.Identity);
                Assert.EndsWith(".csproj", project.Identity, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Confirms WP001 deliberately excludes Discovery UI projects from production and test identities.
        /// </summary>
        [Fact]
        public void DiscoveryUiProjectsAreAbsent()
        {
            // UI delivery is intentionally outside WP001, so the absence check protects the foundation from accidental UI creation.
            ProjectCatalog catalog = ProjectCatalog.Create();
            string[] projectNames = catalog.Projects.Select(project => project.Name).ToArray();

            Assert.DoesNotContain("ArchonUi", projectNames);
            Assert.DoesNotContain("ArchonUi.Tests", projectNames);
            Assert.False(Directory.Exists(Path.Combine(catalog.RepositoryRoot, "src", "ArchonUi")));
            Assert.False(Directory.Exists(Path.Combine(catalog.RepositoryRoot, "test", "ArchonUi.Tests")));
        }
    }
}
