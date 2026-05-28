using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Domain.Graph.ControlledValues;
using Archon.Extractors.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Archon.Api.Extraction.Tests
{
    /// <summary>
    /// Verifies the UI/client Avalonia AXAML extraction stage participates in API-triggered extraction orchestration.
    /// </summary>
    public sealed class AvaloniaAxamlExtractionStageTests
    {
        /// <summary>
        /// Verifies API module registration keeps the UI/client Avalonia AXAML adapter available to the unified UI/client stage.
        /// </summary>
        [Fact]
        public void AddArchonExtractionApi_ShouldRegisterUiClientAvaloniaStage()
        {
            // The API module exposes one unified UI/client pipeline stage while keeping the Avalonia adapter resolvable for direct stage tests and unified orchestration.
            ServiceProvider provider = new ServiceCollection()
                .AddArchonExtractionApi()
                .BuildServiceProvider();

            AvaloniaAxamlExtractionStage stage = provider.GetRequiredService<AvaloniaAxamlExtractionStage>();

            Assert.Equal("wp011-avalonia-axaml", stage.StageId);
        }

        /// <summary>
        /// Verifies the stage merges Avalonia UI facts into the shared snapshot accumulator.
        /// </summary>
        /// <returns>A task that completes after the stage execution and snapshot assertions finish.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenAvaloniaProjectExists_ShouldAccumulateUiFacts()
        {
            // The stage test uses the real extractor with a minimal repository fixture so the API seam and extractor seam are validated together.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "ArchonUiClientAvaloniaStageTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Sample.Avalonia"));
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Avalonia");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Avalonia.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Avalonia\" Version=\"11.3.0\" /></ItemGroup></Project>");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "App.axaml"), "<Application x:Class=\"Sample.Avalonia.App\" xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MainWindow.axaml"), "<Window x:Class=\"Sample.Avalonia.MainWindow\" xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Button x:Name=\"saveButton\" /></Window>");
                AvaloniaAxamlExtractionStage stage = new(new AvaloniaAxamlExtractor(), NullLogger<AvaloniaAxamlExtractionStage>.Instance);
                ExtractionStageContext context = CreateContext(repositoryRoot);

                ExtractionStageResult result = await stage.ExecuteAsync(context, CancellationToken.None);
                ExtractedArchitectureSnapshot snapshot = context.Accumulation.ToSnapshot();

                Assert.False(result.HasBlockingError);
                Assert.Contains(snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiApplication.Value && node.Metadata.ToCanonicalJson().Contains("\"uiFramework\":\"Avalonia\"", StringComparison.Ordinal));
                Assert.Contains(snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiView.Value && node.DisplayName == "MainWindow");
                Assert.Contains(snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiControl.Value && node.DisplayName == "saveButton");
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DeclaresComponent.Value);
                Assert.Contains(snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesControl.Value);
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Creates a pipeline stage context suitable for UI/client Avalonia API stage tests.
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
                new ExtractionRunProgress("Queued", "Queued for UI/client Avalonia stage test.", 0, new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero)),
                warnings: null,
                errors: null,
                timings: null,
                snapshotIdentity: null);

            return new ExtractionStageContext(input, run, new Archon.Application.Extraction.Accumulation.ArchitectureSnapshotAccumulator());
        }
    }
}
