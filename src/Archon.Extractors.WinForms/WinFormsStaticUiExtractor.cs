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

namespace Archon.Extractors.WinForms
{
    /// <summary>
    /// Extracts WP011 Windows Forms static UI facts from project, source, designer, and resource files into shared graph contracts.
    /// </summary>
    /// <remarks>
    /// The extractor performs deterministic repository-file analysis only. It does not compile Windows Forms projects, load designers, instantiate controls, start message loops, connect to databases, render UI, or write directly to persistence.
    /// </remarks>
    public sealed partial class WinFormsStaticUiExtractor
    {
        /// <summary>
        /// Extracts Windows Forms application, form, user-control, designer-control, resource, event, binding, dependency, evidence, warning, and unknown facts.
        /// </summary>
        /// <param name="request">The repository-scoped Windows Forms extraction request.</param>
        /// <param name="cancellationToken">The cancellation token that stops file discovery and artifact analysis.</param>
        /// <returns>A result containing the graph-ready snapshot emitted by this extractor.</returns>
        public async Task<WinFormsStaticUiExtractionResult> ExtractAsync(WinFormsStaticUiExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // Extraction is intentionally staged: discover projects, build source/designer/resource context, then project each supported artifact into deterministic graph facts.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            ArchitectureSnapshotAccumulator accumulator = new();
            IReadOnlyList<WinFormsProjectContext> projects = DiscoverProjects(request.RepositoryRootDirectory);
            IReadOnlyList<WinFormsArtifactContext> artifacts = DiscoverArtifacts(request.RepositoryRootDirectory, projects);
            WinFormsRepositoryContext repositoryContext = await BuildRepositoryContextAsync(request.RepositoryRootDirectory, projects, artifacts, cancellationToken).ConfigureAwait(false);

            foreach (WinFormsProjectContext project in projects.Where(project => artifacts.Any(artifact => StringComparer.Ordinal.Equals(artifact.Project.RelativeProjectPath, project.RelativeProjectPath))))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateProjectAndApplication(request, accumulator, project, repositoryContext);
            }

            foreach (WinFormsArtifactContext artifact in artifacts.Where(artifact => artifact.ArtifactKind is WinFormsArtifactKind.Form or WinFormsArtifactKind.UserControl).OrderBy(artifact => artifact.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await AnalyzeArtifactAsync(request, accumulator, repositoryContext, artifact, cancellationToken).ConfigureAwait(false);
            }

            return new WinFormsStaticUiExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Discovers Windows Forms-capable C# and VB.NET projects from project metadata, references, package references, and source evidence.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <returns>Project contexts ordered by repository-relative path.</returns>
        private static IReadOnlyList<WinFormsProjectContext> DiscoverProjects(string repositoryRootDirectory)
        {
            // Project discovery reads static project text only and avoids MSBuild evaluation so extraction remains safe on machines without Windows desktop workloads.
            if (!Directory.Exists(repositoryRootDirectory))
            {
                return [];
            }

            List<WinFormsProjectContext> projects = [];
            IEnumerable<string> projectPaths = Directory.EnumerateFiles(repositoryRootDirectory, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(repositoryRootDirectory, "*.vbproj", SearchOption.AllDirectories))
                .Where(IsRepositorySourcePath)
                .Order(StringComparer.OrdinalIgnoreCase);

            foreach (string projectPath in projectPaths)
            {
                ProjectMetadata metadata = ReadProjectMetadata(repositoryRootDirectory, projectPath);
                if (!metadata.IsWinFormsCandidate)
                {
                    continue;
                }

                projects.Add(new WinFormsProjectContext(projectPath, metadata.RelativeProjectPath, metadata.ProjectName, metadata.TargetFramework, metadata.Language, metadata.StartupObject, metadata.PackageIdentities));
            }

            return projects;
        }

        /// <summary>
        /// Discovers source, designer, and resource files that belong to discovered Windows Forms projects.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="projects">The Windows Forms project contexts that can own artifacts.</param>
        /// <returns>Artifact contexts ordered by repository-relative path.</returns>
        private static IReadOnlyList<WinFormsArtifactContext> DiscoverArtifacts(string repositoryRootDirectory, IReadOnlyList<WinFormsProjectContext> projects)
        {
            // Discovery includes hand-authored code, designer partials, and resources because all three contribute Windows Forms UI structure.
            if (!Directory.Exists(repositoryRootDirectory) || projects.Count == 0)
            {
                return [];
            }

            List<WinFormsArtifactContext> artifacts = [];
            IEnumerable<string> artifactPaths = Directory.EnumerateFiles(repositoryRootDirectory, "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(repositoryRootDirectory, "*.vb", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(repositoryRootDirectory, "*.resx", SearchOption.AllDirectories))
                .Where(IsRepositorySourcePath)
                .Order(StringComparer.OrdinalIgnoreCase);

            foreach (string artifactPath in artifactPaths)
            {
                WinFormsProjectContext? project = FindNearestProject(projects, artifactPath);
                if (project is null)
                {
                    continue;
                }

                string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRootDirectory, artifactPath);
                string content = File.ReadAllText(artifactPath);
                WinFormsArtifactKind artifactKind = ClassifyArtifact(relativePath, content);
                string typeName = ExtractPrimaryTypeName(content, artifactKind) ?? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(artifactPath));
                artifacts.Add(new WinFormsArtifactContext(project, artifactPath, relativePath, typeName, artifactKind));
            }

            return artifacts;
        }

