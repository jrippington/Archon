using System.Xml.Linq;
using Archon.Application.Extraction.Pipeline;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.AspNet.MinimalApis;
using Archon.Extractors.AspNet.Runtime;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.Extensions.Logging;

namespace Archon.Api.Extraction
{
    /// <summary>
    /// Runs the first WP008 ASP.NET Core minimal API endpoint extraction slice as part of the API-triggered extraction pipeline.
    /// </summary>
    /// <remarks>
    /// The stage is an orchestration adapter. It loads C# source documents from explicitly submitted solution files, delegates static endpoint recognition to <see cref="AspNetCoreMinimalApiEndpointExtractor" />, and merges graph-ready endpoint facts into the shared accumulator. It does not start the analyzed ASP.NET Core application, launch the Aspire AppHost, write Neo4j records directly, expose query APIs, or invoke MCP tools.
    /// </remarks>
    public sealed class Wp008AspNetCoreMinimalApiExtractionStage : IExtractionStage
    {
        /// <summary>
        /// Stores the extractor that recognizes direct ASP.NET Core minimal API endpoint mappings.
        /// </summary>
        private readonly AspNetCoreMinimalApiEndpointExtractor _extractor;

        /// <summary>
        /// Stores the extractor that recognizes C# and VB.NET console entry-point runtime facts.
        /// </summary>
        private readonly ConsoleEntryPointRuntimeExtractor _consoleEntryPointExtractor;

        /// <summary>
        /// Stores the extractor that recognizes C# worker hosted-service runtime facts.
        /// </summary>
        private readonly WorkerHostedServiceRuntimeExtractor _workerHostedServiceExtractor;

        /// <summary>
        /// Stores the extractor that recognizes C# scheduled-job, queue/topic consumer, service-host, and custom-loop runtime facts.
        /// </summary>
        private readonly NonHttpRuntimeConsumerExtractor _nonHttpRuntimeConsumerExtractor;

