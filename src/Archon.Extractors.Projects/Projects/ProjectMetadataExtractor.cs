using System.Xml.Linq;

namespace Archon.Extractors.Projects.Projects
{
    /// <summary>
    /// Extracts supported C# and VB.NET project metadata by reading project XML without executing MSBuild targets or package restore.
    /// </summary>
    internal sealed class ProjectMetadataExtractor
    {
        /// <summary>
        /// Extracts deterministic metadata from one supported project file.
        /// </summary>
        /// <param name="projectPath">The absolute project file path to read.</param>
        /// <param name="relativeProjectPath">The repository-relative project path used for graph identity.</param>
        /// <param name="projectName">The project display name declared by the submitted solution.</param>
        /// <param name="language">The source language inferred from the supported project declaration.</param>
        /// <param name="cancellationToken">The cancellation token that stops project file reading before or during asynchronous I/O.</param>
        /// <returns>Project metadata extracted from XML properties and deterministic defaults.</returns>
        internal async Task<ProjectMetadata> ExtractAsync(string projectPath, string relativeProjectPath, string projectName, ProjectLanguage language, CancellationToken cancellationToken)
        {
            // The extractor reads project files as data. It never creates MSBuildWorkspace, invokes targets, restores packages, or runs repository scripts.
            ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
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
                lineCount);
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
    }
}
