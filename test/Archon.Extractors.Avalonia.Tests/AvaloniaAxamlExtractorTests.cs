using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Xunit;

namespace Archon.Extractors.Avalonia.Tests
{
    /// <summary>
    /// Verifies the WP011 Avalonia AXAML extraction slice from repository fixture files into graph-ready snapshot facts.
    /// </summary>
    public sealed class AvaloniaAxamlExtractorTests
    {
        /// <summary>
        /// Confirms an Avalonia project contributes application, window, user-control, resource, style, binding, command, view-locator, ReactiveUI, navigation, view-model, service, data-access, evidence, and relationship facts.
        /// </summary>
        /// <returns>A task representing asynchronous fixture creation, extraction, and graph assertion flow.</returns>
        [Fact]
        public async Task ExtractAsyncEmitsAvaloniaApplicationWindowUserControlResourceBindingViewLocatorReactiveUiAndDependencyFacts()
        {
            // The fixture uses a real temporary repository tree so package detection, AXAML discovery, source correlation, redaction, and build-output exclusion run together without starting Avalonia.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Avalonia");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Views"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "ViewModels"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Styles"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "bin", "Debug"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Avalonia.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>WinExe</OutputType>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.0" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.0" />
    <PackageReference Include="Avalonia.ReactiveUI" Version="11.3.0" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="6.1.1" />
    <AvaloniaResource Include="Assets\\**" />
  </ItemGroup>
</Project>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Program.cs"), """
using Avalonia;
using Avalonia.ReactiveUI;

namespace Sample.Avalonia
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>().UsePlatformDetect().UseReactiveUI();
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "App.axaml"), """
<Application x:Class="Sample.Avalonia.App"
             xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Styles>
        <FluentTheme />
        <StyleInclude Source="avares://Sample.Avalonia/Styles/Theme.axaml" />
        <Style Selector="Button.primary">
            <Setter Property="Background" Value="Blue" />
        </Style>
    </Application.Styles>
    <Application.Resources>
        <SolidColorBrush x:Key="AccentBrush" Color="#006CBE" />
    </Application.Resources>
</Application>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "App.axaml.cs"), """
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Sample.Avalonia.Views;

namespace Sample.Avalonia
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "ViewLocator.cs"), """
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Sample.Avalonia.ViewModels;
using Sample.Avalonia.Views;

namespace Sample.Avalonia
{
    public sealed class ViewLocator : IDataTemplate
    {
        public Control Build(object? data)
        {
            return data switch
            {
                MainWindowViewModel => new MainWindow(),
                CustomerCardViewModel => new CustomerCard(),
                _ => new TextBlock()
            };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "MainWindow.axaml"), """
<Window x:Class="Sample.Avalonia.Views.MainWindow"
        xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Sample.Avalonia.ViewModels"
        xmlns:views="using:Sample.Avalonia.Views"
        x:DataType="vm:MainWindowViewModel">
    <Design.DataContext>
        <vm:MainWindowViewModel />
    </Design.DataContext>
    <StackPanel>
        <TextBlock x:Name="CustomerNameText" Text="{Binding CustomerName}" Foreground="{StaticResource AccentBrush}" />
        <Button x:Name="SaveButton" Classes="primary" Command="{Binding SaveCommand}" Click="SaveButton_Click" />
        <views:CustomerCard />
        <ContentControl Content="{Binding ActiveViewModel}" />
    </StackPanel>
</Window>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "MainWindow.axaml.cs"), """
using Microsoft.Data.SqlClient;

namespace Sample.Avalonia.Views
{
    public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
    {
        private readonly OrderService _service = new OrderService();

        public MainWindow()
        {
            InitializeComponent();
            using SqlConnection connection = new("Server=(local);Database=Orders;Password=very-secret;");
            Router.Navigate.Execute(new CustomerCardViewModel());
        }

        private void SaveButton_Click(object? sender, EventArgs e)
        {
            _service.Save();
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "CustomerCard.axaml"), """
<UserControl x:Class="Sample.Avalonia.Views.CustomerCard"
             xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Sample.Avalonia.ViewModels"
             x:DataType="vm:CustomerCardViewModel">
    <StackPanel>
        <TextBlock x:Name="NameBlock" Text="{Binding CustomerName}" />
    </StackPanel>
</UserControl>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Styles", "Theme.axaml"), """
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Style Selector="TextBlock.heading">
        <Setter Property="FontWeight" Value="Bold" />
    </Style>
</Styles>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "ViewModels", "MainWindowViewModel.cs"), """
namespace Sample.Avalonia.ViewModels
{
    public sealed class MainWindowViewModel : ViewModelBase
    {
        public string CustomerName { get; set; } = string.Empty;
        public object SaveCommand { get; } = new object();
        public object ActiveViewModel { get; } = new CustomerCardViewModel();
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "ViewModels", "CustomerCardViewModel.cs"), """
namespace Sample.Avalonia.ViewModels
{
    public sealed class CustomerCardViewModel : ViewModelBase
    {
        public string CustomerName { get; set; } = string.Empty;
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "ViewModels", "ViewModelBase.cs"), """
namespace Sample.Avalonia.ViewModels
{
    public abstract class ViewModelBase
    {
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "OrderService.cs"), """
namespace Sample.Avalonia
{
    public sealed class OrderService
    {
        public void Save() { }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "bin", "Debug", "Ignored.axaml"), """
<Window xmlns="https://github.com/avaloniaui" x:Class="Sample.Avalonia.Ignored" />
""");