        /// <summary>
        /// Stores the logger used for credential-safe WP008 orchestration events.
        /// </summary>
        private readonly ILogger<Wp008AspNetCoreMinimalApiExtractionStage> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Wp008AspNetCoreMinimalApiExtractionStage" /> class.
        /// </summary>
        /// <param name="logger">The logger used for start, completion, and degraded extraction messages.</param>
        public Wp008AspNetCoreMinimalApiExtractionStage(ILogger<Wp008AspNetCoreMinimalApiExtractionStage> logger)
            : this(new AspNetCoreMinimalApiEndpointExtractor(), new ConsoleEntryPointRuntimeExtractor(), new WorkerHostedServiceRuntimeExtractor(), new NonHttpRuntimeConsumerExtractor(), logger)
        {
            // The default constructor path keeps API module registration simple while preserving extractor ownership in the ASP.NET extractor project.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Wp008AspNetCoreMinimalApiExtractionStage" /> class with explicit extractor dependencies.
        /// </summary>
        /// <param name="extractor">The extractor responsible for minimal API endpoint graph facts.</param>
        /// <param name="consoleEntryPointExtractor">The extractor responsible for console entry-point graph facts.</param>
        /// <param name="workerHostedServiceExtractor">The extractor responsible for worker hosted-service graph facts.</param>
        /// <param name="nonHttpRuntimeConsumerExtractor">The extractor responsible for scheduled-job, queue/topic consumer, service-host, and custom-loop graph facts.</param>
        /// <param name="logger">The logger used for credential-safe stage diagnostics.</param>
        public Wp008AspNetCoreMinimalApiExtractionStage(AspNetCoreMinimalApiEndpointExtractor extractor, ConsoleEntryPointRuntimeExtractor consoleEntryPointExtractor, WorkerHostedServiceRuntimeExtractor workerHostedServiceExtractor, NonHttpRuntimeConsumerExtractor nonHttpRuntimeConsumerExtractor, ILogger<Wp008AspNetCoreMinimalApiExtractionStage> logger)
        {
            // Explicit dependencies make the stage independently testable and keep host registration free of extraction logic.
            _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
            _consoleEntryPointExtractor = consoleEntryPointExtractor ?? throw new ArgumentNullException(nameof(consoleEntryPointExtractor));
            _workerHostedServiceExtractor = workerHostedServiceExtractor ?? throw new ArgumentNullException(nameof(workerHostedServiceExtractor));
            _nonHttpRuntimeConsumerExtractor = nonHttpRuntimeConsumerExtractor ?? throw new ArgumentNullException(nameof(nonHttpRuntimeConsumerExtractor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the stable stage identifier used by pipeline ordering, progress reporting, and diagnostics.
        /// </summary>
        public string StageId => "wp008-aspnet-core-minimal-api";

        /// <summary>
        /// Loads submitted C# project documents, runs the WP008 minimal API extractor, and merges endpoint facts into the shared accumulator.
        /// </summary>
        /// <param name="context">The pipeline context containing resolved repository input, accepted run state, and shared accumulation.</param>
        /// <param name="cancellationToken">The cancellation token that stops solution/project loading and endpoint extraction.</param>
        /// <returns>A successful stage result when WP008 endpoint extraction completes or degrades non-fatally.</returns>
        public async Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // WP008 extraction is non-blocking for individual solution read failures because earlier stages still own controlled solution validation.
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Starting WP008 ASP.NET Core minimal API extraction for run {RunId} with {SolutionCount} submitted solution path(s).",
                context.Run.RunId.ToString(),
                context.ResolvedInput.SolutionPaths.Count);

            List<SemanticExtractionRequest> csharpSemanticDocuments = [];
            List<SemanticExtractionRequest> runtimeSemanticDocuments = [];
            int degradedSolutionCount = 0;
            foreach (string solutionPath in context.ResolvedInput.SolutionPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    SolutionSemanticDocuments solutionDocuments = await LoadRuntimeSemanticDocumentsAsync(context.ResolvedInput.RepositoryRootDirectory, solutionPath, cancellationToken).ConfigureAwait(false);
                    csharpSemanticDocuments.AddRange(solutionDocuments.CSharpDocuments);
                    runtimeSemanticDocuments.AddRange(solutionDocuments.RuntimeDocuments);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidDataException)
                {
                    // A degraded solution context is recorded as a warning so other submitted solutions can still contribute endpoint facts.
                    degradedSolutionCount++;
                    context.Accumulation.AddWarning("WP008 ASP.NET Core minimal API extraction skipped a submitted solution because its C# project context could not be loaded.");
                    _logger.LogWarning(exception, "WP008 ASP.NET Core minimal API extraction skipped solution context for run {RunId}.", context.Run.RunId.ToString());
                }
            }

            StableKey snapshotStableKey = CreateSnapshotStableKey(context.ResolvedInput.RepositoryRootDirectory, context.Run.RunId.ToString());
            MinimalApiEndpointExtractionResult extractionResult = _extractor.Extract(new MinimalApiEndpointExtractionRequest(snapshotStableKey, csharpSemanticDocuments), cancellationToken);
            context.Accumulation.Merge(extractionResult.Snapshot);
            ConsoleEntryPointExtractionResult consoleEntryPointResult = _consoleEntryPointExtractor.Extract(new ConsoleEntryPointExtractionRequest(snapshotStableKey, runtimeSemanticDocuments), cancellationToken);
            context.Accumulation.Merge(consoleEntryPointResult.Snapshot);
            WorkerHostedServiceExtractionResult workerHostedServiceResult = _workerHostedServiceExtractor.Extract(new WorkerHostedServiceExtractionRequest(snapshotStableKey, csharpSemanticDocuments, context.Accumulation.ToSnapshot()), cancellationToken);
            context.Accumulation.Merge(workerHostedServiceResult.Snapshot);
            NonHttpRuntimeConsumerExtractionResult nonHttpRuntimeConsumerResult = _nonHttpRuntimeConsumerExtractor.Extract(new NonHttpRuntimeConsumerExtractionRequest(snapshotStableKey, csharpSemanticDocuments), cancellationToken);
            context.Accumulation.Merge(nonHttpRuntimeConsumerResult.Snapshot);

            if (degradedSolutionCount > 0)
            {
                _logger.LogWarning(
                    "WP008 ASP.NET Core minimal API extraction completed for run {RunId} with {DegradedSolutionCount} degraded solution load outcome(s).",
                    context.Run.RunId.ToString(),
                    degradedSolutionCount);
            }

            _logger.LogInformation(
                "Completed WP008 ASP.NET Core minimal API, console entry-point, worker hosted-service, and non-HTTP runtime consumer extraction for run {RunId}; inspected {CSharpDocumentCount} C# semantic document(s) and {RuntimeDocumentCount} runtime semantic document(s).",
                context.Run.RunId.ToString(),
                csharpSemanticDocuments.Count,
                runtimeSemanticDocuments.Count);
            return ExtractionStageResult.Success();
        }

        /// <summary>
        /// Loads C# and VB.NET semantic extraction requests for projects declared by one submitted solution file.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="solutionPath">The absolute submitted solution path.</param>
        /// <param name="cancellationToken">The cancellation token that stops solution, project, and source reads.</param>
        /// <returns>Semantic extraction requests for repository-contained runtime source documents.</returns>
        private static async Task<SolutionSemanticDocuments> LoadRuntimeSemanticDocumentsAsync(string repositoryRootDirectory, string solutionPath, CancellationToken cancellationToken)
        {
            // The loader intentionally mirrors existing lightweight stage loading instead of invoking MSBuild or scanning arbitrary solutions.
            List<CSharpProjectInput> projects = [];
            List<VisualBasicProjectInput> visualBasicProjects = [];
            string solutionDirectory = Path.GetDirectoryName(solutionPath) ?? repositoryRootDirectory;
            foreach (string line in await File.ReadAllLinesAsync(solutionPath, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryParseProjectLine(line, out string? projectPath) || string.IsNullOrWhiteSpace(projectPath))
                {
                    continue;
                }

                string absoluteProjectPath = Path.GetFullPath(Path.Combine(solutionDirectory, projectPath));
                string relativeProjectPath = GetRepositoryRelativePath(repositoryRootDirectory, absoluteProjectPath);
                if (projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    projects.Add(await LoadCSharpProjectAsync(repositoryRootDirectory, absoluteProjectPath, relativeProjectPath, cancellationToken).ConfigureAwait(false));
                    continue;
                }

                if (projectPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
                {
                    visualBasicProjects.Add(await LoadVisualBasicProjectAsync(repositoryRootDirectory, absoluteProjectPath, relativeProjectPath, cancellationToken).ConfigureAwait(false));
                }
            }

            List<SemanticExtractionRequest> csharpDocuments = [];
            foreach (CSharpProjectInput project in projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (project.Documents.Count == 0)
                {
                    continue;
                }

                IReadOnlyList<SyntaxTree> syntaxTrees = project.Documents.Select(static document => CSharpSyntaxTree.ParseText(document.SourceText, path: document.AbsolutePath)).ToArray();
                CSharpCompilation compilation = CreateCompilation(project, syntaxTrees);
                foreach (SyntaxTree syntaxTree in syntaxTrees)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    csharpDocuments.Add(new SemanticExtractionRequest(repositoryRootDirectory, project.RelativeProjectPath, syntaxTree.FilePath, syntaxTree, compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true)));
                }
            }

            List<SemanticExtractionRequest> runtimeDocuments = [.. csharpDocuments];
            foreach (VisualBasicProjectInput project in visualBasicProjects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (project.Documents.Count == 0)
                {
                    continue;
                }

                IReadOnlyList<SyntaxTree> syntaxTrees = project.Documents.Select(static document => VisualBasicSyntaxTree.ParseText(document.SourceText, path: document.AbsolutePath)).ToArray();
                VisualBasicCompilation compilation = CreateVisualBasicCompilation(project, syntaxTrees);
                foreach (SyntaxTree syntaxTree in syntaxTrees)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    runtimeDocuments.Add(new SemanticExtractionRequest(repositoryRootDirectory, project.RelativeProjectPath, syntaxTree.FilePath, syntaxTree, compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true)));
                }
            }

