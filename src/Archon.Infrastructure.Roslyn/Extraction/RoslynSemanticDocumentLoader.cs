using System.Xml.Linq;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Archon.Infrastructure.Roslyn.Extraction
{
    /// <summary>
    /// Loads C# project source documents from submitted solution files and creates semantic extraction requests for downstream feature-specific extractors.
    /// </summary>
    public sealed class RoslynSemanticDocumentLoader
    {
        /// <summary>
        /// Loads C# semantic documents from one solution file using the same lightweight project parsing strategy as the generic Roslyn semantic stage.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="solutionPath">The absolute submitted solution path.</param>
        /// <param name="cancellationToken">The cancellation token that stops solution, project, and source reads.</param>
        /// <returns>Semantic extraction requests for C# source files discovered from supported projects.</returns>
        public async Task<IReadOnlyList<SemanticExtractionRequest>> LoadCSharpDocumentsAsync(string repositoryRootDirectory, string solutionPath, CancellationToken cancellationToken)
        {
            // The loader intentionally avoids MSBuildWorkspace so feature-specific stages can reuse semantic models without restore or build side effects.
            List<SemanticExtractionRequest> requests = [];
            string solutionDirectory = Path.GetDirectoryName(solutionPath) ?? repositoryRootDirectory;
            foreach (string line in await File.ReadAllLinesAsync(solutionPath, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryParseSolutionProjectLine(line, out string? projectPath) || string.IsNullOrWhiteSpace(projectPath) || !projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string absoluteProjectPath = Path.GetFullPath(Path.Combine(solutionDirectory, projectPath));
                string relativeProjectPath = GetRepositoryRelativePath(repositoryRootDirectory, absoluteProjectPath);
                IReadOnlyList<SemanticDocumentInput> sourceDocuments = await LoadProjectDocumentsAsync(repositoryRootDirectory, absoluteProjectPath, cancellationToken).ConfigureAwait(false);
                IReadOnlyList<SyntaxTree> syntaxTrees = sourceDocuments.Select(static document => CSharpSyntaxTree.ParseText(document.SourceText, path: document.AbsolutePath)).ToArray();
                Compilation compilation = CreateCompilation(relativeProjectPath, syntaxTrees);
                foreach (SyntaxTree syntaxTree in syntaxTrees)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    requests.Add(new SemanticExtractionRequest(repositoryRootDirectory, relativeProjectPath, syntaxTree.FilePath, syntaxTree, compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true)));
                }
            }

            return requests;
        }

        /// <summary>
        /// Loads source documents for a C# project file.
        /// </summary>
        private static async Task<IReadOnlyList<SemanticDocumentInput>> LoadProjectDocumentsAsync(string repositoryRootDirectory, string absoluteProjectPath, CancellationToken cancellationToken)
        {
            if (!File.Exists(absoluteProjectPath))
            {
                return [];
            }

            XDocument document = XDocument.Parse(await File.ReadAllTextAsync(absoluteProjectPath, cancellationToken).ConfigureAwait(false));
            string projectDirectory = Path.GetDirectoryName(absoluteProjectPath) ?? repositoryRootDirectory;
            IReadOnlyList<string> compileIncludes = ReadCompileIncludes(document);
            if (compileIncludes.Count == 0)
            {
                compileIncludes = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                    .Where(path => !path.Contains(string.Concat(Path.DirectorySeparatorChar, "obj", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    .Select(path => Path.GetRelativePath(projectDirectory, path))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            List<SemanticDocumentInput> sourceDocuments = [];
            foreach (string compileInclude in compileIncludes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string absoluteSourcePath = Path.GetFullPath(Path.Combine(projectDirectory, compileInclude));
                if (!File.Exists(absoluteSourcePath))
                {
                    continue;
                }

                sourceDocuments.Add(new SemanticDocumentInput(absoluteSourcePath, await File.ReadAllTextAsync(absoluteSourcePath, cancellationToken).ConfigureAwait(false)));
            }

            return sourceDocuments;
        }

        /// <summary>
        /// Reads explicit compile item includes from project XML.
        /// </summary>
        private static IReadOnlyList<string> ReadCompileIncludes(XDocument projectDocument)
        {
            return projectDocument.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Compile", StringComparison.Ordinal))
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => include!.Trim())
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Parses a Visual Studio solution project line to extract a project path.
        /// </summary>
        private static bool TryParseSolutionProjectLine(string line, out string? projectPath)
        {
            projectPath = null;
            if (!line.TrimStart().StartsWith("Project(", StringComparison.Ordinal))
            {
                return false;
            }

            string[] parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                return false;
            }

            string candidate = parts[1].Trim().Trim('"');
            if (!candidate.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            projectPath = candidate;
            return true;
        }

        /// <summary>
        /// Creates a Roslyn C# compilation for semantic model creation.
        /// </summary>
        private static Compilation CreateCompilation(string relativeProjectPath, IReadOnlyList<SyntaxTree> syntaxTrees)
        {
            MetadataReference[] references =
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            ];
            return CSharpCompilation.Create(Path.GetFileNameWithoutExtension(relativeProjectPath), syntaxTrees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        /// <summary>
        /// Builds a repository-relative path from an absolute project or source path.
        /// </summary>
        private static string GetRepositoryRelativePath(string repositoryRootDirectory, string absolutePath)
        {
            string relativePath = Path.GetRelativePath(repositoryRootDirectory, absolutePath);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Represents one loaded source document ready for parsing.
        /// </summary>
        /// <param name="AbsolutePath">The absolute source path preserved on the syntax tree for evidence normalization.</param>
        /// <param name="SourceText">The source text to parse.</param>
        private sealed record SemanticDocumentInput(string AbsolutePath, string SourceText);
    }
}
