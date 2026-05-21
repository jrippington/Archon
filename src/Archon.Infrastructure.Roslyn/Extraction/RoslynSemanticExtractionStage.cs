using System.Xml.Linq;
using Archon.Application.Extraction.Pipeline;
using Archon.Roslyn.CSharp;
using Archon.Roslyn.SemanticModel;
using Archon.Roslyn.VisualBasic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.Extensions.Logging;

namespace Archon.Infrastructure.Roslyn.Extraction
{
    /// <summary>
    /// Executes the WP006 semantic extraction stage by loading submitted C# and Visual Basic projects and projecting Roslyn facts into the shared snapshot accumulator.
    /// </summary>
    /// <remarks>
    /// The stage deliberately avoids Neo4j and API response contracts. It is an infrastructure adapter that turns repository files into Roslyn compilations, invokes the language-specific semantic extractors, and hands generic graph facts to the application pipeline.
    /// </remarks>
    public sealed class RoslynSemanticExtractionStage : IExtractionStage
    {
        /// <summary>
        /// Stores the projector that maps semantic facts into application graph contracts.
        /// </summary>
        private readonly SemanticGraphProjection _projection;

        /// <summary>
        /// Stores the logger used for credential-safe semantic extraction events.
        /// </summary>
        private readonly ILogger<RoslynSemanticExtractionStage> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RoslynSemanticExtractionStage" /> class.
        /// </summary>
        /// <param name="logger">The logger used for semantic extraction start, completion, and degraded-condition messages.</param>
        public RoslynSemanticExtractionStage(ILogger<RoslynSemanticExtractionStage> logger)
        {
            // The stage owns only orchestration state; individual extraction behavior remains in the C# and Visual Basic Roslyn projects.
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _projection = new SemanticGraphProjection();
        }

        /// <summary>
        /// Gets the stable stage identifier used by pipeline ordering, progress, and diagnostics.
        /// </summary>
        public string StageId => "roslyn-semantic";

        /// <summary>
        /// Loads submitted solution projects, runs language-specific Roslyn semantic extraction, and contributes graph facts to the shared accumulator.
        /// </summary>
        /// <param name="context">The stage context containing resolved repository input, accepted run state, and shared accumulation.</param>
        /// <param name="cancellationToken">The cancellation token that stops file reads, compilation creation, and document extraction.</param>
        /// <returns>A successful stage result when semantic extraction completes or degrades non-fatally.</returns>
        public async Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // Semantic extraction is non-blocking for ordinary compiler diagnostics: resolvable facts are still projected and degraded facts become warnings/evidence.
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Starting Roslyn semantic extraction for run {RunId} with {SolutionCount} submitted solution path(s).",
                context.Run.RunId.ToString(),
                context.ResolvedInput.SolutionPaths.Count);

            int projectCount = 0;
            int documentCount = 0;
            int degradedCount = 0;
            foreach (string solutionPath in context.ResolvedInput.SolutionPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<SemanticProjectInput> projects;
                try
                {
                    projects = await LoadProjectsAsync(context.ResolvedInput.RepositoryRootDirectory, solutionPath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidDataException)
                {
                    // Solution-loading failures are controlled extraction problems because no reliable semantic project context remains for that input.
                    _logger.LogError(exception, "Roslyn semantic extraction could not load a submitted solution for run {RunId}.", context.Run.RunId.ToString());
                    return ExtractionStageResult.BlockingError("Roslyn semantic extraction could not load a submitted solution. Review server logs for details.");
                }

                foreach (SemanticProjectInput project in projects)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    projectCount++;
                    if (project.Language == SemanticProjectLanguage.Unsupported)
                    {
                        context.Accumulation.AddWarning($"Semantic extraction skipped unsupported project '{project.RelativeProjectPath}'.");
                        continue;
                    }

                    try
                    {
                        SemanticProjectExtractionOutcome outcome = ExtractProject(context, project, cancellationToken);
                        documentCount += outcome.DocumentCount;
                        degradedCount += outcome.DegradedFactCount;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
                    {
                        // Project-level failures degrade the stage instead of blocking other projects in the same submitted solution.
                        degradedCount++;
                        context.Accumulation.AddWarning($"Roslyn semantic extraction skipped project '{project.RelativeProjectPath}' because its semantic model could not be created.");
                        _logger.LogWarning(exception, "Roslyn semantic extraction skipped project {ProjectPath} for run {RunId}.", project.RelativeProjectPath, context.Run.RunId.ToString());
                    }
                }
            }

            if (degradedCount > 0)
            {
                _logger.LogWarning(
                    "Roslyn semantic extraction completed for run {RunId} with {DegradedCount} degraded semantic outcome(s).",
                    context.Run.RunId.ToString(),
                    degradedCount);
            }

            _logger.LogInformation(
                "Completed Roslyn semantic extraction for run {RunId}; inspected {ProjectCount} project(s) and {DocumentCount} document(s).",
                context.Run.RunId.ToString(),
                projectCount,
                documentCount);
            return ExtractionStageResult.Success();
        }

