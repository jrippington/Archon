using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Xunit;

namespace Archon.Extractors.Maui.Tests
{
    /// <summary>
    /// Verifies the WP011 .NET MAUI XAML extraction slice from repository fixture files into graph-ready snapshot facts.
    /// </summary>
    public sealed class MauiXamlExtractorTests
    {
        /// <summary>
        /// Confirms a MAUI project contributes application, Shell, route, page, view, handler, platform-head, resource, style, binding, command, navigation, view-model, service, data-access, evidence, and relationship facts.
        /// </summary>
        /// <returns>A task representing asynchronous fixture creation, extraction, and graph assertion flow.</returns>
        [Fact]
        public async Task ExtractAsyncEmitsMauiApplicationShellPageRouteResourceBindingCommandPlatformHeadAndDependencyFacts()
        {
            // The fixture uses a real temporary repository tree so project detection, XAML discovery, platform-head discovery, redaction, and build-output exclusion run together without starting a MAUI runtime.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Maui");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Pages"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Views"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Resources", "Styles"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "ViewModels"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Platforms", "Android"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Platforms", "iOS"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "bin", "Debug"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Maui.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0-android;net10.0-ios;net10.0-maccatalyst;net10.0-windows10.0.19041.0</TargetFrameworks>
    <UseMaui>true</UseMaui>
    <SingleProject>true</SingleProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls" Version="10.0.0" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="6.1.1" />
    <MauiXaml Include="App.xaml" />
    <MauiXaml Include="AppShell.xaml" />
    <MauiXaml Include="Pages\MainPage.xaml" />
  </ItemGroup>
</Project>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MauiProgram.cs"), """
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;

namespace Sample.Maui
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            MauiAppBuilder builder = MauiApp.CreateBuilder();
            builder.UseMauiApp<App>();
            builder.ConfigureMauiHandlers(handlers =>
            {
                handlers.AddHandler(typeof(CustomMap), typeof(CustomMapHandler));
            });
            builder.Services.AddSingleton<OrderService>();
            return builder.Build();
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "App.xaml"), """
<Application x:Class="Sample.Maui.App"
             xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Resources/Styles/Colors.xaml" />
            </ResourceDictionary.MergedDictionaries>
            <Color x:Key="AccentColor">#006CBE</Color>
            <Style x:Key="PrimaryButtonStyle" TargetType="Button" />
        </ResourceDictionary>
    </Application.Resources>
</Application>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "App.xaml.cs"), """
namespace Sample.Maui
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            MainPage = new AppShell();
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "AppShell.xaml"), """
<Shell x:Class="Sample.Maui.AppShell"
       xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
       xmlns:pages="clr-namespace:Sample.Maui.Pages">
    <ShellContent Title="Home" Route="main" ContentTemplate="{DataTemplate pages:MainPage}" />
</Shell>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "AppShell.xaml.cs"), """
namespace Sample.Maui
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute("details", typeof(DetailsPage));
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "MainPage.xaml"), """
<ContentPage x:Class="Sample.Maui.Pages.MainPage"
             xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:Sample.Maui.ViewModels"
             xmlns:views="clr-namespace:Sample.Maui.Views"
             Shell.Route="main">
    <ContentPage.BindingContext>
        <vm:MainViewModel />
    </ContentPage.BindingContext>
    <VerticalStackLayout>
        <Label x:Name="CustomerNameLabel" Text="{Binding CustomerName}" TextColor="{StaticResource AccentColor}" />
        <Button x:Name="SaveButton" Text="Save" Command="{Binding SaveCommand}" Clicked="SaveButton_Clicked" Style="{StaticResource PrimaryButtonStyle}" />
        <views:CustomerCard />
    </VerticalStackLayout>
</ContentPage>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "MainPage.xaml.cs"), """
using Microsoft.Data.SqlClient;

namespace Sample.Maui.Pages
{
    public partial class MainPage : ContentPage
    {
        private readonly OrderService _service = new OrderService();

        public MainPage()
        {
            InitializeComponent();
            Shell.Current.GoToAsync("details");
            using SqlConnection connection = new("Server=(local);Database=Orders;Password=very-secret;");
        }

        private void SaveButton_Clicked(object sender, EventArgs e)
        {
            _service.Save();
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "DetailsPage.xaml"), """
<ContentPage x:Class="Sample.Maui.Pages.DetailsPage"
             xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
    <Label Text="{Binding DetailsText}" />
</ContentPage>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "CustomerCard.xaml"), """
<ContentView x:Class="Sample.Maui.Views.CustomerCard"
             xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
    <Label x:Name="NameBlock" Text="{Binding CustomerName}" />
</ContentView>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Resources", "Styles", "Colors.xaml"), """
<ResourceDictionary xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
    <Color x:Key="SecondaryColor">#003E73</Color>
</ResourceDictionary>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "ViewModels", "MainViewModel.cs"), """
namespace Sample.Maui.ViewModels
{
    public sealed class MainViewModel
    {
        public string CustomerName { get; set; } = string.Empty;
        public string DetailsText { get; set; } = string.Empty;
        public object SaveCommand { get; } = new object();
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "OrderService.cs"), """
namespace Sample.Maui
{
    public sealed class OrderService
    {
        public void Save() { }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Platforms", "Android", "MainActivity.cs"), """
namespace Sample.Maui
{
    public sealed class MainActivity
    {
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Platforms", "iOS", "AppDelegate.cs"), """
namespace Sample.Maui
{
    public sealed class AppDelegate
    {
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "bin", "Debug", "Ignored.xaml"), """
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui" x:Class="Sample.Maui.Ignored" />
""");