                AvaloniaAxamlExtractor extractor = new();
                AvaloniaAxamlExtractionRequest request = new(new StableKey("snapshot://sample/avalonia"), repositoryRoot);

                AvaloniaAxamlExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Empty(result.Snapshot.Errors);
                Assert.DoesNotContain(result.Snapshot.Nodes, node => node.DisplayName == "Ignored");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiApplication.Value && node.Metadata.ToCanonicalJson().Contains("\"uiFramework\":\"Avalonia\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiView.Value && node.DisplayName == "MainWindow");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiComponent.Value && node.DisplayName == "CustomerCard");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiControl.Value && node.DisplayName == "SaveButton");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiResource.Value && node.DisplayName == "AccentBrush");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiStyle.Value && node.DisplayName == "Button.primary");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Binding.Value && node.Metadata.ToCanonicalJson().Contains("\"bindingPath\":\"CustomerName\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Command.Value && node.DisplayName == "SaveCommand");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Command.Value && node.DisplayName == "SaveButton_Click");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.ViewModel.Value && node.DisplayName == "MainWindowViewModel");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.ViewModel.Value && node.DisplayName == "CustomerCardViewModel");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Type.Value && node.DisplayName == "OrderService");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.ExternalService.Value && node.DisplayName == "Microsoft.Data.SqlClient");
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DeclaresComponent.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesUiResource.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesStyle.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesControl.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesComponent.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.BindsTo.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesCommand.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.HandlesUiEvent.Value && edge.Metadata.ToCanonicalJson().Contains("\"eventName\":\"Click\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesViewModel.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.NavigatesTo.Value && edge.Metadata.ToCanonicalJson().Contains("\"navigationTarget\":\"CustomerCardViewModel\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.CallsApi.Value && edge.Metadata.ToCanonicalJson().Contains("\"packageIdentity\":\"Microsoft.Data.SqlClient\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Sample.Avalonia/Views/MainWindow.axaml" && evidence.SnippetHash is not null);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Sample.Avalonia/Program.cs");
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.SnippetPreview is not null && evidence.SnippetPreview.Contains("[REDACTED]", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Confirms dynamic Avalonia styles, bindings, navigation, view-locator, and ReactiveUI shapes produce warnings and explicit unknown graph facts.
        /// </summary>
        /// <returns>A task representing asynchronous fixture creation, extraction, and unknown-state assertions.</returns>
        [Fact]
        public async Task ExtractAsyncRecordsUnknownsForDynamicAvaloniaTargetsAndAmbiguousReactiveUiRelationships()
        {
            // Dynamic Avalonia shapes are runtime-dependent and should remain queryable unknowns instead of guessed graph targets.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Dynamic.Avalonia");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Views"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Dynamic.Avalonia.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.0" />
    <PackageReference Include="Avalonia.ReactiveUI" Version="11.3.0" />
  </ItemGroup>
</Project>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "DynamicWindow.axaml"), """
<Window x:Class="Dynamic.Avalonia.Views.DynamicWindow"
        xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel>
        <TextBlock Text="{Binding}" Foreground="{DynamicResource RuntimeBrush}" />
        <ContentControl Content="{Binding RuntimeViewModel}" />
        <Button Command="{Binding}" />
    </StackPanel>
</Window>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "DynamicWindow.axaml.cs"), """
namespace Dynamic.Avalonia.Views
{
    public partial class DynamicWindow : ReactiveWindow
    {
        public DynamicWindow()
        {
            InitializeComponent();
            Router.Navigate.Execute(runtimeViewModel);
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "ViewLocator.cs"), """
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Dynamic.Avalonia
{
    public sealed class ViewLocator : IDataTemplate
    {
        public Control Build(object? data)
        {
            Type viewType = Type.GetType(data.GetType().FullName!.Replace("ViewModel", "View"))!;
            return (Control)Activator.CreateInstance(viewType)!;
        }

        public bool Match(object? data)
        {
            return data is object;
        }
    }
}
""");

                AvaloniaAxamlExtractor extractor = new();
                AvaloniaAxamlExtractionRequest request = new(new StableKey("snapshot://sample/avalonia-dynamic"), repositoryRoot);

                AvaloniaAxamlExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("dynamic resource", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("unresolved binding", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("runtime navigation", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("view locator", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("ReactiveUI", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "Avalonia dynamic resource target is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "Avalonia binding path could not be resolved statically.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "Avalonia navigation target is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "Avalonia view locator uses convention or reflection that could not be resolved statically.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "Avalonia ReactiveUI relationship is ambiguous without generic view-model evidence.");
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Creates an empty temporary repository root for an Avalonia extraction fixture.
        /// </summary>
        /// <returns>The absolute path to the temporary repository root.</returns>
        private static string CreateTemporaryRepositoryRoot()
        {
            // A GUID segment prevents tests from sharing paths while keeping cleanup deterministic for every test invocation.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "ArchonAvaloniaExtractorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repositoryRoot);
            return repositoryRoot;
        }
    }
}
