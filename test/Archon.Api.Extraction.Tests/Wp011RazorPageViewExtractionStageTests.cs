using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Domain.Graph.ControlledValues;
using Archon.Extractors.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Archon.Api.Extraction.Tests
{
    /// <summary>
    /// Verifies the WP011 Razor Pages and MVC Razor extraction stage participates in API-triggered extraction orchestration.
    /// </summary>
    public sealed class Wp011RazorPageViewExtractionStageTests
    {
        /// <summary>
        /// Verifies API module registration keeps the WP011 Razor Pages and MVC Razor adapter available to the unified UI/client stage.
        /// </summary>
        [Fact]
        public void AddArchonExtractionApi_ShouldRegisterWp011RazorStage()
        {
            // The API module exposes one unified WP011 pipeline stage while keeping the Razor adapter resolvable for direct stage tests and unified orchestration.
            ServiceProvider provider = new ServiceCollection()
                .AddArchonExtractionApi()
                .BuildServiceProvider();

            Wp011RazorPageViewExtractionStage stage = provider.GetRequiredService<Wp011RazorPageViewExtractionStage>();

            Assert.Equal("wp011-razor-page-view", stage.StageId);
        }

        /// <summary>
        /// Verifies the stage merges Razor Pages facts into the shared snapshot accumulator.
        /// </summary>
        /// <returns>A task that completes after the stage execution and snapshot assertions finish.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenRazorPageExists_ShouldAccumulateUiFacts()
        {
            // The stage test uses the real extractor with a minimal repository fixture so the API seam and extractor seam are validated together.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "ArchonWp011RazorStageTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Sample.Web", "Pages"));
            try
            {
                await File.WriteAllTextAsync(Path.Combine(repositoryRoot, "src", "Sample.Web", "Sample.Web.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
                await File.WriteAllTextAsync(Path.Combine(repositoryRoot, "src", "Sample.Web", "Pages", "Index.cshtml"), "@page \"/\"\n@model Sample.Web.Pages.IndexModel\n<h1>Hello</h1>");
                Wp011RazorPageViewExtractionStage stage = new(new RazorPageViewExtractor(), NullLogger<Wp011RazorPageViewExtractionStage>.Instance);
                ExtractionStageContext context = CreateContext(repositoryRoot);

                ExtractionStageResult result = await stage.ExecuteAsync(context, CancellationToken.None);
                ExtractedArchitectureSnapshot snapshot = context.Accumulation.ToSnapshot();

                Assert.False(result.HasBlockingError);
                Assert.Contains(snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiPage.Value);
                Assert.Contains(snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiRoute.Value);
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DeclaresUiRoute.Value);
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Creates a pipeline stage context suitable for WP011 Razor API stage tests.
        /// </summary>
        /// <param name="repositoryRoot">The temporary repository root used by the stage test.</param>
        /// <returns>A stage context with normalized repository input and an empty accumulator.</returns>
        private static ExtractionStageContext CreateContext(string repositoryRoot)
        {
            // The context mirrors an accepted API-triggered extraction run without requiring endpoint hosting.
            ResolvedExtractionInput input = new(
                repositoryRoot,
                [],
                BranchName: "main",
                CommitSha: "abcdef1234567890",
                RequestedBy: "developer@example.invalid",
                Metadata: new Dictionary<string, string>());
            ExtractionRun run = new(
                ExtractionRunId.New(),
                ExtractionRunStatus.Queued,
                new ExtractionRunRequestSummary(input.RepositoryRootDirectory, input.SolutionPaths, input.BranchName, input.CommitSha, input.RequestedBy, input.Metadata.Keys.ToArray()),
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                completedUtc: null,
                new ExtractionRunProgress("Queued", "Queued for WP011 Razor stage test.", 0, new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero)),
                warnings: null,
                errors: null,
                timings: null,
                snapshotIdentity: null);

            return new ExtractionStageContext(input, run, new Archon.Application.Extraction.Accumulation.ArchitectureSnapshotAccumulator());
        }
    }
}