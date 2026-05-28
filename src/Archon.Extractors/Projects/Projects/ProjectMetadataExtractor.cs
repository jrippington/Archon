using System.Xml.Linq;
using Archon.Extractors.Projects.Classification;
using Archon.Extractors.Projects.Evidence;
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
        /// Stores the deterministic legacy packages.config extractor used for old-style package metadata.
        /// </summary>
        private readonly LegacyPackageConfigExtractor _legacyPackageConfigExtractor;

        /// <summary>
        /// Stores the deterministic application type classifier used for project node metadata.
        /// </summary>
        private readonly ApplicationTypeClassifier _applicationTypeClassifier;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectMetadataExtractor" /> class.
        /// </summary>
        internal ProjectMetadataExtractor()
        {
            // Package extraction is isolated in its own collaborator so project metadata parsing stays focused on project-level XML fields.
            _packageReferenceExtractor = new PackageReferenceExtractor();
            _legacyPackageConfigExtractor = new LegacyPackageConfigExtractor();
            _applicationTypeClassifier = new ApplicationTypeClassifier();
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
            IReadOnlyList<AnalyzerReferenceDeclaration> analyzerReferences = GetAnalyzerReferences(document, projectPath, repositoryRootDirectory, relativeProjectPath);
            List<PackageReferenceDeclaration> packageReferences = [.. await _packageReferenceExtractor.ExtractAsync(document, projectPath, repositoryRootDirectory, relativeProjectPath, cancellationToken).ConfigureAwait(false)];
            List<PackageExtractionDiagnostic> packageDiagnostics = [];
            List<ProjectArtifactDeclaration> artifacts = [new(relativeProjectPath, "ProjectFile", relativeProjectPath)];
            AddBuildArtifacts(artifacts, projectPath, repositoryRootDirectory, relativeProjectPath);
            AddPackageArtifacts(artifacts, packageReferences, packageDiagnostics, relativeProjectPath);
            AddAnalyzerArtifacts(artifacts, analyzerReferences);

            if (!isSdkStyle)
            {
                LegacyPackageConfigExtractionResult legacyPackageResult = await _legacyPackageConfigExtractor.ExtractAsync(projectPath, repositoryRootDirectory, relativeProjectPath, cancellationToken).ConfigureAwait(false);
                packageReferences.AddRange(legacyPackageResult.PackageReferences);
                packageDiagnostics.AddRange(legacyPackageResult.Diagnostics);
                AddPackageArtifacts(artifacts, legacyPackageResult.PackageReferences, legacyPackageResult.Diagnostics, relativeProjectPath);
            }

            int lineCount = CountLines(projectXml);
            ApplicationTypeClassification applicationTypeClassification = _applicationTypeClassifier.Classify(document, projectPath, repositoryRootDirectory, relativeProjectPath, projectName, sdk, outputType, isSdkStyle, packageReferences);

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
                packageDiagnostics,
                analyzerReferences,
                DeduplicateArtifacts(artifacts),
                applicationTypeClassification,
                lineCount);
        }

        /// <summary>
        /// Extracts `Analyzer` declarations from parsed project XML.
        /// </summary>
        /// <param name="document">The parsed project XML document.</param>
        /// <param name="projectPath">The absolute path of the project file being inspected.</param>
        /// <param name="repositoryRootDirectory">The absolute repository root used for containment checks.</param>
        /// <param name="relativeProjectPath">The repository-relative path of the declaring project.</param>
        /// <returns>Ordered analyzer declarations with raw include text, resolved repository-relative paths, and evidence snippet details.</returns>
        private static IReadOnlyList<AnalyzerReferenceDeclaration> GetAnalyzerReferences(XDocument document, string projectPath, string repositoryRootDirectory, string relativeProjectPath)
        {
            // Analyzer items are static MSBuild declarations and can be read safely without Roslyn workspace loading or target execution.
            List<AnalyzerReferenceDeclaration> references = [];
            string projectDirectory = Path.GetDirectoryName(projectPath) ?? repositoryRootDirectory;

            foreach (XElement analyzerElement in document.Descendants().Where(element => string.Equals(element.Name.LocalName, "Analyzer", StringComparison.Ordinal)))
            {
                string? declaredInclude = GetOptionalAttribute(analyzerElement, "Include");

                if (declaredInclude is null)
                {
                    continue;
                }

                string platformPath = declaredInclude.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                string absoluteAnalyzerPath = Path.GetFullPath(Path.Combine(projectDirectory, platformPath));
                bool isRepositoryContained = IsPathContainedByDirectory(repositoryRootDirectory, absoluteAnalyzerPath);
                string? resolvedRelativePath = isRepositoryContained ? GetRepositoryRelativePath(repositoryRootDirectory, absoluteAnalyzerPath) : null;
                int? lineNumber = XmlEvidence.GetLineNumber(analyzerElement);
                SourceSnippet sourceSnippet = XmlEvidence.CreateSnippet(analyzerElement);

                references.Add(new AnalyzerReferenceDeclaration(
                    relativeProjectPath,
                    declaredInclude,
                    resolvedRelativePath,
                    isRepositoryContained,
                    lineNumber,
                    sourceSnippet.Hash,
                    sourceSnippet.Preview));
            }

            return references;
        }

        /// <summary>
        /// Adds repository-contained build artifacts that may support imported or central package facts.
        /// </summary>
        /// <param name="artifacts">The artifact collection being assembled for the project.</param>
        /// <param name="projectPath">The absolute project path whose hierarchy and imports are inspected.</param>
        /// <param name="repositoryRootDirectory">The absolute repository root used for containment checks.</param>
        /// <param name="relativeProjectPath">The repository-relative project path that introduced the artifacts.</param>
        private static void AddBuildArtifacts(List<ProjectArtifactDeclaration> artifacts, string projectPath, string repositoryRootDirectory, string relativeProjectPath)
        {
            // Build artifacts are represented only when they are local, repository-contained files relevant to deterministic XML extraction.
            string? projectDirectory = Path.GetDirectoryName(projectPath);

            if (projectDirectory is null)
            {
                return;
            }

            foreach (string fileName in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
            {
                string currentDirectory = Path.GetFullPath(projectDirectory);

                while (IsPathContainedByDirectory(repositoryRootDirectory, currentDirectory))
                {
                    string candidatePath = Path.Combine(currentDirectory, fileName);

                    if (File.Exists(candidatePath))
                    {
                        artifacts.Add(new ProjectArtifactDeclaration(GetRepositoryRelativePath(repositoryRootDirectory, candidatePath), fileName, relativeProjectPath));
                    }

                    if (string.Equals(Path.TrimEndingDirectorySeparator(currentDirectory), Path.TrimEndingDirectorySeparator(repositoryRootDirectory), StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    string? parentDirectory = Directory.GetParent(currentDirectory)?.FullName;

                    if (parentDirectory is null || string.Equals(parentDirectory, currentDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    currentDirectory = parentDirectory;
                }
            }
        }

        /// <summary>
        /// Adds artifacts used by package declarations and package diagnostics.
        /// </summary>
        /// <param name="artifacts">The artifact collection being assembled for the project.</param>
        /// <param name="packageReferences">The package references whose evidence files should be represented.</param>
        /// <param name="packageDiagnostics">The package diagnostics whose evidence files should be represented.</param>
        /// <param name="relativeProjectPath">The repository-relative project path that introduced the package artifacts.</param>
        private static void AddPackageArtifacts(List<ProjectArtifactDeclaration> artifacts, IEnumerable<PackageReferenceDeclaration> packageReferences, IEnumerable<PackageExtractionDiagnostic> packageDiagnostics, string relativeProjectPath)
        {
            // Package artifacts include direct project files, imported props/targets, central package files, and packages.config files that support package facts or diagnostics.
            foreach (PackageReferenceDeclaration packageReference in packageReferences)
            {
                string kind = string.Equals(packageReference.SourceType, "packages.config", StringComparison.Ordinal) ? "PackagesConfig" : "PackageReferenceSource";
                artifacts.Add(new ProjectArtifactDeclaration(packageReference.EvidenceRelativePath, kind, relativeProjectPath));
            }

            foreach (PackageExtractionDiagnostic diagnostic in packageDiagnostics)
            {
                artifacts.Add(new ProjectArtifactDeclaration(diagnostic.EvidenceRelativePath, "PackageDiagnosticSource", relativeProjectPath));
            }
        }

        /// <summary>
        /// Adds repository-contained analyzer files that can be represented as FilePath artifacts.
        /// </summary>
        /// <param name="artifacts">The artifact collection being assembled for the project.</param>
        /// <param name="analyzerReferences">The analyzer declarations discovered in project XML.</param>
        private static void AddAnalyzerArtifacts(List<ProjectArtifactDeclaration> artifacts, IEnumerable<AnalyzerReferenceDeclaration> analyzerReferences)
        {
            // Analyzer file artifacts are added only when the include path resolved inside the submitted repository boundary.
            foreach (AnalyzerReferenceDeclaration analyzerReference in analyzerReferences)
            {
                if (!string.IsNullOrWhiteSpace(analyzerReference.ResolvedRelativePath))
                {
                    artifacts.Add(new ProjectArtifactDeclaration(analyzerReference.ResolvedRelativePath, "AnalyzerFile", analyzerReference.DeclaringProjectRelativePath));
                }
            }
        }

        /// <summary>
        /// Deduplicates artifact declarations by repository-relative path while preserving deterministic order.
        /// </summary>
        /// <param name="artifacts">The collected artifact declarations.</param>
        /// <returns>A deterministic artifact list containing one entry per relative path.</returns>
        private static IReadOnlyList<ProjectArtifactDeclaration> DeduplicateArtifacts(IEnumerable<ProjectArtifactDeclaration> artifacts)
        {
            // Artifact paths are graph identities, so repeated evidence from the same file collapses to one FilePath node.
            return artifacts
                .GroupBy(artifact => artifact.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(artifact => artifact.ArtifactKind, StringComparer.Ordinal).First())
                .OrderBy(artifact => artifact.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
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
