using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.WinUI;
using Xunit;

namespace Archon.Extractors.Tests.WinUI
{
    /// <summary>
    /// Verifies the WinUI XAML extraction slice from repository fixture files into graph-ready snapshot facts.
    /// </summary>
    public sealed class WinUiXamlExtractorTests
    {
        /// <summary>
        /// Confirms a WinUI project contributes application, startup, window, page, user control, resource, style, binding, command, navigation, packaging, view-model, service, data-access, evidence, and relationship facts.
        /// </summary>
        /// <returns>A task representing asynchronous fixture creation, extraction, and graph assertion flow.</returns>
        [Fact]
        public async Task ExtractAsyncEmitsWinUiApplicationWindowPageResourceBindingCommandPackagingAndDependencyFacts()
        {
            // The fixture uses a real temporary repository tree so project detection, XAML discovery, package metadata, path normalization, redaction, and build-output exclusion run together.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.WinUI");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Views"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Controls"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Assets"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "ViewModels"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "bin", "Debug"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.WinUI.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>MSIX</WindowsPackageType>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.7.0" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="6.1.1" />
    <Page Include="Views\MainWindow.xaml" />
    <ApplicationDefinition Include="App.xaml" />
  </ItemGroup>
</Project>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Package.appxmanifest"), """
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         IgnorableNamespaces="uap">
  <Identity Name="Contoso.SampleWinUI" Publisher="CN=Contoso" Version="1.2.3.4" />
  <Properties>
    <DisplayName>Sample WinUI</DisplayName>
    <PublisherDisplayName>Contoso</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Applications>
    <Application Id="App" Executable="$targetnametoken$.exe" EntryPoint="Sample.WinUI.App">
      <uap:VisualElements DisplayName="Sample WinUI" Square44x44Logo="Assets\Logo.png" Square150x150Logo="Assets\Logo.png" />
    </Application>
  </Applications>
</Package>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "App.xaml"), """
<Application x:Class="Sample.WinUI.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Styles.xaml" />
            </ResourceDictionary.MergedDictionaries>
            <SolidColorBrush x:Key="AccentBrush" Color="DodgerBlue" />
        </ResourceDictionary>
    </Application.Resources>
</Application>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "App.xaml.cs"), """
using Microsoft.UI.Xaml;

namespace Sample.WinUI
{
    public partial class App : Application
    {
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainWindow window = new();
            window.Activate();
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Styles.xaml"), """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Style x:Key="PrimaryButtonStyle" TargetType="Button" />
</ResourceDictionary>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "MainWindow.xaml"), """
<Window x:Class="Sample.WinUI.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Sample.WinUI.ViewModels"
        xmlns:local="using:Sample.WinUI.Controls">
    <Window.DataContext>
        <vm:MainViewModel />
    </Window.DataContext>
    <Grid>
        <Button x:Name="SaveButton" Content="Save" Command="{Binding SaveCommand}" Click="SaveButton_Click" Style="{StaticResource PrimaryButtonStyle}" />
        <TextBox x:Name="CustomerNameTextBox" Text="{Binding CustomerName, Mode=TwoWay}" />
        <Frame x:Name="RootFrame" />
        <local:CustomerSummary />
    </Grid>
</Window>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "MainWindow.xaml.cs"), """
using Microsoft.Data.SqlClient;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Sample.WinUI
{
    public sealed partial class MainWindow : Window
    {
        private readonly OrderService _service = new OrderService();

        public MainWindow()
        {
            InitializeComponent();
            RootFrame.Navigate(typeof(DetailsPage));
            using SqlConnection connection = new("Server=(local);Database=Orders;Password=very-secret;");
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _service.Save();
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "DetailsPage.xaml"), """
<Page x:Class="Sample.WinUI.DetailsPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel>
        <TextBlock Text="{Binding DetailsText}" />
    </StackPanel>
</Page>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Controls", "CustomerSummary.xaml"), """
<UserControl x:Class="Sample.WinUI.Controls.CustomerSummary"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel>
        <TextBlock x:Name="NameBlock" Text="{Binding CustomerName}" />
    </StackPanel>
</UserControl>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "ViewModels", "MainViewModel.cs"), """
namespace Sample.WinUI.ViewModels
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
namespace Sample.WinUI
{
    public sealed class OrderService
    {
        public void Save() { }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "bin", "Debug", "Ignored.xaml"), """
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" x:Class="Sample.WinUI.Ignored" />
""");

                WinUiXamlExtractor extractor = new();
                WinUiXamlExtractionRequest request = new(new StableKey("snapshot://sample/winui"), repositoryRoot);

                WinUiXamlExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Empty(result.Snapshot.Errors);
                Assert.DoesNotContain(result.Snapshot.Nodes, node => node.DisplayName == "Ignored");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiApplication.Value && node.Metadata.ToCanonicalJson().Contains("\"uiFramework\":\"WinUI\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiApplication.Value && node.Metadata.ToCanonicalJson().Contains("\"packageIdentity\":\"Contoso.SampleWinUI\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiView.Value && node.DisplayName == "MainWindow" && node.Metadata.ToCanonicalJson().Contains("\"windowName\":\"MainWindow\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiPage.Value && node.DisplayName == "DetailsPage");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiComponent.Value && node.DisplayName == "CustomerSummary");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiControl.Value && node.DisplayName == "SaveButton");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiResource.Value && node.DisplayName == "AccentBrush");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiStyle.Value && node.DisplayName == "PrimaryButtonStyle");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Binding.Value && node.Metadata.ToCanonicalJson().Contains("\"bindingPath\":\"CustomerName\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Command.Value && node.DisplayName == "SaveCommand");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.ViewModel.Value && node.DisplayName == "MainViewModel");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Type.Value && node.DisplayName == "OrderService");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.ExternalService.Value && node.DisplayName == "Microsoft.Data.SqlClient");
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DeclaresComponent.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesUiResource.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesStyle.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesControl.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.BindsTo.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesCommand.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesViewModel.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.HandlesUiEvent.Value && edge.Metadata.ToCanonicalJson().Contains("\"eventName\":\"Click\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.NavigatesTo.Value && edge.Metadata.ToCanonicalJson().Contains("\"navigationTarget\":\"DetailsPage\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.CallsApi.Value && edge.Metadata.ToCanonicalJson().Contains("\"packageIdentity\":\"Microsoft.Data.SqlClient\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Sample.WinUI/Views/MainWindow.xaml" && evidence.SnippetHash is not null);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Sample.WinUI/Package.appxmanifest");
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.SnippetPreview is not null && evidence.SnippetPreview.Contains("[REDACTED]", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Confirms dynamic WinUI resource, binding, navigation, packaging, and convention-only view-model shapes produce warnings and explicit unknown graph facts.
        /// </summary>
        /// <returns>A task representing asynchronous fixture creation, extraction, and unknown-state assertions.</returns>
        [Fact]
        public async Task ExtractAsyncRecordsUnknownsForDynamicWinUiTargetsAndAmbiguousPackaging()
        {
            // Dynamic WinUI shapes are runtime-dependent and should remain queryable unknowns instead of guessed graph targets.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Dynamic.WinUI");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Views"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Dynamic.WinUI.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.7.0" />
  </ItemGroup>
</Project>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "App.xaml"), """
<Application x:Class="Dynamic.WinUI.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "DynamicWindow.xaml"), """
<Window x:Class="Dynamic.WinUI.DynamicWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <ContentControl ContentTemplateSelector="{Binding RuntimeTemplateSelector}" />
        <TextBlock Text="{Binding}" Foreground="{ThemeResource RuntimeBrush}" />
        <Frame x:Name="RootFrame" />
    </Grid>
</Window>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "DynamicWindow.xaml.cs"), """
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Dynamic.WinUI
{
    public sealed partial class DynamicWindow : Window
    {
        public DynamicWindow()
        {
            InitializeComponent();
            RootFrame.Navigate(Type.GetType(RuntimePageName));
        }
    }
}
""");

                WinUiXamlExtractor extractor = new();
                WinUiXamlExtractionRequest request = new(new StableKey("snapshot://sample/winui-dynamic"), repositoryRoot);

                WinUiXamlExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("dynamic resource", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("unresolved binding", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("runtime template", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("runtime navigation", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("packaging", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("convention-only view model", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "WinUI dynamic resource target is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "WinUI binding path could not be resolved statically.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "WinUI template selection is determined at runtime.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "WinUI navigation target is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "WinUI packaging metadata is ambiguous or unavailable.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "WinUI view model is inferred by convention only and was not found in source.");
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Creates an empty temporary repository root for a WinUI extraction fixture.
        /// </summary>
        /// <returns>The absolute path to the temporary repository root.</returns>
        private static string CreateTemporaryRepositoryRoot()
        {
            // A GUID segment prevents tests from sharing paths while keeping cleanup deterministic for every test invocation.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "ArchonWinUiExtractorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repositoryRoot);
            return repositoryRoot;
        }
    }
}
