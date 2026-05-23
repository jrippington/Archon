using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Domain.Graph.ControlledValues;
using Archon.Extractors.Blazor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Archon.Api.Extraction.Tests
{
    /// <summary>
    /// Verifies the WP011 Blazor extraction stage participates in API-triggered extraction orchestration.
    /// </summary>
    public sealed class Wp011BlazorRouteComponentExtractionStageTests
    {
        /// <summary>
        /// Verifies API module registration keeps the WP011 Blazor adapter available to the unified UI/client stage.
        /// </summary>
        [Fact]
        public void AddArchonExtractionApi_ShouldRegisterWp011BlazorStage()
        {
            // The API module exposes one unified WP011 pipeline stage while keeping the Blazor adapter resolvable for direct stage tests and unified orchestration.
            ServiceProvider provider = new ServiceCollection()
                .AddArchonExtractionApi()
                .BuildServiceProvider();

            Wp011BlazorRouteComponentExtractionStage stage = provider.GetRequiredService<Wp011BlazorRouteComponentExtractionStage>();

            Assert.Equal("wp011-blazor-route-component", stage.StageId);
        }

        /// <summary>
        /// Verifies the stage merges Blazor route and component facts into the shared snapshot accumulator.
        /// </summary>
        /// <returns>A task that completes after the stage execution and snapshot assertions finish.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenRazorComponentExists_ShouldAccumulateUiFacts()
        {
            // The stage test uses the real extractor with a minimal repository fixture so the API seam and extractor seam are validated together.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "ArchonWp011StageTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Sample.Client", "Pages"));
            try
            {
                await File.WriteAllTextAsync(Path.Combine(repositoryRoot, "src", "Sample.Client", "Sample.Client.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.BlazorWebAssembly\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
                await File.WriteAllTextAsync(Path.Combine(repositoryRoot, "src", "Sample.Client", "Pages", "Index.razor"), "@page \"/\"\n<h1>Hello</h1>");
                Wp011BlazorRouteComponentExtractionStage stage = new(new BlazorRouteComponentExtractor(), NullLogger<Wp011BlazorRouteComponentExtractionStage>.Instance);
                ExtractionStageContext context = CreateContext(repositoryRoot);

                ExtractionStageResult result = await stage.ExecuteAsync(context, CancellationToken.None);
                ExtractedArchitectureSnapshot snapshot = context.Accumulation.ToSnapshot();

                Assert.False(result.HasBlockingError);
                Assert.Contains(snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiComponent.Value);
                Assert.Contains(snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiRoute.Value);
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DeclaresUiRoute.Value);
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Creates a pipeline stage context suitable for WP011 API stage tests.
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
                new ExtractionRunProgress("Queued", "Queued for WP011 stage test.", 0, new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero)),
                warnings: null,
                errors: null,
                snapshotIdentity: null);

            return new ExtractionStageContext(input, run, new Archon.Application.Extraction.Accumulation.ArchitectureSnapshotAccumulator());
        }
    }
}