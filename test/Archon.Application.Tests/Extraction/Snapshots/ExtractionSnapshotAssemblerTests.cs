using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Application.Extraction.Snapshots;
using Xunit;

namespace Archon.Application.Tests.Extraction.Snapshots
{
    /// <summary>
    /// Verifies WP004 snapshot assembly turns validated input and accumulated placeholder contributions into a generalized snapshot contract.
    /// </summary>
    public sealed class ExtractionSnapshotAssemblerTests
    {
        /// <summary>
        /// Verifies the assembler creates repository, solution, header, diagnostics, and explicit empty collections for unsupported sections.
        /// </summary>
        [Fact]
        public void Assemble_WhenInputHasTwoSolutions_ShouldCreateCompleteMinimalSnapshotShape()
        {
            // The assembler proves the generalized contract shape without pretending real Roslyn or repository extraction has run.
            ResolvedExtractionInput input = CreateResolvedInput();
            ExtractionRun run = CreateRun(input);
            ArchitectureSnapshotAccumulator accumulation = new();
            accumulation.AddWarning("Placeholder extraction produced only boundary facts.");
            ExtractionSnapshotAssembler assembler = new();

            var snapshot = assembler.Assemble(run, input, accumulation);

            Assert.NotNull(snapshot.SnapshotHeader);
            Assert.Single(snapshot.Repositories);
            Assert.Equal(2, snapshot.Solutions.Count);
            Assert.Empty(snapshot.Nodes);
            Assert.Empty(snapshot.Edges);
            Assert.Empty(snapshot.Evidence);
            Assert.Empty(snapshot.Findings);
            Assert.Empty(snapshot.Metrics);
            Assert.Empty(snapshot.GeneratedSummaries);
            Assert.Contains("Placeholder extraction produced only boundary facts.", snapshot.Warnings);
            Assert.Empty(snapshot.Errors);
            Assert.All(snapshot.Repositories.Select(repository => repository.StableKey.Value), stableKey => Assert.DoesNotContain("neo4j", stableKey, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Verifies the placeholder stage contributes only warning-level boundary diagnostics and no fabricated architecture facts.
        /// </summary>
        [Fact]
        public async Task PlaceholderStage_WhenExecuted_ShouldContributeOnlyMinimalWarningBoundary()
        {
            // Placeholder behavior must remain visibly incomplete so later extractor slices own real repository and Roslyn facts.
            ResolvedExtractionInput input = CreateResolvedInput();
            ExtractionRun run = CreateRun(input);
            ArchitectureSnapshotAccumulator accumulation = new();
            PlaceholderExtractionStage stage = new();

            ExtractionStageResult result = await stage.ExecuteAsync(new ExtractionStageContext(input, run, accumulation), CancellationToken.None);
            var snapshot = accumulation.ToSnapshot();

            Assert.False(result.HasBlockingError);
            Assert.Single(snapshot.Warnings);
            Assert.Contains("placeholder", snapshot.Warnings.Single(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(snapshot.Repositories);
            Assert.Empty(snapshot.Solutions);
            Assert.Empty(snapshot.Nodes);
            Assert.Empty(snapshot.Edges);
            Assert.Empty(snapshot.Evidence);
            Assert.Empty(snapshot.Findings);
            Assert.Empty(snapshot.Metrics);
            Assert.Empty(snapshot.GeneratedSummaries);
        }

        /// <summary>
        /// Verifies the assembler retains blocking errors already captured in the accumulation model.
        /// </summary>
        [Fact]
        public void Assemble_WhenAccumulationHasError_ShouldRetainErrorInSnapshot()
        {
            // Error retention lets later orchestration persist or report controlled stage failures without raw exceptions.
            ResolvedExtractionInput input = CreateResolvedInput();
            ExtractionRun run = CreateRun(input);
            ArchitectureSnapshotAccumulator accumulation = new();
            accumulation.AddError("Controlled stage failure.");
            ExtractionSnapshotAssembler assembler = new();

            var snapshot = assembler.Assemble(run, input, accumulation);

            Assert.Contains("Controlled stage failure.", snapshot.Errors);
        }

        /// <summary>
        /// Creates normalized input with two explicit solution paths for snapshot assembly tests.
        /// </summary>
        /// <returns>A resolved extraction input for a repository with two solutions.</returns>
        private static ResolvedExtractionInput CreateResolvedInput()
        {
            // Absolute paths are already validated by earlier slices, so assembly tests can use deterministic sample paths.
            return new ResolvedExtractionInput(
                "D:/Repositories/CustomerSuite/",
                ["D:/Repositories/CustomerSuite/CustomerSuite.sln", "D:/Repositories/CustomerSuite/tools/Tools.sln"],
                BranchName: "main",
                CommitSha: "abcdef1234567890",
                RequestedBy: "developer@example.invalid",
                Metadata: new Dictionary<string, string>
                {
                    ["source"] = "assembly-test"
                });
        }

        /// <summary>
        /// Creates an accepted run associated with the supplied resolved input.
        /// </summary>
        /// <param name="input">The normalized input represented by the run summary.</param>
        /// <returns>A queued extraction run for snapshot assembly tests.</returns>
        private static ExtractionRun CreateRun(ResolvedExtractionInput input)
        {
            // The run id becomes part of the deterministic snapshot header scope for this accepted extraction.
            return new ExtractionRun(
                new ExtractionRunId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
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
