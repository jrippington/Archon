using System.Xml.Linq;
using Archon.Application.Extraction.Pipeline;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Configuration;
using Archon.Extractors.DependencyInjection;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.Extensions.Logging;

namespace Archon.Api.Extraction
{
    /// <summary>
    /// Runs WP007 dependency-injection and configuration extraction as part of the API-triggered extraction pipeline.
    /// </summary>
    /// <remarks>
    /// The stage is intentionally an orchestration adapter. It loads submitted C# and Visual Basic project source files into Roslyn semantic documents, delegates DI and configuration recognition to the extractor projects, and merges their graph-ready snapshot contributions into the shared application accumulator without writing persistence data directly.
    /// </remarks>
    public sealed class Wp007ExtractionStage : IExtractionStage
    {
        /// <summary>
        /// Stores the dependency-injection extractor used for service-registration and constructor-correlation facts.
        /// </summary>
        private readonly DirectMicrosoftDependencyInjectionExtractor _dependencyInjectionExtractor;

        /// <summary>
        /// Stores the composed configuration extractor used for modern and legacy configuration facts.
        /// </summary>
        private readonly ConfigurationExtractor _configurationExtractor;

        /// <summary>
        /// Stores the logger used for credential-safe WP007 orchestration events.
        /// </summary>
        private readonly ILogger<Wp007ExtractionStage> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Wp007ExtractionStage" /> class.
        /// </summary>
        /// <param name="logger">The logger used for start, completion, and degraded extraction messages.</param>
        public Wp007ExtractionStage(ILogger<Wp007ExtractionStage> logger)
            : this(new DirectMicrosoftDependencyInjectionExtractor(), new ConfigurationExtractor(), logger)
        {
            // The default constructor path keeps API module registration simple while leaving extractor behavior in the dedicated projects.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Wp007ExtractionStage" /> class with explicit extractor dependencies.
        /// </summary>
        /// <param name="dependencyInjectionExtractor">The extractor responsible for dependency-injection graph facts.</param>
        /// <param name="configurationExtractor">The extractor responsible for configuration graph facts.</param>
        /// <param name="logger">The logger used for credential-safe stage diagnostics.</param>
        public Wp007ExtractionStage(DirectMicrosoftDependencyInjectionExtractor dependencyInjectionExtractor, ConfigurationExtractor configurationExtractor, ILogger<Wp007ExtractionStage> logger)
        {
            // Explicit dependencies make the stage independently testable while still avoiding host-layer business logic.
            _dependencyInjectionExtractor = dependencyInjectionExtractor ?? throw new ArgumentNullException(nameof(dependencyInjectionExtractor));
            _configurationExtractor = configurationExtractor ?? throw new ArgumentNullException(nameof(configurationExtractor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the stable stage identifier used by pipeline ordering, progress reporting, and diagnostics.
        /// </summary>
        public string StageId => "wp007-configuration-dependency-injection";

        /// <summary>
        /// Loads submitted semantic project documents, runs WP007 extractors, and merges their graph facts into the shared accumulator.
        /// </summary>
        /// <param name="context">The pipeline context containing resolved repository input, accepted run state, and shared accumulation.</param>
        /// <param name="cancellationToken">The cancellation token that stops project loading and extractor execution.</param>
        /// <returns>A successful stage result when WP007 extraction completes or degrades non-fatally.</returns>
        public async Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // WP007 extraction is non-blocking for individual project load failures: configuration artifacts can still be parsed and other projects can still contribute facts.
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Starting WP007 extraction for run {RunId} with {SolutionCount} submitted solution path(s).",
                context.Run.RunId.ToString(),
                context.ResolvedInput.SolutionPaths.Count);

            List<SemanticExtractionRequest> semanticDocuments = [];
            int degradedProjectCount = 0;
            foreach (string solutionPath in context.ResolvedInput.SolutionPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    semanticDocuments.AddRange(await LoadSemanticDocumentsAsync(context.ResolvedInput.RepositoryRootDirectory, solutionPath, cancellationToken).ConfigureAwait(false));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidDataException)
                {
                    // A solution that cannot be read should not block configuration-file extraction from other evidence, but the warning must remain visible.
                    degradedProjectCount++;
                    context.Accumulation.AddWarning("WP007 extraction skipped a submitted solution because its project context could not be loaded.");
                    _logger.LogWarning(exception, "WP007 extraction skipped solution context for run {RunId}.", context.Run.RunId.ToString());
                }
            }

            StableKey snapshotStableKey = CreateSnapshotStableKey(context.ResolvedInput.RepositoryRootDirectory, context.Run.RunId.ToString());
            foreach (SemanticExtractionRequest semanticDocument in semanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!semanticDocument.DocumentPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DependencyInjectionExtractionResult dependencyInjectionResult = _dependencyInjectionExtractor.Extract(new DependencyInjectionExtractionRequest(snapshotStableKey, semanticDocument), cancellationToken);
                context.Accumulation.Merge(dependencyInjectionResult.Snapshot);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ModernConfigurationExtractionResult configurationResult = _configurationExtractor.Extract(new ModernConfigurationExtractionRequest(snapshotStableKey, context.ResolvedInput.RepositoryRootDirectory, semanticDocuments), cancellationToken);
            context.Accumulation.Merge(configurationResult.Snapshot);

            if (degradedProjectCount > 0)
            {
                _logger.LogWarning(
                    "WP007 extraction completed for run {RunId} with {DegradedProjectCount} degraded solution load outcome(s).",
                    context.Run.RunId.ToString(),
                    degradedProjectCount);
            }

            _logger.LogInformation(
                "Completed WP007 extraction for run {RunId}; inspected {DocumentCount} semantic document(s).",
                context.Run.RunId.ToString(),
                semanticDocuments.Count);
            return ExtractionStageResult.Success();
        }

        /// <summary>
        /// Loads semantic extraction requests for supported projects declared by one submitted solution file.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="solutionPath">The absolute submitted solution path.</param>
        /// <param name="cancellationToken">The cancellation token that stops solution, project, and source reads.</param>
        /// <returns>Semantic extraction requests for repository-contained source documents.</returns>
        private static async Task<IReadOnlyList<SemanticExtractionRequest>> LoadSemanticDocumentsAsync(string repositoryRootDirectory, string solutionPath, CancellationToken cancellationToken)
        {
            // The loader mirrors the lightweight WP006 solution/project handling so WP007 runs only from submitted solution context.
            List<SemanticProjectInput> projects = [];
            string solutionDirectory = Path.GetDirectoryName(solutionPath) ?? repositoryRootDirectory;
            foreach (string line in await File.ReadAllLinesAsync(solutionPath, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryParseSolutionProjectLine(line, out string? projectPath) || string.IsNullOrWhiteSpace(projectPath))
                {
                    continue;
                }

                string absoluteProjectPath = Path.GetFullPath(Path.Combine(solutionDirectory, projectPath));
                string relativeProjectPath = GetRepositoryRelativePath(repositoryRootDirectory, absoluteProjectPath);
                projects.Add(await LoadProjectAsync(repositoryRootDirectory, absoluteProjectPath, relativeProjectPath, cancellationToken).ConfigureAwait(false));
            }

            List<SemanticExtractionRequest> documents = [];
            foreach (SemanticProjectInput project in projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (project.Language == SemanticProjectLanguage.Unsupported || project.Documents.Count == 0)
                {
                    continue;
                }

                IReadOnlyList<SyntaxTree> syntaxTrees = project.Documents.Select(document => ParseSyntaxTree(project.Language, document)).ToArray();
                Compilation compilation = CreateCompilation(project, syntaxTrees);
                foreach (SyntaxTree syntaxTree in syntaxTrees)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
                    documents.Add(new SemanticExtractionRequest(repositoryRootDirectory, project.RelativeProjectPath, syntaxTree.FilePath, syntaxTree, semanticModel));
                }
            }

            return documents;
        }

        /// <summary>
        /// Loads one supported project file and its source documents.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="absoluteProjectPath">The absolute project file path.</param>
        /// <param name="relativeProjectPath">The repository-relative project path.</param>
        /// <param name="cancellationToken">The cancellation token that stops XML and source reads.</param>
        /// <returns>A project input with source documents when the project language is supported.</returns>
        private static async Task<SemanticProjectInput> LoadProjectAsync(string repositoryRootDirectory, string absoluteProjectPath, string relativeProjectPath, CancellationToken cancellationToken)
        {
            // Project files are treated as static repository artifacts; the stage does not run restore, build targets, or target application code.
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
            // Explicit Compile items keep fixtures deterministic; SDK-style defaults are discovered by the caller when this list is empty.
            return projectDocument.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Compile", StringComparison.Ordinal))
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => include!.Trim())
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Parses a Visual Studio solution project line to extract a C# or Visual Basic project path.
        /// </summary>
        /// <param name="line">The solution-file line to parse.</param>
        /// <param name="projectPath">The parsed project path when the line declares a supported project.</param>
        /// <returns><see langword="true" /> when a project path was parsed; otherwise, <see langword="false" />.</returns>
        private static bool TryParseSolutionProjectLine(string line, out string? projectPath)
        {
            // Solution parsing intentionally accepts only source project declarations so WP007 does not scan arbitrary repository files.
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
        /// Determines the supported semantic project language from a project file extension.
        /// </summary>
        /// <param name="projectPath">The project path to classify.</param>
        /// <returns>The supported language category, or unsupported for other file types.</returns>
        private static SemanticProjectLanguage DetermineLanguage(string projectPath)
        {
            // Extension-based classification matches the submitted project declaration boundary used by earlier extraction stages.
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
        /// <param name="document">The loaded source document.</param>
        /// <returns>A syntax tree with the document path attached for evidence normalization.</returns>
        private static SyntaxTree ParseSyntaxTree(SemanticProjectLanguage language, SemanticDocumentInput document)
        {
            // Syntax-tree paths are preserved so downstream extractor evidence remains repository-relative and source navigable.
            return language switch
            {
                SemanticProjectLanguage.CSharp => CSharpSyntaxTree.ParseText(document.SourceText, path: document.AbsolutePath),
                SemanticProjectLanguage.VisualBasic => VisualBasicSyntaxTree.ParseText(document.SourceText, path: document.AbsolutePath),
                _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported semantic project language.")
            };
        }

        /// <summary>
        /// Creates a Roslyn compilation for one loaded project.
        /// </summary>
        /// <param name="project">The project input being compiled.</param>
        /// <param name="syntaxTrees">The syntax trees loaded for the project.</param>
        /// <returns>A Roslyn compilation that can provide semantic models to WP007 extractors.</returns>
        private static Compilation CreateCompilation(SemanticProjectInput project, IReadOnlyList<SyntaxTree> syntaxTrees)
        {
            // Minimal framework references are enough for self-contained fixtures; missing external references become semantic unknowns rather than host execution.
            MetadataReference[] references =
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location)
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
        /// Builds a repository-relative path from an absolute repository-contained path.
        /// </summary>
        /// <param name="repositoryRootDirectory">The absolute repository root directory.</param>
        /// <param name="absolutePath">The absolute repository-contained path.</param>
        /// <returns>A repository-relative path using forward slashes.</returns>
        private static string GetRepositoryRelativePath(string repositoryRootDirectory, string absolutePath)
        {
            // Repository-relative paths keep graph identity deterministic across developer workstations and test agents.
            string relativePath = Path.GetRelativePath(repositoryRootDirectory, absolutePath);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Creates the snapshot stable key used to scope WP007 graph contributions during API orchestration.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory for the extraction run.</param>
        /// <param name="runId">The accepted run identifier that distinguishes extraction snapshots for the same repository.</param>
        /// <returns>A stable key with the snapshot prefix used by existing extraction stages.</returns>
        private static StableKey CreateSnapshotStableKey(string repositoryRootDirectory, string runId)
        {
            // The project stage already uses this logical snapshot shape, so WP007 facts share the same snapshot scope before assembly and persistence.
            string repositoryKey = StableKeyGenerator.ForRepository(repositoryRootDirectory).Value;
            return new StableKey($"snapshot://{repositoryKey}:{runId}");
        }

        /// <summary>
        /// Represents project language categories supported by the WP007 orchestration stage.
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
            /// Represents a project type that WP007 semantic analysis does not currently load.
            /// </summary>
            Unsupported
        }

        /// <summary>
        /// Represents one source document loaded from a repository-contained project.
        /// </summary>
        /// <param name="AbsolutePath">The absolute source path preserved for syntax-tree evidence.</param>
        /// <param name="SourceText">The source text to parse into a syntax tree.</param>
        private sealed record SemanticDocumentInput(string AbsolutePath, string SourceText);

        /// <summary>
        /// Represents one project and its loaded source documents.
        /// </summary>
        /// <param name="RelativeProjectPath">The repository-relative project path used as project context.</param>
        /// <param name="Language">The semantic project language supported by this stage.</param>
        /// <param name="Documents">The source documents loaded from the project.</param>
        private sealed record SemanticProjectInput(string RelativeProjectPath, SemanticProjectLanguage Language, IReadOnlyList<SemanticDocumentInput> Documents);
    }
}
