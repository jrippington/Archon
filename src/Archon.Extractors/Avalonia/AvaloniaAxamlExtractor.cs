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

namespace Archon.Extractors.Avalonia
{
    /// <summary>
    /// Extracts static UI extraction Avalonia AXAML, view-locator, ReactiveUI, navigation, and dependency facts from repository source into shared graph contracts.
    /// </summary>
    /// <remarks>
    /// The extractor performs deterministic file analysis only. It does not evaluate MSBuild, load Avalonia, instantiate controls, start desktop lifetimes, render UI, open databases, or write directly to persistence.
    /// </remarks>
    public sealed partial class AvaloniaAxamlExtractor
    {
        /// <summary>
        /// Extracts Avalonia application, window, user-control, resource, style, binding, command, view-locator, ReactiveUI, navigation, view-model, service, data-access, evidence, warning, and unknown facts.
        /// </summary>
        /// <param name="request">The repository-scoped Avalonia extraction request.</param>
        /// <param name="cancellationToken">The cancellation token that stops file discovery and artifact analysis.</param>
        /// <returns>A result containing the graph-ready snapshot emitted by this extractor.</returns>
        public async Task<AvaloniaAxamlExtractionResult> ExtractAsync(AvaloniaAxamlExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // Extraction is separated into discovery, repository context indexing, project projection, and artifact analysis so partial results remain deterministic.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            ArchitectureSnapshotAccumulator accumulator = new();
            IReadOnlyList<AvaloniaProjectContext> projects = DiscoverProjects(request.RepositoryRootDirectory);
            IReadOnlyList<AvaloniaArtifactContext> artifacts = DiscoverArtifacts(request.RepositoryRootDirectory, projects);
            AvaloniaRepositoryContext repositoryContext = await BuildRepositoryContextAsync(projects, artifacts, cancellationToken).ConfigureAwait(false);

            foreach (AvaloniaProjectContext project in projects.Where(project => artifacts.Any(artifact => StringComparer.Ordinal.Equals(artifact.Project.RelativeProjectPath, project.RelativeProjectPath))))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateProjectAndApplication(request, accumulator, project, artifacts, repositoryContext);
            }

            foreach (AvaloniaArtifactContext artifact in artifacts.Where(artifact => artifact.ArtifactKind is AvaloniaArtifactKind.Application or AvaloniaArtifactKind.Window or AvaloniaArtifactKind.UserControl or AvaloniaArtifactKind.Styles or AvaloniaArtifactKind.ResourceDictionary).OrderBy(artifact => artifact.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeArtifact(request, accumulator, repositoryContext, artifact);
            }

            return new AvaloniaAxamlExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Discovers Avalonia-capable projects from project metadata, package references, target frameworks, AXAML artifacts, and source symbols.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <returns>Project contexts ordered by repository-relative path.</returns>
        private static IReadOnlyList<AvaloniaProjectContext> DiscoverProjects(string repositoryRootDirectory)
        {
            // Discovery reads static project XML only so Avalonia packages or desktop workloads are not required on the machine running extraction.
            if (!Directory.Exists(repositoryRootDirectory))
            {
                return [];
            }

            List<AvaloniaProjectContext> projects = [];
            IEnumerable<string> projectPaths = Directory.EnumerateFiles(repositoryRootDirectory, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(repositoryRootDirectory, "*.vbproj", SearchOption.AllDirectories))
                .Where(IsRepositorySourcePath)
                .Order(StringComparer.OrdinalIgnoreCase);

            foreach (string projectPath in projectPaths)
            {
                ProjectMetadata metadata = ReadProjectMetadata(repositoryRootDirectory, projectPath);
                if (!metadata.IsAvaloniaCandidate)
                {
                    continue;
                }

                projects.Add(new AvaloniaProjectContext(projectPath, metadata.RelativeProjectPath, metadata.ProjectName, metadata.TargetFrameworks, metadata.Language, metadata.PackageIdentities, metadata.UsesReactiveUi));
            }

            return projects;
        }

        /// <summary>
        /// Discovers AXAML and source artifacts that belong to discovered Avalonia projects.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="projects">The Avalonia project contexts that can own artifacts.</param>
        /// <returns>Artifact contexts ordered by repository-relative path.</returns>
        private static IReadOnlyList<AvaloniaArtifactContext> DiscoverArtifacts(string repositoryRootDirectory, IReadOnlyList<AvaloniaProjectContext> projects)
        {
            // Avalonia UI structure spans AXAML, code-behind, startup source, view locators, and view-model source, so source and markup are discovered together.
            if (!Directory.Exists(repositoryRootDirectory) || projects.Count == 0)
            {
                return [];
            }

            List<AvaloniaArtifactContext> artifacts = [];
            IEnumerable<string> artifactPaths = Directory.EnumerateFiles(repositoryRootDirectory, "*.axaml", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(repositoryRootDirectory, "*.cs", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(repositoryRootDirectory, "*.vb", SearchOption.AllDirectories))
                .Where(IsRepositorySourcePath)
                .Order(StringComparer.OrdinalIgnoreCase);

            foreach (string artifactPath in artifactPaths)
            {
                AvaloniaProjectContext? project = FindNearestProject(projects, artifactPath);
                if (project is null)
                {
                    continue;
                }

                string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRootDirectory, artifactPath);
                string content = File.ReadAllText(artifactPath);
                AvaloniaArtifactKind artifactKind = ClassifyArtifact(relativePath, content);
                string typeName = ExtractPrimaryTypeName(relativePath, content, artifactKind);
                artifacts.Add(new AvaloniaArtifactContext(project, artifactPath, relativePath, typeName, artifactKind));
            }

            return artifacts;
        }

        /// <summary>
        /// Builds repository-wide Avalonia context used to correlate AXAML, startup source, code-behind, view models, services, data access, view locators, and ReactiveUI evidence.
        /// </summary>
        /// <param name="projects">The discovered Avalonia projects.</param>
        /// <param name="artifacts">The discovered Avalonia artifacts.</param>
        /// <param name="cancellationToken">The cancellation token that stops source loading.</param>
        /// <returns>A repository context used while analyzing Avalonia artifacts.</returns>
        private static async Task<AvaloniaRepositoryContext> BuildRepositoryContextAsync(IReadOnlyList<AvaloniaProjectContext> projects, IReadOnlyList<AvaloniaArtifactContext> artifacts, CancellationToken cancellationToken)
        {
            // Repository context indexes are built once so per-artifact projection can avoid repeated scans and stable-key ordering stays deterministic.
            Dictionary<string, string> sourceByPath = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> viewModelTypeNames = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<EventUsage>> eventsByType = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<ServiceUsage>> serviceUsagesByType = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<DataAccessUsage>> dataAccessUsagesByType = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<NavigationUsage>> navigationUsagesByType = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<ViewLocatorUsage>> viewLocatorsByProject = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, IReadOnlyList<ViewModelUsage>> reactiveViewModelsByType = new(StringComparer.Ordinal);

            foreach (AvaloniaArtifactContext artifact in artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string content = await File.ReadAllTextAsync(artifact.AbsolutePath, cancellationToken).ConfigureAwait(false);
                sourceByPath[artifact.RelativePath] = content;

                if (artifact.ArtifactKind is not AvaloniaArtifactKind.Code)
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
                reactiveViewModelsByType[ownerTypeName] = ExtractReactiveViewModelUsages(content);

                IReadOnlyList<ViewLocatorUsage> locatorUsages = ExtractViewLocatorUsages(content, artifact.RelativePath);
                if (locatorUsages.Count > 0)
                {
                    viewLocatorsByProject[artifact.Project.RelativeProjectPath] = viewLocatorsByProject.TryGetValue(artifact.Project.RelativeProjectPath, out IReadOnlyList<ViewLocatorUsage>? existing) ? existing.Concat(locatorUsages).ToArray() : locatorUsages;
                }
            }

            return new AvaloniaRepositoryContext(sourceByPath, viewModelTypeNames, eventsByType, serviceUsagesByType, dataAccessUsagesByType, navigationUsagesByType, viewLocatorsByProject, reactiveViewModelsByType);
        }

        /// <summary>
        /// Adds project, application, startup, view-locator, and ReactiveUI package facts for one Avalonia project.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="project">The project context being represented.</param>
        /// <param name="artifacts">The discovered Avalonia artifacts used to resolve application definitions.</param>
        /// <param name="repositoryContext">The repository context that supplies source content, view locators, and dependency metadata.</param>
        private static void AccumulateProjectAndApplication(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaProjectContext project, IReadOnlyList<AvaloniaArtifactContext> artifacts, AvaloniaRepositoryContext repositoryContext)
        {
            // Project and application facts give every Avalonia UI node stable ownership when this extractor runs independently from project inventory.
            StableKey projectStableKey = StableKeyGenerator.ForProject(project.RelativeProjectPath);
            EvidenceRecord projectEvidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(project.RelativeProjectPath, 1, 1, project.ProjectName), "Avalonia", "Application", "ProjectMetadata", Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(projectEvidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, projectStableKey, NodeKind.Project, project.ProjectName, project.RelativeProjectPath, project.RelativeProjectPath, project.Language, projectStableKey, null, Confidence.High, UnknownState.Known, projectEvidence.StableKey, new Dictionary<string, object?>
            {
                ["detectionMode"] = "ProjectMetadata",
                ["language"] = project.Language,
                ["packageIdentity"] = string.Join(",", project.PackageIdentities),
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = project.RelativeProjectPath,
                ["targetFramework"] = string.Join(";", project.TargetFrameworks),
                ["uiFramework"] = "Avalonia"
            }));

            AvaloniaArtifactContext? applicationArtifact = artifacts.FirstOrDefault(artifact => StringComparer.Ordinal.Equals(artifact.Project.RelativeProjectPath, project.RelativeProjectPath) && artifact.ArtifactKind is AvaloniaArtifactKind.Application);
            AvaloniaArtifactContext? startupArtifact = artifacts.FirstOrDefault(artifact => StringComparer.Ordinal.Equals(artifact.Project.RelativeProjectPath, project.RelativeProjectPath) && artifact.ArtifactKind is AvaloniaArtifactKind.Code && repositoryContext.SourceByPath[artifact.RelativePath].Contains("StartWithClassicDesktopLifetime", StringComparison.Ordinal));
            string sourcePath = applicationArtifact?.RelativePath ?? startupArtifact?.RelativePath ?? project.RelativeProjectPath;
            StableKey applicationStableKey = UiStableKeyBuilder.Create("ui-application://", projectStableKey.Value, "Avalonia", string.Join(";", project.TargetFrameworks), applicationArtifact?.TypeName ?? project.ProjectName, startupArtifact?.TypeName ?? "UnknownStartup");
            EvidenceRecord applicationEvidence = applicationArtifact is null ? projectEvidence : CreateEvidence(request, applicationArtifact, repositoryContext.SourceByPath[applicationArtifact.RelativePath], "Application", "AxamlApplication", Confidence.High, UnknownState.Known);
            if (!ReferenceEquals(applicationEvidence, projectEvidence))
            {
                accumulator.AddEvidence(applicationEvidence);
            }

            accumulator.AddNode(CreateNode(request.SnapshotStableKey, applicationStableKey, NodeKind.UiApplication, project.ProjectName, sourcePath, project.ProjectName, project.Language, projectStableKey, projectStableKey, Confidence.High, UnknownState.Known, applicationEvidence.StableKey, new Dictionary<string, object?>
            {
                ["confidenceReason"] = "Project metadata and application source identified the Avalonia application.",
                ["detectionMode"] = applicationArtifact is null ? "ProjectMetadata" : "AxamlApplication",
                ["hostingModel"] = "Desktop",
                ["language"] = project.Language,
                ["packageIdentity"] = string.Join(",", project.PackageIdentities),
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = sourcePath,
                ["targetFramework"] = string.Join(";", project.TargetFrameworks),
                ["uiArtifactKind"] = "Application",
                ["uiFramework"] = "Avalonia"
            }));

            if (project.UsesReactiveUi)
            {
                AccumulateReactiveUiPackage(request, accumulator, project, projectStableKey, startupArtifact ?? applicationArtifact, repositoryContext);
            }

            AccumulateProjectViewLocators(request, accumulator, project, projectStableKey, applicationStableKey, applicationArtifact ?? startupArtifact, repositoryContext);
        }

