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

namespace Archon.Extractors.WinUI
{
    /// <summary>
    /// Extracts WP011 WinUI XAML and packaging facts from project files, manifests, XAML markup, and code-behind source into shared graph contracts.
    /// </summary>
    /// <remarks>
    /// The extractor performs deterministic repository-file analysis only. It does not compile WinUI projects, load XAML, instantiate controls, start dispatchers, evaluate resources, connect to databases, or write directly to persistence.
    /// </remarks>
    public sealed partial class WinUiXamlExtractor
    {
        /// <summary>
        /// Extracts WinUI application, window, page, user-control, resource, style, binding, command, routed-event, navigation, packaging, view-model, service, data-access, evidence, warning, and unknown facts.
        /// </summary>
        /// <param name="request">The repository-scoped WinUI extraction request.</param>
        /// <param name="cancellationToken">The cancellation token that stops file discovery and artifact analysis.</param>
        /// <returns>A result containing the graph-ready snapshot emitted by this extractor.</returns>
        public async Task<WinUiXamlExtractionResult> ExtractAsync(WinUiXamlExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // Extraction is staged so discovery, source-context indexing, and graph projection remain deterministic and can produce useful partial output.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            ArchitectureSnapshotAccumulator accumulator = new();
            IReadOnlyList<WinUiProjectContext> projects = DiscoverProjects(request.RepositoryRootDirectory);
            IReadOnlyList<WinUiArtifactContext> artifacts = DiscoverArtifacts(request.RepositoryRootDirectory, projects);
            WinUiRepositoryContext repositoryContext = await BuildRepositoryContextAsync(projects, artifacts, cancellationToken).ConfigureAwait(false);

            foreach (WinUiProjectContext project in projects.Where(project => artifacts.Any(artifact => StringComparer.Ordinal.Equals(artifact.Project.RelativeProjectPath, project.RelativeProjectPath))))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateProjectAndApplication(request, accumulator, project, artifacts, repositoryContext);
            }

