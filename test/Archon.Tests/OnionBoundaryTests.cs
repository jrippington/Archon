using Xunit;

namespace Archon.Tests
{
    /// <summary>
    /// Verifies WP001 project references obey Onion Architecture dependency direction.
    /// </summary>
    /// <remarks>
    /// Onion Architecture keeps domain concepts at the center and delivery/infrastructure details at the outside. These tests
    /// enforce that inward projects do not depend on outward projects, while host projects remain the only composition boundary.
    /// </remarks>
    public sealed class OnionBoundaryTests
    {
        /// <summary>
        /// Confirms domain projects have no outward project references.
        /// </summary>
        [Fact]
        public void DomainProjectsDoNotReferenceOuterLayers()
        {
            // Domain is the innermost layer, so any project reference would make it aware of outer implementation details.
            ProjectCatalog catalog = ProjectCatalog.Create();
            ProjectDescriptor domain = catalog.GetProjectByName("Archon.Domain");

            Assert.Empty(domain.References);
        }

        /// <summary>
        /// Confirms application projects reference domain only and do not reference infrastructure or hosts.
        /// </summary>
        [Fact]
        public void ApplicationProjectsReferenceOnlyAllowedInwardLayers()
        {
            // Application may orchestrate domain concepts but must not depend on infrastructure adapters or delivery hosts.
            ProjectCatalog catalog = ProjectCatalog.Create();
            ProjectDescriptor application = catalog.GetProjectByName("Archon.Application");

            AssertReferencesAllowed(catalog, application, ProjectLayer.Domain);
        }

        /// <summary>
        /// Confirms API module projects never reference host projects.
        /// </summary>
        [Fact]
        public void ApiModulesDoNotReferenceHosts()
        {
            // API modules can be composed by hosts, but they must not know about the hosts that deliver them.
            ProjectCatalog catalog = ProjectCatalog.Create();
            ProjectDescriptor[] modules = catalog.Projects.Where(project => project.Layer == ProjectLayer.ApiModule).ToArray();

            Assert.All(modules, module => AssertNoReferencesToLayers(catalog, module, ProjectLayer.Host));
        }

        /// <summary>
        /// Confirms infrastructure projects do not reference host projects.
        /// </summary>
        [Fact]
        public void InfrastructureProjectsDoNotReferenceHosts()
        {
            // Infrastructure is an outer adapter layer, but host processes still remain outside it as delivery/composition concerns.
            ProjectCatalog catalog = ProjectCatalog.Create();
            ProjectDescriptor[] infrastructureProjects = catalog.Projects.Where(project => project.Layer == ProjectLayer.Infrastructure).ToArray();

            Assert.All(infrastructureProjects, project => AssertNoReferencesToLayers(catalog, project, ProjectLayer.Host));
        }

        /// <summary>
        /// Confirms host projects are not referenced by non-test production projects.
        /// </summary>
        [Fact]
        public void HostProjectsRemainCompositionEndpointsOnly()
        {
            // Hosts may reference inward services for composition, but inward production projects must not depend on host assemblies.
            ProjectCatalog catalog = ProjectCatalog.Create();
            ProjectDescriptor[] productionNonHostProjects = catalog.Projects
                .Where(project => project.Layer != ProjectLayer.Test && project.Layer != ProjectLayer.Host)
                .ToArray();

            Assert.All(productionNonHostProjects, project => AssertNoReferencesToLayers(catalog, project, ProjectLayer.Host));
        }

        /// <summary>
        /// Confirms the Aspire AppHost's API and MCP references are intentionally allowed composition references.
        /// </summary>
        [Fact]
        public void AppHostReferencesApiAndMcpAsAllowedCompositionReferences()
        {
            // Work Item 3 intentionally allows the AppHost to reference delivery hosts so Aspire can compose them as project resources.
            ProjectCatalog catalog = ProjectCatalog.Create();
            ProjectDescriptor appHost = catalog.GetProjectByName("Archon");
            string[] targets = appHost.References.Select(reference => reference.TargetIdentity).ToArray();

            Assert.Contains("src/ArchonApi/ArchonApi.csproj", targets);
            Assert.Contains("src/ArchonMcp/ArchonMcp.csproj", targets);
            Assert.Equal(2, targets.Length);
        }

        /// <summary>
        /// Confirms no production project references a test project.
        /// </summary>
        [Fact]
        public void ProductionProjectsDoNotReferenceTestProjects()
        {
            // Test code should validate production code without becoming part of production dependency graphs.
            ProjectCatalog catalog = ProjectCatalog.Create();
            ProjectDescriptor[] productionProjects = catalog.Projects.Where(project => project.Layer != ProjectLayer.Test).ToArray();

            Assert.All(productionProjects, project => AssertNoReferencesToLayers(catalog, project, ProjectLayer.Test));
        }

        /// <summary>
        /// Asserts that a project references only the supplied allowed target layers.
        /// </summary>
        /// <param name="catalog">The catalog used to resolve reference identities to target projects.</param>
        /// <param name="project">The project whose references are being checked.</param>
        /// <param name="allowedLayers">The target layers allowed for the supplied project.</param>
        private static void AssertReferencesAllowed(ProjectCatalog catalog, ProjectDescriptor project, params ProjectLayer[] allowedLayers)
        {
            // Resolve references through the catalog so failure messages can report both project identity and logical layer.
            HashSet<ProjectLayer> allowed = allowedLayers.ToHashSet();

            foreach (ProjectReferenceDescriptor reference in project.References)
            {
                ProjectDescriptor target = catalog.ProjectsByIdentity[reference.TargetIdentity];
                Assert.True(
                    allowed.Contains(target.Layer),
                    $"Project '{project.Identity}' ({project.Layer}) references '{target.Identity}' ({target.Layer}), but only [{string.Join(", ", allowed)}] are allowed.");
            }
        }

        /// <summary>
        /// Asserts that a project does not reference any project in forbidden target layers.
        /// </summary>
        /// <param name="catalog">The catalog used to resolve reference identities to target projects.</param>
        /// <param name="project">The project whose references are being checked.</param>
        /// <param name="forbiddenLayers">The target layers forbidden for the supplied project.</param>
        private static void AssertNoReferencesToLayers(ProjectCatalog catalog, ProjectDescriptor project, params ProjectLayer[] forbiddenLayers)
        {
            // The explicit failure message names the offending reference so future contributors can fix the exact project edge.
            HashSet<ProjectLayer> forbidden = forbiddenLayers.ToHashSet();

            foreach (ProjectReferenceDescriptor reference in project.References)
            {
                ProjectDescriptor target = catalog.ProjectsByIdentity[reference.TargetIdentity];
                Assert.False(
                    forbidden.Contains(target.Layer),
                    $"Project '{project.Identity}' ({project.Layer}) must not reference '{target.Identity}' ({target.Layer}).");
            }
        }
    }
}