        /// <summary>
        /// Runs semantic extraction for one loaded project and projects each document result into the shared accumulator.
        /// </summary>
        /// <param name="context">The stage context receiving projected graph facts.</param>
        /// <param name="project">The loaded project input to compile and extract.</param>
        /// <param name="cancellationToken">The cancellation token that stops compilation and document extraction.</param>
        /// <returns>The document and degraded-fact counts observed for logging.</returns>
        private SemanticProjectExtractionOutcome ExtractProject(ExtractionStageContext context, SemanticProjectInput project, CancellationToken cancellationToken)
        {
            // Compilation is created from fixture/project source files directly so targeted tests do not require MSBuildWorkspace or Aspire startup.
            IReadOnlyList<SyntaxTree> syntaxTrees = project.Documents.Select(document => ParseSyntaxTree(project.Language, document)).ToArray();
            Compilation compilation = CreateCompilation(project, syntaxTrees);
            ISemanticDocumentExtractor extractor = CreateExtractor(project.Language);
            int degradedCount = 0;

            foreach (SyntaxTree syntaxTree in syntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
                SemanticExtractionRequest request = new(
                    context.ResolvedInput.RepositoryRootDirectory,
                    project.RelativeProjectPath,
                    syntaxTree.FilePath,
                    syntaxTree,
                    semanticModel);
                SemanticExtractionResult result = extractor.Extract(request, cancellationToken);
                degradedCount += result.Diagnostics.Count + result.Unknowns.Count + result.Warnings.Count + result.Errors.Count;
                if (result.Diagnostics.Count > 0 || result.Unknowns.Count > 0)
                {
                    _logger.LogWarning(
                        "Roslyn semantic extraction found {DiagnosticCount} diagnostic(s) and {UnknownCount} unknown(s) in project {ProjectPath} document {DocumentPath}.",
                        result.Diagnostics.Count,
                        result.Unknowns.Count,
                        project.RelativeProjectPath,
                        Path.GetFileName(syntaxTree.FilePath));
                }

                _projection.Project(context, result, project.RelativeProjectPath);
            }

            return new SemanticProjectExtractionOutcome(syntaxTrees.Count, degradedCount);
        }

        /// <summary>
        /// Loads supported project declarations from one submitted solution file.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="solutionPath">The absolute submitted solution path.</param>
        /// <param name="cancellationToken">The cancellation token that stops solution and project file reads.</param>
        /// <returns>The semantic project inputs discovered from supported solution declarations.</returns>
        private static async Task<IReadOnlyList<SemanticProjectInput>> LoadProjectsAsync(string repositoryRootDirectory, string solutionPath, CancellationToken cancellationToken)
        {
            // The lightweight parser is sufficient for repository-local fixture projects and keeps the semantic stage independent from MSBuildWorkspace restore behavior.
            List<SemanticProjectInput> projects = [];
            string solutionDirectory = Path.GetDirectoryName(solutionPath) ?? repositoryRootDirectory;
            foreach (string line in await File.ReadAllLinesAsync(solutionPath, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryParseSolutionProjectLine(line, out string? projectPath) || string.IsNullOrWhiteSpace(projectPath))
                {
                    continue;
                }

                string parsedProjectPath = projectPath;
                string absoluteProjectPath = Path.GetFullPath(Path.Combine(solutionDirectory, parsedProjectPath));
                string relativeProjectPath = GetRepositoryRelativePath(repositoryRootDirectory, absoluteProjectPath);
                projects.Add(await LoadProjectAsync(repositoryRootDirectory, absoluteProjectPath, relativeProjectPath, cancellationToken).ConfigureAwait(false));
            }

            return projects;
        }

