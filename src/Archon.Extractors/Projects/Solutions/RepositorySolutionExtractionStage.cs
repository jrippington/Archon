using Archon.Application.Extraction.Pipeline;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Extractors.Projects.Packages;
using Archon.Extractors.Projects.Projects;

namespace Archon.Extractors.Projects.Solutions
{
    /// <summary>
    /// Contributes repository and submitted-solution graph facts for the project extraction slice.
    /// </summary>
    public sealed class RepositorySolutionExtractionStage : IExtractionStage
    {
        /// <summary>
        /// Stores the lightweight solution parser used to capture submitted solution file evidence.
        /// </summary>
        private readonly SolutionFileParser _solutionFileParser;

        /// <summary>
        /// Stores the deterministic project metadata extractor used for supported C# and VB.NET project files.
        /// </summary>
        private readonly ProjectMetadataExtractor _projectMetadataExtractor;

        /// <summary>
        /// Initializes a new instance of the <see cref="RepositorySolutionExtractionStage" /> class.
        /// </summary>
        public RepositorySolutionExtractionStage()
        {
            // The default constructor keeps dependency-injection registration simple while isolating file parsing in a dedicated collaborator.
            _solutionFileParser = new SolutionFileParser();
            _projectMetadataExtractor = new ProjectMetadataExtractor();
        }

        /// <summary>
        /// Gets the stable stage identifier used by pipeline ordering, progress, and diagnostics.
        /// </summary>
        public string StageId => "project-repository-solution";

        /// <summary>
        /// Executes repository and submitted-solution extraction against the resolved repository input and shared accumulator.
        /// </summary>
        /// <param name="context">The stage context containing validated input, accepted run state, and shared accumulation state.</param>
        /// <param name="cancellationToken">The cancellation token that stops stage execution before or during file reads.</param>
        /// <returns>A successful stage result when every submitted solution is parsed, or a controlled blocking error for unusable submitted solution content.</returns>
        public async Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // The stage is deliberately narrow: it reads only submitted solution paths and contributes graph facts through the shared accumulator.
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            StableKey repositoryStableKey = CreateRepositoryStableKey(context.ResolvedInput.RepositoryRootDirectory);
            StableKey snapshotStableKey = CreateSnapshotStableKey(repositoryStableKey, context.Run.RunId.ToString());
            string repositoryName = GetRepositoryName(context.ResolvedInput.RepositoryRootDirectory);
            GraphMetadata repositoryMetadata = CreateRepositoryMetadata(context.ResolvedInput.BranchName, context.ResolvedInput.CommitSha, context.ResolvedInput.RequestedBy, context.ResolvedInput.Metadata);
            StableKey repositoryEvidenceStableKey = CreateEvidenceStableKey(snapshotStableKey, "repository", repositoryStableKey.Value);

            List<(string AbsolutePath, string RelativePath, SolutionFileFacts Facts)> parsedSolutions = [];
            Dictionary<string, (ProjectLanguage Language, string RelativeProjectPath, ProjectMetadata Metadata)> extractedProjectsByPath = new(StringComparer.OrdinalIgnoreCase);
            List<(string SolutionRelativePath, SolutionProjectDeclaration Declaration, ProjectLanguage Language, string RelativeProjectPath, ProjectMetadata Metadata)> submittedProjectMemberships = [];
            List<(string SolutionRelativePath, SolutionProjectDeclaration Declaration)> unsupportedDeclarations = [];

