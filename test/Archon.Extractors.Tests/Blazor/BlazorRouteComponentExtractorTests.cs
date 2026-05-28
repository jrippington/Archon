using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Blazor;
using Xunit;

namespace Archon.Extractors.Tests.Blazor
{
    /// <summary>
    /// Verifies the Blazor route and component extraction slice from repository fixture files into graph-ready snapshot facts.
    /// </summary>
    public sealed class BlazorRouteComponentExtractorTests
    {
        /// <summary>
        /// Confirms a routed Razor component contributes UI application, component, route, layout, injected service, parameter, authorization, relationships, and evidence facts.
        /// </summary>
        /// <returns>A task representing the asynchronous fixture creation and extraction assertion flow.</returns>
        [Fact]
        public async Task ExtractAsyncEmitsRoutedComponentLayoutInjectionParameterAndAuthorizationFacts()
        {
            // The fixture uses a real temporary repository tree so discovery, path normalization, and build-output exclusion run together.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Client");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Pages"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Shared"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "bin", "Debug"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Client.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.BlazorWebAssembly\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Shared", "MainLayout.razor"), "@inherits LayoutComponentBase\n<div>@Body</div>");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "bin", "Debug", "Ignored.razor"), "@page \"/ignored\"");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "Index.razor"), """
@page "/home/{id:int}"
@layout MainLayout
@inject HttpClient Http
@attribute [Authorize(Roles = "Admin")]

<AuthorizeView Roles="Admin">
    <Authorized>Welcome @Title</Authorized>
</AuthorizeView>

@code {
    [Parameter]
    public string? Title { get; set; }
}
""");

                BlazorRouteComponentExtractor extractor = new();
                BlazorRouteComponentExtractionRequest request = new(new StableKey("snapshot://sample/run-1"), repositoryRoot);

                BlazorRouteComponentExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Empty(result.Snapshot.Errors);
                Assert.DoesNotContain(result.Snapshot.Nodes, node => node.DisplayName == "Ignored");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiApplication.Value && node.Metadata.ToCanonicalJson().Contains("\"hostingModel\":\"WebAssembly\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiComponent.Value && node.DisplayName == "Index");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiRoute.Value && node.DisplayName == "/home/{id:int}");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiLayout.Value && node.DisplayName == "MainLayout");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.ConfigurationKey.Value && node.DisplayName == "HttpClient");
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DeclaresComponent.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DeclaresUiRoute.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesLayout.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesConfig.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DependsOn.Value);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Sample.Client/Pages/Index.razor" && evidence.StartLine == 1 && evidence.SnippetHash is not null);
                Assert.Contains(result.Snapshot.Nodes, node => node.Metadata.ToCanonicalJson().Contains("\"routeParameter\":\"id:int\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.Metadata.ToCanonicalJson().Contains("\"authorizationPolicy\":\"Admin\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.Metadata.ToCanonicalJson().Contains("\"componentParameter\":\"Title\"", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Confirms malformed or partial Razor files produce non-fatal warnings and explicit unknown facts instead of blocking extraction.
        /// </summary>
        /// <returns>A task representing the asynchronous fixture creation and extraction assertion flow.</returns>
        [Fact]
        public async Task ExtractAsyncRecordsWarningAndUnknownForPartialRazorDirective()
        {
            // The malformed directive intentionally leaves @page without a template so extraction can preserve the component and record uncertainty.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Client");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Pages"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Client.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Razor\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "Broken.razor"), "@page\n<h1>Broken</h1>");

                BlazorRouteComponentExtractor extractor = new();
                BlazorRouteComponentExtractionRequest request = new(new StableKey("snapshot://sample/run-2"), repositoryRoot);

                BlazorRouteComponentExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("missing a route template", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiRoute.Value && node.UnknownState.HasUnknownData);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.UnknownState.HasUnknownData);
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Confirms interaction extraction emits component composition, UI event, form, validation, render-mode, render-fragment, API, and configuration facts.
        /// </summary>
        /// <returns>A task representing the asynchronous fixture creation and extraction assertion flow.</returns>
        [Fact]
        public async Task ExtractAsyncEmitsInteractionDependencyAndCorrelationFacts()
        {
            // The fixture places several interaction patterns in one component so graph relationships can be validated through the public extraction entry path.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Client");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Pages"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Shared"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Client.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Shared", "FeatureCard.razor"), "<article>@Title</article>\n@code { [Parameter] public string? Title { get; set; } }");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "Dashboard.razor"), """
@page "/dashboard"
@rendermode InteractiveServer
@inject HttpClient Http
@inject IConfiguration Configuration

<FeatureCard Title="Alpha" OnSelected="HandleSelected" />
<FeatureCard Title="Beta" OnSelected="HandleSelected" />
<DynamicComponent Type="@CurrentComponent" />

<EditForm Model="@Model" OnValidSubmit="SaveAsync">
    <DataAnnotationsValidator />
    <ValidationSummary />
    <button @onclick="SaveAsync">Save</button>
</EditForm>

@Body

@code {
    private RenderFragment Body => @<p>Projected</p>;
    private Type? CurrentComponent { get; set; }
    private object Model { get; } = new();

    [Parameter]
    public EventCallback<string> OnSaved { get; set; }

    private async Task SaveAsync()
    {
        string? endpoint = Configuration["Services:Catalog:BaseUrl"];
        await Http.GetFromJsonAsync<object>("https://api.example.test/items");
        await OnSaved.InvokeAsync(endpoint ?? string.Empty);
    }
}
""");

                BlazorRouteComponentExtractor extractor = new();
                BlazorRouteComponentExtractionRequest request = new(new StableKey("snapshot://sample/run-3"), repositoryRoot);

                BlazorRouteComponentExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Empty(result.Snapshot.Errors);
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiComponent.Value && node.DisplayName == "FeatureCard");
                Assert.Equal(1, result.Snapshot.Edges.Count(edge => edge.EdgeKind.Value == EdgeKind.UsesComponent.Value && result.Snapshot.Nodes.Any(node => node.StableKey == edge.TargetNodeStableKey && node.DisplayName == "FeatureCard")));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.HandlesUiEvent.Value && edge.Metadata.ToCanonicalJson().Contains("\"eventName\":\"click\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.HandlesUiEvent.Value && edge.Metadata.ToCanonicalJson().Contains("\"eventName\":\"OnValidSubmit\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiControl.Value && node.DisplayName == "EditForm");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiControl.Value && node.DisplayName == "DataAnnotationsValidator");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiControl.Value && node.DisplayName == "ValidationSummary");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiComponent.Value && node.DisplayName == "Dashboard" && node.Metadata.ToCanonicalJson().Contains("\"renderMode\":\"InteractiveServer\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.Metadata.ToCanonicalJson().Contains("\"renderFragment\":\"Body\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.ExternalService.Value && node.DisplayName == "https://api.example.test/items");
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.CallsApi.Value);
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.ConfigurationKey.Value && node.DisplayName == "Services:Catalog:BaseUrl");
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesConfig.Value && edge.Metadata.ToCanonicalJson().Contains("\"configurationKey\":\"Services:Catalog:BaseUrl\"", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Confirms dynamic or ambiguous Blazor interaction targets are represented as explicit unknown graph facts and warnings.
        /// </summary>
        /// <returns>A task representing the asynchronous fixture creation and extraction assertion flow.</returns>
        [Fact]
        public async Task ExtractAsyncRecordsUnknownsForDynamicComponentRenderModeApiAndConfigurationTargets()
        {
            // Dynamic targets are intentionally unsupported in static extraction; they must remain visible as unknowns rather than guessed links.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Client");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Pages"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Client.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Razor\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "Dynamic.razor"), """
@page "/dynamic"
@rendermode @(Mode)
@inject HttpClient Http
@inject IConfiguration Configuration

<DynamicComponent Type="@CurrentComponent" />

@code {
    private string Mode { get; } = "InteractiveServer";
    private Type? CurrentComponent { get; set; }
    private string ConfigKey { get; } = "Services:Dynamic:BaseUrl";
    private string ApiPath { get; } = "/computed";

    private async Task LoadAsync()
    {
        string? endpoint = Configuration[ConfigKey];
        await Http.GetAsync(ApiPath);
    }
}
""");

                BlazorRouteComponentExtractor extractor = new();
                BlazorRouteComponentExtractionRequest request = new(new StableKey("snapshot://sample/run-4"), repositoryRoot);

                BlazorRouteComponentExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("dynamic component", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("computed render mode", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("computed API", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("computed configuration", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "DynamicComponent type is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "Render mode is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "API target is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "Configuration key is computed from runtime state.");
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Creates an empty temporary repository root for an extraction fixture.
        /// </summary>
        /// <returns>The absolute path to the temporary repository root.</returns>
        private static string CreateTemporaryRepositoryRoot()
        {
            // A GUID segment prevents tests from sharing paths while keeping cleanup simple and deterministic for each test invocation.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "ArchonBlazorExtractorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repositoryRoot);
            return repositoryRoot;
        }
    }
}