                MauiXamlExtractor extractor = new();
                MauiXamlExtractionRequest request = new(new StableKey("snapshot://sample/maui"), repositoryRoot);

                MauiXamlExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Empty(result.Snapshot.Errors);
                Assert.DoesNotContain(result.Snapshot.Nodes, node => node.DisplayName == "Ignored");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiApplication.Value && node.Metadata.ToCanonicalJson().Contains("\"uiFramework\":\"Maui\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiApplication.Value && node.Metadata.ToCanonicalJson().Contains("\"platformHead\":\"Android,iOS,MacCatalyst,Windows\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiLayout.Value && node.DisplayName == "AppShell");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiRoute.Value && node.DisplayName == "main");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiRoute.Value && node.DisplayName == "details");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiPage.Value && node.DisplayName == "MainPage");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiPage.Value && node.DisplayName == "DetailsPage");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiComponent.Value && node.DisplayName == "CustomerCard");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiControl.Value && node.DisplayName == "SaveButton");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiResource.Value && node.DisplayName == "AccentColor");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiStyle.Value && node.DisplayName == "PrimaryButtonStyle");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Binding.Value && node.Metadata.ToCanonicalJson().Contains("\"bindingPath\":\"CustomerName\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Command.Value && node.DisplayName == "SaveCommand");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Command.Value && node.DisplayName == "CustomMapHandler");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.ViewModel.Value && node.DisplayName == "MainViewModel");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Type.Value && node.DisplayName == "OrderService");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.ExternalService.Value && node.DisplayName == "Microsoft.Data.SqlClient");
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DeclaresComponent.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DeclaresUiRoute.Value && edge.Metadata.ToCanonicalJson().Contains("\"routeTemplate\":\"main\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesUiResource.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesStyle.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesControl.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.BindsTo.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesCommand.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesViewModel.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.HandlesUiEvent.Value && edge.Metadata.ToCanonicalJson().Contains("\"eventName\":\"Clicked\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.NavigatesTo.Value && edge.Metadata.ToCanonicalJson().Contains("\"navigationTarget\":\"details\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.CallsApi.Value && edge.Metadata.ToCanonicalJson().Contains("\"packageIdentity\":\"Microsoft.Data.SqlClient\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Sample.Maui/Pages/MainPage.xaml" && evidence.SnippetHash is not null);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Sample.Maui/MauiProgram.cs");
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.SnippetPreview is not null && evidence.SnippetPreview.Contains("[REDACTED]", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Confirms dynamic MAUI route, navigation, resource, binding, platform-head, and convention-only view-model shapes produce warnings and explicit unknown graph facts.
        /// </summary>
        /// <returns>A task representing asynchronous fixture creation, extraction, and unknown-state assertions.</returns>
        [Fact]
        public async Task ExtractAsyncRecordsUnknownsForDynamicMauiTargetsAndAmbiguousPlatformHeads()
        {
            // Dynamic MAUI shapes are runtime-dependent and should remain queryable unknowns instead of guessed graph targets.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Dynamic.Maui");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Pages"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Dynamic.Maui.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <UseMaui>true</UseMaui>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls" Version="10.0.0" />
  </ItemGroup>
</Project>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MauiProgram.cs"), """
namespace Dynamic.Maui
{
    public static class MauiProgram
    {
        public static object CreateMauiApp()
        {
            return RuntimeBuilderFactory.CreateBuilder();
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "DynamicPage.xaml"), """
<ContentPage x:Class="Dynamic.Maui.Pages.DynamicPage"
             xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             Shell.Route="{Binding RuntimeRoute}">
    <VerticalStackLayout>
        <Label Text="{Binding}" TextColor="{DynamicResource RuntimeColor}" />
        <ContentView ControlTemplate="{Binding RuntimeTemplate}" />
    </VerticalStackLayout>
</ContentPage>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "DynamicPage.xaml.cs"), """
namespace Dynamic.Maui.Pages
{
    public partial class DynamicPage : ContentPage
    {
        public DynamicPage()
        {
            InitializeComponent();
            Shell.Current.GoToAsync(RuntimeRoute);
        }
    }
}
""");

                MauiXamlExtractor extractor = new();
                MauiXamlExtractionRequest request = new(new StableKey("snapshot://sample/maui-dynamic"), repositoryRoot);

                MauiXamlExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("dynamic resource", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("unresolved binding", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("runtime template", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("runtime navigation", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("Shell route", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("platform", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("convention-only view model", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "MAUI dynamic resource target is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "MAUI binding path could not be resolved statically.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "MAUI template selection is determined at runtime.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "MAUI navigation target is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "MAUI Shell route is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "MAUI platform heads could not be resolved statically.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "MAUI view model is inferred by convention only and was not found in source.");
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Creates an empty temporary repository root for a MAUI extraction fixture.
        /// </summary>
        /// <returns>The absolute path to the temporary repository root.</returns>
        private static string CreateTemporaryRepositoryRoot()
        {
            // A GUID segment prevents tests from sharing paths while keeping cleanup deterministic for every test invocation.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "ArchonMauiExtractorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repositoryRoot);
            return repositoryRoot;
        }
    }
}
