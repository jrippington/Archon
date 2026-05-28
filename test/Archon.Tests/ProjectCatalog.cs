using System.Xml.Linq;

namespace Archon.Tests
{
    /// <summary>
    /// Discovers Archon projects and exposes normalized metadata for architecture boundary tests.
    /// </summary>
    /// <remarks>
    /// The catalog intentionally normalizes all project identities relative to the repository root so test results are stable
    /// across developer machines, clone directories, and CI workspaces.
    /// </remarks>
    internal sealed class ProjectCatalog
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectCatalog"/> class.
        /// </summary>
        /// <param name="repositoryRoot">The absolute repository root that contains `Archon.slnx`.</param>
        /// <param name="projects">The discovered projects keyed by deterministic identity.</param>
        private ProjectCatalog(string repositoryRoot, IReadOnlyDictionary<string, ProjectDescriptor> projects)
        {
            // Store immutable catalog state so all tests reason over one consistent project graph snapshot.
            RepositoryRoot = repositoryRoot;
            ProjectsByIdentity = projects;
        }

        /// <summary>
        /// Gets the absolute repository root path used to normalize project identities.
        /// </summary>
        public string RepositoryRoot { get; }

        /// <summary>
        /// Gets discovered projects keyed by repository-root-relative identity.
        /// </summary>
        public IReadOnlyDictionary<string, ProjectDescriptor> ProjectsByIdentity { get; }

        /// <summary>
        /// Gets discovered projects in deterministic identity order.
        /// </summary>
        public IReadOnlyList<ProjectDescriptor> Projects => ProjectsByIdentity.Values.OrderBy(project => project.Identity, StringComparer.Ordinal).ToArray();

        /// <summary>
        /// Creates a project catalog by locating the repository root from the current test output path.
        /// </summary>
        /// <returns>A catalog containing all production and test projects under `src` and `test`.</returns>
        public static ProjectCatalog Create()
        {
            // Tests run from a build output directory, so repository-root discovery walks upward until the solution file appears.
            string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
            string[] projectPaths = Directory.EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(Path.Combine(repositoryRoot, "test"), "*.csproj", SearchOption.AllDirectories))
                .OrderBy(path => NormalizeRelativePath(repositoryRoot, path), StringComparer.Ordinal)
                .ToArray();

            Dictionary<string, ProjectDescriptor> projects = new(StringComparer.Ordinal);

            foreach (string projectPath in projectPaths)
            {
                string identity = NormalizeRelativePath(repositoryRoot, projectPath);
                string name = Path.GetFileNameWithoutExtension(projectPath);
                ProjectLayer layer = ClassifyProject(name, identity);
                IReadOnlyList<ProjectReferenceDescriptor> references = ReadProjectReferences(repositoryRoot, projectPath);
                projects.Add(identity, new ProjectDescriptor(name, identity, projectPath, layer, references));
            }

