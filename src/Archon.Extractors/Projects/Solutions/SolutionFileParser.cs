using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Archon.Extractors.Projects.Solutions
{
    /// <summary>
    /// Parses the lightweight solution-file facts needed by the repository and solution extraction slice.
    /// </summary>
    internal sealed partial class SolutionFileParser
    {
        /// <summary>
        /// Captures Visual Studio solution project declarations without requiring a full MSBuild solution load.
        /// </summary>
        private static readonly Regex s_projectDeclarationRegex = new(
            "^Project\\(\\\"(?<typeGuid>[^\\\"]+)\\\"\\)\\s*=\\s*\\\"(?<name>[^\\\"]+)\\\",\\s*\\\"(?<path>[^\\\"]+)\\\",\\s*\\\"(?<projectGuid>[^\\\"]+)\\\"",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Parses one submitted solution file into solution-file facts without loading projects or executing build logic.
        /// </summary>
        /// <param name="solutionPath">The absolute submitted solution path to read.</param>
        /// <param name="cancellationToken">The cancellation token that stops file reading before or during asynchronous I/O.</param>
        /// <returns>Parsed solution-file facts containing visible project declarations and file line count.</returns>
        /// <exception cref="InvalidDataException">Thrown when the file is blank or does not contain a recognizable Visual Studio solution header.</exception>
        internal async Task<SolutionFileFacts> ParseAsync(string solutionPath, CancellationToken cancellationToken)
        {
            // The parser intentionally reads text only. It does not invoke MSBuild, restore packages, or scan the repository for additional solutions.
            ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
            cancellationToken.ThrowIfCancellationRequested();

            string[] lines = await File.ReadAllLinesAsync(solutionPath, cancellationToken).ConfigureAwait(false);
            if (IsSlnxSolution(solutionPath))
            {
                return ParseSlnx(lines);
            }

            if (lines.Length == 0 || !HasSolutionHeader(lines))
            {
                throw new InvalidDataException("The submitted solution file does not contain a recognized Visual Studio solution header.");
            }

            List<SolutionProjectDeclaration> declarations = [];
            for (int index = 0; index < lines.Length; index++)
            {
                // Project declarations are optional for Slice 1, but capturing them now gives evidence for visible membership lines.
                Match match = s_projectDeclarationRegex.Match(lines[index]);
                if (match.Success)
                {
                    declarations.Add(new SolutionProjectDeclaration(
                        match.Groups["name"].Value,
                        match.Groups["path"].Value,
                        match.Groups["typeGuid"].Value,
                        match.Groups["projectGuid"].Value,
                        index + 1));
                }
            }

            return new SolutionFileFacts(declarations, lines.Length);
        }

        /// <summary>
        /// Parses project declarations from the XML-based .slnx solution format.
        /// </summary>
        /// <param name="lines">The submitted .slnx file lines read from disk.</param>
        /// <returns>Parsed solution-file facts containing visible project declarations and file line count.</returns>
        /// <exception cref="InvalidDataException">Thrown when the .slnx content is blank, malformed, or does not use the expected root element.</exception>
        private static SolutionFileFacts ParseSlnx(IReadOnlyList<string> lines)
        {
            // .slnx stores project membership as XML Project elements with Path attributes rather than text Project(...) declarations.
            if (lines.Count == 0)
            {
                throw new InvalidDataException("The submitted .slnx file is blank.");
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(string.Join(Environment.NewLine, lines), LoadOptions.SetLineInfo);
            }
            catch (XmlException exception)
            {
                throw new InvalidDataException("The submitted .slnx file does not contain valid XML.", exception);
            }

            if (!string.Equals(document.Root?.Name.LocalName, "Solution", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The submitted .slnx file does not contain a recognized Solution root element.");
            }

            List<SolutionProjectDeclaration> declarations = [];
            foreach (XElement projectElement in document.Descendants().Where(element => string.Equals(element.Name.LocalName, "Project", StringComparison.Ordinal)))
            {
                string? declaredPath = projectElement.Attribute("Path")?.Value;
                if (string.IsNullOrWhiteSpace(declaredPath))
                {
                    continue;
                }

                IXmlLineInfo lineInfo = projectElement;
                declarations.Add(new SolutionProjectDeclaration(
                    Path.GetFileNameWithoutExtension(declaredPath),
                    declaredPath,
                    ProjectTypeGuid: null,
                    ProjectGuid: null,
                    lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1));
            }

            return new SolutionFileFacts(declarations, lines.Count);
        }

        /// <summary>
        /// Determines whether the submitted solution path uses the XML-based .slnx format.
        /// </summary>
        /// <param name="solutionPath">The absolute submitted solution path.</param>
        /// <returns><see langword="true" /> when the solution path uses the .slnx extension; otherwise <see langword="false" />.</returns>
        private static bool IsSlnxSolution(string solutionPath)
        {
            // Extension dispatch keeps the legacy .sln parser unchanged while allowing modern .slnx submissions to use XML parsing.
            return string.Equals(Path.GetExtension(solutionPath), ".slnx", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether the submitted file contains the standard Visual Studio solution header near the top of the file.
        /// </summary>
        /// <param name="lines">The solution file lines read from disk.</param>
        /// <returns><see langword="true" /> when the file contains a recognizable solution header; otherwise, <see langword="false" />.</returns>
        private static bool HasSolutionHeader(IReadOnlyList<string> lines)
        {
            // Visual Studio solution files place the format header at the top, but a UTF-8 BOM or leading blank line should not cause rejection.
            int linesToInspect = Math.Min(lines.Count, 5);
            for (int index = 0; index < linesToInspect; index++)
            {
                if (lines[index].Contains("Microsoft Visual Studio Solution File", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
