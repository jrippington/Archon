using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Extractors.Ui;
using Archon.Roslyn.SemanticModel;

namespace Archon.Extractors.Maui
{
    /// <summary>
    /// Extracts WP011 .NET MAUI XAML, Shell, platform-head, navigation, and dependency facts from repository source into shared graph contracts.
    /// </summary>
    /// <remarks>
    /// The extractor performs deterministic file analysis only. It does not evaluate MSBuild, require MAUI workloads, load XAML, instantiate controls, start platform applications, open databases, or write directly to persistence.
    /// </remarks>
    public sealed partial class MauiXamlExtractor
    {
        /// <summary>
        /// Extracts MAUI application, Shell, route, page, view, resource, style, binding, command, handler, platform-head, navigation, view-model, service, data-access, evidence, warning, and unknown facts.
        /// </summary>
        /// <param name="request">The repository-scoped MAUI extraction request.</param>
        /// <param name="cancellationToken">The cancellation token that stops file discovery and artifact analysis.</param>
        /// <returns>A result containing the graph-ready snapshot emitted by this extractor.</returns>
        public async Task<MauiXamlExtractionResult> ExtractAsync(MauiXamlExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // Extraction is separated into discovery, repository context indexing, project projection, and artifact analysis so partial results remain deterministic.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            ArchitectureSnapshotAccumulator accumulator = new();
            IReadOnlyList<MauiProjectContext> projects = DiscoverProjects(request.RepositoryRootDirectory);
            IReadOnlyList<MauiArtifactContext> artifacts = DiscoverArtifacts(request.RepositoryRootDirectory, projects);
            MauiRepositoryContext repositoryContext = await BuildRepositoryContextAsync(projects, artifacts, cancellationToken).ConfigureAwait(false);

            foreach (MauiProjectContext project in projects.Where(project => artifacts.Any(artifact => StringComparer.Ordinal.Equals(artifact.Project.RelativeProjectPath, project.RelativeProjectPath))))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateProjectAndApplication(request, accumulator, project, artifacts, repositoryContext);
            }

            foreach (MauiArtifactContext artifact in artifacts.Where(artifact => artifact.ArtifactKind is MauiArtifactKind.Application or MauiArtifactKind.Shell or MauiArtifactKind.Page or MauiArtifactKind.View or MauiArtifactKind.ResourceDictionary).OrderBy(artifact => artifact.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeArtifact(request, accumulator, repositoryContext, artifact);
            }

            return new MauiXamlExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Discovers MAUI-capable projects from project metadata, package references, target frameworks, XAML artifacts, and source symbols.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <returns>Project contexts ordered by repository-relative path.</returns>
        private static IReadOnlyList<MauiProjectContext> DiscoverProjects(string repositoryRootDirectory)
        {
            // Discovery reads static project XML only so MAUI workloads are not required on the machine running extraction.
            if (!Directory.Exists(repositoryRootDirectory))
            {
                return [];
            }

            List<MauiProjectContext> projects = [];
            IEnumerable<string> projectPaths = Directory.EnumerateFiles(repositoryRootDirectory, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(repositoryRootDirectory, "*.vbproj", SearchOption.AllDirectories))
                .Where(IsRepositorySourcePath)
                .Order(StringComparer.OrdinalIgnoreCase);

            foreach (string projectPath in projectPaths)
            {
                ProjectMetadata metadata = ReadProjectMetadata(repositoryRootDirectory, projectPath);
                if (!metadata.IsMauiCandidate)
                {
                    continue;
                }

                projects.Add(new MauiProjectContext(projectPath, metadata.RelativeProjectPath, metadata.ProjectName, metadata.TargetFrameworks, metadata.Language, metadata.PackageIdentities, metadata.PlatformHeads));
            }

            return projects;
        }

        /// <summary>
        /// Discovers XAML and source artifacts that belong to discovered MAUI projects.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="projects">The MAUI project contexts that can own artifacts.</param>
        /// <returns>Artifact contexts ordered by repository-relative path.</returns>
        private static IReadOnlyList<MauiArtifactContext> DiscoverArtifacts(string repositoryRootDirectory, IReadOnlyList<MauiProjectContext> projects)
        {
            // MAUI UI structure spans XAML, code-behind, MauiProgram startup source, and platform folders, so source and markup are discovered together.
            if (!Directory.Exists(repositoryRootDirectory) || projects.Count == 0)
            {
                return [];
            }

            List<MauiArtifactContext> artifacts = [];
            IEnumerable<string> artifactPaths = Directory.EnumerateFiles(repositoryRootDirectory, "*.xaml", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(repositoryRootDirectory, "*.cs", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(repositoryRootDirectory, "*.vb", SearchOption.AllDirectories))
                .Where(IsRepositorySourcePath)
                .Order(StringComparer.OrdinalIgnoreCase);

            foreach (string artifactPath in artifactPaths)
            {
                MauiProjectContext? project = FindNearestProject(projects, artifactPath);
                if (project is null)
                {
                    continue;
                }

                string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRootDirectory, artifactPath);
                string content = File.ReadAllText(artifactPath);
                MauiArtifactKind artifactKind = ClassifyArtifact(relativePath, content);
                string typeName = ExtractPrimaryTypeName(relativePath, content, artifactKind);
                artifacts.Add(new MauiArtifactContext(project, artifactPath, relativePath, typeName, artifactKind));
            }

            return artifacts;
        }

        /// <summary>
        /// Builds repository-wide MAUI context used to correlate XAML, startup source, code-behind, view models, services, data access, routes, and handlers.
        /// </summary>
        /// <param name="projects">The discovered MAUI projects.</param>
        /// <param name="artifacts">The discovered MAUI artifacts.</param>
        /// <param name="cancellationToken">The cancellation token that stops source loading.</param>
        /// <returns>A repository context used while analyzing MAUI artifacts.</returns>
        private static async Task<MauiRepositoryContext> BuildRepositoryContextAsync(IReadOnlyList<MauiProjectContext> projects, IReadOnlyList<MauiArtifactContext> artifacts, CancellationToken cancellationToken)
        {
            // Repository context indexes are built once so per-artifact projection can avoid repeated scans and stable-key ordering stays deterministic.
            Dictionary<string, string> sourceByPath = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> viewModelTypeNames = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<EventUsage>> eventsByType = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<ServiceUsage>> serviceUsagesByType = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<DataAccessUsage>> dataAccessUsagesByType = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<NavigationUsage>> navigationUsagesByType = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<RouteUsage>> routesByProject = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, IReadOnlyList<HandlerUsage>> handlersByProject = new(StringComparer.OrdinalIgnoreCase);

            foreach (MauiArtifactContext artifact in artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string content = await File.ReadAllTextAsync(artifact.AbsolutePath, cancellationToken).ConfigureAwait(false);
                sourceByPath[artifact.RelativePath] = content;

                if (artifact.ArtifactKind is not MauiArtifactKind.Code)
                {
                    continue;
                }

                string? declaredTypeName = ExtractCodeTypeName(content);
                string ownerTypeName = declaredTypeName ?? artifact.TypeName;
                foreach (string viewModelTypeName in ExtractRepositoryViewModelTypeNames(content))
                {
                    viewModelTypeNames.Add(viewModelTypeName);
                }

                eventsByType[ownerTypeName] = ExtractCodeBehindEventHandlers(content);
                serviceUsagesByType[ownerTypeName] = ExtractServiceUsages(content);
                dataAccessUsagesByType[ownerTypeName] = ExtractDataAccessUsages(content, projects.FirstOrDefault(project => StringComparer.Ordinal.Equals(project.RelativeProjectPath, artifact.Project.RelativeProjectPath))?.PackageIdentities ?? []);
                navigationUsagesByType[ownerTypeName] = ExtractCodeBehindNavigation(content);

                if (artifact.RelativePath.EndsWith("MauiProgram.cs", StringComparison.OrdinalIgnoreCase) || artifact.RelativePath.EndsWith("MauiProgram.vb", StringComparison.OrdinalIgnoreCase))
                {
                    handlersByProject[artifact.Project.RelativeProjectPath] = ExtractHandlerUsages(content, artifact.RelativePath);
                }

                IReadOnlyList<RouteUsage> sourceRoutes = ExtractSourceRoutes(content);
                if (sourceRoutes.Count > 0)
                {
                    routesByProject[artifact.Project.RelativeProjectPath] = routesByProject.TryGetValue(artifact.Project.RelativeProjectPath, out IReadOnlyList<RouteUsage>? existing) ? existing.Concat(sourceRoutes).ToArray() : sourceRoutes;
                }
            }

            return new MauiRepositoryContext(sourceByPath, viewModelTypeNames, eventsByType, serviceUsagesByType, dataAccessUsagesByType, navigationUsagesByType, routesByProject, handlersByProject);
        }

        /// <summary>
        /// Adds project, application, platform-head, source route, and handler facts for one MAUI project.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="project">The project context being represented.</param>
        /// <param name="artifacts">The discovered MAUI artifacts used to resolve application definitions.</param>
        /// <param name="repositoryContext">The repository context that supplies source content, routes, handlers, and dependency metadata.</param>
        private static void AccumulateProjectAndApplication(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiProjectContext project, IReadOnlyList<MauiArtifactContext> artifacts, MauiRepositoryContext repositoryContext)
        {
            // Project and application facts give every MAUI UI node stable ownership when this extractor runs independently from project inventory.
            StableKey projectStableKey = StableKeyGenerator.ForProject(project.RelativeProjectPath);
            EvidenceRecord projectEvidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(project.RelativeProjectPath, 1, 1, project.ProjectName), "Maui", "Application", "ProjectMetadata", Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(projectEvidence);
            string platformHead = string.Join(",", project.PlatformHeads);
            bool platformUnknown = project.PlatformHeads.Count == 0;
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, projectStableKey, NodeKind.Project, project.ProjectName, project.RelativeProjectPath, project.RelativeProjectPath, project.Language, projectStableKey, null, Confidence.High, UnknownState.Known, projectEvidence.StableKey, new Dictionary<string, object?>
            {
                ["detectionMode"] = "ProjectMetadata",
                ["language"] = project.Language,
                ["packageIdentity"] = string.Join(",", project.PackageIdentities),
                ["platformHead"] = platformUnknown ? "Unknown" : platformHead,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = project.RelativeProjectPath,
                ["targetFramework"] = string.Join(";", project.TargetFrameworks),
                ["uiFramework"] = "Maui"
            }));

            MauiArtifactContext? applicationArtifact = artifacts.FirstOrDefault(artifact => StringComparer.Ordinal.Equals(artifact.Project.RelativeProjectPath, project.RelativeProjectPath) && artifact.ArtifactKind is MauiArtifactKind.Application);
            MauiArtifactContext? shellArtifact = artifacts.FirstOrDefault(artifact => StringComparer.Ordinal.Equals(artifact.Project.RelativeProjectPath, project.RelativeProjectPath) && artifact.ArtifactKind is MauiArtifactKind.Shell);
            UnknownState unknownState = platformUnknown ? UnknownState.Unknown("MAUI platform heads could not be resolved statically.") : UnknownState.Known;
            Confidence confidence = platformUnknown ? Confidence.Low : Confidence.High;
            string sourcePath = applicationArtifact?.RelativePath ?? shellArtifact?.RelativePath ?? project.RelativeProjectPath;
            StableKey applicationStableKey = UiStableKeyBuilder.Create("ui-application://", projectStableKey.Value, "Maui", string.Join(";", project.TargetFrameworks), shellArtifact?.TypeName ?? applicationArtifact?.TypeName ?? project.ProjectName, platformHead);
            EvidenceRecord applicationEvidence = applicationArtifact is null ? projectEvidence : CreateEvidence(request, applicationArtifact, repositoryContext.SourceByPath[applicationArtifact.RelativePath], "Application", "XamlApplication", confidence, unknownState);
            if (!ReferenceEquals(applicationEvidence, projectEvidence))
            {
                accumulator.AddEvidence(applicationEvidence);
            }