        /// <summary>
        /// Builds repository-wide source context used to correlate code-behind, designer partials, resources, services, and data-access usage.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="projects">The discovered Windows Forms projects.</param>
        /// <param name="artifacts">The discovered Windows Forms artifacts.</param>
        /// <param name="cancellationToken">The cancellation token that stops source loading.</param>
        /// <returns>A repository context used while analyzing individual Windows Forms artifacts.</returns>
        private static async Task<WinFormsRepositoryContext> BuildRepositoryContextAsync(string repositoryRootDirectory, IReadOnlyList<WinFormsProjectContext> projects, IReadOnlyList<WinFormsArtifactContext> artifacts, CancellationToken cancellationToken)
        {
            // Context indexes are built once so per-artifact graph projection can stay deterministic and avoid repeated file scans.
            Dictionary<string, string> sourceByPath = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, WinFormsArtifactContext> designerByType = new(StringComparer.Ordinal);
            Dictionary<string, WinFormsArtifactContext> resourceByType = new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> sourcePathsByType = new(StringComparer.Ordinal);
            HashSet<string> serviceTypeNames = new(StringComparer.Ordinal);
            List<StartupCandidate> startupCandidates = [];
            Dictionary<string, IReadOnlyList<EventSubscription>> eventsByType = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<ServiceUsage>> serviceUsagesByType = new(StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<DataAccessUsage>> dataAccessUsagesByType = new(StringComparer.Ordinal);

            foreach (WinFormsArtifactContext artifact in artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string content = await File.ReadAllTextAsync(artifact.AbsolutePath, cancellationToken).ConfigureAwait(false);
                sourceByPath[artifact.RelativePath] = content;

                if (artifact.ArtifactKind is WinFormsArtifactKind.Designer)
                {
                    designerByType[artifact.TypeName] = artifact;
                }
                else if (artifact.ArtifactKind is WinFormsArtifactKind.Resource)
                {
                    resourceByType[artifact.TypeName] = artifact;
                }
                else
                {
                    AddTypePath(sourcePathsByType, artifact.TypeName, artifact.RelativePath);
                }

                foreach (StartupCandidate startupCandidate in ExtractStartupCandidates(artifact, content))
                {
                    startupCandidates.Add(startupCandidate);
                }

                if (artifact.ArtifactKind is WinFormsArtifactKind.Form or WinFormsArtifactKind.UserControl)
                {
                    eventsByType[artifact.TypeName] = ExtractCodeBehindEventSubscriptions(content, artifact.Project.Language);
                    serviceUsagesByType[artifact.TypeName] = ExtractServiceUsages(content);
                    dataAccessUsagesByType[artifact.TypeName] = ExtractDataAccessUsages(content, artifact.Project.PackageIdentities);
                }

                foreach (string serviceTypeName in ExtractRepositoryServiceTypeNames(content))
                {
                    serviceTypeNames.Add(serviceTypeName);
                }
            }

            return new WinFormsRepositoryContext(repositoryRootDirectory, sourceByPath, designerByType, resourceByType, sourcePathsByType, serviceTypeNames, startupCandidates, eventsByType, serviceUsagesByType, dataAccessUsagesByType);
        }

        /// <summary>
        /// Adds project and UI application facts for one Windows Forms project.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="project">The project context being represented.</param>
        /// <param name="repositoryContext">The repository context that supplies startup-form evidence.</param>
        private static void AccumulateProjectAndApplication(WinFormsStaticUiExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinFormsProjectContext project, WinFormsRepositoryContext repositoryContext)
        {
            // Project and application facts give all Windows Forms UI nodes stable ownership when this extractor runs independently from project inventory.
            StableKey projectStableKey = StableKeyGenerator.ForProject(project.RelativeProjectPath);
            EvidenceRecord projectEvidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(project.RelativeProjectPath, 1, 1, project.ProjectName), "WinForms", "Application", "ProjectMetadata", Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(projectEvidence);

            GraphMetadata projectMetadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "ProjectMetadata",
                ["language"] = project.Language,
                ["packageIdentity"] = string.Join(",", project.PackageIdentities),
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = project.RelativeProjectPath,
                ["targetFramework"] = project.TargetFramework,
                ["uiFramework"] = "WinForms"
            });
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, projectStableKey, NodeKind.Project, project.ProjectName, project.RelativeProjectPath, project.RelativeProjectPath, project.Language, projectStableKey, null, Confidence.High, UnknownState.Known, projectEvidence.StableKey, projectMetadata));

            string startupForm = ResolveStartupForm(project, repositoryContext) ?? "Unknown";
            UnknownState unknownState = string.Equals(startupForm, "Unknown", StringComparison.Ordinal) ? UnknownState.Unknown("Windows Forms startup form could not be resolved statically.") : UnknownState.Known;
            Confidence confidence = unknownState.HasUnknownData ? Confidence.Low : Confidence.High;
            StableKey applicationStableKey = UiStableKeyBuilder.Create("ui-application://", projectStableKey.Value, "WinForms", project.TargetFramework, startupForm);
            GraphMetadata applicationMetadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = unknownState.HasUnknownData ? "No static Application.Run or StartupObject value identified the startup form." : "Project metadata or Application.Run identified the startup form.",
                ["detectionMode"] = "ProjectMetadata",
                ["hostingModel"] = "Desktop",
                ["language"] = project.Language,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = project.RelativeProjectPath,
                ["startupForm"] = startupForm,
                ["targetFramework"] = project.TargetFramework,
                ["uiArtifactKind"] = "Application",
                ["uiFramework"] = "WinForms"
            });
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, applicationStableKey, NodeKind.UiApplication, project.ProjectName, project.RelativeProjectPath, project.ProjectName, project.Language, projectStableKey, projectStableKey, confidence, unknownState, projectEvidence.StableKey, applicationMetadata));
            if (unknownState.HasUnknownData)
            {
                accumulator.AddWarning($"Windows Forms startup form for {project.RelativeProjectPath} could not be statically resolved.");
            }
        }

        /// <summary>
        /// Analyzes one Windows Forms form or user-control artifact and contributes graph facts for supported source, designer, and resource patterns.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="repositoryContext">The repository context used for designer/resource/code-behind correlation.</param>
        /// <param name="artifact">The form or user-control artifact being analyzed.</param>
        /// <param name="cancellationToken">The cancellation token that stops file reads.</param>
        /// <returns>A task that completes after all graph facts for the artifact are accumulated.</returns>
        private static async Task AnalyzeArtifactAsync(WinFormsStaticUiExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, WinFormsRepositoryContext repositoryContext, WinFormsArtifactContext artifact, CancellationToken cancellationToken)
        {
            // Form analysis intentionally merges code-behind, designer, and resx evidence around the same type identity without requiring designer execution.
            string content = await File.ReadAllTextAsync(artifact.AbsolutePath, cancellationToken).ConfigureAwait(false);
            StableKey projectStableKey = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath);
            StableKey applicationStableKey = UiStableKeyBuilder.Create("ui-application://", projectStableKey.Value, "WinForms", artifact.Project.TargetFramework, ResolveStartupForm(artifact.Project, repositoryContext) ?? "Unknown");
            EvidenceRecord artifactEvidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, 1, CountLines(content), content), "WinForms", GetArtifactKindMetadata(artifact.ArtifactKind), "StaticSource", Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(artifactEvidence);

            ArchitectureNode artifactNode = CreateArtifactNode(request.SnapshotStableKey, artifact, projectStableKey, artifactEvidence.StableKey, ResolveStartupForm(artifact.Project, repositoryContext));
            accumulator.AddNode(artifactNode);
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DeclaresComponent, applicationStableKey, artifactNode.StableKey, artifactEvidence.StableKey, "DeclaresWinFormsArtifact", artifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["uiFramework"] = "WinForms",
                ["uiArtifactKind"] = GetArtifactKindMetadata(artifact.ArtifactKind),
                ["sourcePath"] = artifact.RelativePath
            }));

            if (repositoryContext.DesignerByType.TryGetValue(artifact.TypeName, out WinFormsArtifactContext? designerArtifact))
            {
                string designerContent = repositoryContext.SourceByPath[designerArtifact.RelativePath];
                AccumulateDesignerFacts(request, accumulator, artifactNode, designerArtifact, designerContent);
            }
            else
            {
                accumulator.AddWarning($"Windows Forms designer partial for {artifact.TypeName} could not be statically resolved.");
            }

            if (repositoryContext.ResourceByType.TryGetValue(artifact.TypeName, out WinFormsArtifactContext? resourceArtifact))
            {
                string resourceContent = repositoryContext.SourceByPath[resourceArtifact.RelativePath];
                AccumulateResourceFacts(request, accumulator, artifactNode, resourceArtifact, resourceContent);
            }

            foreach (EventSubscription eventSubscription in repositoryContext.EventsByType.GetValueOrDefault(artifact.TypeName, []))
            {
                AccumulateEventSubscription(request, accumulator, artifactNode, artifact, eventSubscription);
            }

            foreach (ServiceUsage serviceUsage in repositoryContext.ServiceUsagesByType.GetValueOrDefault(artifact.TypeName, []))
            {
                AccumulateServiceUsage(request, accumulator, artifactNode, artifact, serviceUsage);
            }

            foreach (DataAccessUsage dataAccessUsage in repositoryContext.DataAccessUsagesByType.GetValueOrDefault(artifact.TypeName, []))
            {
                AccumulateDataAccessUsage(request, accumulator, artifactNode, artifact, dataAccessUsage);
            }
        }

        /// <summary>
        /// Accumulates designer-derived control, hierarchy, binding, and designer-unknown facts for a form or user control.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifactNode">The form or user-control node that owns designer facts.</param>
        /// <param name="designerArtifact">The correlated designer artifact.</param>
        /// <param name="designerContent">The designer source content.</param>
        private static void AccumulateDesignerFacts(WinFormsStaticUiExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, ArchitectureNode artifactNode, WinFormsArtifactContext designerArtifact, string designerContent)
        {
            // Designer parsing focuses on stable field declarations and InitializeComponent assignments; runtime-created controls become explicit unknowns.
            IReadOnlyDictionary<string, string> controlTypes = ExtractDesignerControlFields(designerContent, designerArtifact.Project.Language);
            IReadOnlyList<ControlHierarchyUsage> hierarchyUsages = ExtractControlHierarchyUsages(designerContent, designerArtifact.Project.Language);
            IReadOnlyList<BindingUsage> bindingUsages = ExtractBindingUsages(designerContent, designerArtifact.Project.Language);
            IReadOnlyList<DynamicControlUsage> dynamicControls = ExtractDynamicControlUsages(designerContent, designerArtifact.Project.Language);

            foreach (KeyValuePair<string, string> control in controlTypes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                AccumulateControl(request, accumulator, artifactNode, designerArtifact, control.Key, control.Value, designerContent, UnknownState.Known, Confidence.High);
            }

            foreach (ControlHierarchyUsage hierarchyUsage in hierarchyUsages)
            {
                if (!controlTypes.ContainsKey(hierarchyUsage.ChildControlName))
                {
                    continue;
                }

                StableKey parentKey = string.Equals(hierarchyUsage.ParentControlName, "this", StringComparison.OrdinalIgnoreCase) || string.Equals(hierarchyUsage.ParentControlName, "Me", StringComparison.OrdinalIgnoreCase)
                    ? artifactNode.StableKey
                    : CreateControlStableKey(designerArtifact, hierarchyUsage.ParentControlName);
                StableKey childKey = CreateControlStableKey(designerArtifact, hierarchyUsage.ChildControlName);
                EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(designerArtifact.RelativePath, hierarchyUsage.LineNumber, hierarchyUsage.LineNumber, hierarchyUsage.SourceText), "WinForms", "Control", "DesignerSource", Confidence.Medium, UnknownState.Known);
                accumulator.AddEvidence(evidence);
                accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesControl, parentKey, childKey, evidence.StableKey, "ControlHierarchy", designerArtifact.RelativePath, Confidence.Medium, UnknownState.Known, new Dictionary<string, object?>
                {
                    ["controlName"] = hierarchyUsage.ChildControlName,
                    ["parentControlName"] = hierarchyUsage.ParentControlName,
                    ["sourcePath"] = designerArtifact.RelativePath,
                    ["uiArtifactKind"] = "Control",
                    ["uiFramework"] = "WinForms"
                }));
            }

            foreach (BindingUsage bindingUsage in bindingUsages)
            {
                AccumulateBinding(request, accumulator, artifactNode, designerArtifact, bindingUsage);
            }

            foreach (DynamicControlUsage dynamicControl in dynamicControls)
            {
                accumulator.AddWarning($"Windows Forms dynamic control in {designerArtifact.RelativePath} on line {dynamicControl.LineNumber.ToString(CultureInfo.InvariantCulture)} could not be fully resolved statically.");
                AccumulateControl(request, accumulator, artifactNode, designerArtifact, dynamicControl.ControlName, dynamicControl.ControlType, dynamicControl.SourceText, UnknownState.Unknown("Windows Forms control is created dynamically and cannot be fully resolved statically."), Confidence.Low);
            }
        }

        /// <summary>
        /// Accumulates a UI control node and ownership relationship.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifactNode">The form or user-control node that owns the control.</param>
        /// <param name="designerArtifact">The designer artifact that declares or creates the control.</param>
        /// <param name="controlName">The control field or synthesized dynamic-control name.</param>
        /// <param name="controlType">The control type visible in designer source.</param>
        /// <param name="sourceText">The source snippet that supports the control fact.</param>
        /// <param name="unknownState">The unknown-state assigned to the control fact.</param>
        /// <param name="confidence">The confidence assigned to the control fact.</param>
        private static void AccumulateControl(WinFormsStaticUiExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, ArchitectureNode artifactNode, WinFormsArtifactContext designerArtifact, string controlName, string controlType, string sourceText, UnknownState unknownState, Confidence confidence)
        {
            // Control identity is anchored to the owning designer artifact and control name so repeated designer assignments de-duplicate deterministically.
            StableKey controlStableKey = CreateControlStableKey(designerArtifact, controlName);
            int lineNumber = FindLineNumber(sourceText, designerArtifact, controlName);
            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(designerArtifact.RelativePath, lineNumber, lineNumber, sourceText), "WinForms", "Control", "DesignerSource", confidence, unknownState);
            accumulator.AddEvidence(evidence);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = unknownState.HasUnknownData ? "The control was created from a runtime expression in designer source." : "The control field and type are visible in designer source.",
                ["controlName"] = controlName,
                ["controlType"] = controlType,
                ["detectionMode"] = "DesignerSource",
                ["projectKey"] = StableKeyGenerator.ForProject(designerArtifact.Project.RelativeProjectPath).Value,
                ["sourcePath"] = designerArtifact.RelativePath,
                ["targetFramework"] = designerArtifact.Project.TargetFramework,
                ["uiArtifactKind"] = "Control",
                ["uiFramework"] = "WinForms"
            });
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, controlStableKey, NodeKind.UiControl, controlName, string.Concat(designerArtifact.TypeName, ".", controlName), controlName, designerArtifact.Project.Language, StableKeyGenerator.ForProject(designerArtifact.Project.RelativeProjectPath), artifactNode.StableKey, confidence, unknownState, evidence.StableKey, metadata));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesControl, artifactNode.StableKey, controlStableKey, evidence.StableKey, "UsesControl", designerArtifact.RelativePath, confidence, unknownState, new Dictionary<string, object?>
            {
                ["controlName"] = controlName,
                ["controlType"] = controlType,
                ["sourcePath"] = designerArtifact.RelativePath,
                ["uiArtifactKind"] = "Control",
                ["uiFramework"] = "WinForms"
            }));
        }

        /// <summary>
        /// Accumulates `.resx` resource nodes and relationships for a form or user control.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifactNode">The form or user-control node that uses the resources.</param>
        /// <param name="resourceArtifact">The correlated `.resx` artifact.</param>
        /// <param name="resourceContent">The `.resx` XML content.</param>
        private static void AccumulateResourceFacts(WinFormsStaticUiExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, ArchitectureNode artifactNode, WinFormsArtifactContext resourceArtifact, string resourceContent)
        {
            // Resource extraction reads only XML data names and redacts previews before evidence is stored.
            foreach (ResourceUsage resourceUsage in ExtractResourceUsages(resourceContent))
            {
                EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(resourceArtifact.RelativePath, resourceUsage.LineNumber, resourceUsage.LineNumber, resourceUsage.SourceText), "WinForms", "Resource", "ResxResource", Confidence.High, UnknownState.Known);
                accumulator.AddEvidence(evidence);
                StableKey resourceStableKey = UiStableKeyBuilder.Create("ui-resource://", StableKeyGenerator.ForProject(resourceArtifact.Project.RelativeProjectPath).Value, "WinForms", resourceArtifact.RelativePath, resourceUsage.ResourceName);
                GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
                {
                    ["detectionMode"] = "ResxResource",
                    ["projectKey"] = StableKeyGenerator.ForProject(resourceArtifact.Project.RelativeProjectPath).Value,
                    ["resourceKey"] = resourceUsage.ResourceName,
                    ["sourcePath"] = resourceArtifact.RelativePath,
                    ["targetFramework"] = resourceArtifact.Project.TargetFramework,
                    ["uiArtifactKind"] = "Resource",
                    ["uiFramework"] = "WinForms"
                });
                accumulator.AddNode(CreateNode(request.SnapshotStableKey, resourceStableKey, NodeKind.UiResource, resourceUsage.ResourceName, resourceUsage.ResourceName, resourceUsage.ResourceName, resourceArtifact.Project.Language, StableKeyGenerator.ForProject(resourceArtifact.Project.RelativeProjectPath), artifactNode.StableKey, Confidence.High, UnknownState.Known, evidence.StableKey, metadata));
                accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesUiResource, artifactNode.StableKey, resourceStableKey, evidence.StableKey, "UsesResource", resourceArtifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
                {
                    ["resourceKey"] = resourceUsage.ResourceName,
                    ["sourcePath"] = resourceArtifact.RelativePath,
                    ["uiArtifactKind"] = "Resource",
                    ["uiFramework"] = "WinForms"
                }));
            }
        }

        /// <summary>
        /// Accumulates a command node and UI event relationships for one event subscription.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifactNode">The form or user-control node that owns the event subscription.</param>
        /// <param name="artifact">The source artifact that contains the event subscription.</param>
        /// <param name="eventSubscription">The parsed event subscription.</param>
        private static void AccumulateEventSubscription(WinFormsStaticUiExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, ArchitectureNode artifactNode, WinFormsArtifactContext artifact, EventSubscription eventSubscription)
        {
            // Events become command-style facts so UI interactions can be queried through the same relationship vocabulary used by earlier WP011 slices.
            Confidence confidence = eventSubscription.IsDynamic ? Confidence.Low : Confidence.High;
            UnknownState unknownState = eventSubscription.IsDynamic ? UnknownState.Unknown("Windows Forms event handler is wired through runtime-generated code.") : UnknownState.Known;
            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, eventSubscription.LineNumber, eventSubscription.LineNumber, eventSubscription.SourceText), "WinForms", "Command", "StaticSource", confidence, unknownState);
            accumulator.AddEvidence(evidence);
            StableKey commandStableKey = UiStableKeyBuilder.Create("ui-command://", StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath).Value, "WinForms", artifact.RelativePath, artifact.TypeName, eventSubscription.ControlName, eventSubscription.EventName, eventSubscription.HandlerName);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["commandName"] = eventSubscription.HandlerName,
                ["controlName"] = eventSubscription.ControlName,
                ["detectionMode"] = "StaticSource",
                ["eventName"] = eventSubscription.EventName,
                ["projectKey"] = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath).Value,
                ["sourcePath"] = artifact.RelativePath,
                ["targetFramework"] = artifact.Project.TargetFramework,
                ["uiArtifactKind"] = "Command",
                ["uiFramework"] = "WinForms"
            });
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, commandStableKey, NodeKind.Command, eventSubscription.HandlerName, string.Concat(artifact.TypeName, ".", eventSubscription.HandlerName), eventSubscription.HandlerName, artifact.Project.Language, StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath), artifactNode.StableKey, confidence, unknownState, evidence.StableKey, metadata));
            Dictionary<string, object?> edgeMetadata = new(StringComparer.Ordinal)
            {
                ["commandName"] = eventSubscription.HandlerName,
                ["controlName"] = eventSubscription.ControlName,
                ["eventName"] = eventSubscription.EventName,
                ["sourcePath"] = artifact.RelativePath,
                ["uiArtifactKind"] = "Command",
                ["uiFramework"] = "WinForms"
            };
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.HandlesUiEvent, artifactNode.StableKey, commandStableKey, evidence.StableKey, "HandlesUiEvent", artifact.RelativePath, confidence, unknownState, edgeMetadata));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesCommand, artifactNode.StableKey, commandStableKey, evidence.StableKey, "UsesCommand", artifact.RelativePath, confidence, unknownState, edgeMetadata));
            if (unknownState.HasUnknownData)
            {
                accumulator.AddWarning($"Windows Forms runtime event wiring in {artifact.RelativePath} on line {eventSubscription.LineNumber.ToString(CultureInfo.InvariantCulture)} could not be fully resolved statically.");
            }
        }

        /// <summary>
        /// Accumulates a binding node and relationship for one designer data-binding expression.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifactNode">The form or user-control node that owns the binding.</param>
        /// <param name="designerArtifact">The designer artifact that contains the binding expression.</param>
        /// <param name="bindingUsage">The parsed binding usage.</param>
        private static void AccumulateBinding(WinFormsStaticUiExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, ArchitectureNode artifactNode, WinFormsArtifactContext designerArtifact, BindingUsage bindingUsage)
        {
            // Binding nodes keep control/property/source path information visible without attempting to evaluate binding sources at runtime.
            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(designerArtifact.RelativePath, bindingUsage.LineNumber, bindingUsage.LineNumber, bindingUsage.SourceText), "WinForms", "Binding", "DesignerSource", Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(evidence);
            StableKey bindingStableKey = UiStableKeyBuilder.Create("ui-binding://", StableKeyGenerator.ForProject(designerArtifact.Project.RelativeProjectPath).Value, "WinForms", designerArtifact.RelativePath, bindingUsage.ControlName, bindingUsage.ControlProperty, bindingUsage.BindingPath);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["bindingPath"] = bindingUsage.BindingPath,
                ["controlName"] = bindingUsage.ControlName,
                ["controlProperty"] = bindingUsage.ControlProperty,
                ["detectionMode"] = "DesignerSource",
                ["projectKey"] = StableKeyGenerator.ForProject(designerArtifact.Project.RelativeProjectPath).Value,
                ["sourcePath"] = designerArtifact.RelativePath,
                ["targetFramework"] = designerArtifact.Project.TargetFramework,
                ["uiArtifactKind"] = "Binding",
                ["uiFramework"] = "WinForms"
            });
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, bindingStableKey, NodeKind.Binding, string.Concat(bindingUsage.ControlName, ".", bindingUsage.ControlProperty), bindingUsage.BindingPath, bindingUsage.BindingPath, designerArtifact.Project.Language, StableKeyGenerator.ForProject(designerArtifact.Project.RelativeProjectPath), artifactNode.StableKey, Confidence.High, UnknownState.Known, evidence.StableKey, metadata));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.BindsTo, CreateControlStableKey(designerArtifact, bindingUsage.ControlName), bindingStableKey, evidence.StableKey, "BindsTo", designerArtifact.RelativePath, Confidence.High, UnknownState.Known, new Dictionary<string, object?>
            {
                ["bindingPath"] = bindingUsage.BindingPath,
                ["controlName"] = bindingUsage.ControlName,
                ["sourcePath"] = designerArtifact.RelativePath,
                ["uiArtifactKind"] = "Binding",
                ["uiFramework"] = "WinForms"
            }));
        }

        /// <summary>
        /// Accumulates a type dependency for code-behind service usage.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifactNode">The form or user-control node that uses the service.</param>
        /// <param name="artifact">The source artifact that contains service usage.</param>
        /// <param name="serviceUsage">The parsed service usage.</param>
        private static void AccumulateServiceUsage(WinFormsStaticUiExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, ArchitectureNode artifactNode, WinFormsArtifactContext artifact, ServiceUsage serviceUsage)
        {
            // Service correlation is conservative: a direct constructor or field type ending in Service is modeled as a type dependency, not as DI proof.
            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, serviceUsage.LineNumber, serviceUsage.LineNumber, serviceUsage.SourceText), "WinForms", "Dependency", "StaticSource", Confidence.Medium, UnknownState.Known);
            accumulator.AddEvidence(evidence);
            StableKey serviceStableKey = UiStableKeyBuilder.Create("ui-service-type://", StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath).Value, serviceUsage.TypeName);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "StaticSource",
                ["projectKey"] = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath).Value,
                ["sourcePath"] = artifact.RelativePath,
                ["targetFramework"] = artifact.Project.TargetFramework,
                ["typeName"] = serviceUsage.TypeName,
                ["uiFramework"] = "WinForms"
            });
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, serviceStableKey, NodeKind.Type, serviceUsage.TypeName, serviceUsage.TypeName, serviceUsage.TypeName, artifact.Project.Language, StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath), null, Confidence.Medium, UnknownState.Known, evidence.StableKey, metadata));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DependsOn, artifactNode.StableKey, serviceStableKey, evidence.StableKey, "DependsOnService", artifact.RelativePath, Confidence.Medium, UnknownState.Known, new Dictionary<string, object?>
            {
                ["sourcePath"] = artifact.RelativePath,
                ["typeName"] = serviceUsage.TypeName,
                ["uiFramework"] = "WinForms"
            }));
        }

        /// <summary>
        /// Accumulates an external-service/API-style dependency for code-behind data-access usage.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The accumulator that receives graph facts.</param>
        /// <param name="artifactNode">The form or user-control node that uses data access.</param>
        /// <param name="artifact">The source artifact that contains data-access usage.</param>
        /// <param name="dataAccessUsage">The parsed data-access usage.</param>
        private static void AccumulateDataAccessUsage(WinFormsStaticUiExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, ArchitectureNode artifactNode, WinFormsArtifactContext artifact, DataAccessUsage dataAccessUsage)
        {
            // Data-access usage is represented as an outbound call/dependency so legacy desktop screens can be correlated with database-facing libraries.
            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(request.SnapshotStableKey, new UiSourceLocation(artifact.RelativePath, dataAccessUsage.LineNumber, dataAccessUsage.LineNumber, dataAccessUsage.SourceText), "WinForms", "Dependency", "StaticSource", Confidence.Medium, UnknownState.Known);
            accumulator.AddEvidence(evidence);
            StableKey externalStableKey = UiStableKeyBuilder.Create("ui-external-service://", StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath).Value, "WinForms", dataAccessUsage.PackageIdentity);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "StaticSource",
                ["packageIdentity"] = dataAccessUsage.PackageIdentity,
                ["projectKey"] = StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath).Value,
                ["sourcePath"] = artifact.RelativePath,
                ["targetFramework"] = artifact.Project.TargetFramework,
                ["uiFramework"] = "WinForms"
            });
            accumulator.AddNode(CreateNode(request.SnapshotStableKey, externalStableKey, NodeKind.ExternalService, dataAccessUsage.PackageIdentity, dataAccessUsage.PackageIdentity, dataAccessUsage.PackageIdentity, artifact.Project.Language, StableKeyGenerator.ForProject(artifact.Project.RelativeProjectPath), null, Confidence.Medium, UnknownState.Known, evidence.StableKey, metadata));
            Dictionary<string, object?> edgeMetadata = new(StringComparer.Ordinal)
            {
                ["packageIdentity"] = dataAccessUsage.PackageIdentity,
                ["sourcePath"] = artifact.RelativePath,
                ["uiFramework"] = "WinForms"
            };
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.CallsApi, artifactNode.StableKey, externalStableKey, evidence.StableKey, "CallsDataAccess", artifact.RelativePath, Confidence.Medium, UnknownState.Known, edgeMetadata));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.DependsOn, artifactNode.StableKey, externalStableKey, evidence.StableKey, "DependsOnDataAccess", artifact.RelativePath, Confidence.Medium, UnknownState.Known, edgeMetadata));
        }

        /// <summary>
        /// Creates the architecture node for a Windows Forms form or user control artifact.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the node.</param>
        /// <param name="artifact">The form or user-control artifact.</param>
        /// <param name="projectStableKey">The stable key of the owning project.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key.</param>
        /// <param name="startupForm">The resolved startup form name, if available.</param>
        /// <returns>The graph node representing the UI artifact.</returns>
        private static ArchitectureNode CreateArtifactNode(StableKey snapshotStableKey, WinFormsArtifactContext artifact, StableKey projectStableKey, StableKey evidenceStableKey, string? startupForm)
        {
            // Forms are UI views and user controls are UI components; both share the same metadata vocabulary for querying.
            NodeKind nodeKind = artifact.ArtifactKind is WinFormsArtifactKind.Form ? NodeKind.UiView : NodeKind.UiComponent;
            string artifactKind = artifact.ArtifactKind is WinFormsArtifactKind.Form ? "View" : "Component";
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = "The Windows Forms artifact type is visible in source inheritance.",
                ["detectionMode"] = "StaticSource",
                ["language"] = artifact.Project.Language,
                ["projectKey"] = projectStableKey.Value,
                ["sourcePath"] = artifact.RelativePath,
                ["startupForm"] = string.Equals(startupForm, artifact.TypeName, StringComparison.Ordinal) ? artifact.TypeName : null,
                ["targetFramework"] = artifact.Project.TargetFramework,
                ["typeName"] = artifact.TypeName,
                ["uiArtifactKind"] = artifactKind,
                ["uiFramework"] = "WinForms",
                ["viewName"] = artifact.ArtifactKind is WinFormsArtifactKind.Form ? artifact.TypeName : null
            });
            StableKey stableKey = UiStableKeyBuilder.Create(artifact.ArtifactKind is WinFormsArtifactKind.Form ? "ui-view://" : "ui-component://", projectStableKey.Value, "WinForms", artifact.RelativePath, artifact.TypeName);
            return CreateNode(snapshotStableKey, stableKey, nodeKind, artifact.TypeName, artifact.TypeName, artifact.TypeName, artifact.Project.Language, projectStableKey, projectStableKey, Confidence.High, UnknownState.Known, evidenceStableKey, metadata);
        }

        /// <summary>
        /// Creates a graph node with a deterministic fingerprint from normalized node fields.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the node.</param>
        /// <param name="stableKey">The deterministic stable key for the node.</param>
        /// <param name="nodeKind">The controlled node kind.</param>
        /// <param name="displayName">The developer-facing node display name.</param>
        /// <param name="qualifiedName">The optional qualified name.</param>
        /// <param name="searchName">The normalized search name.</param>
        /// <param name="language">The optional programming or artifact language.</param>
        /// <param name="projectStableKey">The owning project stable key.</param>
        /// <param name="parentNodeStableKey">The optional parent node stable key.</param>
        /// <param name="confidence">The confidence assigned to the node.</param>
        /// <param name="unknownState">The unknown-state assigned to the node.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key.</param>
        /// <param name="metadata">The deterministic metadata payload.</param>
        /// <returns>The constructed architecture node.</returns>
        private static ArchitectureNode CreateNode(StableKey snapshotStableKey, StableKey stableKey, NodeKind nodeKind, string displayName, string qualifiedName, string searchName, string? language, StableKey? projectStableKey, StableKey? parentNodeStableKey, Confidence confidence, UnknownState unknownState, StableKey? evidenceStableKey, GraphMetadata metadata)
        {
            // The helper keeps node construction consistent and ensures fingerprints match the emitted canonical metadata.
            return new ArchitectureNode(snapshotStableKey, stableKey, nodeKind, displayName, qualifiedName, searchName, language, projectStableKey, parentNodeStableKey, KnowledgeKind.Fact, null, null, confidence, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForNode(nodeKind, displayName, qualifiedName, searchName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a graph edge with deterministic relationship identity and metadata.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the edge.</param>
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
        /// Reads Windows Forms-relevant metadata from a C# or VB.NET project file.
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
                string[] packageIdentities = document.Descendants().Where(element => string.Equals(element.Name.LocalName, "PackageReference", StringComparison.Ordinal)).Select(element => element.Attribute("Include")?.Value).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                bool isWinFormsCandidate = string.Equals(ReadFirstElementValue(document, "UseWindowsForms"), "true", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("System.Windows.Forms", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("WindowsForms", StringComparison.OrdinalIgnoreCase)
                    || targetFramework.Contains("-windows", StringComparison.OrdinalIgnoreCase)
                    || startupObject is not null;
                return new ProjectMetadata(relativeProjectPath, projectName, string.IsNullOrWhiteSpace(targetFramework) ? "Unknown" : targetFramework.Trim(), language, startupObject?.Trim(), packageIdentities, isWinFormsCandidate);
            }
            catch (Exception) when (IsXmlReadException())
            {
                // Malformed project files cannot be evaluated safely; source-symbol evidence may still identify WinForms through future enhancement, so this project is skipped for now.
                return new ProjectMetadata(relativeProjectPath, projectName, "Unknown", language, null, [], false);
            }
        }

        /// <summary>
        /// Classifies a repository artifact into a Windows Forms source, designer, resource, or unsupported category.
        /// </summary>
        /// <param name="relativePath">The repository-relative artifact path.</param>
        /// <param name="content">The artifact content.</param>
        /// <returns>The artifact kind used by Windows Forms extraction.</returns>
        private static WinFormsArtifactKind ClassifyArtifact(string relativePath, string content)
        {
            // Classification relies on source naming and inheritance tokens because extractor execution must not load WinForms assemblies.
            if (relativePath.EndsWith(".resx", StringComparison.OrdinalIgnoreCase))
            {
                return WinFormsArtifactKind.Resource;
            }

            if (relativePath.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase))
            {
                return WinFormsArtifactKind.Designer;
            }

            if (FormInheritanceRegex().IsMatch(content) || VbFormInheritanceRegex().IsMatch(content))
            {
                return WinFormsArtifactKind.Form;
            }

            if (UserControlInheritanceRegex().IsMatch(content) || VbUserControlInheritanceRegex().IsMatch(content))
            {
                return WinFormsArtifactKind.UserControl;
            }

            return WinFormsArtifactKind.Other;
        }

        /// <summary>
        /// Extracts the primary type name from C# or VB.NET source content.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <param name="artifactKind">The artifact kind being named.</param>
        /// <returns>The primary type name when one can be read; otherwise, <see langword="null" />.</returns>
        private static string? ExtractPrimaryTypeName(string content, WinFormsArtifactKind artifactKind)
        {
            // Designer and code-behind partials both declare a type name; `.resx` files are named by path instead.
            if (artifactKind is WinFormsArtifactKind.Resource)
            {
                return null;
            }

            Match csharp = CSharpTypeRegex().Match(content);
            if (csharp.Success)
            {
                return csharp.Groups["name"].Value.Trim();
            }

            Match visualBasic = VbTypeRegex().Match(content);
            return visualBasic.Success ? visualBasic.Groups["name"].Value.Trim() : null;
        }

        /// <summary>
        /// Extracts startup form candidates from `Application.Run` calls and VB startup metadata visible in source.
        /// </summary>
        /// <param name="artifact">The artifact being inspected.</param>
        /// <param name="content">The artifact content.</param>
        /// <returns>Startup candidates in source order.</returns>
        private static IReadOnlyList<StartupCandidate> ExtractStartupCandidates(WinFormsArtifactContext artifact, string content)
        {
            // Startup forms are detected from code only; the extractor does not run the entry point or evaluate application settings.
            List<StartupCandidate> candidates = [];
            foreach (SourceLine line in SplitLines(content))
            {
                Match csharpMatch = ApplicationRunRegex().Match(line.Text);
                if (csharpMatch.Success)
                {
                    candidates.Add(new StartupCandidate(artifact.Project.RelativeProjectPath, csharpMatch.Groups["form"].Value.Trim(), artifact.RelativePath, line.LineNumber, line.Text.Trim()));
                }

                Match visualBasicMatch = VbApplicationRunRegex().Match(line.Text);
                if (visualBasicMatch.Success)
                {
                    candidates.Add(new StartupCandidate(artifact.Project.RelativeProjectPath, visualBasicMatch.Groups["form"].Value.Trim(), artifact.RelativePath, line.LineNumber, line.Text.Trim()));
                }
            }

            return candidates;
        }

        /// <summary>
        /// Resolves the startup form for a project from project metadata or source candidates.
        /// </summary>
        /// <param name="project">The project whose startup form is being resolved.</param>
        /// <param name="repositoryContext">The repository context containing startup candidates.</param>
        /// <returns>The startup form type name when one is statically available; otherwise, <see langword="null" />.</returns>
        private static string? ResolveStartupForm(WinFormsProjectContext project, WinFormsRepositoryContext repositoryContext)
        {
            // Project StartupObject can be fully qualified, while Application.Run uses a direct type name; both normalize to the final type segment.
            if (!string.IsNullOrWhiteSpace(project.StartupObject))
            {
                return project.StartupObject.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            }

            return repositoryContext.StartupCandidates.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.ProjectRelativePath, project.RelativeProjectPath))?.FormTypeName;
        }

        /// <summary>
        /// Extracts designer field declarations for known Windows Forms controls.
        /// </summary>
        /// <param name="content">The designer source content.</param>
        /// <param name="language">The source language used by the owning project.</param>
        /// <returns>Control type names keyed by control field names.</returns>
        private static IReadOnlyDictionary<string, string> ExtractDesignerControlFields(string content, string language)
        {
            // Field declarations provide stable control names even when InitializeComponent later assigns properties or adds hierarchy.
            Dictionary<string, string> controls = new(StringComparer.Ordinal);
            Regex regex = string.Equals(language, "Visual Basic", StringComparison.Ordinal) ? VbControlFieldRegex() : CSharpControlFieldRegex();
            foreach (Match match in regex.Matches(content))
            {
                string name = match.Groups["name"].Value.Trim();
                string type = NormalizeControlType(match.Groups["type"].Value);
                if (!IsInfrastructureControl(name, type))
                {
                    controls[name] = type;
                }
            }

            return controls;
        }

        /// <summary>
        /// Extracts static control hierarchy relationships from designer `Controls.Add` calls.
        /// </summary>
        /// <param name="content">The designer source content.</param>
        /// <param name="language">The source language used by the owning project.</param>
        /// <returns>Control hierarchy usages in source order.</returns>
        private static IReadOnlyList<ControlHierarchyUsage> ExtractControlHierarchyUsages(string content, string language)
        {
            // The parent control is the receiver of Controls.Add; the child is the argument added to that collection.
            List<ControlHierarchyUsage> usages = [];
            foreach (SourceLine line in SplitLines(content))
            {
                Regex regex = string.Equals(language, "Visual Basic", StringComparison.Ordinal) ? VbControlsAddRegex() : CSharpControlsAddRegex();
                foreach (Match match in regex.Matches(line.Text))
                {
                    usages.Add(new ControlHierarchyUsage(NormalizeReceiver(match.Groups["parent"].Value), NormalizeReceiver(match.Groups["child"].Value), line.LineNumber, line.Text.Trim()));
                }
            }

            return usages;
        }

        /// <summary>
        /// Extracts Windows Forms data-binding expressions from designer source.
        /// </summary>
        /// <param name="content">The designer source content.</param>
        /// <param name="language">The source language used by the owning project.</param>
        /// <returns>Binding usages in source order.</returns>
        private static IReadOnlyList<BindingUsage> ExtractBindingUsages(string content, string language)
        {
            // DataBindings.Add exposes a control property and binding path without requiring runtime binding-source evaluation.
            List<BindingUsage> bindings = [];
            Regex regex = string.Equals(language, "Visual Basic", StringComparison.Ordinal) ? VbDataBindingRegex() : CSharpDataBindingRegex();
            foreach (SourceLine line in SplitLines(content))
            {
                foreach (Match match in regex.Matches(line.Text))
                {
                    bindings.Add(new BindingUsage(NormalizeReceiver(match.Groups["control"].Value), match.Groups["property"].Value.Trim(), match.Groups["path"].Value.Trim(), line.LineNumber, line.Text.Trim()));
                }
            }

            return bindings;
        }

        /// <summary>
        /// Extracts dynamically created controls from designer assignment expressions where the assigned control does not have a direct field declaration.
        /// </summary>
        /// <param name="content">The designer source content.</param>
        /// <param name="language">The source language used by the owning project.</param>
        /// <returns>Dynamic control usages in source order.</returns>
        private static IReadOnlyList<DynamicControlUsage> ExtractDynamicControlUsages(string content, string language)
        {
            // Factory-assigned controls are useful unknowns because they identify UI structure that static field parsing cannot fully describe.
            List<DynamicControlUsage> usages = [];
            Regex regex = string.Equals(language, "Visual Basic", StringComparison.Ordinal) ? VbDynamicControlRegex() : CSharpDynamicControlRegex();
            foreach (SourceLine line in SplitLines(content))
            {
                foreach (Match match in regex.Matches(line.Text))
                {
                    string controlName = NormalizeReceiver(match.Groups["name"].Value);
                    usages.Add(new DynamicControlUsage(controlName, "Unknown", line.LineNumber, line.Text.Trim()));
                }
            }

            return usages;
        }

        /// <summary>
        /// Extracts code-behind event subscriptions from C# subscription syntax and VB Handles clauses.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <param name="language">The source language used by the owning project.</param>
        /// <returns>Event subscriptions in source order.</returns>
        private static IReadOnlyList<EventSubscription> ExtractCodeBehindEventSubscriptions(string content, string language)
        {
            // Event subscriptions produce command facts, while inline lambdas are treated as runtime-generated handlers with explicit unknown state.
            List<EventSubscription> subscriptions = [];
            foreach (SourceLine line in SplitLines(content))
            {
                if (string.Equals(language, "Visual Basic", StringComparison.Ordinal))
                {
                    Match match = VbHandlesEventRegex().Match(line.Text);
                    if (match.Success)
                    {
                        subscriptions.Add(new EventSubscription(match.Groups["control"].Value.Trim(), match.Groups["event"].Value.Trim(), match.Groups["handler"].Value.Trim(), false, line.LineNumber, line.Text.Trim()));
                    }

                    continue;
                }

                Match subscription = CSharpEventSubscriptionRegex().Match(line.Text);
                if (subscription.Success)
                {
                    string handler = NormalizeHandlerName(subscription.Groups["handler"].Value);
                    bool dynamic = IsDynamicHandler(handler);
                    subscriptions.Add(new EventSubscription(NormalizeReceiver(subscription.Groups["control"].Value), subscription.Groups["event"].Value.Trim(), dynamic ? "Unknown Event Handler" : handler, dynamic, line.LineNumber, line.Text.Trim()));
                }

                Match eventHandler = CSharpEventHandlerSubscriptionRegex().Match(line.Text);
                if (eventHandler.Success)
                {
                    subscriptions.Add(new EventSubscription(NormalizeReceiver(eventHandler.Groups["control"].Value), eventHandler.Groups["event"].Value.Trim(), eventHandler.Groups["handler"].Value.Trim(), false, line.LineNumber, line.Text.Trim()));
                }
            }

            return subscriptions;
        }

        /// <summary>
        /// Extracts direct code-behind service type usage from field declarations and constructor creation expressions.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <returns>Service usages in source order.</returns>
        private static IReadOnlyList<ServiceUsage> ExtractServiceUsages(string content)
        {
            // Service usage is intentionally simple and conservative: recognizable type names ending in Service become dependencies.
            List<ServiceUsage> usages = [];
            foreach (SourceLine line in SplitLines(content))
            {
                foreach (Match match in ServiceTypeUsageRegex().Matches(line.Text))
                {
                    usages.Add(new ServiceUsage(match.Groups["type"].Value.Trim(), line.LineNumber, line.Text.Trim()));
                }
            }

            return usages.DistinctBy(usage => usage.TypeName, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Extracts data-access usage from source namespaces/types and project package identities.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <param name="packageIdentities">The package identities from the owning project.</param>
        /// <returns>Data-access usages in source order.</returns>
        private static IReadOnlyList<DataAccessUsage> ExtractDataAccessUsages(string content, IReadOnlyList<string> packageIdentities)
        {
            // Data-access signals are correlated with known package identities when available, falling back to the namespace/type token visible in source.
            List<DataAccessUsage> usages = [];
            string? packageIdentity = packageIdentities.FirstOrDefault(package => package.Contains("SqlClient", StringComparison.OrdinalIgnoreCase)) ?? (content.Contains("SqlConnection", StringComparison.Ordinal) ? "System.Data.SqlClient" : null);
            if (packageIdentity is null)
            {
                return usages;
            }

            foreach (SourceLine line in SplitLines(content))
            {
                if (line.Text.Contains("SqlConnection", StringComparison.Ordinal) || line.Text.Contains("System.Data.SqlClient", StringComparison.Ordinal))
                {
                    usages.Add(new DataAccessUsage(packageIdentity, line.LineNumber, line.Text.Trim()));
                }
            }

            return usages.DistinctBy(usage => usage.PackageIdentity, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>
        /// Extracts repository service type names so later correlations can identify local service classes.
        /// </summary>
        /// <param name="content">The source content.</param>
        /// <returns>Service type names declared in the source content.</returns>
        private static IReadOnlyList<string> ExtractRepositoryServiceTypeNames(string content)
        {
            // The current slice records the service catalog for context even though direct service usage can be detected from code-behind alone.
            List<string> serviceTypes = [];
            foreach (Match match in ServiceClassRegex().Matches(content))
            {
                serviceTypes.Add(match.Groups["name"].Value.Trim());
            }

            return serviceTypes;
        }

        /// <summary>
        /// Extracts resource keys from `.resx` XML content.
        /// </summary>
        /// <param name="resourceContent">The `.resx` XML content.</param>
        /// <returns>Resource usages in document order.</returns>
        private static IReadOnlyList<ResourceUsage> ExtractResourceUsages(string resourceContent)
        {
            // XML parsing preserves resource names while source-line lookup keeps evidence close to the original `<data>` element.
            List<ResourceUsage> resources = [];
            try
            {
                XDocument document = XDocument.Parse(resourceContent, LoadOptions.PreserveWhitespace);
                foreach (XElement dataElement in document.Descendants().Where(element => string.Equals(element.Name.LocalName, "data", StringComparison.Ordinal)))
                {
                    string? name = dataElement.Attribute("name")?.Value;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    int lineNumber = FindLineNumber(resourceContent, name);
                    string sourceText = dataElement.ToString(SaveOptions.DisableFormatting);
                    resources.Add(new ResourceUsage(name.Trim(), lineNumber, sourceText));
                }
            }
            catch (Exception) when (IsXmlReadException())
            {
                // Malformed resources are ignored in this helper; callers still retain form facts and can add warnings in a future expansion.
            }

            return resources;
        }

        /// <summary>
        /// Creates a stable key for a designer control.
        /// </summary>
        /// <param name="designerArtifact">The designer artifact that owns the control.</param>
        /// <param name="controlName">The control name.</param>
        /// <returns>The deterministic control stable key.</returns>
        private static StableKey CreateControlStableKey(WinFormsArtifactContext designerArtifact, string controlName)
        {
            // Control identity is project and designer-path scoped because designer field names are unique only within an owning form/control.
            return UiStableKeyBuilder.Create("ui-control://", StableKeyGenerator.ForProject(designerArtifact.Project.RelativeProjectPath).Value, "WinForms", designerArtifact.RelativePath, designerArtifact.TypeName, controlName);
        }

        /// <summary>
        /// Finds a line number for a value in source text known by artifact context.
        /// </summary>
        /// <param name="sourceText">The source snippet or value to locate.</param>
        /// <param name="artifact">The artifact whose cached source should be searched when available.</param>
        /// <param name="needle">The value to locate.</param>
        /// <returns>The one-based line number when found; otherwise, one.</returns>
        private static int FindLineNumber(string sourceText, WinFormsArtifactContext artifact, string needle)
        {
            // A snippet-only source may already be one line; fallback to one keeps evidence valid even for synthetic unknown facts.
            if (sourceText.Contains('\n', StringComparison.Ordinal))
            {
                return FindLineNumber(sourceText, needle);
            }

            _ = artifact;
            return 1;
        }

        /// <summary>
        /// Finds a one-based line number for a value in source text.
        /// </summary>
        /// <param name="content">The source content to inspect.</param>
        /// <param name="needle">The value to locate.</param>
        /// <returns>The one-based line number when found; otherwise, one.</returns>
        private static int FindLineNumber(string content, string needle)
        {
            // Line lookup is best-effort and supports evidence navigation without affecting graph identity.
            foreach (SourceLine line in SplitLines(content))
            {
                if (line.Text.Contains(needle, StringComparison.Ordinal))
                {
                    return line.LineNumber;
                }
            }

            return 1;
        }

        /// <summary>
        /// Counts lines in source content using normalized line endings.
        /// </summary>
        /// <param name="content">The source content to count.</param>
        /// <returns>The number of logical lines, with a minimum of one.</returns>
        private static int CountLines(string content)
        {
            // Evidence line spans are one-based and should remain valid for empty files.
            return Math.Max(1, SplitLines(content).Length);
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
            // Multiple partial files can contribute to one Windows Forms type, so each type maps to a set of source paths.
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
        private static WinFormsProjectContext? FindNearestProject(IReadOnlyList<WinFormsProjectContext> projects, string artifactPath)
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
            // Excluding output folders prevents duplicate designer artifacts from `bin`/`obj` from destabilizing graph output.
            string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return !normalized.Contains(string.Concat(Path.DirectorySeparatorChar, "bin", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains(string.Concat(Path.DirectorySeparatorChar, "obj", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains(string.Concat(Path.DirectorySeparatorChar, ".git", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Normalizes a Windows Forms control type name to the final type segment.
        /// </summary>
        /// <param name="typeName">The raw control type token.</param>
        /// <returns>The normalized control type name.</returns>
        private static string NormalizeControlType(string typeName)
        {
            // Designer source usually uses fully qualified type names; graph metadata stores the short type for readability.
            return typeName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? typeName.Trim();
        }

        /// <summary>
        /// Normalizes a control receiver expression to its field/control name.
        /// </summary>
        /// <param name="receiver">The raw receiver expression.</param>
        /// <returns>The normalized receiver name.</returns>
        private static string NormalizeReceiver(string receiver)
        {
            // Designer source uses `this.` in C# and `Me.` in VB; both prefixes are implementation details rather than control names.
            return receiver.Trim().TrimEnd(';').Replace("this.", string.Empty, StringComparison.Ordinal).Replace("Me.", string.Empty, StringComparison.Ordinal);
        }

        /// <summary>
        /// Normalizes a C# event handler expression to a handler name.
        /// </summary>
        /// <param name="handlerExpression">The raw handler expression.</param>
        /// <returns>The normalized handler name or expression marker.</returns>
        private static string NormalizeHandlerName(string handlerExpression)
        {
            // Event handlers can be plain method names, method-group expressions, or lambdas; only method names are deterministic commands.
            return handlerExpression.Trim().Replace("this.", string.Empty, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a handler expression is computed or inline rather than a stable method name.
        /// </summary>
        /// <param name="handlerName">The normalized handler expression.</param>
        /// <returns><see langword="true" /> when the handler should be represented as unknown; otherwise, <see langword="false" />.</returns>
        private static bool IsDynamicHandler(string handlerName)
        {
            // Lambdas and anonymous delegates do not provide stable command names, so they become explicit unknowns.
            return handlerName.Contains("=>", StringComparison.Ordinal) || handlerName.Contains("delegate", StringComparison.OrdinalIgnoreCase) || handlerName.Contains("_", StringComparison.Ordinal) && handlerName.StartsWith("(", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a designer field is an infrastructure field rather than a UI control.
        /// </summary>
        /// <param name="name">The field name.</param>
        /// <param name="type">The normalized field type.</param>
        /// <returns><see langword="true" /> when the field should be excluded as infrastructure; otherwise, <see langword="false" />.</returns>
        private static bool IsInfrastructureControl(string name, string type)
        {
            // Component containers and binding sources are support infrastructure; the current slice tracks actual UI controls and explicit binding facts separately.
            return string.Equals(name, "components", StringComparison.OrdinalIgnoreCase)
                || type.Contains("IContainer", StringComparison.OrdinalIgnoreCase)
                || type.Contains("BindingSource", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the metadata artifact-kind value for a Windows Forms artifact kind.
        /// </summary>
        /// <param name="artifactKind">The Windows Forms artifact kind.</param>
        /// <returns>The UI artifact-kind metadata value.</returns>
        private static string GetArtifactKindMetadata(WinFormsArtifactKind artifactKind)
        {
            // Metadata uses shared WP011 artifact names rather than Windows Forms-specific graph node kinds.
            return artifactKind switch
            {
                WinFormsArtifactKind.Form => "View",
                WinFormsArtifactKind.UserControl => "Component",
                WinFormsArtifactKind.Designer => "Designer",
                WinFormsArtifactKind.Resource => "Resource",
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
        /// Creates a regex for C# Windows Forms type declarations.
        /// </summary>
        /// <returns>A regex that detects classes inheriting from Form.</returns>
        [GeneratedRegex("\\bclass\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*:\\s*(?:[A-Za-z0-9_\\.]+\\.)?Form\\b", RegexOptions.CultureInvariant)]
        private static partial Regex FormInheritanceRegex();

        /// <summary>
        /// Creates a regex for C# Windows Forms user-control type declarations.
        /// </summary>
        /// <returns>A regex that detects classes inheriting from UserControl.</returns>
        [GeneratedRegex("\\bclass\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*:\\s*(?:[A-Za-z0-9_\\.]+\\.)?UserControl\\b", RegexOptions.CultureInvariant)]
        private static partial Regex UserControlInheritanceRegex();

        /// <summary>
        /// Creates a regex for VB.NET Windows Forms type declarations.
        /// </summary>
        /// <returns>A regex that detects classes inheriting from Form.</returns>
        [GeneratedRegex("Class\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)[\\s\\S]*?Inherits\\s+(?:[A-Za-z0-9_\\.]+\\.)?Form\\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex VbFormInheritanceRegex();

        /// <summary>
        /// Creates a regex for VB.NET Windows Forms user-control type declarations.
        /// </summary>
        /// <returns>A regex that detects classes inheriting from UserControl.</returns>
        [GeneratedRegex("Class\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)[\\s\\S]*?Inherits\\s+(?:[A-Za-z0-9_\\.]+\\.)?UserControl\\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex VbUserControlInheritanceRegex();

        /// <summary>
        /// Creates a regex for C# or VB class declarations.
        /// </summary>
        /// <returns>A regex that captures class names.</returns>
        [GeneratedRegex("\\b(?:partial\\s+)?class\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex CSharpTypeRegex();

        /// <summary>
        /// Creates a regex for VB class declarations.
        /// </summary>
        /// <returns>A regex that captures VB class names.</returns>
        [GeneratedRegex("\\b(?:Partial\\s+)?Class\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex VbTypeRegex();

        /// <summary>
        /// Creates a regex for `Application.Run(new MainForm())` startup calls.
        /// </summary>
        /// <returns>A regex that captures startup form type names.</returns>
        [GeneratedRegex("Application\\.Run\\s*\\(\\s*new\\s+(?<form>[A-Za-z_][A-Za-z0-9_]*)\\s*\\(", RegexOptions.CultureInvariant)]
        private static partial Regex ApplicationRunRegex();

        /// <summary>
        /// Creates a regex for VB `Application.Run(New MainForm())` startup calls.
        /// </summary>
        /// <returns>A regex that captures startup form type names.</returns>
        [GeneratedRegex("Application\\.Run\\s*\\(\\s*New\\s+(?<form>[A-Za-z_][A-Za-z0-9_]*)\\s*\\(", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex VbApplicationRunRegex();

        /// <summary>
        /// Creates a regex for C# designer control field declarations.
        /// </summary>
        /// <returns>A regex that captures control field type and name.</returns>
        [GeneratedRegex("private\\s+(?<type>(?:System\\.Windows\\.Forms\\.)?[A-Za-z_][A-Za-z0-9_\\.]*)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*;", RegexOptions.CultureInvariant)]
        private static partial Regex CSharpControlFieldRegex();

        /// <summary>
        /// Creates a regex for VB designer control field declarations.
        /// </summary>
        /// <returns>A regex that captures control field type and name.</returns>
        [GeneratedRegex("Private\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s+As\\s+(?<type>(?:System\\.Windows\\.Forms\\.)?[A-Za-z_][A-Za-z0-9_\\.]*)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex VbControlFieldRegex();

        /// <summary>
        /// Creates a regex for C# `Controls.Add` calls.
        /// </summary>
        /// <returns>A regex that captures parent and child controls.</returns>
        [GeneratedRegex("(?<parent>(?:this|[A-Za-z_][A-Za-z0-9_]*)?)\\.Controls\\.Add\\s*\\(\\s*(?<child>(?:this\\.)?[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
        private static partial Regex CSharpControlsAddRegex();

        /// <summary>
        /// Creates a regex for VB `Controls.Add` calls.
        /// </summary>
        /// <returns>A regex that captures parent and child controls.</returns>
        [GeneratedRegex("(?<parent>(?:Me|[A-Za-z_][A-Za-z0-9_]*)?)\\.Controls\\.Add\\s*\\(\\s*(?<child>(?:Me\\.)?[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex VbControlsAddRegex();

        /// <summary>
        /// Creates a regex for C# data-binding expressions.
        /// </summary>
        /// <returns>A regex that captures control, property, and binding path.</returns>
        [GeneratedRegex("(?<control>(?:this\\.)?[A-Za-z_][A-Za-z0-9_]*)\\.DataBindings\\.Add\\s*\\(\\s*\"(?<property>[^\"]+)\"\\s*,[^,]+,\\s*\"(?<path>[^\"]+)\"", RegexOptions.CultureInvariant)]
        private static partial Regex CSharpDataBindingRegex();

        /// <summary>
        /// Creates a regex for VB data-binding expressions.
        /// </summary>
        /// <returns>A regex that captures control, property, and binding path.</returns>
        [GeneratedRegex("(?<control>(?:Me\\.)?[A-Za-z_][A-Za-z0-9_]*)\\.DataBindings\\.Add\\s*\\(\\s*\"(?<property>[^\"]+)\"\\s*,[^,]+,\\s*\"(?<path>[^\"]+)\"", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex VbDataBindingRegex();

        /// <summary>
        /// Creates a regex for C# factory-assigned designer controls.
        /// </summary>
        /// <returns>A regex that captures dynamically assigned control names.</returns>
        [GeneratedRegex("this\\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*(?!new\\s+System\\.Windows\\.Forms\\.)(?<factory>[A-Za-z_][A-Za-z0-9_]*)\\s*\\(", RegexOptions.CultureInvariant)]
        private static partial Regex CSharpDynamicControlRegex();

        /// <summary>
        /// Creates a regex for VB factory-assigned designer controls.
        /// </summary>
        /// <returns>A regex that captures dynamically assigned control names.</returns>
        [GeneratedRegex("Me\\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*(?!New\\s+System\\.Windows\\.Forms\\.)(?<factory>[A-Za-z_][A-Za-z0-9_]*)\\s*\\(", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex VbDynamicControlRegex();

        /// <summary>
        /// Creates a regex for C# direct event subscriptions.
        /// </summary>
        /// <returns>A regex that captures control, event, and handler expression.</returns>
        [GeneratedRegex("(?<control>(?:this\\.)?[A-Za-z_][A-Za-z0-9_]*)\\.(?<event>[A-Za-z_][A-Za-z0-9_]*)\\s*\\+=\\s*(?<handler>[^;]+)", RegexOptions.CultureInvariant)]
        private static partial Regex CSharpEventSubscriptionRegex();

        /// <summary>
        /// Creates a regex for C# `new EventHandler(this.Handler)` subscriptions.
        /// </summary>
        /// <returns>A regex that captures control, event, and handler method.</returns>
        [GeneratedRegex("(?<control>(?:this\\.)?[A-Za-z_][A-Za-z0-9_]*)\\.(?<event>[A-Za-z_][A-Za-z0-9_]*)\\s*\\+=\\s*new\\s+(?:System\\.)?EventHandler\\s*\\(\\s*(?:this\\.)?(?<handler>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
        private static partial Regex CSharpEventHandlerSubscriptionRegex();

        /// <summary>
        /// Creates a regex for VB Handles event clauses.
        /// </summary>
        /// <returns>A regex that captures handler, control, and event.</returns>
        [GeneratedRegex("Sub\\s+(?<handler>[A-Za-z_][A-Za-z0-9_]*)\\s*\\([^)]*\\)\\s+Handles\\s+(?<control>[A-Za-z_][A-Za-z0-9_]*)\\.(?<event>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex VbHandlesEventRegex();

        /// <summary>
        /// Creates a regex for service type usage.
        /// </summary>
        /// <returns>A regex that captures type names ending in Service.</returns>
        [GeneratedRegex("\\b(?:new\\s+|readonly\\s+|private\\s+readonly\\s+|Private\\s+)?(?<type>[A-Za-z_][A-Za-z0-9_]*Service)\\b", RegexOptions.CultureInvariant)]
        private static partial Regex ServiceTypeUsageRegex();

        /// <summary>
        /// Creates a regex for service class declarations.
        /// </summary>
        /// <returns>A regex that captures declared service type names.</returns>
        [GeneratedRegex("\\bclass\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*Service)\\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex ServiceClassRegex();

        /// <summary>
        /// Describes one discovered Windows Forms-capable project.
        /// </summary>
        /// <param name="AbsoluteProjectPath">The absolute project path used for artifact ownership checks.</param>
        /// <param name="RelativeProjectPath">The repository-relative project path used for stable keys.</param>
        /// <param name="ProjectName">The display name of the project.</param>
        /// <param name="TargetFramework">The target framework value read from project metadata.</param>
        /// <param name="Language">The project source language.</param>
        /// <param name="StartupObject">The optional startup object value read from project metadata.</param>
        /// <param name="PackageIdentities">The package identities declared by the project.</param>
        private sealed record WinFormsProjectContext(string AbsoluteProjectPath, string RelativeProjectPath, string ProjectName, string TargetFramework, string Language, string? StartupObject, IReadOnlyList<string> PackageIdentities);

        /// <summary>
        /// Describes normalized project metadata read from a project file.
        /// </summary>
        /// <param name="RelativeProjectPath">The repository-relative project path.</param>
        /// <param name="ProjectName">The project display name.</param>
        /// <param name="TargetFramework">The target framework value or Unknown.</param>
        /// <param name="Language">The project source language.</param>
        /// <param name="StartupObject">The optional startup object value.</param>
        /// <param name="PackageIdentities">The package identities declared by the project.</param>
        /// <param name="IsWinFormsCandidate">Whether the project contains Windows Forms evidence.</param>
        private sealed record ProjectMetadata(string RelativeProjectPath, string ProjectName, string TargetFramework, string Language, string? StartupObject, IReadOnlyList<string> PackageIdentities, bool IsWinFormsCandidate);

        /// <summary>
        /// Describes one discovered Windows Forms artifact and its owning project.
        /// </summary>
        /// <param name="Project">The project that owns the artifact.</param>
        /// <param name="AbsolutePath">The absolute artifact path used for file reads.</param>
        /// <param name="RelativePath">The repository-relative artifact path used for evidence and stable keys.</param>
        /// <param name="TypeName">The source type name associated with the artifact.</param>
        /// <param name="ArtifactKind">The coarse Windows Forms artifact classification.</param>
        private sealed record WinFormsArtifactContext(WinFormsProjectContext Project, string AbsolutePath, string RelativePath, string TypeName, WinFormsArtifactKind ArtifactKind);

        /// <summary>
        /// Describes repository-wide Windows Forms context used during per-artifact analysis.
        /// </summary>
        /// <param name="RepositoryRootDirectory">The accepted repository root directory.</param>
        /// <param name="SourceByPath">Source content keyed by repository-relative path.</param>
        /// <param name="DesignerByType">Designer artifacts keyed by owning type name.</param>
        /// <param name="ResourceByType">Resource artifacts keyed by owning type name.</param>
        /// <param name="SourcePathsByType">Source paths keyed by type name.</param>
        /// <param name="ServiceTypeNames">Repository-local service type names.</param>
        /// <param name="StartupCandidates">Startup form candidates discovered from source.</param>
        /// <param name="EventsByType">Event subscriptions keyed by form or user-control type.</param>
        /// <param name="ServiceUsagesByType">Service usages keyed by form or user-control type.</param>
        /// <param name="DataAccessUsagesByType">Data-access usages keyed by form or user-control type.</param>
        private sealed record WinFormsRepositoryContext(string RepositoryRootDirectory, IReadOnlyDictionary<string, string> SourceByPath, IReadOnlyDictionary<string, WinFormsArtifactContext> DesignerByType, IReadOnlyDictionary<string, WinFormsArtifactContext> ResourceByType, IReadOnlyDictionary<string, HashSet<string>> SourcePathsByType, IReadOnlySet<string> ServiceTypeNames, IReadOnlyList<StartupCandidate> StartupCandidates, IReadOnlyDictionary<string, IReadOnlyList<EventSubscription>> EventsByType, IReadOnlyDictionary<string, IReadOnlyList<ServiceUsage>> ServiceUsagesByType, IReadOnlyDictionary<string, IReadOnlyList<DataAccessUsage>> DataAccessUsagesByType);

        /// <summary>
        /// Describes one source line with its original one-based line number.
        /// </summary>
        /// <param name="LineNumber">The one-based line number.</param>
        /// <param name="Text">The source line text.</param>
        private sealed record SourceLine(int LineNumber, string Text);

        /// <summary>
        /// Describes a statically visible startup form candidate.
        /// </summary>
        /// <param name="ProjectRelativePath">The project path that owns the candidate.</param>
        /// <param name="FormTypeName">The startup form type name.</param>
        /// <param name="RelativePath">The repository-relative source path.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record StartupCandidate(string ProjectRelativePath, string FormTypeName, string RelativePath, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a designer control hierarchy observation.
        /// </summary>
        /// <param name="ParentControlName">The parent form or control name.</param>
        /// <param name="ChildControlName">The child control name.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record ControlHierarchyUsage(string ParentControlName, string ChildControlName, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a designer data-binding observation.
        /// </summary>
        /// <param name="ControlName">The bound control name.</param>
        /// <param name="ControlProperty">The control property being bound.</param>
        /// <param name="BindingPath">The binding path visible in source.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record BindingUsage(string ControlName, string ControlProperty, string BindingPath, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a dynamically created designer control observation.
        /// </summary>
        /// <param name="ControlName">The assigned control name.</param>
        /// <param name="ControlType">The statically known control type, or Unknown.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record DynamicControlUsage(string ControlName, string ControlType, int LineNumber, string SourceText);

        /// <summary>
        /// Describes a Windows Forms event subscription.
        /// </summary>
        /// <param name="ControlName">The control raising the event.</param>
        /// <param name="EventName">The event name.</param>
        /// <param name="HandlerName">The handler method or unknown handler marker.</param>
        /// <param name="IsDynamic">Whether the handler is runtime-generated or otherwise unresolved.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record EventSubscription(string ControlName, string EventName, string HandlerName, bool IsDynamic, int LineNumber, string SourceText);

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
        /// Describes a `.resx` resource key observation.
        /// </summary>
        /// <param name="ResourceName">The resource key.</param>
        /// <param name="LineNumber">The one-based source line number.</param>
        /// <param name="SourceText">The source text used for evidence.</param>
        private sealed record ResourceUsage(string ResourceName, int LineNumber, string SourceText);

        /// <summary>
        /// Describes the coarse category of a Windows Forms artifact.
        /// </summary>
        private enum WinFormsArtifactKind
        {
            /// <summary>
            /// A source file declaring a Windows Forms form.
            /// </summary>
            Form,

            /// <summary>
            /// A source file declaring a Windows Forms user control.
            /// </summary>
            UserControl,

            /// <summary>
            /// A designer partial source file.
            /// </summary>
            Designer,

            /// <summary>
            /// A `.resx` resource file.
            /// </summary>
            Resource,

            /// <summary>
            /// An unsupported artifact.
            /// </summary>
            Other
        }
    }
}
