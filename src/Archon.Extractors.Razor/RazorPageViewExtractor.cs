using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Extractors.Ui;
using Archon.Roslyn.SemanticModel;

namespace Archon.Extractors.Razor
{
    /// <summary>
    /// Extracts WP011 Razor Pages and MVC Razor view facts from static `.cshtml` files into shared graph contracts.
    /// </summary>
    /// <remarks>
    /// The extractor performs deterministic repository-file analysis only. It does not compile Razor, start ASP.NET Core, run MVC routing, evaluate tag helpers, call endpoints, write Neo4j records, or render HTML.
    /// </remarks>
    public sealed partial class RazorPageViewExtractor
    {
        /// <summary>
        /// Extracts Razor Pages, MVC views, routes, layouts, partials, view components, tag-helper usage, forms, navigation links, model links, handler links, evidence, warnings, and unknown facts.
        /// </summary>
        /// <param name="request">The repository-scoped Razor extraction request.</param>
        /// <param name="cancellationToken">The cancellation token that stops file discovery and artifact analysis.</param>
        /// <returns>A result containing the graph-ready snapshot emitted by this extractor.</returns>
        public async Task<RazorPageViewExtractionResult> ExtractAsync(RazorPageViewExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // The extraction flow stays linear and deterministic: discover static project/artifact context, analyze artifacts, and return one accumulated snapshot.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            ArchitectureSnapshotAccumulator accumulator = new();
            IReadOnlyList<RazorProjectContext> projects = DiscoverProjects(request.RepositoryRootDirectory);
            IReadOnlyList<RazorArtifactContext> artifacts = DiscoverRazorArtifacts(request.RepositoryRootDirectory, projects);
            RazorRepositoryContext repositoryContext = BuildRepositoryContext(request.RepositoryRootDirectory, projects, artifacts);

            foreach (RazorProjectContext project in projects.Where(project => artifacts.Any(artifact => StringComparer.Ordinal.Equals(artifact.Project.RelativeProjectPath, project.RelativeProjectPath))))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateProject(request, accumulator, project);
            }

            foreach (RazorArtifactContext artifact in artifacts.Where(artifact => artifact.ArtifactKind is RazorArtifactKind.Page or RazorArtifactKind.View or RazorArtifactKind.ViewComponentView))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string content = await File.ReadAllTextAsync(artifact.AbsolutePath, cancellationToken).ConfigureAwait(false);
                AnalyzeRazorArtifact(request, accumulator, repositoryContext, artifact, content);
            }

            return new RazorPageViewExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Discovers project files that can own Razor Pages or MVC Razor artifacts.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <returns>Project contexts ordered by repository-relative path.</returns>
        private static IReadOnlyList<RazorProjectContext> DiscoverProjects(string repositoryRootDirectory)
        {
            // Project context is static metadata only; the extractor never restores, builds, or evaluates MSBuild targets.
            if (!Directory.Exists(repositoryRootDirectory))
            {
                return [];
            }

            List<RazorProjectContext> projects = [];
            foreach (string projectPath in Directory.EnumerateFiles(repositoryRootDirectory, "*.csproj", SearchOption.AllDirectories).Where(IsRepositorySourcePath).Order(StringComparer.OrdinalIgnoreCase))
            {
                ProjectMetadata metadata = ReadProjectMetadata(repositoryRootDirectory, projectPath);
                projects.Add(new RazorProjectContext(projectPath, metadata.RelativeProjectPath, metadata.ProjectName, metadata.TargetFramework));
            }

            return projects;
        }

        /// <summary>
        /// Discovers repository-contained `.cshtml` files and associates each artifact with its nearest project file.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="projects">The discovered project contexts that can own artifacts.</param>
        /// <returns>Razor artifact contexts ordered by repository-relative path.</returns>
        private static IReadOnlyList<RazorArtifactContext> DiscoverRazorArtifacts(string repositoryRootDirectory, IReadOnlyList<RazorProjectContext> projects)
        {
            // Build output folders are excluded because copied generated artifacts would duplicate source facts and destabilize evidence paths.
            if (!Directory.Exists(repositoryRootDirectory))
            {
                return [];
            }

            List<RazorArtifactContext> artifacts = [];
            foreach (string artifactPath in Directory.EnumerateFiles(repositoryRootDirectory, "*.cshtml", SearchOption.AllDirectories).Where(IsRepositorySourcePath).Order(StringComparer.OrdinalIgnoreCase))
            {
                RazorProjectContext? project = FindNearestProject(projects, artifactPath);
                if (project is null)
                {
                    continue;
                }

                string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRootDirectory, artifactPath);
                artifacts.Add(new RazorArtifactContext(project, artifactPath, relativePath, Path.GetFileNameWithoutExtension(artifactPath), ClassifyArtifact(relativePath)));
            }

