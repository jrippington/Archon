using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Extractors.LegacyWeb
{
    /// <summary>
    /// Extracts graph-ready classic ASP.NET runtime facts from repository-contained legacy web application artifacts.
    /// </summary>
    /// <remarks>
    /// The extractor performs static artifact analysis only. It reads project XML, <c>web.config</c>, <c>Global.asax</c>, Web Forms markup, and C# source files without invoking MSBuild, loading System.Web, executing target application code, or writing directly to Neo4j.
    /// </remarks>
    public sealed class ClassicAspNetRuntimeExtractor
    {
        /// <summary>
        /// Stores the framework metadata value used for broad classic ASP.NET application facts.
        /// </summary>
        private const string ClassicFramework = "Classic ASP.NET";

        /// <summary>
        /// Stores the framework metadata value used for MVC 5 controller facts.
        /// </summary>
        private const string MvcFramework = "ASP.NET MVC 5";

        /// <summary>
        /// Stores the framework metadata value used for Web API 2 controller facts.
        /// </summary>
        private const string WebApiFramework = "ASP.NET Web API 2";

        /// <summary>
        /// Stores the runtime-kind metadata value for classic application project facts.
        /// </summary>
        private const string ClassicApplicationRuntimeKind = "ClassicAspNetApplication";

        /// <summary>
        /// Stores the explicit unknown reason for unresolved convention-based route templates.
        /// </summary>
        private const string ConventionalRouteUnknownReason = "Classic ASP.NET conventional route contains controller/action tokens and cannot be resolved to one deterministic endpoint.";

        /// <summary>
        /// Matches ASP.NET directive attributes such as <c>Inherits</c>, <c>CodeBehind</c>, and <c>CodeFile</c> in markup artifacts.
        /// </summary>
        private static readonly Regex s_directiveAttributeRegex = new("(?<name>[A-Za-z][A-Za-z0-9_]*)\\s*=\\s*\"(?<value>[^\"]*)\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Matches method declarations well enough for static legacy lifecycle and action detection without requiring a full Roslyn compilation.
        /// </summary>
        private static readonly Regex s_methodDeclarationRegex = new("(?<attributes>(?:\\s*\\[[^\\]]+\\])*)\\s*(?:public|protected|private|internal)\\s+(?:static\\s+)?(?<returnType>[A-Za-z0-9_\\.<>]+)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*\\(", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

        /// <summary>
        /// Matches class declarations with optional inheritance lists for controller, handler, and module detection.
        /// </summary>
        private static readonly Regex s_classDeclarationRegex = new("class\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*(?::\\s*(?<base>[^\\{]+))?\\{", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Extracts classic ASP.NET runtime graph facts from the supplied repository and project context.
        /// </summary>
        /// <param name="request">The request that scopes the snapshot, repository root, and classic web project path.</param>
        /// <param name="cancellationToken">A token that stops artifact traversal before or during file inspection.</param>
        /// <returns>An extraction result containing classic runtime nodes, relationships, evidence, and diagnostics.</returns>
        public ClassicAspNetRuntimeExtractionResult Extract(ClassicAspNetRuntimeExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // A single accumulator lets project, markup, source, and configuration facts de-duplicate by stable key before returning a snapshot section.
            ArgumentNullException.ThrowIfNull(request);
            ArchitectureSnapshotAccumulator accumulator = new();
            LegacyWebProjectContext context = CreateProjectContext(request);

            if (!File.Exists(context.AbsoluteProjectPath))
            {
                accumulator.AddError("Classic ASP.NET extraction could not find the requested project file.");
                return new ClassicAspNetRuntimeExtractionResult(accumulator.ToSnapshot());
            }

            IReadOnlyList<string> projectLines = ReadAllLines(context.AbsoluteProjectPath);
            IReadOnlyList<string> projectSourceFiles = ReadProjectCompileIncludes(context, projectLines);
            bool systemWebReferenceDetected = projectLines.Any(static line => line.Contains("System.Web", StringComparison.OrdinalIgnoreCase));
            bool mvcReferenceDetected = projectLines.Any(static line => line.Contains("System.Web.Mvc", StringComparison.OrdinalIgnoreCase));
            bool webApiReferenceDetected = projectLines.Any(static line => line.Contains("System.Web.Http", StringComparison.OrdinalIgnoreCase));
            string? globalAsaxPath = FindFirstArtifact(context.ProjectDirectory, "Global.asax");
            string? webConfigPath = FindFirstArtifact(context.ProjectDirectory, "Web.config");

            AccumulateProjectFact(request.SnapshotStableKey, accumulator, context, systemWebReferenceDetected, mvcReferenceDetected, webApiReferenceDetected, globalAsaxPath, webConfigPath);
            AnalyzeGlobalAsax(request.SnapshotStableKey, accumulator, context, globalAsaxPath, cancellationToken);
            AnalyzeSourceLifecycleHooks(request.SnapshotStableKey, accumulator, context, projectSourceFiles, cancellationToken);
            AnalyzeWebFormsArtifacts(request.SnapshotStableKey, accumulator, context, cancellationToken);
            AnalyzeHandlersAndModules(request.SnapshotStableKey, accumulator, context, projectSourceFiles, webConfigPath, cancellationToken);
            AnalyzeClassicControllers(request.SnapshotStableKey, accumulator, context, projectSourceFiles, cancellationToken);
            AnalyzeRouteConfiguration(request.SnapshotStableKey, accumulator, context, projectSourceFiles, cancellationToken);

            return new ClassicAspNetRuntimeExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Builds normalized project context values used for stable keys and repository-relative evidence paths.
        /// </summary>
        /// <param name="request">The extraction request supplied by the caller.</param>
        /// <returns>A normalized project context for the requested project.</returns>
        private static LegacyWebProjectContext CreateProjectContext(ClassicAspNetRuntimeExtractionRequest request)
        {
            // The context normalizes absolute and repository-relative paths once so all later stable keys use the same path representation.
            string repositoryRoot = Path.GetFullPath(request.RepositoryRootDirectory);
            string absoluteProjectPath = Path.IsPathRooted(request.ProjectPath)
                ? Path.GetFullPath(request.ProjectPath)
                : Path.GetFullPath(Path.Combine(repositoryRoot, request.ProjectPath));
            string relativeProjectPath = ToRepositoryRelativePath(repositoryRoot, absoluteProjectPath);
            string projectDirectory = Path.GetDirectoryName(absoluteProjectPath) ?? repositoryRoot;
            string displayName = Path.GetFileNameWithoutExtension(absoluteProjectPath);
            StableKey projectStableKey = new($"project://{relativeProjectPath}");
            return new LegacyWebProjectContext(repositoryRoot, absoluteProjectPath, relativeProjectPath, projectDirectory, displayName, projectStableKey);
        }

        /// <summary>
        /// Reads C# compile includes from an old-style project file, falling back to repository-contained source files when needed.
        /// </summary>
        /// <param name="context">The normalized project context.</param>
        /// <param name="projectLines">The project file lines used as a safe fallback when XML parsing fails.</param>
        /// <returns>Absolute C# source paths ordered deterministically.</returns>
        private static IReadOnlyList<string> ReadProjectCompileIncludes(LegacyWebProjectContext context, IReadOnlyList<string> projectLines)
        {
            // Classic project files are static XML artifacts; this method does not evaluate MSBuild properties or imports.
            try
            {
                XDocument document = XDocument.Load(context.AbsoluteProjectPath, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                SortedSet<string> includes = new(StringComparer.OrdinalIgnoreCase);
                foreach (XElement element in document.Descendants().Where(static element => string.Equals(element.Name.LocalName, "Compile", StringComparison.Ordinal)))
                {
                    string? include = element.Attribute("Include")?.Value;
                    if (!string.IsNullOrWhiteSpace(include))
                    {
                        includes.Add(Path.GetFullPath(Path.Combine(context.ProjectDirectory, include.Trim())));
                    }
                }

                AddDiscoveredCSharpFiles(context, includes);
                return includes.ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // The fallback below still lets source artifacts contribute facts when a malformed project file is present.
            }

            SortedSet<string> discoveredFiles = new(StringComparer.OrdinalIgnoreCase);
            AddDiscoveredCSharpFiles(context, discoveredFiles);
            return discoveredFiles.ToArray();
        }

        /// <summary>
        /// Adds repository-contained C# files under the project directory to an existing deterministic source set.
        /// </summary>
        /// <param name="context">The normalized project context.</param>
        /// <param name="sourceFiles">The source-file set receiving discovered files.</param>
        private static void AddDiscoveredCSharpFiles(LegacyWebProjectContext context, ISet<string> sourceFiles)
        {
            // Legacy projects often omit generated or conventionally included files from compact fixtures, so discovery supplements explicit Compile items.
            foreach (string sourceFile in Directory.EnumerateFiles(context.ProjectDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(static path => !path.Contains(string.Concat(Path.DirectorySeparatorChar, "obj", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)))
            {
                sourceFiles.Add(sourceFile);
            }
        }

        /// <summary>
        /// Accumulates the classic ASP.NET project-level runtime fact and primary project/configuration evidence.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving facts.</param>
        /// <param name="accumulator">The accumulator receiving project and evidence facts.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="systemWebReferenceDetected">Whether System.Web evidence was found in the project file.</param>
        /// <param name="mvcReferenceDetected">Whether MVC 5 reference evidence was found in the project file.</param>
        /// <param name="webApiReferenceDetected">Whether Web API 2 reference evidence was found in the project file.</param>
        /// <param name="globalAsaxPath">The absolute Global.asax path when present.</param>
        /// <param name="webConfigPath">The absolute Web.config path when present.</param>
        private static void AccumulateProjectFact(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, LegacyWebProjectContext context, bool systemWebReferenceDetected, bool mvcReferenceDetected, bool webApiReferenceDetected, string? globalAsaxPath, string? webConfigPath)
        {
            // Project metadata represents the legacy application boundary; more specific artifacts add child facts and relationships later.
            Dictionary<string, object?> metadataValues = new(StringComparer.Ordinal)
            {
                ["confidenceReason"] = "Classic ASP.NET application evidence detected from project references, configuration, or runtime artifacts.",
                ["detectionMode"] = "ClassicAspNetApplicationArtifacts",
                ["framework"] = ClassicFramework,
                ["mvcReferenceDetected"] = mvcReferenceDetected,
                ["runtimeKind"] = ClassicApplicationRuntimeKind,
                ["systemWebReferenceDetected"] = systemWebReferenceDetected,
                ["webApiReferenceDetected"] = webApiReferenceDetected
            };
            AddOptional(metadataValues, "globalAsaxPath", ToRepositoryRelativePathOrNull(context.RepositoryRootDirectory, globalAsaxPath));
            AddOptional(metadataValues, "webConfigPath", ToRepositoryRelativePathOrNull(context.RepositoryRootDirectory, webConfigPath));
            GraphMetadata metadata = GraphMetadata.From(metadataValues);
            EvidenceRecord projectEvidence = CreateEvidence(snapshotStableKey, context, context.AbsoluteProjectPath, EvidenceKind.ProjectFile, "ClassicAspNetProject", context.DisplayName, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, CreateBasicMetadata("ClassicAspNetApplicationArtifacts", ClassicApplicationRuntimeKind));
            accumulator.AddEvidence(projectEvidence);

            if (webConfigPath is not null)
            {
                accumulator.AddEvidence(CreateEvidence(snapshotStableKey, context, webConfigPath, EvidenceKind.Configuration, "web.config", context.DisplayName, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, CreateBasicMetadata("ClassicAspNetWebConfig", ClassicApplicationRuntimeKind)));
            }

            accumulator.AddNode(new ArchitectureNode(
                snapshotStableKey,
                context.ProjectStableKey,
                NodeKind.Project,
                context.DisplayName,
                context.DisplayName,
                context.DisplayName.ToUpperInvariant(),
                "C#",
                context.ProjectStableKey,
                null,
                KnowledgeKind.Fact,
                null,
                null,
                Confidence.High,
                UnknownState.Known,
                projectEvidence.StableKey,
                metadata,
                FingerprintGenerator.ForNode(NodeKind.Project, context.DisplayName, context.DisplayName, context.DisplayName.ToUpperInvariant(), KnowledgeKind.Fact, metadata)));
        }

        /// <summary>
        /// Accumulates Global.asax markup evidence when the application directive is present.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving facts.</param>
        /// <param name="accumulator">The accumulator receiving evidence facts.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="globalAsaxPath">The absolute Global.asax path when present.</param>
        /// <param name="cancellationToken">A token that stops file inspection.</param>
        private static void AnalyzeGlobalAsax(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, LegacyWebProjectContext context, string? globalAsaxPath, CancellationToken cancellationToken)
        {
            // Global.asax is the classic application directive and often points to the code-behind application class.
            cancellationToken.ThrowIfCancellationRequested();
            if (globalAsaxPath is null || !File.Exists(globalAsaxPath))
            {
                return;
            }

            string text = File.ReadAllText(globalAsaxPath);
            IReadOnlyDictionary<string, string> directiveAttributes = ReadDirectiveAttributes(text);
            string symbolName = directiveAttributes.TryGetValue("Inherits", out string? inherits) ? inherits : "Global.asax";
            accumulator.AddEvidence(CreateEvidence(snapshotStableKey, context, globalAsaxPath, EvidenceKind.SourceCode, symbolName, context.DisplayName, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, CreateBasicMetadata("GlobalAsaxDirective", ClassicApplicationRuntimeKind)));
        }

        /// <summary>
        /// Scans C# source files for classic ASP.NET lifecycle methods and records method facts.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="sourceFiles">The absolute C# source files to inspect.</param>
        /// <param name="cancellationToken">A token that stops source traversal.</param>
        private static void AnalyzeSourceLifecycleHooks(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, LegacyWebProjectContext context, IReadOnlyList<string> sourceFiles, CancellationToken cancellationToken)
        {
            // Lifecycle hooks are method-level runtime entry points that belong to the classic project boundary.
            foreach (string sourceFile in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(sourceFile))
                {
                    continue;
                }

                string sourceText = File.ReadAllText(sourceFile);
                foreach (Match match in s_methodDeclarationRegex.Matches(sourceText).Cast<Match>())
                {
                    string methodName = match.Groups["name"].Value;
                    if (!IsClassicLifecycleMethod(methodName))
                    {
                        continue;
                    }

                    int line = GetLineNumber(sourceText, match.Index);
                    string relativePath = ToRepositoryRelativePath(context.RepositoryRootDirectory, sourceFile);
                    GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
                    {
                        ["confidenceReason"] = "Classic ASP.NET lifecycle method detected from source declaration.",
                        ["detectionMode"] = "ClassicAspNetLifecycleMethod",
                        ["framework"] = ClassicFramework,
                        ["lifecycleHook"] = methodName,
                        ["runtimeKind"] = "ClassicAspNetLifecycleMethod"
                    });
                    StableKey methodStableKey = new($"method://{context.RelativeProjectPath}:{methodName}:{relativePath}");
                    EvidenceRecord evidence = CreateEvidence(snapshotStableKey, context, sourceFile, EvidenceKind.SourceCode, methodName, Path.GetFileName(sourceFile), KnowledgeKind.Fact, Confidence.High, UnknownState.Known, CreateBasicMetadata("ClassicAspNetLifecycleMethod", "ClassicAspNetLifecycleMethod"), line, line, CreateLinePreview(sourceText, line));
                    ArchitectureNode methodNode = new(
                        snapshotStableKey,
                        methodStableKey,
                        NodeKind.Method,
                        methodName,
                        methodName,
                        methodName.ToUpperInvariant(),
                        "C#",
                        context.ProjectStableKey,
                        context.ProjectStableKey,
                        KnowledgeKind.Fact,
                        null,
                        null,
                        Confidence.High,
                        UnknownState.Known,
                        evidence.StableKey,
                        metadata,
                        FingerprintGenerator.ForNode(NodeKind.Method, methodName, methodName, methodName.ToUpperInvariant(), KnowledgeKind.Fact, metadata));
                    ArchitectureEdge dependencyEdge = CreateEdge(snapshotStableKey, EdgeKind.DependsOn, context.ProjectStableKey, methodStableKey, evidence.StableKey, "ClassicAspNetLifecycleMethod", "ClassicAspNetLifecycleMethod", KnowledgeKind.Fact, Confidence.High, UnknownState.Known);
                    accumulator.AddEvidence(evidence).AddNode(methodNode).AddEdge(dependencyEdge);
                }
            }
        }

        /// <summary>
        /// Determines whether a method name is a supported classic ASP.NET lifecycle hook.
        /// </summary>
        /// <param name="methodName">The method name parsed from source.</param>
        /// <returns><see langword="true" /> when the method is a recognized lifecycle hook; otherwise, <see langword="false" />.</returns>
        private static bool IsClassicLifecycleMethod(string methodName)
        {
            // These hooks represent the common application, session, request, error, and shutdown lifecycle surface in Global.asax code-behind.
            return string.Equals(methodName, "Application_Start", StringComparison.Ordinal)
                || string.Equals(methodName, "Application_End", StringComparison.Ordinal)
                || string.Equals(methodName, "Application_BeginRequest", StringComparison.Ordinal)
                || string.Equals(methodName, "Application_EndRequest", StringComparison.Ordinal)
                || string.Equals(methodName, "Application_Error", StringComparison.Ordinal)
                || string.Equals(methodName, "Session_Start", StringComparison.Ordinal)
                || string.Equals(methodName, "Session_End", StringComparison.Ordinal);
        }

        /// <summary>
        /// Scans Web Forms markup artifacts and records page endpoint or user-control file facts.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="cancellationToken">A token that stops markup traversal.</param>
        private static void AnalyzeWebFormsArtifacts(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, LegacyWebProjectContext context, CancellationToken cancellationToken)
        {
            // Web Forms runtime surfaces are usually represented by markup files whose directives carry class and code-behind metadata.
            foreach (string markupPath in Directory.EnumerateFiles(context.ProjectDirectory, "*.as?x", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (markupPath.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
                {
                    AccumulateWebFormsPage(snapshotStableKey, accumulator, context, markupPath);
                }
                else if (markupPath.EndsWith(".ascx", StringComparison.OrdinalIgnoreCase))
                {
                    AccumulateWebFormsUserControl(snapshotStableKey, accumulator, context, markupPath);
                }
            }
        }

        /// <summary>
        /// Accumulates one Web Forms page as an endpoint fact backed by markup evidence.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="markupPath">The absolute page markup path.</param>
        private static void AccumulateWebFormsPage(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, LegacyWebProjectContext context, string markupPath)
        {
            // A page is an addressable runtime endpoint whose virtual path is the route-like identity in classic Web Forms.
            string text = File.ReadAllText(markupPath);
            IReadOnlyDictionary<string, string> directiveAttributes = ReadDirectiveAttributes(text);
            string virtualPath = ToProjectVirtualPath(context, markupPath);
            string handlerType = directiveAttributes.TryGetValue("Inherits", out string? inherits) ? inherits : virtualPath;
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = "Web Forms page directive detected from .aspx markup.",
                ["detectionMode"] = "WebFormsPageDirective",
                ["framework"] = ClassicFramework,
                ["handlerType"] = handlerType,
                ["routeTemplate"] = virtualPath,
                ["runtimeKind"] = "WebFormsPage"
            });
            StableKey endpointStableKey = new($"endpoint://{context.RelativeProjectPath}:WEBFORMS:{virtualPath}:{handlerType}");
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, context, markupPath, EvidenceKind.SourceCode, handlerType, virtualPath, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, CreateBasicMetadata("WebFormsPageDirective", "WebFormsPage"));
            ArchitectureNode endpointNode = new(snapshotStableKey, endpointStableKey, NodeKind.Endpoint, $"WEBFORMS {virtualPath}", $"WEBFORMS {virtualPath}", $"WEBFORMS {virtualPath}".ToUpperInvariant(), "ASP.NET", context.ProjectStableKey, context.ProjectStableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Endpoint, $"WEBFORMS {virtualPath}", $"WEBFORMS {virtualPath}", $"WEBFORMS {virtualPath}".ToUpperInvariant(), KnowledgeKind.Fact, metadata));
            ArchitectureEdge declarationEdge = CreateEdge(snapshotStableKey, EdgeKind.DeclaresEndpoint, context.ProjectStableKey, endpointStableKey, evidence.StableKey, "WebFormsPageDirective", "WebFormsPage", KnowledgeKind.Fact, Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(evidence).AddNode(endpointNode).AddEdge(declarationEdge);
        }

        /// <summary>
        /// Accumulates one Web Forms user control as a FilePath runtime artifact fact.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="markupPath">The absolute user-control markup path.</param>
        private static void AccumulateWebFormsUserControl(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, LegacyWebProjectContext context, string markupPath)
        {
            // User controls are runtime-facing markup components but not directly addressable endpoints, so FilePath is the current graph shape.
            string virtualPath = ToProjectVirtualPath(context, markupPath);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = "Web Forms user-control directive detected from .ascx markup.",
                ["detectionMode"] = "WebFormsUserControlDirective",
                ["framework"] = ClassicFramework,
                ["runtimeKind"] = "WebFormsUserControl",
                ["virtualPath"] = virtualPath
            });
            StableKey fileStableKey = new($"filepath://{context.RelativeProjectPath}:{virtualPath}");
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, context, markupPath, EvidenceKind.SourceCode, virtualPath, context.DisplayName, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, CreateBasicMetadata("WebFormsUserControlDirective", "WebFormsUserControl"));
            ArchitectureNode fileNode = new(snapshotStableKey, fileStableKey, NodeKind.FilePath, virtualPath, virtualPath, virtualPath.ToUpperInvariant(), "ASP.NET", context.ProjectStableKey, context.ProjectStableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.FilePath, virtualPath, virtualPath, virtualPath.ToUpperInvariant(), KnowledgeKind.Fact, metadata));
            accumulator.AddEvidence(evidence).AddNode(fileNode);
        }

        /// <summary>
        /// Scans source and configuration artifacts for HTTP handler and module facts.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="sourceFiles">The absolute C# source files to inspect.</param>
        /// <param name="webConfigPath">The absolute Web.config path when present.</param>
        /// <param name="cancellationToken">A token that stops source and configuration traversal.</param>
        private static void AnalyzeHandlersAndModules(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, LegacyWebProjectContext context, IReadOnlyList<string> sourceFiles, string? webConfigPath, CancellationToken cancellationToken)
        {
            // Source declarations identify handler/module types while Web.config can supply addressable handler paths.
            IReadOnlyDictionary<string, HandlerConfiguration> handlerConfigurations = ReadHandlerConfigurations(webConfigPath);
            HashSet<string> configuredModuleTypes = ReadModuleTypes(webConfigPath).ToHashSet(StringComparer.Ordinal);
            foreach (string sourceFile in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(sourceFile))
                {
                    continue;
                }

                string sourceText = File.ReadAllText(sourceFile);
                string namespaceName = ReadNamespace(sourceText);
                foreach (Match match in s_classDeclarationRegex.Matches(sourceText).Cast<Match>())
                {
                    string className = match.Groups["name"].Value;
                    string baseList = match.Groups["base"].Value;
                    string qualifiedName = string.IsNullOrWhiteSpace(namespaceName) ? className : namespaceName + "." + className;
                    if (baseList.Contains("IHttpHandler", StringComparison.Ordinal))
                    {
                        AccumulateHandlerType(snapshotStableKey, accumulator, context, sourceFile, sourceText, match.Index, qualifiedName, handlerConfigurations.TryGetValue(qualifiedName, out HandlerConfiguration? configuration) ? configuration : null);
                    }
                    else if (baseList.Contains("IHttpModule", StringComparison.Ordinal) || configuredModuleTypes.Contains(qualifiedName))
                    {
                        AccumulateModuleType(snapshotStableKey, accumulator, context, sourceFile, sourceText, match.Index, qualifiedName);
                    }
                }
            }
        }

        /// <summary>
        /// Accumulates an HTTP handler type and optional configured endpoint path.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="sourceFile">The handler source file.</param>
        /// <param name="sourceText">The handler source text.</param>
        /// <param name="classIndex">The class declaration character index.</param>
        /// <param name="qualifiedName">The qualified handler type name.</param>
        /// <param name="configuration">The optional handler configuration entry.</param>
        private static void AccumulateHandlerType(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, LegacyWebProjectContext context, string sourceFile, string sourceText, int classIndex, string qualifiedName, HandlerConfiguration? configuration)
        {
            // Handlers are represented as Type nodes with HANDLES edges to configured handler endpoints when Web.config supplies a path.
            int line = GetLineNumber(sourceText, classIndex);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = "HTTP handler type detected from IHttpHandler implementation.",
                ["detectionMode"] = "HttpHandlerSourceType",
                ["framework"] = ClassicFramework,
                ["runtimeKind"] = "HttpHandler"
            });
            StableKey typeStableKey = new($"type://{context.RelativeProjectPath}:{qualifiedName}");
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, context, sourceFile, EvidenceKind.SourceCode, qualifiedName, Path.GetFileName(sourceFile), KnowledgeKind.Fact, Confidence.High, UnknownState.Known, CreateBasicMetadata("HttpHandlerSourceType", "HttpHandler"), line, line, CreateLinePreview(sourceText, line));
            ArchitectureNode typeNode = new(snapshotStableKey, typeStableKey, NodeKind.Type, qualifiedName, qualifiedName, qualifiedName.ToUpperInvariant(), "C#", context.ProjectStableKey, context.ProjectStableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Type, qualifiedName, qualifiedName, qualifiedName.ToUpperInvariant(), KnowledgeKind.Fact, metadata));
            accumulator.AddEvidence(evidence).AddNode(typeNode);

            if (configuration is not null)
            {
                string route = NormalizeVirtualPath(configuration.Path);
                GraphMetadata endpointMetadata = GraphMetadata.From(new Dictionary<string, object?>
                {
                    ["confidenceReason"] = "HTTP handler endpoint detected from Web.config handler mapping.",
                    ["detectionMode"] = "HttpHandlerConfiguration",
                    ["framework"] = ClassicFramework,
                    ["handlerType"] = qualifiedName,
                    ["httpMethod"] = configuration.Verb,
                    ["routeTemplate"] = route,
                    ["runtimeKind"] = "HttpHandlerEndpoint"
                });
                StableKey endpointStableKey = new($"endpoint://{context.RelativeProjectPath}:HANDLER:{route}:{qualifiedName}");
                EvidenceRecord endpointEvidence = CreateEvidence(snapshotStableKey, context, configuration.ConfigurationPath, EvidenceKind.Configuration, qualifiedName, "web.config", KnowledgeKind.Fact, Confidence.High, UnknownState.Known, CreateBasicMetadata("HttpHandlerConfiguration", "HttpHandlerEndpoint"), configuration.LineNumber, configuration.LineNumber, configuration.SnippetPreview);
                ArchitectureNode endpointNode = new(snapshotStableKey, endpointStableKey, NodeKind.Endpoint, $"HANDLER {route}", $"HANDLER {route}", $"HANDLER {route}".ToUpperInvariant(), "ASP.NET", context.ProjectStableKey, typeStableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, endpointEvidence.StableKey, endpointMetadata, FingerprintGenerator.ForNode(NodeKind.Endpoint, $"HANDLER {route}", $"HANDLER {route}", $"HANDLER {route}".ToUpperInvariant(), KnowledgeKind.Fact, endpointMetadata));
                ArchitectureEdge handlesEdge = CreateEdge(snapshotStableKey, EdgeKind.Handles, typeStableKey, endpointStableKey, endpointEvidence.StableKey, "HttpHandlerConfiguration", "HttpHandlerEndpoint", KnowledgeKind.Fact, Confidence.High, UnknownState.Known);
                accumulator.AddEvidence(endpointEvidence).AddNode(endpointNode).AddEdge(handlesEdge);
            }
        }

        /// <summary>
        /// Accumulates an HTTP module type fact.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="sourceFile">The module source file.</param>
        /// <param name="sourceText">The module source text.</param>
        /// <param name="classIndex">The class declaration character index.</param>
        /// <param name="qualifiedName">The qualified module type name.</param>
        private static void AccumulateModuleType(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, LegacyWebProjectContext context, string sourceFile, string sourceText, int classIndex, string qualifiedName)
        {
            // Modules participate in request processing but are not endpoints, so the current graph contract represents them as Type facts.
            int line = GetLineNumber(sourceText, classIndex);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = "HTTP module type detected from IHttpModule implementation or Web.config module mapping.",
                ["detectionMode"] = "HttpModuleSourceType",
                ["framework"] = ClassicFramework,
                ["runtimeKind"] = "HttpModule"
            });
            StableKey typeStableKey = new($"type://{context.RelativeProjectPath}:{qualifiedName}");
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, context, sourceFile, EvidenceKind.SourceCode, qualifiedName, Path.GetFileName(sourceFile), KnowledgeKind.Fact, Confidence.High, UnknownState.Known, CreateBasicMetadata("HttpModuleSourceType", "HttpModule"), line, line, CreateLinePreview(sourceText, line));
            ArchitectureNode typeNode = new(snapshotStableKey, typeStableKey, NodeKind.Type, qualifiedName, qualifiedName, qualifiedName.ToUpperInvariant(), "C#", context.ProjectStableKey, context.ProjectStableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Type, qualifiedName, qualifiedName, qualifiedName.ToUpperInvariant(), KnowledgeKind.Fact, metadata));
            accumulator.AddEvidence(evidence).AddNode(typeNode);
        }

        /// <summary>
        /// Scans source files for MVC 5 and Web API 2 controllers and attributed action endpoints.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="sourceFiles">The absolute C# source files to inspect.</param>
        /// <param name="cancellationToken">A token that stops source traversal.</param>
        private static void AnalyzeClassicControllers(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, LegacyWebProjectContext context, IReadOnlyList<string> sourceFiles, CancellationToken cancellationToken)
        {
            // Classic controller extraction is intentionally attribute-focused so deterministic endpoints are emitted only when source routes exist.
            foreach (string sourceFile in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(sourceFile))
                {
                    continue;
                }

                string sourceText = File.ReadAllText(sourceFile);
                string namespaceName = ReadNamespace(sourceText);
                foreach (Match classMatch in s_classDeclarationRegex.Matches(sourceText).Cast<Match>())
                {
                    string className = classMatch.Groups["name"].Value;
                    string baseList = classMatch.Groups["base"].Value;
                    ClassicControllerKind? controllerKind = DetermineControllerKind(className, baseList, sourceFile);
                    if (controllerKind is null)
                    {
                        continue;
                    }

                    string qualifiedName = string.IsNullOrWhiteSpace(namespaceName) ? className : namespaceName + "." + className;
                    StableKey controllerStableKey = AccumulateController(snapshotStableKey, accumulator, context, sourceFile, sourceText, classMatch.Index, className, qualifiedName, controllerKind.Value);
                    int classBodyEnd = FindClassBodyEnd(sourceText, classMatch.Index);
                    string classBody = classBodyEnd > classMatch.Index ? sourceText[classMatch.Index..classBodyEnd] : sourceText[classMatch.Index..];
                    foreach (Match methodMatch in s_methodDeclarationRegex.Matches(classBody).Cast<Match>())
                    {
                        AccumulateControllerAction(snapshotStableKey, accumulator, context, sourceFile, sourceText, classMatch.Index + methodMatch.Index, methodMatch, controllerStableKey, className, qualifiedName, controllerKind.Value);
                    }
                }
            }
        }

        /// <summary>
        /// Accumulates a classic MVC or Web API controller node.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="sourceFile">The controller source file.</param>
        /// <param name="sourceText">The controller source text.</param>
        /// <param name="classIndex">The class declaration index.</param>
        /// <param name="className">The controller class name.</param>
        /// <param name="qualifiedName">The qualified controller type name.</param>
        /// <param name="controllerKind">The classic controller framework kind.</param>
        /// <returns>The controller stable key.</returns>
        private static StableKey AccumulateController(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, LegacyWebProjectContext context, string sourceFile, string sourceText, int classIndex, string className, string qualifiedName, ClassicControllerKind controllerKind)
        {
            // Controller nodes provide the parent declaration boundary for action endpoint facts.
            string controllerName = TrimControllerSuffix(className);
            string framework = controllerKind == ClassicControllerKind.Mvc5 ? MvcFramework : WebApiFramework;
            string runtimeKind = controllerKind == ClassicControllerKind.Mvc5 ? "Mvc5Controller" : "WebApi2Controller";
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = "Classic ASP.NET controller detected from controller naming and base type evidence.",
                ["controllerName"] = controllerName,
                ["detectionMode"] = runtimeKind,
                ["framework"] = framework,
                ["runtimeKind"] = runtimeKind
            });
            StableKey controllerStableKey = new($"controller://{context.RelativeProjectPath}:{qualifiedName}");
            int line = GetLineNumber(sourceText, classIndex);
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, context, sourceFile, EvidenceKind.SourceCode, className, qualifiedName, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, CreateFrameworkMetadata(runtimeKind, runtimeKind, framework), line, line, CreateLinePreview(sourceText, line));
            ArchitectureNode controllerNode = new(snapshotStableKey, controllerStableKey, NodeKind.Controller, className, qualifiedName, className.ToUpperInvariant(), "C#", context.ProjectStableKey, context.ProjectStableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Controller, className, qualifiedName, className.ToUpperInvariant(), KnowledgeKind.Fact, metadata));
            accumulator.AddEvidence(evidence).AddNode(controllerNode);
            return controllerStableKey;
        }

        /// <summary>
        /// Accumulates an attributed MVC 5 or Web API 2 action endpoint when deterministic route evidence exists.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="sourceFile">The action source file.</param>
        /// <param name="sourceText">The source text containing the action.</param>
        /// <param name="methodIndex">The method declaration character index.</param>
        /// <param name="methodMatch">The regex match for the method declaration.</param>
        /// <param name="controllerStableKey">The parent controller stable key.</param>
        /// <param name="className">The controller class name.</param>
        /// <param name="qualifiedControllerName">The qualified controller type name.</param>
        /// <param name="controllerKind">The classic controller framework kind.</param>
        private static void AccumulateControllerAction(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, LegacyWebProjectContext context, string sourceFile, string sourceText, int methodIndex, Match methodMatch, StableKey controllerStableKey, string className, string qualifiedControllerName, ClassicControllerKind controllerKind)
        {
            // Attribute routes are deterministic; convention-only actions are handled separately as unknown route-table facts.
            string attributes = methodMatch.Groups["attributes"].Value;
            string? routeTemplate = ReadAttributeLiteral(attributes, "Route");
            if (string.IsNullOrWhiteSpace(routeTemplate))
            {
                return;
            }

            string methodName = methodMatch.Groups["name"].Value;
            string httpMethod = ReadHttpMethod(attributes);
            string normalizedRoute = NormalizeVirtualPath(routeTemplate);
            string controllerName = TrimControllerSuffix(className);
            string framework = controllerKind == ClassicControllerKind.Mvc5 ? MvcFramework : WebApiFramework;
            string runtimeKind = controllerKind == ClassicControllerKind.Mvc5 ? "Mvc5Action" : "WebApi2Action";
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["actionName"] = methodName,
                ["confidenceReason"] = "Classic ASP.NET action endpoint detected from route and HTTP verb attributes.",
                ["controllerName"] = controllerName,
                ["detectionMode"] = runtimeKind,
                ["framework"] = framework,
                ["handlerSymbol"] = qualifiedControllerName + "." + methodName,
                ["httpMethod"] = httpMethod,
                ["routeTemplate"] = normalizedRoute,
                ["runtimeKind"] = runtimeKind
            });
            StableKey endpointStableKey = new($"endpoint://{context.RelativeProjectPath}:{httpMethod}:{normalizedRoute}:{qualifiedControllerName}.{methodName}");
            int line = GetLineNumber(sourceText, methodIndex);
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, context, sourceFile, EvidenceKind.SourceCode, methodName, qualifiedControllerName, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, CreateFrameworkMetadata(runtimeKind, runtimeKind, framework), line, line, CreateLinePreview(sourceText, line));
            ArchitectureNode endpointNode = new(snapshotStableKey, endpointStableKey, NodeKind.Endpoint, $"{httpMethod} {normalizedRoute}", $"{httpMethod} {normalizedRoute}", $"{httpMethod} {normalizedRoute}".ToUpperInvariant(), "C#", context.ProjectStableKey, controllerStableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Endpoint, $"{httpMethod} {normalizedRoute}", $"{httpMethod} {normalizedRoute}", $"{httpMethod} {normalizedRoute}".ToUpperInvariant(), KnowledgeKind.Fact, metadata));
            ArchitectureEdge declaresEdge = CreateEdge(snapshotStableKey, EdgeKind.DeclaresEndpoint, controllerStableKey, endpointStableKey, evidence.StableKey, runtimeKind, runtimeKind, KnowledgeKind.Fact, Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(evidence).AddNode(endpointNode).AddEdge(declaresEdge);
        }

        /// <summary>
        /// Scans route configuration source for convention-based route templates that must remain explicit unknown endpoint facts.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="sourceFiles">The absolute C# source files to inspect.</param>
        /// <param name="cancellationToken">A token that stops source traversal.</param>
        private static void AnalyzeRouteConfiguration(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, LegacyWebProjectContext context, IReadOnlyList<string> sourceFiles, CancellationToken cancellationToken)
        {
            // Conventional routes describe route patterns but not one concrete endpoint, so they are preserved as unknown endpoint facts.
            foreach (string sourceFile in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(sourceFile))
                {
                    continue;
                }

                string sourceText = File.ReadAllText(sourceFile);
                foreach (Match match in Regex.Matches(sourceText, "MapRoute\\s*\\([^;]*url\\s*:\\s*\"(?<route>[^\"]+)\"", RegexOptions.CultureInvariant).Cast<Match>())
                {
                    string routeTemplate = match.Groups["route"].Value;
                    if (!routeTemplate.Contains("{controller}", StringComparison.OrdinalIgnoreCase) && !routeTemplate.Contains("{action}", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AccumulateConventionalRouteUnknown(snapshotStableKey, accumulator, context, sourceFile, sourceText, match.Index, routeTemplate);
                }
            }
        }

        /// <summary>
        /// Accumulates an explicit unknown endpoint fact for one convention-based classic route pattern.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="sourceFile">The route configuration source file.</param>
        /// <param name="sourceText">The route configuration source text.</param>
        /// <param name="routeIndex">The route call character index.</param>
        /// <param name="routeTemplate">The conventional route template.</param>
        private static void AccumulateConventionalRouteUnknown(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, LegacyWebProjectContext context, string sourceFile, string sourceText, int routeIndex, string routeTemplate)
        {
            // Unknown endpoint facts keep convention route evidence visible without manufacturing controller/action combinations.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = "Classic ASP.NET conventional route table entry detected, but controller/action tokens prevent deterministic endpoint expansion.",
                ["detectionMode"] = "ClassicRouteConfiguration",
                ["framework"] = ClassicFramework,
                ["routeTemplate"] = routeTemplate,
                ["runtimeKind"] = "ClassicConventionalRoute"
            });
            StableKey endpointStableKey = new($"endpoint://{context.RelativeProjectPath}:UNKNOWN_ROUTE:{routeTemplate}");
            int line = GetLineNumber(sourceText, routeIndex);
            UnknownState unknownState = UnknownState.Unknown(ConventionalRouteUnknownReason);
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, context, sourceFile, EvidenceKind.SourceCode, "MapRoute", "RouteConfig", KnowledgeKind.Unknown, Confidence.Medium, unknownState, CreateBasicMetadata("ClassicRouteConfiguration", "ClassicConventionalRoute"), line, line, CreateLinePreview(sourceText, line));
            ArchitectureNode endpointNode = new(snapshotStableKey, endpointStableKey, NodeKind.Endpoint, $"UNKNOWN {routeTemplate}", $"UNKNOWN {routeTemplate}", $"UNKNOWN {routeTemplate}".ToUpperInvariant(), "C#", context.ProjectStableKey, context.ProjectStableKey, KnowledgeKind.Unknown, null, null, Confidence.Medium, unknownState, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Endpoint, $"UNKNOWN {routeTemplate}", $"UNKNOWN {routeTemplate}", $"UNKNOWN {routeTemplate}".ToUpperInvariant(), KnowledgeKind.Unknown, metadata));
            ArchitectureEdge declaresEdge = CreateEdge(snapshotStableKey, EdgeKind.DeclaresEndpoint, context.ProjectStableKey, endpointStableKey, evidence.StableKey, "ClassicRouteConfiguration", "ClassicConventionalRoute", KnowledgeKind.Unknown, Confidence.Medium, unknownState);
            accumulator.AddEvidence(evidence).AddNode(endpointNode).AddEdge(declaresEdge);
        }

        /// <summary>
        /// Creates a deterministic architecture edge between two stable-keyed facts.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving the edge.</param>
        /// <param name="edgeKind">The graph relationship kind.</param>
        /// <param name="sourceStableKey">The source node stable key.</param>
        /// <param name="targetStableKey">The target node stable key.</param>
        /// <param name="evidenceStableKey">The evidence stable key supporting the edge.</param>
        /// <param name="detectionMode">The detection mode metadata value.</param>
        /// <param name="runtimeKind">The runtime kind metadata value.</param>
        /// <param name="knowledgeKind">The knowledge kind for the edge.</param>
        /// <param name="confidence">The confidence value for the edge.</param>
        /// <param name="unknownState">The unknown state for the edge.</param>
        /// <returns>A deterministic architecture edge.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, EdgeKind edgeKind, StableKey sourceStableKey, StableKey targetStableKey, StableKey evidenceStableKey, string detectionMode, string runtimeKind, KnowledgeKind knowledgeKind, Confidence confidence, UnknownState unknownState)
        {
            // Stable edge keys use logical endpoint identities and relationship kind, not database-local IDs or source enumeration order.
            GraphMetadata metadata = CreateBasicMetadata(detectionMode, runtimeKind);
            StableKey edgeStableKey = new($"edge://{edgeKind.Value}:{sourceStableKey.Value}->{targetStableKey.Value}");
            return new ArchitectureEdge(snapshotStableKey, edgeStableKey, edgeKind, sourceStableKey, targetStableKey, isDirect: true, knowledgeKind, confidence, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForEdge(edgeKind, sourceStableKey, targetStableKey, isDirect: true, knowledgeKind, metadata));
        }

        /// <summary>
        /// Creates an evidence record for a repository-contained artifact.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot receiving the evidence.</param>
        /// <param name="context">The normalized project context.</param>
        /// <param name="absolutePath">The absolute evidence artifact path.</param>
        /// <param name="evidenceKind">The evidence kind for the artifact.</param>
        /// <param name="symbolName">The optional symbol or artifact name associated with evidence.</param>
        /// <param name="containingSymbol">The optional containing symbol associated with evidence.</param>
        /// <param name="knowledgeKind">The knowledge kind for the evidence.</param>
        /// <param name="confidence">The confidence value for the evidence.</param>
        /// <param name="unknownState">The unknown state for the evidence.</param>
        /// <param name="metadata">The evidence metadata.</param>
        /// <param name="startLine">The optional evidence start line.</param>
        /// <param name="endLine">The optional evidence end line.</param>
        /// <param name="snippetPreview">The optional evidence snippet preview.</param>
        /// <returns>A deterministic evidence record.</returns>
        private static EvidenceRecord CreateEvidence(StableKey snapshotStableKey, LegacyWebProjectContext context, string absolutePath, EvidenceKind evidenceKind, string? symbolName, string? containingSymbol, KnowledgeKind knowledgeKind, Confidence confidence, UnknownState unknownState, GraphMetadata metadata, int? startLine = null, int? endLine = null, string? snippetPreview = null)
        {
            // Evidence stable keys include repository-relative location and symbol context so repeated extraction collapses identical explanations.
            string relativePath = ToRepositoryRelativePath(context.RepositoryRootDirectory, absolutePath);
            string? preview = snippetPreview ?? CreateFilePreview(absolutePath);
            string? snippetHash = string.IsNullOrWhiteSpace(preview) ? null : CreateSha256Hash(preview);
            StableKey evidenceStableKey = new($"evidence://{relativePath}:{startLine?.ToString() ?? "file"}:{symbolName ?? Path.GetFileName(absolutePath)}");
            return new EvidenceRecord(snapshotStableKey, evidenceStableKey, evidenceKind, RepositoryRelativePath.Parse(relativePath), startLine, endLine, symbolName, containingSymbol, snippetHash, preview, knowledgeKind, confidence, unknownState, metadata, FingerprintGenerator.ForEvidence(evidenceKind, relativePath, startLine, endLine, symbolName, knowledgeKind, metadata));
        }

        /// <summary>
        /// Creates common metadata for evidence and relationship facts.
        /// </summary>
        /// <param name="detectionMode">The detection mode metadata value.</param>
        /// <param name="runtimeKind">The runtime kind metadata value.</param>
        /// <returns>Canonical graph metadata.</returns>
        private static GraphMetadata CreateBasicMetadata(string detectionMode, string runtimeKind)
        {
            // Basic metadata keeps supporting facts aligned with the node metadata without hiding normalized graph fields.
            return GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = detectionMode,
                ["framework"] = ClassicFramework,
                ["runtimeKind"] = runtimeKind
            });
        }

        /// <summary>
        /// Creates common metadata for facts that use a specific classic web framework value.
        /// </summary>
        /// <param name="detectionMode">The detection mode metadata value.</param>
        /// <param name="runtimeKind">The runtime kind metadata value.</param>
        /// <param name="framework">The framework metadata value.</param>
        /// <returns>Canonical graph metadata.</returns>
        private static GraphMetadata CreateFrameworkMetadata(string detectionMode, string runtimeKind, string framework)
        {
            // MVC 5 and Web API 2 facts need framework-specific evidence metadata instead of the broad Classic ASP.NET value.
            return GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = detectionMode,
                ["framework"] = framework,
                ["runtimeKind"] = runtimeKind
            });
        }

        /// <summary>
        /// Adds a metadata property only when a value exists.
        /// </summary>
        /// <param name="values">The metadata dictionary receiving the optional property.</param>
        /// <param name="key">The metadata property key.</param>
        /// <param name="value">The optional metadata value.</param>
        private static void AddOptional(IDictionary<string, object?> values, string key, string? value)
        {
            // Optional values are omitted instead of serialized as null so absence remains distinct from known empty content.
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value;
            }
        }

        /// <summary>
        /// Reads simple ASP.NET directive attributes from markup text.
        /// </summary>
        /// <param name="text">The markup text to inspect.</param>
        /// <returns>A case-insensitive dictionary of directive attribute names to values.</returns>
        private static IReadOnlyDictionary<string, string> ReadDirectiveAttributes(string text)
        {
            // Directive parsing is intentionally narrow and deterministic; malformed markup simply contributes fewer optional details.
            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in s_directiveAttributeRegex.Matches(text).Cast<Match>())
            {
                values[match.Groups["name"].Value] = match.Groups["value"].Value;
            }

            return values;
        }

        /// <summary>
        /// Reads HTTP handler mappings from Web.config.
        /// </summary>
        /// <param name="webConfigPath">The optional absolute Web.config path.</param>
        /// <returns>Handler mappings keyed by configured handler type.</returns>
        private static IReadOnlyDictionary<string, HandlerConfiguration> ReadHandlerConfigurations(string? webConfigPath)
        {
            // Web.config handler mappings provide route-like virtual paths for IHttpHandler implementations.
            Dictionary<string, HandlerConfiguration> handlers = new(StringComparer.Ordinal);
            if (webConfigPath is null || !File.Exists(webConfigPath))
            {
                return handlers;
            }

            IReadOnlyList<string> lines = ReadAllLines(webConfigPath);
            try
            {
                XDocument document = XDocument.Load(webConfigPath, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                foreach (XElement element in document.Descendants().Where(static element => string.Equals(element.Name.LocalName, "add", StringComparison.Ordinal)))
                {
                    string? type = element.Attribute("type")?.Value;
                    string? path = element.Attribute("path")?.Value;
                    if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    int lineNumber = element is System.Xml.IXmlLineInfo lineInfo && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
                    string verb = element.Attribute("verb")?.Value ?? "*";
                    string snippet = lineNumber > 0 && lineNumber <= lines.Count ? lines[lineNumber - 1].Trim() : element.ToString(SaveOptions.DisableFormatting);
                    handlers[type.Trim()] = new HandlerConfiguration(type.Trim(), path.Trim(), verb.Trim(), webConfigPath, lineNumber, snippet);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // Malformed configuration simply prevents configuration-backed handler endpoints from being emitted.
            }

            return handlers;
        }

        /// <summary>
        /// Reads HTTP module type mappings from Web.config.
        /// </summary>
        /// <param name="webConfigPath">The optional absolute Web.config path.</param>
        /// <returns>Configured module type names.</returns>
        private static IReadOnlyList<string> ReadModuleTypes(string? webConfigPath)
        {
            // Module mappings help identify request-pipeline types even when source inheritance is incomplete.
            List<string> moduleTypes = [];
            if (webConfigPath is null || !File.Exists(webConfigPath))
            {
                return moduleTypes;
            }

            try
            {
                XDocument document = XDocument.Load(webConfigPath, LoadOptions.PreserveWhitespace);
                foreach (XElement element in document.Descendants().Where(static element => string.Equals(element.Name.LocalName, "modules", StringComparison.Ordinal)).Descendants().Where(static element => string.Equals(element.Name.LocalName, "add", StringComparison.Ordinal)))
                {
                    string? type = element.Attribute("type")?.Value;
                    if (!string.IsNullOrWhiteSpace(type))
                    {
                        moduleTypes.Add(type.Trim());
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // Malformed configuration simply prevents configuration-backed module detection from being emitted.
            }

            return moduleTypes.Order(StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Reads the first namespace declaration from source text.
        /// </summary>
        /// <param name="sourceText">The source text to inspect.</param>
        /// <returns>The namespace name when present; otherwise, an empty string.</returns>
        private static string ReadNamespace(string sourceText)
        {
            // A narrow namespace parser is sufficient for test fixtures and common legacy source files without adding Roslyn to this extractor.
            Match match = Regex.Match(sourceText, "namespace\\s+(?<name>[A-Za-z_][A-Za-z0-9_\\.]*)", RegexOptions.CultureInvariant);
            return match.Success ? match.Groups["name"].Value : string.Empty;
        }

        /// <summary>
        /// Determines whether a class is a supported MVC 5 or Web API 2 controller.
        /// </summary>
        /// <param name="className">The class name parsed from source.</param>
        /// <param name="baseList">The inheritance list parsed from source.</param>
        /// <param name="sourceFile">The source file path used as a secondary framework hint.</param>
        /// <returns>The controller kind when the class is supported; otherwise, <see langword="null" />.</returns>
        private static ClassicControllerKind? DetermineControllerKind(string className, string baseList, string sourceFile)
        {
            // Base types are preferred, while folder naming gives deterministic fallback evidence for compact fixtures.
            if (baseList.Contains("ApiController", StringComparison.Ordinal) || sourceFile.Contains(string.Concat(Path.DirectorySeparatorChar, "Api", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                return className.EndsWith("Controller", StringComparison.Ordinal) ? ClassicControllerKind.WebApi2 : null;
            }

            if (baseList.Contains("Controller", StringComparison.Ordinal) || sourceFile.Contains(string.Concat(Path.DirectorySeparatorChar, "Controllers", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                return className.EndsWith("Controller", StringComparison.Ordinal) ? ClassicControllerKind.Mvc5 : null;
            }

            return null;
        }

        /// <summary>
        /// Reads a literal attribute constructor argument for a named attribute from an attribute block.
        /// </summary>
        /// <param name="attributes">The attribute block text preceding a method.</param>
        /// <param name="attributeName">The attribute name without the Attribute suffix.</param>
        /// <returns>The literal string argument when found; otherwise, <see langword="null" />.</returns>
        private static string? ReadAttributeLiteral(string attributes, string attributeName)
        {
            // Route attributes in legacy MVC/Web API commonly carry their template as the first string literal.
            Match match = Regex.Match(attributes, "\\[" + Regex.Escape(attributeName) + "(?:Attribute)?\\s*\\(\\s*\"(?<value>[^\"]+)\"", RegexOptions.CultureInvariant);
            return match.Success ? match.Groups["value"].Value : null;
        }

        /// <summary>
        /// Reads the HTTP method implied by classic MVC/Web API verb attributes.
        /// </summary>
        /// <param name="attributes">The attribute block text preceding a method.</param>
        /// <returns>The HTTP method, defaulting to GET when no supported verb attribute is present.</returns>
        private static string ReadHttpMethod(string attributes)
        {
            // GET is the safest default for simple route-attributed read actions and matches common MVC action behavior.
            if (attributes.Contains("HttpPost", StringComparison.Ordinal))
            {
                return "POST";
            }

            if (attributes.Contains("HttpPut", StringComparison.Ordinal))
            {
                return "PUT";
            }

            if (attributes.Contains("HttpDelete", StringComparison.Ordinal))
            {
                return "DELETE";
            }

            if (attributes.Contains("HttpPatch", StringComparison.Ordinal))
            {
                return "PATCH";
            }

            return "GET";
        }

        /// <summary>
        /// Trims the conventional Controller suffix from a class name.
        /// </summary>
        /// <param name="className">The controller class name.</param>
        /// <returns>The controller name without the suffix.</returns>
        private static string TrimControllerSuffix(string className)
        {
            // Controller metadata stores the human controller name separately from the full type display name.
            return className.EndsWith("Controller", StringComparison.Ordinal) ? className[..^"Controller".Length] : className;
        }

        /// <summary>
        /// Finds the approximate end of a class body by matching braces from a class declaration index.
        /// </summary>
        /// <param name="sourceText">The source text containing the class.</param>
        /// <param name="classIndex">The class declaration character index.</param>
        /// <returns>The exclusive class body end index when found; otherwise, the source length.</returns>
        private static int FindClassBodyEnd(string sourceText, int classIndex)
        {
            // The brace scan keeps action-method matching inside the controller body for compact fixture source.
            int openBrace = sourceText.IndexOf('{', classIndex);
            if (openBrace < 0)
            {
                return sourceText.Length;
            }

            int depth = 0;
            for (int i = openBrace; i < sourceText.Length; i++)
            {
                if (sourceText[i] == '{')
                {
                    depth++;
                }
                else if (sourceText[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i + 1;
                    }
                }
            }

            return sourceText.Length;
        }

        /// <summary>
        /// Converts an artifact path into a classic ASP.NET virtual path relative to the project root.
        /// </summary>
        /// <param name="context">The normalized project context.</param>
        /// <param name="absolutePath">The absolute artifact path.</param>
        /// <returns>A forward-slash virtual path with a leading slash.</returns>
        private static string ToProjectVirtualPath(LegacyWebProjectContext context, string absolutePath)
        {
            // Virtual paths are the route-like identity for legacy markup and handler artifacts.
            return "/" + Path.GetRelativePath(context.ProjectDirectory, absolutePath).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Normalizes a Web.config handler path into a leading-slash virtual path.
        /// </summary>
        /// <param name="path">The configured path value.</param>
        /// <returns>A normalized virtual path.</returns>
        private static string NormalizeVirtualPath(string path)
        {
            // Handler paths in configuration often omit the leading slash, but endpoint display and stable keys use one form.
            string trimmed = path.Trim().Replace('\\', '/');
            return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed;
        }

        /// <summary>
        /// Finds the first artifact with the given file name under a project directory.
        /// </summary>
        /// <param name="projectDirectory">The project directory to search.</param>
        /// <param name="fileName">The file name to find.</param>
        /// <returns>The absolute artifact path when found; otherwise, <see langword="null" />.</returns>
        private static string? FindFirstArtifact(string projectDirectory, string fileName)
        {
            // A deterministic ordinal-ignore-case order avoids machine-specific file enumeration differences.
            return Directory.EnumerateFiles(projectDirectory, fileName, SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        /// <summary>
        /// Reads all lines from a file, returning an empty collection when the file cannot be read.
        /// </summary>
        /// <param name="absolutePath">The absolute file path to read.</param>
        /// <returns>The file lines or an empty collection.</returns>
        private static IReadOnlyList<string> ReadAllLines(string absolutePath)
        {
            // Controlled empty fallback lets extraction degrade without throwing for routine file issues.
            try
            {
                return File.ReadAllLines(absolutePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        /// <summary>
        /// Creates a short preview from the first non-empty file line.
        /// </summary>
        /// <param name="absolutePath">The absolute file path to preview.</param>
        /// <returns>A bounded snippet preview or <see langword="null" />.</returns>
        private static string? CreateFilePreview(string absolutePath)
        {
            // Evidence previews should help locate an artifact without copying entire source or configuration files into metadata.
            try
            {
                return File.ReadLines(absolutePath).FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line))?.Trim();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Creates a one-line snippet preview from a source text and one-based line number.
        /// </summary>
        /// <param name="sourceText">The source text containing the line.</param>
        /// <param name="lineNumber">The one-based line number to preview.</param>
        /// <returns>A bounded line preview.</returns>
        private static string CreateLinePreview(string sourceText, int lineNumber)
        {
            // Line previews are used for method-level evidence where a full file preview would be too coarse.
            string[] lines = sourceText.Split(["\r\n", "\n"], StringSplitOptions.None);
            if (lineNumber <= 0 || lineNumber > lines.Length)
            {
                return string.Empty;
            }

            return lines[lineNumber - 1].Trim();
        }

        /// <summary>
        /// Calculates a one-based line number for an index in source text.
        /// </summary>
        /// <param name="text">The text being inspected.</param>
        /// <param name="index">The zero-based character index.</param>
        /// <returns>The one-based line number containing the index.</returns>
        private static int GetLineNumber(string text, int index)
        {
            // Counting newline characters avoids a Roslyn dependency for simple legacy text extraction.
            int line = 1;
            int boundedIndex = Math.Min(Math.Max(index, 0), text.Length);
            for (int i = 0; i < boundedIndex; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        /// <summary>
        /// Builds a repository-relative path using forward slashes.
        /// </summary>
        /// <param name="repositoryRootDirectory">The absolute repository root.</param>
        /// <param name="absolutePath">The absolute repository-contained path.</param>
        /// <returns>A repository-relative path using forward slashes.</returns>
        private static string ToRepositoryRelativePath(string repositoryRootDirectory, string absolutePath)
        {
            // Repository-relative paths keep stable keys deterministic across developer machines.
            return Path.GetRelativePath(repositoryRootDirectory, absolutePath).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Builds a repository-relative path when an optional absolute path exists.
        /// </summary>
        /// <param name="repositoryRootDirectory">The absolute repository root.</param>
        /// <param name="absolutePath">The optional absolute repository-contained path.</param>
        /// <returns>A repository-relative path, or <see langword="null" /> when no path exists.</returns>
        private static string? ToRepositoryRelativePathOrNull(string repositoryRootDirectory, string? absolutePath)
        {
            // Optional path handling keeps metadata creation concise without serializing null values.
            return string.IsNullOrWhiteSpace(absolutePath) ? null : ToRepositoryRelativePath(repositoryRootDirectory, absolutePath);
        }

        /// <summary>
        /// Creates a deterministic SHA-256 hash for snippet preview text.
        /// </summary>
        /// <param name="text">The text to hash.</param>
        /// <returns>The SHA-256 hash with the repository's standard prefix.</returns>
        private static string CreateSha256Hash(string text)
        {
            // Snippet hashes support deterministic comparison without persisting full artifact contents.
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] hash = SHA256.HashData(bytes);
            return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Stores normalized path and identity values for a classic web project.
        /// </summary>
        /// <param name="RepositoryRootDirectory">The absolute repository root directory.</param>
        /// <param name="AbsoluteProjectPath">The absolute project file path.</param>
        /// <param name="RelativeProjectPath">The repository-relative project file path.</param>
        /// <param name="ProjectDirectory">The absolute project directory.</param>
        /// <param name="DisplayName">The project display name.</param>
        /// <param name="ProjectStableKey">The deterministic project stable key.</param>
        private sealed record LegacyWebProjectContext(string RepositoryRootDirectory, string AbsoluteProjectPath, string RelativeProjectPath, string ProjectDirectory, string DisplayName, StableKey ProjectStableKey);

        /// <summary>
        /// Stores a Web.config HTTP handler mapping with source evidence details.
        /// </summary>
        /// <param name="TypeName">The configured handler type name.</param>
        /// <param name="Path">The configured handler virtual path.</param>
        /// <param name="Verb">The configured HTTP verb or wildcard.</param>
        /// <param name="ConfigurationPath">The absolute configuration file path.</param>
        /// <param name="LineNumber">The one-based configuration line number when available.</param>
        /// <param name="SnippetPreview">The configuration snippet preview.</param>
        private sealed record HandlerConfiguration(string TypeName, string Path, string Verb, string ConfigurationPath, int LineNumber, string SnippetPreview);

        /// <summary>
        /// Identifies the supported classic controller framework detected from source evidence.
        /// </summary>
        private enum ClassicControllerKind
        {
            /// <summary>
            /// Represents an ASP.NET MVC 5 controller.
            /// </summary>
            Mvc5,

            /// <summary>
            /// Represents an ASP.NET Web API 2 controller.
            /// </summary>
            WebApi2
        }
    }
}
