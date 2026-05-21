using System.Xml.Linq;
using Archon.Extractors.Projects.Packages;

namespace Archon.Extractors.Projects.Projects
{
    /// <summary>
    /// Extracts supported C# and VB.NET project metadata by reading project XML without executing MSBuild targets or package restore.
    /// </summary>
    internal sealed class ProjectMetadataExtractor
    {
        /// <summary>
        /// Stores the deterministic package-reference extractor used for SDK-style package metadata.
        /// </summary>
        private readonly PackageReferenceExtractor _packageReferenceExtractor;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectMetadataExtractor" /> class.
        /// </summary>
        internal ProjectMetadataExtractor()
        {
            // Package extraction is isolated in its own collaborator so project metadata parsing stays focused on project-level XML fields.
            _packageReferenceExtractor = new PackageReferenceExtractor();
        }

        /// <summary>
        /// Extracts deterministic metadata from one supported project file.
        /// </summary>
        /// <param name="projectPath">The absolute project file path to read.</param>
        /// <param name="repositoryRootDirectory">The absolute repository root used to normalize project-reference targets.</param>
        /// <param name="relativeProjectPath">The repository-relative project path used for graph identity.</param>
        /// <param name="projectName">The project display name declared by the submitted solution.</param>
        /// <param name="language">The source language inferred from the supported project declaration.</param>
        /// <param name="cancellationToken">The cancellation token that stops project file reading before or during asynchronous I/O.</param>
        /// <returns>Project metadata extracted from XML properties and deterministic defaults.</returns>
        internal async Task<ProjectMetadata> ExtractAsync(string projectPath, string repositoryRootDirectory, string relativeProjectPath, string projectName, ProjectLanguage language, CancellationToken cancellationToken)
        {
            // The extractor reads project files as data. It never creates MSBuildWorkspace, invokes targets, restores packages, or runs repository scripts.
            ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRootDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(relativeProjectPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
            cancellationToken.ThrowIfCancellationRequested();

            string projectXml = await File.ReadAllTextAsync(projectPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            XDocument document = XDocument.Parse(projectXml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            XElement root = document.Root ?? throw new InvalidDataException("The submitted project file does not contain an XML root element.");
            string? sdk = GetOptionalAttribute(root, "Sdk");
            bool isSdkStyle = !string.IsNullOrWhiteSpace(sdk);
            string? targetFramework = GetFirstPropertyValue(document, "TargetFramework");
            IReadOnlyList<string> targetFrameworks = SplitTargetFrameworks(GetFirstPropertyValue(document, "TargetFrameworks"));
            string? legacyTargetFramework = GetFirstPropertyValue(document, "TargetFrameworkVersion");
            string? outputType = GetFirstPropertyValue(document, "OutputType");
            string? assemblyName = GetFirstPropertyValue(document, "AssemblyName");
            string? rootNamespace = GetFirstPropertyValue(document, "RootNamespace");
            string? nullable = GetFirstPropertyValue(document, "Nullable");
            string? implicitUsings = GetFirstPropertyValue(document, "ImplicitUsings");
            IReadOnlyList<ProjectReferenceDeclaration> projectReferences = GetProjectReferences(document, projectPath, repositoryRootDirectory, relativeProjectPath);
            IReadOnlyList<PackageReferenceDeclaration> packageReferences = await _packageReferenceExtractor.ExtractAsync(document, projectPath, repositoryRootDirectory, relativeProjectPath, cancellationToken).ConfigureAwait(false);
            int lineCount = CountLines(projectXml);

            return new ProjectMetadata(
                projectName,
                relativeProjectPath,
                language,
                targetFramework,
                targetFrameworks,
                legacyTargetFramework,
                outputType,
                string.IsNullOrWhiteSpace(assemblyName) ? Path.GetFileNameWithoutExtension(projectPath) : assemblyName.Trim(),
                rootNamespace,
                sdk,
                isSdkStyle,
                !isSdkStyle,
                nullable,
                implicitUsings,
                projectReferences,
                packageReferences,
                lineCount);
        }

        /// <summary>
        /// Extracts `ProjectReference` declarations from the parsed project XML.
        /// </summary>
        /// <param name="document">The parsed project XML document.</param>
        /// <param name="projectPath">The absolute path of the project file being inspected.</param>
        /// <param name="repositoryRootDirectory">The absolute repository root used for containment checks.</param>
        /// <param name="relativeProjectPath">The repository-relative path of the declaring project.</param>
        /// <returns>Ordered project-reference declarations with raw include text, normalized repository-relative target paths, and evidence line numbers.</returns>
        private static IReadOnlyList<ProjectReferenceDeclaration> GetProjectReferences(XDocument document, string projectPath, string repositoryRootDirectory, string relativeProjectPath)
        {
            // ProjectReference items are MSBuild item declarations; local-name matching supports old-style XML namespaces and SDK-style XML.
            List<ProjectReferenceDeclaration> references = [];
            string projectDirectory = Path.GetDirectoryName(projectPath) ?? repositoryRootDirectory;

            foreach (XElement projectReferenceElement in document.Descendants().Where(element => string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.Ordinal)))
            {
                string? declaredInclude = GetOptionalAttribute(projectReferenceElement, "Include");

                if (declaredInclude is null)
                {
                    continue;
                }

                string platformPath = declaredInclude.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                string absoluteReferencedPath = Path.GetFullPath(Path.Combine(projectDirectory, platformPath));
                bool isRepositoryContained = IsPathContainedByDirectory(repositoryRootDirectory, absoluteReferencedPath);
                string? resolvedRelativePath = isRepositoryContained ? GetRepositoryRelativePath(repositoryRootDirectory, absoluteReferencedPath) : null;
                int? lineNumber = projectReferenceElement is System.Xml.IXmlLineInfo lineInfo && lineInfo.HasLineInfo() ? lineInfo.LineNumber : null;

                references.Add(new ProjectReferenceDeclaration(
                    relativeProjectPath,
                    declaredInclude,
                    resolvedRelativePath,
                    isRepositoryContained,
                    lineNumber));
            }

            return references;
        }

        /// <summary>
        /// Reads an optional root-element attribute after trimming insignificant whitespace.
        /// </summary>
        /// <param name="element">The XML element that may contain the attribute.</param>
        /// <param name="attributeName">The local attribute name to read.</param>
        /// <returns>The trimmed attribute value, or <see langword="null" /> when the attribute is missing or blank.</returns>
        private static string? GetOptionalAttribute(XElement element, string attributeName)
        {
            // Attribute matching is local-name based so XML namespace declarations do not affect SDK-style detection.
            string? value = element.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, attributeName, StringComparison.Ordinal))?.Value;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Gets the first matching MSBuild property value from a project document.
        /// </summary>
        /// <param name="document">The parsed project XML document.</param>
        /// <param name="propertyName">The MSBuild property local name to find.</param>
        /// <returns>The trimmed property value, or <see langword="null" /> when no non-empty property is declared.</returns>
        private static string? GetFirstPropertyValue(XDocument document, string propertyName)
        {
            // Descendant local-name lookup handles both SDK-style XML and old-style MSBuild XML namespaces.
            XElement? element = document.Descendants().FirstOrDefault(candidate => string.Equals(candidate.Name.LocalName, propertyName, StringComparison.Ordinal));
            string? value = element?.Value;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Splits a semicolon-delimited `TargetFrameworks` property into ordered individual target framework values.
        /// </summary>
        /// <param name="targetFrameworks">The raw `TargetFrameworks` property value.</param>
        /// <returns>An ordered read-only list of non-empty target framework monikers.</returns>
        private static IReadOnlyList<string> SplitTargetFrameworks(string? targetFrameworks)
        {
            // MSBuild multi-targeting uses semicolon separators; preserving order keeps the project declaration faithful to source.
            if (string.IsNullOrWhiteSpace(targetFrameworks))
            {
                return [];
            }

            return targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        /// <summary>
        /// Counts logical lines in project XML for evidence span fallback.
        /// </summary>
        /// <param name="content">The project file content that was read from disk.</param>
        /// <returns>The number of text lines, with a minimum of one for non-empty project XML.</returns>
        private static int CountLines(string content)
        {
            // Splitting on line-feed is sufficient for evidence spans because CRLF still includes exactly one LF per line break.
            if (content.Length == 0)
            {
                return 1;
            }

            return content.Count(character => character == '\n') + 1;
        }

        /// <summary>
        /// Builds a repository-relative path using forward slash separators.
        /// </summary>
        /// <param name="repositoryRootDirectory">The absolute repository root directory.</param>
        /// <param name="filePath">The absolute file path to normalize.</param>
        /// <returns>A repository-relative path suitable for graph identity.</returns>
        private static string GetRepositoryRelativePath(string repositoryRootDirectory, string filePath)
        {
            // Project-reference identities use the same path normalization as project node identities.
            string relativePath = Path.GetRelativePath(repositoryRootDirectory, filePath);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Determines whether a candidate path is contained by the submitted repository root.
        /// </summary>
        /// <param name="repositoryRootDirectory">The absolute repository root directory.</param>
        /// <param name="candidatePath">The absolute candidate path to check.</param>
        /// <returns><see langword="true" /> when the candidate path is inside the repository root; otherwise, <see langword="false" />.</returns>
        private static bool IsPathContainedByDirectory(string repositoryRootDirectory, string candidatePath)
        {
            // The relative-path check avoids accepting sibling paths that merely share a string prefix with the repository path.
            string relativePath = Path.GetRelativePath(repositoryRootDirectory, candidatePath);
            return !relativePath.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativePath);
        }
    }
}
