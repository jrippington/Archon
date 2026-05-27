using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Wpf;
using Xunit;

namespace Archon.Extractors.Wpf.Tests
{
    /// <summary>
    /// Verifies the WP011 WPF XAML extraction slice from repository fixture files into graph-ready snapshot facts.
    /// </summary>
    public sealed class WpfXamlExtractorTests
    {
        /// <summary>
        /// Confirms a WPF project contributes application, startup, window, page, user control, resource, style, template, binding, command, event, navigation, view-model, service, data-access, evidence, and relationship facts.
        /// </summary>
        /// <returns>A task representing asynchronous fixture creation, extraction, and graph assertion flow.</returns>
        [Fact]
        public async Task ExtractAsyncEmitsWpfApplicationWindowPageControlResourceBindingCommandAndDependencyFacts()
        {
            // The fixture uses a real temporary repository tree so project detection, XAML discovery, code-behind correlation, path normalization, redaction, and build-output exclusion run together.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Wpf");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Views"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Controls"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Resources"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "ViewModels"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "bin", "Debug"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Wpf.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="PresentationFramework" />
    <Page Include="Views\MainWindow.xaml" />
    <ApplicationDefinition Include="App.xaml" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="6.1.1" />
  </ItemGroup>
</Project>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "App.xaml"), """
<Application x:Class="Sample.Wpf.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="Views/MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary Source="Resources/Theme.xaml" />
    </Application.Resources>
</Application>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "MainWindow.xaml"), """
<Window x:Class="Sample.Wpf.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:Sample.Wpf.ViewModels"
        xmlns:local="clr-namespace:Sample.Wpf.Controls"
        Title="Orders" Height="450" Width="800">
    <Window.DataContext>
        <vm:MainViewModel />
    </Window.DataContext>
    <Window.Resources>
        <Style x:Key="PrimaryButtonStyle" TargetType="Button">
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border><ContentPresenter /></Border>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
    <Grid>
        <Button x:Name="SaveButton" Content="Save" Command="{Binding SaveCommand}" Click="SaveButton_Click" Style="{StaticResource PrimaryButtonStyle}" />
        <TextBox x:Name="CustomerNameTextBox" Text="{Binding CustomerName, Mode=TwoWay}" />
        <Frame Source="DetailsPage.xaml" />
        <local:CustomerSummary />
    </Grid>
</Window>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "MainWindow.xaml.cs"), """
using Microsoft.Data.SqlClient;
using System.Windows;

namespace Sample.Wpf.Views
{
    public partial class MainWindow : Window
    {
        private readonly OrderService _service = new OrderService();

        public MainWindow()
        {
            InitializeComponent();
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
<Page x:Class="Sample.Wpf.Views.DetailsPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel>
        <TextBlock Text="{Binding DetailsText}" />
    </StackPanel>
</Page>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Controls", "CustomerSummary.xaml"), """
<UserControl x:Class="Sample.Wpf.Controls.CustomerSummary"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel>
        <TextBlock x:Name="NameBlock" Text="{Binding CustomerName}" />
    </StackPanel>
</UserControl>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Resources", "Theme.xaml"), """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="AccentBrush" Color="DodgerBlue" />
    <Style x:Key="TextBlockStyle" TargetType="TextBlock" />
</ResourceDictionary>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "ViewModels", "MainViewModel.cs"), """
namespace Sample.Wpf.ViewModels
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
namespace Sample.Wpf
{
    public sealed class OrderService
    {
        public void Save() { }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "bin", "Debug", "Ignored.xaml"), """
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" x:Class="Sample.Wpf.Ignored" />
""");

                WpfXamlExtractor extractor = new();
                WpfXamlExtractionRequest request = new(new StableKey("snapshot://sample/wpf"), repositoryRoot);

                WpfXamlExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Empty(result.Snapshot.Errors);
                Assert.DoesNotContain(result.Snapshot.Nodes, node => node.DisplayName == "Ignored");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiApplication.Value && node.Metadata.ToCanonicalJson().Contains("\"uiFramework\":\"Wpf\"", StringComparison.Ordinal));
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
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.NavigatesTo.Value && edge.Metadata.ToCanonicalJson().Contains("\"navigationTarget\":\"DetailsPage.xaml\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.CallsApi.Value && edge.Metadata.ToCanonicalJson().Contains("\"packageIdentity\":\"Microsoft.Data.SqlClient\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Sample.Wpf/Views/MainWindow.xaml" && evidence.SnippetHash is not null);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.SnippetPreview is not null && evidence.SnippetPreview.Contains("[REDACTED]", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Confirms dynamic WPF resource, binding, template, navigation, and convention-only view-model shapes produce warnings and explicit unknown graph facts.
        /// </summary>
        /// <returns>A task representing asynchronous fixture creation, extraction, and unknown-state assertions.</returns>
        [Fact]
        public async Task ExtractAsyncRecordsUnknownsForDynamicWpfTargets()
        {
            // Dynamic WPF shapes are runtime-dependent and should remain queryable unknowns instead of guessed graph targets.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Dynamic.Wpf");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Views"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Dynamic.Wpf.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "App.xaml"), """