            accumulator.AddNode(CreateNode(request.SnapshotStableKey, applicationStableKey, NodeKind.UiApplication, project.ProjectName, sourcePath, project.ProjectName, project.Language, projectStableKey, projectStableKey, confidence, unknownState, applicationEvidence.StableKey, new Dictionary<string, object?>
            {
                ["confidenceReason"] = platformUnknown ? unknownState.UnknownReason : "Project metadata and application source identified the MAUI application.",
                ["detectionMode"] = applicationArtifact is null ? "ProjectMetadata" : "XamlApplication",
                ["hostingModel"] = "Hybrid",
                ["language"] = project.Language,
                ["packageIdentity"] = string.Join(",", project.PackageIdentities),
                ["platformHead"] = platformUnknown ? "Unknown" : platformHead,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = sourcePath,
                ["targetFramework"] = string.Join(";", project.TargetFrameworks),
                ["uiArtifactKind"] = "Application",
                ["uiFramework"] = "Maui"
            }));

            if (platformUnknown)
            {
                accumulator.AddWarning($"MAUI platform heads for {project.RelativeProjectPath} could not be fully resolved statically: {unknownState.UnknownReason}");
            }

            AccumulateProjectRoutes(request, accumulator, project, projectStableKey, shellArtifact ?? applicationArtifact, repositoryContext);
            AccumulateProjectHandlers(request, accumulator, project, projectStableKey, shellArtifact ?? applicationArtifact, repositoryContext);
        }