            foreach (string solutionPath in context.ResolvedInput.SolutionPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativeSolutionPath = GetRepositoryRelativePath(context.ResolvedInput.RepositoryRootDirectory, solutionPath);

                try
                {
                    SolutionFileFacts solutionFacts = await _solutionFileParser.ParseAsync(solutionPath, cancellationToken).ConfigureAwait(false);
                    parsedSolutions.Add((solutionPath, relativeSolutionPath, solutionFacts));

                    foreach (SolutionProjectDeclaration declaration in solutionFacts.ProjectDeclarations)
                    {
                        if (!ProjectDeclarationClassifier.TryClassify(declaration, out ProjectLanguage language))
                        {
                            unsupportedDeclarations.Add((relativeSolutionPath, declaration));
                            continue;
                        }

                        string absoluteProjectPath = ResolveDeclaredProjectPath(Path.GetDirectoryName(solutionPath)!, declaration.DeclaredPath);
                        string relativeProjectPath = GetRepositoryRelativePath(context.ResolvedInput.RepositoryRootDirectory, absoluteProjectPath);
                        ProjectMetadata projectMetadata = await _projectMetadataExtractor.ExtractAsync(absoluteProjectPath, context.ResolvedInput.RepositoryRootDirectory, relativeProjectPath, declaration.Name, language, cancellationToken).ConfigureAwait(false);
                        extractedProjectsByPath.TryAdd(relativeProjectPath, (language, relativeProjectPath, projectMetadata));
                        submittedProjectMemberships.Add((relativeSolutionPath, declaration, language, relativeProjectPath, projectMetadata));
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    // The returned message is intentionally path-free and exception-type-free so run status remains credential-safe.
                    return ExtractionStageResult.BlockingError("A submitted solution file could not be read as a valid Visual Studio solution. Review server logs for details.");
                }
            }

            if (submittedProjectMemberships.Count == 0 && unsupportedDeclarations.Count > 0)
            {
                return ExtractionStageResult.BlockingError("No supported C# or VB.NET projects could be extracted from the submitted solution files.");
            }

            await ExtractRepositoryContainedReferenceTargetsAsync(context.ResolvedInput.RepositoryRootDirectory, extractedProjectsByPath, cancellationToken).ConfigureAwait(false);

            context.Accumulation.AddRepository(new RepositoryModel(
                repositoryStableKey,
                repositoryName,
                context.ResolvedInput.RepositoryRootDirectory,
                remoteUrl: null,
                defaultBranch: context.ResolvedInput.BranchName,
                repositoryMetadata));
            context.Accumulation.AddNode(CreateRepositoryNode(snapshotStableKey, repositoryStableKey, repositoryName, repositoryEvidenceStableKey, repositoryMetadata));
            context.Accumulation.AddEvidence(CreateRepositoryEvidence(snapshotStableKey, repositoryEvidenceStableKey, repositoryMetadata));

            foreach ((string solutionPath, string relativeSolutionPath, SolutionFileFacts solutionFacts) in parsedSolutions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                StableKey solutionStableKey = StableKeyGenerator.ForSolution(relativeSolutionPath);
                StableKey solutionEvidenceStableKey = CreateEvidenceStableKey(snapshotStableKey, "solution", solutionStableKey.Value);
                GraphMetadata solutionMetadata = CreateSolutionMetadata(relativeSolutionPath, solutionFacts);
                context.Accumulation.AddSolution(new SolutionModel(
                    repositoryStableKey,
                    solutionStableKey,
                    Path.GetFileName(solutionPath),
                    RepositoryRelativePath.Parse(relativeSolutionPath),
                    solutionMetadata));
                context.Accumulation.AddNode(CreateSolutionNode(snapshotStableKey, solutionStableKey, Path.GetFileName(solutionPath), relativeSolutionPath, solutionEvidenceStableKey, solutionMetadata));
                context.Accumulation.AddEvidence(CreateSolutionFileEvidence(snapshotStableKey, solutionEvidenceStableKey, relativeSolutionPath, solutionFacts.LineCount, solutionMetadata));
                context.Accumulation.AddEdge(CreateContainsEdge(snapshotStableKey, repositoryStableKey, solutionStableKey, solutionEvidenceStableKey, relativeSolutionPath));

                foreach (SolutionProjectDeclaration declaration in solutionFacts.ProjectDeclarations)
                {
                    // The declaration evidence supports both unsupported warnings and supported solution-to-project membership facts.
                    StableKey declarationEvidenceStableKey = CreateEvidenceStableKey(snapshotStableKey, "solution-project", string.Concat(solutionStableKey.Value, ":", declaration.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    context.Accumulation.AddEvidence(CreateProjectDeclarationEvidence(snapshotStableKey, declarationEvidenceStableKey, relativeSolutionPath, declaration));
                }

                foreach ((string _, SolutionProjectDeclaration declaration, ProjectLanguage _, string relativeProjectPath, ProjectMetadata metadata) in submittedProjectMemberships.Where(project => string.Equals(project.SolutionRelativePath, relativeSolutionPath, StringComparison.Ordinal)))
                {
                    StableKey projectStableKey = CreateProjectStableKey(relativeProjectPath);
                    StableKey projectEvidenceStableKey = CreateEvidenceStableKey(snapshotStableKey, "project", projectStableKey.Value);
                    context.Accumulation.AddNode(CreateProjectNode(snapshotStableKey, projectStableKey, metadata, projectEvidenceStableKey));
                    context.Accumulation.AddEvidence(CreateProjectFileEvidence(snapshotStableKey, projectEvidenceStableKey, relativeProjectPath, declaration.Name, metadata));
                    context.Accumulation.AddEdge(CreateSolutionProjectContainsEdge(snapshotStableKey, solutionStableKey, projectStableKey, projectEvidenceStableKey, relativeProjectPath));
                }

                foreach ((string _, SolutionProjectDeclaration declaration) in unsupportedDeclarations.Where(project => string.Equals(project.SolutionRelativePath, relativeSolutionPath, StringComparison.Ordinal)))
                {
                    context.Accumulation.AddWarning($"Unsupported project declaration '{declaration.Name}' at '{declaration.DeclaredPath}' was recorded as evidence but was not extracted because it is not a C# or VB.NET project.");
                }
            }

            ContributeFilePathNodes(context, snapshotStableKey, parsedSolutions.Select(solution => new ProjectArtifactDeclaration(solution.RelativePath, "SolutionFile", null)).Concat(extractedProjectsByPath.Values.SelectMany(project => project.Metadata.Artifacts)));

            foreach ((ProjectLanguage _, string relativeProjectPath, ProjectMetadata metadata) in extractedProjectsByPath.Values)
            {
                // Repository-contained referenced projects may not be declared by a submitted solution, so they are contributed after membership edges.
                StableKey projectStableKey = CreateProjectStableKey(relativeProjectPath);
                StableKey projectEvidenceStableKey = CreateEvidenceStableKey(snapshotStableKey, "project", projectStableKey.Value);
                context.Accumulation.AddNode(CreateProjectNode(snapshotStableKey, projectStableKey, metadata, projectEvidenceStableKey));
                context.Accumulation.AddEvidence(CreateProjectFileEvidence(snapshotStableKey, projectEvidenceStableKey, relativeProjectPath, metadata.ProjectName, metadata));
            }

            ContributeProjectReferenceFacts(context, snapshotStableKey, extractedProjectsByPath.Values.Select(project => project.Metadata));
            ContributeAnalyzerReferenceFacts(context, snapshotStableKey, extractedProjectsByPath.Values.Select(project => project.Metadata));
            ContributePackageDiagnostics(context, snapshotStableKey, extractedProjectsByPath.Values.Select(project => project.Metadata));
            ContributePackageReferenceFacts(context, snapshotStableKey, extractedProjectsByPath.Values.Select(project => project.Metadata));

            return ExtractionStageResult.Success();
        }

        /// <summary>
        /// Contributes deterministic FilePath nodes for repository-contained artifacts that support extracted facts.
        /// </summary>
        /// <param name="context">The stage context containing shared accumulation state.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes FilePath nodes.</param>
        /// <param name="artifacts">The artifact declarations to represent as FilePath nodes.</param>
        private static void ContributeFilePathNodes(ExtractionStageContext context, StableKey snapshotStableKey, IEnumerable<ProjectArtifactDeclaration> artifacts)
        {
            // FilePath nodes make source artifacts queryable, while evidence records remain the precise explanation anchors for individual facts.
            foreach (ProjectArtifactDeclaration artifact in artifacts.GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).Select(group => group.OrderBy(item => item.ArtifactKind, StringComparer.Ordinal).First()).OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                context.Accumulation.AddNode(CreateFilePathNode(snapshotStableKey, artifact));
            }
        }

        /// <summary>
        /// Contributes analyzer-reference evidence and warnings for all extracted projects.
        /// </summary>
        /// <param name="context">The stage context containing shared accumulation state.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes analyzer evidence.</param>
        /// <param name="projectMetadata">The extracted project metadata values to inspect for analyzer references.</param>
        private static void ContributeAnalyzerReferenceFacts(ExtractionStageContext context, StableKey snapshotStableKey, IEnumerable<ProjectMetadata> projectMetadata)
        {
            // Analyzer references are currently represented as project metadata plus evidence because the existing graph vocabulary has no analyzer node kind.
            foreach (ProjectMetadata metadata in projectMetadata)
            {
                foreach (AnalyzerReferenceDeclaration analyzerReference in metadata.AnalyzerReferences)
                {
                    StableKey evidenceStableKey = CreateAnalyzerReferenceEvidenceStableKey(snapshotStableKey, analyzerReference);
                    context.Accumulation.AddEvidence(CreateAnalyzerReferenceEvidence(snapshotStableKey, evidenceStableKey, analyzerReference));

                    if (!analyzerReference.IsRepositoryContained)
                    {
                        context.Accumulation.AddWarning($"Analyzer reference '{analyzerReference.DeclaredInclude}' declared by '{analyzerReference.DeclaringProjectRelativePath}' could not be resolved inside the submitted repository.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(analyzerReference.ResolvedRelativePath))
                    {
                        string absoluteAnalyzerPath = Path.GetFullPath(Path.Combine(context.ResolvedInput.RepositoryRootDirectory, analyzerReference.ResolvedRelativePath.Replace('/', Path.DirectorySeparatorChar)));

                        if (!File.Exists(absoluteAnalyzerPath))
                        {
                            context.Accumulation.AddWarning($"Analyzer reference '{analyzerReference.DeclaredInclude}' declared by '{analyzerReference.DeclaringProjectRelativePath}' points to a repository-contained analyzer file that does not exist.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Contributes controlled package extraction diagnostics and diagnostic evidence for extracted projects.
        /// </summary>
        /// <param name="context">The stage context containing shared accumulation state.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes contributed evidence.</param>
        /// <param name="projectMetadata">The extracted project metadata values to inspect for package diagnostics.</param>
        private static void ContributePackageDiagnostics(ExtractionStageContext context, StableKey snapshotStableKey, IEnumerable<ProjectMetadata> projectMetadata)
        {
            // Package diagnostics are non-blocking warnings because a malformed packages.config should not hide valid project metadata.
            foreach (ProjectMetadata metadata in projectMetadata)
            {
                foreach (PackageExtractionDiagnostic diagnostic in metadata.PackageDiagnostics)
                {
                    context.Accumulation.AddWarning(diagnostic.Message);
                    StableKey evidenceStableKey = CreatePackageDiagnosticEvidenceStableKey(snapshotStableKey, metadata.RelativeProjectPath, diagnostic);
                    context.Accumulation.AddEvidence(CreatePackageDiagnosticEvidence(snapshotStableKey, evidenceStableKey, metadata.RelativeProjectPath, diagnostic));
                }
            }
        }

        /// <summary>
        /// Extracts metadata for repository-contained project-reference targets that were not declared by any submitted solution.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory used to resolve target files.</param>
        /// <param name="extractedProjectsByPath">The current project metadata map keyed by repository-relative project path.</param>
        /// <param name="cancellationToken">The cancellation token that stops recursive reference-target extraction.</param>
        /// <returns>A task that completes after reachable repository-contained project-reference targets have been inspected.</returns>
        private async Task ExtractRepositoryContainedReferenceTargetsAsync(string repositoryRootDirectory, Dictionary<string, (ProjectLanguage Language, string RelativeProjectPath, ProjectMetadata Metadata)> extractedProjectsByPath, CancellationToken cancellationToken)
        {
            // A queue lets the stage discover transitive repository-contained project references without scanning unrelated project files.
            Queue<ProjectReferenceDeclaration> pendingReferences = new(extractedProjectsByPath.Values.SelectMany(project => project.Metadata.ProjectReferences));

            while (pendingReferences.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProjectReferenceDeclaration reference = pendingReferences.Dequeue();

                if (!reference.IsRepositoryContained || string.IsNullOrWhiteSpace(reference.ResolvedRelativePath) || extractedProjectsByPath.ContainsKey(reference.ResolvedRelativePath))
                {
                    continue;
                }

                string absoluteProjectPath = Path.GetFullPath(Path.Combine(repositoryRootDirectory, reference.ResolvedRelativePath.Replace('/', Path.DirectorySeparatorChar)));

                if (!File.Exists(absoluteProjectPath) || !TryInferProjectLanguage(absoluteProjectPath, out ProjectLanguage language))
                {
                    continue;
                }

                ProjectMetadata metadata = await _projectMetadataExtractor.ExtractAsync(
                    absoluteProjectPath,
                    repositoryRootDirectory,
                    reference.ResolvedRelativePath,
                    Path.GetFileNameWithoutExtension(absoluteProjectPath),
                    language,
                    cancellationToken).ConfigureAwait(false);
                extractedProjectsByPath.Add(reference.ResolvedRelativePath, (language, reference.ResolvedRelativePath, metadata));

                foreach (ProjectReferenceDeclaration nestedReference in metadata.ProjectReferences)
                {
                    pendingReferences.Enqueue(nestedReference);
                }
            }
        }

        /// <summary>
        /// Contributes package nodes, package-use evidence, and `USES_PACKAGE` edges for all extracted project package references.
        /// </summary>
        /// <param name="context">The stage context containing shared accumulation state.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes contributed package facts.</param>
        /// <param name="projectMetadata">The extracted project metadata values to inspect for package references.</param>
        private static void ContributePackageReferenceFacts(ExtractionStageContext context, StableKey snapshotStableKey, IEnumerable<ProjectMetadata> projectMetadata)
        {
            // Stable-key sets deduplicate package nodes and package-use edges when repeated PackageReference items declare the same dependency.
            HashSet<string> contributedPackageNodeKeys = new(StringComparer.Ordinal);
            HashSet<string> contributedPackageEdgeKeys = new(StringComparer.Ordinal);

            foreach (ProjectMetadata metadata in projectMetadata)
            {
                StableKey sourceProjectStableKey = CreateProjectStableKey(metadata.RelativeProjectPath);

                foreach (PackageReferenceDeclaration packageReference in metadata.PackageReferences)
                {
                    StableKey packageStableKey = CreatePackageStableKey(packageReference);
                    StableKey packageEvidenceStableKey = CreatePackageReferenceEvidenceStableKey(snapshotStableKey, packageReference);

                    if (contributedPackageNodeKeys.Add(packageStableKey.Value))
                    {
                        context.Accumulation.AddNode(CreatePackageNode(snapshotStableKey, packageStableKey, packageReference, packageEvidenceStableKey));
                    }

                    context.Accumulation.AddEvidence(CreatePackageReferenceEvidence(snapshotStableKey, packageEvidenceStableKey, packageReference));

                    ArchitectureEdge packageUseEdge = CreatePackageUseEdge(snapshotStableKey, sourceProjectStableKey, packageStableKey, packageEvidenceStableKey, packageReference);

                    if (contributedPackageEdgeKeys.Add(packageUseEdge.StableKey.Value))
                    {
                        context.Accumulation.AddEdge(packageUseEdge);
                    }
                }
            }
        }

        /// <summary>
        /// Contributes project-reference evidence, warnings, and `REFERENCES` edges for all extracted projects.
        /// </summary>
        /// <param name="context">The stage context containing shared accumulation state.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes contributed facts.</param>
        /// <param name="projectMetadata">The extracted project metadata values to inspect for project references.</param>
        private static void ContributeProjectReferenceFacts(ExtractionStageContext context, StableKey snapshotStableKey, IEnumerable<ProjectMetadata> projectMetadata)
        {
            // Edge stable keys deduplicate repeated ProjectReference items while preserving one evidence record per declaration line.
            HashSet<string> contributedEdgeKeys = new(StringComparer.Ordinal);

            foreach (ProjectMetadata metadata in projectMetadata)
            {
                StableKey sourceProjectStableKey = CreateProjectStableKey(metadata.RelativeProjectPath);

                foreach (ProjectReferenceDeclaration reference in metadata.ProjectReferences)
                {
                    StableKey referenceEvidenceStableKey = CreateProjectReferenceEvidenceStableKey(snapshotStableKey, reference);
                    context.Accumulation.AddEvidence(CreateProjectReferenceEvidence(snapshotStableKey, referenceEvidenceStableKey, reference));

                    if (!reference.IsRepositoryContained || string.IsNullOrWhiteSpace(reference.ResolvedRelativePath))
                    {
                        context.Accumulation.AddWarning($"Project reference '{reference.DeclaredInclude}' declared by '{reference.DeclaringProjectRelativePath}' could not be resolved inside the submitted repository.");
                        continue;
                    }

                    string absoluteReferencedPath = Path.GetFullPath(Path.Combine(context.ResolvedInput.RepositoryRootDirectory, reference.ResolvedRelativePath.Replace('/', Path.DirectorySeparatorChar)));

                    if (!File.Exists(absoluteReferencedPath))
                    {
                        context.Accumulation.AddWarning($"Project reference '{reference.DeclaredInclude}' declared by '{reference.DeclaringProjectRelativePath}' points to a repository-contained project file that does not exist.");
                        continue;
                    }

                    StableKey targetProjectStableKey = CreateProjectStableKey(reference.ResolvedRelativePath);
                    ArchitectureEdge referenceEdge = CreateProjectReferenceEdge(snapshotStableKey, sourceProjectStableKey, targetProjectStableKey, referenceEvidenceStableKey, reference);

                    if (contributedEdgeKeys.Add(referenceEdge.StableKey.Value))
                    {
                        context.Accumulation.AddEdge(referenceEdge);
                    }
                }
            }
        }

        /// <summary>
        /// Creates a project architecture node from deterministic project metadata.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the node.</param>
        /// <param name="projectStableKey">The repository-relative stable key for the project.</param>
        /// <param name="metadata">The extracted project metadata.</param>
        /// <param name="primaryEvidenceStableKey">The project-file evidence stable key that explains the node.</param>
        /// <returns>An architecture node representing one supported C# or VB.NET project file.</returns>
        private static ArchitectureNode CreateProjectNode(StableKey snapshotStableKey, StableKey projectStableKey, ProjectMetadata metadata, StableKey primaryEvidenceStableKey)
        {
            // Project nodes use repository-relative paths as qualified/search names so same-named projects in different folders remain distinct.
            GraphMetadata graphMetadata = metadata.ToGraphMetadata();
            string language = ProjectMetadata.ToLanguageDisplayName(metadata.Language);
            return new ArchitectureNode(
                snapshotStableKey,
                projectStableKey,
                NodeKind.Project,
                metadata.ProjectName,
                qualifiedName: metadata.RelativeProjectPath,
                searchName: metadata.RelativeProjectPath,
                language,
                projectStableKey,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey,
                graphMetadata,
                FingerprintGenerator.ForNode(NodeKind.Project, metadata.ProjectName, metadata.RelativeProjectPath, metadata.RelativeProjectPath, KnowledgeKind.Fact, graphMetadata));
        }

        /// <summary>
        /// Creates a package architecture node from one package reference declaration.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the node.</param>
        /// <param name="packageStableKey">The normalized stable key for the package identity and version state.</param>
        /// <param name="packageReference">The package reference that supports the node.</param>
        /// <param name="primaryEvidenceStableKey">The evidence stable key that explains the package node.</param>
        /// <returns>An architecture node representing one NuGet package dependency identity.</returns>
        private static ArchitectureNode CreatePackageNode(StableKey snapshotStableKey, StableKey packageStableKey, PackageReferenceDeclaration packageReference, StableKey primaryEvidenceStableKey)
        {
            // Package nodes represent external NuGet dependencies and keep project-specific asset metadata on USES_PACKAGE edges.
            GraphMetadata metadata = packageReference.ToPackageNodeMetadata();
            string displayName = string.IsNullOrWhiteSpace(packageReference.ResolvedVersion)
                ? packageReference.PackageId
                : string.Concat(packageReference.PackageId, " ", packageReference.ResolvedVersion);
            return new ArchitectureNode(
                snapshotStableKey,
                packageStableKey,
                NodeKind.Package,
                displayName,
                qualifiedName: packageReference.NormalizedPackageId,
                searchName: packageReference.NormalizedPackageId,
                language: null,
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: "NuGet",
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey,
                metadata,
                FingerprintGenerator.ForNode(NodeKind.Package, displayName, packageReference.NormalizedPackageId, packageReference.NormalizedPackageId, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a FilePath architecture node from a repository-contained source artifact declaration.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the node.</param>
        /// <param name="artifact">The source artifact declaration to represent.</param>
        /// <returns>An architecture node representing one repository-relative artifact path.</returns>
        private static ArchitectureNode CreateFilePathNode(StableKey snapshotStableKey, ProjectArtifactDeclaration artifact)
        {
            // FilePath nodes are artifact identities, so the repository-relative path is the display, qualified, and search name.
            StableKey fileStableKey = StableKeyGenerator.ForFile(artifact.RelativePath);
            GraphMetadata metadata = artifact.ToGraphMetadata();
            return new ArchitectureNode(
                snapshotStableKey,
                fileStableKey,
                NodeKind.FilePath,
                artifact.RelativePath,
                qualifiedName: artifact.RelativePath,
                searchName: artifact.RelativePath,
                language: null,
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey: null,
                metadata,
                FingerprintGenerator.ForNode(NodeKind.FilePath, artifact.RelativePath, artifact.RelativePath, artifact.RelativePath, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates project-file evidence for a supported project node and its extracted metadata.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="evidenceStableKey">The stable key for this evidence record.</param>
        /// <param name="relativeProjectPath">The repository-relative project path.</param>
        /// <param name="projectName">The project display name declared by the solution.</param>
        /// <param name="metadata">The extracted project metadata used for evidence metadata.</param>
        /// <returns>An evidence record representing the project file that supported the project node.</returns>
        private static EvidenceRecord CreateProjectFileEvidence(StableKey snapshotStableKey, StableKey evidenceStableKey, string relativeProjectPath, string projectName, ProjectMetadata metadata)
        {
            // File-level project evidence is sufficient for core metadata until later slices add property-level line spans.
            GraphMetadata graphMetadata = metadata.ToGraphMetadata();
            return new EvidenceRecord(
                snapshotStableKey,
                evidenceStableKey,
                EvidenceKind.ProjectFile,
                RepositoryRelativePath.Parse(relativeProjectPath),
                startLine: 1,
                endLine: Math.Max(1, metadata.LineCount),
                projectName,
                containingSymbol: null,
                snippetHash: null,
                snippetPreview: "Supported project file used for project metadata extraction.",
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                graphMetadata,
                FingerprintGenerator.ForEvidence(EvidenceKind.ProjectFile, relativeProjectPath, 1, Math.Max(1, metadata.LineCount), projectName, KnowledgeKind.Fact, graphMetadata));
        }

        /// <summary>
        /// Creates the repository architecture node contributed by this stage.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the node.</param>
        /// <param name="repositoryStableKey">The stable key of the submitted repository.</param>
        /// <param name="repositoryName">The developer-facing repository name.</param>
        /// <param name="primaryEvidenceStableKey">The evidence stable key that explains the repository node.</param>
        /// <param name="metadata">The deterministic repository metadata.</param>
        /// <returns>An architecture node representing the submitted repository.</returns>
        private static ArchitectureNode CreateRepositoryNode(StableKey snapshotStableKey, StableKey repositoryStableKey, string repositoryName, StableKey primaryEvidenceStableKey, GraphMetadata metadata)
        {
            // Repository nodes make the repository boundary queryable through the generalized architecture-node section.
            return new ArchitectureNode(
                snapshotStableKey,
                repositoryStableKey,
                NodeKind.Repository,
                repositoryName,
                qualifiedName: repositoryName,
                searchName: repositoryName,
                language: null,
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey,
                metadata,
                FingerprintGenerator.ForNode(NodeKind.Repository, repositoryName, repositoryName, repositoryName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a solution-to-project containment edge contributed by project metadata extraction.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the edge.</param>
        /// <param name="solutionStableKey">The source solution stable key.</param>
        /// <param name="projectStableKey">The target project stable key.</param>
        /// <param name="primaryEvidenceStableKey">The evidence stable key that explains the project membership.</param>
        /// <param name="relativeProjectPath">The repository-relative project path used in deterministic metadata.</param>
        /// <returns>An architecture edge representing solution membership for one project.</returns>
        private static ArchitectureEdge CreateSolutionProjectContainsEdge(StableKey snapshotStableKey, StableKey solutionStableKey, StableKey projectStableKey, StableKey primaryEvidenceStableKey, string relativeProjectPath)
        {
            // The membership edge is distinct per solution so shared projects keep one project node but multiple solution containment facts.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["contains.source"] = "SubmittedSolution",
                ["contains.targetPath"] = relativeProjectPath
            });
            StableKey edgeStableKey = new($"edge://{snapshotStableKey.Value}/contains/{solutionStableKey.Value}/{projectStableKey.Value}");
            return new ArchitectureEdge(
                snapshotStableKey,
                edgeStableKey,
                EdgeKind.Contains,
                solutionStableKey,
                projectStableKey,
                isDirect: true,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey,
                metadata,
                FingerprintGenerator.ForEdge(EdgeKind.Contains, solutionStableKey, projectStableKey, isDirect: true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a project-to-project reference edge from one resolved `ProjectReference` declaration.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the edge.</param>
        /// <param name="sourceProjectStableKey">The stable key of the project declaring the reference.</param>
        /// <param name="targetProjectStableKey">The stable key of the referenced project.</param>
        /// <param name="primaryEvidenceStableKey">The evidence stable key for the source `ProjectReference` item.</param>
        /// <param name="reference">The extracted project-reference declaration.</param>
        /// <returns>An architecture edge representing a direct project dependency.</returns>
        private static ArchitectureEdge CreateProjectReferenceEdge(StableKey snapshotStableKey, StableKey sourceProjectStableKey, StableKey targetProjectStableKey, StableKey primaryEvidenceStableKey, ProjectReferenceDeclaration reference)
        {
            // REFERENCES edges model explicit project-to-project dependencies declared in MSBuild project files.
            GraphMetadata metadata = reference.ToGraphMetadata();
            StableKey edgeStableKey = new($"edge://{snapshotStableKey.Value}/references/{sourceProjectStableKey.Value}/{targetProjectStableKey.Value}");
            return new ArchitectureEdge(
                snapshotStableKey,
                edgeStableKey,
                EdgeKind.References,
                sourceProjectStableKey,
                targetProjectStableKey,
                isDirect: true,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey,
                metadata,
                FingerprintGenerator.ForEdge(EdgeKind.References, sourceProjectStableKey, targetProjectStableKey, isDirect: true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a project-to-package use edge from one package reference declaration.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the edge.</param>
        /// <param name="sourceProjectStableKey">The stable key of the project declaring the package reference.</param>
        /// <param name="packageStableKey">The stable key of the package node used by the project.</param>
        /// <param name="primaryEvidenceStableKey">The evidence stable key for the source package reference item.</param>
        /// <param name="packageReference">The extracted package reference declaration.</param>
        /// <returns>An architecture edge representing a direct package dependency.</returns>
        private static ArchitectureEdge CreatePackageUseEdge(StableKey snapshotStableKey, StableKey sourceProjectStableKey, StableKey packageStableKey, StableKey primaryEvidenceStableKey, PackageReferenceDeclaration packageReference)
        {
            // USES_PACKAGE edges preserve version source and asset metadata because those details vary per project dependency.
            GraphMetadata metadata = packageReference.ToUseMetadata();
            StableKey edgeStableKey = new($"edge://{snapshotStableKey.Value}/uses-package/{sourceProjectStableKey.Value}/{packageStableKey.Value}");
            return new ArchitectureEdge(
                snapshotStableKey,
                edgeStableKey,
                EdgeKind.UsesPackage,
                sourceProjectStableKey,
                packageStableKey,
                isDirect: true,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey,
                metadata,
                FingerprintGenerator.ForEdge(EdgeKind.UsesPackage, sourceProjectStableKey, packageStableKey, isDirect: true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates source evidence for a `ProjectReference` declaration in a project file.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="evidenceStableKey">The stable key for this evidence record.</param>
        /// <param name="reference">The extracted project-reference declaration.</param>
        /// <returns>An evidence record representing the source `ProjectReference` item.</returns>
        private static EvidenceRecord CreateProjectReferenceEvidence(StableKey snapshotStableKey, StableKey evidenceStableKey, ProjectReferenceDeclaration reference)
        {
            // Line-level evidence is used when XML line info is present, with a file-level fallback for unusual XML readers.
            int? lineNumber = reference.LineNumber;
            GraphMetadata metadata = reference.ToGraphMetadata();
            return new EvidenceRecord(
                snapshotStableKey,
                evidenceStableKey,
                EvidenceKind.ProjectFile,
                RepositoryRelativePath.Parse(reference.DeclaringProjectRelativePath),
                lineNumber,
                lineNumber,
                symbolName: reference.ResolvedRelativePath,
                containingSymbol: null,
                snippetHash: null,
                snippetPreview: reference.DeclaredInclude,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                metadata,
                FingerprintGenerator.ForEvidence(EvidenceKind.ProjectFile, reference.DeclaringProjectRelativePath, lineNumber, lineNumber, reference.ResolvedRelativePath, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates source evidence for a package reference declaration in a project or imported build file.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="evidenceStableKey">The stable key for this evidence record.</param>
        /// <param name="packageReference">The extracted package reference declaration.</param>
        /// <returns>An evidence record representing the source package reference item.</returns>
        private static EvidenceRecord CreatePackageReferenceEvidence(StableKey snapshotStableKey, StableKey evidenceStableKey, PackageReferenceDeclaration packageReference)
        {
            // Package evidence points to the file that actually declared the PackageReference, including imported props or targets files.
            int? lineNumber = packageReference.LineNumber;
            GraphMetadata metadata = packageReference.ToUseMetadata();
            return new EvidenceRecord(
                snapshotStableKey,
                evidenceStableKey,
                EvidenceKind.ProjectFile,
                RepositoryRelativePath.Parse(packageReference.EvidenceRelativePath),
                lineNumber,
                lineNumber,
                symbolName: packageReference.PackageId,
                containingSymbol: packageReference.DeclaringProjectRelativePath,
                snippetHash: packageReference.SnippetHash,
                snippetPreview: packageReference.SnippetPreview ?? packageReference.PackageId,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                metadata,
                FingerprintGenerator.ForEvidence(EvidenceKind.ProjectFile, packageReference.EvidenceRelativePath, lineNumber, lineNumber, packageReference.PackageId, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates source evidence for an analyzer declaration in a project file.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="evidenceStableKey">The stable key for this evidence record.</param>
        /// <param name="analyzerReference">The analyzer-reference declaration to represent.</param>
        /// <returns>An evidence record representing the source `Analyzer` item.</returns>
        private static EvidenceRecord CreateAnalyzerReferenceEvidence(StableKey snapshotStableKey, StableKey evidenceStableKey, AnalyzerReferenceDeclaration analyzerReference)
        {
            // Analyzer evidence remains project-file evidence because the source declaration is the Analyzer item in the project XML.
            GraphMetadata metadata = analyzerReference.ToGraphMetadata();
            return new EvidenceRecord(
                snapshotStableKey,
                evidenceStableKey,
                EvidenceKind.ProjectFile,
                RepositoryRelativePath.Parse(analyzerReference.DeclaringProjectRelativePath),
                analyzerReference.LineNumber,
                analyzerReference.LineNumber,
                symbolName: analyzerReference.ResolvedRelativePath ?? analyzerReference.DeclaredInclude,
                containingSymbol: analyzerReference.DeclaringProjectRelativePath,
                snippetHash: analyzerReference.SnippetHash,
                snippetPreview: analyzerReference.SnippetPreview ?? analyzerReference.DeclaredInclude,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                metadata,
                FingerprintGenerator.ForEvidence(EvidenceKind.ProjectFile, analyzerReference.DeclaringProjectRelativePath, analyzerReference.LineNumber, analyzerReference.LineNumber, analyzerReference.ResolvedRelativePath ?? analyzerReference.DeclaredInclude, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates source evidence for a controlled package extraction diagnostic.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="evidenceStableKey">The stable key for this evidence record.</param>
        /// <param name="declaringProjectRelativePath">The repository-relative project path associated with the diagnostic.</param>
        /// <param name="diagnostic">The controlled package extraction diagnostic to represent.</param>
        /// <returns>An evidence record representing the source artifact that produced the diagnostic.</returns>
        private static EvidenceRecord CreatePackageDiagnosticEvidence(StableKey snapshotStableKey, StableKey evidenceStableKey, string declaringProjectRelativePath, PackageExtractionDiagnostic diagnostic)
        {
            // Diagnostic evidence uses a file-level fallback so malformed XML still leaves a traceable source artifact without storing file contents.
            GraphMetadata metadata = diagnostic.ToGraphMetadata();
            return new EvidenceRecord(
                snapshotStableKey,
                evidenceStableKey,
                EvidenceKind.ProjectFile,
                RepositoryRelativePath.Parse(diagnostic.EvidenceRelativePath),
                diagnostic.LineNumber,
                diagnostic.LineNumber,
                symbolName: null,
                containingSymbol: declaringProjectRelativePath,
                snippetHash: null,
                snippetPreview: diagnostic.SnippetPreview,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                metadata,
                FingerprintGenerator.ForEvidence(EvidenceKind.ProjectFile, diagnostic.EvidenceRelativePath, diagnostic.LineNumber, diagnostic.LineNumber, null, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates the solution architecture node contributed by this stage.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the node.</param>
        /// <param name="solutionStableKey">The stable key of the submitted solution.</param>
        /// <param name="solutionName">The developer-facing solution file name.</param>
        /// <param name="relativeSolutionPath">The repository-relative solution path.</param>
        /// <param name="primaryEvidenceStableKey">The evidence stable key that explains the solution node.</param>
        /// <param name="metadata">The deterministic solution metadata.</param>
        /// <returns>An architecture node representing the submitted solution file.</returns>
        private static ArchitectureNode CreateSolutionNode(StableKey snapshotStableKey, StableKey solutionStableKey, string solutionName, string relativeSolutionPath, StableKey primaryEvidenceStableKey, GraphMetadata metadata)
        {
            // Solution nodes use the relative path as their qualified/search identity so same-named files in different folders remain distinct.
            return new ArchitectureNode(
                snapshotStableKey,
                solutionStableKey,
                NodeKind.Solution,
                solutionName,
                qualifiedName: relativeSolutionPath,
                searchName: relativeSolutionPath,
                language: null,
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey,
                metadata,
                FingerprintGenerator.ForNode(NodeKind.Solution, solutionName, relativeSolutionPath, relativeSolutionPath, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates evidence for the submitted repository boundary.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="evidenceStableKey">The stable key for this evidence record.</param>
        /// <param name="metadata">The deterministic repository evidence metadata.</param>
        /// <returns>An evidence record representing repository-boundary input evidence.</returns>
        private static EvidenceRecord CreateRepositoryEvidence(StableKey snapshotStableKey, StableKey evidenceStableKey, GraphMetadata metadata)
        {
            // Repository boundary evidence is modeled as inference because a directory is the submitted boundary rather than a source file.
            return new EvidenceRecord(
                snapshotStableKey,
                evidenceStableKey,
                EvidenceKind.Inference,
                RepositoryRelativePath.Parse("."),
                startLine: null,
                endLine: null,
                symbolName: null,
                containingSymbol: null,
                snippetHash: null,
                snippetPreview: "Submitted repository root accepted by extraction validation.",
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                metadata,
                FingerprintGenerator.ForEvidence(EvidenceKind.Inference, ".", null, null, null, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates file-level evidence for a submitted solution file.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="evidenceStableKey">The stable key for this evidence record.</param>
        /// <param name="relativeSolutionPath">The repository-relative solution path.</param>
        /// <param name="lineCount">The number of lines read from the solution file.</param>
        /// <param name="metadata">The deterministic solution metadata.</param>
        /// <returns>An evidence record representing the submitted solution file.</returns>
        private static EvidenceRecord CreateSolutionFileEvidence(StableKey snapshotStableKey, StableKey evidenceStableKey, string relativeSolutionPath, int lineCount, GraphMetadata metadata)
        {
            // File-level solution evidence supports the solution node and repository-to-solution containment edge.
            return new EvidenceRecord(
                snapshotStableKey,
                evidenceStableKey,
                EvidenceKind.ProjectFile,
                RepositoryRelativePath.Parse(relativeSolutionPath),
                startLine: 1,
                endLine: Math.Max(1, lineCount),
                symbolName: null,
                containingSymbol: null,
                snippetHash: null,
                snippetPreview: "Submitted Visual Studio solution file.",
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                metadata,
                FingerprintGenerator.ForEvidence(EvidenceKind.ProjectFile, relativeSolutionPath, 1, Math.Max(1, lineCount), null, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates line-level evidence for a visible project declaration in a submitted solution file.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="evidenceStableKey">The stable key for this evidence record.</param>
        /// <param name="relativeSolutionPath">The repository-relative solution path.</param>
        /// <param name="declaration">The parsed project declaration details.</param>
        /// <returns>An evidence record representing one solution project declaration line.</returns>
        private static EvidenceRecord CreateProjectDeclarationEvidence(StableKey snapshotStableKey, StableKey evidenceStableKey, string relativeSolutionPath, SolutionProjectDeclaration declaration)
        {
            // Project declaration evidence is precise to a single solution line so later project slices can attach membership facts to it.
            GraphMetadata metadata = declaration.ToMetadata();
            return new EvidenceRecord(
                snapshotStableKey,
                evidenceStableKey,
                EvidenceKind.ProjectFile,
                RepositoryRelativePath.Parse(relativeSolutionPath),
                declaration.LineNumber,
                declaration.LineNumber,
                declaration.Name,
                containingSymbol: null,
                snippetHash: null,
                snippetPreview: declaration.DeclaredPath,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                metadata,
                FingerprintGenerator.ForEvidence(EvidenceKind.ProjectFile, relativeSolutionPath, declaration.LineNumber, declaration.LineNumber, declaration.Name, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates the repository-to-solution containment edge contributed by this stage.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the edge.</param>
        /// <param name="repositoryStableKey">The source repository node stable key.</param>
        /// <param name="solutionStableKey">The target solution node stable key.</param>
        /// <param name="primaryEvidenceStableKey">The evidence stable key that explains the containment edge.</param>
        /// <param name="relativeSolutionPath">The repository-relative solution path used in deterministic metadata.</param>
        /// <returns>An architecture edge representing repository containment of a submitted solution.</returns>
        private static ArchitectureEdge CreateContainsEdge(StableKey snapshotStableKey, StableKey repositoryStableKey, StableKey solutionStableKey, StableKey primaryEvidenceStableKey, string relativeSolutionPath)
        {
            // The edge is snapshot-scoped because containment is observed as part of this accepted extraction request.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["contains.source"] = "SubmittedRepository",
                ["contains.targetPath"] = relativeSolutionPath
            });
            StableKey edgeStableKey = new($"edge://{snapshotStableKey.Value}/contains/{repositoryStableKey.Value}/{solutionStableKey.Value}");
            return new ArchitectureEdge(
                snapshotStableKey,
                edgeStableKey,
                EdgeKind.Contains,
                repositoryStableKey,
                solutionStableKey,
                isDirect: true,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey,
                metadata,
                FingerprintGenerator.ForEdge(EdgeKind.Contains, repositoryStableKey, solutionStableKey, isDirect: true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates deterministic repository metadata from accepted extraction context while avoiding raw request metadata values.
        /// </summary>
        /// <param name="branchName">The optional branch name supplied with the accepted request.</param>
        /// <param name="commitSha">The optional commit SHA supplied with the accepted request.</param>
        /// <param name="requestedBy">The optional actor supplied with the accepted request.</param>
        /// <param name="metadata">The accepted request metadata dictionary.</param>
        /// <returns>Graph metadata containing safe repository extraction context.</returns>
        private static GraphMetadata CreateRepositoryMetadata(string? branchName, string? commitSha, string? requestedBy, IReadOnlyDictionary<string, string> metadata)
        {
            // Metadata values may be sensitive, so only metadata keys are included alongside explicitly accepted boundary fields.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["repository.inputKind"] = "SubmittedRoot"
            };

            AddOptional(values, "repository.branchName", branchName);
            AddOptional(values, "repository.commitSha", commitSha);
            AddOptional(values, "repository.requestedBy", requestedBy);

            foreach (string key in metadata.Keys.Order(StringComparer.Ordinal))
            {
                values[string.Concat("repository.requestMetadataKey.", key)] = key;
            }

            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Creates deterministic metadata for a submitted solution file.
        /// </summary>
        /// <param name="relativeSolutionPath">The repository-relative solution path.</param>
        /// <param name="solutionFacts">The parsed solution file facts.</param>
        /// <returns>Graph metadata describing solution extraction facts.</returns>
        private static GraphMetadata CreateSolutionMetadata(string relativeSolutionPath, SolutionFileFacts solutionFacts)
        {
            // The metadata intentionally summarizes the file rather than storing full content.
            return GraphMetadata.From(new Dictionary<string, object?>
            {
                ["solution.relativePath"] = relativeSolutionPath,
                ["solution.submissionKind"] = "ExplicitRequestPath",
                ["solution.projectDeclarationCount"] = solutionFacts.ProjectDeclarations.Count,
                ["solution.lineCount"] = solutionFacts.LineCount
            });
        }

        /// <summary>
        /// Adds an optional metadata value when the supplied text is meaningful.
        /// </summary>
        /// <param name="values">The metadata dictionary being assembled.</param>
        /// <param name="key">The metadata property key to add.</param>
        /// <param name="value">The optional metadata text value.</param>
        private static void AddOptional(Dictionary<string, object?> values, string key, string? value)
        {
            // Optional context is useful when present, but blank values should not create noisy metadata properties.
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value.Trim();
            }
        }

        /// <summary>
        /// Creates a deterministic repository stable key from the accepted root path.
        /// </summary>
        /// <param name="repositoryRootDirectory">The normalized absolute repository root directory.</param>
        /// <returns>The repository stable key used by repository models and nodes.</returns>
        private static StableKey CreateRepositoryStableKey(string repositoryRootDirectory)
        {
            // This mirrors the existing snapshot assembler identity so stage contributions merge with snapshot assembly boundary facts.
            return StableKeyGenerator.ForRepository(NormalizeIdentitySegment(repositoryRootDirectory));
        }

        /// <summary>
        /// Creates the snapshot stable key used for snapshot-scoped nodes, edges, and evidence created during stage execution.
        /// </summary>
        /// <param name="repositoryStableKey">The stable repository key that scopes the extraction snapshot.</param>
        /// <param name="runId">The accepted extraction run identifier.</param>
        /// <returns>The deterministic snapshot stable key for this run.</returns>
        private static StableKey CreateSnapshotStableKey(StableKey repositoryStableKey, string runId)
        {
            // The snapshot key mirrors the assembler so contributed facts are scoped to the same final snapshot identity.
            return StableKeyGenerator.ForSummary(repositoryStableKey.Value, "ExtractionRun", runId);
        }

        /// <summary>
        /// Creates a deterministic evidence stable key within the extraction snapshot.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes evidence identity.</param>
        /// <param name="evidenceKind">A short discriminator describing the evidence category.</param>
        /// <param name="targetIdentity">The stable target identity or line discriminator supported by the evidence.</param>
        /// <returns>A stable evidence key.</returns>
        private static StableKey CreateEvidenceStableKey(StableKey snapshotStableKey, string evidenceKind, string targetIdentity)
        {
            // Evidence keys are snapshot-scoped so equivalent source lines in different runs remain separate evidence records.
            return new StableKey($"evidence://{snapshotStableKey.Value}/{evidenceKind}/{targetIdentity}");
        }

        /// <summary>
        /// Creates a deterministic evidence stable key for a project-reference declaration.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes evidence identity.</param>
        /// <param name="reference">The project-reference declaration that requires evidence.</param>
        /// <returns>A stable evidence key for the declaration.</returns>
        private static StableKey CreateProjectReferenceEvidenceStableKey(StableKey snapshotStableKey, ProjectReferenceDeclaration reference)
        {
            // Include the raw declaration and line number so duplicate declarations keep separate source evidence even when they produce one edge.
            string lineSegment = reference.LineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "file";
            return CreateEvidenceStableKey(snapshotStableKey, "project-reference", string.Concat(reference.DeclaringProjectRelativePath, ":", reference.DeclaredInclude, ":", lineSegment));
        }

        /// <summary>
        /// Creates a deterministic evidence stable key for a package-reference declaration.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes evidence identity.</param>
        /// <param name="packageReference">The package-reference declaration that requires evidence.</param>
        /// <returns>A stable evidence key for the declaration.</returns>
        private static StableKey CreatePackageReferenceEvidenceStableKey(StableKey snapshotStableKey, PackageReferenceDeclaration packageReference)
        {
            // Include the declaring project and evidence path so imported references and direct references remain traceable.
            string lineSegment = packageReference.LineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "file";
            return CreateEvidenceStableKey(snapshotStableKey, "package-reference", string.Concat(packageReference.DeclaringProjectRelativePath, ":", packageReference.EvidenceRelativePath, ":", packageReference.NormalizedPackageId, ":", lineSegment));
        }

        /// <summary>
        /// Creates a deterministic evidence stable key for an analyzer-reference declaration.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes evidence identity.</param>
        /// <param name="analyzerReference">The analyzer-reference declaration that requires evidence.</param>
        /// <returns>A stable evidence key for the analyzer declaration.</returns>
        private static StableKey CreateAnalyzerReferenceEvidenceStableKey(StableKey snapshotStableKey, AnalyzerReferenceDeclaration analyzerReference)
        {
            // Include the raw include and line number so duplicate analyzer declarations keep distinct evidence records.
            string lineSegment = analyzerReference.LineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "file";
            return CreateEvidenceStableKey(snapshotStableKey, "analyzer-reference", string.Concat(analyzerReference.DeclaringProjectRelativePath, ":", analyzerReference.DeclaredInclude, ":", lineSegment));
        }

        /// <summary>
        /// Creates a deterministic evidence stable key for a package extraction diagnostic.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes evidence identity.</param>
        /// <param name="declaringProjectRelativePath">The repository-relative project path associated with the diagnostic.</param>
        /// <param name="diagnostic">The package extraction diagnostic that requires evidence.</param>
        /// <returns>A stable evidence key for the diagnostic.</returns>
        private static StableKey CreatePackageDiagnosticEvidenceStableKey(StableKey snapshotStableKey, string declaringProjectRelativePath, PackageExtractionDiagnostic diagnostic)
        {
            // Include the project path and diagnostic evidence path so repeated malformed files in different projects remain distinct.
            string lineSegment = diagnostic.LineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "file";
            return CreateEvidenceStableKey(snapshotStableKey, "package-diagnostic", string.Concat(declaringProjectRelativePath, ":", diagnostic.EvidenceRelativePath, ":", lineSegment));
        }

        /// <summary>
        /// Creates the deterministic project stable key for a repository-relative project path.
        /// </summary>
        /// <param name="relativeProjectPath">The repository-relative project path normalized with forward slashes.</param>
        /// <returns>The stable key used for project nodes and solution membership edges.</returns>
        private static StableKey CreateProjectStableKey(string relativeProjectPath)
        {
            // Project identity must be path-based so duplicate declarations across submitted solutions collapse into one node.
            return new StableKey($"project://{relativeProjectPath}");
        }

        /// <summary>
        /// Creates a deterministic package stable key from normalized package identity and version state.
        /// </summary>
        /// <param name="packageReference">The package reference that identifies the package node.</param>
        /// <returns>The stable key used for package nodes and package-use edges.</returns>
        private static StableKey CreatePackageStableKey(PackageReferenceDeclaration packageReference)
        {
            // Include the resolved version when known so different package versions remain distinct queryable dependencies.
            string versionSegment = string.IsNullOrWhiteSpace(packageReference.ResolvedVersion)
                ? string.Concat("version-source/", packageReference.VersionSource.ToString().ToLowerInvariant())
                : string.Concat("version/", packageReference.ResolvedVersion.Trim().ToLowerInvariant());
            return StableKeyGenerator.ForPackage(string.Concat(packageReference.NormalizedPackageId, "/", versionSegment));
        }

        /// <summary>
        /// Infers a supported project language from a referenced project file extension.
        /// </summary>
        /// <param name="projectPath">The absolute project file path to inspect.</param>
        /// <param name="language">The inferred supported project language when the extension is recognized.</param>
        /// <returns><see langword="true" /> when the file extension maps to a supported project language; otherwise, <see langword="false" />.</returns>
        private static bool TryInferProjectLanguage(string projectPath, out ProjectLanguage language)
        {
            // Repository-contained reference targets are not solution declarations, so extension-based inference is the deterministic safe fallback.
            string extension = Path.GetExtension(projectPath);

            if (string.Equals(extension, ".vbproj", StringComparison.OrdinalIgnoreCase))
            {
                language = ProjectLanguage.VisualBasic;
                return true;
            }

            if (string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                language = ProjectLanguage.CSharp;
                return true;
            }

            language = ProjectLanguage.CSharp;
            return false;
        }

        /// <summary>
        /// Resolves a solution-declared project path against the directory containing the submitted solution file.
        /// </summary>
        /// <param name="solutionDirectory">The absolute directory that contains the submitted solution.</param>
        /// <param name="declaredProjectPath">The project path text declared inside the solution file.</param>
        /// <returns>The absolute normalized project file path.</returns>
        private static string ResolveDeclaredProjectPath(string solutionDirectory, string declaredProjectPath)
        {
            // Solution declarations are relative to the solution file; absolute declarations remain absolute after Path.Combine semantics.
            string platformPath = declaredProjectPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(solutionDirectory, platformPath));
        }

        /// <summary>
        /// Builds a repository-relative path using forward slash separators.
        /// </summary>
        /// <param name="repositoryRootDirectory">The normalized absolute repository root directory.</param>
        /// <param name="filePath">The normalized absolute file path inside the repository root.</param>
        /// <returns>A repository-relative path suitable for stable keys and evidence records.</returns>
        private static string GetRepositoryRelativePath(string repositoryRootDirectory, string filePath)
        {
            // repository path validation already guarantees containment; this method canonicalizes separators for graph identity.
            string relativePath = Path.GetRelativePath(repositoryRootDirectory, filePath);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Gets the developer-facing repository name from the accepted repository root path.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <returns>The final directory name, or a fallback name when the path has no final segment.</returns>
        private static string GetRepositoryName(string repositoryRootDirectory)
        {
            // Trimming the trailing separator ensures Path.GetFileName returns the final directory segment on both Windows and Linux.
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(repositoryRootDirectory));
            return string.IsNullOrWhiteSpace(name) ? "Repository" : name;
        }

        /// <summary>
        /// Normalizes a filesystem path into the stable repository identity segment used by earlier snapshot assembly behavior.
        /// </summary>
        /// <param name="value">The absolute repository root path to normalize.</param>
        /// <returns>A deterministic lowercase path segment.</returns>
        private static string NormalizeIdentitySegment(string value)
        {
            // This helper is intentionally equivalent to the application assembler so duplicate repository facts collapse by stable key.
            string trimmed = Path.TrimEndingDirectorySeparator(value).Replace('\\', '/').Trim();
            return trimmed.ToLowerInvariant();
        }
    }
}