<Application x:Class="Dynamic.Wpf.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "DynamicWindow.xaml"), """
<Window x:Class="Dynamic.Wpf.Views.DynamicWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <ContentControl ContentTemplateSelector="{Binding RuntimeTemplateSelector}" />
        <TextBlock Text="{Binding}" Foreground="{DynamicResource RuntimeBrush}" />
        <Frame Source="{Binding NextPage}" />
    </Grid>
</Window>
""");

                WpfXamlExtractor extractor = new();
                WpfXamlExtractionRequest request = new(new StableKey("snapshot://sample/wpf-dynamic"), repositoryRoot);

                WpfXamlExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("dynamic resource", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("unresolved binding", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("runtime template", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("computed navigation", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("convention-only view model", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "WPF dynamic resource target is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "WPF binding path could not be resolved statically.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "WPF template selection is determined at runtime.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "WPF navigation target is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "WPF view model is inferred by convention only and was not found in source.");
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Confirms Avalonia AXAML projects are not treated as WPF projects even when their package metadata and markup contain XAML-related text.
        /// </summary>
        /// <returns>A task representing asynchronous fixture creation, extraction, and graph assertion flow.</returns>
        [Fact]
        public async Task ExtractAsync_WhenAvaloniaProjectContainsDynamicResource_ShouldNotEmitWpfWarnings()
        {
            // Avalonia projects may use AXAML and DynamicResource markup, but those facts belong to the Avalonia extractor rather than the WPF extractor.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Avalonia");
                Directory.CreateDirectory(projectDirectory);
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Avalonia.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.0" />
    <AvaloniaResource Include="**/*.axaml" />
  </ItemGroup>
</Project>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MainWindow.axaml"), """
<Window x:Class="Sample.Avalonia.MainWindow"
        xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <TextBlock Foreground="{DynamicResource RuntimeBrush}" />
</Window>
""");

                WpfXamlExtractor extractor = new();
                WpfXamlExtractionRequest request = new(new StableKey("snapshot://sample/avalonia"), repositoryRoot);

                WpfXamlExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Empty(result.Snapshot.Nodes);
                Assert.Empty(result.Snapshot.Edges);
                Assert.Empty(result.Snapshot.Evidence);
                Assert.DoesNotContain(result.Snapshot.Warnings, warning => warning.Contains("WPF", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(result.Snapshot.Warnings, warning => warning.Contains("DynamicResource", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Creates an empty temporary repository root for a WPF extraction fixture.
        /// </summary>
        /// <returns>The absolute path to the temporary repository root.</returns>
        private static string CreateTemporaryRepositoryRoot()
        {
            // A GUID segment prevents tests from sharing paths while keeping cleanup deterministic for every test invocation.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "ArchonWpfExtractorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repositoryRoot);
            return repositoryRoot;
        }
    }
}