        /// <summary>
        /// Analyzes one Avalonia AXAML artifact and contributes graph facts for supported markup and source patterns.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="repositoryContext">The repository context used for source and symbol correlation.</param>
        /// <param name="artifact">The Avalonia AXAML artifact being analyzed.</param>
        private static void AnalyzeArtifact(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaRepositoryContext repositoryContext, AvaloniaArtifactContext artifact)
        {
            // AXAML parsing is best-effort; malformed artifacts become non-fatal warnings instead of aborting the UI slice.
            string content = repositoryContext.SourceByPath[artifact.RelativePath];
            XDocument? document = TryLoadAxaml(content, artifact.RelativePath, accumulator);
            if (document?.Root is null)
            {
                return;
            }

            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            EvidenceRecord artifactEvidence = CreateEvidence(request, artifact, content, GetArtifactKindMetadata(artifact.ArtifactKind), "AxamlMarkup", Confidence.High, UnknownState.Known);
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

            foreach (ViewModelUsage reactiveUsage in repositoryContext.ReactiveViewModelsByType.TryGetValue(artifact.TypeName, out IReadOnlyList<ViewModelUsage>? reactiveViewModels) ? reactiveViewModels : [])
            {
                AccumulateReactiveViewModel(request, accumulator, artifact, projectStableKey, ownerStableKey, reactiveUsage);
            }

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
        /// Creates the primary graph node for an Avalonia AXAML artifact.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives the graph node.</param>
        /// <param name="artifact">The Avalonia artifact being represented.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="evidenceStableKey">The evidence stable key for the artifact.</param>
        /// <returns>The stable key of the created artifact node.</returns>
        private static StableKey CreateArtifactNode(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaArtifactContext artifact, StableKey projectStableKey, StableKey evidenceStableKey)
        {
            // Framework-specific subtypes remain metadata values while node kinds use the shared static UI extraction UI vocabulary.
            NodeKind nodeKind = artifact.ArtifactKind switch
            {
                AvaloniaArtifactKind.Application => NodeKind.UiApplication,
                AvaloniaArtifactKind.Window => NodeKind.UiView,
                AvaloniaArtifactKind.UserControl => NodeKind.UiComponent,
                AvaloniaArtifactKind.Styles => NodeKind.UiStyle,
                AvaloniaArtifactKind.ResourceDictionary => NodeKind.UiResource,
                _ => NodeKind.UiComponent
            };
            StableKey nodeStableKey = CreateArtifactStableKey(artifact, projectStableKey);
            string artifactKind = GetArtifactKindMetadata(artifact.ArtifactKind);
            Dictionary<string, object?> metadata = CreateBaseMetadata(artifact.Project, projectStableKey, artifact.RelativePath, artifactKind, artifact.TypeName, "AxamlMarkup");
            if (artifact.ArtifactKind is AvaloniaArtifactKind.Window)
            {
                metadata["windowName"] = artifact.TypeName;
                metadata["viewName"] = artifact.TypeName;
            }
            else if (artifact.ArtifactKind is AvaloniaArtifactKind.UserControl)
            {
                metadata["componentName"] = artifact.TypeName;
            }
            else if (artifact.ArtifactKind is AvaloniaArtifactKind.Styles)
            {
                metadata["styleKey"] = Path.GetFileName(artifact.RelativePath);
            }

            accumulator.AddNode(CreateNode(request.SnapshotStableKey, nodeStableKey, nodeKind, artifact.TypeName, artifact.RelativePath, artifact.TypeName, "AXAML", projectStableKey, projectStableKey, Confidence.High, UnknownState.Known, evidenceStableKey, metadata));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DeclaresComponent, projectStableKey, nodeStableKey, evidenceStableKey, artifact.RelativePath, artifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["detectionMode"] = "AxamlMarkup",
                ["uiArtifactKind"] = artifactKind,
                ["uiFramework"] = "Avalonia"
            }));
            return nodeStableKey;
        }

        /// <summary>
        /// Adds graph facts for Avalonia view-locator observations discovered from project-level source.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="project">The Avalonia project that owns the view locators.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="applicationStableKey">The application node stable key that owns project-level locator relationships.</param>
        /// <param name="fallbackArtifact">The optional artifact used as fallback evidence context.</param>
        /// <param name="repositoryContext">The repository context containing view-locator observations.</param>
        private static void AccumulateProjectViewLocators(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaProjectContext project, StableKey projectStableKey, StableKey applicationStableKey, AvaloniaArtifactContext? fallbackArtifact, AvaloniaRepositoryContext repositoryContext)
        {
            // View locators map view-model types to view controls and are represented with USES_VIEW_MODEL plus USES_COMPONENT relationships where static evidence exists.
            foreach (ViewLocatorUsage locator in repositoryContext.ViewLocatorsByProject.TryGetValue(project.RelativeProjectPath, out IReadOnlyList<ViewLocatorUsage>? locators) ? locators : [])
            {
                AvaloniaArtifactContext artifact = fallbackArtifact ?? new AvaloniaArtifactContext(project, project.AbsoluteProjectPath, locator.SourcePath, project.ProjectName, AvaloniaArtifactKind.Code);
                UnknownState unknownState = locator.IsUnknown ? UnknownState.Unknown("Avalonia view locator uses convention or reflection that could not be resolved statically.") : UnknownState.Known;
                Confidence confidence = locator.IsUnknown ? Confidence.Low : Confidence.Medium;
                StableKey viewModelStableKey = UiStableKeyBuilder.Create("ui-viewmodel://", projectStableKey.Value, "Avalonia", locator.ViewModelType, locator.LineNumber.ToString(CultureInfo.InvariantCulture));
                StableKey viewStableKey = UiStableKeyBuilder.Create("ui-artifact://", projectStableKey.Value, "Avalonia", locator.ViewType, "ViewLocator", locator.ViewType);
                EvidenceRecord evidence = CreateEvidence(request, artifact, locator.SourceText, "ViewModel", locator.DetectionMode, confidence, unknownState, locator.LineNumber);
                accumulator.AddEvidence(evidence);
                accumulator.AddNode(CreateNode(request.SnapshotStableKey, viewModelStableKey, NodeKind.ViewModel, locator.ViewModelType, locator.ViewModelType, locator.ViewModelType, project.Language, projectStableKey, applicationStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
                {
                    ["confidenceReason"] = locator.IsUnknown ? unknownState.UnknownReason : "View locator source maps the view model to a view.",
                    ["detectionMode"] = locator.DetectionMode,
                    ["projectKey"] = projectStableKey.Value,
                    ["sourcePath"] = locator.SourcePath,
                    ["uiArtifactKind"] = "ViewModel",
                    ["uiFramework"] = "Avalonia",
                    ["viewModelType"] = locator.ViewModelType
                }));
                accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesViewModel, applicationStableKey, viewModelStableKey, evidence.StableKey, locator.ViewModelType, locator.SourcePath, confidence, unknownState, new Dictionary<string, object?>
                {
                    ["detectionMode"] = locator.DetectionMode,
                    ["uiFramework"] = "Avalonia",
                    ["viewModelType"] = locator.ViewModelType
                }));

                if (!locator.IsUnknown)
                {
                    accumulator.AddNode(CreateNode(request.SnapshotStableKey, viewStableKey, NodeKind.UiComponent, locator.ViewType, locator.ViewType, locator.ViewType, project.Language, projectStableKey, applicationStableKey, confidence, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
                    {
                        ["componentName"] = locator.ViewType,
                        ["detectionMode"] = locator.DetectionMode,
                        ["projectKey"] = projectStableKey.Value,
                        ["sourcePath"] = locator.SourcePath,
                        ["uiArtifactKind"] = "Component",
                        ["uiFramework"] = "Avalonia",
                        ["viewModelType"] = locator.ViewModelType
                    }));
                    accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesComponent, viewModelStableKey, viewStableKey, evidence.StableKey, locator.ViewType, locator.SourcePath, confidence, UnknownState.Known, new Dictionary<string, object?>
                    {
                        ["componentName"] = locator.ViewType,
                        ["detectionMode"] = locator.DetectionMode,
                        ["uiFramework"] = "Avalonia",
                        ["viewModelType"] = locator.ViewModelType
                    }));
                }
                else
                {
                    accumulator.AddWarning($"Avalonia view locator in {locator.SourcePath} requires runtime information: {unknownState.UnknownReason}");
                }
            }
        }

        /// <summary>
        /// Adds a project-level ReactiveUI package usage fact when Avalonia.ReactiveUI evidence exists.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="project">The Avalonia project that references ReactiveUI.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="fallbackArtifact">The optional artifact used as fallback evidence context.</param>
        /// <param name="repositoryContext">The repository context containing source content.</param>
        private static void AccumulateReactiveUiPackage(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaProjectContext project, StableKey projectStableKey, AvaloniaArtifactContext? fallbackArtifact, AvaloniaRepositoryContext repositoryContext)
        {
            // Package-level ReactiveUI evidence helps consumers identify projects that may use reactive view/view-model relationships even before specific generic types are found.
            AvaloniaArtifactContext artifact = fallbackArtifact ?? new AvaloniaArtifactContext(project, project.AbsoluteProjectPath, project.RelativeProjectPath, project.ProjectName, AvaloniaArtifactKind.Code);
            string sourceText = repositoryContext.SourceByPath.TryGetValue(artifact.RelativePath, out string? content) ? content : project.ProjectName;
            StableKey dependencyStableKey = UiStableKeyBuilder.Create("ui-reactiveui://", projectStableKey.Value, "Avalonia.ReactiveUI");
            EvidenceRecord evidence = CreateEvidence(request, artifact, sourceText, "ReactiveUI", "ProjectPackage", Confidence.Medium, UnknownState.Known);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, dependencyStableKey, NodeKind.ExternalService, "Avalonia.ReactiveUI", "Avalonia.ReactiveUI", "Avalonia.ReactiveUI", project.Language, projectStableKey, projectStableKey, Confidence.Medium, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["detectionMode"] = "ProjectPackage",
                ["packageIdentity"] = "Avalonia.ReactiveUI",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiFramework"] = "Avalonia"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DependsOn, projectStableKey, dependencyStableKey, evidence.StableKey, "Avalonia.ReactiveUI", artifact.RelativePath, Confidence.Medium, UnknownState.Known, new Dictionary<string, object?>
            {
                ["detectionMode"] = "ProjectPackage",
                ["packageIdentity"] = "Avalonia.ReactiveUI",
                ["uiFramework"] = "Avalonia"
            }));
        }

        /// <summary>
        /// Adds graph facts for an Avalonia resource, style, or style include observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the resource.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact using or declaring the resource.</param>
        /// <param name="resource">The resource observation.</param>
        private static void AccumulateResource(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, ResourceUsage resource)
        {
            // Resources are normalized into shared resource/style nodes while preserving Avalonia selector and style-include metadata.
            UnknownState unknownState = resource.IsUnknown ? UnknownState.Unknown(resource.UnknownReason!) : UnknownState.Known;
            Confidence confidence = resource.IsUnknown ? Confidence.Low : Confidence.High;
            NodeKind nodeKind = resource.ArtifactKind is "Style" ? NodeKind.UiStyle : NodeKind.UiResource;
            StableKey resourceStableKey = UiStableKeyBuilder.Create("ui-resource://", projectStableKey.Value, "Avalonia", artifact.RelativePath, resource.ArtifactKind, resource.Key, resource.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, resource.SourceText, resource.ArtifactKind, resource.DetectionMode, confidence, unknownState, resource.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, resourceStableKey, nodeKind, resource.Key, artifact.RelativePath, resource.Key, "AXAML", projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["confidenceReason"] = resource.IsUnknown ? resource.UnknownReason : "Static Avalonia resource evidence.",
                ["detectionMode"] = resource.DetectionMode,
                ["projectKey"] = projectStableKey.Value,
                ["resourceKey"] = resource.Key,
                ["sourcePath"] = artifact.RelativePath,
                ["styleKey"] = nodeKind == NodeKind.UiStyle ? resource.Key : null,
                ["uiArtifactKind"] = resource.ArtifactKind,
                ["uiFramework"] = "Avalonia"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, resource.ArtifactKind is "Style" ? EdgeKind.UsesStyle : EdgeKind.UsesUiResource, ownerStableKey, resourceStableKey, evidence.StableKey, resource.Key, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["detectionMode"] = resource.DetectionMode,
                ["resourceKey"] = resource.Key,
                ["styleKey"] = resource.ArtifactKind is "Style" ? resource.Key : null,
                ["uiArtifactKind"] = resource.ArtifactKind,
                ["uiFramework"] = "Avalonia"
            }));

            if (resource.IsUnknown)
            {
                accumulator.AddWarning($"Avalonia {resource.ArtifactKind.ToLowerInvariant()} in {artifact.RelativePath} has unresolved dynamic resource evidence: {resource.Key}.");
            }
        }

        /// <summary>
        /// Adds graph facts for an Avalonia control or nested component observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the control.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact using the control.</param>
        /// <param name="control">The control observation.</param>
        private static void AccumulateControl(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, ControlUsage control)
        {
            // Named controls are represented as UiControl nodes, while project-local Avalonia views are also queryable through component-style relationships.
            StableKey controlStableKey = UiStableKeyBuilder.Create("ui-control://", projectStableKey.Value, "Avalonia", artifact.RelativePath, control.ControlType, control.ControlName, control.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, control.SourceText, "Control", "AxamlControl", Confidence.High, UnknownState.Known, control.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, controlStableKey, NodeKind.UiControl, control.ControlName, artifact.RelativePath, control.ControlName, "AXAML", projectStableKey, ownerStableKey, Confidence.High, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["controlName"] = control.ControlName,
                ["controlType"] = control.ControlType,
                ["detectionMode"] = "AxamlControl",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Control",
                ["uiFramework"] = "Avalonia"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, control.IsCustomComponent ? EdgeKind.UsesComponent : EdgeKind.UsesControl, ownerStableKey, controlStableKey, evidence.StableKey, control.ControlName, artifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["controlName"] = control.ControlName,
                ["controlType"] = control.ControlType,
                ["detectionMode"] = "AxamlControl",
                ["uiFramework"] = "Avalonia"
            }));
        }

        /// <summary>
        /// Adds graph facts for an Avalonia binding observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the binding.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the bound artifact.</param>
        /// <param name="binding">The binding observation.</param>
        private static void AccumulateBinding(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, BindingUsage binding)
        {
            // Unqualified `{Binding}` expressions are explicit unknowns because their runtime target depends on DataContext shape.
            UnknownState unknownState = binding.IsUnknown ? UnknownState.Unknown("Avalonia binding path could not be resolved statically.") : UnknownState.Known;
            Confidence confidence = binding.IsUnknown ? Confidence.Low : Confidence.High;
            StableKey bindingStableKey = UiStableKeyBuilder.Create("ui-binding://", projectStableKey.Value, "Avalonia", artifact.RelativePath, binding.PropertyName, binding.BindingPath, binding.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, binding.SourceText, "Binding", "AxamlBinding", confidence, unknownState, binding.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, bindingStableKey, NodeKind.Binding, binding.BindingPath, artifact.RelativePath, binding.BindingPath, "AXAML", projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["bindingPath"] = binding.BindingPath,
                ["confidenceReason"] = binding.IsUnknown ? "Binding expression did not include a static path." : "Binding expression included a static path.",
                ["detectionMode"] = "AxamlBinding",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Binding",
                ["uiFramework"] = "Avalonia"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.BindsTo, ownerStableKey, bindingStableKey, evidence.StableKey, binding.BindingPath, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["bindingPath"] = binding.BindingPath,
                ["detectionMode"] = "AxamlBinding",
                ["uiFramework"] = "Avalonia"
            }));

            if (binding.IsUnknown)
            {
                accumulator.AddWarning($"Avalonia unresolved binding path in {artifact.RelativePath} at line {binding.LineNumber.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        /// <summary>
        /// Adds graph facts for an Avalonia command binding observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the command binding.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact using the command.</param>
        /// <param name="command">The command observation.</param>
        private static void AccumulateCommand(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, CommandUsage command)
        {
            // Command bindings usually target view-model command properties and are represented separately from events.
            UnknownState unknownState = command.IsUnknown ? UnknownState.Unknown("Avalonia command binding could not be resolved statically.") : UnknownState.Known;
            Confidence confidence = command.IsUnknown ? Confidence.Low : Confidence.High;
            StableKey commandStableKey = UiStableKeyBuilder.Create("ui-command://", projectStableKey.Value, "Avalonia", artifact.RelativePath, command.CommandName, command.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, command.SourceText, "Command", "AxamlCommand", confidence, unknownState, command.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, commandStableKey, NodeKind.Command, command.CommandName, artifact.RelativePath, command.CommandName, "AXAML", projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["commandName"] = command.CommandName,
                ["detectionMode"] = "AxamlCommand",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Command",
                ["uiFramework"] = "Avalonia"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesCommand, ownerStableKey, commandStableKey, evidence.StableKey, command.CommandName, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["commandName"] = command.CommandName,
                ["detectionMode"] = "AxamlCommand",
                ["uiFramework"] = "Avalonia"
            }));
        }

        /// <summary>
        /// Adds graph facts for an Avalonia routed event handler observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the routed event.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact handling the event.</param>
        /// <param name="eventUsage">The event observation.</param>
        private static void AccumulateEvent(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, EventUsage eventUsage)
        {
            // Avalonia AXAML events are represented by command nodes so handlers can be traversed uniformly with command facts.
            StableKey commandStableKey = UiStableKeyBuilder.Create("ui-command://", projectStableKey.Value, "Avalonia", artifact.RelativePath, eventUsage.HandlerName, eventUsage.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, eventUsage.SourceText, "Command", eventUsage.DetectionMode, Confidence.High, UnknownState.Known, eventUsage.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, commandStableKey, NodeKind.Command, eventUsage.HandlerName, artifact.RelativePath, eventUsage.HandlerName, "AXAML", projectStableKey, ownerStableKey, Confidence.High, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["commandName"] = eventUsage.HandlerName,
                ["detectionMode"] = eventUsage.DetectionMode,
                ["eventName"] = eventUsage.EventName,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Command",
                ["uiFramework"] = "Avalonia"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.HandlesUiEvent, ownerStableKey, commandStableKey, evidence.StableKey, eventUsage.EventName, artifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["commandName"] = eventUsage.HandlerName,
                ["detectionMode"] = eventUsage.DetectionMode,
                ["eventName"] = eventUsage.EventName,
                ["uiFramework"] = "Avalonia"
            }));
        }

        /// <summary>
        /// Adds graph facts for an Avalonia navigation observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains navigation evidence.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the navigating artifact.</param>
        /// <param name="navigation">The navigation observation.</param>
        private static void AccumulateNavigation(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, NavigationUsage navigation)
        {
            // Static ReactiveUI navigation targets become view-model nodes; computed navigation is recorded as an explicit unknown instead of a guessed route.
            UnknownState unknownState = navigation.IsUnknown ? UnknownState.Unknown("Avalonia navigation target is computed from runtime state.") : UnknownState.Known;
            Confidence confidence = navigation.IsUnknown ? Confidence.Low : Confidence.High;
            StableKey targetStableKey = UiStableKeyBuilder.Create("ui-viewmodel://", projectStableKey.Value, "Avalonia", artifact.RelativePath, navigation.Target, navigation.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, navigation.SourceText, "ViewModel", navigation.DetectionMode, confidence, unknownState, navigation.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, targetStableKey, NodeKind.ViewModel, navigation.Target, artifact.RelativePath, navigation.Target, artifact.Project.Language, projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["confidenceReason"] = navigation.IsUnknown ? "Navigation target uses a runtime expression." : "Navigation target is statically visible.",
                ["detectionMode"] = navigation.DetectionMode,
                ["navigationTarget"] = navigation.Target,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "ViewModel",
                ["uiFramework"] = "Avalonia"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.NavigatesTo, ownerStableKey, targetStableKey, evidence.StableKey, navigation.Target, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["detectionMode"] = navigation.DetectionMode,
                ["navigationTarget"] = navigation.Target,
                ["uiFramework"] = "Avalonia"
            }));

            if (navigation.IsUnknown)
            {
                accumulator.AddWarning($"Avalonia runtime navigation target in {artifact.RelativePath} at line {navigation.LineNumber.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        /// <summary>
        /// Adds graph facts for Avalonia view-model evidence or convention-only unknowns.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="repositoryContext">The repository context containing known view-model types.</param>
        /// <param name="artifact">The artifact whose view model is being correlated.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the view using the view model.</param>
        /// <param name="root">The parsed AXAML root element.</param>
        private static void AccumulateViewModel(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaRepositoryContext repositoryContext, AvaloniaArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, XElement root)
        {
            // x:DataType and design data contexts are high-confidence evidence; missing conventions are recorded as unknowns so consumers can distinguish absence from unresolved patterns.
            if (artifact.ArtifactKind is AvaloniaArtifactKind.Application or AvaloniaArtifactKind.Styles or AvaloniaArtifactKind.ResourceDictionary)
            {
                return;
            }

            ViewModelUsage usage = ExtractViewModel(root, artifact, repositoryContext);
            UnknownState unknownState = usage.IsUnknown ? UnknownState.Unknown("Avalonia view model is inferred by convention only and was not found in source.") : UnknownState.Known;
            Confidence confidence = usage.IsUnknown ? Confidence.Low : usage.Confidence;
            StableKey viewModelStableKey = UiStableKeyBuilder.Create("ui-viewmodel://", projectStableKey.Value, "Avalonia", artifact.RelativePath, usage.ViewModelType, usage.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, usage.SourceText, "ViewModel", usage.DetectionMode, confidence, unknownState, usage.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, viewModelStableKey, NodeKind.ViewModel, usage.ViewModelType, artifact.RelativePath, usage.ViewModelType, artifact.Project.Language, projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["confidenceReason"] = usage.ConfidenceReason,
                ["detectionMode"] = usage.DetectionMode,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "ViewModel",
                ["uiFramework"] = "Avalonia",
                ["viewModelType"] = usage.ViewModelType
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesViewModel, ownerStableKey, viewModelStableKey, evidence.StableKey, usage.ViewModelType, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["detectionMode"] = usage.DetectionMode,
                ["uiFramework"] = "Avalonia",
                ["viewModelType"] = usage.ViewModelType
            }));

            if (usage.IsUnknown)
            {
                accumulator.AddWarning($"Avalonia convention-only view model for {artifact.TypeName} in {artifact.RelativePath} could not be found in source.");
            }
        }

        /// <summary>
        /// Adds graph facts for a ReactiveUI generic view or ambiguous relationship observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact whose source contains ReactiveUI evidence.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the view using the view model.</param>
        /// <param name="usage">The ReactiveUI view-model observation.</param>
        private static void AccumulateReactiveViewModel(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, ViewModelUsage usage)
        {
            // ReactiveWindow<TViewModel> and ReactiveUserControl<TViewModel> provide direct relationships; non-generic ReactiveUI base classes remain explicit unknowns.
            UnknownState unknownState = usage.IsUnknown ? UnknownState.Unknown("Avalonia ReactiveUI relationship is ambiguous without generic view-model evidence.") : UnknownState.Known;
            Confidence confidence = usage.IsUnknown ? Confidence.Low : usage.Confidence;
            StableKey viewModelStableKey = UiStableKeyBuilder.Create("ui-viewmodel://", projectStableKey.Value, "Avalonia", artifact.RelativePath, usage.ViewModelType, usage.DetectionMode);
            EvidenceRecord evidence = CreateEvidence(request, artifact, usage.SourceText, "ViewModel", usage.DetectionMode, confidence, unknownState, usage.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, viewModelStableKey, NodeKind.ViewModel, usage.ViewModelType, artifact.RelativePath, usage.ViewModelType, artifact.Project.Language, projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["confidenceReason"] = usage.ConfidenceReason,
                ["detectionMode"] = usage.DetectionMode,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "ViewModel",
                ["uiFramework"] = "Avalonia",
                ["viewModelType"] = usage.ViewModelType
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesViewModel, ownerStableKey, viewModelStableKey, evidence.StableKey, usage.ViewModelType, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["detectionMode"] = usage.DetectionMode,
                ["uiFramework"] = "Avalonia",
                ["viewModelType"] = usage.ViewModelType
            }));

            if (usage.IsUnknown)
            {
                accumulator.AddWarning($"Avalonia ReactiveUI relationship in {artifact.RelativePath} is ambiguous without generic view-model evidence.");
            }
        }

        /// <summary>
        /// Adds graph facts for an Avalonia code-behind service usage.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact whose code-behind uses the service.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the UI artifact using the service.</param>
        /// <param name="serviceUsage">The service usage observation.</param>
        private static void AccumulateServiceUsage(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, ServiceUsage serviceUsage)
        {
            // Service usages are correlated from code-behind source and linked with DEPENDS_ON because semantic service registration may be emitted by earlier stages.
            StableKey serviceStableKey = UiStableKeyBuilder.Create("ui-service://", projectStableKey.Value, serviceUsage.TypeName);
            EvidenceRecord evidence = CreateEvidence(request, artifact, serviceUsage.SourceText, "ServiceUsage", "CodeBehind", Confidence.Medium, UnknownState.Known, serviceUsage.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, serviceStableKey, NodeKind.Type, serviceUsage.TypeName, serviceUsage.TypeName, serviceUsage.TypeName, artifact.Project.Language, projectStableKey, projectStableKey, Confidence.Medium, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["detectionMode"] = "CodeBehind",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["typeName"] = serviceUsage.TypeName,
                ["uiFramework"] = "Avalonia"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DependsOn, ownerStableKey, serviceStableKey, evidence.StableKey, serviceUsage.TypeName, artifact.RelativePath, Confidence.Medium, UnknownState.Known, new Dictionary<string, object?>
            {
                ["detectionMode"] = "CodeBehind",
                ["typeName"] = serviceUsage.TypeName,
                ["uiFramework"] = "Avalonia"
            }));
        }

        /// <summary>
        /// Adds graph facts for an Avalonia code-behind data-access or external package usage.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact whose code-behind uses the dependency.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the UI artifact using the dependency.</param>
        /// <param name="dataAccessUsage">The data-access usage observation.</param>
        private static void AccumulateDataAccessUsage(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, DataAccessUsage dataAccessUsage)
        {
            // Data-access usages are emitted as external-service facts so UI-to-data paths remain visible before specialized data-access stages correlate exact contexts.
            StableKey dependencyStableKey = UiStableKeyBuilder.Create("ui-data-access://", projectStableKey.Value, dataAccessUsage.PackageIdentity);
            EvidenceRecord evidence = CreateEvidence(request, artifact, dataAccessUsage.SourceText, "DataAccess", "CodeBehind", Confidence.Medium, UnknownState.Known, dataAccessUsage.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, dependencyStableKey, NodeKind.ExternalService, dataAccessUsage.PackageIdentity, dataAccessUsage.PackageIdentity, dataAccessUsage.PackageIdentity, artifact.Project.Language, projectStableKey, projectStableKey, Confidence.Medium, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["detectionMode"] = "CodeBehind",
                ["packageIdentity"] = dataAccessUsage.PackageIdentity,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiFramework"] = "Avalonia"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.CallsApi, ownerStableKey, dependencyStableKey, evidence.StableKey, dataAccessUsage.PackageIdentity, artifact.RelativePath, Confidence.Medium, UnknownState.Known, new Dictionary<string, object?>
            {
                ["detectionMode"] = "CodeBehind",
                ["packageIdentity"] = dataAccessUsage.PackageIdentity,
                ["uiFramework"] = "Avalonia"
            }));
        }

        /// <summary>
        /// Adds explicit unknown facts for dynamic resource, style, binding, locator, and navigation patterns that cannot be resolved statically.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The Avalonia artifact that contains runtime-dependent markup.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact containing the unknowns.</param>
        /// <param name="root">The parsed AXAML root element.</param>
        private static void AccumulateDynamicUnknowns(AvaloniaAxamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, AvaloniaArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, XElement root)
        {
            // Dynamic unknowns are handled after normal extraction so unsupported runtime decisions are captured without blocking known facts.
            foreach (UnknownUsage unknown in ExtractUnknowns(root))
            {
                UnknownState unknownState = UnknownState.Unknown(unknown.UnknownReason);
                StableKey unknownStableKey = UiStableKeyBuilder.Create("ui-unknown://", projectStableKey.Value, "Avalonia", artifact.RelativePath, unknown.Category, unknown.LineNumber.ToString(CultureInfo.InvariantCulture));
                EvidenceRecord evidence = CreateEvidence(request, artifact, unknown.SourceText, unknown.ArtifactKind, unknown.DetectionMode, Confidence.Low, unknownState, unknown.LineNumber);
                accumulator.AddEvidence(evidence);
                accumulator.AddNode(CreateNode(request.SnapshotStableKey, unknownStableKey, unknown.NodeKind, unknown.DisplayName, artifact.RelativePath, unknown.DisplayName, "AXAML", projectStableKey, ownerStableKey, Confidence.Low, unknownState, evidence.StableKey, new Dictionary<string, object?>
                {
                    ["confidenceReason"] = unknown.UnknownReason,
                    ["detectionMode"] = unknown.DetectionMode,
                    ["projectKey"] = projectStableKey.Value,
                    ["sourcePath"] = artifact.RelativePath,
                    ["uiArtifactKind"] = unknown.ArtifactKind,
                    ["uiFramework"] = "Avalonia"
                }));
                accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, GetUnknownEdgeKind(unknown.NodeKind), ownerStableKey, unknownStableKey, evidence.StableKey, unknown.Category, artifact.RelativePath, Confidence.Low, unknownState, new Dictionary<string, object?>
                {
                    ["detectionMode"] = unknown.DetectionMode,
                    ["uiFramework"] = "Avalonia"
                }));
                accumulator.AddWarning($"Avalonia {unknown.Category} in {artifact.RelativePath} requires runtime information: {unknown.UnknownReason}");
            }
        }

        /// <summary>
        /// Reads Avalonia-relevant metadata from a C# or VB.NET project file.
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
                bool usesReactiveUi = packageIdentities.Any(package => package.Contains("ReactiveUI", StringComparison.OrdinalIgnoreCase)) || text.Contains("UseReactiveUI", StringComparison.OrdinalIgnoreCase) || text.Contains("ReactiveWindow", StringComparison.OrdinalIgnoreCase) || text.Contains("ReactiveUserControl", StringComparison.OrdinalIgnoreCase);
                bool isAvaloniaCandidate = text.Contains("Avalonia", StringComparison.OrdinalIgnoreCase)
                    || packageIdentities.Any(package => package.Contains("Avalonia", StringComparison.OrdinalIgnoreCase))
                    || Directory.EnumerateFiles(Path.GetDirectoryName(projectPath) ?? string.Empty, "*.axaml", SearchOption.AllDirectories).Any(IsRepositorySourcePath);
                return new ProjectMetadata(relativeProjectPath, projectName, targetFrameworks.Length == 0 ? ["Unknown"] : targetFrameworks, language, packageIdentities, usesReactiveUi, isAvaloniaCandidate);
            }
            catch (Exception) when (IsXmlReadException())
            {
                // Malformed project files cannot be evaluated safely; the project is skipped rather than producing guessed Avalonia facts.
                return new ProjectMetadata(relativeProjectPath, projectName, ["Unknown"], language, [], false, false);
            }
        }

        /// <summary>
        /// Reads target framework values from a project document.
        /// </summary>
        /// <param name="document">The project XML document.</param>
        /// <returns>Target framework values in stable order.</returns>
        private static string[] ReadTargetFrameworks(XDocument document)
        {
            // Avalonia projects may use either TargetFramework or TargetFrameworks depending on desktop and mobile head configuration.
            string? combined = ReadFirstElementValue(document, "TargetFrameworks") ?? ReadFirstElementValue(document, "TargetFramework");
            return string.IsNullOrWhiteSpace(combined) ? [] : combined.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>
        /// Classifies a repository artifact into an Avalonia AXAML/source category.
        /// </summary>
        /// <param name="relativePath">The repository-relative artifact path.</param>
        /// <param name="content">The artifact content.</param>
        /// <returns>The artifact kind used by Avalonia extraction.</returns>
        private static AvaloniaArtifactKind ClassifyArtifact(string relativePath, string content)
        {
            // Classification relies on AXAML root tags and source naming because extractor execution must not load Avalonia assemblies.
            if (relativePath.EndsWith(".axaml.cs", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith(".axaml.vb", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase))
            {
                return AvaloniaArtifactKind.Code;
            }

            string trimmed = content.TrimStart();
            if (trimmed.StartsWith("<Application", StringComparison.OrdinalIgnoreCase))
            {
                return AvaloniaArtifactKind.Application;
            }

            if (trimmed.StartsWith("<Window", StringComparison.OrdinalIgnoreCase))
            {
                return AvaloniaArtifactKind.Window;
            }

            if (trimmed.StartsWith("<UserControl", StringComparison.OrdinalIgnoreCase))
            {
                return AvaloniaArtifactKind.UserControl;
            }

            if (trimmed.StartsWith("<Styles", StringComparison.OrdinalIgnoreCase))
            {
                return AvaloniaArtifactKind.Styles;
            }

            if (trimmed.StartsWith("<ResourceDictionary", StringComparison.OrdinalIgnoreCase))
            {
                return AvaloniaArtifactKind.ResourceDictionary;
            }

            return AvaloniaArtifactKind.Other;
        }

        /// <summary>
        /// Extracts the primary type or artifact name from Avalonia markup or source content.
        /// </summary>
        /// <param name="relativePath">The repository-relative artifact path.</param>
        /// <param name="content">The artifact content.</param>
        /// <param name="artifactKind">The artifact kind being named.</param>
        /// <returns>The primary artifact type name.</returns>
        private static string ExtractPrimaryTypeName(string relativePath, string content, AvaloniaArtifactKind artifactKind)
        {
            // AXAML `x:Class` is authoritative; code artifacts use source declarations; dictionaries and styles fall back to file names.
            if (artifactKind is AvaloniaArtifactKind.Code)
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
        /// Attempts to parse AXAML content as XML while preserving non-fatal extraction behavior.
        /// </summary>
        /// <param name="content">The AXAML content.</param>
        /// <param name="relativePath">The repository-relative path used for diagnostics.</param>
        /// <param name="accumulator">The accumulator that receives parse warnings.</param>
        /// <returns>The parsed AXAML document, or <see langword="null" /> when parsing fails.</returns>
        private static XDocument? TryLoadAxaml(string content, string relativePath, ArchitectureSnapshotAccumulator accumulator)
        {
            // AXAML is XML-like enough for static markup extraction; parse failures are diagnostics rather than fatal pipeline errors.
            try
            {
                return XDocument.Parse(content, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            }
            catch (Exception exception) when (exception is XmlException or InvalidOperationException)
            {
                accumulator.AddWarning($"Avalonia AXAML artifact {relativePath} could not be parsed: {exception.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts resource, style, and style-include observations from a parsed Avalonia AXAML document.
        /// </summary>
        /// <param name="document">The parsed AXAML document.</param>
        /// <returns>Resource observations in document order.</returns>
        private static IReadOnlyList<ResourceUsage> ExtractResources(XDocument document)
        {
            // Avalonia resources can be declared as keyed objects, selector styles, merged dictionaries, or style includes.
            List<ResourceUsage> resources = [];
            foreach (XElement element in document.Descendants())
            {
                string localName = element.Name.LocalName;
                string? key = GetXamlAttribute(element, "Key") ?? element.Attribute("Source")?.Value;
                if (string.IsNullOrWhiteSpace(key) && localName is "Style")
                {
                    key = element.Attribute("Selector")?.Value ?? element.Attribute("TargetType")?.Value ?? "ImplicitStyle";
                }

                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                string artifactKind = localName switch
                {
                    "Style" or "StyleInclude" or "Styles" => "Style",
                    "ResourceDictionary" when element.Attribute("Source") is not null => "Resource",
                    _ when IsResourceContainer(element) => "Resource",
                    _ => string.Empty
                };
                if (string.IsNullOrWhiteSpace(artifactKind))
                {
                    continue;
                }

                resources.Add(new ResourceUsage(key.Trim(), artifactKind, localName is "StyleInclude" ? "AxamlStyleInclude" : "AxamlResource", GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting), false, null));
            }

            return resources;
        }

        /// <summary>
        /// Extracts named controls and custom component usages from a parsed Avalonia AXAML document.
        /// </summary>
        /// <param name="document">The parsed AXAML document.</param>
        /// <param name="artifact">The artifact that owns the document.</param>
        /// <returns>Control observations in document order.</returns>
        private static IReadOnlyList<ControlUsage> ExtractControls(XDocument document, AvaloniaArtifactContext artifact)
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
        /// Extracts binding expressions from attributes in a parsed Avalonia AXAML document.
        /// </summary>
        /// <param name="document">The parsed AXAML document.</param>
        /// <returns>Binding observations in document order.</returns>
        private static IReadOnlyList<BindingUsage> ExtractBindings(XDocument document)
        {
            // Avalonia binding markup extensions can contain `Path=Name`, direct `Binding Name`, compiled bindings, reflection bindings, or bare `{Binding}` expressions.
            List<BindingUsage> bindings = [];
            foreach (XElement element in document.Descendants())
            {
                foreach (XAttribute attribute in element.Attributes())
                {
                    if (!attribute.Value.Contains("{Binding", StringComparison.Ordinal) && !attribute.Value.Contains("{CompiledBinding", StringComparison.Ordinal) && !attribute.Value.Contains("{ReflectionBinding", StringComparison.Ordinal))
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
        /// Extracts command bindings from Avalonia Command attributes.
        /// </summary>
        /// <param name="document">The parsed AXAML document.</param>
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
                bool isUnknown = string.Equals(commandName, "Unknown", StringComparison.Ordinal);
                if (isUnknown)
                {
                    commandName = commandAttribute.Value.Trim();
                }

                commands.Add(new CommandUsage(commandName, isUnknown, GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
            }

            return commands;
        }

        /// <summary>
        /// Extracts event attributes from Avalonia AXAML markup.
        /// </summary>
        /// <param name="document">The parsed AXAML document.</param>
        /// <returns>Event observations in document order.</returns>
        private static IReadOnlyList<EventUsage> ExtractMarkupEvents(XDocument document)
        {
            // Events are inferred from known Avalonia event attribute names with method-like handler values.
            List<EventUsage> events = [];
            foreach (XElement element in document.Descendants())
            {
                foreach (XAttribute attribute in element.Attributes().Where(IsRoutedEventAttribute))
                {
                    events.Add(new EventUsage(attribute.Name.LocalName, attribute.Value.Trim(), "AxamlEvent", GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
                }
            }

            return events;
        }

        /// <summary>
        /// Extracts static navigation source attributes from Avalonia markup.
        /// </summary>
        /// <param name="document">The parsed AXAML document.</param>
        /// <returns>Navigation observations in document order.</returns>
        private static IReadOnlyList<NavigationUsage> ExtractNavigation(XDocument document)
        {
            // Markup-level navigation is often represented by content controls whose content is a view model.
            List<NavigationUsage> navigations = [];
            foreach (XElement element in document.Descendants())
            {
                foreach (XAttribute attribute in element.Attributes().Where(attribute => string.Equals(attribute.Name.LocalName, "NavigateUri", StringComparison.Ordinal) || string.Equals(attribute.Name.LocalName, "Content", StringComparison.Ordinal)))
                {
                    if (!attribute.Value.Contains("ViewModel", StringComparison.Ordinal) && !attribute.Value.Contains("Navigate", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string target = ExtractBindingPath(attribute.Value);
                    bool isUnknown = string.Equals(target, "Unknown", StringComparison.Ordinal) || attribute.Value.Contains('{', StringComparison.Ordinal);
                    navigations.Add(new NavigationUsage(isUnknown ? "RuntimeNavigation" : target.Trim(), isUnknown, "AxamlNavigation", GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
                }
            }

            return navigations;
        }

        /// <summary>
        /// Extracts a direct or convention-based view-model usage for an Avalonia artifact.
        /// </summary>
        /// <param name="root">The parsed AXAML root element.</param>
        /// <param name="artifact">The artifact being analyzed.</param>
        /// <param name="repositoryContext">The repository context containing known view-model types.</param>
        /// <returns>The view-model usage classification.</returns>
        private static ViewModelUsage ExtractViewModel(XElement root, AvaloniaArtifactContext artifact, AvaloniaRepositoryContext repositoryContext)
        {
            // x:DataType is the preferred static Avalonia view-model evidence because it supports compiled bindings.
            string? dataType = GetXamlAttribute(root, "DataType");
            if (!string.IsNullOrWhiteSpace(dataType))
            {
                string viewModelType = NormalizeTypeName(dataType);
                return new ViewModelUsage(viewModelType, "AxamlDataType", Confidence.High, "x:DataType identifies the Avalonia view model for compiled bindings.", false, GetLineNumber(root), root.ToString(SaveOptions.DisableFormatting));
            }

            XElement? designDataContext = root.Descendants().FirstOrDefault(element => element.Name.LocalName.EndsWith(".DataContext", StringComparison.Ordinal));
            XElement? directViewModel = designDataContext?.Elements().FirstOrDefault();
            if (directViewModel is not null)
            {
                string viewModelType = directViewModel.Name.LocalName;
                return new ViewModelUsage(viewModelType, "DesignDataContext", Confidence.Medium, "Design data-context element identifies a likely view model.", false, GetLineNumber(directViewModel), directViewModel.ToString(SaveOptions.DisableFormatting));
            }

            string conventionType = string.Concat(artifact.TypeName, "ViewModel").Replace("WindowViewModel", "ViewModel", StringComparison.Ordinal).Replace("ViewViewModel", "ViewModel", StringComparison.Ordinal).Replace("ControlViewModel", "ViewModel", StringComparison.Ordinal);
            if (repositoryContext.ViewModelTypeNames.Contains(conventionType))
            {
                return new ViewModelUsage(conventionType, "Convention", Confidence.Medium, "Repository source contains a matching convention-based view-model type.", false, 1, root.ToString(SaveOptions.DisableFormatting));
            }

            return new ViewModelUsage(conventionType, "Convention", Confidence.Low, "Convention-based view-model type was not found in source.", true, 1, root.ToString(SaveOptions.DisableFormatting));
        }

        /// <summary>
        /// Extracts explicit unknown runtime-dependent Avalonia observations from parsed markup.
        /// </summary>
        /// <param name="root">The parsed AXAML root element.</param>
        /// <returns>Unknown observations in document order.</returns>
        private static IReadOnlyList<UnknownUsage> ExtractUnknowns(XElement root)
        {
            // Unknown extraction focuses on runtime patterns called out by static UI extraction rather than attempting to model every Avalonia dynamic feature.
            List<UnknownUsage> unknowns = [];
            foreach (XElement element in root.DescendantsAndSelf())
            {
                foreach (XAttribute attribute in element.Attributes())
                {
                    if (attribute.Value.Contains("{DynamicResource", StringComparison.Ordinal))
                    {
                        unknowns.Add(new UnknownUsage("dynamic resource", "DynamicResource", "Resource", NodeKind.UiResource, "DynamicResource", "Avalonia dynamic resource target is computed from runtime state.", GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
                    }

                    if (string.Equals(attribute.Name.LocalName, "Classes", StringComparison.Ordinal) && attribute.Value.Contains('{', StringComparison.Ordinal))
                    {
                        unknowns.Add(new UnknownUsage("dynamic style", "RuntimeStyleClass", "Style", NodeKind.UiStyle, "RuntimeStyle", "Avalonia style class is computed from runtime state.", GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
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
            // Code-behind handlers supplement AXAML event declarations and preserve evidence when markup is omitted.
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
        /// Extracts ReactiveUI navigation calls from code-behind source.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <returns>Navigation observations in source order.</returns>
        private static IReadOnlyList<NavigationUsage> ExtractCodeBehindNavigation(string content)
        {
            // Router.Navigate.Execute(new TargetViewModel()) is static enough to identify a view-model target, while other expressions become explicit unknowns.
            List<NavigationUsage> navigations = [];
            foreach (SourceLine line in SplitLines(content))
            {
                Match staticMatch = StaticNavigateRegex().Match(line.Text);
                if (staticMatch.Success)
                {
                    navigations.Add(new NavigationUsage(staticMatch.Groups["target"].Value.Trim(), false, "CodeNavigation", line.LineNumber, line.Text.Trim()));
                    continue;
                }

                Match dynamicMatch = DynamicNavigateRegex().Match(line.Text);
                if (dynamicMatch.Success)
                {
                    navigations.Add(new NavigationUsage("RuntimeNavigation", true, "CodeNavigation", line.LineNumber, line.Text.Trim()));
                }
            }

            return navigations;
        }

        /// <summary>
        /// Extracts Avalonia view-locator observations from source content.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <param name="sourcePath">The repository-relative source path containing view-locator source.</param>
        /// <returns>View-locator observations in source order.</returns>
        private static IReadOnlyList<ViewLocatorUsage> ExtractViewLocatorUsages(string content, string sourcePath)
        {
            // IDataTemplate-based view locators commonly map view-model types to views in switch expressions or use runtime reflection conventions.
            if (!content.Contains("IDataTemplate", StringComparison.Ordinal) && !content.Contains("ViewLocator", StringComparison.Ordinal))
            {
                return [];
            }

            List<ViewLocatorUsage> locators = [];
            foreach (SourceLine line in SplitLines(content))
            {
                Match staticMatch = ViewLocatorStaticRegex().Match(line.Text);
                if (staticMatch.Success)
                {
                    locators.Add(new ViewLocatorUsage(staticMatch.Groups["viewModel"].Value.Trim(), staticMatch.Groups["view"].Value.Trim(), false, "ViewLocator", sourcePath, line.LineNumber, line.Text.Trim()));
                    continue;
                }

                if (line.Text.Contains("Activator.CreateInstance", StringComparison.Ordinal) || line.Text.Contains("Type.GetType", StringComparison.Ordinal) || line.Text.Contains("Replace(\"ViewModel\"", StringComparison.Ordinal))
                {
                    locators.Add(new ViewLocatorUsage("RuntimeViewModel", "RuntimeView", true, "ViewLocator", sourcePath, line.LineNumber, line.Text.Trim()));
                }
            }

            return locators;
        }

        /// <summary>
        /// Extracts ReactiveUI view-model relationships from source content.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <returns>ReactiveUI view-model observations in source order.</returns>
        private static IReadOnlyList<ViewModelUsage> ExtractReactiveViewModelUsages(string content)
        {
            // Generic ReactiveUI base classes give high-confidence view-model evidence; non-generic usage is retained as an ambiguous unknown.
            List<ViewModelUsage> usages = [];
            foreach (SourceLine line in SplitLines(content))
            {
                Match genericMatch = ReactiveGenericRegex().Match(line.Text);
                if (genericMatch.Success)
                {
                    string viewModelType = genericMatch.Groups["viewModel"].Value.Trim();
                    usages.Add(new ViewModelUsage(viewModelType, "ReactiveUiGeneric", Confidence.High, "ReactiveUI generic view base identifies the view model.", false, line.LineNumber, line.Text.Trim()));
                    continue;
                }

                if (line.Text.Contains("ReactiveWindow", StringComparison.Ordinal) || line.Text.Contains("ReactiveUserControl", StringComparison.Ordinal))
                {
                    usages.Add(new ViewModelUsage("UnknownReactiveViewModel", "ReactiveUiGeneric", Confidence.Low, "ReactiveUI base class did not provide a generic view-model type.", true, line.LineNumber, line.Text.Trim()));
                }
            }

            return usages;
        }

        /// <summary>
        /// Extracts service type usages from source content.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <returns>Service usages in source order.</returns>
        private static IReadOnlyList<ServiceUsage> ExtractServiceUsages(string content)
        {
            // Type-name heuristics intentionally align with prior static UI extraction slices until full semantic facts are available in-process.
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
            // View-model declarations support direct x:DataType validation and convention-based confidence classification.
            foreach (Match match in ViewModelClassRegex().Matches(content))
            {
                yield return match.Groups["name"].Value.Trim();
            }
        }

        /// <summary>
        /// Creates a source-backed evidence record for one Avalonia observation.
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
        private static EvidenceRecord CreateEvidence(AvaloniaAxamlExtractionRequest request, AvaloniaArtifactContext artifact, string sourceText, string artifactKind, string detectionMode, Confidence confidence, UnknownState unknownState, int? lineNumber = null)
        {
            // Evidence previews are redacted by the shared UI evidence factory so secrets in AXAML or connection strings do not leak to graph consumers.
            int startLine = lineNumber ?? 1;
            return UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, startLine, startLine, sourceText), "Avalonia", artifactKind, detectionMode, confidence, unknownState);
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
        /// Creates base metadata shared by Avalonia artifact nodes.
        /// </summary>
        /// <param name="project">The owning Avalonia project.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="sourcePath">The repository-relative source path.</param>
        /// <param name="artifactKind">The UI artifact-kind metadata value.</param>
        /// <param name="typeName">The associated artifact type name.</param>
        /// <param name="detectionMode">The detection mode metadata value.</param>
        /// <returns>A mutable metadata dictionary populated with shared Avalonia fields.</returns>
        private static Dictionary<string, object?> CreateBaseMetadata(AvaloniaProjectContext project, StableKey projectStableKey, string sourcePath, string artifactKind, string typeName, string detectionMode)
        {
            // Centralizing metadata fields keeps all Avalonia facts aligned on lower-camel-case keys and normalized framework values.
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["detectionMode"] = detectionMode,
                ["language"] = "AXAML",
                ["packageIdentity"] = string.Join(",", project.PackageIdentities),
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = sourcePath,
                ["targetFramework"] = string.Join(";", project.TargetFrameworks),
                ["typeName"] = typeName,
                ["uiArtifactKind"] = artifactKind,
                ["uiFramework"] = "Avalonia"
            };
        }

        /// <summary>
        /// Creates a stable key for an Avalonia primary artifact node.
        /// </summary>
        /// <param name="artifact">The artifact being represented.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <returns>The deterministic artifact stable key.</returns>
        private static StableKey CreateArtifactStableKey(AvaloniaArtifactContext artifact, StableKey projectStableKey)
        {
            // Primary artifact identity uses project key, framework, repository-relative path, artifact kind, and type name.
            return UiStableKeyBuilder.Create("ui-artifact://", projectStableKey.Value, "Avalonia", artifact.RelativePath, artifact.ArtifactKind.ToString(), artifact.TypeName);
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

            if (nodeKind == NodeKind.UiStyle)
            {
                return EdgeKind.UsesStyle;
            }

            if (nodeKind == NodeKind.ViewModel)
            {
                return EdgeKind.UsesViewModel;
            }

            return EdgeKind.BindsTo;
        }

        /// <summary>
        /// Extracts an Avalonia binding path from a markup extension value.
        /// </summary>
        /// <param name="value">The raw AXAML attribute value.</param>
        /// <returns>The static binding path, or Unknown when none can be resolved.</returns>
        private static string ExtractBindingPath(string value)
        {
            // The parser handles common Avalonia binding forms without attempting to evaluate the full markup-extension grammar.
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
            // Local-name matching supports `x:Key`, `x:Name`, `x:DataType`, and namespace-prefixed attached attributes without depending on namespace prefixes.
            return element.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))?.Value;
        }

        /// <summary>
        /// Normalizes an AXAML type reference such as `vm:MainWindowViewModel` into a type name.
        /// </summary>
        /// <param name="value">The raw AXAML type reference.</param>
        /// <returns>The normalized type name segment.</returns>
        private static string NormalizeTypeName(string value)
        {
            // AXAML namespace prefixes and nested generic punctuation are removed because graph metadata stores type names, not prefix syntax.
            string trimmed = value.Trim();
            int prefixSeparator = trimmed.LastIndexOf(':');
            if (prefixSeparator >= 0 && prefixSeparator < trimmed.Length - 1)
            {
                trimmed = trimmed[(prefixSeparator + 1)..];
            }

            return trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? trimmed;
        }

        /// <summary>
        /// Determines whether an element is a root Avalonia artifact element.
        /// </summary>
        /// <param name="element">The candidate element.</param>
        /// <param name="artifact">The artifact containing the element.</param>
        /// <returns><see langword="true" /> when the element is the primary artifact root; otherwise, <see langword="false" />.</returns>
        private static bool IsRootArtifactElement(XElement element, AvaloniaArtifactContext artifact)
        {
            // Root elements are already represented by the artifact node and should not also become child controls.
            return element.Parent is null && artifact.ArtifactKind is AvaloniaArtifactKind.Application or AvaloniaArtifactKind.Window or AvaloniaArtifactKind.UserControl or AvaloniaArtifactKind.Styles or AvaloniaArtifactKind.ResourceDictionary;
        }

        /// <summary>
        /// Determines whether an element is a project-local Avalonia component reference.
        /// </summary>
        /// <param name="element">The candidate element.</param>
        /// <returns><see langword="true" /> when the element uses a source namespace prefix; otherwise, <see langword="false" />.</returns>
        private static bool IsCustomComponentElement(XElement element)
        {
            // Avalonia namespace mappings commonly use `using:` values for project-local views.
            return element.Name.NamespaceName.StartsWith("using:", StringComparison.OrdinalIgnoreCase) || element.Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a AXAML element can represent a resource declaration.
        /// </summary>
        /// <param name="element">The candidate element.</param>
        /// <returns><see langword="true" /> when the element has a resource key; otherwise, <see langword="false" />.</returns>
        private static bool IsResourceContainer(XElement element)
        {
            // Any keyed object under Avalonia resources can be referenced by StaticResource or DynamicResource.
            return GetXamlAttribute(element, "Key") is not null;
        }

        /// <summary>
        /// Determines whether a AXAML attribute name and value represent an Avalonia event handler.
        /// </summary>
        /// <param name="attribute">The candidate attribute.</param>
        /// <returns><see langword="true" /> when the attribute looks like an event handler; otherwise, <see langword="false" />.</returns>
        private static bool IsRoutedEventAttribute(XAttribute attribute)
        {
            // The current slice recognizes common Avalonia event attribute names and requires method-like values to avoid classifying regular text properties as events.
            string name = attribute.Name.LocalName;
            return (name is "Click" or "Clicked" or "Loaded" or "Tapped" or "PointerPressed" or "SelectionChanged" or "TextChanged" or "KeyDown")
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
        private static AvaloniaProjectContext? FindNearestProject(IReadOnlyList<AvaloniaProjectContext> projects, string artifactPath)
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
            // Excluding output folders prevents duplicate generated AXAML artifacts from `bin`/`obj` from destabilizing graph output.
            string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return !normalized.Contains(string.Concat(Path.DirectorySeparatorChar, "bin", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains(string.Concat(Path.DirectorySeparatorChar, "obj", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains(string.Concat(Path.DirectorySeparatorChar, ".git", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the metadata artifact-kind value for an Avalonia artifact kind.
        /// </summary>
        /// <param name="artifactKind">The Avalonia artifact kind.</param>
        /// <returns>The UI artifact-kind metadata value.</returns>
        private static string GetArtifactKindMetadata(AvaloniaArtifactKind artifactKind)
        {
            // Metadata uses shared static UI extraction artifact names rather than Avalonia-specific graph node kinds.
            return artifactKind switch
            {
                AvaloniaArtifactKind.Application => "Application",
                AvaloniaArtifactKind.Window => "View",
                AvaloniaArtifactKind.UserControl => "Component",
                AvaloniaArtifactKind.Styles => "Style",
                AvaloniaArtifactKind.ResourceDictionary => "Resource",
                AvaloniaArtifactKind.Code => "CodeBehind",
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
        /// Creates a regex for AXAML x:Class attributes.
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
        /// Creates a regex for Avalonia code-behind event handler methods.
        /// </summary>
        /// <returns>A regex that captures handler method names.</returns>
        [GeneratedRegex("\\b(?<handler>[A-Za-z_][A-Za-z0-9_]*)\\s*\\([^)]*(?:EventArgs|RoutedEventArgs|PointerPressedEventArgs|SelectionChangedEventArgs|TextChangedEventArgs)", RegexOptions.CultureInvariant)]
        private static partial Regex EventHandlerRegex();

        /// <summary>
        /// Creates a regex for static ReactiveUI navigation calls.
        /// </summary>
        /// <returns>A regex that captures target view-model names.</returns>
        [GeneratedRegex("Navigate\\.Execute\\s*\\(\\s*new\\s+(?<target>[A-Za-z_][A-Za-z0-9_]*ViewModel)\\s*\\(", RegexOptions.CultureInvariant)]
        private static partial Regex StaticNavigateRegex();

        /// <summary>
        /// Creates a regex for non-static ReactiveUI navigation calls.
        /// </summary>
        /// <returns>A regex that captures runtime navigation calls.</returns>
        [GeneratedRegex("Navigate\\.Execute\\s*\\(", RegexOptions.CultureInvariant)]
        private static partial Regex DynamicNavigateRegex();

        /// <summary>
        /// Creates a regex for static view-locator switch arms.
        /// </summary>
        /// <returns>A regex that captures view-model and view type names.</returns>
        [GeneratedRegex("(?<viewModel>[A-Za-z_][A-Za-z0-9_]*ViewModel)\\s*=>\\s*new\\s+(?<view>[A-Za-z_][A-Za-z0-9_]*)\\s*\\(", RegexOptions.CultureInvariant)]
        private static partial Regex ViewLocatorStaticRegex();

        /// <summary>
        /// Creates a regex for generic ReactiveUI view base classes.
        /// </summary>
        /// <returns>A regex that captures view-model type names.</returns>
        [GeneratedRegex("Reactive(?:Window|UserControl)<(?<viewModel>[A-Za-z_][A-Za-z0-9_]*ViewModel)>", RegexOptions.CultureInvariant)]
        private static partial Regex ReactiveGenericRegex();

        /// <summary>
        /// Creates a regex for Path= binding syntax.
        /// </summary>
        /// <returns>A regex that captures binding paths.</returns>
        [GeneratedRegex("Path\\s*=\\s*(?<path>[A-Za-z_][A-Za-z0-9_\\.]*)", RegexOptions.CultureInvariant)]
        private static partial Regex BindingPathRegex();

        /// <summary>
        /// Creates a regex for direct `{Binding Name}`, `{CompiledBinding Name}`, or `{ReflectionBinding Name}` syntax.
        /// </summary>
        /// <returns>A regex that captures binding paths.</returns>
        [GeneratedRegex("\\{(?:CompiledBinding|ReflectionBinding|Binding)\\s+(?<path>[A-Za-z_][A-Za-z0-9_\\.]*)", RegexOptions.CultureInvariant)]
        private static partial Regex DirectBindingRegex();

        /// <summary>
        /// Creates a regex for method-like AXAML handler values.
        /// </summary>
        /// <returns>A regex that validates simple handler names.</returns>
        [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
        private static partial Regex MethodNameRegex();

        /// <summary>
        /// Describes one discovered Avalonia-capable project.
        /// </summary>
        /// <param name="AbsoluteProjectPath">The absolute project path used for artifact ownership checks.</param>
        /// <param name="RelativeProjectPath">The repository-relative project path used for stable keys.</param>
        /// <param name="ProjectName">The display name of the project.</param>
        /// <param name="TargetFrameworks">The target framework values read from project metadata.</param>
        /// <param name="Language">The project source language.</param>
        /// <param name="PackageIdentities">The package identities declared by the project.</param>
        /// <param name="UsesReactiveUi">Whether project/package/source metadata indicates Avalonia ReactiveUI usage.</param>
        private sealed record AvaloniaProjectContext(string AbsoluteProjectPath, string RelativeProjectPath, string ProjectName, IReadOnlyList<string> TargetFrameworks, string Language, IReadOnlyList<string> PackageIdentities, bool UsesReactiveUi);

        /// <summary>
        /// Describes normalized project metadata read from a project file.
        /// </summary>
        /// <param name="RelativeProjectPath">The repository-relative project path.</param>
        /// <param name="ProjectName">The project display name.</param>
        /// <param name="TargetFrameworks">The target framework values or Unknown.</param>
        /// <param name="Language">The project source language.</param>
        /// <param name="PackageIdentities">The package identities declared by the project.</param>
        /// <param name="UsesReactiveUi">Whether project metadata indicates ReactiveUI usage.</param>
        /// <param name="IsAvaloniaCandidate">Whether the project contains Avalonia evidence.</param>
        private sealed record ProjectMetadata(string RelativeProjectPath, string ProjectName, IReadOnlyList<string> TargetFrameworks, string Language, IReadOnlyList<string> PackageIdentities, bool UsesReactiveUi, bool IsAvaloniaCandidate);

        /// <summary>
        /// Describes one discovered Avalonia artifact and its owning project.
        /// </summary>
        /// <param name="Project">The project that owns the artifact.</param>
        /// <param name="AbsolutePath">The absolute artifact path used for file reads.</param>
        /// <param name="RelativePath">The repository-relative artifact path used for evidence and stable keys.</param>
        /// <param name="TypeName">The source type name associated with the artifact.</param>
        /// <param name="ArtifactKind">The coarse Avalonia artifact classification.</param>
        private sealed record AvaloniaArtifactContext(AvaloniaProjectContext Project, string AbsolutePath, string RelativePath, string TypeName, AvaloniaArtifactKind ArtifactKind);

        /// <summary>
        /// Describes repository-wide Avalonia context used during per-artifact analysis.
        /// </summary>
        /// <param name="SourceByPath">Source content keyed by repository-relative path.</param>
        /// <param name="ViewModelTypeNames">Repository-local view-model type names.</param>
        /// <param name="EventsByType">Event handler observations keyed by owner type.</param>
        /// <param name="ServiceUsagesByType">Service usages keyed by owner type.</param>
        /// <param name="DataAccessUsagesByType">Data-access usages keyed by owner type.</param>
        /// <param name="NavigationUsagesByType">Navigation usages keyed by owner type.</param>
        /// <param name="ViewLocatorsByProject">View-locator observations keyed by project path.</param>
        /// <param name="ReactiveViewModelsByType">ReactiveUI view-model observations keyed by owner type.</param>
        private sealed record AvaloniaRepositoryContext(IReadOnlyDictionary<string, string> SourceByPath, IReadOnlySet<string> ViewModelTypeNames, IReadOnlyDictionary<string, IReadOnlyList<EventUsage>> EventsByType, IReadOnlyDictionary<string, IReadOnlyList<ServiceUsage>> ServiceUsagesByType, IReadOnlyDictionary<string, IReadOnlyList<DataAccessUsage>> DataAccessUsagesByType, IReadOnlyDictionary<string, IReadOnlyList<NavigationUsage>> NavigationUsagesByType, IReadOnlyDictionary<string, IReadOnlyList<ViewLocatorUsage>> ViewLocatorsByProject, IReadOnlyDictionary<string, IReadOnlyList<ViewModelUsage>> ReactiveViewModelsByType);

        /// <summary>
        /// Describes one source line with its original one-based line number.
        /// </summary>
        /// <param name="LineNumber">The one-based line number.</param>
        /// <param name="Text">The source line text.</param>
        private sealed record SourceLine(int LineNumber, string Text);

        /// <summary>
        /// Describes an Avalonia resource, style, or style-include observation.
        /// </summary>
        /// <param name="Key">The resource, style selector, or style include key.</param>
        /// <param name="ArtifactKind">The UI artifact-kind metadata value.</param>
        /// <param name="DetectionMode">The detection mode that produced the observation.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        /// <param name="IsUnknown">Whether the resource target is runtime-dependent.</param>
        /// <param name="UnknownReason">The unknown reason when <paramref name="IsUnknown" /> is true.</param>
        private sealed record ResourceUsage(string Key, string ArtifactKind, string DetectionMode, int LineNumber, string SourceText, bool IsUnknown, string? UnknownReason);

        /// <summary>
        /// Describes an Avalonia control observation.
        /// </summary>
        /// <param name="ControlName">The control name or type when unnamed.</param>
        /// <param name="ControlType">The Avalonia control type.</param>
        /// <param name="IsCustomComponent">Whether the control is a project-local component reference.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record ControlUsage(string ControlName, string ControlType, bool IsCustomComponent, int LineNumber, string SourceText);

        /// <summary>
        /// Describes an Avalonia binding observation.
        /// </summary>
        /// <param name="PropertyName">The AXAML property being bound.</param>
        /// <param name="BindingPath">The binding path visible in markup.</param>
        /// <param name="IsUnknown">Whether the binding path could not be resolved statically.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record BindingUsage(string PropertyName, string BindingPath, bool IsUnknown, int LineNumber, string SourceText);

        /// <summary>
        /// Describes an Avalonia command binding observation.
        /// </summary>
        /// <param name="CommandName">The command property or expression name.</param>
        /// <param name="IsUnknown">Whether the command could not be resolved statically.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record CommandUsage(string CommandName, bool IsUnknown, int LineNumber, string SourceText);

        /// <summary>
        /// Describes an Avalonia event observation.
        /// </summary>
        /// <param name="EventName">The event name.</param>
        /// <param name="HandlerName">The handler method name.</param>
        /// <param name="DetectionMode">The detection mode that produced the observation.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record EventUsage(string EventName, string HandlerName, string DetectionMode, int LineNumber, string SourceText);

        /// <summary>
        /// Describes an Avalonia navigation observation.
        /// </summary>
        /// <param name="Target">The static navigation target or runtime expression.</param>
        /// <param name="IsUnknown">Whether the target is computed from runtime state.</param>
        /// <param name="DetectionMode">The detection mode that produced the observation.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record NavigationUsage(string Target, bool IsUnknown, string DetectionMode, int LineNumber, string SourceText);

        /// <summary>
        /// Describes an Avalonia view-locator observation.
        /// </summary>
        /// <param name="ViewModelType">The view-model type matched by the locator.</param>
        /// <param name="ViewType">The view type created by the locator.</param>
        /// <param name="IsUnknown">Whether the mapping is runtime-dependent.</param>
        /// <param name="DetectionMode">The detection mode that produced the observation.</param>
        /// <param name="SourcePath">The repository-relative source path that contains the locator.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record ViewLocatorUsage(string ViewModelType, string ViewType, bool IsUnknown, string DetectionMode, string SourcePath, int LineNumber, string SourceText);

        /// <summary>
        /// Describes an Avalonia view-model correlation observation.
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
        /// Describes an Avalonia runtime-dependent unknown observation.
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
        /// Describes the coarse category of an Avalonia artifact.
        /// </summary>
        private enum AvaloniaArtifactKind
        {
            /// <summary>
            /// An Avalonia application definition AXAML file.
            /// </summary>
            Application,

            /// <summary>
            /// An Avalonia window AXAML file.
            /// </summary>
            Window,

            /// <summary>
            /// An Avalonia user-control AXAML file.
            /// </summary>
            UserControl,

            /// <summary>
            /// An Avalonia styles AXAML file.
            /// </summary>
            Styles,

            /// <summary>
            /// An Avalonia resource dictionary AXAML file.
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
