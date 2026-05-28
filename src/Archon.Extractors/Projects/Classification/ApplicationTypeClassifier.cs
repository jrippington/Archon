using System.Xml.Linq;
using Archon.Extractors.Projects.Packages;

namespace Archon.Extractors.Projects.Classification
{
    /// <summary>
    /// Classifies supported .NET project files into deterministic high-level application categories for project graph metadata.
    /// </summary>
    internal sealed class ApplicationTypeClassifier
    {
        /// <summary>
        /// Stores the maximum number of characters read from an individual source or configuration artifact for safe indicator checks.
        /// </summary>
        private const int ArtifactPreviewCharacterLimit = 64 * 1024;

        /// <summary>
        /// Classifies one project using static project XML, extracted package references, and safe repository-contained artifact indicators.
        /// </summary>
        /// <param name="document">The parsed project XML document.</param>
        /// <param name="projectPath">The absolute project file path being classified.</param>
        /// <param name="repositoryRootDirectory">The absolute repository root that bounds safe artifact inspection.</param>
        /// <param name="relativeProjectPath">The repository-relative project path used for deterministic evidence descriptions.</param>
        /// <param name="projectName">The project display name used only for low-confidence naming heuristics.</param>
        /// <param name="sdk">The SDK value declared on the project root element, when present.</param>
        /// <param name="outputType">The project output type value, when present.</param>
        /// <param name="isSdkStyle">A value indicating whether the project uses SDK-style root metadata.</param>
        /// <param name="packageReferences">The package references extracted from the project and safe imported build files.</param>
        /// <returns>A deterministic application type classification with confidence, evidence, and Unknown details.</returns>
        internal ApplicationTypeClassification Classify(XDocument document, string projectPath, string repositoryRootDirectory, string relativeProjectPath, string projectName, string? sdk, string? outputType, bool isSdkStyle, IReadOnlyList<PackageReferenceDeclaration> packageReferences)
        {
            // The classifier runs after project/package extraction so it can use direct project XML first, then package indicators, then bounded artifact inspection, and finally weak naming heuristics.
            ArgumentNullException.ThrowIfNull(document);
            ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRootDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(relativeProjectPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
            ArgumentNullException.ThrowIfNull(packageReferences);

            IReadOnlyList<ClassificationIndicator> highConfidenceIndicators = GetHighConfidenceIndicators(document, sdk, outputType, isSdkStyle, packageReferences);
            ApplicationTypeClassification? highConfidenceClassification = ResolveHighConfidence(highConfidenceIndicators);

            if (highConfidenceClassification is not null)
            {
                return highConfidenceClassification;
            }

            ClassificationIndicator? mediumConfidenceIndicator = GetMediumConfidenceIndicators(projectPath, repositoryRootDirectory, relativeProjectPath)
                .OrderBy(indicator => indicator.Priority)
                .ThenBy(indicator => indicator.ApplicationType, StringComparer.Ordinal)
                .FirstOrDefault();

            if (mediumConfidenceIndicator is not null)
            {
                return CreateKnown(mediumConfidenceIndicator.ApplicationType, "Medium", 0.50m, [mediumConfidenceIndicator.Evidence]);
            }

            ClassificationIndicator? lowConfidenceIndicator = GetLowConfidenceIndicator(relativeProjectPath, projectName);

            if (lowConfidenceIndicator is not null)
            {
                return CreateKnown(lowConfidenceIndicator.ApplicationType, "Low", 0.25m, [lowConfidenceIndicator.Evidence]);
            }

            return ApplicationTypeClassification.Unknown("No supported application type indicators were found.");
        }

        /// <summary>
        /// Resolves direct high-confidence indicators, including contradiction handling.
        /// </summary>
        /// <param name="indicators">The high-confidence project metadata and package indicators.</param>
        /// <returns>A known high-confidence classification, Unknown for contradictions, or <see langword="null" /> when no direct indicators exist.</returns>
        private static ApplicationTypeClassification? ResolveHighConfidence(IReadOnlyList<ClassificationIndicator> indicators)
        {
            // High-confidence signals are grouped by category so repeated evidence for the same category strengthens that category without creating a conflict.
            if (indicators.Count == 0)
            {
                return null;
            }

            ClassificationIndicator[] orderedIndicators = indicators
                .OrderBy(indicator => indicator.Priority)
                .ThenBy(indicator => indicator.ApplicationType, StringComparer.Ordinal)
                .ThenBy(indicator => indicator.Evidence, StringComparer.Ordinal)
                .ToArray();
            string[] distinctTypes = orderedIndicators.Select(indicator => indicator.ApplicationType).Distinct(StringComparer.Ordinal).ToArray();

            if (distinctTypes.Length > 1)
            {
                return ApplicationTypeClassification.Contradictory(orderedIndicators.Select(indicator => string.Concat(indicator.ApplicationType, ": ", indicator.Evidence)).ToArray());
            }

            return CreateKnown(distinctTypes[0], "High", 0.90m, orderedIndicators.Select(indicator => indicator.Evidence).ToArray());
        }

        /// <summary>
        /// Collects direct SDK, project type, output type, and explicit package indicators.
        /// </summary>
        /// <param name="document">The parsed project XML document.</param>
        /// <param name="sdk">The project SDK value, when present.</param>
        /// <param name="outputType">The output type value, when present.</param>
        /// <param name="isSdkStyle">A value indicating whether the project is SDK-style.</param>
        /// <param name="packageReferences">The package references already extracted from project XML and safe imports.</param>
        /// <returns>Ordered high-confidence classification indicators.</returns>
        private static IReadOnlyList<ClassificationIndicator> GetHighConfidenceIndicators(XDocument document, string? sdk, string? outputType, bool isSdkStyle, IReadOnlyList<PackageReferenceDeclaration> packageReferences)
        {
            // Direct project metadata and explicit packages are the only high-confidence inputs because they are intentional project declarations.
            List<ClassificationIndicator> indicators = [];

            if (ContainsIgnoreCase(sdk, "Microsoft.NET.Sdk.Worker"))
            {
                indicators.Add(new ClassificationIndicator("WorkerService", "Project SDK declares Microsoft.NET.Sdk.Worker.", 10));
            }
            else if (ContainsIgnoreCase(sdk, "Microsoft.Build.NoTargets") || ContainsIgnoreCase(sdk, "Microsoft.Build.Traversal") || ContainsIgnoreCase(sdk, "Microsoft.NET.Sdk.Razor.SourceGenerators"))
            {
                indicators.Add(new ClassificationIndicator("ToolingProject", "Project SDK declares a build/tooling SDK.", 20));
            }
            else if (ContainsIgnoreCase(sdk, "Microsoft.NET.Sdk.Web"))
            {
                string webType = HasAnyPackage(packageReferences, "swashbuckle.aspnetcore", "microsoft.aspnetcore.openapi") ? "AspNetCoreWebApi" : "AspNetCoreWebApp";
                indicators.Add(new ClassificationIndicator(webType, "Project SDK declares Microsoft.NET.Sdk.Web.", 30));
            }

            if (HasAnyPackage(packageReferences, "microsoft.net.test.sdk", "xunit", "nunit", "mstest.testframework"))
            {
                indicators.Add(new ClassificationIndicator("TestProject", "Project declares a recognized .NET test package.", 40));
            }

            if (HasAnyPackage(packageReferences, "microsoft.extensions.hosting.windowsservices", "microsoft.extensions.hosting.systemd"))
            {
                indicators.Add(new ClassificationIndicator("WorkerService", "Project declares a recognized worker hosting package.", 50));
            }

            if (HasAnyPackage(packageReferences, "microsoft.aspnet.webapi.core", "microsoft.aspnet.webapi.webhost"))
            {
                indicators.Add(new ClassificationIndicator("WebApi2App", "Project declares a recognized ASP.NET Web API 2 package.", 60));
            }

            if (HasAnyPackage(packageReferences, "microsoft.aspnet.mvc"))
            {
                indicators.Add(new ClassificationIndicator("MvcApp", "Project declares a recognized ASP.NET MVC package.", 70));
            }

            if (ContainsProjectTypeGuid(document, "{349c5851-65df-11da-9384-00065b846f21}"))
            {
                indicators.Add(new ClassificationIndicator("ClassicAspNetWebApp", "ProjectTypeGuids declares the classic ASP.NET web application type GUID.", 80));
            }

            if (HasReference(document, "System.Web.Mvc"))
            {
                indicators.Add(new ClassificationIndicator("MvcApp", "Project references System.Web.Mvc.", 90));
            }

            if (HasReference(document, "System.Web.Http") || HasReference(document, "System.Web.Http.WebHost"))
            {
                indicators.Add(new ClassificationIndicator("WebApi2App", "Project references ASP.NET Web API 2 assemblies.", 100));
            }

            if (HasContentIncludeEnding(document, ".aspx") || HasContentIncludeEnding(document, ".ascx") || HasContentIncludeEnding(document, ".master"))
            {
                indicators.Add(new ClassificationIndicator("WebFormsApp", "Project includes Web Forms markup artifacts.", 110));
            }

            if (indicators.Count == 0 && IsExecutableOutput(outputType))
            {
                indicators.Add(new ClassificationIndicator("ConsoleApp", "Project OutputType declares an executable.", 120));
            }

            if (indicators.Count == 0 && isSdkStyle && string.Equals(outputType?.Trim(), "Library", StringComparison.OrdinalIgnoreCase))
            {
                indicators.Add(new ClassificationIndicator("ClassLibrary", "SDK-style project explicitly declares library output semantics.", 130));
            }

            return indicators;
        }

        /// <summary>
        /// Reads safe source and configuration artifacts for strong medium-confidence indicators.
        /// </summary>
        /// <param name="projectPath">The absolute project path whose sibling artifacts may be inspected.</param>
        /// <param name="repositoryRootDirectory">The absolute repository root that bounds artifact traversal.</param>
        /// <param name="relativeProjectPath">The repository-relative project path used in evidence descriptions.</param>
        /// <returns>Medium-confidence indicators discovered from bounded artifact content.</returns>
        private static IReadOnlyList<ClassificationIndicator> GetMediumConfidenceIndicators(string projectPath, string repositoryRootDirectory, string relativeProjectPath)
        {
            // Artifact inspection is intentionally shallow and bounded; it does not parse syntax trees, execute code, or scan outside the project directory.
            string? projectDirectory = Path.GetDirectoryName(projectPath);

            if (projectDirectory is null)
            {
                return [];
            }

            List<ClassificationIndicator> indicators = [];

            foreach (string artifactPath in Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!IsPathContainedByDirectory(repositoryRootDirectory, artifactPath) || !IsSupportedArtifactExtension(artifactPath))
                {
                    continue;
                }

                string relativeArtifactPath = GetRepositoryRelativePath(repositoryRootDirectory, artifactPath);
                string content = ReadBoundedArtifactText(artifactPath);

                if (content.Contains("BackgroundService", StringComparison.Ordinal) || content.Contains("IHostedService", StringComparison.Ordinal))
                {
                    indicators.Add(new ClassificationIndicator("WorkerService", $"Repository-contained source artifact '{relativeArtifactPath}' contains hosted-service indicators for '{relativeProjectPath}'.", 200));
                }

                if (content.Contains("WebApplication.CreateBuilder", StringComparison.Ordinal) || content.Contains("AddControllers", StringComparison.Ordinal) || content.Contains("MapControllers", StringComparison.Ordinal))
                {
                    string applicationType = content.Contains("MapControllers", StringComparison.Ordinal) || content.Contains("AddControllers", StringComparison.Ordinal) ? "AspNetCoreWebApi" : "AspNetCoreWebApp";
                    indicators.Add(new ClassificationIndicator(applicationType, $"Repository-contained source artifact '{relativeArtifactPath}' contains ASP.NET Core startup indicators for '{relativeProjectPath}'.", 210));
                }

                if (string.Equals(Path.GetFileName(artifactPath), "web.config", StringComparison.OrdinalIgnoreCase) && content.Contains("system.web", StringComparison.OrdinalIgnoreCase))
                {
                    indicators.Add(new ClassificationIndicator("ClassicAspNetWebApp", $"Repository-contained configuration artifact '{relativeArtifactPath}' contains classic ASP.NET configuration.", 220));
                }
            }

            return indicators;
        }

