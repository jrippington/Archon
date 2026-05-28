using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.WinForms;
using Xunit;

namespace Archon.Extractors.Tests.WinForms
{
    /// <summary>
    /// Verifies the Windows Forms static UI extraction slice from repository fixture files into graph-ready snapshot facts.
    /// </summary>
    public sealed class WinFormsStaticUiExtractorTests
    {
        /// <summary>
        /// Confirms a C# Windows Forms project contributes application, startup form, form, user control, designer control, resource, event, binding, service, data-access, evidence, and relationship facts.
        /// </summary>
        /// <returns>A task representing asynchronous fixture creation, extraction, and graph assertion flow.</returns>
        [Fact]
        public async Task ExtractAsyncEmitsCSharpWinFormsApplicationDesignerControlResourceEventBindingAndDependencyFacts()
        {
            // The fixture uses a real temporary repository tree so discovery, project classification, designer correlation, path normalization, and build-output exclusion run together.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.WinForms");
                Directory.CreateDirectory(projectDirectory);
                Directory.CreateDirectory(Path.Combine(projectDirectory, "bin", "Debug"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.WinForms.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Data.SqlClient" Version="4.8.6" />
  </ItemGroup>
</Project>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Program.cs"), """
using System;
using System.Windows.Forms;

namespace Sample.WinForms
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.Run(new MainForm());
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MainForm.cs"), """
using System.Data.SqlClient;
using System.Windows.Forms;

namespace Sample.WinForms
{
    public partial class MainForm : Form
    {
        private readonly OrderService _service = new OrderService();

        public MainForm()
        {
            InitializeComponent();
            this.saveButton.Click += SaveButton_Click;
            this.dynamicButton.Click += (_, _) => _service.Save();
            using SqlConnection connection = new("Server=(local);Database=Orders;Password=very-secret;");
        }

        private void SaveButton_Click(object? sender, System.EventArgs e)
        {
            _service.Save();
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MainForm.Designer.cs"), """
namespace Sample.WinForms
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Panel mainPanel;
        private System.Windows.Forms.Button saveButton;
        private System.Windows.Forms.Button dynamicButton;
        private System.Windows.Forms.TextBox nameTextBox;

        private void InitializeComponent()
        {
            this.mainPanel = new System.Windows.Forms.Panel();
            this.saveButton = new System.Windows.Forms.Button();
            this.dynamicButton = CreateButton();
            this.nameTextBox = new System.Windows.Forms.TextBox();
            this.saveButton.Text = "Save";
            this.saveButton.Click += new System.EventHandler(this.SaveButton_Click);
            this.nameTextBox.DataBindings.Add("Text", this.orderBindingSource, "CustomerName", true);
            this.mainPanel.Controls.Add(this.saveButton);
            this.Controls.Add(this.mainPanel);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MainForm.resx"), """
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="$this.Icon" type="System.Resources.ResXFileRef, System.Windows.Forms">
    <value>app.ico;System.Drawing.Icon</value>
  </data>
  <data name="ApiToken" xml:space="preserve">
    <value>secret-token-value</value>
  </data>
</root>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "CustomerControl.cs"), """
using System.Windows.Forms;

namespace Sample.WinForms
{
    public partial class CustomerControl : UserControl
    {
        public CustomerControl()
        {
            InitializeComponent();
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "CustomerControl.Designer.cs"), """
namespace Sample.WinForms
{
    partial class CustomerControl
    {
        private System.Windows.Forms.Label titleLabel;

        private void InitializeComponent()
        {
            this.titleLabel = new System.Windows.Forms.Label();
            this.Controls.Add(this.titleLabel);
        }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "OrderService.cs"), """
namespace Sample.WinForms
{
    public sealed class OrderService
    {
        public void Save() { }
    }
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "bin", "Debug", "Ignored.Designer.cs"), """
namespace Sample.WinForms
{
    partial class IgnoredForm
    {
        private void InitializeComponent() { }
    }
}
""");

                WinFormsStaticUiExtractor extractor = new();
                WinFormsStaticUiExtractionRequest request = new(new StableKey("snapshot://sample/winforms-csharp"), repositoryRoot);

                WinFormsStaticUiExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Empty(result.Snapshot.Errors);
                Assert.DoesNotContain(result.Snapshot.Nodes, node => node.DisplayName == "IgnoredForm");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiApplication.Value && node.Metadata.ToCanonicalJson().Contains("\"uiFramework\":\"WinForms\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiView.Value && node.DisplayName == "MainForm" && node.Metadata.ToCanonicalJson().Contains("\"startupForm\":\"MainForm\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiComponent.Value && node.DisplayName == "CustomerControl");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiControl.Value && node.DisplayName == "saveButton" && node.Metadata.ToCanonicalJson().Contains("\"controlName\":\"saveButton\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiResource.Value && node.DisplayName == "$this.Icon");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Binding.Value && node.Metadata.ToCanonicalJson().Contains("\"bindingPath\":\"CustomerName\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Command.Value && node.DisplayName == "SaveButton_Click");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Type.Value && node.DisplayName == "OrderService");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.ExternalService.Value && node.DisplayName == "System.Data.SqlClient");
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DeclaresComponent.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesControl.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesUiResource.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.BindsTo.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.HandlesUiEvent.Value && edge.Metadata.ToCanonicalJson().Contains("\"eventName\":\"Click\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesCommand.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DependsOn.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.CallsApi.Value && edge.Metadata.ToCanonicalJson().Contains("\"packageIdentity\":\"System.Data.SqlClient\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("dynamic control", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "Windows Forms control is created dynamically and cannot be fully resolved statically.");
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Sample.WinForms/MainForm.Designer.cs" && evidence.SnippetHash is not null);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.SnippetPreview is not null && evidence.SnippetPreview.Contains("[REDACTED]", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Confirms a VB.NET Windows Forms project contributes application, startup form, form, control, event, evidence, and framework metadata facts.
        /// </summary>
        /// <returns>A task representing asynchronous fixture creation, extraction, and graph assertion flow.</returns>
        [Fact]
        public async Task ExtractAsyncEmitsVisualBasicWinFormsApplicationFormControlAndEventFacts()
        {
            // The VB fixture covers the second source language expected by the work item without requiring compilation or Windows desktop workloads.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.VbWinForms");
                Directory.CreateDirectory(projectDirectory);
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.VbWinForms.vbproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <StartupObject>Sample.VbWinForms.MainForm</StartupObject>
  </PropertyGroup>
</Project>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MainForm.vb"), """
Imports System.Windows.Forms

Public Class MainForm
    Inherits Form

    Private Sub SaveButton_Click(sender As Object, e As EventArgs) Handles saveButton.Click
    End Sub
End Class
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "MainForm.Designer.vb"), """
Partial Class MainForm
    Private saveButton As System.Windows.Forms.Button

    Private Sub InitializeComponent()
        Me.saveButton = New System.Windows.Forms.Button()
        Me.Controls.Add(Me.saveButton)
    End Sub
End Class
""");

                WinFormsStaticUiExtractor extractor = new();
                WinFormsStaticUiExtractionRequest request = new(new StableKey("snapshot://sample/winforms-vb"), repositoryRoot);

                WinFormsStaticUiExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Empty(result.Snapshot.Errors);
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiApplication.Value && node.Metadata.ToCanonicalJson().Contains("\"language\":\"Visual Basic\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiView.Value && node.DisplayName == "MainForm");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiControl.Value && node.DisplayName == "saveButton");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Command.Value && node.DisplayName == "SaveButton_Click");
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.HandlesUiEvent.Value && edge.Metadata.ToCanonicalJson().Contains("\"eventName\":\"Click\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Sample.VbWinForms/MainForm.Designer.vb" && evidence.SnippetHash is not null);
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Creates an empty temporary repository root for a Windows Forms extraction fixture.
        /// </summary>
        /// <returns>The absolute path to the temporary repository root.</returns>
        private static string CreateTemporaryRepositoryRoot()
        {
            // A GUID segment prevents tests from sharing paths while keeping cleanup deterministic for every test invocation.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "ArchonWinFormsExtractorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repositoryRoot);
            return repositoryRoot;
        }
    }
}