            return new ProjectCatalog(repositoryRoot, projects);
        }

        /// <summary>
        /// Gets a project by its production or test project name.
        /// </summary>
        /// <param name="name">The project name without `.csproj` extension.</param>
        /// <returns>The matching project descriptor.</returns>
        public ProjectDescriptor GetProjectByName(string name)
        {
            // Name lookup keeps tests expressive while identity remains the stable machine-independent key.
            return Projects.Single(project => string.Equals(project.Name, name, StringComparison.Ordinal));
        }

        /// <summary>
        /// Normalizes an absolute path to a repository-root-relative identity using forward slashes.
        /// </summary>
        /// <param name="repositoryRoot">The absolute repository root used as the identity base.</param>
        /// <param name="path">The absolute path to normalize.</param>
        /// <returns>A repository-root-relative path with forward slashes.</returns>
        public static string NormalizeRelativePath(string repositoryRoot, string path)
        {
            // Forward slashes make identity comparisons independent from Windows or Unix path separators.
            string relativePath = Path.GetRelativePath(repositoryRoot, path);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Finds the repository root by walking upward until `Archon.slnx` is found.
        /// </summary>
        /// <param name="startDirectory">The directory where the upward search should begin.</param>
        /// <returns>The absolute repository root path.</returns>
        private static string FindRepositoryRoot(string startDirectory)
        {
            // The root solution file is the stable sentinel for this repository's working tree.
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

        /// <summary>
        /// Reads and normalizes project references from a project file.
        /// </summary>
        /// <param name="repositoryRoot">The absolute repository root used to normalize target identities.</param>
        /// <param name="projectPath">The absolute path to the project file being inspected.</param>
        /// <returns>The normalized project references declared by the project file.</returns>
        private static IReadOnlyList<ProjectReferenceDescriptor> ReadProjectReferences(string repositoryRoot, string projectPath)
        {
            // Project references are XML metadata, so parsing the project file directly avoids requiring a build or design-time load.
            XDocument project = XDocument.Load(projectPath);
            string projectDirectory = Path.GetDirectoryName(projectPath) ?? repositoryRoot;

            return project.Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => CreateReferenceDescriptor(repositoryRoot, projectDirectory, include!))
                .OrderBy(reference => reference.TargetIdentity, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates normalized reference metadata from a project-reference include value.
        /// </summary>
        /// <param name="repositoryRoot">The absolute repository root used to normalize target identities.</param>
        /// <param name="projectDirectory">The directory containing the referencing project file.</param>
        /// <param name="include">The raw project-reference include path.</param>
        /// <returns>A normalized project reference descriptor.</returns>
        private static ProjectReferenceDescriptor CreateReferenceDescriptor(string repositoryRoot, string projectDirectory, string include)
        {
            // Include paths are relative to the referencing project file, so combine and normalize before deriving identity.
            string absoluteTarget = Path.GetFullPath(Path.Combine(projectDirectory, include));
            string targetIdentity = NormalizeRelativePath(repositoryRoot, absoluteTarget);
            string normalizedInclude = include.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

            return new ProjectReferenceDescriptor(normalizedInclude, targetIdentity);
        }

        /// <summary>
        /// Assigns an architectural layer to a project based on its name and path.
        /// </summary>
        /// <param name="name">The project name without `.csproj` extension.</param>
        /// <param name="identity">The normalized repository-root-relative project identity.</param>
        /// <returns>The layer used by boundary tests.</returns>
        private static ProjectLayer ClassifyProject(string name, string identity)
        {
            // Test projects are excluded from production Onion dependency rules because they validate outward-facing behavior.
            if (identity.StartsWith("test/", StringComparison.Ordinal))
            {
                return ProjectLayer.Test;
            }

            return name switch
            {
                "Archon" or "ArchonApi" or "ArchonMcp" => ProjectLayer.Host,
                "Archon.ServiceDefaults" => ProjectLayer.ServiceDefaults,
                "Archon.Domain" => ProjectLayer.Domain,
                "Archon.Application" => ProjectLayer.Application,
                "Archon.Api.Extraction" or "Archon.Api.Query" or "Archon.Api.Management" => ProjectLayer.ApiModule,
                "Archon.Roslyn" => ProjectLayer.RoslynAbstraction,
                "Archon.Roslyn.CSharp" or "Archon.Roslyn.VisualBasic" or "Archon.Roslyn.Legacy" => ProjectLayer.RoslynImplementation,
                "Archon.Extractors" => ProjectLayer.Extractor,
                _ when name.StartsWith("Archon.Extractors.", StringComparison.Ordinal) => ProjectLayer.Extractor,
                _ when name.StartsWith("Archon.Infrastructure.", StringComparison.Ordinal) => ProjectLayer.Infrastructure,
                _ => throw new InvalidOperationException($"Project '{name}' with identity '{identity}' is not classified for WP001 boundary checks.")
            };
        }
    }
}
