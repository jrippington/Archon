using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Domain.Graph.ControlledValues;
using Archon.Extractors.Projects.Solutions;
using Xunit;

namespace Archon.Extractors.Projects.Tests.Solutions
{
    /// <summary>
    /// Verifies the WP005 repository and solution extraction stage contributes the first real graph facts through the shared pipeline contract.
    /// </summary>
    public sealed class RepositorySolutionExtractionStageTests : IDisposable
    {
        /// <summary>
        /// Tracks temporary repository roots created by these tests so each test can use real submitted solution files without leaking directories.
        /// </summary>
        private readonly List<string> _temporaryDirectories = [];

        /// <summary>
        /// Deletes temporary repository directories after each test has finished reading submitted solution files.
        /// </summary>
        public void Dispose()
        {
            // The extractor reads real solution files, so cleanup removes the isolated filesystem fixtures created by each test.
            foreach (string temporaryDirectory in _temporaryDirectories)
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies one submitted solution contributes repository and solution nodes, a containment relationship, and solution-file evidence.
        /// </summary>
        /// <returns>A task that completes after the stage output has been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenOneSolutionIsSubmitted_ShouldContributeRepositorySolutionEdgeAndEvidence()
        {
            // This scenario is the minimal WP005 vertical slice: one validated repository root and one explicit solution path.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "CustomerSuite.sln", "Customer.Api", "Customer.Api.csproj");
            ResolvedExtractionInput input = CreateResolvedInput(repositoryRoot, [solutionPath]);
            ExtractionRun run = CreateRun(input);
            ArchitectureSnapshotAccumulator accumulation = new();
            RepositorySolutionExtractionStage stage = new();

            ExtractionStageResult result = await stage.ExecuteAsync(new ExtractionStageContext(input, run, accumulation), CancellationToken.None);
            ExtractedArchitectureSnapshot snapshot = accumulation.ToSnapshot();

            Assert.False(result.HasBlockingError);
            Assert.Single(snapshot.Repositories);
            Assert.Single(snapshot.Solutions);
            Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Repository && node.StableKey.Value.StartsWith("repository://", StringComparison.Ordinal));
            Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Solution && node.StableKey.Value == "solution://CustomerSuite.sln");
            Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Contains && edge.SourceNodeStableKey.Value.StartsWith("repository://", StringComparison.Ordinal) && edge.TargetNodeStableKey.Value == "solution://CustomerSuite.sln");
            Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "CustomerSuite.sln" && evidence.EvidenceKind == EvidenceKind.ProjectFile);
            Assert.Contains(snapshot.Evidence, evidence => evidence.SymbolName == "Customer.Api" && evidence.StartLine == 3);
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies multi-solution submissions preserve each submitted solution as a distinct node and do not scan unsubmitted solution files.
        /// </summary>
        /// <returns>A task that completes after multi-solution extraction output has been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenMultipleSolutionsAreSubmitted_ShouldPreserveSubmittedSolutionsAndIgnoreUnsubmittedSolutions()
        {
            // The extractor must respect the WP004 validation boundary and only read solution paths that were explicitly submitted.
            string repositoryRoot = CreateRepositoryRoot();
            string firstSolutionPath = CreateSolutionFile(repositoryRoot, "CustomerSuite.sln", "Customer.Api", "Customer.Api.csproj");
            string secondSolutionPath = CreateSolutionFile(repositoryRoot, Path.Combine("tools", "Tools.sln"), "Customer.Tools", "Customer.Tools.csproj");
            _ = CreateSolutionFile(repositoryRoot, "Unsubmitted.sln", "Should.Not.Appear", "Should.Not.Appear.csproj");
            ResolvedExtractionInput input = CreateResolvedInput(repositoryRoot, [firstSolutionPath, secondSolutionPath]);
            ExtractionRun run = CreateRun(input);
            ArchitectureSnapshotAccumulator accumulation = new();
            RepositorySolutionExtractionStage stage = new();

            ExtractionStageResult result = await stage.ExecuteAsync(new ExtractionStageContext(input, run, accumulation), CancellationToken.None);
            ExtractedArchitectureSnapshot snapshot = accumulation.ToSnapshot();

            Assert.False(result.HasBlockingError);
            Assert.Single(snapshot.Repositories);
            Assert.Equal(2, snapshot.Solutions.Count);
            Assert.Equal(2, snapshot.Nodes.Count(node => node.NodeKind == NodeKind.Solution));
            Assert.Equal(2, snapshot.Edges.Count(edge => edge.EdgeKind == EdgeKind.Contains));
            Assert.Contains(snapshot.Solutions, solution => solution.Path.Value == "CustomerSuite.sln");
            Assert.Contains(snapshot.Solutions, solution => solution.Path.Value == "tools/Tools.sln");
            Assert.DoesNotContain(snapshot.Solutions, solution => solution.Path.Value == "Unsubmitted.sln");
            Assert.DoesNotContain(snapshot.Nodes, node => node.StableKey.Value == "solution://Unsubmitted.sln");
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies unreadable or malformed submitted solution content becomes a controlled blocking stage error instead of an unhandled exception.
        /// </summary>
        /// <returns>A task that completes after controlled error behavior has been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenSubmittedSolutionIsMalformed_ShouldReturnControlledBlockingError()
        {
            // A malformed solution cannot provide trustworthy solution facts, so the stage should stop with a safe error message.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = Path.Combine(repositoryRoot, "Broken.sln");
            File.WriteAllText(solutionPath, "This is not a Visual Studio solution file.");
            ResolvedExtractionInput input = CreateResolvedInput(repositoryRoot, [solutionPath]);
            ExtractionRun run = CreateRun(input);
            ArchitectureSnapshotAccumulator accumulation = new();
            RepositorySolutionExtractionStage stage = new();

            ExtractionStageResult result = await stage.ExecuteAsync(new ExtractionStageContext(input, run, accumulation), CancellationToken.None);
            ExtractedArchitectureSnapshot snapshot = accumulation.ToSnapshot();

            Assert.True(result.HasBlockingError);
            Assert.Contains("submitted solution", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(repositoryRoot, result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(snapshot.Nodes);
            Assert.Empty(snapshot.Edges);
            Assert.Empty(snapshot.Evidence);
        }

        /// <summary>
        /// Creates an isolated temporary repository root for solution-file extraction tests.
        /// </summary>
        /// <returns>The absolute temporary repository root path.</returns>
        private string CreateRepositoryRoot()
        {
            // Prefixing the directory name keeps manual temp-folder inspection recognizable while the GUID prevents collisions.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-wp005-projects-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repositoryRoot);
            _temporaryDirectories.Add(repositoryRoot);
            return repositoryRoot;
        }

        /// <summary>
        /// Creates a minimal Visual Studio solution file that declares one project entry.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that contains the solution file.</param>
        /// <param name="relativeSolutionPath">The repository-relative solution path to create.</param>
        /// <param name="projectName">The project display name to declare in the solution file.</param>
        /// <param name="projectPath">The project path string to declare in the solution file.</param>
        /// <returns>The absolute solution path written to disk.</returns>
        private static string CreateSolutionFile(string repositoryRoot, string relativeSolutionPath, string projectName, string projectPath)
        {
            // The file content contains the minimum header and Project line needed for deterministic solution evidence parsing.
            string solutionPath = Path.Combine(repositoryRoot, relativeSolutionPath);
            Directory.CreateDirectory(Path.GetDirectoryName(solutionPath)!);
            File.WriteAllText(
                solutionPath,
                string.Join(
                    Environment.NewLine,
                    [
                        "Microsoft Visual Studio Solution File, Format Version 12.00",
                        "# Visual Studio Version 17",
                        $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{projectName}\", \"{projectPath}\", \"{{11111111-1111-1111-1111-111111111111}}\"",
                        "EndProject",
                        "Global",
                        "EndGlobal"
                    ]));
            return solutionPath;
        }

        /// <summary>
        /// Creates normalized extraction input for a repository root and explicit submitted solution paths.
        /// </summary>
        /// <param name="repositoryRoot">The accepted repository root directory.</param>
        /// <param name="solutionPaths">The accepted absolute solution paths.</param>
        /// <returns>A resolved extraction input mirroring the WP004 validation output.</returns>
        private static ResolvedExtractionInput CreateResolvedInput(string repositoryRoot, IReadOnlyList<string> solutionPaths)
        {
            // Metadata values are deliberately simple because the stage should not log or expose them as sensitive diagnostics.
            return new ResolvedExtractionInput(
                repositoryRoot,
                solutionPaths,
                BranchName: "main",
                CommitSha: "abcdef1234567890",
                RequestedBy: "developer@example.invalid",
                Metadata: new Dictionary<string, string>
                {
                    ["source"] = "wp005-project-extractor-test"
                });
        }

        /// <summary>
        /// Creates an accepted extraction run for stage context tests.
        /// </summary>
        /// <param name="input">The resolved input represented by the accepted run summary.</param>
        /// <returns>An extraction run suitable for direct pipeline-stage invocation.</returns>
        private static ExtractionRun CreateRun(ResolvedExtractionInput input)
        {
            // The run id scopes snapshot-stable evidence and relationship keys while avoiding the full HTTP start path in focused stage tests.
            return new ExtractionRun(
                new ExtractionRunId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
                ExtractionRunStatus.Queued,
                new ExtractionRunRequestSummary(
                    input.RepositoryRootDirectory,
                    input.SolutionPaths,
                    input.BranchName,
                    input.CommitSha,
                    input.RequestedBy,
                    input.Metadata.Keys.ToArray()),
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                completedUtc: null,
                new ExtractionRunProgress("Queued", "Queued for test execution.", 0, new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero)),
                warnings: null,
                errors: null,
                snapshotIdentity: null);
        }
    }
}