        /// <summary>
        /// Applies low-confidence naming heuristics only after stronger signals have been exhausted.
        /// </summary>
        /// <param name="relativeProjectPath">The repository-relative project path.</param>
        /// <param name="projectName">The project display name.</param>
        /// <returns>A low-confidence indicator, or <see langword="null" /> when naming is not useful.</returns>
        private static ClassificationIndicator? GetLowConfidenceIndicator(string relativeProjectPath, string projectName)
        {
            // Naming heuristics are limited to categories where repository names are commonly intentional and low-risk for inventory display.
            string combinedName = string.Concat(relativeProjectPath, "/", projectName);

            if (combinedName.Contains(".Tools", StringComparison.OrdinalIgnoreCase) || combinedName.Contains("/tools/", StringComparison.OrdinalIgnoreCase))
            {
                return new ClassificationIndicator("ToolingProject", "Project path or name contains a tooling convention.", 300);
            }

            if (combinedName.Contains(".Tests", StringComparison.OrdinalIgnoreCase) || combinedName.Contains("/test/", StringComparison.OrdinalIgnoreCase) || combinedName.Contains("/tests/", StringComparison.OrdinalIgnoreCase))
            {
                return new ClassificationIndicator("TestProject", "Project path or name contains a test convention.", 310);
            }

            return null;
        }