            foreach (WinUiArtifactContext artifact in artifacts.Where(artifact => artifact.ArtifactKind is WinUiArtifactKind.Application or WinUiArtifactKind.Window or WinUiArtifactKind.Page or WinUiArtifactKind.UserControl or WinUiArtifactKind.ResourceDictionary).OrderBy(artifact => artifact.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeArtifact(request, accumulator, repositoryContext, artifact);
            }

            return new WinUiXamlExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Discovers WinUI-capable projects from project metadata, Windows App SDK package references, XAML files, and source symbols.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <returns>Project contexts ordered by repository-relative path.</returns>
        private static IReadOnlyList<WinUiProjectContext> DiscoverProjects(string repositoryRootDirectory)
        {
            // Project discovery reads static project text only and avoids MSBuild evaluation so extraction remains safe on machines without desktop workloads.
            if (!Directory.Exists(repositoryRootDirectory))
            {
                return [];
            }

            List<WinUiProjectContext> projects = [];
            IEnumerable<string> projectPaths = Directory.EnumerateFiles(repositoryRootDirectory, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(repositoryRootDirectory, "*.vbproj", SearchOption.AllDirectories))
                .Where(IsRepositorySourcePath)
                .Order(StringComparer.OrdinalIgnoreCase);

            foreach (string projectPath in projectPaths)
            {
                ProjectMetadata metadata = ReadProjectMetadata(repositoryRootDirectory, projectPath);
                if (!metadata.IsWinUiCandidate)
                {
                    continue;
                }

                projects.Add(new WinUiProjectContext(projectPath, metadata.RelativeProjectPath, metadata.ProjectName, metadata.TargetFramework, metadata.Language, metadata.StartupObject, metadata.PackageType, metadata.ApplicationManifest, metadata.PackageIdentities));
            }

            return projects;
        }

        /// <summary>
        /// Discovers XAML, source, and packaging artifacts that belong to discovered WinUI projects.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="projects">The WinUI project contexts that can own artifacts.</param>
        /// <returns>Artifact contexts ordered by repository-relative path.</returns>
        private static IReadOnlyList<WinUiArtifactContext> DiscoverArtifacts(string repositoryRootDirectory, IReadOnlyList<WinUiProjectContext> projects)
        {
            // Discovery includes XAML, code-behind, and manifest files because WinUI UI structure spans declarative markup, startup source, and packaging metadata.
            if (!Directory.Exists(repositoryRootDirectory) || projects.Count == 0)
            {
                return [];
            }

            List<WinUiArtifactContext> artifacts = [];
            IEnumerable<string> artifactPaths = Directory.EnumerateFiles(repositoryRootDirectory, "*.xaml", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(repositoryRootDirectory, "*.cs", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(repositoryRootDirectory, "*.vb", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(repositoryRootDirectory, "*.appxmanifest", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(repositoryRootDirectory, "app.manifest", SearchOption.AllDirectories))
                .Where(IsRepositorySourcePath)
                .Order(StringComparer.OrdinalIgnoreCase);

            foreach (string artifactPath in artifactPaths)
            {
                WinUiProjectContext? project = FindNearestProject(projects, artifactPath);
                if (project is null)
                {
                    continue;
                }

                string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRootDirectory, artifactPath);
                string content = File.ReadAllText(artifactPath);
                WinUiArtifactKind artifactKind = ClassifyArtifact(relativePath, content);
                string typeName = ExtractPrimaryTypeName(relativePath, content, artifactKind);
                artifacts.Add(new WinUiArtifactContext(project, artifactPath, relativePath, typeName, artifactKind));
            }

            return artifacts;
        }

        /// <summary>
        /// Builds repository-wide WinUI context used to correlate XAML, manifests, code-behind, view models, services, and data-access usage.
        /// </summary>
        /// <param name="projects">The discovered WinUI projects.</param>
        /// <param name="artifacts">The discovered WinUI artifacts.</param>
        /// <param name="cancellationToken">The cancellation token that stops source loading.</param>
        /// <returns>A repository context used while analyzing WinUI artifacts.</returns>
        private static async Task<WinUiRepositoryContext> BuildRepositoryContextAsync(IReadOnlyList<WinUiProjectContext> projects, IReadOnlyList<WinUiArtifactContext> artifacts, CancellationToken cancellationToken)
        {
            // Context indexes are built once so per-artifact graph projection can avoid repeated scans and stay deterministic.
            Dictionary<string, string> sourceByPath = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, WinUiArtifactContext> artifactByType = new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> sourcePathsByType = new(StringComparer.Ordinal);
            HashSet<string> viewModelTypeNames = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<EventUsage>> eventsByType = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<ServiceUsage>> serviceUsagesByType = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<DataAccessUsage>> dataAccessUsagesByType = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<NavigationUsage>> navigationUsagesByType = new(StringComparer.Ordinal);
            Dictionary<string, PackagingMetadata> packagingByProject = new(StringComparer.OrdinalIgnoreCase);

            foreach (WinUiArtifactContext artifact in artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string content = await File.ReadAllTextAsync(artifact.AbsolutePath, cancellationToken).ConfigureAwait(false);
                sourceByPath[artifact.RelativePath] = content;

                if (artifact.ArtifactKind is WinUiArtifactKind.Window or WinUiArtifactKind.Page or WinUiArtifactKind.UserControl or WinUiArtifactKind.Application)
                {
                    artifactByType[artifact.TypeName] = artifact;
                    AddTypePath(sourcePathsByType, artifact.TypeName, artifact.RelativePath);
                }

                if (artifact.ArtifactKind is WinUiArtifactKind.PackageManifest)
                {
                    packagingByProject[artifact.Project.RelativeProjectPath] = ReadPackagingMetadata(artifact, content);
                }

                if (artifact.ArtifactKind is WinUiArtifactKind.Code)
                {
                    string? declaredTypeName = ExtractCodeTypeName(content);
                    if (!string.IsNullOrWhiteSpace(declaredTypeName))
                    {
                        AddTypePath(sourcePathsByType, declaredTypeName, artifact.RelativePath);
                    }

                    foreach (string viewModelTypeName in ExtractRepositoryViewModelTypeNames(content))
                    {
                        viewModelTypeNames.Add(viewModelTypeName);
                    }

                    string ownerTypeName = declaredTypeName ?? artifact.TypeName;
                    eventsByType[ownerTypeName] = ExtractCodeBehindEventHandlers(content);
                    serviceUsagesByType[ownerTypeName] = ExtractServiceUsages(content);
                    dataAccessUsagesByType[ownerTypeName] = ExtractDataAccessUsages(content, projects.FirstOrDefault(project => StringComparer.Ordinal.Equals(project.RelativeProjectPath, artifact.Project.RelativeProjectPath))?.PackageIdentities ?? []);
                    navigationUsagesByType[ownerTypeName] = ExtractCodeBehindNavigation(content);
                }
            }

            return new WinUiRepositoryContext(sourceByPath, artifactByType, sourcePathsByType, viewModelTypeNames, eventsByType, serviceUsagesByType, dataAccessUsagesByType, navigationUsagesByType, packagingByProject);
        }

        /// <summary>
        /// Adds project, UI application, and packaging facts for one WinUI project.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="project">The project context being represented.</param>
        /// <param name="artifacts">The discovered WinUI artifacts used to resolve application definitions.</param>
        /// <param name="repositoryContext">The repository context that supplies source content, startup evidence, and packaging metadata.</param>
        private static void AccumulateProjectAndApplication(WinUiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinUiProjectContext project, IReadOnlyList<WinUiArtifactContext> artifacts, WinUiRepositoryContext repositoryContext)
        {
            // Project and application facts give all WinUI UI nodes stable ownership when this extractor runs independently from project inventory.
            StableKey projectStableKey = StableKeyGenerator.ForProject(project.RelativeProjectPath);
            EvidenceRecord projectEvidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(project.RelativeProjectPath, 1, 1, project.ProjectName), "WinUI", "Application", "ProjectMetadata", Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(projectEvidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, projectStableKey, NodeKind.Project, project.ProjectName, project.RelativeProjectPath, project.RelativeProjectPath, project.Language, projectStableKey, null, Confidence.High, UnknownState.Known, projectEvidence.StableKey, new Dictionary<string, object?>
            {
                ["detectionMode"] = "ProjectMetadata",
                ["language"] = project.Language,
                ["packageIdentity"] = string.Join(",", project.PackageIdentities),
                ["platformHead"] = "Windows",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = project.RelativeProjectPath,
                ["targetFramework"] = project.TargetFramework,
                ["uiFramework"] = "WinUI"
            }));

            WinUiArtifactContext? applicationArtifact = artifacts.FirstOrDefault(artifact => StringComparer.Ordinal.Equals(artifact.Project.RelativeProjectPath, project.RelativeProjectPath) && artifact.ArtifactKind is WinUiArtifactKind.Application);
            WinUiArtifactContext? manifestArtifact = artifacts.FirstOrDefault(artifact => StringComparer.Ordinal.Equals(artifact.Project.RelativeProjectPath, project.RelativeProjectPath) && artifact.ArtifactKind is WinUiArtifactKind.PackageManifest);
            PackagingMetadata packagingMetadata = repositoryContext.PackagingByProject.TryGetValue(project.RelativeProjectPath, out PackagingMetadata? metadata) ? metadata : PackagingMetadata.Unknown(project.PackageType, project.ApplicationManifest);
            string startupIdentity = ResolveStartupIdentity(project, applicationArtifact, repositoryContext);
            bool isStartupUnknown = string.Equals(startupIdentity, "Unknown", StringComparison.Ordinal);
            bool isPackagingUnknown = packagingMetadata.IsUnknown;
            UnknownState unknownState = isStartupUnknown || isPackagingUnknown ? UnknownState.Unknown(isPackagingUnknown ? "WinUI packaging metadata is ambiguous or unavailable." : "WinUI startup window or startup object could not be resolved statically.") : UnknownState.Known;
            Confidence confidence = unknownState.HasUnknownData ? Confidence.Low : Confidence.High;
            string applicationSourcePath = applicationArtifact?.RelativePath ?? manifestArtifact?.RelativePath ?? project.RelativeProjectPath;
            StableKey applicationStableKey = UiStableKeyBuilder.Create("ui-application://", projectStableKey.Value, "WinUI", project.TargetFramework, startupIdentity, packagingMetadata.PackageIdentity);
            EvidenceRecord applicationEvidence = manifestArtifact is not null ? CreateEvidence(request, manifestArtifact, repositoryContext.SourceByPath[manifestArtifact.RelativePath], "Application", "PackageManifest", confidence, unknownState) : applicationArtifact is null ? projectEvidence : CreateEvidence(request, applicationArtifact, repositoryContext.SourceByPath[applicationArtifact.RelativePath], "Application", "XamlApplication", confidence, unknownState);
            if (!ReferenceEquals(applicationEvidence, projectEvidence))
            {
                accumulator.AddEvidence(applicationEvidence);
            }

            accumulator.AddNode(CreateNode(request.SnapshotStableKey, applicationStableKey, NodeKind.UiApplication, project.ProjectName, applicationSourcePath, project.ProjectName, project.Language, projectStableKey, projectStableKey, confidence, unknownState, applicationEvidence.StableKey, new Dictionary<string, object?>
            {
                ["confidenceReason"] = unknownState.HasUnknownData ? unknownState.UnknownReason : "Project, startup, and package metadata identified the WinUI application.",
                ["detectionMode"] = manifestArtifact is not null ? "PackageManifest" : applicationArtifact is null ? "ProjectMetadata" : "XamlApplication",
                ["hostingModel"] = "Desktop",
                ["language"] = project.Language,
                ["packageIdentity"] = packagingMetadata.PackageIdentity,
                ["packageDisplayName"] = packagingMetadata.DisplayName,
                ["packagePublisher"] = packagingMetadata.Publisher,
                ["packageType"] = packagingMetadata.PackageType,
                ["packageVersion"] = packagingMetadata.Version,
                ["platformHead"] = "Windows",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = applicationSourcePath,
                ["startupObject"] = startupIdentity,
                ["targetFramework"] = project.TargetFramework,
                ["uiArtifactKind"] = "Application",
                ["uiFramework"] = "WinUI"
            }));

            if (unknownState.HasUnknownData)
            {
                accumulator.AddWarning($"WinUI startup or packaging metadata for {project.RelativeProjectPath} could not be fully resolved statically: {unknownState.UnknownReason}");
            }
        }

        /// <summary>
        /// Analyzes one WinUI XAML artifact and contributes graph facts for supported markup and source patterns.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="repositoryContext">The repository context used for source and symbol correlation.</param>
        /// <param name="artifact">The WinUI XAML artifact being analyzed.</param>
        private static void AnalyzeArtifact(WinUiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinUiRepositoryContext repositoryContext, WinUiArtifactContext artifact)
        {
            // XAML parsing is best-effort; malformed artifacts become non-fatal warnings through the accumulator rather than aborting the entire UI slice.
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

            foreach (BindingUsage binding in ExtractBindings(document))
            {
                AccumulateBinding(request, accumulator, artifact, projectStableKey, ownerStableKey, binding);
            }

            foreach (CommandUsage command in ExtractCommands(document))
            {
                AccumulateCommand(request, accumulator, artifact, projectStableKey, ownerStableKey, command);
            }

            foreach (EventUsage routedEvent in ExtractMarkupEvents(document).Concat(repositoryContext.EventsByType.TryGetValue(artifact.TypeName, out IReadOnlyList<EventUsage>? codeEvents) ? codeEvents : []))
            {
                AccumulateEvent(request, accumulator, artifact, projectStableKey, ownerStableKey, routedEvent);
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
        /// Creates the primary graph node for a WinUI XAML artifact.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives the graph node.</param>
        /// <param name="artifact">The WinUI artifact being represented.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="evidenceStableKey">The evidence stable key for the artifact.</param>
        /// <returns>The stable key of the created artifact node.</returns>
        private static StableKey CreateArtifactNode(WinUiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinUiArtifactContext artifact, StableKey projectStableKey, StableKey evidenceStableKey)
        {
            // Framework-specific subtypes remain metadata values; the node kind uses the shared WP011 UI vocabulary.
            NodeKind nodeKind = artifact.ArtifactKind switch
            {
                WinUiArtifactKind.Window => NodeKind.UiView,
                WinUiArtifactKind.Application => NodeKind.UiApplication,
                WinUiArtifactKind.Page => NodeKind.UiPage,
                WinUiArtifactKind.UserControl => NodeKind.UiComponent,
                WinUiArtifactKind.ResourceDictionary => NodeKind.UiResource,
                _ => NodeKind.UiComponent
            };
            StableKey nodeStableKey = CreateArtifactStableKey(artifact, projectStableKey);
            string artifactKind = GetArtifactKindMetadata(artifact.ArtifactKind);
            Dictionary<string, object?> metadata = new(StringComparer.Ordinal)
            {
                ["detectionMode"] = "XamlMarkup",
                ["language"] = "XAML",
                ["platformHead"] = "Windows",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["targetFramework"] = artifact.Project.TargetFramework,
                ["typeName"] = artifact.TypeName,
                ["uiArtifactKind"] = artifactKind,
                ["uiFramework"] = "WinUI"
            };
            if (artifact.ArtifactKind is WinUiArtifactKind.Window)
            {
                metadata["windowName"] = artifact.TypeName;
            }
            else if (artifact.ArtifactKind is WinUiArtifactKind.Page)
            {
                metadata["pageName"] = artifact.TypeName;
            }
            else if (artifact.ArtifactKind is WinUiArtifactKind.ResourceDictionary)
            {
                metadata["resourceKey"] = Path.GetFileName(artifact.RelativePath);
            }

            accumulator.AddNode(CreateNode(request.SnapshotStableKey, nodeStableKey, nodeKind, artifact.TypeName, artifact.RelativePath, artifact.TypeName, "XAML", projectStableKey, projectStableKey, Confidence.High, UnknownState.Known, evidenceStableKey, metadata));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DeclaresComponent, projectStableKey, nodeStableKey, evidenceStableKey, artifact.RelativePath, artifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["detectionMode"] = "XamlMarkup",
                ["uiArtifactKind"] = artifactKind,
                ["uiFramework"] = "WinUI"
            }));
            return nodeStableKey;
        }

        /// <summary>
        /// Adds graph facts for a WinUI resource, style, or template observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the resource.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact using or declaring the resource.</param>
        /// <param name="resource">The resource observation.</param>
        private static void AccumulateResource(WinUiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinUiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, ResourceUsage resource)
        {
            // Resources are normalized into resource or style nodes while preserving style/template subtype metadata for queries.
            UnknownState unknownState = resource.IsUnknown ? UnknownState.Unknown(resource.UnknownReason!) : UnknownState.Known;
            Confidence confidence = resource.IsUnknown ? Confidence.Low : Confidence.High;
            NodeKind nodeKind = resource.ArtifactKind is "Style" or "Template" ? NodeKind.UiStyle : NodeKind.UiResource;
            StableKey resourceStableKey = UiStableKeyBuilder.Create("ui-resource://", projectStableKey.Value, "WinUI", artifact.RelativePath, resource.ArtifactKind, resource.Key, resource.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, resource.SourceText, resource.ArtifactKind, resource.DetectionMode, confidence, unknownState, resource.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, resourceStableKey, nodeKind, resource.Key, artifact.RelativePath, resource.Key, "XAML", projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["confidenceReason"] = resource.IsUnknown ? resource.UnknownReason : "Static WinUI resource evidence.",
                ["detectionMode"] = resource.DetectionMode,
                ["platformHead"] = "Windows",
                ["projectKey"] = projectStableKey.Value,
                ["resourceKey"] = resource.Key,
                ["sourcePath"] = artifact.RelativePath,
                ["styleKey"] = nodeKind == NodeKind.UiStyle ? resource.Key : null,
                ["uiArtifactKind"] = resource.ArtifactKind,
                ["uiFramework"] = "WinUI"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, resource.ArtifactKind is "Style" or "Template" ? EdgeKind.UsesStyle : EdgeKind.UsesUiResource, ownerStableKey, resourceStableKey, evidence.StableKey, resource.Key, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["detectionMode"] = resource.DetectionMode,
                ["resourceKey"] = resource.Key,
                ["styleKey"] = resource.ArtifactKind is "Style" or "Template" ? resource.Key : null,
                ["uiArtifactKind"] = resource.ArtifactKind,
                ["uiFramework"] = "WinUI"
            }));

