using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Domain.Graph.ControlledValues;
using Archon.Extractors.WinForms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Archon.Api.Extraction.Tests
{
    /// <summary>
    /// Verifies the WP011 Windows Forms static UI extraction stage participates in API-triggered extraction orchestration.
    /// </summary>
    public sealed class Wp011WinFormsStaticUiExtractionStageTests
    {
        /// <summary>
        /// Verifies API module registration keeps the WP011 Windows Forms static UI adapter available to the unified UI/client stage.
        /// </summary>
        [Fact]
        public void AddArchonExtractionApi_ShouldRegisterWp011WinFormsStage()
        {
            // The API module exposes one unified WP011 pipeline stage while keeping the Windows Forms adapter resolvable for direct stage tests and unified orchestration.
            ServiceProvider provider = new ServiceCollection()
                .AddArchonExtractionApi()
                .BuildServiceProvider();

            Wp011WinFormsStaticUiExtractionStage stage = provider.GetRequiredService<Wp011WinFormsStaticUiExtractionStage>();

            Assert.Equal("wp011-winforms-static-ui", stage.StageId);
        }

        /// <summary>
        /// Verifies the stage merges Windows Forms UI facts into the shared snapshot accumulator.
        /// </summary>
        /// <returns>A task that completes after the stage execution and snapshot assertions finish.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenWinFormsProjectExists_ShouldAccumulateUiFacts()
        {
            // The stage test uses the real extractor with a minimal repository fixture so the API seam and extractor seam are validated together.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "ArchonWp011WinFormsStageTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Sample.WinForms"));
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.WinForms");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.WinForms.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms></PropertyGroup></Project>");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Program.cs"), "using System.Windows.Forms; namespace Sample.WinForms { internal static class Program { private static void Main() { Application.Run(new MainForm()); } } }");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MainForm.cs"), "using System.Windows.Forms; namespace Sample.WinForms { public partial class MainForm : Form { public MainForm() { InitializeComponent(); } } }");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MainForm.Designer.cs"), "namespace Sample.WinForms { partial class MainForm { private System.Windows.Forms.Button saveButton; private void InitializeComponent() { this.saveButton = new System.Windows.Forms.Button(); this.Controls.Add(this.saveButton); } } }");
                Wp011WinFormsStaticUiExtractionStage stage = new(new WinFormsStaticUiExtractor(), NullLogger<Wp011WinFormsStaticUiExtractionStage>.Instance);
                ExtractionStageContext context = CreateContext(repositoryRoot);

                ExtractionStageResult result = await stage.ExecuteAsync(context, CancellationToken.None);
                ExtractedArchitectureSnapshot snapshot = context.Accumulation.ToSnapshot();

                Assert.False(result.HasBlockingError);
                Assert.Contains(snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiApplication.Value && node.Metadata.ToCanonicalJson().Contains("\"uiFramework\":\"WinForms\"", StringComparison.Ordinal));
                Assert.Contains(snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiView.Value && node.DisplayName == "MainForm");
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
        /// Creates a pipeline stage context suitable for WP011 Windows Forms API stage tests.
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
                new ExtractionRunProgress("Queued", "Queued for WP011 Windows Forms stage test.", 0, new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero)),
                warnings: null,
                errors: null,
                snapshotIdentity: null);

            return new ExtractionStageContext(input, run, new Archon.Application.Extraction.Accumulation.ArchitectureSnapshotAccumulator());
        }
    }
}