        /// <summary>
        /// Loads one project file and the source documents it includes.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="absoluteProjectPath">The absolute project path to inspect.</param>
        /// <param name="relativeProjectPath">The repository-relative project path.</param>
        /// <param name="cancellationToken">The cancellation token that stops XML and source reads.</param>
        /// <returns>A semantic project input for supported or unsupported languages.</returns>
        private static async Task<SemanticProjectInput> LoadProjectAsync(string repositoryRootDirectory, string absoluteProjectPath, string relativeProjectPath, CancellationToken cancellationToken)
        {
            // Project XML is parsed for explicit Compile items first and then falls back to SDK-style default source discovery for compact fixtures.
            SemanticProjectLanguage language = DetermineLanguage(absoluteProjectPath);
            if (language == SemanticProjectLanguage.Unsupported || !File.Exists(absoluteProjectPath))
            {
                return new SemanticProjectInput(relativeProjectPath, language, []);
            }

            XDocument document = XDocument.Parse(await File.ReadAllTextAsync(absoluteProjectPath, cancellationToken).ConfigureAwait(false));
            string projectDirectory = Path.GetDirectoryName(absoluteProjectPath) ?? repositoryRootDirectory;
            IReadOnlyList<string> compileIncludes = ReadCompileIncludes(document);
            if (compileIncludes.Count == 0)
            {
                compileIncludes = Directory.EnumerateFiles(projectDirectory, language == SemanticProjectLanguage.CSharp ? "*.cs" : "*.vb", SearchOption.AllDirectories)
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

                string sourceText = await File.ReadAllTextAsync(absoluteSourcePath, cancellationToken).ConfigureAwait(false);
                sourceDocuments.Add(new SemanticDocumentInput(absoluteSourcePath, sourceText));
            }

            return new SemanticProjectInput(relativeProjectPath, language, sourceDocuments);
        }