            return new SolutionSemanticDocuments(csharpDocuments, runtimeDocuments);
        }

        /// <summary>
        /// Loads one C# project file and its source documents.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="absoluteProjectPath">The absolute C# project file path.</param>
        /// <param name="relativeProjectPath">The repository-relative C# project path.</param>
        /// <param name="cancellationToken">The cancellation token that stops XML and source reads.</param>
        /// <returns>A C# project input with loaded source documents.</returns>
        private static async Task<CSharpProjectInput> LoadCSharpProjectAsync(string repositoryRootDirectory, string absoluteProjectPath, string relativeProjectPath, CancellationToken cancellationToken)
        {
            // The project file is treated as a static artifact; no restore, build target, or target application startup is executed.
            if (!File.Exists(absoluteProjectPath))
            {
                return new CSharpProjectInput(relativeProjectPath, []);
            }

            XDocument document = XDocument.Parse(await File.ReadAllTextAsync(absoluteProjectPath, cancellationToken).ConfigureAwait(false));
            string projectDirectory = Path.GetDirectoryName(absoluteProjectPath) ?? repositoryRootDirectory;
            IReadOnlyList<string> compileIncludes = ReadCompileIncludes(document);
            if (compileIncludes.Count == 0)
            {
                compileIncludes = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                    .Where(static path => !path.Contains(string.Concat(Path.DirectorySeparatorChar, "obj", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    .Select(path => Path.GetRelativePath(projectDirectory, path))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            List<CSharpDocumentInput> sourceDocuments = [];
            foreach (string compileInclude in compileIncludes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string absoluteSourcePath = Path.GetFullPath(Path.Combine(projectDirectory, compileInclude));
                if (!File.Exists(absoluteSourcePath))
                {
                    continue;
                }

                sourceDocuments.Add(new CSharpDocumentInput(absoluteSourcePath, await File.ReadAllTextAsync(absoluteSourcePath, cancellationToken).ConfigureAwait(false)));
            }

            return new CSharpProjectInput(relativeProjectPath, sourceDocuments);
        }

        /// <summary>
        /// Reads explicit compile item includes from project XML.
        /// </summary>
        /// <param name="projectDocument">The parsed project XML document.</param>
        /// <returns>Repository-local compile include strings ordered deterministically.</returns>
        private static IReadOnlyList<string> ReadCompileIncludes(XDocument projectDocument)
        {
            // Explicit Compile items keep fixtures deterministic; SDK-style defaults are discovered when no explicit items exist.
            return projectDocument.Descendants()
                .Where(static element => string.Equals(element.Name.LocalName, "Compile", StringComparison.Ordinal))
                .Select(static element => element.Attribute("Include")?.Value)
                .Where(static include => !string.IsNullOrWhiteSpace(include))
                .Select(static include => include!.Trim())
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Loads one VB.NET project file and its source documents.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="absoluteProjectPath">The absolute VB.NET project file path.</param>
        /// <param name="relativeProjectPath">The repository-relative VB.NET project path.</param>
        /// <param name="cancellationToken">The cancellation token that stops XML and source reads.</param>
        /// <returns>A VB.NET project input with loaded source documents.</returns>
        private static async Task<VisualBasicProjectInput> LoadVisualBasicProjectAsync(string repositoryRootDirectory, string absoluteProjectPath, string relativeProjectPath, CancellationToken cancellationToken)
        {
            // VB.NET project loading mirrors the C# path and treats source as static artifacts instead of invoking a build.
            if (!File.Exists(absoluteProjectPath))
            {
                return new VisualBasicProjectInput(relativeProjectPath, []);
            }

            XDocument document = XDocument.Parse(await File.ReadAllTextAsync(absoluteProjectPath, cancellationToken).ConfigureAwait(false));
            string projectDirectory = Path.GetDirectoryName(absoluteProjectPath) ?? repositoryRootDirectory;
            IReadOnlyList<string> compileIncludes = ReadCompileIncludes(document);
            if (compileIncludes.Count == 0)
            {
                compileIncludes = Directory.EnumerateFiles(projectDirectory, "*.vb", SearchOption.AllDirectories)
                    .Where(static path => !path.Contains(string.Concat(Path.DirectorySeparatorChar, "obj", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    .Select(path => Path.GetRelativePath(projectDirectory, path))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            List<VisualBasicDocumentInput> sourceDocuments = [];
            foreach (string compileInclude in compileIncludes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string absoluteSourcePath = Path.GetFullPath(Path.Combine(projectDirectory, compileInclude));
                if (!File.Exists(absoluteSourcePath))
                {
                    continue;
                }

                sourceDocuments.Add(new VisualBasicDocumentInput(absoluteSourcePath, await File.ReadAllTextAsync(absoluteSourcePath, cancellationToken).ConfigureAwait(false)));
            }

            return new VisualBasicProjectInput(relativeProjectPath, sourceDocuments);
        }

        /// <summary>
        /// Parses a Visual Studio solution project line to extract a supported C# or VB.NET project path.
        /// </summary>
        /// <param name="line">The solution-file line to parse.</param>
        /// <param name="projectPath">The parsed project path when the line declares a supported runtime project.</param>
        /// <returns><see langword="true" /> when a supported project path was parsed; otherwise, <see langword="false" />.</returns>
        private static bool TryParseProjectLine(string line, out string? projectPath)
        {
            // Solution parsing accepts C# for ASP.NET Core runtime extraction and C#/VB.NET for console entry-point extraction.
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
        /// Creates a lightweight C# compilation that can provide semantic models for the endpoint extractor.
        /// </summary>
        /// <param name="project">The C# project input being compiled.</param>
        /// <param name="syntaxTrees">The syntax trees loaded for the project.</param>
        /// <returns>A C# compilation for semantic-model access.</returns>
        private static CSharpCompilation CreateCompilation(CSharpProjectInput project, IReadOnlyList<SyntaxTree> syntaxTrees)
        {
            // Missing ASP.NET Core references are acceptable because runtime extraction uses syntax plus opportunistic symbol binding.
            MetadataReference[] references =
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location)
            ];
            return CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(project.RelativeProjectPath),
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        /// <summary>
        /// Creates a lightweight VB.NET compilation that can provide semantic models for the console entry-point extractor.
        /// </summary>
        /// <param name="project">The VB.NET project input being compiled.</param>
        /// <param name="syntaxTrees">The syntax trees loaded for the project.</param>
        /// <returns>A VB.NET compilation for semantic-model access.</returns>
        private static VisualBasicCompilation CreateVisualBasicCompilation(VisualBasicProjectInput project, IReadOnlyList<SyntaxTree> syntaxTrees)
        {
            // The console slice only needs source entry-point binding, so a minimal reference set is sufficient for tests and degraded real projects.
            MetadataReference[] references =
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location)
            ];
            return VisualBasicCompilation.Create(
                Path.GetFileNameWithoutExtension(project.RelativeProjectPath),
                syntaxTrees,
                references,
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
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
        /// Creates the snapshot stable key used to scope WP008 graph contributions during API orchestration.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory for the extraction run.</param>
        /// <param name="runId">The accepted run identifier that distinguishes extraction snapshots for the same repository.</param>
        /// <returns>A stable key with the snapshot prefix used by existing extraction stages.</returns>
        private static StableKey CreateSnapshotStableKey(string repositoryRootDirectory, string runId)
        {
            // The snapshot key mirrors the assembler so contributed facts are scoped to the same final snapshot identity.
            StableKey repositoryStableKey = StableKeyGenerator.ForRepository(NormalizeIdentitySegment(repositoryRootDirectory));
            return StableKeyGenerator.ForSummary(repositoryStableKey.Value, "ExtractionRun", runId);
        }

        /// <summary>
        /// Normalizes a filesystem path into the repository identity segment used by final snapshot assembly.
        /// </summary>
        /// <param name="value">The absolute path value to normalize.</param>
        /// <returns>A deterministic lowercase segment suitable for stable-key generation.</returns>
        private static string NormalizeIdentitySegment(string value)
        {
            // Stable keys must match the final snapshot assembler so stage contributions pass persistence scope validation.
            string trimmed = Path.TrimEndingDirectorySeparator(value).Replace('\\', '/').Trim();
            return trimmed.ToLowerInvariant();
        }

        /// <summary>
        /// Represents one source document loaded from a repository-contained C# project.
        /// </summary>
        /// <param name="AbsolutePath">The absolute source path preserved for syntax-tree evidence.</param>
        /// <param name="SourceText">The source text to parse into a syntax tree.</param>
        private sealed record CSharpDocumentInput(string AbsolutePath, string SourceText);

        /// <summary>
        /// Represents one C# project and its loaded source documents.
        /// </summary>
        /// <param name="RelativeProjectPath">The repository-relative project path used as project context.</param>
        /// <param name="Documents">The source documents loaded from the project.</param>
        private sealed record CSharpProjectInput(string RelativeProjectPath, IReadOnlyList<CSharpDocumentInput> Documents);

        /// <summary>
        /// Represents one source document loaded from a repository-contained VB.NET project.
        /// </summary>
        /// <param name="AbsolutePath">The absolute source path preserved for syntax-tree evidence.</param>
        /// <param name="SourceText">The source text to parse into a syntax tree.</param>
        private sealed record VisualBasicDocumentInput(string AbsolutePath, string SourceText);

        /// <summary>
        /// Represents one VB.NET project and its loaded source documents.
        /// </summary>
        /// <param name="RelativeProjectPath">The repository-relative project path used as project context.</param>
        /// <param name="Documents">The source documents loaded from the project.</param>
        private sealed record VisualBasicProjectInput(string RelativeProjectPath, IReadOnlyList<VisualBasicDocumentInput> Documents);

        /// <summary>
        /// Represents the semantic documents loaded from one solution for WP008 runtime extraction.
        /// </summary>
        /// <param name="CSharpDocuments">The C# semantic documents used by ASP.NET Core and console extraction.</param>
        /// <param name="RuntimeDocuments">The C# and VB.NET semantic documents used by console entry-point extraction.</param>
        private sealed record SolutionSemanticDocuments(IReadOnlyList<SemanticExtractionRequest> CSharpDocuments, IReadOnlyList<SemanticExtractionRequest> RuntimeDocuments);
    }
}