        /// <summary>
        /// Creates a known classification value from ordered evidence.
        /// </summary>
        /// <param name="applicationType">The supported application type value.</param>
        /// <param name="confidenceLabel">The confidence band name.</param>
        /// <param name="confidenceValue">The normalized confidence value.</param>
        /// <param name="evidence">The evidence descriptions supporting the decision.</param>
        /// <returns>A known application type classification.</returns>
        private static ApplicationTypeClassification CreateKnown(string applicationType, string confidenceLabel, decimal confidenceValue, IReadOnlyList<string> evidence)
        {
            // Sorting evidence makes metadata deterministic even when multiple equivalent indicators are discovered.
            return new ApplicationTypeClassification(
                applicationType,
                confidenceLabel,
                confidenceValue,
                evidence.Order(StringComparer.Ordinal).ToArray(),
                [],
                IsUnknown: false,
                UnknownReason: null);
        }

        /// <summary>
        /// Determines whether an SDK, reference, or package string contains a value ignoring case.
        /// </summary>
        /// <param name="value">The candidate value to inspect.</param>
        /// <param name="expected">The expected substring.</param>
        /// <returns><see langword="true" /> when <paramref name="expected" /> appears in <paramref name="value" />; otherwise, <see langword="false" />.</returns>
        private static bool ContainsIgnoreCase(string? value, string expected)
        {
            // Null and whitespace values are treated as absent indicators.
            return !string.IsNullOrWhiteSpace(value) && value.Contains(expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether any extracted package reference matches one of the supplied normalized package IDs.
        /// </summary>
        /// <param name="packageReferences">The package references to inspect.</param>
        /// <param name="normalizedPackageIds">The lowercase package IDs that identify a supported indicator.</param>
        /// <returns><see langword="true" /> when a package ID matches; otherwise, <see langword="false" />.</returns>
        private static bool HasAnyPackage(IEnumerable<PackageReferenceDeclaration> packageReferences, params string[] normalizedPackageIds)
        {
            // Package matching uses normalized IDs so display casing in project files does not affect classification.
            HashSet<string> expectedPackageIds = new(normalizedPackageIds, StringComparer.OrdinalIgnoreCase);
            return packageReferences.Any(packageReference => expectedPackageIds.Contains(packageReference.NormalizedPackageId));
        }

        /// <summary>
        /// Determines whether the project declares a known Visual Studio project type GUID.
        /// </summary>
        /// <param name="document">The parsed project XML document.</param>
        /// <param name="projectTypeGuid">The lowercase GUID text to search for.</param>
        /// <returns><see langword="true" /> when a `ProjectTypeGuids` value contains the supplied GUID; otherwise, <see langword="false" />.</returns>
        private static bool ContainsProjectTypeGuid(XDocument document, string projectTypeGuid)
        {
            // Old-style project GUIDs often appear in semicolon-delimited text, so substring matching is sufficient after lowercase normalization.
            return document.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "ProjectTypeGuids", StringComparison.Ordinal))
                .Select(element => element.Value)
                .Any(value => value.Contains(projectTypeGuid, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Determines whether the project references an assembly with the supplied include prefix.
        /// </summary>
        /// <param name="document">The parsed project XML document.</param>
        /// <param name="referencePrefix">The assembly reference prefix to match.</param>
        /// <returns><see langword="true" /> when a matching reference is present; otherwise, <see langword="false" />.</returns>
        private static bool HasReference(XDocument document, string referencePrefix)
        {
            // Reference Include values may contain version and culture metadata after a comma, so prefix matching covers old-style forms.
            return document.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Reference", StringComparison.Ordinal))
                .Select(element => element.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "Include", StringComparison.Ordinal))?.Value)
                .Any(value => ContainsIgnoreCase(value, referencePrefix));
        }

        /// <summary>
        /// Determines whether the project includes a content artifact ending with a supported extension.
        /// </summary>
        /// <param name="document">The parsed project XML document.</param>
        /// <param name="extension">The file extension to match.</param>
        /// <returns><see langword="true" /> when a matching content include is present; otherwise, <see langword="false" />.</returns>
        private static bool HasContentIncludeEnding(XDocument document, string extension)
        {
            // Web Forms artifacts are usually declared as Content items in old-style web application projects.
            return document.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Content", StringComparison.Ordinal))
                .Select(element => element.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "Include", StringComparison.Ordinal))?.Value)
                .Any(value => !string.IsNullOrWhiteSpace(value) && value.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Determines whether an output type represents an executable project.
        /// </summary>
        /// <param name="outputType">The output type value to inspect.</param>
        /// <returns><see langword="true" /> when the output type is executable; otherwise, <see langword="false" />.</returns>
        private static bool IsExecutableOutput(string? outputType)
        {
            // Exe and WinExe both represent runnable entrypoint projects at project metadata level.
            return string.Equals(outputType?.Trim(), "Exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(outputType?.Trim(), "WinExe", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a file extension is safe and relevant for bounded artifact indicator inspection.
        /// </summary>
        /// <param name="artifactPath">The artifact path whose extension is checked.</param>
        /// <returns><see langword="true" /> for supported source or configuration artifacts; otherwise, <see langword="false" />.</returns>
        private static bool IsSupportedArtifactExtension(string artifactPath)
        {
            // Project classification inspects only small textual source/configuration files and avoids binaries, generated outputs, packages, and build artifacts with executable behavior.
            string extension = Path.GetExtension(artifactPath);
            return string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".vb", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".config", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads a bounded prefix from a text artifact for indicator matching.
        /// </summary>
        /// <param name="artifactPath">The artifact file path to read.</param>
        /// <returns>A bounded artifact text preview, or an empty string when the artifact cannot be read safely.</returns>
        private static string ReadBoundedArtifactText(string artifactPath)
        {
            // Classification should not fail extraction for an optional artifact read issue, and it should never load very large files into metadata processing.
            try
            {
                using FileStream stream = File.OpenRead(artifactPath);
                int length = (int)Math.Min(stream.Length, ArtifactPreviewCharacterLimit);
                byte[] buffer = new byte[length];
                int read = stream.Read(buffer, 0, length);
                return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Determines whether one path is contained by a directory path.
        /// </summary>
        /// <param name="directoryPath">The containing directory path.</param>
        /// <param name="candidatePath">The candidate path to test.</param>
        /// <returns><see langword="true" /> when the candidate path is inside the directory; otherwise, <see langword="false" />.</returns>
        private static bool IsPathContainedByDirectory(string directoryPath, string candidatePath)
        {
            // Full-path prefix comparison prevents parent-directory traversal from escaping the submitted repository boundary.
            string normalizedDirectory = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedCandidate = Path.GetFullPath(candidatePath);
            return normalizedCandidate.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds a repository-relative path using forward slash separators.
        /// </summary>
        /// <param name="repositoryRootDirectory">The absolute repository root directory.</param>
        /// <param name="filePath">The absolute artifact file path.</param>
        /// <returns>A repository-relative path suitable for deterministic evidence descriptions.</returns>
        private static string GetRepositoryRelativePath(string repositoryRootDirectory, string filePath)
        {
            // Classification evidence descriptions use the same relative path format as graph identity metadata.
            string relativePath = Path.GetRelativePath(repositoryRootDirectory, filePath);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Represents one ordered classification indicator before final confidence resolution.
        /// </summary>
        /// <param name="ApplicationType">The application type implied by the indicator.</param>
        /// <param name="Evidence">The concise evidence text that explains the indicator.</param>
        /// <param name="Priority">The deterministic priority used when several non-conflicting indicators exist.</param>
        private sealed record ClassificationIndicator(string ApplicationType, string Evidence, int Priority);
    }
}
