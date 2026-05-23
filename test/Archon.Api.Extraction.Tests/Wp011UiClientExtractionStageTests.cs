using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Domain.Graph.ControlledValues;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Archon.Api.Extraction.Tests
{
    /// <summary>
    /// Verifies unified WP011 UI/client extraction stage registration and mixed-framework API orchestration behavior.
    /// </summary>
    public sealed class Wp011UiClientExtractionStageTests
    {
        /// <summary>
        /// Verifies the API module exposes one unified WP011 UI/client pipeline stage while framework-specific adapters remain concrete services.
        /// </summary>
        [Fact]
        public void AddArchonExtractionApi_ShouldRegisterUnifiedWp011UiClientStageOnlyAsPipelineStage()
        {
            // The unified stage is the only IExtractionStage entry for WP011 so API-triggered orchestration sees one UI/client slice after WP010.
            ServiceProvider provider = new ServiceCollection()
                .AddArchonExtractionApi()
                .BuildServiceProvider();

            IExtractionStage[] stages = provider.GetServices<IExtractionStage>().ToArray();
            string[] wp011StageIds = stages.Select(stage => stage.StageId).Where(stageId => stageId.StartsWith("wp011-", StringComparison.Ordinal)).ToArray();

            Assert.Contains(stages, stage => stage is Wp011UiClientExtractionStage);
            Assert.Equal(["wp011-ui-client"], wp011StageIds);
            Assert.NotNull(provider.GetRequiredService<Wp011BlazorRouteComponentExtractionStage>());
            Assert.NotNull(provider.GetRequiredService<Wp011RazorPageViewExtractionStage>());
            Assert.NotNull(provider.GetRequiredService<Wp011WinFormsStaticUiExtractionStage>());
            Assert.NotNull(provider.GetRequiredService<Wp011WpfXamlExtractionStage>());
            Assert.NotNull(provider.GetRequiredService<Wp011WinUiXamlExtractionStage>());
            Assert.NotNull(provider.GetRequiredService<Wp011MauiXamlExtractionStage>());
            Assert.NotNull(provider.GetRequiredService<Wp011AvaloniaAxamlExtractionStage>());
        }

        /// <summary>
        /// Verifies unified WP011 extraction accumulates mixed UI frameworks, redacts evidence, preserves unknowns and warnings, and deduplicates repeated stable-key facts.
        /// </summary>
        /// <returns>A task that completes after the unified stage has executed and snapshot assertions have finished.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenMixedUiRepositoryExists_ShouldAccumulateDeduplicatedSnapshotFacts()
        {
            // The fixture intentionally combines web, desktop, and cross-platform client artifacts so the unified API stage proves framework adapters can cooperate in one snapshot.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "ArchonWp011UnifiedStageTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repositoryRoot);
            try
            {
                await CreateMixedUiRepositoryAsync(repositoryRoot);
                ServiceProvider provider = new ServiceCollection()
                    .AddArchonExtractionApi()
                    .BuildServiceProvider();
                IExtractionStage stage = provider.GetServices<IExtractionStage>().Single(candidate => string.Equals(candidate.StageId, "wp011-ui-client", StringComparison.Ordinal));
                ExtractionStageContext context = CreateContext(repositoryRoot);

                ExtractionStageResult firstResult = await stage.ExecuteAsync(context, CancellationToken.None);
                ExtractedArchitectureSnapshot firstSnapshot = context.Accumulation.ToSnapshot();
                int nodeCountAfterFirstRun = firstSnapshot.Nodes.Count;
                int edgeCountAfterFirstRun = firstSnapshot.Edges.Count;
                int evidenceCountAfterFirstRun = firstSnapshot.Evidence.Count;

                ExtractionStageResult secondResult = await stage.ExecuteAsync(context, CancellationToken.None);
                ExtractedArchitectureSnapshot secondSnapshot = context.Accumulation.ToSnapshot();

                Assert.False(firstResult.HasBlockingError);
                Assert.False(secondResult.HasBlockingError);
                Assert.Equal(nodeCountAfterFirstRun, secondSnapshot.Nodes.Count);
                Assert.Equal(edgeCountAfterFirstRun, secondSnapshot.Edges.Count);
                Assert.Equal(evidenceCountAfterFirstRun, secondSnapshot.Evidence.Count);
                Assert.Contains("wp011-ui-client", stage.StageId, StringComparison.Ordinal);
                Assert.Contains(firstSnapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiComponent.Value && HasMetadataValue(node.Metadata.ToCanonicalJson(), "uiFramework", "Blazor"));
                Assert.Contains(firstSnapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiPage.Value && HasMetadataValue(node.Metadata.ToCanonicalJson(), "uiFramework", "RazorPages"));
                Assert.Contains(firstSnapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiView.Value && HasMetadataValue(node.Metadata.ToCanonicalJson(), "uiFramework", "WinForms"));
                Assert.Contains(firstSnapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiView.Value && HasMetadataValue(node.Metadata.ToCanonicalJson(), "uiFramework", "Wpf"));
                Assert.Contains(firstSnapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiView.Value && HasMetadataValue(node.Metadata.ToCanonicalJson(), "uiFramework", "WinUI"));
                Assert.Contains(firstSnapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiPage.Value && HasMetadataValue(node.Metadata.ToCanonicalJson(), "uiFramework", "Maui"));
                Assert.Contains(firstSnapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiView.Value && HasMetadataValue(node.Metadata.ToCanonicalJson(), "uiFramework", "Avalonia"));
                Assert.Contains(firstSnapshot.Nodes, node => node.NodeKind.Value == NodeKind.ConfigurationKey.Value);
                Assert.Contains(firstSnapshot.Nodes, node => node.NodeKind.Value == NodeKind.ExternalService.Value);
                Assert.Contains(firstSnapshot.Nodes, node => node.NodeKind.Value == NodeKind.Binding.Value);
                Assert.Contains(firstSnapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesConfig.Value);
                Assert.Contains(firstSnapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.CallsApi.Value || edge.EdgeKind.Value == EdgeKind.CallsExternalService.Value);
                Assert.Contains(firstSnapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DependsOn.Value || edge.EdgeKind.Value == EdgeKind.UsesDbContext.Value);
                Assert.Contains(firstSnapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DeclaresComponent.Value);
                Assert.Contains(firstSnapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesControl.Value);
                Assert.Contains(firstSnapshot.Evidence, evidence => evidence.SnippetPreview is not null && evidence.SnippetPreview.Contains("[REDACTED]", StringComparison.Ordinal));
                Assert.DoesNotContain(firstSnapshot.Evidence, evidence => evidence.SnippetPreview is not null && evidence.SnippetPreview.Contains("super-secret", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(firstSnapshot.Warnings, warning => warning.Contains("dynamic", StringComparison.OrdinalIgnoreCase) || warning.Contains("runtime", StringComparison.OrdinalIgnoreCase) || warning.Contains("computed", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(firstSnapshot.Nodes, node => node.UnknownState.HasUnknownData || node.Metadata.ToCanonicalJson().Contains("unknownReason", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Creates a mixed UI repository fixture containing representative artifacts for every WP011 framework extractor.
        /// </summary>
        /// <param name="repositoryRoot">The temporary repository root where fixture files should be written.</param>
        /// <returns>A task that completes after all fixture files have been written.</returns>
        private static async Task CreateMixedUiRepositoryAsync(string repositoryRoot)
        {
            // Each fixture is deliberately small and static so tests exercise extraction behavior without restoring, building, rendering, or launching target applications.
            await CreateBlazorFixtureAsync(repositoryRoot);
            await CreateRazorFixtureAsync(repositoryRoot);
            await CreateWinFormsFixtureAsync(repositoryRoot);
            await CreateWpfFixtureAsync(repositoryRoot);
            await CreateWinUiFixtureAsync(repositoryRoot);
            await CreateMauiFixtureAsync(repositoryRoot);
            await CreateAvaloniaFixtureAsync(repositoryRoot);
        }

        /// <summary>
        /// Creates a Blazor fixture with route, configuration, external API, and secret-like evidence content.
        /// </summary>
        /// <param name="repositoryRoot">The temporary repository root where the Blazor project should be created.</param>
        /// <returns>A task that completes after the Blazor fixture files have been written.</returns>
        private static async Task CreateBlazorFixtureAsync(string repositoryRoot)
        {
            // The component includes deterministic links plus a token-like value so the unified test can assert redaction through the real evidence path.
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Blazor", "Pages");
            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllTextAsync(Path.Combine(repositoryRoot, "src", "Sample.Blazor", "Sample.Blazor.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.BlazorWebAssembly\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Index.razor"), "@page \"/\"\n@inject IConfiguration Configuration\n@inject HttpClient Http\n<button @onclick=\"SaveAsync\">Save</button>\n<p token=\"super-secret\">secret</p>\n@code { private async Task SaveAsync() { var apiKey = Configuration[\"Payments:ApiKey\"]; await Http.GetAsync(\"https://api.example.invalid/orders\"); } }");
        }

        /// <summary>
        /// Creates a Razor Pages fixture with a page route and simple markup.
        /// </summary>
        /// <param name="repositoryRoot">The temporary repository root where the Razor project should be created.</param>
        /// <returns>A task that completes after the Razor fixture files have been written.</returns>
        private static async Task CreateRazorFixtureAsync(string repositoryRoot)
        {
            // Razor Pages supply a server-rendered UI framework that should coexist with Blazor in the same unified snapshot.
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Razor", "Pages");
            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllTextAsync(Path.Combine(repositoryRoot, "src", "Sample.Razor", "Sample.Razor.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Details.cshtml"), "@page \"/details/{id}\"\n@model Sample.Razor.Pages.DetailsModel\n<a href=\"/orders\">Orders</a>");
        }

        /// <summary>
        /// Creates a Windows Forms fixture with a form, designer control, binding, and service/data-access-looking dependencies.
        /// </summary>
        /// <param name="repositoryRoot">The temporary repository root where the Windows Forms project should be created.</param>
        /// <returns>A task that completes after the Windows Forms fixture files have been written.</returns>
        private static async Task CreateWinFormsFixtureAsync(string repositoryRoot)
        {
            // Designer and code-behind files are paired by partial type name to exercise static legacy desktop extraction.
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.WinForms");
            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.WinForms.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Program.cs"), "namespace Sample.WinForms { internal static class Program { public static void Main() { System.Windows.Forms.Application.Run(new MainForm()); } } }");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MainForm.cs"), "namespace Sample.WinForms { public partial class MainForm : System.Windows.Forms.Form { private readonly OrderService _service = new(); private readonly AppDbContext _db = new(); public MainForm() { InitializeComponent(); saveButton.Click += SaveButton_Click; } private void SaveButton_Click(object? sender, System.EventArgs e) { _service.Send(); } } internal sealed class OrderService { public void Send() { } } internal sealed class AppDbContext { } }");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MainForm.Designer.cs"), "namespace Sample.WinForms { public partial class MainForm { private System.Windows.Forms.Button saveButton; private void InitializeComponent() { saveButton = new System.Windows.Forms.Button(); saveButton.DataBindings.Add(\"Text\", this, \"Secret\"); Controls.Add(saveButton); } } }");
        }

        /// <summary>
        /// Creates a WPF fixture with XAML binding and dynamic-resource unknown evidence.
        /// </summary>
        /// <param name="repositoryRoot">The temporary repository root where the WPF project should be created.</param>
        /// <returns>A task that completes after the WPF fixture files have been written.</returns>
        private static async Task CreateWpfFixtureAsync(string repositoryRoot)
        {
            // WPF contributes XAML views, controls, bindings, and dynamic-resource warnings through the same unified stage.
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Wpf");
            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Wpf.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "App.xaml"), "<Application x:Class=\"Sample.Wpf.App\" StartupUri=\"MainWindow.xaml\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MainWindow.xaml"), "<Window x:Class=\"Sample.Wpf.MainWindow\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Grid><Button x:Name=\"saveButton\" Content=\"{Binding SaveText}\" Background=\"{DynamicResource RuntimeBrush}\" /></Grid></Window>");
        }

        /// <summary>
        /// Creates a WinUI fixture with XAML page/window artifacts and package metadata.
        /// </summary>
        /// <param name="repositoryRoot">The temporary repository root where the WinUI project should be created.</param>
        /// <returns>A task that completes after the WinUI fixture files have been written.</returns>
        private static async Task CreateWinUiFixtureAsync(string repositoryRoot)
        {
            // WinUI contributes modern Windows desktop facts without requiring Windows App SDK runtime execution.
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.WinUI");
            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.WinUI.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-windows10.0.19041.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Microsoft.WindowsAppSDK\" Version=\"1.7.0\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "App.xaml"), "<Application x:Class=\"Sample.WinUI.App\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MainWindow.xaml"), "<Window x:Class=\"Sample.WinUI.MainWindow\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Frame x:Name=\"RootFrame\" /></Window>");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Package.appxmanifest"), "<Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\"><Identity Name=\"Sample.WinUI\" Publisher=\"CN=Test\" Version=\"1.0.0.0\" /></Package>");
        }

        /// <summary>
        /// Creates a .NET MAUI fixture with Shell route and page binding artifacts.
        /// </summary>
        /// <param name="repositoryRoot">The temporary repository root where the MAUI project should be created.</param>
        /// <returns>A task that completes after the MAUI fixture files have been written.</returns>
        private static async Task CreateMauiFixtureAsync(string repositoryRoot)
        {
            // MAUI adds cross-platform page and Shell-route facts while staying fully static in the test fixture.
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Maui");
            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Maui.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><UseMaui>true</UseMaui></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MauiProgram.cs"), "namespace Sample.Maui { public static class MauiProgram { public static object CreateMauiApp() { Routing.RegisterRoute(\"orders\", typeof(OrdersPage)); return new object(); } } }");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "AppShell.xaml"), "<Shell x:Class=\"Sample.Maui.AppShell\" xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2009/xaml\"><ShellContent Route=\"home\" ContentTemplate=\"{DataTemplate local:OrdersPage}\" /></Shell>");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "OrdersPage.xaml"), "<ContentPage x:Class=\"Sample.Maui.OrdersPage\" xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2009/xaml\"><Button Text=\"{Binding SaveText}\" Command=\"{Binding SaveCommand}\" /></ContentPage>");
        }

        /// <summary>
        /// Creates an Avalonia fixture with AXAML view, style, command, and ReactiveUI metadata.
        /// </summary>
        /// <param name="repositoryRoot">The temporary repository root where the Avalonia project should be created.</param>
        /// <returns>A task that completes after the Avalonia fixture files have been written.</returns>
        private static async Task CreateAvaloniaFixtureAsync(string repositoryRoot)
        {
            // Avalonia adds AXAML and ReactiveUI-aware facts to the same unified snapshot as the other UI frameworks.
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Avalonia");
            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Avalonia.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Avalonia\" Version=\"11.3.0\" /><PackageReference Include=\"Avalonia.ReactiveUI\" Version=\"11.3.0\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Program.cs"), "namespace Sample.Avalonia { internal static class Program { public static void Main() { BuildAvaloniaApp().StartWithClassicDesktopLifetime(System.Array.Empty<string>()); } private static object BuildAvaloniaApp() => new object(); } }");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "App.axaml"), "<Application x:Class=\"Sample.Avalonia.App\" xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MainWindow.axaml"), "<Window x:Class=\"Sample.Avalonia.MainWindow\" xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Button x:Name=\"saveButton\" Command=\"{Binding SaveCommand}\" /></Window>");
        }

        /// <summary>
        /// Creates a pipeline stage context suitable for unified WP011 API stage tests.
        /// </summary>
        /// <param name="repositoryRoot">The temporary repository root used by the unified stage test.</param>
        /// <returns>A stage context with normalized repository input and an empty accumulator.</returns>
        private static ExtractionStageContext CreateContext(string repositoryRoot)
        {
            // The context mirrors an accepted API-triggered extraction run without requiring endpoint hosting or persistence adapters.
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
                new ExtractionRunProgress("Queued", "Queued for unified WP011 UI/client stage test.", 0, new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero)),
                warnings: null,
                errors: null,
                snapshotIdentity: null);

            return new ExtractionStageContext(input, run, new Archon.Application.Extraction.Accumulation.ArchitectureSnapshotAccumulator());
        }

        /// <summary>
        /// Determines whether canonical metadata contains a specific string property value.
        /// </summary>
        /// <param name="canonicalJson">The canonical metadata JSON emitted by a graph fact.</param>
        /// <param name="propertyName">The metadata property name to match.</param>
        /// <param name="propertyValue">The metadata property value to match.</param>
        /// <returns><see langword="true" /> when the canonical metadata contains the requested property/value pair; otherwise, <see langword="false" />.</returns>
        private static bool HasMetadataValue(string canonicalJson, string propertyName, string propertyValue)
        {
            // Canonical metadata is compact JSON, so an ordinal substring check is sufficient for these controlled test values.
            return canonicalJson.Contains($"\"{propertyName}\":\"{propertyValue}\"", StringComparison.Ordinal);
        }
    }
}
