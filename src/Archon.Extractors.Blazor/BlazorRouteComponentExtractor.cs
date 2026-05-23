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

namespace Archon.Extractors.Blazor
{
    /// <summary>
    /// Extracts the first WP011 Blazor vertical slice from static `.razor` files into shared graph contracts.
    /// </summary>
    /// <remarks>
    /// The extractor performs deterministic file and text analysis only. It does not compile Razor, render UI, start the target application, call APIs, contact external services, write Neo4j records, or invoke browser automation.
    /// </remarks>
    public sealed partial class BlazorRouteComponentExtractor
    {
        /// <summary>
        /// Extracts Blazor application, component, route, layout, injected dependency, parameter, authorization, evidence, warning, and unknown facts.
        /// </summary>
        /// <param name="request">The repository-scoped Blazor extraction request.</param>
        /// <param name="cancellationToken">The cancellation token that stops file discovery and artifact analysis.</param>
        /// <returns>A result containing the graph-ready snapshot emitted by this extractor.</returns>
        public async Task<BlazorRouteComponentExtractionResult> ExtractAsync(BlazorRouteComponentExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // The extraction flow is intentionally linear: discover artifacts, analyze each component, and then return the accumulated deterministic snapshot.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            ArchitectureSnapshotAccumulator accumulator = new();
            IReadOnlyList<BlazorProjectContext> projects = DiscoverProjects(request.RepositoryRootDirectory);
            IReadOnlyList<BlazorArtifactContext> artifacts = DiscoverRazorArtifacts(request.RepositoryRootDirectory, projects);

            foreach (BlazorProjectContext project in projects.Where(project => artifacts.Any(artifact => StringComparer.Ordinal.Equals(artifact.Project.RelativeProjectPath, project.RelativeProjectPath))))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateProjectAndApplication(request, accumulator, project);
            }

            foreach (BlazorArtifactContext artifact in artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string content = await File.ReadAllTextAsync(artifact.AbsolutePath, cancellationToken).ConfigureAwait(false);
                AnalyzeRazorArtifact(request, accumulator, artifact, content);
            }

            return new BlazorRouteComponentExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Discovers project files that can own Blazor Razor artifacts.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <returns>Project contexts ordered by repository-relative path.</returns>
        private static IReadOnlyList<BlazorProjectContext> DiscoverProjects(string repositoryRootDirectory)
        {
            // Project context is static metadata only; the extractor never restores or builds the target project.
            if (!Directory.Exists(repositoryRootDirectory))
            {
                return [];
            }

            List<BlazorProjectContext> projects = [];
            foreach (string projectPath in Directory.EnumerateFiles(repositoryRootDirectory, "*.csproj", SearchOption.AllDirectories).Where(IsRepositorySourcePath).Order(StringComparer.OrdinalIgnoreCase))
            {
                ProjectMetadata metadata = ReadProjectMetadata(repositoryRootDirectory, projectPath);
                projects.Add(new BlazorProjectContext(projectPath, metadata.RelativeProjectPath, metadata.ProjectName, metadata.TargetFramework, metadata.HostingModel));
            }

            return projects;
        }

        /// <summary>
        /// Discovers repository-contained `.razor` files and associates each artifact with its nearest project file.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="projects">The discovered project contexts that can own artifacts.</param>
        /// <returns>Razor artifact contexts ordered by repository-relative path.</returns>
        private static IReadOnlyList<BlazorArtifactContext> DiscoverRazorArtifacts(string repositoryRootDirectory, IReadOnlyList<BlazorProjectContext> projects)
        {
            // Build output folders are excluded because generated and copied Razor artifacts would duplicate source facts and destabilize evidence paths.
            if (!Directory.Exists(repositoryRootDirectory))
            {
                return [];
            }

            List<BlazorArtifactContext> artifacts = [];
            foreach (string artifactPath in Directory.EnumerateFiles(repositoryRootDirectory, "*.razor", SearchOption.AllDirectories).Where(IsRepositorySourcePath).Order(StringComparer.OrdinalIgnoreCase))
            {
                BlazorProjectContext? project = FindNearestProject(projects, artifactPath);
                if (project is null)
                {
                    continue;
                }

                string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRootDirectory, artifactPath);
                artifacts.Add(new BlazorArtifactContext(project, artifactPath, relativePath, Path.GetFileNameWithoutExtension(artifactPath)));
            }

            return artifacts;
        }