            return artifacts;
        }

        /// <summary>
        /// Builds repository-wide static context for view imports, view starts, companion page models, and MVC controllers.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="projects">The discovered project contexts.</param>
        /// <param name="artifacts">The discovered Razor artifacts.</param>
        /// <returns>A context object used while analyzing individual Razor files.</returns>
        private static RazorRepositoryContext BuildRepositoryContext(string repositoryRootDirectory, IReadOnlyList<RazorProjectContext> projects, IReadOnlyList<RazorArtifactContext> artifacts)
        {
            // Context is assembled before per-file analysis so individual artifact extraction can remain simple and deterministic.
            IReadOnlyList<TagHelperImport> tagHelpers = LoadTagHelperImports(artifacts);
            IReadOnlyList<ViewStartLayout> viewStartLayouts = LoadViewStartLayouts(artifacts);
            IReadOnlyDictionary<string, CompanionPageModel> pageModels = LoadCompanionPageModels(repositoryRootDirectory, artifacts);
            IReadOnlyDictionary<string, ControllerActionIndex> controllers = LoadControllerActions(repositoryRootDirectory, projects);
            return new RazorRepositoryContext(tagHelpers, viewStartLayouts, pageModels, controllers);
        }

        /// <summary>
        /// Adds the owning project node used by Razor UI facts.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="project">The project context being represented.</param>
        private static void AccumulateProject(RazorPageViewExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, RazorProjectContext project)
        {
            // Project nodes give Razor facts stable owners even when this extractor runs independently of the project inventory stage.
            StableKey projectStableKey = StableKeyGenerator.ForProject(project.RelativeProjectPath);
            EvidenceRecord projectEvidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(project.RelativeProjectPath, 1, 1, project.ProjectName), "Razor", "Application", "ProjectMetadata", Confidence.High, UnknownState.Known);
            GraphMetadata projectMetadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "ProjectMetadata",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = project.RelativeProjectPath,
                ["targetFramework"] = project.TargetFramework,
                ["uiFramework"] = "Razor"
            });
            accumulator.AddEvidence(projectEvidence);
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, projectStableKey, NodeKind.Project, project.ProjectName, project.RelativeProjectPath, project.RelativeProjectPath, "C#", projectStableKey, null, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, projectEvidence.StableKey, projectMetadata, FingerprintGenerator.ForNode(NodeKind.Project, project.ProjectName, project.RelativeProjectPath, project.RelativeProjectPath, KnowledgeKind.Fact, projectMetadata)));
        }

        /// <summary>
        /// Analyzes one Razor artifact and contributes graph facts for supported Razor Pages and MVC Razor patterns.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="repositoryContext">The repository-wide Razor context.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="content">The Razor artifact content.</param>
        private static void AnalyzeRazorArtifact(RazorPageViewExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, RazorRepositoryContext repositoryContext, RazorArtifactContext artifact, string content)
        {
            // Artifact analysis keeps static and dynamic shapes separate so unsupported runtime-computed values become explicit unknown facts rather than guesses.
            RazorLine[] lines = SplitLines(content);
            string framework = GetFrameworkName(artifact);
            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            StableKey applicationStableKey = AccumulateApplication(request, accumulator, artifact.Project, projectStableKey, framework);
            EvidenceRecord artifactEvidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, 1, Math.Max(1, lines.Length), content), framework, GetArtifactKindMetadata(artifact), "StaticMarkup", Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(artifactEvidence);

            RazorModelDirective? modelDirective = ExtractModelDirective(lines);
            RouteDirective? routeDirective = ExtractPageRouteDirective(lines);
            LayoutUsage? layout = ExtractLayoutUsage(lines) ?? FindInheritedLayout(repositoryContext.ViewStartLayouts, artifact.RelativePath);
            IReadOnlyList<ComponentUsage> componentUsages = ExtractComponentUsages(lines);
            IReadOnlyList<FormUsage> formUsages = ExtractFormUsages(lines);
            IReadOnlyList<NavigationUsage> navigationUsages = ExtractNavigationUsages(lines);
            string? authorizationPolicy = ExtractAuthorizationPolicy(content);
            string? tagHelper = FindNearestTagHelper(repositoryContext.TagHelpers, artifact.RelativePath)?.TagHelperIdentity;
            ArchitectureNode artifactNode = CreateArtifactNode(request.SnapshotStableKey, artifact, projectStableKey, artifactEvidence.StableKey, framework, routeDirective, modelDirective, layout, authorizationPolicy, tagHelper);
            accumulator.AddNode(artifactNode);
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DeclaresComponent, applicationStableKey, artifactNode.StableKey, artifactEvidence.StableKey, "DeclaresRazorArtifact", artifact.RelativePath, framework, Confidence.High, UnknownState.Known));

            AccumulateRoute(request, accumulator, artifact, artifactNode, routeDirective, framework);
            AccumulateLayout(request, accumulator, artifact, artifactNode, layout, framework, artifactEvidence.StableKey);
            AccumulateModel(request, accumulator, repositoryContext, artifact, artifactNode, modelDirective, framework);
            AccumulateMvcControllerAction(request, accumulator, repositoryContext, artifact, artifactNode, framework, artifactEvidence.StableKey);

            foreach (ComponentUsage usage in componentUsages)
            {
                AccumulateComponentUsage(request, accumulator, artifact, artifactNode, usage, framework);
            }

            foreach (FormUsage usage in formUsages)
            {
                AccumulateFormUsage(request, accumulator, repositoryContext, artifact, artifactNode, usage, framework);
            }

            foreach (NavigationUsage usage in navigationUsages)
            {
                AccumulateNavigationUsage(request, accumulator, artifact, artifactNode, usage, framework);
            }
        }

        /// <summary>
        /// Adds the UI application node for the project/framework pair currently being analyzed.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="project">The project context that owns the application.</param>
        /// <param name="projectStableKey">The stable key of the owning project.</param>
        /// <param name="framework">The normalized UI framework value.</param>
        /// <returns>The stable key of the accumulated UI application node.</returns>
        private static StableKey AccumulateApplication(RazorPageViewExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, RazorProjectContext project, StableKey projectStableKey, string framework)
        {
            // Application identity includes framework and target framework so Razor Pages and MVC Razor facts can coexist in one web project.
            StableKey applicationStableKey = UiStableKeyBuilder.Create("ui-application://", projectStableKey.Value, framework, project.TargetFramework, "Server");
            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(project.RelativeProjectPath, 1, 1, project.ProjectName), framework, "Application", "ProjectMetadata", Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(evidence);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "ProjectMetadata",
                ["hostingModel"] = "Server",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = project.RelativeProjectPath,
                ["targetFramework"] = project.TargetFramework,
                ["uiArtifactKind"] = "Application",
                ["uiFramework"] = framework
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, applicationStableKey, NodeKind.UiApplication, project.ProjectName, project.RelativeProjectPath, project.ProjectName, "Razor", projectStableKey, projectStableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.UiApplication, project.ProjectName, project.RelativeProjectPath, project.ProjectName, KnowledgeKind.Fact, metadata)));
            return applicationStableKey;
        }

        /// <summary>
        /// Adds a UI route node and declaration relationship when an artifact declares or partially declares a route.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="artifactNode">The page or view node that declares the route.</param>
        /// <param name="route">The parsed route directive when present.</param>
        /// <param name="framework">The normalized UI framework value.</param>
        private static void AccumulateRoute(RazorPageViewExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, RazorArtifactContext artifact, ArchitectureNode artifactNode, RouteDirective? route, string framework)
        {
            // Razor Pages routes can be explicit literals, conventional paths, or unsupported dynamic expressions; each shape remains visible in the graph.
            if (artifact.ArtifactKind is not RazorArtifactKind.Page)
            {
                return;
            }

            RouteDirective effectiveRoute = route ?? new RouteDirective(GetConventionalPageRoute(artifact), false, 1, artifact.RelativePath);
            UnknownState unknownState = effectiveRoute.IsDynamic ? UnknownState.Unknown("Razor route template is computed from runtime state.") : UnknownState.Known;
            Confidence confidence = effectiveRoute.IsDynamic ? Confidence.Low : Confidence.High;
            string routeTemplate = effectiveRoute.RouteTemplate ?? $"unknown:{artifact.RelativePath}:{effectiveRoute.LineNumber.ToString(CultureInfo.InvariantCulture)}";
            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, effectiveRoute.LineNumber, effectiveRoute.LineNumber, effectiveRoute.SourceText), framework, "Route", "StaticMarkup", confidence, unknownState);
            accumulator.AddEvidence(evidence);
            if (effectiveRoute.IsDynamic)
            {
                accumulator.AddWarning($"Razor dynamic route in {artifact.RelativePath} on line {effectiveRoute.LineNumber.ToString(CultureInfo.InvariantCulture)} could not be statically resolved.");
            }

            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = effectiveRoute.IsDynamic ? "The @page route expression is computed from runtime state." : "The Razor Page route is statically visible.",
                ["detectionMode"] = "StaticMarkup",
                ["projectKey"] = projectStableKey.Value,
                ["routeTemplate"] = routeTemplate,
                ["sourcePath"] = artifact.RelativePath,
                ["targetFramework"] = artifact.Project.TargetFramework,
                ["uiArtifactKind"] = "Route",
                ["uiFramework"] = framework
            });
            StableKey routeStableKey = UiStableKeyBuilder.Create("ui-route://", projectStableKey.Value, framework, routeTemplate, artifact.RelativePath, effectiveRoute.LineNumber.ToString(CultureInfo.InvariantCulture));
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, routeStableKey, NodeKind.UiRoute, routeTemplate, routeTemplate, routeTemplate, "Razor", projectStableKey, artifactNode.StableKey, KnowledgeKind.Fact, null, null, confidence, unknownState, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.UiRoute, routeTemplate, routeTemplate, routeTemplate, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DeclaresUiRoute, artifactNode.StableKey, routeStableKey, evidence.StableKey, "DeclaresRazorRoute", artifact.RelativePath, framework, confidence, unknownState));
        }

        /// <summary>
        /// Adds a layout node and usage relationship when an artifact has an explicit or inherited layout.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="artifactNode">The page or view node that uses the layout.</param>
        /// <param name="layout">The detected layout usage when present.</param>
        /// <param name="framework">The normalized UI framework value.</param>
        /// <param name="artifactEvidenceStableKey">The artifact evidence stable key used when no more specific layout evidence exists.</param>
        private static void AccumulateLayout(RazorPageViewExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, RazorArtifactContext artifact, ArchitectureNode artifactNode, LayoutUsage? layout, string framework, StableKey artifactEvidenceStableKey)
        {
            // Dynamic layout names are preserved as unknown layout facts because the source still expresses a UI composition decision.
            if (layout is null)
            {
                return;
            }

            UnknownState unknownState = layout.IsDynamic ? UnknownState.Unknown("Razor layout target is computed from runtime state.") : UnknownState.Known;
            Confidence confidence = layout.IsDynamic ? Confidence.Low : Confidence.High;
            string layoutName = layout.LayoutName ?? $"Unknown Layout {layout.LineNumber.ToString(CultureInfo.InvariantCulture)}";
            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, layout.LineNumber, layout.LineNumber, layout.SourceText), framework, "Layout", "StaticMarkup", confidence, unknownState);
            accumulator.AddEvidence(evidence);
            if (layout.IsDynamic)
            {
                accumulator.AddWarning($"Razor dynamic layout in {artifact.RelativePath} on line {layout.LineNumber.ToString(CultureInfo.InvariantCulture)} could not be statically resolved.");
            }

            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            StableKey layoutStableKey = UiStableKeyBuilder.Create("ui-layout://", projectStableKey.Value, framework, layoutName, layout.IsDynamic ? artifact.RelativePath : null);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = layout.IsInherited ? "ViewStart" : "StaticMarkup",
                ["layoutName"] = layoutName,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["targetFramework"] = artifact.Project.TargetFramework,
                ["uiArtifactKind"] = "Layout",
                ["uiFramework"] = framework
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, layoutStableKey, NodeKind.UiLayout, layoutName, layoutName, layoutName, "Razor", projectStableKey, null, KnowledgeKind.Fact, null, null, confidence, unknownState, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.UiLayout, layoutName, layoutName, layoutName, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesLayout, artifactNode.StableKey, layoutStableKey, artifactEvidenceStableKey, "UsesRazorLayout", artifact.RelativePath, framework, confidence, unknownState));
        }

        /// <summary>
        /// Adds model or page-model facts and handler method facts when static evidence supports them.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="repositoryContext">The repository-wide Razor context.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="artifactNode">The page or view node being linked to model facts.</param>
        /// <param name="modelDirective">The parsed model directive when present.</param>
        /// <param name="framework">The normalized UI framework value.</param>
        private static void AccumulateModel(RazorPageViewExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, RazorRepositoryContext repositoryContext, RazorArtifactContext artifact, ArchitectureNode artifactNode, RazorModelDirective? modelDirective, string framework)
        {
            // Model directives can describe a Razor Page model or an MVC view model; unresolved page models remain explicit unknowns.
            if (modelDirective is null)
            {
                return;
            }

            bool unresolvedPageModel = artifact.ArtifactKind is RazorArtifactKind.Page && !repositoryContext.PageModels.ContainsKey(artifact.RelativePath) && !modelDirective.ModelType.Contains('.', StringComparison.Ordinal);
            UnknownState unknownState = unresolvedPageModel ? UnknownState.Unknown("Razor page model type could not be resolved statically.") : UnknownState.Known;
            Confidence confidence = unresolvedPageModel ? Confidence.Low : Confidence.High;
            if (unresolvedPageModel)
            {
                accumulator.AddWarning($"Razor unresolved page model in {artifact.RelativePath} on line {modelDirective.LineNumber.ToString(CultureInfo.InvariantCulture)} could not be statically resolved.");
            }

            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, modelDirective.LineNumber, modelDirective.LineNumber, modelDirective.SourceText), framework, "ViewModel", "StaticMarkup", confidence, unknownState);
            accumulator.AddEvidence(evidence);
            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            string modelName = modelDirective.ModelType;
            StableKey modelStableKey = UiStableKeyBuilder.Create("ui-view-model://", projectStableKey.Value, framework, artifact.RelativePath, modelName);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "StaticMarkup",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["targetFramework"] = artifact.Project.TargetFramework,
                ["uiArtifactKind"] = artifact.ArtifactKind is RazorArtifactKind.Page ? "PageModel" : "ViewModel",
                ["uiFramework"] = framework,
                ["viewModelType"] = modelName
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, modelStableKey, NodeKind.ViewModel, modelName, modelName, modelName, "C#", projectStableKey, artifactNode.StableKey, KnowledgeKind.Fact, null, null, confidence, unknownState, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.ViewModel, modelName, modelName, modelName, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesViewModel, artifactNode.StableKey, modelStableKey, evidence.StableKey, "UsesRazorModel", artifact.RelativePath, framework, confidence, unknownState));

            if (repositoryContext.PageModels.TryGetValue(artifact.RelativePath, out CompanionPageModel? companion))
            {
                foreach (HandlerMethod handler in companion.Handlers)
                {
                    AccumulateHandlerMethod(request, accumulator, artifact, artifactNode, handler, framework, evidence.StableKey);
                }
            }
        }

        /// <summary>
        /// Adds MVC controller/action facts when view path and controller source prove a deterministic relationship.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="repositoryContext">The repository-wide Razor context.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="artifactNode">The MVC view node being linked.</param>
        /// <param name="framework">The normalized UI framework value.</param>
        /// <param name="evidenceStableKey">The evidence stable key that supports the conventional view relationship.</param>
        private static void AccumulateMvcControllerAction(RazorPageViewExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, RazorRepositoryContext repositoryContext, RazorArtifactContext artifact, ArchitectureNode artifactNode, string framework, StableKey evidenceStableKey)
        {
            // Conventional MVC view correlation requires both a Views/{Controller}/{Action}.cshtml path and a matching controller action source method.
            if (!TryGetMvcControllerAndAction(artifact.RelativePath, out string? controllerName, out string? actionName))
            {
                return;
            }

            string resolvedControllerName = controllerName ?? string.Empty;
            string resolvedActionName = actionName ?? string.Empty;

            if (!repositoryContext.Controllers.TryGetValue(resolvedControllerName, out ControllerActionIndex? controller) || !controller.Actions.Contains(resolvedActionName))
            {
                return;
            }

            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            StableKey controllerStableKey = UiStableKeyBuilder.Create("mvc-controller://", projectStableKey.Value, resolvedControllerName, controller.RelativePath);
            GraphMetadata controllerMetadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["controllerName"] = resolvedControllerName,
                ["detectionMode"] = "StaticCode",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = controller.RelativePath,
                ["uiFramework"] = framework
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, controllerStableKey, NodeKind.Controller, string.Concat(resolvedControllerName, "Controller"), controller.RelativePath, resolvedControllerName, "C#", projectStableKey, null, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidenceStableKey, controllerMetadata, FingerprintGenerator.ForNode(NodeKind.Controller, string.Concat(resolvedControllerName, "Controller"), controller.RelativePath, resolvedControllerName, KnowledgeKind.Fact, controllerMetadata)));

            StableKey methodStableKey = UiStableKeyBuilder.Create("mvc-action://", projectStableKey.Value, resolvedControllerName, resolvedActionName, controller.RelativePath);
            GraphMetadata methodMetadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["controllerName"] = resolvedControllerName,
                ["detectionMode"] = "StaticCode",
                ["methodName"] = resolvedActionName,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = controller.RelativePath,
                ["uiFramework"] = framework
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, methodStableKey, NodeKind.Method, resolvedActionName, resolvedActionName, resolvedActionName, "C#", projectStableKey, controllerStableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidenceStableKey, methodMetadata, FingerprintGenerator.ForNode(NodeKind.Method, resolvedActionName, resolvedActionName, resolvedActionName, KnowledgeKind.Fact, methodMetadata)));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DependsOn, artifactNode.StableKey, methodStableKey, evidenceStableKey, "MvcViewDependsOnAction", artifact.RelativePath, framework, Confidence.High, UnknownState.Known));
        }

        /// <summary>
        /// Adds a method node and UI-event relationship for a Razor Page handler method.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor Page artifact context.</param>
        /// <param name="artifactNode">The Razor Page node that handles the UI event.</param>
        /// <param name="handler">The handler method descriptor.</param>
        /// <param name="framework">The normalized UI framework value.</param>
        /// <param name="evidenceStableKey">The evidence stable key that supports the handler link.</param>
        private static void AccumulateHandlerMethod(RazorPageViewExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, RazorArtifactContext artifact, ArchitectureNode artifactNode, HandlerMethod handler, string framework, StableKey evidenceStableKey)
        {
            // Page handlers are represented as method targets so form posts and handler methods share a queryable graph identity.
            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            StableKey methodStableKey = UiStableKeyBuilder.Create("razor-handler://", projectStableKey.Value, framework, artifact.RelativePath, handler.MethodName);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "StaticCode",
                ["eventName"] = handler.EventName,
                ["methodName"] = handler.MethodName,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = handler.RelativePath,
                ["uiArtifactKind"] = "Command",
                ["uiFramework"] = framework
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, methodStableKey, NodeKind.Method, handler.MethodName, handler.MethodName, handler.MethodName, "C#", projectStableKey, artifactNode.StableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidenceStableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Method, handler.MethodName, handler.MethodName, handler.MethodName, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdgeWithMetadata(request.SnapshotStableKey, EdgeKind.HandlesUiEvent, artifactNode.StableKey, methodStableKey, evidenceStableKey, "RazorPageHandler", artifact.RelativePath, framework, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["eventName"] = handler.EventName,
                ["methodName"] = handler.MethodName
            }));
        }

        /// <summary>
        /// Adds component usage facts for static partials, view components, or explicit unknowns for dynamic component targets.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="artifactNode">The page or view node that uses the component.</param>
        /// <param name="usage">The parsed component usage descriptor.</param>
        /// <param name="framework">The normalized UI framework value.</param>
        private static void AccumulateComponentUsage(RazorPageViewExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, RazorArtifactContext artifact, ArchitectureNode artifactNode, ComponentUsage usage, string framework)
        {
            // Dynamic partials cannot be linked to a concrete artifact, so they become unknown component targets with source evidence.
            if (usage.IsDynamic)
            {
                EvidenceRecord unknownEvidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, usage.LineNumber, usage.LineNumber, usage.SourceText), framework, "Component", "StaticMarkup", Confidence.Low, UnknownState.Unknown("Razor partial target is computed from runtime state."));
                accumulator.AddEvidence(unknownEvidence);
                accumulator.AddWarning($"Razor dynamic partial target in {artifact.RelativePath} on line {usage.LineNumber.ToString(CultureInfo.InvariantCulture)} could not be statically resolved.");
                AccumulateUnknownNode(request, accumulator, artifact, artifactNode, NodeKind.UiComponent, EdgeKind.UsesComponent, "Component", "Component", usage.LineNumber, usage.SourceText, "Razor partial target is computed from runtime state.", "UsesDynamicRazorComponent", framework, unknownEvidence.StableKey);
                return;
            }

            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, usage.LineNumber, usage.LineNumber, usage.SourceText), framework, "Component", "StaticMarkup", Confidence.Medium, UnknownState.Known);
            accumulator.AddEvidence(evidence);
            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            StableKey componentStableKey = UiStableKeyBuilder.Create("ui-component://", projectStableKey.Value, framework, usage.ComponentName);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["componentName"] = usage.ComponentName,
                ["detectionMode"] = usage.ComponentKind,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["targetFramework"] = artifact.Project.TargetFramework,
                ["uiArtifactKind"] = "Component",
                ["uiFramework"] = framework
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, componentStableKey, NodeKind.UiComponent, usage.ComponentName, usage.ComponentName, usage.ComponentName, "Razor", projectStableKey, null, KnowledgeKind.Fact, null, null, Confidence.Medium, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.UiComponent, usage.ComponentName, usage.ComponentName, usage.ComponentName, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesComponent, artifactNode.StableKey, componentStableKey, evidence.StableKey, usage.ComponentKind, artifact.RelativePath, framework, Confidence.Medium, UnknownState.Known));
        }

        /// <summary>
        /// Adds form control facts and event relationships for statically visible form posts.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="repositoryContext">The repository-wide Razor context.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="artifactNode">The page or view node that owns the form.</param>
        /// <param name="usage">The parsed form usage descriptor.</param>
        /// <param name="framework">The normalized UI framework value.</param>
        private static void AccumulateFormUsage(RazorPageViewExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, RazorRepositoryContext repositoryContext, RazorArtifactContext artifact, ArchitectureNode artifactNode, FormUsage usage, string framework)
        {
            // Forms are controls, and post targets become UI-event relationships when the target is statically visible.
            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, usage.LineNumber, usage.LineNumber, usage.SourceText), framework, "Control", "StaticMarkup", Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(evidence);
            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            StableKey formStableKey = UiStableKeyBuilder.Create("ui-control://", projectStableKey.Value, framework, "form", artifact.RelativePath, usage.LineNumber.ToString(CultureInfo.InvariantCulture));
            GraphMetadata formMetadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["controlName"] = "form",
                ["detectionMode"] = "StaticMarkup",
                ["eventName"] = usage.EventName,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Control",
                ["uiFramework"] = framework
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, formStableKey, NodeKind.UiControl, "form", "form", usage.SourceText, "Razor", projectStableKey, artifactNode.StableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidence.StableKey, formMetadata, FingerprintGenerator.ForNode(NodeKind.UiControl, "form", "form", usage.SourceText, KnowledgeKind.Fact, formMetadata)));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesControl, artifactNode.StableKey, formStableKey, evidence.StableKey, "UsesRazorForm", artifact.RelativePath, framework, Confidence.High, UnknownState.Known));

            StableKey targetStableKey = ResolveFormTargetStableKey(repositoryContext, artifact, usage, framework, projectStableKey) ?? formStableKey;
            accumulator.AddEdge(CreateEdgeWithMetadata(request.SnapshotStableKey, EdgeKind.HandlesUiEvent, artifactNode.StableKey, targetStableKey, evidence.StableKey, "RazorFormPost", artifact.RelativePath, framework, Confidence.Medium, UnknownState.Known, new Dictionary<string, object?>
            {
                ["eventName"] = usage.EventName,
                ["methodName"] = usage.HandlerName
            }));
        }

        /// <summary>
        /// Adds navigation relationships for anchor tag helpers or explicit unknowns for computed navigation targets.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="artifactNode">The page or view node that declares the navigation link.</param>
        /// <param name="usage">The parsed navigation usage descriptor.</param>
        /// <param name="framework">The normalized UI framework value.</param>
        private static void AccumulateNavigationUsage(RazorPageViewExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, RazorArtifactContext artifact, ArchitectureNode artifactNode, NavigationUsage usage, string framework)
        {
            // Literal tag-helper targets become navigation edges; expression-based targets are retained as explicit unknowns.
            if (usage.IsDynamic)
            {
                EvidenceRecord unknownEvidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, usage.LineNumber, usage.LineNumber, usage.SourceText), framework, "Route", "StaticMarkup", Confidence.Low, UnknownState.Unknown("Razor navigation target is computed from runtime state."));
                accumulator.AddEvidence(unknownEvidence);
                accumulator.AddWarning($"Razor dynamic navigation target in {artifact.RelativePath} on line {usage.LineNumber.ToString(CultureInfo.InvariantCulture)} could not be statically resolved.");
                AccumulateUnknownNode(request, accumulator, artifact, artifactNode, NodeKind.UiRoute, EdgeKind.NavigatesTo, "Route", "Navigation", usage.LineNumber, usage.SourceText, "Razor navigation target is computed from runtime state.", "NavigatesToDynamicRazorTarget", framework, unknownEvidence.StableKey);
                return;
            }

            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, usage.LineNumber, usage.LineNumber, usage.SourceText), framework, "Route", "StaticMarkup", Confidence.Medium, UnknownState.Known);
            accumulator.AddEvidence(evidence);
            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            StableKey targetStableKey = UiStableKeyBuilder.Create("ui-route://", projectStableKey.Value, framework, usage.Target, "navigation");
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "StaticMarkup",
                ["navigationTarget"] = usage.Target,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Route",
                ["uiFramework"] = framework
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, targetStableKey, NodeKind.UiRoute, usage.Target, usage.Target, usage.Target, "Razor", projectStableKey, null, KnowledgeKind.Fact, null, null, Confidence.Medium, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.UiRoute, usage.Target, usage.Target, usage.Target, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdgeWithMetadata(request.SnapshotStableKey, EdgeKind.NavigatesTo, artifactNode.StableKey, targetStableKey, evidence.StableKey, "RazorAnchorNavigation", artifact.RelativePath, framework, Confidence.Medium, UnknownState.Known, new Dictionary<string, object?>
            {
                ["navigationTarget"] = usage.Target
            }));
        }

        /// <summary>
        /// Adds a low-confidence unknown node and relationship for a dynamic or ambiguous Razor target.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="artifactNode">The node that contains the ambiguous usage.</param>
        /// <param name="nodeKind">The controlled node kind that best describes the unknown target.</param>
        /// <param name="edgeKind">The controlled edge kind that links the artifact to the unknown target.</param>
        /// <param name="artifactKind">The UI artifact kind metadata value.</param>
        /// <param name="identityPrefix">The display identity prefix for this unknown category.</param>
        /// <param name="lineNumber">The one-based source line number.</param>
        /// <param name="sourceText">The source text used for evidence context.</param>
        /// <param name="unknownReason">The human-readable reason the target could not be resolved statically.</param>
        /// <param name="relationshipRole">The extractor-specific edge role.</param>
        /// <param name="framework">The normalized UI framework value.</param>
        /// <param name="evidenceStableKey">The evidence stable key that supports the unknown fact.</param>
        private static void AccumulateUnknownNode(RazorPageViewExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, RazorArtifactContext artifact, ArchitectureNode artifactNode, NodeKind nodeKind, EdgeKind edgeKind, string artifactKind, string identityPrefix, int lineNumber, string sourceText, string unknownReason, string relationshipRole, string framework, StableKey evidenceStableKey)
        {
            // Unknown identities include the source location so multiple ambiguous patterns in one file remain distinct and deterministic.
            UnknownState unknownState = UnknownState.Unknown(unknownReason);
            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            string displayName = $"Unknown {identityPrefix}";
            StableKey unknownStableKey = UiStableKeyBuilder.Create("ui-unknown://", projectStableKey.Value, framework, artifactKind, artifact.RelativePath, lineNumber.ToString(CultureInfo.InvariantCulture), unknownReason);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = unknownReason,
                ["detectionMode"] = "StaticMarkup",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = artifactKind,
                ["uiFramework"] = framework
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, unknownStableKey, nodeKind, displayName, displayName, sourceText, "Razor", projectStableKey, artifactNode.StableKey, KnowledgeKind.Fact, null, null, Confidence.Low, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForNode(nodeKind, displayName, displayName, sourceText, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, edgeKind, artifactNode.StableKey, unknownStableKey, evidenceStableKey, relationshipRole, artifact.RelativePath, framework, Confidence.Low, unknownState));
        }

        /// <summary>
        /// Creates the UI page, UI view, or UI component node for a Razor artifact.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the node.</param>
        /// <param name="artifact">The Razor artifact context being represented.</param>
        /// <param name="projectStableKey">The stable key of the owning project.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the artifact.</param>
        /// <param name="framework">The normalized UI framework value.</param>
        /// <param name="route">The optional route directive.</param>
        /// <param name="model">The optional model directive.</param>
        /// <param name="layout">The optional layout usage.</param>
        /// <param name="authorizationPolicy">The optional authorization role or policy.</param>
        /// <param name="tagHelper">The optional imported tag-helper identity.</param>
        /// <returns>A graph node representing the Razor artifact.</returns>
        private static ArchitectureNode CreateArtifactNode(StableKey snapshotStableKey, RazorArtifactContext artifact, StableKey projectStableKey, StableKey evidenceStableKey, string framework, RouteDirective? route, RazorModelDirective? model, LayoutUsage? layout, string? authorizationPolicy, string? tagHelper)
        {
            // Artifact metadata records framework-specific details without creating framework-specific graph kinds.
            string artifactKind = GetArtifactKindMetadata(artifact);
            NodeKind nodeKind = artifact.ArtifactKind switch
            {
                RazorArtifactKind.Page => NodeKind.UiPage,
                RazorArtifactKind.ViewComponentView => NodeKind.UiComponent,
                _ => NodeKind.UiView
            };
            Dictionary<string, object?> metadataValues = new(StringComparer.Ordinal)
            {
                ["detectionMode"] = "StaticMarkup",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["targetFramework"] = artifact.Project.TargetFramework,
                ["uiArtifactKind"] = artifactKind,
                ["uiFramework"] = framework
            };
            AddOptional(metadataValues, artifact.ArtifactKind is RazorArtifactKind.Page ? "pageName" : "viewName", artifact.DisplayName);
            AddOptional(metadataValues, "routeTemplate", route?.RouteTemplate);
            AddOptional(metadataValues, "layoutName", layout?.LayoutName);
            AddOptional(metadataValues, "viewModelType", model?.ModelType);
            AddOptional(metadataValues, "authorizationPolicy", authorizationPolicy);
            AddOptional(metadataValues, "tagHelper", tagHelper);
            if (TryGetMvcControllerAndAction(artifact.RelativePath, out string? controllerName, out string? actionName))
            {
                AddOptional(metadataValues, "controllerName", controllerName);
                AddOptional(metadataValues, "methodName", actionName);
            }

            GraphMetadata metadata = GraphMetadata.From(metadataValues);
            StableKey stableKey = UiStableKeyBuilder.Create("ui-artifact://", projectStableKey.Value, framework, artifactKind, artifact.RelativePath, artifact.DisplayName);
            return new ArchitectureNode(snapshotStableKey, stableKey, nodeKind, artifact.DisplayName, artifact.RelativePath, artifact.DisplayName, "Razor", projectStableKey, null, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidenceStableKey, metadata, FingerprintGenerator.ForNode(nodeKind, artifact.DisplayName, artifact.RelativePath, artifact.DisplayName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a graph edge with deterministic metadata and stable-key generation.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the edge.</param>
        /// <param name="edgeKind">The controlled relationship kind.</param>
        /// <param name="sourceStableKey">The source node stable key.</param>
        /// <param name="targetStableKey">The target node stable key.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the relationship.</param>
        /// <param name="relationshipRole">The extractor-specific relationship role for metadata and identity.</param>
        /// <param name="sourcePath">The repository-relative artifact path that produced the relationship.</param>
        /// <param name="framework">The normalized UI framework value.</param>
        /// <param name="confidence">The confidence assigned to the edge.</param>
        /// <param name="unknownState">The unknown-state assigned to the edge.</param>
        /// <returns>A deterministic architecture edge.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, EdgeKind edgeKind, StableKey sourceStableKey, StableKey targetStableKey, StableKey evidenceStableKey, string relationshipRole, string sourcePath, string framework, Confidence confidence, UnknownState unknownState)
        {
            // Relationship identity includes endpoints, kind, role, framework, and source path so repeated directives deduplicate deterministically.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "StaticMarkup",
                ["relationshipRole"] = relationshipRole,
                ["sourcePath"] = sourcePath,
                ["uiFramework"] = framework
            });
            StableKey stableKey = UiStableKeyBuilder.Create("ui-edge://", sourceStableKey.Value, targetStableKey.Value, edgeKind.Value, relationshipRole, framework, sourcePath);
            return new ArchitectureEdge(snapshotStableKey, stableKey, edgeKind, sourceStableKey, targetStableKey, isDirect: true, KnowledgeKind.Fact, confidence, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForEdge(edgeKind, sourceStableKey, targetStableKey, isDirect: true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a graph edge with deterministic base metadata plus caller-supplied relationship metadata.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the edge.</param>
        /// <param name="edgeKind">The controlled relationship kind.</param>
        /// <param name="sourceStableKey">The source node stable key.</param>
        /// <param name="targetStableKey">The target node stable key.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the relationship.</param>
        /// <param name="relationshipRole">The extractor-specific relationship role for metadata and identity.</param>
        /// <param name="sourcePath">The repository-relative artifact path that produced the relationship.</param>
        /// <param name="framework">The normalized UI framework value.</param>
        /// <param name="confidence">The confidence assigned to the edge.</param>
        /// <param name="unknownState">The unknown-state assigned to the edge.</param>
        /// <param name="additionalMetadata">Additional relationship-specific metadata values.</param>
        /// <returns>A deterministic architecture edge.</returns>
        private static ArchitectureEdge CreateEdgeWithMetadata(StableKey snapshotStableKey, EdgeKind edgeKind, StableKey sourceStableKey, StableKey targetStableKey, StableKey evidenceStableKey, string relationshipRole, string sourcePath, string framework, Confidence confidence, UnknownState unknownState, IReadOnlyDictionary<string, object?> additionalMetadata)
        {
            // Additional metadata is folded into both fingerprinting and stable identity so different events or navigation targets remain distinct.
            Dictionary<string, object?> metadataValues = new(StringComparer.Ordinal)
            {
                ["detectionMode"] = "StaticMarkup",
                ["relationshipRole"] = relationshipRole,
                ["sourcePath"] = sourcePath,
                ["uiFramework"] = framework
            };
            foreach (KeyValuePair<string, object?> item in additionalMetadata.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                metadataValues[item.Key] = item.Value;
            }

            GraphMetadata metadata = GraphMetadata.From(metadataValues);
            StableKey stableKey = UiStableKeyBuilder.Create("ui-edge://", sourceStableKey.Value, targetStableKey.Value, edgeKind.Value, relationshipRole, framework, sourcePath, GraphMetadata.From(additionalMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)).ToCanonicalJson());
            return new ArchitectureEdge(snapshotStableKey, stableKey, edgeKind, sourceStableKey, targetStableKey, isDirect: true, KnowledgeKind.Fact, confidence, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForEdge(edgeKind, sourceStableKey, targetStableKey, isDirect: true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Reads Razor-relevant metadata from a project file.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="projectPath">The absolute project file path.</param>
        /// <returns>Static project metadata used for UI application identity and ownership.</returns>
        private static ProjectMetadata ReadProjectMetadata(string repositoryRootDirectory, string projectPath)
        {
            // XML read failures degrade to Unknown metadata because Razor artifact analysis can still proceed from repository paths.
            string relativeProjectPath = SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRootDirectory, projectPath);
            string projectName = Path.GetFileNameWithoutExtension(projectPath);
            string targetFramework = "Unknown";
            try
            {
                XDocument document = XDocument.Parse(File.ReadAllText(projectPath));
                targetFramework = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "TargetFramework")?.Value.Trim() ?? "Unknown";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // Unknown metadata is acceptable for this extractor; API-stage logging records broader orchestration diagnostics.
            }

            return new ProjectMetadata(relativeProjectPath, projectName, targetFramework);
        }

        /// <summary>
        /// Loads `_ViewImports.cshtml` tag-helper imports from discovered artifacts.
        /// </summary>
        /// <param name="artifacts">The discovered Razor artifacts.</param>
        /// <returns>Tag-helper imports ordered by path.</returns>
        private static IReadOnlyList<TagHelperImport> LoadTagHelperImports(IReadOnlyList<RazorArtifactContext> artifacts)
        {
            // View imports apply to descendant Razor artifacts, so the nearest import is selected during artifact analysis.
            List<TagHelperImport> imports = [];
            foreach (RazorArtifactContext artifact in artifacts.Where(artifact => artifact.ArtifactKind is RazorArtifactKind.ViewImports))
            {
                foreach (RazorLine line in SplitLines(File.ReadAllText(artifact.AbsolutePath)))
                {
                    Match match = AddTagHelperRegex().Match(line.Text);
                    if (match.Success)
                    {
                    imports.Add(new TagHelperImport(GetDirectoryRelativePath(artifact.RelativePath), NormalizeTagHelperIdentity(match.Groups["identity"].Value.Trim()), line.LineNumber, line.Text.Trim()));
                    }
                }
            }

            return imports.OrderBy(import => import.DirectoryPath, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>
        /// Loads `_ViewStart.cshtml` layout declarations from discovered artifacts.
        /// </summary>
        /// <param name="artifacts">The discovered Razor artifacts.</param>
        /// <returns>View-start layout declarations ordered by path.</returns>
        private static IReadOnlyList<ViewStartLayout> LoadViewStartLayouts(IReadOnlyList<RazorArtifactContext> artifacts)
        {
            // View starts provide inherited layouts for child pages and views when the artifact has no explicit layout assignment.
            List<ViewStartLayout> layouts = [];
            foreach (RazorArtifactContext artifact in artifacts.Where(artifact => artifact.ArtifactKind is RazorArtifactKind.ViewStart))
            {
                LayoutUsage? layout = ExtractLayoutUsage(SplitLines(File.ReadAllText(artifact.AbsolutePath)));
                if (layout is not null && !layout.IsDynamic && layout.LayoutName is not null)
                {
                    layouts.Add(new ViewStartLayout(GetDirectoryRelativePath(artifact.RelativePath), layout.LayoutName, layout.LineNumber, layout.SourceText));
                }
            }

            return layouts.OrderBy(layout => layout.DirectoryPath, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>
        /// Loads companion `.cshtml.cs` PageModel source files and visible handler methods.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="artifacts">The discovered Razor artifacts.</param>
        /// <returns>A dictionary keyed by repository-relative `.cshtml` path.</returns>
        private static IReadOnlyDictionary<string, CompanionPageModel> LoadCompanionPageModels(string repositoryRootDirectory, IReadOnlyList<RazorArtifactContext> artifacts)
        {
            // Companion discovery is path-based and does not compile PageModel classes; handler names remain source-visible method tokens.
            Dictionary<string, CompanionPageModel> models = new(StringComparer.Ordinal);
            foreach (RazorArtifactContext artifact in artifacts.Where(artifact => artifact.ArtifactKind is RazorArtifactKind.Page))
            {
                string companionPath = string.Concat(artifact.AbsolutePath, ".cs");
                if (!File.Exists(companionPath))
                {
                    continue;
                }

                string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRootDirectory, companionPath);
                string content = File.ReadAllText(companionPath);
                List<HandlerMethod> handlers = [];
                foreach (RazorLine line in SplitLines(content))
                {
                    foreach (Match match in HandlerMethodRegex().Matches(line.Text))
                    {
                        string methodName = match.Groups["name"].Value.Trim();
                        handlers.Add(new HandlerMethod(methodName, NormalizeHandlerEventName(methodName), relativePath, line.LineNumber, line.Text.Trim()));
                    }
                }

                models[artifact.RelativePath] = new CompanionPageModel(relativePath, handlers);
            }

            return models;
        }

        /// <summary>
        /// Loads MVC controller actions from repository-contained C# source files under discovered projects.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="projects">The discovered project contexts.</param>
        /// <returns>A dictionary keyed by controller name without the `Controller` suffix.</returns>
        private static IReadOnlyDictionary<string, ControllerActionIndex> LoadControllerActions(string repositoryRootDirectory, IReadOnlyList<RazorProjectContext> projects)
        {
            // Controller/action correlation is intentionally conservative and uses conventional class and method declarations only.
            Dictionary<string, ControllerActionIndex> controllers = new(StringComparer.Ordinal);
            foreach (RazorProjectContext project in projects)
            {
                string projectDirectory = Path.GetDirectoryName(project.AbsoluteProjectPath) ?? repositoryRootDirectory;
                foreach (string sourcePath in Directory.EnumerateFiles(projectDirectory, "*Controller.cs", SearchOption.AllDirectories).Where(IsRepositorySourcePath).Order(StringComparer.OrdinalIgnoreCase))
                {
                    string content = File.ReadAllText(sourcePath);
                    Match controllerMatch = ControllerClassRegex().Match(content);
                    if (!controllerMatch.Success)
                    {
                        continue;
                    }

                    string controllerName = controllerMatch.Groups["name"].Value.Trim();
                    string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRootDirectory, sourcePath);
                    HashSet<string> actions = PublicMethodRegex().Matches(content).Select(match => match.Groups["name"].Value.Trim()).ToHashSet(StringComparer.Ordinal);
                    controllers[controllerName] = new ControllerActionIndex(controllerName, relativePath, actions);
                }
            }

            return controllers;
        }

        /// <summary>
        /// Classifies a `.cshtml` artifact from its repository-relative path.
        /// </summary>
        /// <param name="relativePath">The repository-relative artifact path.</param>
        /// <returns>The coarse Razor artifact kind.</returns>
        private static RazorArtifactKind ClassifyArtifact(string relativePath)
        {
            // Razor Pages and MVC views have strong conventional folder names; special imports/start files are context inputs rather than primary UI artifacts.
            string fileName = Path.GetFileName(relativePath);
            if (StringComparer.OrdinalIgnoreCase.Equals(fileName, "_ViewImports.cshtml"))
            {
                return RazorArtifactKind.ViewImports;
            }

            if (StringComparer.OrdinalIgnoreCase.Equals(fileName, "_ViewStart.cshtml"))
            {
                return RazorArtifactKind.ViewStart;
            }

            string normalized = relativePath.Replace('\\', '/');
            if (normalized.Contains("/Pages/", StringComparison.OrdinalIgnoreCase))
            {
                return RazorArtifactKind.Page;
            }

            if (normalized.Contains("/Views/Shared/Components/", StringComparison.OrdinalIgnoreCase))
            {
                return RazorArtifactKind.ViewComponentView;
            }

            return RazorArtifactKind.View;
        }

        /// <summary>
        /// Finds the nearest ancestor project for a Razor artifact.
        /// </summary>
        /// <param name="projects">The candidate project contexts.</param>
        /// <param name="artifactPath">The absolute Razor artifact path.</param>
        /// <returns>The nearest owning project, or <see langword="null" /> when no project directory contains the artifact.</returns>
        private static RazorProjectContext? FindNearestProject(IReadOnlyList<RazorProjectContext> projects, string artifactPath)
        {
            // The longest project directory match models normal SDK-style project ownership for nested source folders.
            return projects
                .Where(project => IsPathUnderDirectory(artifactPath, Path.GetDirectoryName(project.AbsoluteProjectPath) ?? string.Empty))
                .OrderByDescending(project => (Path.GetDirectoryName(project.AbsoluteProjectPath) ?? string.Empty).Length)
                .FirstOrDefault();
        }

        /// <summary>
        /// Determines whether a file path is inside a directory.
        /// </summary>
        /// <param name="path">The absolute file path to test.</param>
        /// <param name="directory">The absolute directory path that may contain the file.</param>
        /// <returns><see langword="true" /> when <paramref name="path" /> is inside <paramref name="directory" />; otherwise, <see langword="false" />.</returns>
        private static bool IsPathUnderDirectory(string path, string directory)
        {
            // Full-path normalization avoids false positives from similar path prefixes.
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(path);
            string fullDirectory = string.Concat(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), Path.DirectorySeparatorChar);
            return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a discovered file path belongs to source rather than generated build output.
        /// </summary>
        /// <param name="path">The absolute candidate file path.</param>
        /// <returns><see langword="true" /> when the path should be analyzed; otherwise, <see langword="false" />.</returns>
        private static bool IsRepositorySourcePath(string path)
        {
            // Excluding standard output folders prevents duplicate facts from bin/obj copies and generated intermediate files.
            string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return !normalized.Contains(string.Concat(Path.DirectorySeparatorChar, "bin", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains(string.Concat(Path.DirectorySeparatorChar, "obj", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains(string.Concat(Path.DirectorySeparatorChar, ".git", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Splits Razor content into one-based line descriptors.
        /// </summary>
        /// <param name="content">The Razor file content.</param>
        /// <returns>Line descriptors preserving original line numbers.</returns>
        private static RazorLine[] SplitLines(string content)
        {
            // Normalizing line endings keeps directive line detection deterministic across platforms.
            return content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Select((text, index) => new RazorLine(index + 1, text)).ToArray();
        }

        /// <summary>
        /// Extracts a Razor `@model` directive from markup lines.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <returns>The model directive when present; otherwise, <see langword="null" />.</returns>
        private static RazorModelDirective? ExtractModelDirective(IReadOnlyList<RazorLine> lines)
        {
            // The first model directive is used because Razor permits one effective model declaration per artifact.
            foreach (RazorLine line in lines)
            {
                Match match = ModelDirectiveRegex().Match(line.Text);
                if (match.Success)
                {
                    return new RazorModelDirective(match.Groups["model"].Value.Trim(), line.LineNumber, line.Text.Trim());
                }
            }

            return null;
        }

        /// <summary>
        /// Extracts a Razor Pages `@page` route directive from markup lines.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <returns>The route directive when present; otherwise, <see langword="null" />.</returns>
        private static RouteDirective? ExtractPageRouteDirective(IReadOnlyList<RazorLine> lines)
        {
            // Literal route templates are captured directly; non-literal expressions become dynamic unknown routes.
            foreach (RazorLine line in lines)
            {
                Match match = PageDirectiveRegex().Match(line.Text);
                if (!match.Success)
                {
                    continue;
                }

                if (match.Groups["literal"].Success)
                {
                    return new RouteDirective(match.Groups["literal"].Value.Trim(), false, line.LineNumber, line.Text.Trim());
                }

                if (match.Groups["expression"].Success)
                {
                    return new RouteDirective(null, true, line.LineNumber, line.Text.Trim());
                }

                return new RouteDirective(null, false, line.LineNumber, line.Text.Trim());
            }

            return null;
        }

        /// <summary>
        /// Extracts an explicit layout assignment from Razor lines.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <returns>The layout usage when present; otherwise, <see langword="null" />.</returns>
        private static LayoutUsage? ExtractLayoutUsage(IReadOnlyList<RazorLine> lines)
        {
            // Literal layout assignments are resolved; expression assignments are preserved as unknown dynamic layout targets.
            foreach (RazorLine line in lines)
            {
                Match match = LayoutAssignmentRegex().Match(line.Text);
                if (!match.Success)
                {
                    continue;
                }

                if (match.Groups["literal"].Success)
                {
                    return new LayoutUsage(match.Groups["literal"].Value.Trim(), false, false, line.LineNumber, line.Text.Trim());
                }

                return new LayoutUsage(null, true, false, line.LineNumber, line.Text.Trim());
            }

            return null;
        }

        /// <summary>
        /// Finds the nearest inherited `_ViewStart` layout for an artifact.
        /// </summary>
        /// <param name="viewStartLayouts">The loaded view-start layout declarations.</param>
        /// <param name="artifactPath">The repository-relative artifact path.</param>
        /// <returns>The inherited layout usage when present; otherwise, <see langword="null" />.</returns>
        private static LayoutUsage? FindInheritedLayout(IReadOnlyList<ViewStartLayout> viewStartLayouts, string artifactPath)
        {
            // The nearest ancestor directory wins, which mirrors common Razor view-start scoping without executing Razor.
            ViewStartLayout? layout = viewStartLayouts
                .Where(candidate => IsRelativePathUnderDirectory(artifactPath, candidate.DirectoryPath))
                .OrderByDescending(candidate => candidate.DirectoryPath.Length)
                .FirstOrDefault();
            return layout is null ? null : new LayoutUsage(layout.LayoutName, false, true, layout.LineNumber, layout.SourceText);
        }

        /// <summary>
        /// Finds the nearest `_ViewImports` tag-helper import for an artifact.
        /// </summary>
        /// <param name="tagHelpers">The loaded tag-helper imports.</param>
        /// <param name="artifactPath">The repository-relative artifact path.</param>
        /// <returns>The nearest tag-helper import when present; otherwise, <see langword="null" />.</returns>
        private static TagHelperImport? FindNearestTagHelper(IReadOnlyList<TagHelperImport> tagHelpers, string artifactPath)
        {
            // Tag-helper metadata records the nearest visible import as context for form, anchor, and component-like markup.
            return tagHelpers
                .Where(candidate => IsRelativePathUnderDirectory(artifactPath, candidate.DirectoryPath))
                .OrderByDescending(candidate => candidate.DirectoryPath.Length)
                .FirstOrDefault();
        }

        /// <summary>
        /// Extracts partial and view-component usages from Razor markup lines.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <returns>Component usage descriptors in source order.</returns>
        private static IReadOnlyList<ComponentUsage> ExtractComponentUsages(IReadOnlyList<RazorLine> lines)
        {
            // Partial and view-component usages are represented as generic UI components because the graph vocabulary intentionally avoids Razor-specific node kinds.
            List<ComponentUsage> usages = [];
            foreach (RazorLine line in lines)
            {
                foreach (Match match in PartialTagRegex().Matches(line.Text))
                {
                    string name = match.Groups["name"].Value.Trim();
                    usages.Add(new ComponentUsage(name, IsDynamicExpression(name), "Partial", line.LineNumber, line.Text.Trim()));
                }

                foreach (Match match in PartialAsyncRegex().Matches(line.Text))
                {
                    if (match.Groups["literal"].Success)
                    {
                        usages.Add(new ComponentUsage(match.Groups["literal"].Value.Trim(), false, "Partial", line.LineNumber, line.Text.Trim()));
                    }
                    else
                    {
                        usages.Add(new ComponentUsage("Unknown Partial", true, "Partial", line.LineNumber, line.Text.Trim()));
                    }
                }

                foreach (Match match in ViewComponentTagRegex().Matches(line.Text))
                {
                    usages.Add(new ComponentUsage(match.Groups["name"].Value.Trim(), false, "ViewComponent", line.LineNumber, line.Text.Trim()));
                }

                foreach (Match match in ComponentInvokeRegex().Matches(line.Text))
                {
                    usages.Add(new ComponentUsage(match.Groups["name"].Value.Trim(), false, "ViewComponent", line.LineNumber, line.Text.Trim()));
                }
            }

            return usages;
        }

        /// <summary>
        /// Extracts form post usages from Razor markup lines.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <returns>Form usage descriptors in source order.</returns>
        private static IReadOnlyList<FormUsage> ExtractFormUsages(IReadOnlyList<RazorLine> lines)
        {
            // Form tag-helper attributes expose post targets directly in markup and become UI event facts.
            List<FormUsage> usages = [];
            foreach (RazorLine line in lines)
            {
                Match match = FormTagRegex().Match(line.Text);
                if (!match.Success)
                {
                    continue;
                }

                string? handler = GetAttributeValue(line.Text, "asp-page-handler");
                string? controller = GetAttributeValue(line.Text, "asp-controller");
                string? action = GetAttributeValue(line.Text, "asp-action");
                string eventName = handler is not null ? string.Concat("post:", handler) : controller is not null || action is not null ? string.Concat("post:", controller ?? "Unknown", ".", action ?? "Unknown") : "post";
                usages.Add(new FormUsage(eventName, handler ?? action ?? "post", controller, action, handler, line.LineNumber, line.Text.Trim()));
            }

            return usages;
        }

        /// <summary>
        /// Extracts anchor tag-helper navigation targets from Razor markup lines.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <returns>Navigation usage descriptors in source order.</returns>
        private static IReadOnlyList<NavigationUsage> ExtractNavigationUsages(IReadOnlyList<RazorLine> lines)
        {
            // Anchor tag helpers are recognized only when a literal page or controller/action target is visible.
            List<NavigationUsage> usages = [];
            foreach (RazorLine line in lines)
            {
                if (!AnchorTagRegex().IsMatch(line.Text))
                {
                    continue;
                }

                string? page = GetAttributeValue(line.Text, "asp-page");
                string? controller = GetAttributeValue(line.Text, "asp-controller");
                string? action = GetAttributeValue(line.Text, "asp-action");
                if (page is not null)
                {
                    usages.Add(new NavigationUsage(page, IsDynamicExpression(page), line.LineNumber, line.Text.Trim()));
                }
                else if (controller is not null || action is not null)
                {
                    string target = string.Concat(controller ?? "Unknown", ".", action ?? "Unknown");
                    usages.Add(new NavigationUsage(target, IsDynamicExpression(controller) || IsDynamicExpression(action), line.LineNumber, line.Text.Trim()));
                }
            }

            return usages;
        }

        /// <summary>
        /// Extracts an authorization role or policy marker from Razor markup.
        /// </summary>
        /// <param name="content">The Razor file content.</param>
        /// <returns>The first detected authorization role or policy value; otherwise, <see langword="null" />.</returns>
        private static string? ExtractAuthorizationPolicy(string content)
        {
            // Authorization metadata is recorded as artifact metadata and does not imply policy execution or runtime authorization success.
            Match match = AuthorizeAttributeRegex().Match(content);
            return match.Success ? match.Groups["value"].Value.Trim() : null;
        }

        /// <summary>
        /// Resolves a form event target to an existing handler or MVC action stable key when deterministic evidence exists.
        /// </summary>
        /// <param name="repositoryContext">The repository-wide Razor context.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="usage">The parsed form usage descriptor.</param>
        /// <param name="framework">The normalized UI framework value.</param>
        /// <param name="projectStableKey">The stable key of the owning project.</param>
        /// <returns>The resolved target stable key, or <see langword="null" /> when no deterministic target is available.</returns>
        private static StableKey? ResolveFormTargetStableKey(RazorRepositoryContext repositoryContext, RazorArtifactContext artifact, FormUsage usage, string framework, StableKey projectStableKey)
        {
            // Razor Page handler names and MVC controller actions use the same stable-key formula as their node creation methods.
            if (artifact.ArtifactKind is RazorArtifactKind.Page && usage.Handler is not null && repositoryContext.PageModels.TryGetValue(artifact.RelativePath, out CompanionPageModel? companion))
            {
                string handlerMethodName = string.Concat("OnPost", usage.Handler);
                if (companion.Handlers.Any(handler => StringComparer.Ordinal.Equals(handler.MethodName, handlerMethodName)))
                {
                    return UiStableKeyBuilder.Create("razor-handler://", projectStableKey.Value, framework, artifact.RelativePath, handlerMethodName);
                }
            }

            if (usage.Controller is not null && usage.Action is not null && repositoryContext.Controllers.TryGetValue(usage.Controller, out ControllerActionIndex? controller) && controller.Actions.Contains(usage.Action))
            {
                return UiStableKeyBuilder.Create("mvc-action://", projectStableKey.Value, usage.Controller, usage.Action, controller.RelativePath);
            }

            return null;
        }

        /// <summary>
        /// Gets a conventional Razor Page route from a repository-relative artifact path.
        /// </summary>
        /// <param name="artifact">The Razor Page artifact context.</param>
        /// <returns>The conventional route template.</returns>
        private static string GetConventionalPageRoute(RazorArtifactContext artifact)
        {
            // Pages/Index.cshtml maps to `/`, while nested pages use path segments under Pages without file extensions.
            string normalized = artifact.RelativePath.Replace('\\', '/');
            int pagesIndex = normalized.IndexOf("/Pages/", StringComparison.OrdinalIgnoreCase);
            string pagePath = pagesIndex >= 0 ? normalized[(pagesIndex + "/Pages/".Length)..] : Path.GetFileName(normalized);
            pagePath = pagePath.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase) ? pagePath[..^".cshtml".Length] : pagePath;
            if (StringComparer.OrdinalIgnoreCase.Equals(pagePath, "Index"))
            {
                return "/";
            }

            if (pagePath.EndsWith("/Index", StringComparison.OrdinalIgnoreCase))
            {
                pagePath = pagePath[..^"/Index".Length];
            }

            return string.Concat("/", pagePath);
        }

        /// <summary>
        /// Gets the normalized UI framework value for an artifact.
        /// </summary>
        /// <param name="artifact">The Razor artifact context.</param>
        /// <returns>`RazorPages` for page artifacts; otherwise, `MvcRazor`.</returns>
        private static string GetFrameworkName(RazorArtifactContext artifact)
        {
            // Framework is metadata, not a graph kind; it distinguishes server-rendered Razor Pages from MVC views.
            return artifact.ArtifactKind is RazorArtifactKind.Page ? "RazorPages" : "MvcRazor";
        }

        /// <summary>
        /// Gets the normalized UI artifact-kind metadata value for an artifact.
        /// </summary>
        /// <param name="artifact">The Razor artifact context.</param>
        /// <returns>The UI artifact kind metadata value.</returns>
        private static string GetArtifactKindMetadata(RazorArtifactContext artifact)
        {
            // Artifact kind follows the shared UI metadata vocabulary from WP011.
            return artifact.ArtifactKind switch
            {
                RazorArtifactKind.Page => "Page",
                RazorArtifactKind.ViewComponentView => "Component",
                _ => "View"
            };
        }

        /// <summary>
        /// Determines whether an MVC view path exposes a conventional controller/action pair.
        /// </summary>
        /// <param name="relativePath">The repository-relative view path.</param>
        /// <param name="controllerName">The inferred controller name without `Controller` when available.</param>
        /// <param name="actionName">The inferred action name when available.</param>
        /// <returns><see langword="true" /> when the path is a conventional MVC view path; otherwise, <see langword="false" />.</returns>
        private static bool TryGetMvcControllerAndAction(string relativePath, out string? controllerName, out string? actionName)
        {
            // Conventional paths use Views/{Controller}/{Action}.cshtml; shared views and component views are intentionally excluded.
            controllerName = null;
            actionName = null;
            Match match = MvcViewPathRegex().Match(relativePath.Replace('\\', '/'));
            if (!match.Success)
            {
                return false;
            }

            controllerName = match.Groups["controller"].Value.Trim();
            actionName = match.Groups["action"].Value.Trim();
            return !StringComparer.OrdinalIgnoreCase.Equals(controllerName, "Shared");
        }

        /// <summary>
        /// Gets a quoted attribute value from a markup line.
        /// </summary>
        /// <param name="text">The markup text to inspect.</param>
        /// <param name="attributeName">The attribute name to locate.</param>
        /// <returns>The attribute value when present; otherwise, <see langword="null" />.</returns>
        private static string? GetAttributeValue(string text, string attributeName)
        {
            // Attribute matching is escaped so caller-supplied names cannot alter the regex pattern.
            Match match = Regex.Match(text, string.Concat(Regex.Escape(attributeName), "\\s*=\\s*\"(?<value>[^\"]+)\""), RegexOptions.CultureInvariant);
            return match.Success ? match.Groups["value"].Value.Trim() : null;
        }

        /// <summary>
        /// Determines whether a Razor attribute value is expression-based rather than literal.
        /// </summary>
        /// <param name="value">The candidate attribute value.</param>
        /// <returns><see langword="true" /> when the value is a Razor expression; otherwise, <see langword="false" />.</returns>
        private static bool IsDynamicExpression(string? value)
        {
            // Razor expression values are runtime-computed and cannot be safely converted into concrete graph targets.
            return value is not null && value.TrimStart().StartsWith('@');
        }

        /// <summary>
        /// Gets the repository-relative directory portion of a path.
        /// </summary>
        /// <param name="relativePath">The repository-relative file path.</param>
        /// <returns>The repository-relative directory path using forward slashes.</returns>
        private static string GetDirectoryRelativePath(string relativePath)
        {
            // Directory paths use forward slashes so ancestry checks are platform independent.
            string normalized = relativePath.Replace('\\', '/');
            int index = normalized.LastIndexOf('/');
            return index < 0 ? string.Empty : normalized[..index];
        }

        /// <summary>
        /// Determines whether a repository-relative path is under a repository-relative directory.
        /// </summary>
        /// <param name="path">The repository-relative file path to test.</param>
        /// <param name="directory">The repository-relative directory path that may contain the file.</param>
        /// <returns><see langword="true" /> when <paramref name="path" /> is under <paramref name="directory" />; otherwise, <see langword="false" />.</returns>
        private static bool IsRelativePathUnderDirectory(string path, string directory)
        {
            // Relative path ancestry is used for ViewImports and ViewStart scoping.
            string normalizedPath = path.Replace('\\', '/');
            string normalizedDirectory = directory.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(normalizedDirectory))
            {
                return true;
            }

            return normalizedPath.StartsWith(string.Concat(normalizedDirectory, "/"), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Normalizes a Razor Page handler method name into an event metadata value.
        /// </summary>
        /// <param name="methodName">The handler method name.</param>
        /// <returns>The normalized handler event name.</returns>
        private static string NormalizeHandlerEventName(string methodName)
        {
            // OnPostSave becomes post:Save, while OnGet becomes get so form and handler metadata share a readable convention.
            if (methodName.StartsWith("OnPost", StringComparison.Ordinal))
            {
                string suffix = methodName["OnPost".Length..];
                return string.IsNullOrWhiteSpace(suffix) ? "post" : string.Concat("post:", suffix);
            }

            if (methodName.StartsWith("OnGet", StringComparison.Ordinal))
            {
                string suffix = methodName["OnGet".Length..];
                return string.IsNullOrWhiteSpace(suffix) ? "get" : string.Concat("get:", suffix);
            }

            return methodName;
        }

        /// <summary>
        /// Normalizes a tag-helper import directive into the package or assembly identity that contributors usually recognize.
        /// </summary>
        /// <param name="identity">The raw `_ViewImports.cshtml` tag-helper import identity.</param>
        /// <returns>The normalized tag-helper package or assembly identity.</returns>
        private static string NormalizeTagHelperIdentity(string identity)
        {
            // Directives such as `*, Microsoft.AspNetCore.Mvc.TagHelpers` combine a wildcard with the assembly identity; metadata stores the useful identity segment.
            string[] segments = identity.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 1 ? segments[^1] : identity.Trim();
        }

        /// <summary>
        /// Adds an optional metadata value when the supplied text is meaningful.
        /// </summary>
        /// <param name="values">The metadata dictionary being built.</param>
        /// <param name="key">The metadata property name.</param>
        /// <param name="value">The optional metadata value.</param>
        private static void AddOptional(Dictionary<string, object?> values, string key, string? value)
        {
            // Optional values are omitted rather than written as null to keep canonical metadata compact and intentional.
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value.Trim();
            }
        }

        /// <summary>
        /// Creates a regex for Razor `@model` directives.
        /// </summary>
        /// <returns>A regex that captures the model type token.</returns>
        [GeneratedRegex("^\\s*@model\\s+(?<model>[^\\s]+)", RegexOptions.CultureInvariant)]
        private static partial Regex ModelDirectiveRegex();

        /// <summary>
        /// Creates a regex for Razor Pages `@page` directives.
        /// </summary>
        /// <returns>A regex that captures literal or expression route values when present.</returns>
        [GeneratedRegex("^\\s*@page(?:\\s+(?:\\\"(?<literal>[^\\\"]*)\\\"|(?<expression>\\S+)))?", RegexOptions.CultureInvariant)]
        private static partial Regex PageDirectiveRegex();

        /// <summary>
        /// Creates a regex for layout assignments.
        /// </summary>
        /// <returns>A regex that captures literal or dynamic layout assignments.</returns>
        [GeneratedRegex("\\bLayout\\s*=\\s*(?:\\\"(?<literal>[^\\\"]+)\\\"|(?<dynamic>[^;]+))", RegexOptions.CultureInvariant)]
        private static partial Regex LayoutAssignmentRegex();

        /// <summary>
        /// Creates a regex for `_ViewImports` tag-helper imports.
        /// </summary>
        /// <returns>A regex that captures tag-helper import identities.</returns>
        [GeneratedRegex("^\\s*@addTagHelper\\s+(?<identity>.+)$", RegexOptions.CultureInvariant)]
        private static partial Regex AddTagHelperRegex();

        /// <summary>
        /// Creates a regex for partial tag helpers.
        /// </summary>
        /// <returns>A regex that captures partial names from `<partial>` tags.</returns>
        [GeneratedRegex("<partial\\b[^>]*\\bname\\s*=\\s*\\\"(?<name>[^\\\"]+)\\\"", RegexOptions.CultureInvariant)]
        private static partial Regex PartialTagRegex();

        /// <summary>
        /// Creates a regex for `Html.PartialAsync` and `Html.Partial` calls.
        /// </summary>
        /// <returns>A regex that captures literal partial targets when available.</returns>
        [GeneratedRegex("Html\\.Partial(?:Async)?\\s*\\(\\s*(?:\\\"(?<literal>[^\\\"]+)\\\"|(?<computed>[^\\),]+))", RegexOptions.CultureInvariant)]
        private static partial Regex PartialAsyncRegex();

        /// <summary>
        /// Creates a regex for view-component tag-helper tags.
        /// </summary>
        /// <returns>A regex that captures view-component tag names.</returns>
        [GeneratedRegex("<vc:(?<name>[A-Za-z0-9_-]+)\\b", RegexOptions.CultureInvariant)]
        private static partial Regex ViewComponentTagRegex();

        /// <summary>
        /// Creates a regex for `Component.InvokeAsync` calls.
        /// </summary>
        /// <returns>A regex that captures literal view-component names.</returns>
        [GeneratedRegex("Component\\.InvokeAsync\\s*\\(\\s*\\\"(?<name>[^\\\"]+)\\\"", RegexOptions.CultureInvariant)]
        private static partial Regex ComponentInvokeRegex();

        /// <summary>
        /// Creates a regex for form tags.
        /// </summary>
        /// <returns>A regex that detects `<form>` start tags.</returns>
        [GeneratedRegex("<form\\b[^>]*>", RegexOptions.CultureInvariant)]
        private static partial Regex FormTagRegex();

        /// <summary>
        /// Creates a regex for anchor tags.
        /// </summary>
        /// <returns>A regex that detects `<a>` start tags.</returns>
        [GeneratedRegex("<a\\b[^>]*>", RegexOptions.CultureInvariant)]
        private static partial Regex AnchorTagRegex();

        /// <summary>
        /// Creates a regex for authorization attributes with literal Roles or Policy values.
        /// </summary>
        /// <returns>A regex that captures the first literal authorization value.</returns>
        [GeneratedRegex("Authorize\\s*\\([^\\)]*(?:Roles|Policy)\\s*=\\s*\\\"(?<value>[^\\\"]+)\\\"", RegexOptions.CultureInvariant)]
        private static partial Regex AuthorizeAttributeRegex();

        /// <summary>
        /// Creates a regex for companion Razor Page handler methods.
        /// </summary>
        /// <returns>A regex that captures public handler method names.</returns>
        [GeneratedRegex("\\bpublic\\s+[^;{}=]+?\\s+(?<name>On(?:Get|Post)[A-Za-z0-9_]*)\\s*\\(", RegexOptions.CultureInvariant)]
        private static partial Regex HandlerMethodRegex();

        /// <summary>
        /// Creates a regex for MVC controller class declarations.
        /// </summary>
        /// <returns>A regex that captures controller names without the `Controller` suffix.</returns>
        [GeneratedRegex("\\bclass\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)Controller\\b", RegexOptions.CultureInvariant)]
        private static partial Regex ControllerClassRegex();

        /// <summary>
        /// Creates a regex for simple public C# method declarations.
        /// </summary>
        /// <returns>A regex that captures method names.</returns>
        [GeneratedRegex("\\bpublic\\s+[^;{}=]+?\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*\\(", RegexOptions.CultureInvariant)]
        private static partial Regex PublicMethodRegex();

        /// <summary>
        /// Creates a regex for conventional MVC view paths.
        /// </summary>
        /// <returns>A regex that captures controller and action segments from a view path.</returns>
        [GeneratedRegex("/Views/(?<controller>[^/]+)/(?<action>[^/]+)\\.cshtml$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex MvcViewPathRegex();

        /// <summary>
        /// Describes one discovered Razor-capable project.
        /// </summary>
        /// <param name="AbsoluteProjectPath">The absolute project path used for artifact ownership checks.</param>
        /// <param name="RelativeProjectPath">The repository-relative project path used for stable keys.</param>
        /// <param name="ProjectName">The display name of the project.</param>
        /// <param name="TargetFramework">The target framework value read from project metadata.</param>
        private sealed record RazorProjectContext(string AbsoluteProjectPath, string RelativeProjectPath, string ProjectName, string TargetFramework);

        /// <summary>
        /// Describes one discovered Razor artifact and its owning project.
        /// </summary>
        /// <param name="Project">The project that owns the artifact.</param>
        /// <param name="AbsolutePath">The absolute artifact path used for file reads.</param>
        /// <param name="RelativePath">The repository-relative artifact path used for evidence and stable keys.</param>
        /// <param name="DisplayName">The display name inferred from the file name.</param>
        /// <param name="ArtifactKind">The coarse Razor artifact classification.</param>
        private sealed record RazorArtifactContext(RazorProjectContext Project, string AbsolutePath, string RelativePath, string DisplayName, RazorArtifactKind ArtifactKind);

        /// <summary>
        /// Describes one Razor source line with its original one-based line number.
        /// </summary>
        /// <param name="LineNumber">The one-based line number.</param>
        /// <param name="Text">The text content of the line.</param>
        private sealed record RazorLine(int LineNumber, string Text);

        /// <summary>
        /// Describes static metadata read from an owning project file.
        /// </summary>
        /// <param name="RelativeProjectPath">The repository-relative project path.</param>
        /// <param name="ProjectName">The project display name.</param>
        /// <param name="TargetFramework">The target framework value or Unknown.</param>
        private sealed record ProjectMetadata(string RelativeProjectPath, string ProjectName, string TargetFramework);

        /// <summary>
        /// Describes repository-wide Razor context used during per-artifact analysis.
        /// </summary>
        /// <param name="TagHelpers">The visible `_ViewImports` tag-helper declarations.</param>
        /// <param name="ViewStartLayouts">The visible `_ViewStart` layout declarations.</param>
        /// <param name="PageModels">The companion Razor Page model descriptors keyed by `.cshtml` path.</param>
        /// <param name="Controllers">The MVC controller/action descriptors keyed by controller name.</param>
        private sealed record RazorRepositoryContext(IReadOnlyList<TagHelperImport> TagHelpers, IReadOnlyList<ViewStartLayout> ViewStartLayouts, IReadOnlyDictionary<string, CompanionPageModel> PageModels, IReadOnlyDictionary<string, ControllerActionIndex> Controllers);

        /// <summary>
        /// Describes a parsed `@model` directive.
        /// </summary>
        /// <param name="ModelType">The model type token visible in markup.</param>
        /// <param name="LineNumber">The one-based directive line number.</param>
        /// <param name="SourceText">The directive source text used for evidence.</param>
        private sealed record RazorModelDirective(string ModelType, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a parsed Razor Pages `@page` directive.
        /// </summary>
        /// <param name="RouteTemplate">The literal route template when present.</param>
        /// <param name="IsDynamic">Whether the route is computed from runtime state.</param>
        /// <param name="LineNumber">The one-based directive line number.</param>
        /// <param name="SourceText">The directive source text used for evidence.</param>
        private sealed record RouteDirective(string? RouteTemplate, bool IsDynamic, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a parsed layout usage.
        /// </summary>
        /// <param name="LayoutName">The literal layout name when statically available.</param>
        /// <param name="IsDynamic">Whether the layout is computed from runtime state.</param>
        /// <param name="IsInherited">Whether the layout came from `_ViewStart.cshtml`.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record LayoutUsage(string? LayoutName, bool IsDynamic, bool IsInherited, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a `_ViewImports.cshtml` tag-helper declaration.
        /// </summary>
        /// <param name="DirectoryPath">The repository-relative directory where the import applies.</param>
        /// <param name="TagHelperIdentity">The tag-helper import identity.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record TagHelperImport(string DirectoryPath, string TagHelperIdentity, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a `_ViewStart.cshtml` layout declaration.
        /// </summary>
        /// <param name="DirectoryPath">The repository-relative directory where the view-start applies.</param>
        /// <param name="LayoutName">The literal layout name.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record ViewStartLayout(string DirectoryPath, string LayoutName, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a companion Razor Page model source file.
        /// </summary>
        /// <param name="RelativePath">The repository-relative companion source path.</param>
        /// <param name="Handlers">The visible handler methods in source order.</param>
        private sealed record CompanionPageModel(string RelativePath, IReadOnlyList<HandlerMethod> Handlers);

        /// <summary>
        /// Describes a Razor Page handler method.
        /// </summary>
        /// <param name="MethodName">The handler method name.</param>
        /// <param name="EventName">The normalized UI event name represented by the handler.</param>
        /// <param name="RelativePath">The repository-relative source file path.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record HandlerMethod(string MethodName, string EventName, string RelativePath, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a conventional MVC controller and its visible action methods.
        /// </summary>
        /// <param name="ControllerName">The controller name without the `Controller` suffix.</param>
        /// <param name="RelativePath">The repository-relative controller source path.</param>
        /// <param name="Actions">The visible public action method names.</param>
        private sealed record ControllerActionIndex(string ControllerName, string RelativePath, IReadOnlySet<string> Actions);

        /// <summary>
        /// Describes a parsed partial or view-component usage.
        /// </summary>
        /// <param name="ComponentName">The target component or partial name.</param>
        /// <param name="IsDynamic">Whether the target is computed from runtime state.</param>
        /// <param name="ComponentKind">The component usage category.</param>
        /// <param name="LineNumber">The one-based markup line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record ComponentUsage(string ComponentName, bool IsDynamic, string ComponentKind, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a parsed form tag-helper usage.
        /// </summary>
        /// <param name="EventName">The normalized UI event name for the form post.</param>
        /// <param name="HandlerName">The handler or action token displayed in metadata.</param>
        /// <param name="Controller">The optional MVC controller target.</param>
        /// <param name="Action">The optional MVC action target.</param>
        /// <param name="Handler">The optional Razor Page handler target.</param>
        /// <param name="LineNumber">The one-based markup line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record FormUsage(string EventName, string HandlerName, string? Controller, string? Action, string? Handler, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a parsed anchor navigation usage.
        /// </summary>
        /// <param name="Target">The target page or controller/action token.</param>
        /// <param name="IsDynamic">Whether the target is computed from runtime state.</param>
        /// <param name="LineNumber">The one-based markup line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record NavigationUsage(string Target, bool IsDynamic, int LineNumber, string SourceText);

        /// <summary>
        /// Describes the coarse category of a `.cshtml` artifact.
        /// </summary>
        private enum RazorArtifactKind
        {
            /// <summary>
            /// A Razor Pages page under a Pages folder.
            /// </summary>
            Page,

            /// <summary>
            /// An MVC Razor view under a Views folder.
            /// </summary>
            View,

            /// <summary>
            /// A Razor view that renders a view component.
            /// </summary>
            ViewComponentView,

            /// <summary>
            /// A `_ViewImports.cshtml` context artifact.
            /// </summary>
            ViewImports,

            /// <summary>
            /// A `_ViewStart.cshtml` context artifact.
            /// </summary>
            ViewStart
        }
    }
}