        /// <summary>
        /// Analyzes one MAUI XAML artifact and contributes graph facts for supported markup and source patterns.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="repositoryContext">The repository context used for source and symbol correlation.</param>
        /// <param name="artifact">The MAUI XAML artifact being analyzed.</param>
        private static void AnalyzeArtifact(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiRepositoryContext repositoryContext, MauiArtifactContext artifact)
        {
            // XAML parsing is best-effort; malformed artifacts become non-fatal warnings instead of aborting the UI slice.
            string content = repositoryContext.SourceByPath[artifact.RelativePath];
            XDocument? document = TryLoadXaml(content, artifact.RelativePath, accumulator);
            if (document?.Root is null)
            {
                return;
            }

            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            EvidenceRecord artifactEvidence = CreateEvidence(request, artifact, content, GetArtifactKindMetadata(artifact.ArtifactKind), "XamlMarkup", Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(artifactEvidence);
            StableKey ownerStableKey = CreateArtifactNode(request, accumulator, artifact, projectStableKey, artifactEvidence.StableKey);

            foreach (ResourceUsage resource in ExtractResources(document))
            {
                AccumulateResource(request, accumulator, artifact, projectStableKey, ownerStableKey, resource);
            }

            foreach (ControlUsage control in ExtractControls(document, artifact))
            {
                AccumulateControl(request, accumulator, artifact, projectStableKey, ownerStableKey, control);
            }

            foreach (RouteUsage route in ExtractMarkupRoutes(document))
            {
                AccumulateRoute(request, accumulator, artifact, projectStableKey, ownerStableKey, route);
            }

            foreach (BindingUsage binding in ExtractBindings(document))
            {
                AccumulateBinding(request, accumulator, artifact, projectStableKey, ownerStableKey, binding);
            }

            foreach (CommandUsage command in ExtractCommands(document))
            {
                AccumulateCommand(request, accumulator, artifact, projectStableKey, ownerStableKey, command);
            }

            foreach (EventUsage eventUsage in ExtractMarkupEvents(document).Concat(repositoryContext.EventsByType.TryGetValue(artifact.TypeName, out IReadOnlyList<EventUsage>? codeEvents) ? codeEvents : []))
            {
                AccumulateEvent(request, accumulator, artifact, projectStableKey, ownerStableKey, eventUsage);
            }

            foreach (NavigationUsage navigation in ExtractNavigation(document).Concat(repositoryContext.NavigationUsagesByType.TryGetValue(artifact.TypeName, out IReadOnlyList<NavigationUsage>? codeNavigation) ? codeNavigation : []))
            {
                AccumulateNavigation(request, accumulator, artifact, projectStableKey, ownerStableKey, navigation);
            }

            AccumulateViewModel(request, accumulator, repositoryContext, artifact, projectStableKey, ownerStableKey, document.Root);

            foreach (ServiceUsage serviceUsage in repositoryContext.ServiceUsagesByType.TryGetValue(artifact.TypeName, out IReadOnlyList<ServiceUsage>? services) ? services : [])
            {
                AccumulateServiceUsage(request, accumulator, artifact, projectStableKey, ownerStableKey, serviceUsage);
            }

            foreach (DataAccessUsage dataAccessUsage in repositoryContext.DataAccessUsagesByType.TryGetValue(artifact.TypeName, out IReadOnlyList<DataAccessUsage>? dataAccess) ? dataAccess : [])
            {
                AccumulateDataAccessUsage(request, accumulator, artifact, projectStableKey, ownerStableKey, dataAccessUsage);
            }

            AccumulateDynamicUnknowns(request, accumulator, artifact, projectStableKey, ownerStableKey, document.Root);
        }

        /// <summary>
        /// Creates the primary graph node for a MAUI XAML artifact.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives the graph node.</param>
        /// <param name="artifact">The MAUI artifact being represented.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="evidenceStableKey">The evidence stable key for the artifact.</param>
        /// <returns>The stable key of the created artifact node.</returns>
        private static StableKey CreateArtifactNode(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiArtifactContext artifact, StableKey projectStableKey, StableKey evidenceStableKey)
        {
            // Framework-specific subtypes remain metadata values while node kinds use the shared WP011 UI vocabulary.
            NodeKind nodeKind = artifact.ArtifactKind switch
            {
                MauiArtifactKind.Application => NodeKind.UiApplication,
                MauiArtifactKind.Shell => NodeKind.UiLayout,
                MauiArtifactKind.Page => NodeKind.UiPage,
                MauiArtifactKind.View => NodeKind.UiComponent,
                MauiArtifactKind.ResourceDictionary => NodeKind.UiResource,
                _ => NodeKind.UiComponent
            };
            StableKey nodeStableKey = CreateArtifactStableKey(artifact, projectStableKey);
            string artifactKind = GetArtifactKindMetadata(artifact.ArtifactKind);
            Dictionary<string, object?> metadata = CreateBaseMetadata(artifact.Project, projectStableKey, artifact.RelativePath, artifactKind, artifact.TypeName, "XamlMarkup");
            if (artifact.ArtifactKind is MauiArtifactKind.Page)
            {
                metadata["pageName"] = artifact.TypeName;
            }
            else if (artifact.ArtifactKind is MauiArtifactKind.Shell)
            {
                metadata["layoutName"] = artifact.TypeName;
            }
            else if (artifact.ArtifactKind is MauiArtifactKind.ResourceDictionary)
            {
                metadata["resourceKey"] = Path.GetFileName(artifact.RelativePath);
            }

            accumulator.AddNode(CreateNode(request.SnapshotStableKey, nodeStableKey, nodeKind, artifact.TypeName, artifact.RelativePath, artifact.TypeName, "XAML", projectStableKey, projectStableKey, Confidence.High, UnknownState.Known, evidenceStableKey, metadata));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DeclaresComponent, projectStableKey, nodeStableKey, evidenceStableKey, artifact.RelativePath, artifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["detectionMode"] = "XamlMarkup",
                ["uiArtifactKind"] = artifactKind,
                ["uiFramework"] = "Maui"
            }));
            return nodeStableKey;
        }

        /// <summary>
        /// Adds graph facts for MAUI routes discovered from project-level source such as AppShell code-behind.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="project">The MAUI project that owns the routes.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="fallbackArtifact">The optional XAML artifact used as the route source when no code artifact exists.</param>
        /// <param name="repositoryContext">The repository context containing source route observations.</param>
        private static void AccumulateProjectRoutes(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiProjectContext project, StableKey projectStableKey, MauiArtifactContext? fallbackArtifact, MauiRepositoryContext repositoryContext)
        {
            // Source-registered Shell routes belong to the project or Shell even when the target page is declared in code-behind.
            foreach (RouteUsage route in repositoryContext.RoutesByProject.TryGetValue(project.RelativeProjectPath, out IReadOnlyList<RouteUsage>? routes) ? routes : [])
            {
                MauiArtifactContext artifact = fallbackArtifact ?? new MauiArtifactContext(project, project.AbsoluteProjectPath, project.RelativeProjectPath, project.ProjectName, MauiArtifactKind.Application);
                AccumulateRoute(request, accumulator, artifact, projectStableKey, projectStableKey, route);
            }
        }

        /// <summary>
        /// Adds graph facts for MAUI handler registrations discovered from MauiProgram source.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="project">The MAUI project that owns the handlers.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="fallbackArtifact">The optional artifact used as fallback evidence context.</param>
        /// <param name="repositoryContext">The repository context containing handler observations.</param>
        private static void AccumulateProjectHandlers(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiProjectContext project, StableKey projectStableKey, MauiArtifactContext? fallbackArtifact, MauiRepositoryContext repositoryContext)
        {
            // Handler registrations are represented as command-style facts because the shared graph vocabulary has no handler-specific node kind.
            foreach (HandlerUsage handler in repositoryContext.HandlersByProject.TryGetValue(project.RelativeProjectPath, out IReadOnlyList<HandlerUsage>? handlers) ? handlers : [])
            {
                MauiArtifactContext artifact = new(project, project.AbsoluteProjectPath, handler.SourcePath, project.ProjectName, MauiArtifactKind.Code);
                StableKey handlerStableKey = UiStableKeyBuilder.Create("ui-command://", projectStableKey.Value, "Maui", "Handler", handler.HandlerName, handler.ControlType);
                EvidenceRecord evidence = CreateEvidence(request, artifact, handler.SourceText, "Command", "MauiHandler", Confidence.Medium, UnknownState.Known, handler.LineNumber);
                accumulator.AddEvidence(evidence);
                accumulator.AddNode(CreateNode(request.SnapshotStableKey, handlerStableKey, NodeKind.Command, handler.HandlerName, artifact.RelativePath, handler.HandlerName, project.Language, projectStableKey, projectStableKey, Confidence.Medium, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
                {
                    ["commandName"] = handler.HandlerName,
                    ["controlType"] = handler.ControlType,
                    ["detectionMode"] = "MauiHandler",
                    ["platformHead"] = string.Join(",", project.PlatformHeads),
                    ["projectKey"] = projectStableKey.Value,
                    ["sourcePath"] = artifact.RelativePath,
                    ["uiArtifactKind"] = "Command",
                    ["uiFramework"] = "Maui"
                }));
                accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesCommand, projectStableKey, handlerStableKey, evidence.StableKey, handler.HandlerName, artifact.RelativePath, Confidence.Medium, UnknownState.Known, new Dictionary<string, object?>
                {
                    ["commandName"] = handler.HandlerName,
                    ["controlType"] = handler.ControlType,
                    ["detectionMode"] = "MauiHandler",
                    ["uiFramework"] = "Maui"
                }));
            }
        }

        /// <summary>
        /// Adds graph facts for a MAUI Shell route observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact containing route evidence.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the route-declaring artifact.</param>
        /// <param name="route">The route observation.</param>
        private static void AccumulateRoute(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, RouteUsage route)
        {
            // Static Shell routes become UiRoute facts; computed route expressions are preserved as explicit unknowns.
            UnknownState unknownState = route.IsUnknown ? UnknownState.Unknown("MAUI Shell route is computed from runtime state.") : UnknownState.Known;
            Confidence confidence = route.IsUnknown ? Confidence.Low : Confidence.High;
            StableKey routeStableKey = UiStableKeyBuilder.Create("ui-route://", projectStableKey.Value, "Maui", artifact.RelativePath, route.RouteTemplate, route.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, route.SourceText, "Route", route.DetectionMode, confidence, unknownState, route.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, routeStableKey, NodeKind.UiRoute, route.RouteTemplate, artifact.RelativePath, route.RouteTemplate, "XAML", projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["confidenceReason"] = route.IsUnknown ? "Route value uses a runtime expression." : "Shell route is statically visible.",
                ["detectionMode"] = route.DetectionMode,
                ["platformHead"] = string.Join(",", artifact.Project.PlatformHeads),
                ["projectKey"] = projectStableKey.Value,
                ["routeTemplate"] = route.RouteTemplate,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Route",
                ["uiFramework"] = "Maui"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DeclaresUiRoute, ownerStableKey, routeStableKey, evidence.StableKey, route.RouteTemplate, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["detectionMode"] = route.DetectionMode,
                ["routeTemplate"] = route.RouteTemplate,
                ["uiFramework"] = "Maui"
            }));

            if (route.IsUnknown)
            {
                accumulator.AddWarning($"MAUI Shell route in {artifact.RelativePath} at line {route.LineNumber.ToString(CultureInfo.InvariantCulture)} is computed from runtime state.");
            }
        }

        /// <summary>
        /// Adds graph facts for a MAUI resource, style, or template observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the resource.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact using or declaring the resource.</param>
        /// <param name="resource">The resource observation.</param>
        private static void AccumulateResource(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, ResourceUsage resource)
        {
            // Resources are normalized into shared resource/style nodes while preserving MAUI style/template subtype metadata.
            UnknownState unknownState = resource.IsUnknown ? UnknownState.Unknown(resource.UnknownReason!) : UnknownState.Known;
            Confidence confidence = resource.IsUnknown ? Confidence.Low : Confidence.High;
            NodeKind nodeKind = resource.ArtifactKind is "Style" or "Template" ? NodeKind.UiStyle : NodeKind.UiResource;
            StableKey resourceStableKey = UiStableKeyBuilder.Create("ui-resource://", projectStableKey.Value, "Maui", artifact.RelativePath, resource.ArtifactKind, resource.Key, resource.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, resource.SourceText, resource.ArtifactKind, resource.DetectionMode, confidence, unknownState, resource.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, resourceStableKey, nodeKind, resource.Key, artifact.RelativePath, resource.Key, "XAML", projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["confidenceReason"] = resource.IsUnknown ? resource.UnknownReason : "Static MAUI resource evidence.",
                ["detectionMode"] = resource.DetectionMode,
                ["platformHead"] = string.Join(",", artifact.Project.PlatformHeads),
                ["projectKey"] = projectStableKey.Value,
                ["resourceKey"] = resource.Key,
                ["sourcePath"] = artifact.RelativePath,
                ["styleKey"] = nodeKind == NodeKind.UiStyle ? resource.Key : null,
                ["uiArtifactKind"] = resource.ArtifactKind,
                ["uiFramework"] = "Maui"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, resource.ArtifactKind is "Style" or "Template" ? EdgeKind.UsesStyle : EdgeKind.UsesUiResource, ownerStableKey, resourceStableKey, evidence.StableKey, resource.Key, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["detectionMode"] = resource.DetectionMode,
                ["resourceKey"] = resource.Key,
                ["styleKey"] = resource.ArtifactKind is "Style" or "Template" ? resource.Key : null,
                ["uiArtifactKind"] = resource.ArtifactKind,
                ["uiFramework"] = "Maui"
            }));

            if (resource.IsUnknown)
            {
                accumulator.AddWarning($"MAUI {resource.ArtifactKind.ToLowerInvariant()} in {artifact.RelativePath} has unresolved dynamic resource evidence: {resource.Key}.");
            }
        }

        /// <summary>
        /// Adds graph facts for a MAUI control or nested component observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the control.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact using the control.</param>
        /// <param name="control">The control observation.</param>
        private static void AccumulateControl(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, ControlUsage control)
        {
            // Named controls are represented as UiControl nodes, while project-local MAUI views are also queryable through component-style relationships.
            StableKey controlStableKey = UiStableKeyBuilder.Create("ui-control://", projectStableKey.Value, "Maui", artifact.RelativePath, control.ControlType, control.ControlName, control.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, control.SourceText, "Control", "XamlControl", Confidence.High, UnknownState.Known, control.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, controlStableKey, NodeKind.UiControl, control.ControlName, artifact.RelativePath, control.ControlName, "XAML", projectStableKey, ownerStableKey, Confidence.High, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["controlName"] = control.ControlName,
                ["controlType"] = control.ControlType,
                ["detectionMode"] = "XamlControl",
                ["platformHead"] = string.Join(",", artifact.Project.PlatformHeads),
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Control",
                ["uiFramework"] = "Maui"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, control.IsCustomComponent ? EdgeKind.UsesComponent : EdgeKind.UsesControl, ownerStableKey, controlStableKey, evidence.StableKey, control.ControlName, artifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["controlName"] = control.ControlName,
                ["controlType"] = control.ControlType,
                ["detectionMode"] = "XamlControl",
                ["uiFramework"] = "Maui"
            }));
        }

        /// <summary>
        /// Adds graph facts for a MAUI binding observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the binding.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the bound artifact.</param>
        /// <param name="binding">The binding observation.</param>
        private static void AccumulateBinding(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, BindingUsage binding)
        {
            // Unqualified `{Binding}` expressions are explicit unknowns because their runtime target depends on BindingContext shape.
            UnknownState unknownState = binding.IsUnknown ? UnknownState.Unknown("MAUI binding path could not be resolved statically.") : UnknownState.Known;
            Confidence confidence = binding.IsUnknown ? Confidence.Low : Confidence.High;
            StableKey bindingStableKey = UiStableKeyBuilder.Create("ui-binding://", projectStableKey.Value, "Maui", artifact.RelativePath, binding.PropertyName, binding.BindingPath, binding.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, binding.SourceText, "Binding", "XamlBinding", confidence, unknownState, binding.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, bindingStableKey, NodeKind.Binding, binding.BindingPath, artifact.RelativePath, binding.BindingPath, "XAML", projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["bindingPath"] = binding.BindingPath,
                ["confidenceReason"] = binding.IsUnknown ? "Binding expression did not include a static path." : "Binding expression included a static path.",
                ["detectionMode"] = "XamlBinding",
                ["platformHead"] = string.Join(",", artifact.Project.PlatformHeads),
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Binding",
                ["uiFramework"] = "Maui"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.BindsTo, ownerStableKey, bindingStableKey, evidence.StableKey, binding.BindingPath, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["bindingPath"] = binding.BindingPath,
                ["detectionMode"] = "XamlBinding",
                ["uiFramework"] = "Maui"
            }));

            if (binding.IsUnknown)
            {
                accumulator.AddWarning($"MAUI unresolved binding path in {artifact.RelativePath} at line {binding.LineNumber.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        /// <summary>
        /// Adds graph facts for a MAUI command binding observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the command binding.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact using the command.</param>
        /// <param name="command">The command observation.</param>
        private static void AccumulateCommand(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, CommandUsage command)
        {
            // Command bindings usually target view-model command properties and are represented separately from events.
            StableKey commandStableKey = UiStableKeyBuilder.Create("ui-command://", projectStableKey.Value, "Maui", artifact.RelativePath, command.CommandName, command.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, command.SourceText, "Command", "XamlCommand", Confidence.High, UnknownState.Known, command.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, commandStableKey, NodeKind.Command, command.CommandName, artifact.RelativePath, command.CommandName, "XAML", projectStableKey, ownerStableKey, Confidence.High, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["commandName"] = command.CommandName,
                ["detectionMode"] = "XamlCommand",
                ["platformHead"] = string.Join(",", artifact.Project.PlatformHeads),
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Command",
                ["uiFramework"] = "Maui"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesCommand, ownerStableKey, commandStableKey, evidence.StableKey, command.CommandName, artifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["commandName"] = command.CommandName,
                ["detectionMode"] = "XamlCommand",
                ["uiFramework"] = "Maui"
            }));
        }

        /// <summary>
        /// Adds graph facts for a MAUI routed event handler observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the routed event.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact handling the event.</param>
        /// <param name="eventUsage">The event observation.</param>
        private static void AccumulateEvent(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, EventUsage eventUsage)
        {
            // MAUI XAML events are represented by command nodes so handlers can be traversed uniformly with command facts.
            StableKey commandStableKey = UiStableKeyBuilder.Create("ui-command://", projectStableKey.Value, "Maui", artifact.RelativePath, eventUsage.HandlerName, eventUsage.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, eventUsage.SourceText, "Command", eventUsage.DetectionMode, Confidence.High, UnknownState.Known, eventUsage.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, commandStableKey, NodeKind.Command, eventUsage.HandlerName, artifact.RelativePath, eventUsage.HandlerName, "XAML", projectStableKey, ownerStableKey, Confidence.High, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["commandName"] = eventUsage.HandlerName,
                ["detectionMode"] = eventUsage.DetectionMode,
                ["eventName"] = eventUsage.EventName,
                ["platformHead"] = string.Join(",", artifact.Project.PlatformHeads),
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Command",
                ["uiFramework"] = "Maui"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.HandlesUiEvent, ownerStableKey, commandStableKey, evidence.StableKey, eventUsage.EventName, artifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["commandName"] = eventUsage.HandlerName,
                ["detectionMode"] = eventUsage.DetectionMode,
                ["eventName"] = eventUsage.EventName,
                ["uiFramework"] = "Maui"
            }));
        }

        /// <summary>
        /// Adds graph facts for a MAUI navigation observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains navigation evidence.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the navigating artifact.</param>
        /// <param name="navigation">The navigation observation.</param>
        private static void AccumulateNavigation(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, NavigationUsage navigation)
        {
            // Static Shell navigation targets become route nodes; computed navigation is recorded as an explicit unknown instead of a guessed route.
            UnknownState unknownState = navigation.IsUnknown ? UnknownState.Unknown("MAUI navigation target is computed from runtime state.") : UnknownState.Known;
            Confidence confidence = navigation.IsUnknown ? Confidence.Low : Confidence.High;
            StableKey targetStableKey = UiStableKeyBuilder.Create("ui-route://", projectStableKey.Value, "Maui", artifact.RelativePath, navigation.Target, navigation.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, navigation.SourceText, "Route", navigation.DetectionMode, confidence, unknownState, navigation.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, targetStableKey, NodeKind.UiRoute, navigation.Target, artifact.RelativePath, navigation.Target, "XAML", projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["confidenceReason"] = navigation.IsUnknown ? "Navigation target uses a runtime expression." : "Navigation target is statically visible.",
                ["detectionMode"] = navigation.DetectionMode,
                ["navigationTarget"] = navigation.Target,
                ["platformHead"] = string.Join(",", artifact.Project.PlatformHeads),
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Route",
                ["uiFramework"] = "Maui"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.NavigatesTo, ownerStableKey, targetStableKey, evidence.StableKey, navigation.Target, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["detectionMode"] = navigation.DetectionMode,
                ["navigationTarget"] = navigation.Target,
                ["uiFramework"] = "Maui"
            }));

            if (navigation.IsUnknown)
            {
                accumulator.AddWarning($"MAUI runtime navigation target in {artifact.RelativePath} at line {navigation.LineNumber.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        /// <summary>
        /// Adds graph facts for MAUI view-model evidence or convention-only unknowns.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="repositoryContext">The repository context containing known view-model types.</param>
        /// <param name="artifact">The artifact whose view model is being correlated.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the view using the view model.</param>
        /// <param name="root">The parsed XAML root element.</param>
        private static void AccumulateViewModel(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiRepositoryContext repositoryContext, MauiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, XElement root)
        {
            // Direct BindingContext elements are high-confidence evidence; missing conventions are recorded as unknowns so consumers can distinguish absence from unresolved patterns.
            if (artifact.ArtifactKind is MauiArtifactKind.Application or MauiArtifactKind.Shell or MauiArtifactKind.ResourceDictionary)
            {
                return;
            }

            ViewModelUsage usage = ExtractViewModel(root, artifact, repositoryContext);
            UnknownState unknownState = usage.IsUnknown ? UnknownState.Unknown("MAUI view model is inferred by convention only and was not found in source.") : UnknownState.Known;
            Confidence confidence = usage.IsUnknown ? Confidence.Low : usage.Confidence;
            StableKey viewModelStableKey = UiStableKeyBuilder.Create("ui-viewmodel://", projectStableKey.Value, "Maui", artifact.RelativePath, usage.ViewModelType, usage.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, usage.SourceText, "ViewModel", usage.DetectionMode, confidence, unknownState, usage.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, viewModelStableKey, NodeKind.ViewModel, usage.ViewModelType, artifact.RelativePath, usage.ViewModelType, artifact.Project.Language, projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["confidenceReason"] = usage.ConfidenceReason,
                ["detectionMode"] = usage.DetectionMode,
                ["platformHead"] = string.Join(",", artifact.Project.PlatformHeads),
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "ViewModel",
                ["uiFramework"] = "Maui",
                ["viewModelType"] = usage.ViewModelType
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesViewModel, ownerStableKey, viewModelStableKey, evidence.StableKey, usage.ViewModelType, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["detectionMode"] = usage.DetectionMode,
                ["uiFramework"] = "Maui",
                ["viewModelType"] = usage.ViewModelType
            }));

            if (usage.IsUnknown)
            {
                accumulator.AddWarning($"MAUI convention-only view model for {artifact.TypeName} in {artifact.RelativePath} could not be found in source.");
            }
        }

        /// <summary>
        /// Adds graph facts for a MAUI code-behind service usage.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact whose code-behind uses the service.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the UI artifact using the service.</param>
        /// <param name="serviceUsage">The service usage observation.</param>
        private static void AccumulateServiceUsage(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, ServiceUsage serviceUsage)
        {
            // Service usages are correlated from code-behind source and linked with DEPENDS_ON because semantic service registration may be emitted by earlier stages.
            StableKey serviceStableKey = UiStableKeyBuilder.Create("ui-service://", projectStableKey.Value, serviceUsage.TypeName);
            EvidenceRecord evidence = CreateEvidence(request, artifact, serviceUsage.SourceText, "ServiceUsage", "CodeBehind", Confidence.Medium, UnknownState.Known, serviceUsage.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, serviceStableKey, NodeKind.Type, serviceUsage.TypeName, serviceUsage.TypeName, serviceUsage.TypeName, artifact.Project.Language, projectStableKey, projectStableKey, Confidence.Medium, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["detectionMode"] = "CodeBehind",
                ["platformHead"] = string.Join(",", artifact.Project.PlatformHeads),
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["typeName"] = serviceUsage.TypeName,
                ["uiFramework"] = "Maui"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DependsOn, ownerStableKey, serviceStableKey, evidence.StableKey, serviceUsage.TypeName, artifact.RelativePath, Confidence.Medium, UnknownState.Known, new Dictionary<string, object?>
            {
                ["detectionMode"] = "CodeBehind",
                ["typeName"] = serviceUsage.TypeName,
                ["uiFramework"] = "Maui"
            }));
        }

        /// <summary>
        /// Adds graph facts for a MAUI code-behind data-access or external package usage.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact whose code-behind uses the dependency.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the UI artifact using the dependency.</param>
        /// <param name="dataAccessUsage">The data-access usage observation.</param>
        private static void AccumulateDataAccessUsage(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, DataAccessUsage dataAccessUsage)
        {
            // Data-access usages are emitted as external-service facts so UI-to-data paths remain visible before specialized data-access stages correlate exact contexts.
            StableKey dependencyStableKey = UiStableKeyBuilder.Create("ui-data-access://", projectStableKey.Value, dataAccessUsage.PackageIdentity);
            EvidenceRecord evidence = CreateEvidence(request, artifact, dataAccessUsage.SourceText, "DataAccess", "CodeBehind", Confidence.Medium, UnknownState.Known, dataAccessUsage.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, dependencyStableKey, NodeKind.ExternalService, dataAccessUsage.PackageIdentity, dataAccessUsage.PackageIdentity, dataAccessUsage.PackageIdentity, artifact.Project.Language, projectStableKey, projectStableKey, Confidence.Medium, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["detectionMode"] = "CodeBehind",
                ["packageIdentity"] = dataAccessUsage.PackageIdentity,
                ["platformHead"] = string.Join(",", artifact.Project.PlatformHeads),
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiFramework"] = "Maui"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.CallsApi, ownerStableKey, dependencyStableKey, evidence.StableKey, dataAccessUsage.PackageIdentity, artifact.RelativePath, Confidence.Medium, UnknownState.Known, new Dictionary<string, object?>
            {
                ["detectionMode"] = "CodeBehind",
                ["packageIdentity"] = dataAccessUsage.PackageIdentity,
                ["uiFramework"] = "Maui"
            }));
        }

        /// <summary>
        /// Adds explicit unknown facts for dynamic resource, template, and binding patterns that cannot be resolved statically.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The MAUI artifact that contains runtime-dependent markup.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact containing the unknowns.</param>
        /// <param name="root">The parsed XAML root element.</param>
        private static void AccumulateDynamicUnknowns(MauiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, MauiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, XElement root)
        {
            // Dynamic unknowns are handled after normal extraction so unsupported runtime decisions are captured without blocking known facts.
            foreach (UnknownUsage unknown in ExtractUnknowns(root))
            {
                UnknownState unknownState = UnknownState.Unknown(unknown.UnknownReason);
                StableKey unknownStableKey = UiStableKeyBuilder.Create("ui-unknown://", projectStableKey.Value, "Maui", artifact.RelativePath, unknown.Category, unknown.LineNumber.ToString(CultureInfo.InvariantCulture));
                EvidenceRecord evidence = CreateEvidence(request, artifact, unknown.SourceText, unknown.ArtifactKind, unknown.DetectionMode, Confidence.Low, unknownState, unknown.LineNumber);
                accumulator.AddEvidence(evidence);
                accumulator.AddNode(CreateNode(request.SnapshotStableKey, unknownStableKey, unknown.NodeKind, unknown.DisplayName, artifact.RelativePath, unknown.DisplayName, "XAML", projectStableKey, ownerStableKey, Confidence.Low, unknownState, evidence.StableKey, new Dictionary<string, object?>
                {
                    ["confidenceReason"] = unknown.UnknownReason,
                    ["detectionMode"] = unknown.DetectionMode,
                    ["platformHead"] = string.Join(",", artifact.Project.PlatformHeads),
                    ["projectKey"] = projectStableKey.Value,
                    ["sourcePath"] = artifact.RelativePath,
                    ["uiArtifactKind"] = unknown.ArtifactKind,
                    ["uiFramework"] = "Maui"
                }));
                accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, GetUnknownEdgeKind(unknown.NodeKind), ownerStableKey, unknownStableKey, evidence.StableKey, unknown.Category, artifact.RelativePath, Confidence.Low, unknownState, new Dictionary<string, object?>
                {
                    ["detectionMode"] = unknown.DetectionMode,
                    ["uiFramework"] = "Maui"
                }));
                accumulator.AddWarning($"MAUI {unknown.Category} in {artifact.RelativePath} requires runtime information: {unknown.UnknownReason}");
            }
        }

        /// <summary>
        /// Reads MAUI-relevant metadata from a C# or VB.NET project file.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="projectPath">The absolute project file path.</param>
        /// <returns>The normalized project metadata.</returns>
        private static ProjectMetadata ReadProjectMetadata(string repositoryRootDirectory, string projectPath)
        {
            // XML parsing is best-effort because SDK-style project files are regular XML but may include custom namespaces or conditions.
            string relativeProjectPath = SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRootDirectory, projectPath);
            string projectName = Path.GetFileNameWithoutExtension(projectPath);
            string language = projectPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ? "Visual Basic" : "C#";
            try
            {
                XDocument document = XDocument.Load(projectPath, LoadOptions.None);
                string text = document.ToString(SaveOptions.DisableFormatting);
                string[] targetFrameworks = ReadTargetFrameworks(document);
                string[] packageIdentities = document.Descendants().Where(element => string.Equals(element.Name.LocalName, "PackageReference", StringComparison.Ordinal)).Select(element => element.Attribute("Include")?.Value).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                string[] platformHeads = ReadPlatformHeads(projectPath, targetFrameworks);
                bool isMauiCandidate = string.Equals(ReadFirstElementValue(document, "UseMaui"), "true", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Microsoft.Maui", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("MauiProgram", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("MauiXaml", StringComparison.OrdinalIgnoreCase)
                    || targetFrameworks.Any(framework => framework.Contains("android", StringComparison.OrdinalIgnoreCase) || framework.Contains("ios", StringComparison.OrdinalIgnoreCase) || framework.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase))
                    || packageIdentities.Any(package => package.Contains("Maui", StringComparison.OrdinalIgnoreCase));
                return new ProjectMetadata(relativeProjectPath, projectName, targetFrameworks.Length == 0 ? ["Unknown"] : targetFrameworks, language, packageIdentities, platformHeads, isMauiCandidate);
            }
            catch (Exception) when (IsXmlReadException())
            {
                // Malformed project files cannot be evaluated safely; the project is skipped rather than producing guessed MAUI facts.
                return new ProjectMetadata(relativeProjectPath, projectName, ["Unknown"], language, [], [], false);
            }
        }

        /// <summary>
        /// Reads target framework values from a project document.
        /// </summary>
        /// <param name="document">The project XML document.</param>
        /// <returns>Target framework values in stable order.</returns>
        private static string[] ReadTargetFrameworks(XDocument document)
        {
            // MAUI projects commonly use TargetFrameworks to represent platform heads, while smaller fixtures may use TargetFramework.
            string? combined = ReadFirstElementValue(document, "TargetFrameworks") ?? ReadFirstElementValue(document, "TargetFramework");
            return string.IsNullOrWhiteSpace(combined) ? [] : combined.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>
        /// Reads platform heads from target frameworks and conventional Platforms folders.
        /// </summary>
        /// <param name="projectPath">The absolute project path whose directory may contain platform folders.</param>
        /// <param name="targetFrameworks">The normalized target frameworks from project metadata.</param>
        /// <returns>Platform-head names in deterministic order.</returns>
        private static string[] ReadPlatformHeads(string projectPath, IReadOnlyList<string> targetFrameworks)
        {
            // Platform heads are normalized to contributor-facing names rather than raw target-framework suffixes.
            HashSet<string> heads = new(StringComparer.Ordinal);
            foreach (string framework in targetFrameworks)
            {
                if (framework.Contains("android", StringComparison.OrdinalIgnoreCase))
                {
                    heads.Add("Android");
                }

                if (framework.Contains("ios", StringComparison.OrdinalIgnoreCase))
                {
                    heads.Add("iOS");
                }

                if (framework.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase))
                {
                    heads.Add("MacCatalyst");
                }

                if (framework.Contains("windows", StringComparison.OrdinalIgnoreCase))
                {
                    heads.Add("Windows");
                }
            }

            string platformsDirectory = Path.Combine(Path.GetDirectoryName(projectPath) ?? string.Empty, "Platforms");
            if (Directory.Exists(platformsDirectory))
            {
                foreach (string directory in Directory.EnumerateDirectories(platformsDirectory).Select(Path.GetFileName).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!))
                {
                    heads.Add(NormalizePlatformHead(directory));
                }
            }

            string[] preferredOrder = ["Android", "iOS", "MacCatalyst", "Windows", "Tizen"];
            return preferredOrder.Where(heads.Contains).Concat(heads.Except(preferredOrder, StringComparer.Ordinal).Order(StringComparer.Ordinal)).ToArray();
        }

        /// <summary>
        /// Normalizes a platform folder name to a stable MAUI platform-head metadata value.
        /// </summary>
        /// <param name="platformName">The raw platform folder name.</param>
        /// <returns>The normalized platform-head name.</returns>
        private static string NormalizePlatformHead(string platformName)
        {
            // Folder names are case-sensitive in source but the metadata values should be stable for queries.
            return platformName.Trim().ToLowerInvariant() switch
            {
                "android" => "Android",
                "ios" => "iOS",
                "maccatalyst" => "MacCatalyst",
                "windows" => "Windows",
                "tizen" => "Tizen",
                _ => platformName.Trim()
            };
        }

        /// <summary>
        /// Classifies a repository artifact into a MAUI XAML/source category.
        /// </summary>
        /// <param name="relativePath">The repository-relative artifact path.</param>
        /// <param name="content">The artifact content.</param>
        /// <returns>The artifact kind used by MAUI extraction.</returns>
        private static MauiArtifactKind ClassifyArtifact(string relativePath, string content)
        {
            // Classification relies on XAML root tags and source naming because extractor execution must not load MAUI assemblies.
            if (relativePath.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith(".xaml.vb", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase))
            {
                return MauiArtifactKind.Code;
            }

            string trimmed = content.TrimStart();
            if (trimmed.StartsWith("<Application", StringComparison.OrdinalIgnoreCase))
            {
                return MauiArtifactKind.Application;
            }

            if (trimmed.StartsWith("<Shell", StringComparison.OrdinalIgnoreCase))
            {
                return MauiArtifactKind.Shell;
            }

            if (trimmed.StartsWith("<ContentPage", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("<TabbedPage", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("<NavigationPage", StringComparison.OrdinalIgnoreCase))
            {
                return MauiArtifactKind.Page;
            }

            if (trimmed.StartsWith("<ContentView", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("<TemplatedView", StringComparison.OrdinalIgnoreCase))
            {
                return MauiArtifactKind.View;
            }

            if (trimmed.StartsWith("<ResourceDictionary", StringComparison.OrdinalIgnoreCase))
            {
                return MauiArtifactKind.ResourceDictionary;
            }

            return MauiArtifactKind.Other;
        }

        /// <summary>
        /// Extracts the primary type or artifact name from MAUI markup or source content.
        /// </summary>
        /// <param name="relativePath">The repository-relative artifact path.</param>
        /// <param name="content">The artifact content.</param>
        /// <param name="artifactKind">The artifact kind being named.</param>
        /// <returns>The primary artifact type name.</returns>
        private static string ExtractPrimaryTypeName(string relativePath, string content, MauiArtifactKind artifactKind)
        {
            // XAML `x:Class` is authoritative; code artifacts use source declarations; dictionaries fall back to file names.
            if (artifactKind is MauiArtifactKind.Code)
            {
                return ExtractCodeTypeName(content) ?? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(relativePath));
            }

            Match classMatch = XClassRegex().Match(content);
            if (classMatch.Success)
            {
                return classMatch.Groups["name"].Value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? classMatch.Groups["name"].Value.Trim();
            }

            return Path.GetFileNameWithoutExtension(relativePath);
        }

        /// <summary>
        /// Extracts a C# or VB.NET type declaration from source content.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <returns>The declared type name when one is present; otherwise, <see langword="null" />.</returns>
        private static string? ExtractCodeTypeName(string content)
        {
            // Source type names are used only for correlation and do not require semantic compilation.
            Match match = TypeRegex().Match(content);
            return match.Success ? match.Groups["name"].Value.Trim() : null;
        }

        /// <summary>
        /// Attempts to parse XAML content as XML while preserving non-fatal extraction behavior.
        /// </summary>
        /// <param name="content">The XAML content.</param>
        /// <param name="relativePath">The repository-relative path used for diagnostics.</param>
        /// <param name="accumulator">The accumulator that receives parse warnings.</param>
        /// <returns>The parsed XAML document, or <see langword="null" /> when parsing fails.</returns>
        private static XDocument? TryLoadXaml(string content, string relativePath, ArchitectureSnapshotAccumulator accumulator)
        {
            // XAML is XML-like enough for static markup extraction; parse failures are diagnostics rather than fatal pipeline errors.
            try
            {
                return XDocument.Parse(content, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            }
            catch (Exception exception) when (exception is XmlException or InvalidOperationException)
            {
                accumulator.AddWarning($"MAUI XAML artifact {relativePath} could not be parsed: {exception.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts resource, style, and template observations from a parsed MAUI XAML document.
        /// </summary>
        /// <param name="document">The parsed XAML document.</param>
        /// <returns>Resource observations in document order.</returns>
        private static IReadOnlyList<ResourceUsage> ExtractResources(XDocument document)
        {
            // Resources can be declared as keyed dictionary entries, styles, templates, or merged dictionaries.
            List<ResourceUsage> resources = [];
            foreach (XElement element in document.Descendants())
            {
                string localName = element.Name.LocalName;
                string? key = GetXamlAttribute(element, "Key") ?? element.Attribute("Source")?.Value;
                if (string.IsNullOrWhiteSpace(key) && localName is "Style")
                {
                    key = element.Attribute("TargetType")?.Value ?? "ImplicitStyle";
                }

                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                string artifactKind = localName switch
                {
                    "Style" => "Style",
                    "ControlTemplate" or "DataTemplate" => "Template",
                    "ResourceDictionary" when element.Attribute("Source") is not null => "Resource",
                    _ when IsResourceContainer(element) => "Resource",
                    _ => string.Empty
                };
                if (string.IsNullOrWhiteSpace(artifactKind))
                {
                    continue;
                }

                resources.Add(new ResourceUsage(key.Trim(), artifactKind, "XamlResource", GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting), false, null));
            }

            return resources;
        }

        /// <summary>
        /// Extracts named controls and custom component usages from a parsed MAUI XAML document.
        /// </summary>
        /// <param name="document">The parsed XAML document.</param>
        /// <param name="artifact">The artifact that owns the document.</param>
        /// <returns>Control observations in document order.</returns>
        private static IReadOnlyList<ControlUsage> ExtractControls(XDocument document, MauiArtifactContext artifact)
        {
            // A control is included when it has an explicit XAML name or when its XML namespace indicates a project-local custom view.
            List<ControlUsage> controls = [];
            foreach (XElement element in document.Descendants().Where(element => !IsRootArtifactElement(element, artifact)))
            {
                string? name = GetXamlAttribute(element, "Name") ?? element.Attribute("Name")?.Value;
                bool isCustomComponent = IsCustomComponentElement(element);
                if (string.IsNullOrWhiteSpace(name) && !isCustomComponent)
                {
                    continue;
                }

                string controlType = element.Name.LocalName;
                string controlName = string.IsNullOrWhiteSpace(name) ? controlType : name.Trim();
                controls.Add(new ControlUsage(controlName, controlType, isCustomComponent, GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
            }

            return controls;
        }

        /// <summary>
        /// Extracts Shell routes from MAUI markup attributes.
        /// </summary>
        /// <param name="document">The parsed XAML document.</param>
        /// <returns>Route observations in document order.</returns>
        private static IReadOnlyList<RouteUsage> ExtractMarkupRoutes(XDocument document)
        {
            // Shell routes can be declared as `Route` on ShellContent or as attached `Shell.Route` attributes on pages.
            List<RouteUsage> routes = [];
            foreach (XElement element in document.Root is null ? [] : document.Root.DescendantsAndSelf())
            {
                foreach (XAttribute attribute in element.Attributes().Where(attribute => string.Equals(attribute.Name.LocalName, "Route", StringComparison.Ordinal) || attribute.Name.LocalName.EndsWith(".Route", StringComparison.Ordinal)))
                {
                    bool isUnknown = attribute.Value.Contains('{', StringComparison.Ordinal) || attribute.Value.Contains("Binding", StringComparison.OrdinalIgnoreCase);
                    routes.Add(new RouteUsage(isUnknown ? "RuntimeRoute" : attribute.Value.Trim(), isUnknown, "XamlShellRoute", GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
                }
            }

            return routes;
        }

        /// <summary>
        /// Extracts binding expressions from attributes in a parsed MAUI XAML document.
        /// </summary>
        /// <param name="document">The parsed XAML document.</param>
        /// <returns>Binding observations in document order.</returns>
        private static IReadOnlyList<BindingUsage> ExtractBindings(XDocument document)
        {
            // MAUI binding markup extensions can contain `Path=Name`, direct `Binding Name`, x:Bind, or bare `{Binding}` expressions.
            List<BindingUsage> bindings = [];
            foreach (XElement element in document.Descendants())
            {
                foreach (XAttribute attribute in element.Attributes())
                {
                    if (!attribute.Value.Contains("{Binding", StringComparison.Ordinal) && !attribute.Value.Contains("{x:Bind", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string path = ExtractBindingPath(attribute.Value);
                    bool isUnknown = string.Equals(path, "Unknown", StringComparison.Ordinal);
                    bindings.Add(new BindingUsage(attribute.Name.LocalName, path, isUnknown, GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
                }
            }

            return bindings;
        }

        /// <summary>
        /// Extracts command bindings from MAUI Command attributes.
        /// </summary>
        /// <param name="document">The parsed XAML document.</param>
        /// <returns>Command observations in document order.</returns>
        private static IReadOnlyList<CommandUsage> ExtractCommands(XDocument document)
        {
            // Command properties often point to view-model command paths and are represented as command nodes distinct from event handlers.
            List<CommandUsage> commands = [];
            foreach (XElement element in document.Descendants())
            {
                XAttribute? commandAttribute = element.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "Command", StringComparison.Ordinal));
                if (commandAttribute is null)
                {
                    continue;
                }

                string commandName = ExtractBindingPath(commandAttribute.Value);
                if (string.Equals(commandName, "Unknown", StringComparison.Ordinal))
                {
                    commandName = commandAttribute.Value.Trim();
                }

                commands.Add(new CommandUsage(commandName, GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
            }

            return commands;
        }

        /// <summary>
        /// Extracts event attributes from MAUI XAML markup.
        /// </summary>
        /// <param name="document">The parsed XAML document.</param>
        /// <returns>Event observations in document order.</returns>
        private static IReadOnlyList<EventUsage> ExtractMarkupEvents(XDocument document)
        {
            // Events are inferred from known MAUI event attribute names with method-like handler values.
            List<EventUsage> events = [];
            foreach (XElement element in document.Descendants())
            {
                foreach (XAttribute attribute in element.Attributes().Where(IsRoutedEventAttribute))
                {
                    events.Add(new EventUsage(attribute.Name.LocalName, attribute.Value.Trim(), "XamlEvent", GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
                }
            }

            return events;
        }

        /// <summary>
        /// Extracts static navigation source attributes from MAUI markup.
        /// </summary>
        /// <param name="document">The parsed XAML document.</param>
        /// <returns>Navigation observations in document order.</returns>
        private static IReadOnlyList<NavigationUsage> ExtractNavigation(XDocument document)
        {
            // Markup-level navigation is less common than Shell code navigation, but static route-like attributes remain useful architecture evidence.
            List<NavigationUsage> navigations = [];
            foreach (XElement element in document.Descendants())
            {
                foreach (XAttribute attribute in element.Attributes().Where(attribute => string.Equals(attribute.Name.LocalName, "Source", StringComparison.Ordinal) || string.Equals(attribute.Name.LocalName, "NavigateUri", StringComparison.Ordinal)))
                {
                    bool isUnknown = attribute.Value.Contains('{', StringComparison.Ordinal) || attribute.Value.Contains("Binding", StringComparison.OrdinalIgnoreCase);
                    navigations.Add(new NavigationUsage(attribute.Value.Trim(), isUnknown, "XamlNavigation", GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
                }
            }

            return navigations;
        }

        /// <summary>
        /// Extracts a direct or convention-based view-model usage for a MAUI artifact.
        /// </summary>
        /// <param name="root">The parsed XAML root element.</param>
        /// <param name="artifact">The artifact being analyzed.</param>
        /// <param name="repositoryContext">The repository context containing known view-model types.</param>
        /// <returns>The view-model usage classification.</returns>
        private static ViewModelUsage ExtractViewModel(XElement root, MauiArtifactContext artifact, MauiRepositoryContext repositoryContext)
        {
            // Direct `<ContentPage.BindingContext><vm:MainViewModel /></ContentPage.BindingContext>` evidence is preferred over naming conventions.
            XElement? bindingContext = root.Descendants().FirstOrDefault(element => element.Name.LocalName.EndsWith(".BindingContext", StringComparison.Ordinal));
            XElement? directViewModel = bindingContext?.Elements().FirstOrDefault();
            if (directViewModel is not null)
            {
                string viewModelType = directViewModel.Name.LocalName;
                return new ViewModelUsage(viewModelType, "DirectBindingContext", Confidence.High, "Direct BindingContext element identifies the view model.", false, GetLineNumber(directViewModel), directViewModel.ToString(SaveOptions.DisableFormatting));
            }

            string conventionType = string.Concat(artifact.TypeName, "ViewModel").Replace("PageViewModel", "ViewModel", StringComparison.Ordinal).Replace("ViewViewModel", "ViewModel", StringComparison.Ordinal);
            if (repositoryContext.ViewModelTypeNames.Contains(conventionType))
            {
                return new ViewModelUsage(conventionType, "Convention", Confidence.Medium, "Repository source contains a matching convention-based view-model type.", false, 1, root.ToString(SaveOptions.DisableFormatting));
            }

            return new ViewModelUsage(conventionType, "Convention", Confidence.Low, "Convention-based view-model type was not found in source.", true, 1, root.ToString(SaveOptions.DisableFormatting));
        }

        /// <summary>
        /// Extracts explicit unknown runtime-dependent MAUI observations from parsed markup.
        /// </summary>
        /// <param name="root">The parsed XAML root element.</param>
        /// <returns>Unknown observations in document order.</returns>
        private static IReadOnlyList<UnknownUsage> ExtractUnknowns(XElement root)
        {
            // Unknown extraction focuses on runtime patterns called out by WP011 rather than attempting to model every MAUI dynamic feature.
            List<UnknownUsage> unknowns = [];
            foreach (XElement element in root.DescendantsAndSelf())
            {
                foreach (XAttribute attribute in element.Attributes())
                {
                    if (attribute.Value.Contains("{DynamicResource", StringComparison.Ordinal))
                    {
                        unknowns.Add(new UnknownUsage("dynamic resource", "DynamicResource", "Resource", NodeKind.UiResource, "DynamicResource", "MAUI dynamic resource target is computed from runtime state.", GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
                    }

                    if (attribute.Value.Contains("Template", StringComparison.OrdinalIgnoreCase) && attribute.Value.Contains("Binding", StringComparison.OrdinalIgnoreCase))
                    {
                        unknowns.Add(new UnknownUsage("runtime template", "RuntimeTemplate", "Style", NodeKind.UiStyle, "RuntimeTemplate", "MAUI template selection is determined at runtime.", GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
                    }
                }
            }

            return unknowns;
        }

        /// <summary>
        /// Extracts code-behind event handler declarations.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <returns>Event handler observations in source order.</returns>
        private static IReadOnlyList<EventUsage> ExtractCodeBehindEventHandlers(string content)
        {
            // Code-behind handlers supplement XAML event declarations and preserve evidence when markup is omitted.
            List<EventUsage> events = [];
            foreach (SourceLine line in SplitLines(content))
            {
                Match match = EventHandlerRegex().Match(line.Text);
                if (match.Success)
                {
                    events.Add(new EventUsage("Unknown", match.Groups["handler"].Value.Trim(), "CodeBehind", line.LineNumber, line.Text.Trim()));
                }
            }

            return events;
        }

        /// <summary>
        /// Extracts MAUI Shell navigation calls from code-behind source.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <returns>Navigation observations in source order.</returns>
        private static IReadOnlyList<NavigationUsage> ExtractCodeBehindNavigation(string content)
        {
            // Shell.Current.GoToAsync("route") is static enough to identify a route target, while other expressions become explicit unknowns.
            List<NavigationUsage> navigations = [];
            foreach (SourceLine line in SplitLines(content))
            {
                Match staticMatch = StaticGoToRegex().Match(line.Text);
                if (staticMatch.Success)
                {
                    navigations.Add(new NavigationUsage(staticMatch.Groups["target"].Value.Trim(), false, "CodeNavigation", line.LineNumber, line.Text.Trim()));
                    continue;
                }

                Match dynamicMatch = DynamicGoToRegex().Match(line.Text);
                if (dynamicMatch.Success)
                {
                    navigations.Add(new NavigationUsage("RuntimeNavigation", true, "CodeNavigation", line.LineNumber, line.Text.Trim()));
                }
            }

            return navigations;
        }

        /// <summary>
        /// Extracts Shell route registrations from source content.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <returns>Route observations in source order.</returns>
        private static IReadOnlyList<RouteUsage> ExtractSourceRoutes(string content)
        {
            // Routing.RegisterRoute calls are the common static Shell route registration shape in MAUI applications.
            List<RouteUsage> routes = [];
            foreach (SourceLine line in SplitLines(content))
            {
                Match staticMatch = RegisterRouteRegex().Match(line.Text);
                if (staticMatch.Success)
                {
                    routes.Add(new RouteUsage(staticMatch.Groups["route"].Value.Trim(), false, "CodeShellRoute", line.LineNumber, line.Text.Trim()));
                    continue;
                }

                if (line.Text.Contains("RegisterRoute", StringComparison.Ordinal) && !staticMatch.Success)
                {
                    routes.Add(new RouteUsage("RuntimeRoute", true, "CodeShellRoute", line.LineNumber, line.Text.Trim()));
                }
            }

            return routes;
        }

        /// <summary>
        /// Extracts MAUI handler registrations from MauiProgram source.
        /// </summary>
        /// <param name="content">The MauiProgram source content.</param>
        /// <param name="sourcePath">The repository-relative source path containing the handler registrations.</param>
        /// <returns>Handler observations in source order.</returns>
        private static IReadOnlyList<HandlerUsage> ExtractHandlerUsages(string content, string sourcePath)
        {
            // ConfigureMauiHandlers/AddHandler registrations are represented as command-style facts until the graph model gains a handler node kind.
            List<HandlerUsage> handlers = [];
            foreach (SourceLine line in SplitLines(content))
            {
                Match match = AddHandlerRegex().Match(line.Text);
                if (match.Success)
                {
                    handlers.Add(new HandlerUsage(match.Groups["control"].Value.Trim(), match.Groups["handler"].Value.Trim(), sourcePath, line.LineNumber, line.Text.Trim()));
                }
            }

            return handlers;
        }

        /// <summary>
        /// Extracts service type usages from source content.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <returns>Service usages in source order.</returns>
        private static IReadOnlyList<ServiceUsage> ExtractServiceUsages(string content)
        {
            // Type-name heuristics intentionally align with prior WP011 slices until full semantic facts are available in-process.
            List<ServiceUsage> usages = [];
            foreach (SourceLine line in SplitLines(content))
            {
                foreach (Match match in ServiceTypeUsageRegex().Matches(line.Text))
                {
                    usages.Add(new ServiceUsage(match.Groups["type"].Value.Trim(), line.LineNumber, line.Text.Trim()));
                }
            }

            return usages;
        }

        /// <summary>
        /// Extracts data-access or external integration usages from code-behind source and project packages.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <param name="packageIdentities">The package identities declared by the project.</param>
        /// <returns>Data-access usages in source order.</returns>
        private static IReadOnlyList<DataAccessUsage> ExtractDataAccessUsages(string content, IReadOnlyList<string> packageIdentities)
        {
            // Package names provide stable dependency identities while source snippets provide evidence and secret-redacted previews.
            List<DataAccessUsage> usages = [];
            foreach (SourceLine line in SplitLines(content))
            {
                if (line.Text.Contains("SqlConnection", StringComparison.Ordinal) || line.Text.Contains("DbContext", StringComparison.Ordinal))
                {
                    string packageIdentity = packageIdentities.FirstOrDefault(package => package.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) || package.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase)) ?? "System.Data";
                    usages.Add(new DataAccessUsage(packageIdentity, line.LineNumber, line.Text.Trim()));
                }
            }

            return usages;
        }

        /// <summary>
        /// Extracts repository-local view-model type declarations.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <returns>View-model type names in source order.</returns>
        private static IEnumerable<string> ExtractRepositoryViewModelTypeNames(string content)
        {
            // View-model declarations support direct BindingContext validation and convention-based confidence classification.
            foreach (Match match in ViewModelClassRegex().Matches(content))
            {
                yield return match.Groups["name"].Value.Trim();
            }
        }

        /// <summary>
        /// Creates a source-backed evidence record for one MAUI observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="artifact">The artifact containing the observation.</param>
        /// <param name="sourceText">The snippet text that supports the observation.</param>
        /// <param name="artifactKind">The artifact kind metadata value.</param>
        /// <param name="detectionMode">The detection mode metadata value.</param>
        /// <param name="confidence">The confidence assigned to the evidence.</param>
        /// <param name="unknownState">The unknown-state assigned to the evidence.</param>
        /// <param name="lineNumber">The optional one-based line number for the observation.</param>
        /// <returns>The created evidence record.</returns>
        private static EvidenceRecord CreateEvidence(MauiXamlExtractionRequest request, MauiArtifactContext artifact, string sourceText, string artifactKind, string detectionMode, Confidence confidence, UnknownState unknownState, int? lineNumber = null)
        {
            // Evidence previews are redacted by the shared UI evidence factory so secrets in XAML or connection strings do not leak to graph consumers.
            int startLine = lineNumber ?? 1;
            return UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, startLine, startLine, sourceText), "Maui", artifactKind, detectionMode, confidence, unknownState);
        }

        /// <summary>
        /// Creates a graph node using shared domain contracts and deterministic metadata.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the node.</param>
        /// <param name="stableKey">The node stable key.</param>
        /// <param name="nodeKind">The controlled node kind.</param>
        /// <param name="displayName">The developer-facing display name.</param>
        /// <param name="qualifiedName">The qualified name or source path.</param>
        /// <param name="searchName">The searchable name.</param>
        /// <param name="language">The programming or artifact language.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="parentNodeStableKey">The parent node stable key.</param>
        /// <param name="confidence">The confidence assigned to the node.</param>
        /// <param name="unknownState">The unknown-state assigned to the node.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key.</param>
        /// <param name="metadataValues">Node-specific metadata values.</param>
        /// <returns>The constructed architecture node.</returns>
        private static ArchitectureNode CreateNode(StableKey snapshotStableKey, StableKey stableKey, NodeKind nodeKind, string displayName, string qualifiedName, string searchName, string? language, StableKey? projectStableKey, StableKey? parentNodeStableKey, Confidence confidence, UnknownState unknownState, StableKey? evidenceStableKey, IReadOnlyDictionary<string, object?> metadataValues)
        {
            // Nodes use caller-provided stable keys and metadata because each fact category has different identity requirements.
            GraphMetadata metadata = GraphMetadata.From(metadataValues);
            return new ArchitectureNode(snapshotStableKey, stableKey, nodeKind, displayName, qualifiedName, searchName, language, projectStableKey, parentNodeStableKey, KnowledgeKind.Fact, ownership: null, externalCategory: null, confidence, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForNode(nodeKind, displayName, qualifiedName, searchName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a graph edge using shared domain contracts and deterministic metadata.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the edge.</param>
        /// <param name="edgeKind">The controlled edge kind.</param>
        /// <param name="sourceNodeStableKey">The source node stable key.</param>
        /// <param name="targetNodeStableKey">The target node stable key.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key.</param>
        /// <param name="relationshipIdentity">A stable relationship identity segment for de-duplication.</param>
        /// <param name="sourcePath">The repository-relative source path used in metadata and stable-key identity.</param>
        /// <param name="confidence">The confidence assigned to the edge.</param>
        /// <param name="unknownState">The unknown-state assigned to the edge.</param>
        /// <param name="metadataValues">Relationship-specific metadata values.</param>
        /// <returns>The constructed architecture edge.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, EdgeKind edgeKind, StableKey sourceNodeStableKey, StableKey targetNodeStableKey, StableKey? evidenceStableKey, string relationshipIdentity, string sourcePath, Confidence confidence, UnknownState unknownState, IReadOnlyDictionary<string, object?> metadataValues)
        {
            // Relationship keys include endpoints, edge kind, source path, and local identity so duplicate observations collapse without hiding distinct relationships.
            Dictionary<string, object?> values = new(metadataValues, StringComparer.Ordinal)
            {
                ["detectionMode"] = metadataValues.TryGetValue("detectionMode", out object? detectionMode) ? detectionMode : "StaticSource",
                ["sourcePath"] = sourcePath
            };
            GraphMetadata metadata = GraphMetadata.From(values);
            StableKey stableKey = UiStableKeyBuilder.Create("ui-edge://", sourceNodeStableKey.Value, targetNodeStableKey.Value, edgeKind.Value, sourcePath, relationshipIdentity);
            return new ArchitectureEdge(snapshotStableKey, stableKey, edgeKind, sourceNodeStableKey, targetNodeStableKey, true, KnowledgeKind.Fact, confidence, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForEdge(edgeKind, sourceNodeStableKey, targetNodeStableKey, true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates base metadata shared by MAUI artifact nodes.
        /// </summary>
        /// <param name="project">The owning MAUI project.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="sourcePath">The repository-relative source path.</param>
        /// <param name="artifactKind">The UI artifact-kind metadata value.</param>
        /// <param name="typeName">The associated artifact type name.</param>
        /// <param name="detectionMode">The detection mode metadata value.</param>
        /// <returns>A mutable metadata dictionary populated with shared MAUI fields.</returns>
        private static Dictionary<string, object?> CreateBaseMetadata(MauiProjectContext project, StableKey projectStableKey, string sourcePath, string artifactKind, string typeName, string detectionMode)
        {
            // Centralizing metadata fields keeps all MAUI facts aligned on lower-camel-case keys and normalized platform-head values.
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["detectionMode"] = detectionMode,
                ["language"] = "XAML",
                ["platformHead"] = string.Join(",", project.PlatformHeads),
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = sourcePath,
                ["targetFramework"] = string.Join(";", project.TargetFrameworks),
                ["typeName"] = typeName,
                ["uiArtifactKind"] = artifactKind,
                ["uiFramework"] = "Maui"
            };
        }

        /// <summary>
        /// Creates a stable key for a MAUI primary artifact node.
        /// </summary>
        /// <param name="artifact">The artifact being represented.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <returns>The deterministic artifact stable key.</returns>
        private static StableKey CreateArtifactStableKey(MauiArtifactContext artifact, StableKey projectStableKey)
        {
            // Primary artifact identity uses project key, framework, repository-relative path, artifact kind, and type name.
            return UiStableKeyBuilder.Create("ui-artifact://", projectStableKey.Value, "Maui", artifact.RelativePath, artifact.ArtifactKind.ToString(), artifact.TypeName);
        }

        /// <summary>
        /// Gets the edge kind that best links an unknown target node to its source artifact.
        /// </summary>
        /// <param name="nodeKind">The unknown target node kind.</param>
        /// <returns>The relationship kind for the unknown edge.</returns>
        private static EdgeKind GetUnknownEdgeKind(NodeKind nodeKind)
        {
            // Unknowns preserve the same traversal shape that a known fact would normally use.
            if (nodeKind == NodeKind.UiResource)
            {
                return EdgeKind.UsesUiResource;
            }

            return nodeKind == NodeKind.UiStyle ? EdgeKind.UsesStyle : EdgeKind.BindsTo;
        }

        /// <summary>
        /// Extracts a MAUI binding path from a markup extension value.
        /// </summary>
        /// <param name="value">The raw XAML attribute value.</param>
        /// <returns>The static binding path, or Unknown when none can be resolved.</returns>
        private static string ExtractBindingPath(string value)
        {
            // The parser handles common MAUI binding forms without attempting to evaluate the full markup-extension grammar.
            Match pathMatch = BindingPathRegex().Match(value);
            if (pathMatch.Success)
            {
                return pathMatch.Groups["path"].Value.Trim();
            }

            Match directMatch = DirectBindingRegex().Match(value);
            if (directMatch.Success && !string.IsNullOrWhiteSpace(directMatch.Groups["path"].Value))
            {
                return directMatch.Groups["path"].Value.Trim();
            }

            return "Unknown";
        }

        /// <summary>
        /// Reads an attribute by XAML local name across namespaced and non-namespaced forms.
        /// </summary>
        /// <param name="element">The element to inspect.</param>
        /// <param name="localName">The local attribute name to find.</param>
        /// <returns>The attribute value when present; otherwise, <see langword="null" />.</returns>
        private static string? GetXamlAttribute(XElement element, string localName)
        {
            // Local-name matching supports `x:Key`, `x:Name`, and namespace-prefixed attached attributes without depending on namespace prefixes.
            return element.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))?.Value;
        }

        /// <summary>
        /// Determines whether an element is a root MAUI artifact element.
        /// </summary>
        /// <param name="element">The candidate element.</param>
        /// <param name="artifact">The artifact containing the element.</param>
        /// <returns><see langword="true" /> when the element is the primary artifact root; otherwise, <see langword="false" />.</returns>
        private static bool IsRootArtifactElement(XElement element, MauiArtifactContext artifact)
        {
            // Root elements are already represented by the artifact node and should not also become child controls.
            return element.Parent is null && artifact.ArtifactKind is MauiArtifactKind.Application or MauiArtifactKind.Shell or MauiArtifactKind.Page or MauiArtifactKind.View;
        }

        /// <summary>
        /// Determines whether an element is a project-local MAUI component reference.
        /// </summary>
        /// <param name="element">The candidate element.</param>
        /// <returns><see langword="true" /> when the element uses a CLR namespace prefix; otherwise, <see langword="false" />.</returns>
        private static bool IsCustomComponentElement(XElement element)
        {
            // MAUI namespace mappings commonly use `clr-namespace:` values for project-local views.
            return element.Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.OrdinalIgnoreCase) || element.Name.NamespaceName.StartsWith("using:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a XAML element can represent a resource declaration.
        /// </summary>
        /// <param name="element">The candidate element.</param>
        /// <returns><see langword="true" /> when the element has a resource key; otherwise, <see langword="false" />.</returns>
        private static bool IsResourceContainer(XElement element)
        {
            // Any keyed object under MAUI resources can be referenced by StaticResource or DynamicResource.
            return GetXamlAttribute(element, "Key") is not null;
        }

        /// <summary>
        /// Determines whether a XAML attribute name and value represent a MAUI event handler.
        /// </summary>
        /// <param name="attribute">The candidate attribute.</param>
        /// <returns><see langword="true" /> when the attribute looks like an event handler; otherwise, <see langword="false" />.</returns>
        private static bool IsRoutedEventAttribute(XAttribute attribute)
        {
            // The current slice recognizes common MAUI event attribute names and requires method-like values to avoid classifying regular text properties as events.
            string name = attribute.Name.LocalName;
            return (name is "Clicked" or "Loaded" or "Tapped" or "Appearing" or "Disappearing" or "SelectionChanged" or "TextChanged" or "Completed")
                && MethodNameRegex().IsMatch(attribute.Value);
        }

        /// <summary>
        /// Reads a one-based source line number from an XML element when line information is available.
        /// </summary>
        /// <param name="element">The XML element.</param>
        /// <returns>The one-based line number or one when unavailable.</returns>
        private static int GetLineNumber(XElement element)
        {
            // XML line information is best-effort but sufficient for evidence navigation and stable relationship identity.
            if (element is IXmlLineInfo lineInfo && lineInfo.HasLineInfo())
            {
                return lineInfo.LineNumber;
            }

            return 1;
        }

        /// <summary>
        /// Splits source content into one-based line descriptors.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <returns>Line descriptors preserving original line numbers.</returns>
        private static SourceLine[] SplitLines(string content)
        {
            // Normalized line endings keep source-line evidence deterministic across Windows and Unix checkouts.
            return content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Select((text, index) => new SourceLine(index + 1, text)).ToArray();
        }

        /// <summary>
        /// Reads the first XML element value with a specific local name.
        /// </summary>
        /// <param name="document">The XML document to inspect.</param>
        /// <param name="localName">The local element name to read.</param>
        /// <returns>The trimmed value when present; otherwise, <see langword="null" />.</returns>
        private static string? ReadFirstElementValue(XDocument document, string localName)
        {
            // Local-name matching supports project files with or without XML namespaces.
            return document.Descendants().FirstOrDefault(element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal))?.Value.Trim();
        }

        /// <summary>
        /// Finds the nearest project for a source artifact by longest containing project directory.
        /// </summary>
        /// <param name="projects">The discovered project contexts.</param>
        /// <param name="artifactPath">The absolute artifact path.</param>
        /// <returns>The owning project context when found; otherwise, <see langword="null" />.</returns>
        private static MauiProjectContext? FindNearestProject(IReadOnlyList<MauiProjectContext> projects, string artifactPath)
        {
            // SDK-style project ownership is modeled by nearest containing project directory without evaluating project include/exclude items.
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
        /// Determines whether a discovered path belongs to source rather than generated build output.
        /// </summary>
        /// <param name="path">The absolute candidate path.</param>
        /// <returns><see langword="true" /> when the path should be analyzed; otherwise, <see langword="false" />.</returns>
        private static bool IsRepositorySourcePath(string path)
        {
            // Excluding output folders prevents duplicate generated XAML artifacts from `bin`/`obj` from destabilizing graph output.
            string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return !normalized.Contains(string.Concat(Path.DirectorySeparatorChar, "bin", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains(string.Concat(Path.DirectorySeparatorChar, "obj", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains(string.Concat(Path.DirectorySeparatorChar, ".git", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the metadata artifact-kind value for a MAUI artifact kind.
        /// </summary>
        /// <param name="artifactKind">The MAUI artifact kind.</param>
        /// <returns>The UI artifact-kind metadata value.</returns>
        private static string GetArtifactKindMetadata(MauiArtifactKind artifactKind)
        {
            // Metadata uses shared WP011 artifact names rather than MAUI-specific graph node kinds.
            return artifactKind switch
            {
                MauiArtifactKind.Application => "Application",
                MauiArtifactKind.Shell => "Layout",
                MauiArtifactKind.Page => "Page",
                MauiArtifactKind.View => "Component",
                MauiArtifactKind.ResourceDictionary => "Resource",
                MauiArtifactKind.Code => "CodeBehind",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Determines whether an exception filter is handling an XML read failure.
        /// </summary>
        /// <returns><see langword="true" /> for XML read exception filters.</returns>
        private static bool IsXmlReadException()
        {
            // This helper keeps exception filters concise while documenting that XML failures are intentionally non-fatal for static extraction.
            return true;
        }

        /// <summary>
        /// Creates a regex for XAML x:Class attributes.
        /// </summary>
        /// <returns>A regex that captures the declared class name.</returns>
        [GeneratedRegex("x:Class=\"(?<name>[^\"]+)\"", RegexOptions.CultureInvariant)]
        private static partial Regex XClassRegex();

        /// <summary>
        /// Creates a regex for C# or VB class declarations.
        /// </summary>
        /// <returns>A regex that captures class names.</returns>
        [GeneratedRegex("\\b(?:public|internal|private|partial|sealed|static|NotInheritable|Partial|Friend|Public|Private|Protected|\\s)*class\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\b|\\bClass\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex TypeRegex();

        /// <summary>
        /// Creates a regex for service type usage.
        /// </summary>
        /// <returns>A regex that captures type names ending in Service.</returns>
        [GeneratedRegex("\\b(?:new\\s+|readonly\\s+|private\\s+readonly\\s+|Private\\s+|AddSingleton<|AddScoped<|AddTransient<)?(?<type>[A-Za-z_][A-Za-z0-9_]*Service)\\b", RegexOptions.CultureInvariant)]
        private static partial Regex ServiceTypeUsageRegex();

        /// <summary>
        /// Creates a regex for view-model class declarations.
        /// </summary>
        /// <returns>A regex that captures declared view-model type names.</returns>
        [GeneratedRegex("\\bclass\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*ViewModel)\\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex ViewModelClassRegex();

        /// <summary>
        /// Creates a regex for MAUI code-behind event handler methods.
        /// </summary>
        /// <returns>A regex that captures handler method names.</returns>
        [GeneratedRegex("\\b(?<handler>[A-Za-z_][A-Za-z0-9_]*)\\s*\\([^)]*(?:EventArgs|TappedEventArgs|TextChangedEventArgs)", RegexOptions.CultureInvariant)]
        private static partial Regex EventHandlerRegex();

        /// <summary>
        /// Creates a regex for static MAUI Shell navigation calls.
        /// </summary>
        /// <returns>A regex that captures target route names.</returns>
        [GeneratedRegex("GoToAsync\\s*\\(\\s*\"(?<target>[^\"]+)\"", RegexOptions.CultureInvariant)]
        private static partial Regex StaticGoToRegex();

        /// <summary>
        /// Creates a regex for non-static MAUI Shell navigation calls.
        /// </summary>
        /// <returns>A regex that captures runtime navigation calls.</returns>
        [GeneratedRegex("GoToAsync\\s*\\(", RegexOptions.CultureInvariant)]
        private static partial Regex DynamicGoToRegex();

        /// <summary>
        /// Creates a regex for static MAUI Shell route registrations.
        /// </summary>
        /// <returns>A regex that captures route names.</returns>
        [GeneratedRegex("RegisterRoute\\s*\\(\\s*\"(?<route>[^\"]+)\"", RegexOptions.CultureInvariant)]
        private static partial Regex RegisterRouteRegex();

        /// <summary>
        /// Creates a regex for MAUI handler registrations.
        /// </summary>
        /// <returns>A regex that captures control and handler type names.</returns>
        [GeneratedRegex("AddHandler\\s*\\(\\s*typeof\\s*\\(\\s*(?<control>[A-Za-z_][A-Za-z0-9_]*)\\s*\\)\\s*,\\s*typeof\\s*\\(\\s*(?<handler>[A-Za-z_][A-Za-z0-9_]*)\\s*\\)", RegexOptions.CultureInvariant)]
        private static partial Regex AddHandlerRegex();

        /// <summary>
        /// Creates a regex for Path= binding syntax.
        /// </summary>
        /// <returns>A regex that captures binding paths.</returns>
        [GeneratedRegex("Path\\s*=\\s*(?<path>[A-Za-z_][A-Za-z0-9_\\.]*)", RegexOptions.CultureInvariant)]
        private static partial Regex BindingPathRegex();

        /// <summary>
        /// Creates a regex for direct `{Binding Name}` or `{x:Bind Name}` syntax.
        /// </summary>
        /// <returns>A regex that captures binding paths.</returns>
        [GeneratedRegex("\\{(?:x:)?Binding\\s+(?<path>[A-Za-z_][A-Za-z0-9_\\.]*)", RegexOptions.CultureInvariant)]
        private static partial Regex DirectBindingRegex();

        /// <summary>
        /// Creates a regex for method-like XAML handler values.
        /// </summary>
        /// <returns>A regex that validates simple handler names.</returns>
        [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
        private static partial Regex MethodNameRegex();

        /// <summary>
        /// Describes one discovered MAUI-capable project.
        /// </summary>
        /// <param name="AbsoluteProjectPath">The absolute project path used for artifact ownership checks.</param>
        /// <param name="RelativeProjectPath">The repository-relative project path used for stable keys.</param>
        /// <param name="ProjectName">The display name of the project.</param>
        /// <param name="TargetFrameworks">The target framework values read from project metadata.</param>
        /// <param name="Language">The project source language.</param>
        /// <param name="PackageIdentities">The package identities declared by the project.</param>
        /// <param name="PlatformHeads">The normalized MAUI platform heads discovered from project metadata and folders.</param>
        private sealed record MauiProjectContext(string AbsoluteProjectPath, string RelativeProjectPath, string ProjectName, IReadOnlyList<string> TargetFrameworks, string Language, IReadOnlyList<string> PackageIdentities, IReadOnlyList<string> PlatformHeads);

        /// <summary>
        /// Describes normalized project metadata read from a project file.
        /// </summary>
        /// <param name="RelativeProjectPath">The repository-relative project path.</param>
        /// <param name="ProjectName">The project display name.</param>
        /// <param name="TargetFrameworks">The target framework values or Unknown.</param>
        /// <param name="Language">The project source language.</param>
        /// <param name="PackageIdentities">The package identities declared by the project.</param>
        /// <param name="PlatformHeads">The normalized MAUI platform heads.</param>
        /// <param name="IsMauiCandidate">Whether the project contains MAUI evidence.</param>
        private sealed record ProjectMetadata(string RelativeProjectPath, string ProjectName, IReadOnlyList<string> TargetFrameworks, string Language, IReadOnlyList<string> PackageIdentities, IReadOnlyList<string> PlatformHeads, bool IsMauiCandidate);

        /// <summary>
        /// Describes one discovered MAUI artifact and its owning project.
        /// </summary>
        /// <param name="Project">The project that owns the artifact.</param>
        /// <param name="AbsolutePath">The absolute artifact path used for file reads.</param>
        /// <param name="RelativePath">The repository-relative artifact path used for evidence and stable keys.</param>
        /// <param name="TypeName">The source type name associated with the artifact.</param>
        /// <param name="ArtifactKind">The coarse MAUI artifact classification.</param>
        private sealed record MauiArtifactContext(MauiProjectContext Project, string AbsolutePath, string RelativePath, string TypeName, MauiArtifactKind ArtifactKind);

        /// <summary>
        /// Describes repository-wide MAUI context used during per-artifact analysis.
        /// </summary>
        /// <param name="SourceByPath">Source content keyed by repository-relative path.</param>
        /// <param name="ViewModelTypeNames">Repository-local view-model type names.</param>
        /// <param name="EventsByType">Event handler observations keyed by owner type.</param>
        /// <param name="ServiceUsagesByType">Service usages keyed by owner type.</param>
        /// <param name="DataAccessUsagesByType">Data-access usages keyed by owner type.</param>
        /// <param name="NavigationUsagesByType">Navigation usages keyed by owner type.</param>
        /// <param name="RoutesByProject">Shell route observations keyed by project path.</param>
        /// <param name="HandlersByProject">Handler registration observations keyed by project path.</param>
        private sealed record MauiRepositoryContext(IReadOnlyDictionary<string, string> SourceByPath, IReadOnlySet<string> ViewModelTypeNames, IReadOnlyDictionary<string, IReadOnlyList<EventUsage>> EventsByType, IReadOnlyDictionary<string, IReadOnlyList<ServiceUsage>> ServiceUsagesByType, IReadOnlyDictionary<string, IReadOnlyList<DataAccessUsage>> DataAccessUsagesByType, IReadOnlyDictionary<string, IReadOnlyList<NavigationUsage>> NavigationUsagesByType, IReadOnlyDictionary<string, IReadOnlyList<RouteUsage>> RoutesByProject, IReadOnlyDictionary<string, IReadOnlyList<HandlerUsage>> HandlersByProject);

        /// <summary>
        /// Describes one source line with its original one-based line number.
        /// </summary>
        /// <param name="LineNumber">The one-based line number.</param>
        /// <param name="Text">The source line text.</param>
        private sealed record SourceLine(int LineNumber, string Text);

        /// <summary>
        /// Describes a MAUI resource, style, or template observation.
        /// </summary>
        /// <param name="Key">The resource, style, or template key.</param>
        /// <param name="ArtifactKind">The UI artifact-kind metadata value.</param>
        /// <param name="DetectionMode">The detection mode that produced the observation.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        /// <param name="IsUnknown">Whether the resource target is runtime-dependent.</param>
        /// <param name="UnknownReason">The unknown reason when <paramref name="IsUnknown" /> is true.</param>
        private sealed record ResourceUsage(string Key, string ArtifactKind, string DetectionMode, int LineNumber, string SourceText, bool IsUnknown, string? UnknownReason);

        /// <summary>
        /// Describes a MAUI control observation.
        /// </summary>
        /// <param name="ControlName">The control name or type when unnamed.</param>
        /// <param name="ControlType">The MAUI control type.</param>
        /// <param name="IsCustomComponent">Whether the control is a project-local component reference.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record ControlUsage(string ControlName, string ControlType, bool IsCustomComponent, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a MAUI Shell route observation.
        /// </summary>
        /// <param name="RouteTemplate">The static route template or runtime route marker.</param>
        /// <param name="IsUnknown">Whether the route template is runtime-dependent.</param>
        /// <param name="DetectionMode">The detection mode that produced the route.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record RouteUsage(string RouteTemplate, bool IsUnknown, string DetectionMode, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a MAUI binding observation.
        /// </summary>
        /// <param name="PropertyName">The XAML property being bound.</param>
        /// <param name="BindingPath">The binding path visible in markup.</param>
        /// <param name="IsUnknown">Whether the binding path could not be resolved statically.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record BindingUsage(string PropertyName, string BindingPath, bool IsUnknown, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a MAUI command binding observation.
        /// </summary>
        /// <param name="CommandName">The command property or expression name.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record CommandUsage(string CommandName, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a MAUI event observation.
        /// </summary>
        /// <param name="EventName">The event name.</param>
        /// <param name="HandlerName">The handler method name.</param>
        /// <param name="DetectionMode">The detection mode that produced the observation.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record EventUsage(string EventName, string HandlerName, string DetectionMode, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a MAUI navigation observation.
        /// </summary>
        /// <param name="Target">The static navigation target or runtime expression.</param>
        /// <param name="IsUnknown">Whether the target is computed from runtime state.</param>
        /// <param name="DetectionMode">The detection mode that produced the observation.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record NavigationUsage(string Target, bool IsUnknown, string DetectionMode, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a MAUI handler registration observation.
        /// </summary>
        /// <param name="ControlType">The MAUI control type being handled.</param>
        /// <param name="HandlerName">The handler type name.</param>
        /// <param name="SourcePath">The repository-relative source path that contains the handler registration.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record HandlerUsage(string ControlType, string HandlerName, string SourcePath, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a MAUI view-model correlation observation.
        /// </summary>
        /// <param name="ViewModelType">The view-model type name.</param>
        /// <param name="DetectionMode">The detection mode that produced the observation.</param>
        /// <param name="Confidence">The confidence assigned to the observation.</param>
        /// <param name="ConfidenceReason">The explanation for the confidence assignment.</param>
        /// <param name="IsUnknown">Whether the view-model type could not be resolved statically.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record ViewModelUsage(string ViewModelType, string DetectionMode, Confidence Confidence, string ConfidenceReason, bool IsUnknown, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a MAUI runtime-dependent unknown observation.
        /// </summary>
        /// <param name="Category">The unknown category used for warnings and stable keys.</param>
        /// <param name="DetectionMode">The detection mode that produced the unknown.</param>
        /// <param name="ArtifactKind">The UI artifact-kind metadata value.</param>
        /// <param name="NodeKind">The node kind that would normally represent the target.</param>
        /// <param name="DisplayName">The display name for the unknown node.</param>
        /// <param name="UnknownReason">The explicit unknown reason.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record UnknownUsage(string Category, string DetectionMode, string ArtifactKind, NodeKind NodeKind, string DisplayName, string UnknownReason, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a code-behind service usage.
        /// </summary>
        /// <param name="TypeName">The service type name.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record ServiceUsage(string TypeName, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a code-behind data-access usage.
        /// </summary>
        /// <param name="PackageIdentity">The package or namespace identity for the data-access dependency.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record DataAccessUsage(string PackageIdentity, int LineNumber, string SourceText);

        /// <summary>
        /// Describes the coarse category of a MAUI artifact.
        /// </summary>
        private enum MauiArtifactKind
        {
            /// <summary>
            /// A MAUI application definition XAML file.
            /// </summary>
            Application,

            /// <summary>
            /// A MAUI Shell XAML file.
            /// </summary>
            Shell,

            /// <summary>
            /// A MAUI page XAML file.
            /// </summary>
            Page,

            /// <summary>
            /// A MAUI content-view XAML file.
            /// </summary>
            View,

            /// <summary>
            /// A MAUI resource dictionary XAML file.
            /// </summary>
            ResourceDictionary,

            /// <summary>
            /// A code-behind or source file.
            /// </summary>
            Code,

            /// <summary>
            /// An unsupported artifact.
            /// </summary>
            Other
        }
    }
}