        /// <summary>
        /// Adds the owning project and UI application nodes for a Blazor project.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="project">The project context being represented.</param>
        private static void AccumulateProjectAndApplication(BlazorRouteComponentExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, BlazorProjectContext project)
        {
            // Project and UI application nodes give component facts stable owners even when the API pipeline has not run earlier project stages.
            StableKey projectStableKey = StableKeyGenerator.ForProject(project.RelativeProjectPath);
            EvidenceRecord projectEvidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(project.RelativeProjectPath, 1, 1, project.ProjectName), "Blazor", "Application", "ProjectMetadata", Confidence.High, UnknownState.Known);
            GraphMetadata projectMetadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "ProjectMetadata",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = project.RelativeProjectPath,
                ["targetFramework"] = project.TargetFramework,
                ["uiFramework"] = "Blazor"
            });
            accumulator.AddEvidence(projectEvidence);
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, projectStableKey, NodeKind.Project, project.ProjectName, project.RelativeProjectPath, project.RelativeProjectPath, "C#", projectStableKey, null, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, projectEvidence.StableKey, projectMetadata, FingerprintGenerator.ForNode(NodeKind.Project, project.ProjectName, project.RelativeProjectPath, project.RelativeProjectPath, KnowledgeKind.Fact, projectMetadata)));

            StableKey applicationStableKey = UiStableKeyBuilder.Create("ui-application://", projectStableKey.Value, "Blazor", project.TargetFramework, project.HostingModel);
            GraphMetadata applicationMetadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "ProjectMetadata",
                ["hostingModel"] = project.HostingModel,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = project.RelativeProjectPath,
                ["targetFramework"] = project.TargetFramework,
                ["uiArtifactKind"] = "Application",
                ["uiFramework"] = "Blazor"
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, applicationStableKey, NodeKind.UiApplication, project.ProjectName, project.RelativeProjectPath, project.ProjectName, "Razor", projectStableKey, projectStableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, projectEvidence.StableKey, applicationMetadata, FingerprintGenerator.ForNode(NodeKind.UiApplication, project.ProjectName, project.RelativeProjectPath, project.ProjectName, KnowledgeKind.Fact, applicationMetadata)));
        }

        /// <summary>
        /// Analyzes one Razor artifact and contributes graph facts for supported first-slice Blazor patterns.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="content">The Razor artifact content.</param>
        private static void AnalyzeRazorArtifact(BlazorRouteComponentExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, BlazorArtifactContext artifact, string content)
        {
            // Directive extraction uses conservative line-oriented matching so malformed markup degrades to warnings and unknowns instead of hard failures.
            RazorLine[] lines = SplitLines(content);
            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            StableKey applicationStableKey = UiStableKeyBuilder.Create("ui-application://", projectStableKey.Value, "Blazor", artifact.Project.TargetFramework, artifact.Project.HostingModel);
            EvidenceRecord componentEvidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, 1, Math.Max(1, lines.Length), content), "Blazor", "Component", "StaticMarkup", Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(componentEvidence);

            IReadOnlyList<RouteDirective> routes = ExtractRouteDirectives(lines);
            string? layoutName = ExtractLayoutName(lines);
            IReadOnlyList<InjectedService> injectedServices = ExtractInjectedServices(lines);
            IReadOnlyList<string> parameters = ExtractParameters(lines);
            IReadOnlyList<ComponentUsage> componentUsages = ExtractComponentUsages(lines, artifact.ComponentName);
            IReadOnlyList<UiEventUsage> eventUsages = ExtractUiEventUsages(lines);
            IReadOnlyList<UiControlUsage> controlUsages = ExtractUiControlUsages(lines);
            IReadOnlyList<string> renderFragments = ExtractRenderFragments(lines);
            RenderModeUsage? renderMode = ExtractRenderMode(lines);
            IReadOnlyList<ApiUsage> apiUsages = ExtractApiUsages(lines, injectedServices);
            IReadOnlyList<ConfigurationUsage> configurationUsages = ExtractConfigurationUsages(lines, injectedServices);
            string? authorizationPolicy = ExtractAuthorizationPolicy(content);
            ArchitectureNode componentNode = CreateComponentNode(request.SnapshotStableKey, artifact, projectStableKey, componentEvidence.StableKey, routes, layoutName, injectedServices, parameters, authorizationPolicy, renderMode, renderFragments);
            accumulator.AddNode(componentNode);
            if (renderMode?.UnknownReason is not null)
            {
                accumulator.AddWarning($"Blazor computed render mode in {artifact.RelativePath} on line {renderMode.LineNumber.ToString(CultureInfo.InvariantCulture)} could not be statically resolved.");
            }

            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DeclaresComponent, applicationStableKey, componentNode.StableKey, componentEvidence.StableKey, "DeclaresComponent", artifact.RelativePath, Confidence.High, UnknownState.Known));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DeclaresComponent, projectStableKey, componentNode.StableKey, componentEvidence.StableKey, "DeclaresComponent", artifact.RelativePath, Confidence.High, UnknownState.Known));

            foreach (RouteDirective route in routes)
            {
                AccumulateRoute(request, accumulator, artifact, componentNode, route);
            }

            if (layoutName is not null)
            {
                AccumulateLayout(request, accumulator, artifact, componentNode, layoutName, componentEvidence.StableKey);
            }

            foreach (InjectedService injectedService in injectedServices)
            {
                AccumulateInjectedService(request, accumulator, artifact, componentNode, injectedService);
            }

            foreach (ComponentUsage componentUsage in componentUsages)
            {
                AccumulateComponentUsage(request, accumulator, artifact, componentNode, componentUsage);
            }

            foreach (UiEventUsage eventUsage in eventUsages)
            {
                AccumulateUiEventUsage(request, accumulator, artifact, componentNode, eventUsage);
            }

            foreach (UiControlUsage controlUsage in controlUsages)
            {
                AccumulateUiControlUsage(request, accumulator, artifact, componentNode, controlUsage);
            }

            foreach (ApiUsage apiUsage in apiUsages)
            {
                AccumulateApiUsage(request, accumulator, artifact, componentNode, apiUsage);
            }

            foreach (ConfigurationUsage configurationUsage in configurationUsages)
            {
                AccumulateConfigurationUsage(request, accumulator, artifact, componentNode, configurationUsage);
            }
        }

        /// <summary>
        /// Adds a UI route node and declaration relationship for one `@page` directive.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="componentNode">The component node that declares the route.</param>
        /// <param name="route">The parsed route directive.</param>
        private static void AccumulateRoute(BlazorRouteComponentExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, BlazorArtifactContext artifact, ArchitectureNode componentNode, RouteDirective route)
        {
            // Missing route templates become explicit unknown route nodes so partial Razor files still produce useful evidence.
            UnknownState unknownState = route.RouteTemplate is null ? UnknownState.Unknown("Blazor @page directive is missing a route template.") : UnknownState.Known;
            Confidence confidence = route.RouteTemplate is null ? Confidence.Low : Confidence.High;
            string routeTemplate = route.RouteTemplate ?? $"unknown:{artifact.RelativePath}:{route.LineNumber.ToString(CultureInfo.InvariantCulture)}";
            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, route.LineNumber, route.LineNumber, route.SourceText), "Blazor", "Route", "StaticMarkup", confidence, unknownState);
            accumulator.AddEvidence(evidence);
            if (route.RouteTemplate is null)
            {
                accumulator.AddWarning($"Blazor @page directive in {artifact.RelativePath} on line {route.LineNumber.ToString(CultureInfo.InvariantCulture)} is missing a route template.");
            }

            Dictionary<string, object?> metadataValues = new(StringComparer.Ordinal)
            {
                ["confidenceReason"] = route.RouteTemplate is null ? "The @page directive was present but no literal route template was available." : "The @page directive provided a literal route template.",
                ["detectionMode"] = "StaticMarkup",
                ["projectKey"] = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath).Value,
                ["routeTemplate"] = routeTemplate,
                ["sourcePath"] = artifact.RelativePath,
                ["targetFramework"] = artifact.Project.TargetFramework,
                ["uiArtifactKind"] = "Route",
                ["uiFramework"] = "Blazor"
            };
            string? routeParameter = ExtractFirstRouteParameter(route.RouteTemplate);
            if (routeParameter is not null)
            {
                metadataValues["routeParameter"] = routeParameter;
            }

            GraphMetadata metadata = GraphMetadata.From(metadataValues);
            StableKey routeStableKey = UiStableKeyBuilder.Create("ui-route://", StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath).Value, "Blazor", routeTemplate, artifact.RelativePath, route.LineNumber.ToString(CultureInfo.InvariantCulture));
            ArchitectureNode routeNode = new(request.SnapshotStableKey, routeStableKey, NodeKind.UiRoute, routeTemplate, routeTemplate, routeTemplate, "Razor", StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath), componentNode.StableKey, KnowledgeKind.Fact, null, null, confidence, unknownState, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.UiRoute, routeTemplate, routeTemplate, routeTemplate, KnowledgeKind.Fact, metadata));
            accumulator.AddNode(routeNode);
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DeclaresUiRoute, componentNode.StableKey, routeStableKey, evidence.StableKey, "DeclaresUiRoute", artifact.RelativePath, confidence, unknownState));
        }

        /// <summary>
        /// Adds a UI layout node and usage relationship for an `@layout` directive.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="componentNode">The component node that uses the layout.</param>
        /// <param name="layoutName">The layout type name parsed from markup.</param>
        /// <param name="evidenceStableKey">The component evidence that supports the layout usage.</param>
        private static void AccumulateLayout(BlazorRouteComponentExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, BlazorArtifactContext artifact, ArchitectureNode componentNode, string layoutName, StableKey evidenceStableKey)
        {
            // Layout identity uses the owning project and normalized layout name because first-slice extraction does not require semantic type binding.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "StaticMarkup",
                ["layoutName"] = layoutName,
                ["projectKey"] = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath).Value,
                ["sourcePath"] = artifact.RelativePath,
                ["targetFramework"] = artifact.Project.TargetFramework,
                ["uiArtifactKind"] = "Layout",
                ["uiFramework"] = "Blazor"
            });
            StableKey layoutStableKey = UiStableKeyBuilder.Create("ui-layout://", StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath).Value, "Blazor", layoutName);
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, layoutStableKey, NodeKind.UiLayout, layoutName, layoutName, layoutName, "Razor", StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath), null, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidenceStableKey, metadata, FingerprintGenerator.ForNode(NodeKind.UiLayout, layoutName, layoutName, layoutName, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesLayout, componentNode.StableKey, layoutStableKey, evidenceStableKey, "UsesLayout", artifact.RelativePath, Confidence.High, UnknownState.Known));
        }

        /// <summary>
        /// Adds configuration/dependency facts for a Blazor `@inject` directive.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="componentNode">The component node that declares the injection.</param>
        /// <param name="injectedService">The parsed injected service descriptor.</param>
        private static void AccumulateInjectedService(BlazorRouteComponentExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, BlazorArtifactContext artifact, ArchitectureNode componentNode, InjectedService injectedService)
        {
            // The first slice represents injected Blazor dependencies as reusable ConfigurationKey nodes because that controlled node kind already models configuration/dependency names in current contracts.
            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, injectedService.LineNumber, injectedService.LineNumber, injectedService.SourceText), "Blazor", "InjectedService", "StaticMarkup", Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(evidence);
            StableKey configurationStableKey = StableKeyGenerator.ForConfigurationKey(injectedService.ServiceType);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["configurationKey"] = injectedService.ServiceType,
                ["detectionMode"] = "StaticMarkup",
                ["injectedMemberName"] = injectedService.MemberName,
                ["projectKey"] = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath).Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Component",
                ["uiFramework"] = "Blazor"
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, configurationStableKey, NodeKind.ConfigurationKey, injectedService.ServiceType, injectedService.ServiceType, injectedService.ServiceType, "Razor", StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath), null, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.ConfigurationKey, injectedService.ServiceType, injectedService.ServiceType, injectedService.ServiceType, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesConfig, componentNode.StableKey, configurationStableKey, evidence.StableKey, "UsesInjectedService", artifact.RelativePath, Confidence.High, UnknownState.Known));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DependsOn, componentNode.StableKey, configurationStableKey, evidence.StableKey, "DependsOnInjectedService", artifact.RelativePath, Confidence.High, UnknownState.Known));
        }

        /// <summary>
        /// Adds component usage facts for a static child component tag or an unknown fact for dynamic component composition.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="componentNode">The component node that uses the child component.</param>
        /// <param name="componentUsage">The parsed component usage descriptor.</param>
        private static void AccumulateComponentUsage(BlazorRouteComponentExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, BlazorArtifactContext artifact, ArchitectureNode componentNode, ComponentUsage componentUsage)
        {
            // DynamicComponent targets are runtime type values, so the extractor emits an explicit unknown instead of inventing a component identity.
            if (componentUsage.IsDynamic)
            {
                UnknownState unknownState = UnknownState.Unknown("DynamicComponent type is computed from runtime state.");
                EvidenceRecord unknownEvidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, componentUsage.LineNumber, componentUsage.LineNumber, componentUsage.SourceText), "Blazor", "Component", "StaticMarkup", Confidence.Low, unknownState);
                accumulator.AddEvidence(unknownEvidence);
                accumulator.AddWarning($"Blazor dynamic component in {artifact.RelativePath} on line {componentUsage.LineNumber.ToString(CultureInfo.InvariantCulture)} has a computed target.");
                AccumulateUnknownNode(request, accumulator, artifact, componentNode, NodeKind.UiComponent, EdgeKind.UsesComponent, "DynamicComponent", "DynamicComponent", componentUsage.LineNumber, componentUsage.SourceText, "DynamicComponent type is computed from runtime state.", "UsesDynamicComponent", unknownEvidence.StableKey);
                return;
            }

            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, componentUsage.LineNumber, componentUsage.LineNumber, componentUsage.SourceText), "Blazor", "Component", "StaticMarkup", Confidence.Medium, UnknownState.Known);
            accumulator.AddEvidence(evidence);
            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            StableKey targetStableKey = UiStableKeyBuilder.Create("ui-component://", projectStableKey.Value, "Blazor", componentUsage.ComponentName);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["componentName"] = componentUsage.ComponentName,
                ["detectionMode"] = "StaticMarkup",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["targetFramework"] = artifact.Project.TargetFramework,
                ["uiArtifactKind"] = "Component",
                ["uiFramework"] = "Blazor"
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, targetStableKey, NodeKind.UiComponent, componentUsage.ComponentName, componentUsage.ComponentName, componentUsage.ComponentName, "Razor", projectStableKey, null, KnowledgeKind.Fact, null, null, Confidence.Medium, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.UiComponent, componentUsage.ComponentName, componentUsage.ComponentName, componentUsage.ComponentName, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesComponent, componentNode.StableKey, targetStableKey, evidence.StableKey, "UsesComponent", artifact.RelativePath, Confidence.Medium, UnknownState.Known));
        }

        /// <summary>
        /// Adds a UI event relationship for a statically visible Blazor event or callback attribute.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="componentNode">The component node that handles the event.</param>
        /// <param name="eventUsage">The parsed event descriptor.</param>
        private static void AccumulateUiEventUsage(BlazorRouteComponentExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, BlazorArtifactContext artifact, ArchitectureNode componentNode, UiEventUsage eventUsage)
        {
            // Event relationships target a command node named after the handler so impact analysis can find callback handlers without compiling Razor.
            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, eventUsage.LineNumber, eventUsage.LineNumber, eventUsage.SourceText), "Blazor", "Command", "StaticMarkup", Confidence.Medium, UnknownState.Known);
            accumulator.AddEvidence(evidence);
            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            StableKey commandStableKey = UiStableKeyBuilder.Create("ui-command://", projectStableKey.Value, "Blazor", artifact.RelativePath, eventUsage.EventName, eventUsage.HandlerName);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["commandName"] = eventUsage.HandlerName,
                ["detectionMode"] = "StaticMarkup",
                ["eventName"] = eventUsage.EventName,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Command",
                ["uiFramework"] = "Blazor"
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, commandStableKey, NodeKind.Command, eventUsage.HandlerName, eventUsage.HandlerName, eventUsage.HandlerName, "Razor", projectStableKey, componentNode.StableKey, KnowledgeKind.Fact, null, null, Confidence.Medium, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Command, eventUsage.HandlerName, eventUsage.HandlerName, eventUsage.HandlerName, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdgeWithMetadata(request.SnapshotStableKey, EdgeKind.HandlesUiEvent, componentNode.StableKey, commandStableKey, evidence.StableKey, "HandlesUiEvent", artifact.RelativePath, Confidence.Medium, UnknownState.Known, new Dictionary<string, object?>
            {
                ["eventName"] = eventUsage.EventName,
                ["methodName"] = eventUsage.HandlerName
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesCommand, componentNode.StableKey, commandStableKey, evidence.StableKey, "UsesCommand", artifact.RelativePath, Confidence.Medium, UnknownState.Known));
        }

        /// <summary>
        /// Adds UI control facts for form and validation component markers.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="componentNode">The component node that uses the control.</param>
        /// <param name="controlUsage">The parsed control descriptor.</param>
        private static void AccumulateUiControlUsage(BlazorRouteComponentExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, BlazorArtifactContext artifact, ArchitectureNode componentNode, UiControlUsage controlUsage)
        {
            // Forms and validators are represented as controls because they are reusable UI building blocks rather than component-specific node kinds.
            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, controlUsage.LineNumber, controlUsage.LineNumber, controlUsage.SourceText), "Blazor", "Control", "StaticMarkup", Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(evidence);
            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            StableKey controlStableKey = UiStableKeyBuilder.Create("ui-control://", projectStableKey.Value, "Blazor", controlUsage.ControlName, artifact.RelativePath);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["controlName"] = controlUsage.ControlName,
                ["detectionMode"] = "StaticMarkup",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Control",
                ["uiFramework"] = "Blazor"
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, controlStableKey, NodeKind.UiControl, controlUsage.ControlName, controlUsage.ControlName, controlUsage.ControlName, "Razor", projectStableKey, componentNode.StableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.UiControl, controlUsage.ControlName, controlUsage.ControlName, controlUsage.ControlName, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesControl, componentNode.StableKey, controlStableKey, evidence.StableKey, "UsesControl", artifact.RelativePath, Confidence.High, UnknownState.Known));
        }

        /// <summary>
        /// Adds API call facts for static HTTP client usages or explicit unknowns for computed API targets.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="componentNode">The component node that initiates the API call.</param>
        /// <param name="apiUsage">The parsed API usage descriptor.</param>
        private static void AccumulateApiUsage(BlazorRouteComponentExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, BlazorArtifactContext artifact, ArchitectureNode componentNode, ApiUsage apiUsage)
        {
            // Literal HTTP targets can be correlated to an ExternalService node; computed targets remain explicit low-confidence unknowns.
            if (apiUsage.ApiTarget is null)
            {
                UnknownState unknownState = UnknownState.Unknown("API target is computed from runtime state.");
                EvidenceRecord unknownEvidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, apiUsage.LineNumber, apiUsage.LineNumber, apiUsage.SourceText), "Blazor", "ExternalService", "StaticCode", Confidence.Low, unknownState);
                accumulator.AddEvidence(unknownEvidence);
                accumulator.AddWarning($"Blazor computed API target in {artifact.RelativePath} on line {apiUsage.LineNumber.ToString(CultureInfo.InvariantCulture)} could not be statically resolved.");
                AccumulateUnknownNode(request, accumulator, artifact, componentNode, NodeKind.ExternalService, EdgeKind.CallsApi, "ExternalService", "API", apiUsage.LineNumber, apiUsage.SourceText, "API target is computed from runtime state.", "CallsApiUnknown", unknownEvidence.StableKey);
                return;
            }

            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, apiUsage.LineNumber, apiUsage.LineNumber, apiUsage.SourceText), "Blazor", "ExternalService", "StaticCode", Confidence.Medium, UnknownState.Known);
            accumulator.AddEvidence(evidence);
            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            StableKey serviceStableKey = UiStableKeyBuilder.Create("external-service://", projectStableKey.Value, "Blazor", apiUsage.ApiTarget);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "StaticCode",
                ["externalServiceEndpoint"] = apiUsage.ApiTarget,
                ["methodName"] = apiUsage.MethodName,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiFramework"] = "Blazor"
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, serviceStableKey, NodeKind.ExternalService, apiUsage.ApiTarget, apiUsage.ApiTarget, apiUsage.ApiTarget, "Razor", projectStableKey, componentNode.StableKey, KnowledgeKind.Fact, null, null, Confidence.Medium, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.ExternalService, apiUsage.ApiTarget, apiUsage.ApiTarget, apiUsage.ApiTarget, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdgeWithMetadata(request.SnapshotStableKey, EdgeKind.CallsApi, componentNode.StableKey, serviceStableKey, evidence.StableKey, "CallsApi", artifact.RelativePath, Confidence.Medium, UnknownState.Known, new Dictionary<string, object?>
            {
                ["methodName"] = apiUsage.MethodName,
                ["navigationTarget"] = apiUsage.ApiTarget
            }));
        }

        /// <summary>
        /// Adds configuration key facts for static configuration indexer usage or unknown facts for computed keys.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="componentNode">The component node that uses the configuration key.</param>
        /// <param name="configurationUsage">The parsed configuration usage descriptor.</param>
        private static void AccumulateConfigurationUsage(BlazorRouteComponentExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, BlazorArtifactContext artifact, ArchitectureNode componentNode, ConfigurationUsage configurationUsage)
        {
            // Configuration indexers with literal keys are linked to ConfigurationKey nodes; variable keys produce a specific unknown reason.
            if (configurationUsage.ConfigurationKey is null)
            {
                UnknownState unknownState = UnknownState.Unknown("Configuration key is computed from runtime state.");
                EvidenceRecord unknownEvidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, configurationUsage.LineNumber, configurationUsage.LineNumber, configurationUsage.SourceText), "Blazor", "ConfigurationKey", "StaticCode", Confidence.Low, unknownState);
                accumulator.AddEvidence(unknownEvidence);
                accumulator.AddWarning($"Blazor computed configuration key in {artifact.RelativePath} on line {configurationUsage.LineNumber.ToString(CultureInfo.InvariantCulture)} could not be statically resolved.");
                AccumulateUnknownNode(request, accumulator, artifact, componentNode, NodeKind.ConfigurationKey, EdgeKind.UsesConfig, "ConfigurationKey", "Configuration", configurationUsage.LineNumber, configurationUsage.SourceText, "Configuration key is computed from runtime state.", "UsesConfigUnknown", unknownEvidence.StableKey);
                return;
            }

            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, configurationUsage.LineNumber, configurationUsage.LineNumber, configurationUsage.SourceText), "Blazor", "ConfigurationKey", "StaticCode", Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(evidence);
            StableKey configurationStableKey = StableKeyGenerator.ForConfigurationKey(configurationUsage.ConfigurationKey);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["configurationKey"] = configurationUsage.ConfigurationKey,
                ["detectionMode"] = "StaticCode",
                ["projectKey"] = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath).Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Component",
                ["uiFramework"] = "Blazor"
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, configurationStableKey, NodeKind.ConfigurationKey, configurationUsage.ConfigurationKey, configurationUsage.ConfigurationKey, configurationUsage.ConfigurationKey, "Razor", StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath), componentNode.StableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidence.StableKey, metadata, FingerprintGenerator.ForNode(NodeKind.ConfigurationKey, configurationUsage.ConfigurationKey, configurationUsage.ConfigurationKey, configurationUsage.ConfigurationKey, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdgeWithMetadata(request.SnapshotStableKey, EdgeKind.UsesConfig, componentNode.StableKey, configurationStableKey, evidence.StableKey, "UsesConfig", artifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["configurationKey"] = configurationUsage.ConfigurationKey
            }));
        }

        /// <summary>
        /// Adds a low-confidence unknown node and relationship for a dynamic or ambiguous Blazor target.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Razor artifact context being analyzed.</param>
        /// <param name="componentNode">The component node that contains the ambiguous usage.</param>
        /// <param name="nodeKind">The controlled node kind that best describes the unknown target.</param>
        /// <param name="edgeKind">The controlled edge kind that links the component to the unknown target.</param>
        /// <param name="artifactKind">The UI artifact kind metadata value.</param>
        /// <param name="identityPrefix">The stable-key identity prefix for this unknown category.</param>
        /// <param name="lineNumber">The one-based source line number.</param>
        /// <param name="sourceText">The source text used for evidence context.</param>
        /// <param name="unknownReason">The human-readable reason the target could not be resolved statically.</param>
        /// <param name="relationshipRole">The extractor-specific edge role.</param>
        /// <param name="evidenceStableKey">The evidence stable key that supports the unknown fact.</param>
        private static void AccumulateUnknownNode(BlazorRouteComponentExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, BlazorArtifactContext artifact, ArchitectureNode componentNode, NodeKind nodeKind, EdgeKind edgeKind, string artifactKind, string identityPrefix, int lineNumber, string sourceText, string unknownReason, string relationshipRole, StableKey evidenceStableKey)
        {
            // Unknown identities include the source location so multiple ambiguous patterns in the same component remain distinct and deterministic.
            UnknownState unknownState = UnknownState.Unknown(unknownReason);
            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            string displayName = $"Unknown {identityPrefix}";
            StableKey unknownStableKey = UiStableKeyBuilder.Create("ui-unknown://", projectStableKey.Value, "Blazor", artifactKind, artifact.RelativePath, lineNumber.ToString(CultureInfo.InvariantCulture), unknownReason);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = unknownReason,
                ["detectionMode"] = "StaticMarkup",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = artifactKind,
                ["uiFramework"] = "Blazor"
            });
            accumulator.AddNode(new ArchitectureNode(request.SnapshotStableKey, unknownStableKey, nodeKind, displayName, displayName, sourceText, "Razor", projectStableKey, componentNode.StableKey, KnowledgeKind.Fact, null, null, Confidence.Low, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForNode(nodeKind, displayName, displayName, sourceText, KnowledgeKind.Fact, metadata)));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, edgeKind, componentNode.StableKey, unknownStableKey, evidenceStableKey, relationshipRole, artifact.RelativePath, Confidence.Low, unknownState));
        }

        /// <summary>
        /// Creates the UI component node for a Razor artifact.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the node.</param>
        /// <param name="artifact">The Razor artifact context being represented.</param>
        /// <param name="projectStableKey">The stable key of the owning project.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the component.</param>
        /// <param name="routes">The route directives detected in the component.</param>
        /// <param name="layoutName">The optional layout directive value.</param>
        /// <param name="injectedServices">The injected service directives detected in the component.</param>
        /// <param name="parameters">The parameter properties detected in the component code block.</param>
        /// <param name="authorizationPolicy">The optional authorization role or policy detected in the component.</param>
        /// <returns>A graph node representing the Blazor component artifact.</returns>
        private static ArchitectureNode CreateComponentNode(StableKey snapshotStableKey, BlazorArtifactContext artifact, StableKey projectStableKey, StableKey evidenceStableKey, IReadOnlyList<RouteDirective> routes, string? layoutName, IReadOnlyList<InjectedService> injectedServices, IReadOnlyList<string> parameters, string? authorizationPolicy, RenderModeUsage? renderMode, IReadOnlyList<string> renderFragments)
        {
            // Component metadata keeps first-slice Blazor details queryable without creating framework-specific node kinds.
            Dictionary<string, object?> metadataValues = new(StringComparer.Ordinal)
            {
                ["componentName"] = artifact.ComponentName,
                ["detectionMode"] = "StaticMarkup",
                ["hostingModel"] = artifact.Project.HostingModel,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["targetFramework"] = artifact.Project.TargetFramework,
                ["uiArtifactKind"] = "Component",
                ["uiFramework"] = "Blazor"
            };
            AddOptional(metadataValues, "layoutName", layoutName);
            AddOptional(metadataValues, "authorizationPolicy", authorizationPolicy);
            AddFirst(metadataValues, "routeTemplate", routes.Select(route => route.RouteTemplate).Where(route => route is not null));
            AddFirst(metadataValues, "componentParameter", parameters);
            AddFirst(metadataValues, "packageIdentity", injectedServices.Select(service => service.ServiceType));
            AddOptional(metadataValues, "renderMode", renderMode?.RenderModeName);
            AddFirst(metadataValues, "renderFragment", renderFragments);

            GraphMetadata metadata = GraphMetadata.From(metadataValues);
            StableKey componentStableKey = UiStableKeyBuilder.Create("ui-component://", projectStableKey.Value, "Blazor", artifact.RelativePath, artifact.ComponentName);
            UnknownState unknownState = renderMode?.UnknownReason is null ? UnknownState.Known : UnknownState.Unknown(renderMode.UnknownReason);
            Confidence confidence = renderMode?.UnknownReason is null ? Confidence.High : Confidence.Medium;
            return new ArchitectureNode(snapshotStableKey, componentStableKey, NodeKind.UiComponent, artifact.ComponentName, artifact.RelativePath, artifact.ComponentName, "Razor", projectStableKey, null, KnowledgeKind.Fact, null, null, confidence, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForNode(NodeKind.UiComponent, artifact.ComponentName, artifact.RelativePath, artifact.ComponentName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a graph edge with deterministic metadata and stable key generation.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the edge.</param>
        /// <param name="edgeKind">The controlled relationship kind.</param>
        /// <param name="sourceStableKey">The source node stable key.</param>
        /// <param name="targetStableKey">The target node stable key.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the relationship.</param>
        /// <param name="relationshipRole">The extractor-specific relationship role for metadata and identity.</param>
        /// <param name="sourcePath">The repository-relative artifact path that produced the relationship.</param>
        /// <param name="confidence">The confidence assigned to the edge.</param>
        /// <param name="unknownState">The unknown-state assigned to the edge.</param>
        /// <returns>A deterministic architecture edge.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, EdgeKind edgeKind, StableKey sourceStableKey, StableKey targetStableKey, StableKey evidenceStableKey, string relationshipRole, string sourcePath, Confidence confidence, UnknownState unknownState)
        {
            // Relationship identity includes endpoints, kind, role, and source path so repeat directives de-duplicate only when they describe the same relationship.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "StaticMarkup",
                ["relationshipRole"] = relationshipRole,
                ["sourcePath"] = sourcePath,
                ["uiFramework"] = "Blazor"
            });
            StableKey stableKey = UiStableKeyBuilder.Create("ui-edge://", sourceStableKey.Value, targetStableKey.Value, edgeKind.Value, relationshipRole, sourcePath);
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
        /// <param name="confidence">The confidence assigned to the edge.</param>
        /// <param name="unknownState">The unknown-state assigned to the edge.</param>
        /// <param name="additionalMetadata">Additional relationship-specific metadata values.</param>
        /// <returns>A deterministic architecture edge.</returns>
        private static ArchitectureEdge CreateEdgeWithMetadata(StableKey snapshotStableKey, EdgeKind edgeKind, StableKey sourceStableKey, StableKey targetStableKey, StableKey evidenceStableKey, string relationshipRole, string sourcePath, Confidence confidence, UnknownState unknownState, IReadOnlyDictionary<string, object?> additionalMetadata)
        {
            // Additional metadata is folded into both fingerprinting and stable identity so edges for different events or targets do not collapse accidentally.
            Dictionary<string, object?> metadataValues = new(StringComparer.Ordinal)
            {
                ["detectionMode"] = "StaticMarkup",
                ["relationshipRole"] = relationshipRole,
                ["sourcePath"] = sourcePath,
                ["uiFramework"] = "Blazor"
            };
            foreach (KeyValuePair<string, object?> item in additionalMetadata.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                metadataValues[item.Key] = item.Value;
            }

            GraphMetadata metadata = GraphMetadata.From(metadataValues);
            StableKey stableKey = UiStableKeyBuilder.Create("ui-edge://", sourceStableKey.Value, targetStableKey.Value, edgeKind.Value, relationshipRole, sourcePath, GraphMetadata.From(additionalMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)).ToCanonicalJson());
            return new ArchitectureEdge(snapshotStableKey, stableKey, edgeKind, sourceStableKey, targetStableKey, isDirect: true, KnowledgeKind.Fact, confidence, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForEdge(edgeKind, sourceStableKey, targetStableKey, isDirect: true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Reads Blazor-relevant metadata from a project file.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="projectPath">The absolute project file path.</param>
        /// <returns>Static project metadata used for UI application identity and ownership.</returns>
        private static ProjectMetadata ReadProjectMetadata(string repositoryRootDirectory, string projectPath)
        {
            // XML read failures degrade to Unknown metadata because Razor artifact analysis can still proceed from file paths.
            string relativeProjectPath = SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRootDirectory, projectPath);
            string projectName = Path.GetFileNameWithoutExtension(projectPath);
            string targetFramework = "Unknown";
            string hostingModel = "Unknown";
            try
            {
                XDocument document = XDocument.Parse(File.ReadAllText(projectPath));
                string sdk = document.Root?.Attribute("Sdk")?.Value ?? string.Empty;
                targetFramework = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "TargetFramework")?.Value.Trim() ?? "Unknown";
                hostingModel = ClassifyHostingModel(sdk, document);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // Unknown metadata is acceptable for this extractor; API-stage logging records broader orchestration diagnostics.
            }

            return new ProjectMetadata(relativeProjectPath, projectName, targetFramework, hostingModel);
        }

        /// <summary>
        /// Classifies Blazor hosting from project SDK and package evidence.
        /// </summary>
        /// <param name="sdk">The project SDK string.</param>
        /// <param name="document">The parsed project XML document.</param>
        /// <returns>A normalized hosting model value.</returns>
        private static string ClassifyHostingModel(string sdk, XDocument document)
        {
            // Hosting classification is conservative and returns Unknown when static project evidence is insufficient.
            if (sdk.Contains("BlazorWebAssembly", StringComparison.OrdinalIgnoreCase) || ContainsPackage(document, "Microsoft.AspNetCore.Components.WebAssembly"))
            {
                return "WebAssembly";
            }

            if (ContainsPackage(document, "Microsoft.AspNetCore.Components.WebView") || ContainsPackage(document, "Microsoft.AspNetCore.Components.WebView.Maui"))
            {
                return "Hybrid";
            }

            if (sdk.Contains("Web", StringComparison.OrdinalIgnoreCase) || ContainsPackage(document, "Microsoft.AspNetCore.Components.Server"))
            {
                return "Server";
            }

            return "Unknown";
        }

        /// <summary>
        /// Determines whether a project contains a package reference with the requested identity.
        /// </summary>
        /// <param name="document">The parsed project XML document.</param>
        /// <param name="packageId">The package identity to search for.</param>
        /// <returns><see langword="true" /> when the package reference is present; otherwise, <see langword="false" />.</returns>
        private static bool ContainsPackage(XDocument document, string packageId)
        {
            // Package identity comparison is ordinal-ignore-case because NuGet package IDs are case-insensitive in practice.
            return document.Descendants().Any(element => element.Name.LocalName == "PackageReference" && StringComparer.OrdinalIgnoreCase.Equals(element.Attribute("Include")?.Value, packageId));
        }

        /// <summary>
        /// Finds the nearest ancestor project for a Razor artifact.
        /// </summary>
        /// <param name="projects">The candidate project contexts.</param>
        /// <param name="artifactPath">The absolute Razor artifact path.</param>
        /// <returns>The nearest owning project, or <see langword="null" /> when no project directory contains the artifact.</returns>
        private static BlazorProjectContext? FindNearestProject(IReadOnlyList<BlazorProjectContext> projects, string artifactPath)
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
        /// Extracts Blazor `@page` directives from Razor lines.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <returns>Route directives in source order.</returns>
        private static IReadOnlyList<RouteDirective> ExtractRouteDirectives(IReadOnlyList<RazorLine> lines)
        {
            // Literal route templates are the only supported first-slice route shape; missing literals are preserved as unknown routes.
            List<RouteDirective> routes = [];
            foreach (RazorLine line in lines)
            {
                Match match = PageDirectiveRegex().Match(line.Text);
                if (!match.Success)
                {
                    continue;
                }

                string? routeTemplate = match.Groups["route"].Success ? match.Groups["route"].Value : null;
                routes.Add(new RouteDirective(routeTemplate, line.LineNumber, line.Text.Trim()));
            }

            return routes;
        }

        /// <summary>
        /// Extracts the first Blazor `@layout` directive from Razor lines.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <returns>The layout name when present; otherwise, <see langword="null" />.</returns>
        private static string? ExtractLayoutName(IReadOnlyList<RazorLine> lines)
        {
            // Layout directives are project-local type names in the first slice and are not semantically bound.
            return lines.Select(line => LayoutDirectiveRegex().Match(line.Text)).FirstOrDefault(match => match.Success)?.Groups["layout"].Value.Trim();
        }

        /// <summary>
        /// Extracts Blazor `@inject` directives from Razor lines.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <returns>Injected service descriptors in source order.</returns>
        private static IReadOnlyList<InjectedService> ExtractInjectedServices(IReadOnlyList<RazorLine> lines)
        {
            // Inject directives expose type and member names directly in markup, so first-slice detection can remain purely textual.
            List<InjectedService> services = [];
            foreach (RazorLine line in lines)
            {
                Match match = InjectDirectiveRegex().Match(line.Text);
                if (match.Success)
                {
                    services.Add(new InjectedService(match.Groups["type"].Value.Trim(), match.Groups["member"].Value.Trim(), line.LineNumber, line.Text.Trim()));
                }
            }

            return services;
        }

        /// <summary>
        /// Extracts component parameters from `[Parameter]` and `[CascadingParameter]` property declarations.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <returns>Parameter property names in source order.</returns>
        private static IReadOnlyList<string> ExtractParameters(IReadOnlyList<RazorLine> lines)
        {
            // The scanner keeps one line of attribute state so common multi-line attribute/property declarations are supported without compiling Razor.
            List<string> parameters = [];
            bool pendingParameterAttribute = false;
            foreach (RazorLine line in lines)
            {
                if (ParameterAttributeRegex().IsMatch(line.Text))
                {
                    pendingParameterAttribute = true;
                    Match sameLineProperty = PropertyDeclarationRegex().Match(line.Text);
                    if (sameLineProperty.Success)
                    {
                        parameters.Add(sameLineProperty.Groups["name"].Value.Trim());
                        pendingParameterAttribute = false;
                    }

                    continue;
                }

                if (!pendingParameterAttribute)
                {
                    continue;
                }

                Match property = PropertyDeclarationRegex().Match(line.Text);
                if (property.Success)
                {
                    parameters.Add(property.Groups["name"].Value.Trim());
                    pendingParameterAttribute = false;
                }
            }

            return parameters;
        }

        /// <summary>
        /// Extracts an authorization role or policy marker from component markup.
        /// </summary>
        /// <param name="content">The Razor file content.</param>
        /// <returns>The first detected authorization role or policy value; otherwise, <see langword="null" />.</returns>
        private static string? ExtractAuthorizationPolicy(string content)
        {
            // Authorization metadata can appear as @attribute [Authorize(...)] or AuthorizeView attributes; the first literal value is enough for the foundation slice.
            Match attributeMatch = AuthorizeAttributeRegex().Match(content);
            if (attributeMatch.Success)
            {
                return attributeMatch.Groups["value"].Value.Trim();
            }

            Match authorizeViewMatch = AuthorizeViewRegex().Match(content);
            return authorizeViewMatch.Success ? authorizeViewMatch.Groups["value"].Value.Trim() : null;
        }

        /// <summary>
        /// Extracts child component and dynamic component usages from Razor markup lines.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <param name="currentComponentName">The component name for the artifact being analyzed.</param>
        /// <returns>Component usage descriptors in source order.</returns>
        private static IReadOnlyList<ComponentUsage> ExtractComponentUsages(IReadOnlyList<RazorLine> lines, string currentComponentName)
        {
            // Blazor component tags conventionally start with an uppercase letter; common built-in controls are filtered into the UI control path instead.
            List<ComponentUsage> usages = [];
            foreach (RazorLine line in lines)
            {
                MatchCollection matches = ComponentTagRegex().Matches(line.Text);
                foreach (Match match in matches)
                {
                    string componentName = match.Groups["name"].Value.Trim();
                    if (StringComparer.Ordinal.Equals(componentName, currentComponentName) || IsBuiltInBlazorControl(componentName))
                    {
                        continue;
                    }

                    bool isDynamic = StringComparer.Ordinal.Equals(componentName, "DynamicComponent");
                    usages.Add(new ComponentUsage(componentName, isDynamic, line.LineNumber, line.Text.Trim()));
                }
            }

            return usages;
        }

        /// <summary>
        /// Extracts UI event and callback handler usages from Razor markup lines.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <returns>UI event descriptors in source order.</returns>
        private static IReadOnlyList<UiEventUsage> ExtractUiEventUsages(IReadOnlyList<RazorLine> lines)
        {
            // The matcher handles both Razor DOM events such as @onclick and component callback attributes such as OnValidSubmit.
            List<UiEventUsage> usages = [];
            foreach (RazorLine line in lines)
            {
                foreach (Match match in EventAttributeRegex().Matches(line.Text))
                {
                    string eventName = NormalizeEventName(match.Groups["event"].Value.Trim());
                    string handlerName = NormalizeHandlerExpression(match.Groups["handler"].Value.Trim());
                    if (!string.IsNullOrWhiteSpace(handlerName))
                    {
                        usages.Add(new UiEventUsage(eventName, handlerName, line.LineNumber, line.Text.Trim()));
                    }
                }
            }

            return usages;
        }

        /// <summary>
        /// Extracts form and validation control usages from Razor markup lines.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <returns>UI control descriptors in source order.</returns>
        private static IReadOnlyList<UiControlUsage> ExtractUiControlUsages(IReadOnlyList<RazorLine> lines)
        {
            // Only controls with architectural value are captured here; generic HTML elements are intentionally ignored.
            List<UiControlUsage> usages = [];
            foreach (RazorLine line in lines)
            {
                foreach (Match match in ControlTagRegex().Matches(line.Text))
                {
                    usages.Add(new UiControlUsage(match.Groups["name"].Value.Trim(), line.LineNumber, line.Text.Trim()));
                }
            }

            return usages;
        }

        /// <summary>
        /// Extracts render fragment member names from Razor code blocks and markup references.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <returns>Render fragment names in source order.</returns>
        private static IReadOnlyList<string> ExtractRenderFragments(IReadOnlyList<RazorLine> lines)
        {
            // RenderFragment members represent projected UI content; a representative name is recorded on the component for impact analysis.
            List<string> renderFragments = [];
            foreach (RazorLine line in lines)
            {
                Match match = RenderFragmentRegex().Match(line.Text);
                if (match.Success)
                {
                    renderFragments.Add(match.Groups["name"].Value.Trim());
                }
            }

            return renderFragments;
        }

        /// <summary>
        /// Extracts a Blazor render-mode directive when statically visible.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <returns>A render-mode descriptor when a directive is present; otherwise, <see langword="null" />.</returns>
        private static RenderModeUsage? ExtractRenderMode(IReadOnlyList<RazorLine> lines)
        {
            // Literal render mode identifiers are captured directly; expressions are marked unknown because runtime state controls the mode.
            foreach (RazorLine line in lines)
            {
                Match match = RenderModeRegex().Match(line.Text);
                if (!match.Success)
                {
                    continue;
                }

                string expression = match.Groups["mode"].Value.Trim();
                if (expression.StartsWith("@(", StringComparison.Ordinal) || expression.StartsWith('@'))
                {
                    return new RenderModeUsage(null, "Render mode is computed from runtime state.", line.LineNumber, line.Text.Trim());
                }

                return new RenderModeUsage(expression, null, line.LineNumber, line.Text.Trim());
            }

            return null;
        }

        /// <summary>
        /// Extracts HTTP client method usages from Razor code lines when an injected HTTP client is available.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <param name="injectedServices">The injected service descriptors available to the component.</param>
        /// <returns>API usage descriptors in source order.</returns>
        private static IReadOnlyList<ApiUsage> ExtractApiUsages(IReadOnlyList<RazorLine> lines, IReadOnlyList<InjectedService> injectedServices)
        {
            // The correlation is intentionally conservative: only members injected as HttpClient are scanned for common HTTP call methods.
            HashSet<string> httpMembers = injectedServices.Where(service => service.ServiceType.EndsWith("HttpClient", StringComparison.OrdinalIgnoreCase)).Select(service => service.MemberName).ToHashSet(StringComparer.Ordinal);
            if (httpMembers.Count == 0)
            {
                return [];
            }

            List<ApiUsage> usages = [];
            foreach (RazorLine line in lines)
            {
                foreach (Match match in ApiCallRegex().Matches(line.Text))
                {
                    string memberName = match.Groups["member"].Value.Trim();
                    if (!httpMembers.Contains(memberName))
                    {
                        continue;
                    }

                    string methodName = match.Groups["method"].Value.Trim();
                    string? apiTarget = match.Groups["literal"].Success ? match.Groups["literal"].Value.Trim() : null;
                    usages.Add(new ApiUsage(methodName, apiTarget, line.LineNumber, line.Text.Trim()));
                }
            }

            return usages;
        }

        /// <summary>
        /// Extracts configuration indexer usages from Razor code lines when an injected configuration service is available.
        /// </summary>
        /// <param name="lines">The Razor line descriptors to inspect.</param>
        /// <param name="injectedServices">The injected service descriptors available to the component.</param>
        /// <returns>Configuration usage descriptors in source order.</returns>
        private static IReadOnlyList<ConfigurationUsage> ExtractConfigurationUsages(IReadOnlyList<RazorLine> lines, IReadOnlyList<InjectedService> injectedServices)
        {
            // IConfiguration indexers with literal keys become direct configuration facts; variable indexers are retained as unknowns.
            HashSet<string> configurationMembers = injectedServices.Where(service => service.ServiceType.EndsWith("IConfiguration", StringComparison.OrdinalIgnoreCase)).Select(service => service.MemberName).ToHashSet(StringComparer.Ordinal);
            if (configurationMembers.Count == 0)
            {
                return [];
            }

            List<ConfigurationUsage> usages = [];
            foreach (RazorLine line in lines)
            {
                foreach (Match match in ConfigurationIndexerRegex().Matches(line.Text))
                {
                    string memberName = match.Groups["member"].Value.Trim();
                    if (!configurationMembers.Contains(memberName))
                    {
                        continue;
                    }

                    string? configurationKey = match.Groups["literal"].Success ? match.Groups["literal"].Value.Trim() : null;
                    usages.Add(new ConfigurationUsage(configurationKey, line.LineNumber, line.Text.Trim()));
                }
            }

            return usages;
        }

        /// <summary>
        /// Determines whether a component-looking tag is a built-in Blazor form or validation control.
        /// </summary>
        /// <param name="componentName">The tag name to inspect.</param>
        /// <returns><see langword="true" /> when the tag is handled as a UI control; otherwise, <see langword="false" />.</returns>
        private static bool IsBuiltInBlazorControl(string componentName)
        {
            // Built-in controls are emitted through UiControl so they do not masquerade as project components.
            return StringComparer.Ordinal.Equals(componentName, "EditForm")
                || StringComparer.Ordinal.Equals(componentName, "DataAnnotationsValidator")
                || StringComparer.Ordinal.Equals(componentName, "ValidationSummary")
                || StringComparer.Ordinal.Equals(componentName, "ValidationMessage")
                || StringComparer.Ordinal.Equals(componentName, "InputText")
                || StringComparer.Ordinal.Equals(componentName, "InputNumber")
                || StringComparer.Ordinal.Equals(componentName, "InputSelect")
                || StringComparer.Ordinal.Equals(componentName, "InputCheckbox")
                || StringComparer.Ordinal.Equals(componentName, "InputDate");
        }

        /// <summary>
        /// Normalizes a Razor event handler expression into a stable method-like token.
        /// </summary>
        /// <param name="handlerExpression">The event handler expression captured from markup.</param>
        /// <returns>A stable handler token, or an empty string when the expression is not useful.</returns>
        private static string NormalizeHandlerExpression(string handlerExpression)
        {
            // Method group and simple lambda forms are reduced to the visible method token; complex expressions retain a short deterministic token.
            string normalized = handlerExpression.Trim().TrimStart('@').Trim();
            Match methodMatch = HandlerMethodRegex().Match(normalized);
            return methodMatch.Success ? methodMatch.Groups["method"].Value.Trim() : normalized;
        }

        /// <summary>
        /// Normalizes Razor event attribute names into stable metadata tokens.
        /// </summary>
        /// <param name="eventName">The raw event or callback attribute name.</param>
        /// <returns>The normalized event metadata value.</returns>
        private static string NormalizeEventName(string eventName)
        {
            // Razor DOM events use @on prefixes in markup; graph metadata stores the event name without the Razor directive marker.
            string normalized = eventName.Trim();
            return normalized.StartsWith("@on", StringComparison.Ordinal) ? normalized[3..] : normalized;
        }

        /// <summary>
        /// Extracts the first route parameter token from a route template.
        /// </summary>
        /// <param name="routeTemplate">The route template to inspect.</param>
        /// <returns>The route parameter token without braces, or <see langword="null" /> when none is present.</returns>
        private static string? ExtractFirstRouteParameter(string? routeTemplate)
        {
            // Route parameters are useful metadata for impact analysis even before later work items expand interaction extraction.
            if (routeTemplate is null)
            {
                return null;
            }

            Match match = RouteParameterRegex().Match(routeTemplate);
            return match.Success ? match.Groups["parameter"].Value.Trim() : null;
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
        /// Adds the first non-empty value from a sequence to metadata.
        /// </summary>
        /// <param name="values">The metadata dictionary being built.</param>
        /// <param name="key">The metadata property name.</param>
        /// <param name="candidates">The candidate values in preferred order.</param>
        private static void AddFirst(Dictionary<string, object?> values, string key, IEnumerable<string?> candidates)
        {
            // The first-slice metadata records one representative value while later work items can expand repeated details into richer nodes and edges.
            string? value = candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
            AddOptional(values, key, value);
        }

        /// <summary>
        /// Creates a regex for Blazor `@page` directives with optional literal route templates.
        /// </summary>
        /// <returns>A regex that captures a `route` group when a literal route template is present.</returns>
        [GeneratedRegex("^\\s*@page(?:\\s+\\\"(?<route>[^\\\"]*)\\\")?", RegexOptions.CultureInvariant)]
        private static partial Regex PageDirectiveRegex();

        /// <summary>
        /// Creates a regex for Blazor `@layout` directives.
        /// </summary>
        /// <returns>A regex that captures the layout type token.</returns>
        [GeneratedRegex("^\\s*@layout\\s+(?<layout>[A-Za-z_][A-Za-z0-9_\\.]*)", RegexOptions.CultureInvariant)]
        private static partial Regex LayoutDirectiveRegex();

        /// <summary>
        /// Creates a regex for Blazor `@inject` directives.
        /// </summary>
        /// <returns>A regex that captures injected service type and member names.</returns>
        [GeneratedRegex("^\\s*@inject\\s+(?<type>[^\\s]+)\\s+(?<member>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
        private static partial Regex InjectDirectiveRegex();

        /// <summary>
        /// Creates a regex for parameter attributes.
        /// </summary>
        /// <returns>A regex that recognizes `[Parameter]` and `[CascadingParameter]` markers.</returns>
        [GeneratedRegex("\\[(?:Cascading)?Parameter(?:\\([^\\)]*\\))?\\]", RegexOptions.CultureInvariant)]
        private static partial Regex ParameterAttributeRegex();

        /// <summary>
        /// Creates a regex for simple C# property declarations inside Razor code blocks.
        /// </summary>
        /// <returns>A regex that captures the property name.</returns>
        [GeneratedRegex("\\bpublic\\s+[^;{}=]+?\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*\\{", RegexOptions.CultureInvariant)]
        private static partial Regex PropertyDeclarationRegex();

        /// <summary>
        /// Creates a regex for authorization attributes with literal Roles or Policy values.
        /// </summary>
        /// <returns>A regex that captures the first literal authorization value.</returns>
        [GeneratedRegex("Authorize\\s*\\([^\\)]*(?:Roles|Policy)\\s*=\\s*\\\"(?<value>[^\\\"]+)\\\"", RegexOptions.CultureInvariant)]
        private static partial Regex AuthorizeAttributeRegex();

        /// <summary>
        /// Creates a regex for AuthorizeView attributes with literal Roles or Policy values.
        /// </summary>
        /// <returns>A regex that captures the first literal authorization value.</returns>
        [GeneratedRegex("<AuthorizeView[^>]*(?:Roles|Policy)\\s*=\\s*\\\"(?<value>[^\\\"]+)\\\"", RegexOptions.CultureInvariant)]
        private static partial Regex AuthorizeViewRegex();

        /// <summary>
        /// Creates a regex for route parameter tokens in route templates.
        /// </summary>
        /// <returns>A regex that captures the parameter token without braces.</returns>
        [GeneratedRegex("\\{(?<parameter>[^}]+)\\}", RegexOptions.CultureInvariant)]
        private static partial Regex RouteParameterRegex();

        /// <summary>
        /// Creates a regex for component-shaped Razor tags that begin with an uppercase character.
        /// </summary>
        /// <returns>A regex that captures the tag name.</returns>
        [GeneratedRegex("<(?<name>[A-Z][A-Za-z0-9_\\.]*)\\b", RegexOptions.CultureInvariant)]
        private static partial Regex ComponentTagRegex();

        /// <summary>
        /// Creates a regex for Blazor event and callback attributes with literal handler expressions.
        /// </summary>
        /// <returns>A regex that captures the event/callback name and handler expression.</returns>
        [GeneratedRegex("(?<event>@on[A-Za-z0-9_:-]+|On[A-Za-z0-9_]+)\\s*=\\s*\"(?<handler>[^\"]+)\"", RegexOptions.CultureInvariant)]
        private static partial Regex EventAttributeRegex();

        /// <summary>
        /// Creates a regex for Blazor form and validation control tags.
        /// </summary>
        /// <returns>A regex that captures the control tag name.</returns>
        [GeneratedRegex("<(?<name>EditForm|DataAnnotationsValidator|ValidationSummary|ValidationMessage|InputText|InputNumber|InputSelect|InputCheckbox|InputDate)\\b", RegexOptions.CultureInvariant)]
        private static partial Regex ControlTagRegex();

        /// <summary>
        /// Creates a regex for render fragment member declarations.
        /// </summary>
        /// <returns>A regex that captures the render fragment member name.</returns>
        [GeneratedRegex("\\bRenderFragment(?:<[^>]+>)?\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\b", RegexOptions.CultureInvariant)]
        private static partial Regex RenderFragmentRegex();

        /// <summary>
        /// Creates a regex for Blazor render mode directives.
        /// </summary>
        /// <returns>A regex that captures the render mode expression.</returns>
        [GeneratedRegex("^\\s*@rendermode\\s+(?<mode>.+?)\\s*$", RegexOptions.CultureInvariant)]
        private static partial Regex RenderModeRegex();

        /// <summary>
        /// Creates a regex for HTTP client method invocations with a literal or computed first argument.
        /// </summary>
        /// <returns>A regex that captures the injected member, method, and optional literal target.</returns>
        [GeneratedRegex("(?<member>[A-Za-z_][A-Za-z0-9_]*)\\.(?<method>Get(?:FromJson)?Async|Post(?:AsJson)?Async|Put(?:AsJson)?Async|DeleteAsync)\\s*(?:<[^>]+>)?\\s*\\(\\s*(?:\"(?<literal>[^\"]+)\"|(?<computed>[A-Za-z_@][A-Za-z0-9_@\\.]*))", RegexOptions.CultureInvariant)]
        private static partial Regex ApiCallRegex();

        /// <summary>
        /// Creates a regex for configuration indexer access with a literal or computed key.
        /// </summary>
        /// <returns>A regex that captures the injected configuration member and optional literal key.</returns>
        [GeneratedRegex("(?<member>[A-Za-z_][A-Za-z0-9_]*)\\s*\\[\\s*(?:\"(?<literal>[^\"]+)\"|(?<computed>[A-Za-z_@][A-Za-z0-9_@\\.]*))\\s*\\]", RegexOptions.CultureInvariant)]
        private static partial Regex ConfigurationIndexerRegex();

        /// <summary>
        /// Creates a regex that extracts a method token from a Razor handler expression.
        /// </summary>
        /// <returns>A regex that captures the first method-looking token.</returns>
        [GeneratedRegex("(?<method>[A-Za-z_][A-Za-z0-9_]*)\\s*(?:\\(|$)", RegexOptions.CultureInvariant)]
        private static partial Regex HandlerMethodRegex();

        /// <summary>
        /// Describes one discovered Blazor project.
        /// </summary>
        /// <param name="AbsoluteProjectPath">The absolute project path used for artifact ownership checks.</param>
        /// <param name="RelativeProjectPath">The repository-relative project path used for stable keys.</param>
        /// <param name="ProjectName">The display name of the project.</param>
        /// <param name="TargetFramework">The target framework value read from project metadata.</param>
        /// <param name="HostingModel">The conservative Blazor hosting classification.</param>
        private sealed record BlazorProjectContext(string AbsoluteProjectPath, string RelativeProjectPath, string ProjectName, string TargetFramework, string HostingModel);

        /// <summary>
        /// Describes one discovered Blazor Razor artifact and its owning project.
        /// </summary>
        /// <param name="Project">The project that owns the artifact.</param>
        /// <param name="AbsolutePath">The absolute artifact path used for file reads.</param>
        /// <param name="RelativePath">The repository-relative artifact path used for evidence and stable keys.</param>
        /// <param name="ComponentName">The component display name inferred from the file name.</param>
        private sealed record BlazorArtifactContext(BlazorProjectContext Project, string AbsolutePath, string RelativePath, string ComponentName);

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
        /// <param name="HostingModel">The Blazor hosting model classification or Unknown.</param>
        private sealed record ProjectMetadata(string RelativeProjectPath, string ProjectName, string TargetFramework, string HostingModel);

        /// <summary>
        /// Describes a parsed `@page` directive.
        /// </summary>
        /// <param name="RouteTemplate">The literal route template when present.</param>
        /// <param name="LineNumber">The one-based directive line number.</param>
        /// <param name="SourceText">The directive source text used for evidence.</param>
        private sealed record RouteDirective(string? RouteTemplate, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a parsed `@inject` directive.
        /// </summary>
        /// <param name="ServiceType">The injected service type token.</param>
        /// <param name="MemberName">The injected member name token.</param>
        /// <param name="LineNumber">The one-based directive line number.</param>
        /// <param name="SourceText">The directive source text used for evidence.</param>
        private sealed record InjectedService(string ServiceType, string MemberName, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a parsed child component tag or dynamic component marker.
        /// </summary>
        /// <param name="ComponentName">The component tag name visible in markup.</param>
        /// <param name="IsDynamic">Whether the usage is a `DynamicComponent` target that cannot be statically resolved.</param>
        /// <param name="LineNumber">The one-based markup line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record ComponentUsage(string ComponentName, bool IsDynamic, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a parsed UI event or component callback binding.
        /// </summary>
        /// <param name="EventName">The event or callback attribute name.</param>
        /// <param name="HandlerName">The handler method or expression token.</param>
        /// <param name="LineNumber">The one-based markup line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record UiEventUsage(string EventName, string HandlerName, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a parsed form or validation UI control tag.
        /// </summary>
        /// <param name="ControlName">The control tag name visible in markup.</param>
        /// <param name="LineNumber">The one-based markup line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record UiControlUsage(string ControlName, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a parsed render mode directive.
        /// </summary>
        /// <param name="RenderModeName">The literal render mode name when statically available.</param>
        /// <param name="UnknownReason">The reason the render mode is unknown when computed dynamically.</param>
        /// <param name="LineNumber">The one-based directive line number.</param>
        /// <param name="SourceText">The directive source text used for evidence.</param>
        private sealed record RenderModeUsage(string? RenderModeName, string? UnknownReason, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a parsed HTTP client API usage.
        /// </summary>
        /// <param name="MethodName">The HTTP client method name.</param>
        /// <param name="ApiTarget">The literal API target when statically available.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record ApiUsage(string MethodName, string? ApiTarget, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a parsed configuration indexer usage.
        /// </summary>
        /// <param name="ConfigurationKey">The literal configuration key when statically available.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record ConfigurationUsage(string? ConfigurationKey, int LineNumber, string SourceText);
    }
}