        /// <summary>
        /// Reads explicit compile item includes from project XML.
        /// </summary>
        /// <param name="projectDocument">The parsed project XML document.</param>
        /// <returns>Repository-local compile include strings ordered deterministically.</returns>
        private static IReadOnlyList<string> ReadCompileIncludes(XDocument projectDocument)
        {
            // SDK-style projects often use default compile items, but explicit includes make semantic tests deterministic and fast.
            return projectDocument.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Compile", StringComparison.Ordinal))
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => include!.Trim())
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Parses a Visual Studio solution project line to extract the declared project path.
        /// </summary>
        /// <param name="line">The solution-file line to parse.</param>
        /// <param name="projectPath">The parsed project path when the line contains one.</param>
        /// <returns><see langword="true" /> when a project path was parsed; otherwise, <see langword="false" />.</returns>
        private static bool TryParseSolutionProjectLine(string line, out string? projectPath)
        {
            // A solution Project line stores name, path, and id in quoted comma-separated fields; only the path is needed here.
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
            if (!candidate.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && !candidate.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            projectPath = candidate;
            return true;
        }

        /// <summary>
        /// Determines the semantic project language from a project file extension.
        /// </summary>
        /// <param name="projectPath">The project path to classify.</param>
        /// <returns>The semantic project language supported by this stage.</returns>
        private static SemanticProjectLanguage DetermineLanguage(string projectPath)
        {
            // File extensions are enough at this boundary because solution parsing already identifies project files.
            string extension = Path.GetExtension(projectPath);
            if (string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return SemanticProjectLanguage.CSharp;
            }

            if (string.Equals(extension, ".vbproj", StringComparison.OrdinalIgnoreCase))
            {
                return SemanticProjectLanguage.VisualBasic;
            }

            return SemanticProjectLanguage.Unsupported;
        }

        /// <summary>
        /// Parses one source document into a Roslyn syntax tree for the project language.
        /// </summary>
        /// <param name="language">The semantic project language.</param>
        /// <param name="document">The source document to parse.</param>
        /// <returns>A syntax tree with the document path attached for evidence.</returns>
        private static SyntaxTree ParseSyntaxTree(SemanticProjectLanguage language, SemanticDocumentInput document)
        {
            // The file path is preserved on the syntax tree so evidence can normalize it against the repository root.
            return language switch
            {
                SemanticProjectLanguage.CSharp => CSharpSyntaxTree.ParseText(document.SourceText, path: document.AbsolutePath),
                SemanticProjectLanguage.VisualBasic => VisualBasicSyntaxTree.ParseText(document.SourceText, path: document.AbsolutePath),
                _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported semantic project language.")
            };
        }

        /// <summary>
        /// Creates a Roslyn compilation for one semantic project input.
        /// </summary>
        /// <param name="project">The semantic project input being compiled.</param>
        /// <param name="syntaxTrees">The syntax trees that belong to the project.</param>
        /// <returns>A Roslyn compilation for semantic model creation.</returns>
        private static Compilation CreateCompilation(SemanticProjectInput project, IReadOnlyList<SyntaxTree> syntaxTrees)
        {
            // The minimal metadata references cover ordinary fixture declarations; unresolved external references remain diagnostics rather than blocking failures.
            MetadataReference[] references =
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            ];
            return project.Language switch
            {
                SemanticProjectLanguage.CSharp => CSharpCompilation.Create(
                    Path.GetFileNameWithoutExtension(project.RelativeProjectPath),
                    syntaxTrees,
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)),
                SemanticProjectLanguage.VisualBasic => VisualBasicCompilation.Create(
                    Path.GetFileNameWithoutExtension(project.RelativeProjectPath),
                    syntaxTrees,
                    references,
                    new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary)),
                _ => throw new ArgumentOutOfRangeException(nameof(project), project.Language, "Unsupported semantic project language.")
            };
        }

        /// <summary>
        /// Creates the language-specific document extractor for a supported project language.
        /// </summary>
        /// <param name="language">The semantic project language to extract.</param>
        /// <returns>An extractor that can process the project's syntax trees and semantic models.</returns>
        private static ISemanticDocumentExtractor CreateExtractor(SemanticProjectLanguage language)
        {
            // Extractor construction stays local because the implementations are stateless and cheap to create per project.
            return language switch
            {
                SemanticProjectLanguage.CSharp => new CSharpSemanticDocumentExtractor(),
                SemanticProjectLanguage.VisualBasic => new VisualBasicSemanticDocumentExtractor(),
                _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported semantic project language.")
            };
        }

        /// <summary>
        /// Builds a repository-relative path from an absolute project or source path.
        /// </summary>
        /// <param name="repositoryRootDirectory">The absolute repository root directory.</param>
        /// <param name="absolutePath">The absolute path inside the repository.</param>
        /// <returns>A repository-relative path with forward slashes.</returns>
        private static string GetRepositoryRelativePath(string repositoryRootDirectory, string absolutePath)
        {
            // Repository-relative paths keep stable identities independent from developer machine roots.
            string relativePath = Path.GetRelativePath(repositoryRootDirectory, absolutePath);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Represents the project language categories supported by the semantic infrastructure stage.
        /// </summary>
        private enum SemanticProjectLanguage
        {
            /// <summary>
            /// Represents a C# project.
            /// </summary>
            CSharp,

            /// <summary>
            /// Represents a Visual Basic project.
            /// </summary>
            VisualBasic,

            /// <summary>
            /// Represents a project declaration that semantic extraction does not currently support.
            /// </summary>
            Unsupported
        }

        /// <summary>
        /// Represents one loaded source document ready for parsing.
        /// </summary>
        /// <param name="AbsolutePath">The absolute source path preserved on the syntax tree for evidence normalization.</param>
        /// <param name="SourceText">The source text to parse.</param>
        private sealed record SemanticDocumentInput(string AbsolutePath, string SourceText);

        /// <summary>
        /// Represents one loaded project and its source documents.
        /// </summary>
        /// <param name="RelativeProjectPath">The repository-relative project path used as project context and stable identity.</param>
        /// <param name="Language">The supported semantic project language.</param>
        /// <param name="Documents">The loaded source documents that belong to the project.</param>
        private sealed record SemanticProjectInput(string RelativeProjectPath, SemanticProjectLanguage Language, IReadOnlyList<SemanticDocumentInput> Documents);

        /// <summary>
        /// Represents extraction counts from one semantic project.
        /// </summary>
        /// <param name="DocumentCount">The number of source documents extracted.</param>
        /// <param name="DegradedFactCount">The number of diagnostics, unknowns, warnings, and errors observed.</param>
        private readonly record struct SemanticProjectExtractionOutcome(int DocumentCount, int DegradedFactCount);
    }
}
