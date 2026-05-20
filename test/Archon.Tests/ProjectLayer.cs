namespace Archon.Tests
{
    /// <summary>
    /// Describes the architectural layer assigned to a project for WP001 boundary verification.
    /// </summary>
    /// <remarks>
    /// The enum keeps test rules readable by naming the project responsibility instead of comparing raw project-name prefixes throughout the boundary tests.
    /// </remarks>
    internal enum ProjectLayer
    {
        /// <summary>
        /// Represents executable host and composition projects such as the Aspire AppHost, API host, and MCP host.
        /// </summary>
        Host,

        /// <summary>
        /// Represents shared host runtime configuration used by executable projects.
        /// </summary>
        ServiceDefaults,

        /// <summary>
        /// Represents the innermost domain model and rules layer.
        /// </summary>
        Domain,

        /// <summary>
        /// Represents the application orchestration and use-case layer.
        /// </summary>
        Application,

        /// <summary>
        /// Represents API module projects that are composed by delivery hosts.
        /// </summary>
        ApiModule,

        /// <summary>
        /// Represents Roslyn abstraction projects shared by extractor and infrastructure projects.
        /// </summary>
        RoslynAbstraction,

        /// <summary>
        /// Represents Roslyn implementation projects for specific language or legacy analysis slices.
        /// </summary>
        RoslynImplementation,

        /// <summary>
        /// Represents extractor projects that will later translate source evidence into architecture facts.
        /// </summary>
        Extractor,

        /// <summary>
        /// Represents outer adapter projects for Roslyn, Neo4j, markdown, and other infrastructure concerns.
        /// </summary>
        Infrastructure,

        /// <summary>
        /// Represents test projects that validate production projects without participating in production dependency rules.
        /// </summary>
        Test
    }
}
