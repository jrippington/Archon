using Archon.Application.Extraction.Pipeline;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Extractors.Projects.Solutions
{
    /// <summary>
    /// Contributes repository and submitted-solution graph facts for the first WP005 project extraction vertical slice.
    /// </summary>
    public sealed class RepositorySolutionExtractionStage : IExtractionStage
    {
        /// <summary>
        /// Stores the lightweight solution parser used to capture submitted solution file evidence.
        /// </summary>
        private readonly SolutionFileParser _solutionFileParser;

        /// <summary>
        /// Initializes a new instance of the <see cref="RepositorySolutionExtractionStage" /> class.
        /// </summary>
        public RepositorySolutionExtractionStage()
        {
            // The default constructor keeps dependency-injection registration simple while isolating file parsing in a dedicated collaborator.
            _solutionFileParser = new SolutionFileParser();
        }

        /// <summary>
        /// Gets the stable stage identifier used by pipeline ordering, progress, and diagnostics.
        /// </summary>
        public string StageId => "project-repository-solution";

        /// <summary>
        /// Executes repository and submitted-solution extraction against the resolved WP004 input and shared accumulator.
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

            foreach (string solutionPath in context.ResolvedInput.SolutionPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativeSolutionPath = GetRepositoryRelativePath(context.ResolvedInput.RepositoryRootDirectory, solutionPath);

                try
                {
                    SolutionFileFacts solutionFacts = await _solutionFileParser.ParseAsync(solutionPath, cancellationToken).ConfigureAwait(false);
                    parsedSolutions.Add((solutionPath, relativeSolutionPath, solutionFacts));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    // The returned message is intentionally path-free and exception-type-free so run status remains credential-safe.
                    return ExtractionStageResult.BlockingError("A submitted solution file could not be read as a valid Visual Studio solution. Review server logs for details.");
                }
            }

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
                    // Slice 1 captures project declaration evidence only; later slices turn supported declarations into project nodes and relationships.
                    StableKey declarationEvidenceStableKey = CreateEvidenceStableKey(snapshotStableKey, "solution-project", string.Concat(solutionStableKey.Value, ":", declaration.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    context.Accumulation.AddEvidence(CreateProjectDeclarationEvidence(snapshotStableKey, declarationEvidenceStableKey, relativeSolutionPath, declaration));
                }
            }

            return ExtractionStageResult.Success();
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
            // This mirrors the existing WP004 assembler identity so stage contributions merge with snapshot assembly boundary facts.
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
        /// Builds a repository-relative path using forward slash separators.
        /// </summary>
        /// <param name="repositoryRootDirectory">The normalized absolute repository root directory.</param>
        /// <param name="filePath">The normalized absolute file path inside the repository root.</param>
        /// <returns>A repository-relative path suitable for stable keys and evidence records.</returns>
        private static string GetRepositoryRelativePath(string repositoryRootDirectory, string filePath)
        {
            // WP004 validation already guarantees containment; this method canonicalizes separators for graph identity.
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