            if (resource.IsUnknown)
            {
                accumulator.AddWarning($"WinUI {resource.ArtifactKind.ToLowerInvariant()} in {artifact.RelativePath} has unresolved dynamic resource evidence: {resource.Key}.");
            }
        }

        /// <summary>
        /// Adds graph facts for a WinUI control or nested component observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the control.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact using the control.</param>
        /// <param name="control">The control observation.</param>
        private static void AccumulateControl(WinUiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinUiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, ControlUsage control)
        {
            // Named controls are represented as UiControl nodes, while project-local WinUI controls are also queryable through component-style relationships.
            StableKey controlStableKey = UiStableKeyBuilder.Create("ui-control://", projectStableKey.Value, "WinUI", artifact.RelativePath, control.ControlType, control.ControlName, control.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, control.SourceText, "Control", "XamlControl", Confidence.High, UnknownState.Known, control.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, controlStableKey, NodeKind.UiControl, control.ControlName, artifact.RelativePath, control.ControlName, "XAML", projectStableKey, ownerStableKey, Confidence.High, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["controlName"] = control.ControlName,
                ["controlType"] = control.ControlType,
                ["detectionMode"] = "XamlControl",
                ["platformHead"] = "Windows",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Control",
                ["uiFramework"] = "WinUI"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, control.IsCustomComponent ? EdgeKind.UsesComponent : EdgeKind.UsesControl, ownerStableKey, controlStableKey, evidence.StableKey, control.ControlName, artifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["controlName"] = control.ControlName,
                ["controlType"] = control.ControlType,
                ["detectionMode"] = "XamlControl",
                ["uiFramework"] = "WinUI"
            }));
        }

        /// <summary>
        /// Adds graph facts for a WinUI binding observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the binding.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the bound artifact.</param>
        /// <param name="binding">The binding observation.</param>
        private static void AccumulateBinding(WinUiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinUiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, BindingUsage binding)
        {
            // Unqualified `{Binding}` expressions are explicit unknowns because their runtime target depends on DataContext shape.
            UnknownState unknownState = binding.IsUnknown ? UnknownState.Unknown("WinUI binding path could not be resolved statically.") : UnknownState.Known;
            Confidence confidence = binding.IsUnknown ? Confidence.Low : Confidence.High;
            StableKey bindingStableKey = UiStableKeyBuilder.Create("ui-binding://", projectStableKey.Value, "WinUI", artifact.RelativePath, binding.PropertyName, binding.BindingPath, binding.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, binding.SourceText, "Binding", "XamlBinding", confidence, unknownState, binding.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, bindingStableKey, NodeKind.Binding, binding.BindingPath, artifact.RelativePath, binding.BindingPath, "XAML", projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["bindingPath"] = binding.BindingPath,
                ["confidenceReason"] = binding.IsUnknown ? "Binding expression did not include a static path." : "Binding expression included a static path.",
                ["detectionMode"] = "XamlBinding",
                ["platformHead"] = "Windows",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Binding",
                ["uiFramework"] = "WinUI"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.BindsTo, ownerStableKey, bindingStableKey, evidence.StableKey, binding.BindingPath, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["bindingPath"] = binding.BindingPath,
                ["detectionMode"] = "XamlBinding",
                ["uiFramework"] = "WinUI"
            }));

            if (binding.IsUnknown)
            {
                accumulator.AddWarning($"WinUI unresolved binding path in {artifact.RelativePath} at line {binding.LineNumber.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        /// <summary>
        /// Adds graph facts for a WinUI command binding observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the command binding.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact using the command.</param>
        /// <param name="command">The command observation.</param>
        private static void AccumulateCommand(WinUiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinUiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, CommandUsage command)
        {
            // Command bindings are modeled separately from routed events because they usually target view-model command properties.
            StableKey commandStableKey = UiStableKeyBuilder.Create("ui-command://", projectStableKey.Value, "WinUI", artifact.RelativePath, command.CommandName, command.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, command.SourceText, "Command", "XamlCommand", Confidence.High, UnknownState.Known, command.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, commandStableKey, NodeKind.Command, command.CommandName, artifact.RelativePath, command.CommandName, "XAML", projectStableKey, ownerStableKey, Confidence.High, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["commandName"] = command.CommandName,
                ["detectionMode"] = "XamlCommand",
                ["platformHead"] = "Windows",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Command",
                ["uiFramework"] = "WinUI"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesCommand, ownerStableKey, commandStableKey, evidence.StableKey, command.CommandName, artifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["commandName"] = command.CommandName,
                ["detectionMode"] = "XamlCommand",
                ["uiFramework"] = "WinUI"
            }));
        }

        /// <summary>
        /// Adds graph facts for a WinUI routed event handler observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains the routed event.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact handling the event.</param>
        /// <param name="eventUsage">The event observation.</param>
        private static void AccumulateEvent(WinUiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinUiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, EventUsage eventUsage)
        {
            // Routed events are represented by command nodes so handlers can be traversed uniformly with command facts.
            StableKey commandStableKey = UiStableKeyBuilder.Create("ui-command://", projectStableKey.Value, "WinUI", artifact.RelativePath, eventUsage.HandlerName, eventUsage.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, eventUsage.SourceText, "Command", eventUsage.DetectionMode, Confidence.High, UnknownState.Known, eventUsage.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, commandStableKey, NodeKind.Command, eventUsage.HandlerName, artifact.RelativePath, eventUsage.HandlerName, "XAML", projectStableKey, ownerStableKey, Confidence.High, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["commandName"] = eventUsage.HandlerName,
                ["detectionMode"] = eventUsage.DetectionMode,
                ["eventName"] = eventUsage.EventName,
                ["platformHead"] = "Windows",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Command",
                ["uiFramework"] = "WinUI"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.HandlesUiEvent, ownerStableKey, commandStableKey, evidence.StableKey, eventUsage.EventName, artifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["commandName"] = eventUsage.HandlerName,
                ["detectionMode"] = eventUsage.DetectionMode,
                ["eventName"] = eventUsage.EventName,
                ["uiFramework"] = "WinUI"
            }));
        }

        /// <summary>
        /// Adds graph facts for a WinUI navigation observation.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact that contains navigation evidence.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the navigating artifact.</param>
        /// <param name="navigation">The navigation observation.</param>
        private static void AccumulateNavigation(WinUiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinUiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, NavigationUsage navigation)
        {
            // Static navigation targets become page nodes; computed navigation is recorded as an explicit unknown instead of a guessed route.
            UnknownState unknownState = navigation.IsUnknown ? UnknownState.Unknown("WinUI navigation target is computed from runtime state.") : UnknownState.Known;
            Confidence confidence = navigation.IsUnknown ? Confidence.Low : Confidence.High;
            StableKey targetStableKey = UiStableKeyBuilder.Create("ui-page://", projectStableKey.Value, "WinUI", artifact.RelativePath, navigation.Target, navigation.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, navigation.SourceText, "Page", navigation.DetectionMode, confidence, unknownState, navigation.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, targetStableKey, NodeKind.UiPage, navigation.Target, artifact.RelativePath, navigation.Target, "XAML", projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["confidenceReason"] = navigation.IsUnknown ? "Navigation target uses a runtime expression." : "Navigation target is statically visible.",
                ["detectionMode"] = navigation.DetectionMode,
                ["navigationTarget"] = navigation.Target,
                ["platformHead"] = "Windows",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Page",
                ["uiFramework"] = "WinUI"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.NavigatesTo, ownerStableKey, targetStableKey, evidence.StableKey, navigation.Target, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["detectionMode"] = navigation.DetectionMode,
                ["navigationTarget"] = navigation.Target,
                ["uiFramework"] = "WinUI"
            }));

            if (navigation.IsUnknown)
            {
                accumulator.AddWarning($"WinUI runtime navigation target in {artifact.RelativePath} at line {navigation.LineNumber.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        /// <summary>
        /// Adds graph facts for WinUI view-model evidence or convention-only unknowns.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="repositoryContext">The repository context containing known view-model types.</param>
        /// <param name="artifact">The artifact whose view model is being correlated.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the view using the view model.</param>
        /// <param name="root">The parsed XAML root element.</param>
        private static void AccumulateViewModel(WinUiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinUiRepositoryContext repositoryContext, WinUiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, XElement root)
        {
            // Direct DataContext elements are high-confidence evidence; missing conventions are recorded as unknowns so consumers can distinguish absence from unresolved patterns.
            ViewModelUsage usage = ExtractViewModel(root, artifact, repositoryContext);
            UnknownState unknownState = usage.IsUnknown ? UnknownState.Unknown("WinUI view model is inferred by convention only and was not found in source.") : UnknownState.Known;
            Confidence confidence = usage.IsUnknown ? Confidence.Low : usage.Confidence;
            StableKey viewModelStableKey = UiStableKeyBuilder.Create("ui-viewmodel://", projectStableKey.Value, "WinUI", artifact.RelativePath, usage.ViewModelType, usage.LineNumber.ToString(CultureInfo.InvariantCulture));
            EvidenceRecord evidence = CreateEvidence(request, artifact, usage.SourceText, "ViewModel", usage.DetectionMode, confidence, unknownState, usage.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, viewModelStableKey, NodeKind.ViewModel, usage.ViewModelType, artifact.RelativePath, usage.ViewModelType, artifact.Project.Language, projectStableKey, ownerStableKey, confidence, unknownState, evidence.StableKey, new Dictionary<string, object?>
            {
                ["confidenceReason"] = usage.ConfidenceReason,
                ["detectionMode"] = usage.DetectionMode,
                ["platformHead"] = "Windows",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "ViewModel",
                ["uiFramework"] = "WinUI",
                ["viewModelType"] = usage.ViewModelType
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesViewModel, ownerStableKey, viewModelStableKey, evidence.StableKey, usage.ViewModelType, artifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["detectionMode"] = usage.DetectionMode,
                ["uiFramework"] = "WinUI",
                ["viewModelType"] = usage.ViewModelType
            }));

            if (usage.IsUnknown)
            {
                accumulator.AddWarning($"WinUI convention-only view model for {artifact.TypeName} in {artifact.RelativePath} could not be found in source.");
            }
        }

        /// <summary>
        /// Adds graph facts for a WinUI code-behind service usage.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact whose code-behind uses the service.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the UI artifact using the service.</param>
        /// <param name="serviceUsage">The service usage observation.</param>
        private static void AccumulateServiceUsage(WinUiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinUiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, ServiceUsage serviceUsage)
        {
            // Service usages are correlated from code-behind source and linked with DEPENDS_ON because semantic service registration may be emitted by earlier stages.
            StableKey serviceStableKey = UiStableKeyBuilder.Create("ui-service://", projectStableKey.Value, serviceUsage.TypeName);
            EvidenceRecord evidence = CreateEvidence(request, artifact, serviceUsage.SourceText, "ServiceUsage", "CodeBehind", Confidence.Medium, UnknownState.Known, serviceUsage.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, serviceStableKey, NodeKind.Type, serviceUsage.TypeName, serviceUsage.TypeName, serviceUsage.TypeName, artifact.Project.Language, projectStableKey, projectStableKey, Confidence.Medium, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["detectionMode"] = "CodeBehind",
                ["platformHead"] = "Windows",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["typeName"] = serviceUsage.TypeName,
                ["uiFramework"] = "WinUI"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DependsOn, ownerStableKey, serviceStableKey, evidence.StableKey, serviceUsage.TypeName, artifact.RelativePath, Confidence.Medium, UnknownState.Known, new Dictionary<string, object?>
            {
                ["detectionMode"] = "CodeBehind",
                ["typeName"] = serviceUsage.TypeName,
                ["uiFramework"] = "WinUI"
            }));
        }

        /// <summary>
        /// Adds graph facts for a WinUI code-behind data-access or external package usage.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The artifact whose code-behind uses the dependency.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the UI artifact using the dependency.</param>
        /// <param name="dataAccessUsage">The data-access usage observation.</param>
        private static void AccumulateDataAccessUsage(WinUiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinUiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, DataAccessUsage dataAccessUsage)
        {
            // Data-access usages are emitted as external-service facts so UI-to-data paths remain visible even before specialized data-access stages correlate exact contexts.
            StableKey dependencyStableKey = UiStableKeyBuilder.Create("ui-data-access://", projectStableKey.Value, dataAccessUsage.PackageIdentity);
            EvidenceRecord evidence = CreateEvidence(request, artifact, dataAccessUsage.SourceText, "DataAccess", "CodeBehind", Confidence.Medium, UnknownState.Known, dataAccessUsage.LineNumber);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, dependencyStableKey, NodeKind.ExternalService, dataAccessUsage.PackageIdentity, dataAccessUsage.PackageIdentity, dataAccessUsage.PackageIdentity, artifact.Project.Language, projectStableKey, projectStableKey, Confidence.Medium, UnknownState.Known, evidence.StableKey, new Dictionary<string, object?>
            {
                ["detectionMode"] = "CodeBehind",
                ["packageIdentity"] = dataAccessUsage.PackageIdentity,
                ["platformHead"] = "Windows",
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["uiFramework"] = "WinUI"
            }));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.CallsApi, ownerStableKey, dependencyStableKey, evidence.StableKey, dataAccessUsage.PackageIdentity, artifact.RelativePath, Confidence.Medium, UnknownState.Known, new Dictionary<string, object?>
            {
                ["detectionMode"] = "CodeBehind",
                ["packageIdentity"] = dataAccessUsage.PackageIdentity,
                ["uiFramework"] = "WinUI"
            }));
        }

        /// <summary>
        /// Adds explicit unknown facts for dynamic resource, template, binding, and navigation patterns that cannot be resolved statically.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifact">The WinUI artifact that contains runtime-dependent markup.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="ownerStableKey">The stable key of the artifact containing the unknowns.</param>
        /// <param name="root">The parsed XAML root element.</param>
        private static void AccumulateDynamicUnknowns(WinUiXamlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinUiArtifactContext artifact, StableKey projectStableKey, StableKey ownerStableKey, XElement root)
        {
            // Dynamic unknowns are handled after normal extraction so unsupported runtime decisions are captured without blocking known static facts.
            foreach (UnknownUsage unknown in ExtractUnknowns(root))
            {
                UnknownState unknownState = UnknownState.Unknown(unknown.UnknownReason);
                StableKey unknownStableKey = UiStableKeyBuilder.Create("ui-unknown://", projectStableKey.Value, "WinUI", artifact.RelativePath, unknown.Category, unknown.LineNumber.ToString(CultureInfo.InvariantCulture));
                EvidenceRecord evidence = CreateEvidence(request, artifact, unknown.SourceText, unknown.ArtifactKind, unknown.DetectionMode, Confidence.Low, unknownState, unknown.LineNumber);
                accumulator.AddEvidence(evidence);
                accumulator.AddNode(CreateNode(request.SnapshotStableKey, unknownStableKey, unknown.NodeKind, unknown.DisplayName, artifact.RelativePath, unknown.DisplayName, "XAML", projectStableKey, ownerStableKey, Confidence.Low, unknownState, evidence.StableKey, new Dictionary<string, object?>
                {
                    ["confidenceReason"] = unknown.UnknownReason,
                    ["detectionMode"] = unknown.DetectionMode,
                    ["platformHead"] = "Windows",
                    ["projectKey"] = projectStableKey.Value,
                    ["sourcePath"] = artifact.RelativePath,
                    ["uiArtifactKind"] = unknown.ArtifactKind,
                    ["uiFramework"] = "WinUI"
                }));
                accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, GetUnknownEdgeKind(unknown.NodeKind), ownerStableKey, unknownStableKey, evidence.StableKey, unknown.Category, artifact.RelativePath, Confidence.Low, unknownState, new Dictionary<string, object?>
                {
                    ["detectionMode"] = unknown.DetectionMode,
                    ["uiFramework"] = "WinUI"
                }));
                accumulator.AddWarning($"WinUI {unknown.Category} in {artifact.RelativePath} requires runtime information: {unknown.UnknownReason}");
            }
        }

        /// <summary>
        /// Reads WinUI-relevant metadata from a C# or VB.NET project file.
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
                string targetFramework = ReadFirstElementValue(document, "TargetFramework") ?? ReadFirstElementValue(document, "TargetFrameworks")?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "Unknown";
                string? startupObject = ReadFirstElementValue(document, "StartupObject");
                string packageType = ReadFirstElementValue(document, "WindowsPackageType") ?? "Unknown";
                string? applicationManifest = ReadFirstElementValue(document, "ApplicationManifest");
                string[] packageIdentities = document.Descendants().Where(element => string.Equals(element.Name.LocalName, "PackageReference", StringComparison.Ordinal)).Select(element => element.Attribute("Include")?.Value).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                bool isWinUiCandidate = string.Equals(ReadFirstElementValue(document, "UseWinUI"), "true", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Microsoft.UI.Xaml", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Microsoft.WindowsAppSDK", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("WindowsAppSDK", StringComparison.OrdinalIgnoreCase)
                    || text.Contains(".xaml", StringComparison.OrdinalIgnoreCase)
                    || targetFramework.Contains("windows10", StringComparison.OrdinalIgnoreCase)
                    || startupObject is not null;
                return new ProjectMetadata(relativeProjectPath, projectName, string.IsNullOrWhiteSpace(targetFramework) ? "Unknown" : targetFramework.Trim(), language, startupObject?.Trim(), packageType.Trim(), applicationManifest?.Trim(), packageIdentities, isWinUiCandidate);
            }
            catch (Exception) when (IsXmlReadException())
            {
                // Malformed project files cannot be evaluated safely; source evidence may still identify WinUI through future enhancement, so this project is skipped for now.
                return new ProjectMetadata(relativeProjectPath, projectName, "Unknown", language, null, "Unknown", null, [], false);
            }
        }

        /// <summary>
        /// Reads safe package identity metadata from a WinUI package manifest.
        /// </summary>
        /// <param name="artifact">The manifest artifact being parsed.</param>
        /// <param name="content">The manifest XML content.</param>
        /// <returns>Safe normalized packaging metadata.</returns>
        private static PackagingMetadata ReadPackagingMetadata(WinUiArtifactContext artifact, string content)
        {
            // Packaging metadata is limited to identity and display fields that are safe and useful for architecture queries.
            try
            {
                XDocument document = XDocument.Parse(content, LoadOptions.None);
                XElement? identity = document.Descendants().FirstOrDefault(element => string.Equals(element.Name.LocalName, "Identity", StringComparison.Ordinal));
                XElement? properties = document.Descendants().FirstOrDefault(element => string.Equals(element.Name.LocalName, "Properties", StringComparison.Ordinal));
                string? packageIdentity = identity?.Attribute("Name")?.Value?.Trim();
                string? publisher = identity?.Attribute("Publisher")?.Value?.Trim();
                string? version = identity?.Attribute("Version")?.Value?.Trim();
                string? displayName = properties?.Elements().FirstOrDefault(element => string.Equals(element.Name.LocalName, "DisplayName", StringComparison.Ordinal))?.Value.Trim();
                bool isUnknown = string.IsNullOrWhiteSpace(packageIdentity);
                return new PackagingMetadata(isUnknown ? "Unknown" : packageIdentity!, string.IsNullOrWhiteSpace(displayName) ? null : displayName, string.IsNullOrWhiteSpace(publisher) ? null : publisher, string.IsNullOrWhiteSpace(version) ? null : version, artifact.Project.PackageType, artifact.RelativePath, isUnknown);
            }
            catch (Exception) when (IsXmlReadException())
            {
                // Malformed manifests should not fail UI extraction; unknown packaging facts keep the gap queryable.
                return PackagingMetadata.Unknown(artifact.Project.PackageType, artifact.RelativePath);
            }
        }

        /// <summary>
        /// Classifies a repository artifact into a WinUI XAML/source/package category.
        /// </summary>
        /// <param name="relativePath">The repository-relative artifact path.</param>
        /// <param name="content">The artifact content.</param>
        /// <returns>The artifact kind used by WinUI extraction.</returns>
        private static WinUiArtifactKind ClassifyArtifact(string relativePath, string content)
        {
            // Classification relies on XAML root tags, source naming, and package manifest names because extractor execution must not load WinUI assemblies.
            if (relativePath.EndsWith(".appxmanifest", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith("app.manifest", StringComparison.OrdinalIgnoreCase))
            {
                return WinUiArtifactKind.PackageManifest;
            }

            if (relativePath.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith(".xaml.vb", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase))
            {
                return WinUiArtifactKind.Code;
            }

            string trimmed = content.TrimStart();
            if (trimmed.StartsWith("<Application", StringComparison.OrdinalIgnoreCase))
            {
                return WinUiArtifactKind.Application;
            }

            if (trimmed.StartsWith("<Window", StringComparison.OrdinalIgnoreCase))
            {
                return WinUiArtifactKind.Window;
            }

            if (trimmed.StartsWith("<Page", StringComparison.OrdinalIgnoreCase))
            {
                return WinUiArtifactKind.Page;
            }

            if (trimmed.StartsWith("<UserControl", StringComparison.OrdinalIgnoreCase))
            {
                return WinUiArtifactKind.UserControl;
            }

            if (trimmed.StartsWith("<ResourceDictionary", StringComparison.OrdinalIgnoreCase))
            {
                return WinUiArtifactKind.ResourceDictionary;
            }

            return WinUiArtifactKind.Other;
        }

        /// <summary>
        /// Extracts the primary type or artifact name from WinUI markup or source content.
        /// </summary>
        /// <param name="relativePath">The repository-relative artifact path.</param>
        /// <param name="content">The artifact content.</param>
        /// <param name="artifactKind">The artifact kind being named.</param>
        /// <returns>The primary artifact type name.</returns>
        private static string ExtractPrimaryTypeName(string relativePath, string content, WinUiArtifactKind artifactKind)
        {
            // XAML `x:Class` is authoritative; code artifacts use source declarations; manifests and dictionaries fall back to file names.
            if (artifactKind is WinUiArtifactKind.Code)
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
            catch (Exception exception) when (exception is System.Xml.XmlException or InvalidOperationException)
            {
                accumulator.AddWarning($"WinUI XAML artifact {relativePath} could not be parsed: {exception.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts resource, style, and template observations from a parsed WinUI XAML document.
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
        /// Extracts named controls and custom component usages from a parsed WinUI XAML document.
        /// </summary>
        /// <param name="document">The parsed XAML document.</param>
        /// <param name="artifact">The artifact that owns the document.</param>
        /// <returns>Control observations in document order.</returns>
        private static IReadOnlyList<ControlUsage> ExtractControls(XDocument document, WinUiArtifactContext artifact)
        {
            // A control is included when it has an explicit XAML name or when its XML namespace indicates a project-local custom component.
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
        /// Extracts binding expressions from attributes in a parsed WinUI XAML document.
        /// </summary>
        /// <param name="document">The parsed XAML document.</param>
        /// <returns>Binding observations in document order.</returns>
        private static IReadOnlyList<BindingUsage> ExtractBindings(XDocument document)
        {
            // WinUI binding markup extensions can contain `Path=Name`, direct `Binding Name`, x:Bind, or bare `{Binding}` expressions.
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
        /// Extracts command bindings from WinUI Command attributes.
        /// </summary>
        /// <param name="document">The parsed XAML document.</param>
        /// <returns>Command observations in document order.</returns>
        private static IReadOnlyList<CommandUsage> ExtractCommands(XDocument document)
        {
            // Command properties often point to view-model command paths and are represented as command nodes distinct from routed events.
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
        /// Extracts routed event attributes from WinUI XAML markup.
        /// </summary>
        /// <param name="document">The parsed XAML document.</param>
        /// <returns>Event observations in document order.</returns>
        private static IReadOnlyList<EventUsage> ExtractMarkupEvents(XDocument document)
        {
            // Routed events are inferred from known WinUI event attribute names with method-like handler values.
            List<EventUsage> events = [];
            foreach (XElement element in document.Descendants())
            {
                foreach (XAttribute attribute in element.Attributes().Where(IsRoutedEventAttribute))
                {
                    events.Add(new EventUsage(attribute.Name.LocalName, attribute.Value.Trim(), "XamlRoutedEvent", GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
                }
            }

            return events;
        }

        /// <summary>
        /// Extracts static navigation source attributes from WinUI markup.
        /// </summary>
        /// <param name="document">The parsed XAML document.</param>
        /// <returns>Navigation observations in document order.</returns>
        private static IReadOnlyList<NavigationUsage> ExtractNavigation(XDocument document)
        {
            // Markup-level navigation is uncommon in WinUI, but static Source values on frame-like elements remain useful architecture evidence.
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
        /// Extracts a direct or convention-based view-model usage for a WinUI artifact.
        /// </summary>
        /// <param name="root">The parsed XAML root element.</param>
        /// <param name="artifact">The artifact being analyzed.</param>
        /// <param name="repositoryContext">The repository context containing known view-model types.</param>
        /// <returns>The view-model usage classification.</returns>
        private static ViewModelUsage ExtractViewModel(XElement root, WinUiArtifactContext artifact, WinUiRepositoryContext repositoryContext)
        {
            // Direct `<Window.DataContext><vm:MainViewModel /></Window.DataContext>` evidence is preferred over naming conventions.
            XElement? dataContext = root.Descendants().FirstOrDefault(element => element.Name.LocalName.EndsWith(".DataContext", StringComparison.Ordinal));
            XElement? directViewModel = dataContext?.Elements().FirstOrDefault();
            if (directViewModel is not null)
            {
                string viewModelType = directViewModel.Name.LocalName;
                return new ViewModelUsage(viewModelType, "DirectDataContext", Confidence.High, "Direct DataContext element identifies the view model.", false, GetLineNumber(directViewModel), directViewModel.ToString(SaveOptions.DisableFormatting));
            }

            string conventionType = string.Concat(artifact.TypeName, "ViewModel").Replace("WindowViewModel", "ViewModel", StringComparison.Ordinal).Replace("PageViewModel", "ViewModel", StringComparison.Ordinal);
            if (repositoryContext.ViewModelTypeNames.Contains(conventionType))
            {
                return new ViewModelUsage(conventionType, "Convention", Confidence.Medium, "Repository source contains a matching convention-based view-model type.", false, 1, root.ToString(SaveOptions.DisableFormatting));
            }

            return new ViewModelUsage(conventionType, "Convention", Confidence.Low, "Convention-based view-model type was not found in source.", true, 1, root.ToString(SaveOptions.DisableFormatting));
        }

        /// <summary>
        /// Extracts explicit unknown runtime-dependent WinUI observations from parsed markup.
        /// </summary>
        /// <param name="root">The parsed XAML root element.</param>
        /// <returns>Unknown observations in document order.</returns>
        private static IReadOnlyList<UnknownUsage> ExtractUnknowns(XElement root)
        {
            // Unknown extraction focuses on runtime patterns called out by WP011 rather than attempting to model every WinUI dynamic feature.
            List<UnknownUsage> unknowns = [];
            foreach (XElement element in root.DescendantsAndSelf())
            {
                foreach (XAttribute attribute in element.Attributes())
                {
                    if (attribute.Value.Contains("{DynamicResource", StringComparison.Ordinal) || attribute.Value.Contains("{ThemeResource", StringComparison.Ordinal))
                    {
                        unknowns.Add(new UnknownUsage("dynamic resource", "DynamicResource", "Resource", NodeKind.UiResource, "DynamicResource", "WinUI dynamic resource target is computed from runtime state.", GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
                    }

                    if (attribute.Value.Contains("TemplateSelector", StringComparison.OrdinalIgnoreCase) || string.Equals(attribute.Name.LocalName, "ContentTemplateSelector", StringComparison.Ordinal))
                    {
                        unknowns.Add(new UnknownUsage("runtime template", "RuntimeTemplate", "Style", NodeKind.UiStyle, "RuntimeTemplate", "WinUI template selection is determined at runtime.", GetLineNumber(element), element.ToString(SaveOptions.DisableFormatting)));
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
            // Code-behind handlers supplement XAML routed-event declarations and preserve evidence when markup is omitted.
            List<EventUsage> events = [];
            foreach (SourceLine line in SplitLines(content))
            {
                Match match = RoutedEventHandlerRegex().Match(line.Text);
                if (match.Success)
                {
                    events.Add(new EventUsage("Unknown", match.Groups["handler"].Value.Trim(), "CodeBehind", line.LineNumber, line.Text.Trim()));
                }
            }

            return events;
        }

        /// <summary>
        /// Extracts WinUI frame navigation calls from code-behind source.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <returns>Navigation observations in source order.</returns>
        private static IReadOnlyList<NavigationUsage> ExtractCodeBehindNavigation(string content)
        {
            // Frame.Navigate(typeof(PageType)) is static enough to identify a target page, while other expressions become explicit unknowns.
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
        /// Extracts service type usages from code-behind source.
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
            // View-model declarations support direct DataContext validation and convention-based confidence classification.
            foreach (Match match in ViewModelClassRegex().Matches(content))
            {
                yield return match.Groups["name"].Value.Trim();
            }
        }

        /// <summary>
        /// Resolves WinUI startup identity from project metadata and code-behind source.
        /// </summary>
        /// <param name="project">The project whose startup identity is being resolved.</param>
        /// <param name="repositoryContext">The repository context containing startup source content.</param>
        /// <returns>The startup identity when statically available; otherwise, Unknown.</returns>
        private static string ResolveStartupIdentity(WinUiProjectContext project, WinUiArtifactContext? applicationArtifact, WinUiRepositoryContext repositoryContext)
        {
            // WinUI apps usually create the startup window in App.OnLaunched rather than through a XAML StartupUri.
            if (!string.IsNullOrWhiteSpace(project.StartupObject))
            {
                return project.StartupObject.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? project.StartupObject;
            }

            foreach (KeyValuePair<string, string> source in repositoryContext.SourceByPath.Where(pair => pair.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || pair.Key.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)))
            {
                Match match = StartupWindowRegex().Match(source.Value);
                if (match.Success)
                {
                    return match.Groups["window"].Value.Trim();
                }
            }

            _ = applicationArtifact;
            return "Unknown";
        }

        /// <summary>
        /// Creates a source-backed evidence record for one WinUI observation.
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
        private static EvidenceRecord CreateEvidence(WinUiXamlExtractionRequest request, WinUiArtifactContext artifact, string sourceText, string artifactKind, string detectionMode, Confidence confidence, UnknownState unknownState, int? lineNumber = null)
        {
            // Evidence previews are redacted by the shared UI evidence factory so secrets in XAML, manifests, or connection strings do not leak to graph consumers.
            int startLine = lineNumber ?? 1;
            return UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, startLine, startLine, sourceText), "WinUI", artifactKind, detectionMode, confidence, unknownState);
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
        /// Creates a stable key for a WinUI primary artifact node.
        /// </summary>
        /// <param name="artifact">The artifact being represented.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <returns>The deterministic artifact stable key.</returns>
        private static StableKey CreateArtifactStableKey(WinUiArtifactContext artifact, StableKey projectStableKey)
        {
            // Primary artifact identity uses project key, framework, repository-relative path, artifact kind, and type name.
            return UiStableKeyBuilder.Create("ui-artifact://", projectStableKey.Value, "WinUI", artifact.RelativePath, artifact.ArtifactKind.ToString(), artifact.TypeName);
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
        /// Extracts a WinUI binding path from a markup extension value.
        /// </summary>
        /// <param name="value">The raw XAML attribute value.</param>
        /// <returns>The static binding path, or Unknown when none can be resolved.</returns>
        private static string ExtractBindingPath(string value)
        {
            // The parser handles common WinUI binding forms without attempting to evaluate the full markup-extension grammar.
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
            // Local-name matching supports `x:Key`, `x:Name`, and namespace-prefixed attributes without depending on namespace prefixes.
            return element.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))?.Value;
        }

        /// <summary>
        /// Determines whether an element is a root WinUI artifact element.
        /// </summary>
        /// <param name="element">The candidate element.</param>
        /// <param name="artifact">The artifact containing the element.</param>
        /// <returns><see langword="true" /> when the element is the primary artifact root; otherwise, <see langword="false" />.</returns>
        private static bool IsRootArtifactElement(XElement element, WinUiArtifactContext artifact)
        {
            // Root elements are already represented by the artifact node and should not also become child controls.
            return element.Parent is null && artifact.ArtifactKind is WinUiArtifactKind.Window or WinUiArtifactKind.Page or WinUiArtifactKind.UserControl;
        }

        /// <summary>
        /// Determines whether an element is a project-local WinUI component reference.
        /// </summary>
        /// <param name="element">The candidate element.</param>
        /// <returns><see langword="true" /> when the element uses a CLR or WinUI using namespace prefix; otherwise, <see langword="false" />.</returns>
        private static bool IsCustomComponentElement(XElement element)
        {
            // WinUI namespace mappings can appear as `using:` or `clr-namespace:` values and identify project or assembly component references.
            return element.Name.NamespaceName.StartsWith("using:", StringComparison.OrdinalIgnoreCase) || element.Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a XAML element can represent a resource declaration.
        /// </summary>
        /// <param name="element">The candidate element.</param>
        /// <returns><see langword="true" /> when the element has a resource key; otherwise, <see langword="false" />.</returns>
        private static bool IsResourceContainer(XElement element)
        {
            // Any keyed object under WinUI resources can be referenced by StaticResource or ThemeResource.
            return GetXamlAttribute(element, "Key") is not null;
        }

        /// <summary>
        /// Determines whether a XAML attribute name and value represent a routed event handler.
        /// </summary>
        /// <param name="attribute">The candidate attribute.</param>
        /// <returns><see langword="true" /> when the attribute looks like a routed event handler; otherwise, <see langword="false" />.</returns>
        private static bool IsRoutedEventAttribute(XAttribute attribute)
        {
            // The current slice recognizes common WinUI event attribute names and requires method-like handler values to avoid classifying regular text properties as events.
            string name = attribute.Name.LocalName;
            return (name is "Click" or "Loaded" or "SelectionChanged" or "Tapped" or "PointerPressed" or "PointerReleased" or "KeyDown" or "KeyUp")
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
        /// Adds a type-to-source path association to the repository context.
        /// </summary>
        /// <param name="sourcePathsByType">The dictionary being populated.</param>
        /// <param name="typeName">The type name being indexed.</param>
        /// <param name="relativePath">The repository-relative source path.</param>
        private static void AddTypePath(Dictionary<string, HashSet<string>> sourcePathsByType, string typeName, string relativePath)
        {
            // Multiple partial files can contribute to one WinUI type, so each type maps to a set of source paths.
            if (!sourcePathsByType.TryGetValue(typeName, out HashSet<string>? paths))
            {
                paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                sourcePathsByType[typeName] = paths;
            }

            paths.Add(relativePath);
        }

        /// <summary>
        /// Finds the nearest project for a source artifact by longest containing project directory.
        /// </summary>
        /// <param name="projects">The discovered project contexts.</param>
        /// <param name="artifactPath">The absolute artifact path.</param>
        /// <returns>The owning project context when found; otherwise, <see langword="null" />.</returns>
        private static WinUiProjectContext? FindNearestProject(IReadOnlyList<WinUiProjectContext> projects, string artifactPath)
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
        /// Gets the metadata artifact-kind value for a WinUI artifact kind.
        /// </summary>
        /// <param name="artifactKind">The WinUI artifact kind.</param>
        /// <returns>The UI artifact-kind metadata value.</returns>
        private static string GetArtifactKindMetadata(WinUiArtifactKind artifactKind)
        {
            // Metadata uses shared WP011 artifact names rather than WinUI-specific graph node kinds.
            return artifactKind switch
            {
                WinUiArtifactKind.Application => "Application",
                WinUiArtifactKind.Window => "View",
                WinUiArtifactKind.Page => "Page",
                WinUiArtifactKind.UserControl => "Component",
                WinUiArtifactKind.ResourceDictionary => "Resource",
                WinUiArtifactKind.PackageManifest => "Application",
                WinUiArtifactKind.Code => "CodeBehind",
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
        [GeneratedRegex("\\b(?:new\\s+|readonly\\s+|private\\s+readonly\\s+|Private\\s+)?(?<type>[A-Za-z_][A-Za-z0-9_]*Service)\\b", RegexOptions.CultureInvariant)]
        private static partial Regex ServiceTypeUsageRegex();

        /// <summary>
        /// Creates a regex for view-model class declarations.
        /// </summary>
        /// <returns>A regex that captures declared view-model type names.</returns>
        [GeneratedRegex("\\bclass\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*ViewModel)\\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex ViewModelClassRegex();

        /// <summary>
        /// Creates a regex for WinUI code-behind routed event handler methods.
        /// </summary>
        /// <returns>A regex that captures handler method names.</returns>
        [GeneratedRegex("\\b(?<handler>[A-Za-z_][A-Za-z0-9_]*)\\s*\\([^)]*RoutedEventArgs", RegexOptions.CultureInvariant)]
        private static partial Regex RoutedEventHandlerRegex();

        /// <summary>
        /// Creates a regex for static WinUI frame navigation calls.
        /// </summary>
        /// <returns>A regex that captures target page type names.</returns>
        [GeneratedRegex("\\.Navigate\\s*\\(\\s*typeof\\s*\\(\\s*(?<target>[A-Za-z_][A-Za-z0-9_]*)\\s*\\)", RegexOptions.CultureInvariant)]
        private static partial Regex StaticNavigateRegex();

        /// <summary>
        /// Creates a regex for non-static WinUI frame navigation calls.
        /// </summary>
        /// <returns>A regex that captures runtime navigation calls.</returns>
        [GeneratedRegex("\\.Navigate\\s*\\(", RegexOptions.CultureInvariant)]
        private static partial Regex DynamicNavigateRegex();

        /// <summary>
        /// Creates a regex for WinUI startup window construction.
        /// </summary>
        /// <returns>A regex that captures startup window type names.</returns>
        [GeneratedRegex("(?<window>[A-Za-z_][A-Za-z0-9_]*Window)\\s+[A-Za-z_][A-Za-z0-9_]*\\s*=\\s*new\\s*\\(", RegexOptions.CultureInvariant)]
        private static partial Regex StartupWindowRegex();

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
        /// Describes one discovered WinUI-capable project.
        /// </summary>
        /// <param name="AbsoluteProjectPath">The absolute project path used for artifact ownership checks.</param>
        /// <param name="RelativeProjectPath">The repository-relative project path used for stable keys.</param>
        /// <param name="ProjectName">The display name of the project.</param>
        /// <param name="TargetFramework">The target framework value read from project metadata.</param>
        /// <param name="Language">The project source language.</param>
        /// <param name="StartupObject">The optional startup object value read from project metadata.</param>
        /// <param name="PackageType">The Windows packaging mode read from project metadata.</param>
        /// <param name="ApplicationManifest">The optional application manifest path read from project metadata.</param>
        /// <param name="PackageIdentities">The package identities declared by the project.</param>
        private sealed record WinUiProjectContext(string AbsoluteProjectPath, string RelativeProjectPath, string ProjectName, string TargetFramework, string Language, string? StartupObject, string PackageType, string? ApplicationManifest, IReadOnlyList<string> PackageIdentities);

        /// <summary>
        /// Describes normalized project metadata read from a project file.
        /// </summary>
        /// <param name="RelativeProjectPath">The repository-relative project path.</param>
        /// <param name="ProjectName">The project display name.</param>
        /// <param name="TargetFramework">The target framework value or Unknown.</param>
        /// <param name="Language">The project source language.</param>
        /// <param name="StartupObject">The optional startup object value.</param>
        /// <param name="PackageType">The Windows packaging mode value.</param>
        /// <param name="ApplicationManifest">The optional application manifest path.</param>
        /// <param name="PackageIdentities">The package identities declared by the project.</param>
        /// <param name="IsWinUiCandidate">Whether the project contains WinUI evidence.</param>
        private sealed record ProjectMetadata(string RelativeProjectPath, string ProjectName, string TargetFramework, string Language, string? StartupObject, string PackageType, string? ApplicationManifest, IReadOnlyList<string> PackageIdentities, bool IsWinUiCandidate);

        /// <summary>
        /// Describes one discovered WinUI artifact and its owning project.
        /// </summary>
        /// <param name="Project">The project that owns the artifact.</param>
        /// <param name="AbsolutePath">The absolute artifact path used for file reads.</param>
        /// <param name="RelativePath">The repository-relative artifact path used for evidence and stable keys.</param>
        /// <param name="TypeName">The source type name associated with the artifact.</param>
        /// <param name="ArtifactKind">The coarse WinUI artifact classification.</param>
        private sealed record WinUiArtifactContext(WinUiProjectContext Project, string AbsolutePath, string RelativePath, string TypeName, WinUiArtifactKind ArtifactKind);

        /// <summary>
        /// Describes repository-wide WinUI context used during per-artifact analysis.
        /// </summary>
        /// <param name="SourceByPath">Source content keyed by repository-relative path.</param>
        /// <param name="ArtifactByType">XAML artifacts keyed by owning type name.</param>
        /// <param name="SourcePathsByType">Source paths keyed by type name.</param>
        /// <param name="ViewModelTypeNames">Repository-local view-model type names.</param>
        /// <param name="EventsByType">Event handler observations keyed by owner type.</param>
        /// <param name="ServiceUsagesByType">Service usages keyed by owner type.</param>
        /// <param name="DataAccessUsagesByType">Data-access usages keyed by owner type.</param>
        /// <param name="NavigationUsagesByType">Navigation usages keyed by owner type.</param>
        /// <param name="PackagingByProject">Safe packaging metadata keyed by project path.</param>
        private sealed record WinUiRepositoryContext(IReadOnlyDictionary<string, string> SourceByPath, IReadOnlyDictionary<string, WinUiArtifactContext> ArtifactByType, IReadOnlyDictionary<string, HashSet<string>> SourcePathsByType, IReadOnlySet<string> ViewModelTypeNames, IReadOnlyDictionary<string, IReadOnlyList<EventUsage>> EventsByType, IReadOnlyDictionary<string, IReadOnlyList<ServiceUsage>> ServiceUsagesByType, IReadOnlyDictionary<string, IReadOnlyList<DataAccessUsage>> DataAccessUsagesByType, IReadOnlyDictionary<string, IReadOnlyList<NavigationUsage>> NavigationUsagesByType, IReadOnlyDictionary<string, PackagingMetadata> PackagingByProject);

        /// <summary>
        /// Describes safe WinUI packaging metadata.
        /// </summary>
        /// <param name="PackageIdentity">The package identity or Unknown.</param>
        /// <param name="DisplayName">The optional package display name.</param>
        /// <param name="Publisher">The optional package publisher.</param>
        /// <param name="Version">The optional package version.</param>
        /// <param name="PackageType">The packaging mode from project metadata.</param>
        /// <param name="SourcePath">The source path that contributed package evidence.</param>
        /// <param name="IsUnknown">Whether packaging metadata is ambiguous or unavailable.</param>
        private sealed record PackagingMetadata(string PackageIdentity, string? DisplayName, string? Publisher, string? Version, string PackageType, string? SourcePath, bool IsUnknown)
        {
            /// <summary>
            /// Creates unknown packaging metadata for projects without a parseable manifest identity.
            /// </summary>
            /// <param name="packageType">The package type read from project metadata.</param>
            /// <param name="sourcePath">The optional source path that explains the unknown state.</param>
            /// <returns>Unknown packaging metadata with safe defaults.</returns>
            public static PackagingMetadata Unknown(string packageType, string? sourcePath)
            {
                // The unknown factory keeps ambiguous package state explicit without inventing an MSIX identity.
                return new PackagingMetadata("Unknown", null, null, null, string.IsNullOrWhiteSpace(packageType) ? "Unknown" : packageType, sourcePath, true);
            }
        }

        /// <summary>
        /// Describes one source line with its original one-based line number.
        /// </summary>
        /// <param name="LineNumber">The one-based line number.</param>
        /// <param name="Text">The source line text.</param>
        private sealed record SourceLine(int LineNumber, string Text);

        /// <summary>
        /// Describes a WinUI resource, style, or template observation.
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
        /// Describes a WinUI control observation.
        /// </summary>
        /// <param name="ControlName">The control name or type when unnamed.</param>
        /// <param name="ControlType">The WinUI control type.</param>
        /// <param name="IsCustomComponent">Whether the control is a project-local component reference.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record ControlUsage(string ControlName, string ControlType, bool IsCustomComponent, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a WinUI binding observation.
        /// </summary>
        /// <param name="PropertyName">The XAML property being bound.</param>
        /// <param name="BindingPath">The binding path visible in markup.</param>
        /// <param name="IsUnknown">Whether the binding path could not be resolved statically.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record BindingUsage(string PropertyName, string BindingPath, bool IsUnknown, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a WinUI command binding observation.
        /// </summary>
        /// <param name="CommandName">The command property or expression name.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record CommandUsage(string CommandName, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a WinUI routed event observation.
        /// </summary>
        /// <param name="EventName">The routed event name.</param>
        /// <param name="HandlerName">The handler method name.</param>
        /// <param name="DetectionMode">The detection mode that produced the observation.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record EventUsage(string EventName, string HandlerName, string DetectionMode, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a WinUI navigation observation.
        /// </summary>
        /// <param name="Target">The static navigation target or runtime expression.</param>
        /// <param name="IsUnknown">Whether the target is computed from runtime state.</param>
        /// <param name="DetectionMode">The detection mode that produced the observation.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record NavigationUsage(string Target, bool IsUnknown, string DetectionMode, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a WinUI view-model correlation observation.
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
        /// Describes a WinUI runtime-dependent unknown observation.
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
        /// Describes the coarse category of a WinUI artifact.
        /// </summary>
        private enum WinUiArtifactKind
        {
            /// <summary>
            /// A WinUI application definition XAML file.
            /// </summary>
            Application,

            /// <summary>
            /// A WinUI window XAML file.
            /// </summary>
            Window,

            /// <summary>
            /// A WinUI page XAML file.
            /// </summary>
            Page,

            /// <summary>
            /// A WinUI user-control XAML file.
            /// </summary>
            UserControl,

            /// <summary>
            /// A WinUI resource dictionary XAML file.
            /// </summary>
            ResourceDictionary,

            /// <summary>
            /// A code-behind or source file.
            /// </summary>
            Code,

            /// <summary>
            /// A package or application manifest file.
            /// </summary>
            PackageManifest,

            /// <summary>
            /// An unsupported artifact.
            /// </summary>
            Other
        }
    }
}
