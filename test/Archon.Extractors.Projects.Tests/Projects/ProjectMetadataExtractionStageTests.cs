using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Domain.Graph.ControlledValues;
using Archon.Extractors.Projects.Solutions;
using Xunit;

namespace Archon.Extractors.Projects.Tests.Projects
{
    /// <summary>
    /// Verifies the WP005 project metadata slice extracts C# and VB.NET project nodes through the shared repository/solution stage.
    /// </summary>
    public sealed class ProjectMetadataExtractionStageTests : IDisposable
    {
        /// <summary>
        /// Tracks temporary repository roots created for project metadata fixtures.
        /// </summary>
        private readonly List<string> _temporaryDirectories = [];

        /// <summary>
        /// Deletes temporary repositories and fixture files created by each test.
        /// </summary>
        public void Dispose()
        {
            // The extractor reads real solution and project files, so test cleanup removes every temporary repository recursively.
            foreach (string temporaryDirectory in _temporaryDirectories)
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies SDK-style C# projects are extracted with core build metadata and project-file evidence.
        /// </summary>
        /// <returns>A task that completes after the snapshot contribution assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenSolutionDeclaresCSharpProject_ShouldExtractProjectNodeMetadataAndEvidence()
        {
            // This scenario covers the primary SDK-style C# project inventory path for Work Item 2.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "CustomerSuite.sln", [ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj")]);
            CreateProjectFile(
                repositoryRoot,
                "src/Customer.Api/Customer.Api.csproj",
                """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                    <AssemblyName>Customer.Api.Host</AssemblyName>
                    <RootNamespace>Customer.Api.Root</RootNamespace>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            var projectNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
            Assert.Equal("project://src/Customer.Api/Customer.Api.csproj", projectNode.StableKey.Value);
            Assert.Equal("Customer.Api", projectNode.DisplayName);
            Assert.Equal("C#", projectNode.Language);
            Assert.Contains("\"project.targetFramework\":\"net10.0\"", projectNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("\"project.outputType\":\"Exe\"", projectNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("\"project.assemblyName\":\"Customer.Api.Host\"", projectNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("\"project.rootNamespace\":\"Customer.Api.Root\"", projectNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("\"project.isSdkStyle\":true", projectNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("\"project.isOldStyle\":false", projectNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("\"project.nullable\":\"enable\"", projectNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("\"project.implicitUsings\":\"enable\"", projectNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Contains && edge.SourceNodeStableKey.Value == "solution://CustomerSuite.sln" && edge.TargetNodeStableKey.Value == projectNode.StableKey.Value);
            Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Customer.Api/Customer.Api.csproj" && evidence.SymbolName == "Customer.Api");
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies mixed C# and VB.NET solutions are supported and each language is represented on project nodes.
        /// </summary>
        /// <returns>A task that completes after mixed-language project extraction is asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenSolutionDeclaresCSharpAndVisualBasicProjects_ShouldExtractMixedLanguageProjects()
        {
            // Mixed-language repositories are common in legacy estates and must not require separate extraction runs.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(
                repositoryRoot,
                "MixedSuite.sln",
                [
                    ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj"),
                    ProjectDeclaration.VisualBasic("Customer.Legacy", "src/Customer.Legacy/Customer.Legacy.vbproj")
                ]);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            CreateProjectFile(repositoryRoot, "src/Customer.Legacy/Customer.Legacy.vbproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net48</TargetFramework>
                    <RootNamespace>Customer.Legacy</RootNamespace>
                    <AssemblyName>Customer.Legacy.Assembly</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            Assert.Equal(2, snapshot.Nodes.Count(node => node.NodeKind == NodeKind.Project));
            Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Project && node.Language == "C#" && node.StableKey.Value == "project://src/Customer.Api/Customer.Api.csproj");
            Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Project && node.Language == "VB.NET" && node.StableKey.Value == "project://src/Customer.Legacy/Customer.Legacy.vbproj");
            Assert.Equal(2, snapshot.Edges.Count(edge => edge.EdgeKind == EdgeKind.Contains && edge.SourceNodeStableKey.Value == "solution://MixedSuite.sln" && edge.TargetNodeStableKey.Value.StartsWith("project://", StringComparison.Ordinal)));
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies old-style project files are identified and legacy target framework declarations are preserved as metadata.
        /// </summary>
        /// <returns>A task that completes after old-style metadata assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenOldStyleProjectIsDeclared_ShouldExtractOldStyleMetadata()
        {
            // Old-style project support starts with deterministic XML property extraction and avoids MSBuild target execution.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "LegacySuite.sln", [ProjectDeclaration.CSharp("Legacy.Library", "Legacy.Library/Legacy.Library.csproj")]);
            CreateProjectFile(
                repositoryRoot,
                "Legacy.Library/Legacy.Library.csproj",
                """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
                    <OutputType>Library</OutputType>
                    <AssemblyName>Legacy.Library.Custom</AssemblyName>
                    <RootNamespace>Legacy.Library.Root</RootNamespace>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            var projectNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
            string metadata = projectNode.Metadata.ToCanonicalJson();
            Assert.Contains("\"project.isSdkStyle\":false", metadata, StringComparison.Ordinal);
            Assert.Contains("\"project.isOldStyle\":true", metadata, StringComparison.Ordinal);
            Assert.Contains("\"project.legacyTargetFramework\":\"v4.7.2\"", metadata, StringComparison.Ordinal);
            Assert.Contains("\"project.outputType\":\"Library\"", metadata, StringComparison.Ordinal);
            Assert.Contains("\"project.assemblyName\":\"Legacy.Library.Custom\"", metadata, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies unsupported solution project declarations become warnings when at least one supported project can still be extracted.
        /// </summary>
        /// <returns>A task that completes after warning behavior has been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenSolutionContainsSupportedAndUnsupportedProjects_ShouldExtractSupportedAndWarnForUnsupported()
        {
            // Unsupported project evidence should degrade extraction without hiding supported C# or VB.NET project facts.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(
                repositoryRoot,
                "MixedSupport.sln",
                [
                    ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj"),
                    ProjectDeclaration.Unsupported("Setup.Project", "setup/Setup.wixproj")
                ]);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
            Assert.Contains(snapshot.Warnings, warning => warning.Contains("unsupported project", StringComparison.OrdinalIgnoreCase) && warning.Contains(".wixproj", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(snapshot.Evidence, evidence => evidence.SymbolName == "Setup.Project");
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies duplicate project declarations across submitted solutions create one project node while preserving each solution membership edge.
        /// </summary>
        /// <returns>A task that completes after deduplication assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenProjectAppearsInMultipleSolutions_ShouldDeduplicateProjectNodeAndPreserveMembershipEdges()
        {
            // Project identity is repository-relative, so the same project declared by two solutions must not become two project nodes.
            string repositoryRoot = CreateRepositoryRoot();
            string firstSolutionPath = CreateSolutionFile(repositoryRoot, "CustomerSuite.sln", [ProjectDeclaration.CSharp("Customer.Shared", "src/Customer.Shared/Customer.Shared.csproj")]);
            string secondSolutionPath = CreateSolutionFile(repositoryRoot, "ToolsSuite.sln", [ProjectDeclaration.CSharp("Customer.Shared", "src/Customer.Shared/Customer.Shared.csproj")]);
            CreateProjectFile(repositoryRoot, "src/Customer.Shared/Customer.Shared.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net10.0;net8.0</TargetFrameworks>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [firstSolutionPath, secondSolutionPath]);

            var projectNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
            Assert.Equal("project://src/Customer.Shared/Customer.Shared.csproj", projectNode.StableKey.Value);
            Assert.Contains("\"project.targetFrameworks\":[\"net10.0\",\"net8.0\"]", projectNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Equal(2, snapshot.Edges.Count(edge => edge.EdgeKind == EdgeKind.Contains && edge.TargetNodeStableKey.Value == projectNode.StableKey.Value));
        }

        /// <summary>
        /// Verifies a solution containing only unsupported project declarations fails with a controlled error.
        /// </summary>
        /// <returns>A task that completes after no-supported-project error behavior has been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenNoSupportedProjectsAreDeclared_ShouldReturnControlledBlockingError()
        {
            // Work Item 2 requires unsupported declarations to fail only when no supported project can be extracted.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "UnsupportedOnly.sln", [ProjectDeclaration.Unsupported("Setup.Project", "setup/Setup.wixproj")]);
            ResolvedExtractionInput input = CreateResolvedInput(repositoryRoot, [solutionPath]);
            ExtractionRun run = CreateRun(input);
            ArchitectureSnapshotAccumulator accumulation = new();
            RepositorySolutionExtractionStage stage = new();

            ExtractionStageResult result = await stage.ExecuteAsync(new ExtractionStageContext(input, run, accumulation), CancellationToken.None);
            ExtractedArchitectureSnapshot snapshot = accumulation.ToSnapshot();

            Assert.True(result.HasBlockingError);
            Assert.Contains("No supported C# or VB.NET projects", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
        }

        /// <summary>
        /// Verifies resolved project references create deterministic `REFERENCES` relationships and project-reference evidence.
        /// </summary>
        /// <returns>A task that completes after resolved reference assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenProjectReferenceTargetsSubmittedProject_ShouldCreateReferencesEdgeAndEvidence()
        {
            // This scenario verifies the primary Work Item 3 dependency path where both projects are declared by the submitted solution.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(
                repositoryRoot,
                "ReferenceSuite.sln",
                [
                    ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj"),
                    ProjectDeclaration.CSharp("Customer.Core", "src/Customer.Core/Customer.Core.csproj")
                ]);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\Customer.Core\Customer.Core.csproj" />
                  </ItemGroup>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            CreateProjectFile(repositoryRoot, "src/Customer.Core/Customer.Core.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            var referenceEdge = Assert.Single(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.References);
            Assert.Equal("project://src/Customer.Api/Customer.Api.csproj", referenceEdge.SourceNodeStableKey.Value);
            Assert.Equal("project://src/Customer.Core/Customer.Core.csproj", referenceEdge.TargetNodeStableKey.Value);
            Assert.Contains("\"projectReference.declaredInclude\":\"..\\\\Customer.Core\\\\Customer.Core.csproj\"", referenceEdge.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Customer.Api/Customer.Api.csproj" && evidence.SnippetPreview == "..\\Customer.Core\\Customer.Core.csproj");
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies missing repository-contained referenced project files produce warnings and evidence without creating a dependency edge.
        /// </summary>
        /// <returns>A task that completes after unresolved-reference assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenProjectReferenceTargetIsMissing_ShouldWarnAndPreserveEvidenceWithoutEdge()
        {
            // Missing referenced project files should remain actionable warnings rather than blocking otherwise useful project extraction.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "MissingReferenceSuite.sln", [ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj")]);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\Missing\Missing.csproj" />
                  </ItemGroup>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            Assert.DoesNotContain(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.References);
            Assert.Contains(snapshot.Warnings, warning => warning.Contains("does not exist", StringComparison.OrdinalIgnoreCase) && warning.Contains("Missing.csproj", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Customer.Api/Customer.Api.csproj" && evidence.SnippetPreview == "..\\Missing\\Missing.csproj");
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies duplicate `ProjectReference` items create one deterministic `REFERENCES` edge while preserving source evidence.
        /// </summary>
        /// <returns>A task that completes after duplicate-reference assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenProjectReferenceIsDuplicated_ShouldDeduplicateReferencesEdge()
        {
            // Duplicate declarations can occur through hand-edited project files; graph dependencies must remain deterministic.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(
                repositoryRoot,
                "DuplicateReferenceSuite.sln",
                [
                    ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj"),
                    ProjectDeclaration.CSharp("Customer.Core", "src/Customer.Core/Customer.Core.csproj")
                ]);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\Customer.Core\Customer.Core.csproj" />
                    <ProjectReference Include="..\Customer.Core\Customer.Core.csproj" />
                  </ItemGroup>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            CreateProjectFile(repositoryRoot, "src/Customer.Core/Customer.Core.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            Assert.Single(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.References);
            Assert.Equal(2, snapshot.Evidence.Count(evidence => evidence.FilePath.Value == "src/Customer.Api/Customer.Api.csproj" && evidence.SnippetPreview == "..\\Customer.Core\\Customer.Core.csproj"));
        }

        /// <summary>
        /// Verifies projects shared across submitted solutions stay deduplicated while cross-solution references remain visible.
        /// </summary>
        /// <returns>A task that completes after multi-solution dependency assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenSubmittedSolutionsShareProjectsAndCrossReference_ShouldPreserveMembershipAndReferences()
        {
            // Multi-solution repositories should retain explicit solution membership and still expose project-to-project dependencies.
            string repositoryRoot = CreateRepositoryRoot();
            string firstSolutionPath = CreateSolutionFile(
                repositoryRoot,
                "CustomerSuite.sln",
                [
                    ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj"),
                    ProjectDeclaration.CSharp("Customer.Shared", "src/Customer.Shared/Customer.Shared.csproj")
                ]);
            string secondSolutionPath = CreateSolutionFile(
                repositoryRoot,
                "ToolsSuite.sln",
                [
                    ProjectDeclaration.CSharp("Customer.Tools", "tools/Customer.Tools/Customer.Tools.csproj"),
                    ProjectDeclaration.CSharp("Customer.Shared", "src/Customer.Shared/Customer.Shared.csproj")
                ]);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\Customer.Shared\Customer.Shared.csproj" />
                  </ItemGroup>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            CreateProjectFile(repositoryRoot, "tools/Customer.Tools/Customer.Tools.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\..\src\Customer.Shared\Customer.Shared.csproj" />
                  </ItemGroup>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            CreateProjectFile(repositoryRoot, "src/Customer.Shared/Customer.Shared.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [firstSolutionPath, secondSolutionPath]);

            Assert.Equal(3, snapshot.Nodes.Count(node => node.NodeKind == NodeKind.Project));
            Assert.Equal(2, snapshot.Edges.Count(edge => edge.EdgeKind == EdgeKind.Contains && edge.TargetNodeStableKey.Value == "project://src/Customer.Shared/Customer.Shared.csproj"));
            Assert.Equal(2, snapshot.Edges.Count(edge => edge.EdgeKind == EdgeKind.References && edge.TargetNodeStableKey.Value == "project://src/Customer.Shared/Customer.Shared.csproj"));
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies repository-contained referenced projects outside submitted solutions are represented as project nodes and reference targets.
        /// </summary>
        /// <returns>A task that completes after out-of-solution reference assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenProjectReferenceTargetsRepositoryProjectOutsideSubmittedSolutions_ShouldExtractTargetProjectNodeAndReference()
        {
            // The submitted solution bounds solution membership, but repository-contained project references still provide dependency facts.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "OutOfSolutionReferenceSuite.sln", [ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj")]);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\Customer.Internal\Customer.Internal.csproj" />
                  </ItemGroup>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            CreateProjectFile(repositoryRoot, "src/Customer.Internal/Customer.Internal.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>Customer.Internal.Assembly</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Project && node.StableKey.Value == "project://src/Customer.Internal/Customer.Internal.csproj");
            Assert.DoesNotContain(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Contains && edge.TargetNodeStableKey.Value == "project://src/Customer.Internal/Customer.Internal.csproj");
            Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.References && edge.SourceNodeStableKey.Value == "project://src/Customer.Api/Customer.Api.csproj" && edge.TargetNodeStableKey.Value == "project://src/Customer.Internal/Customer.Internal.csproj");
            Assert.Contains("Customer.Internal.Assembly", snapshot.Nodes.Single(node => node.StableKey.Value == "project://src/Customer.Internal/Customer.Internal.csproj").Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Executes the production stage against accepted input and returns the accumulated snapshot.
        /// </summary>
        /// <param name="repositoryRoot">The temporary repository root to submit.</param>
        /// <param name="solutionPaths">The absolute submitted solution paths.</param>
        /// <returns>The snapshot accumulated by the stage.</returns>
        private static async Task<ExtractedArchitectureSnapshot> ExecuteStageAsync(string repositoryRoot, IReadOnlyList<string> solutionPaths)
        {
            // Direct stage execution keeps the tests focused on extraction behavior without starting API hosting or Aspire composition.
            ResolvedExtractionInput input = CreateResolvedInput(repositoryRoot, solutionPaths);
            ExtractionRun run = CreateRun(input);
            ArchitectureSnapshotAccumulator accumulation = new();
            RepositorySolutionExtractionStage stage = new();

            ExtractionStageResult result = await stage.ExecuteAsync(new ExtractionStageContext(input, run, accumulation), CancellationToken.None);

            Assert.False(result.HasBlockingError, result.ErrorMessage);
            return accumulation.ToSnapshot();
        }

        /// <summary>
        /// Creates an isolated temporary repository root for project metadata tests.
        /// </summary>
        /// <returns>The absolute temporary repository root path.</returns>
        private string CreateRepositoryRoot()
        {
            // A unique root prevents project-relative path assertions from seeing stale files across tests.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-wp005-project-metadata-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repositoryRoot);
            _temporaryDirectories.Add(repositoryRoot);
            return repositoryRoot;
        }

        /// <summary>
        /// Creates a minimal Visual Studio solution containing the supplied project declarations.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that contains the solution file.</param>
        /// <param name="relativeSolutionPath">The repository-relative solution path to write.</param>
        /// <param name="declarations">The project declarations to write into the solution file.</param>
        /// <returns>The absolute solution path written to disk.</returns>
        private static string CreateSolutionFile(string repositoryRoot, string relativeSolutionPath, IReadOnlyList<ProjectDeclaration> declarations)
        {
            // The fixture uses real solution syntax so the production parser exercises the same path as runtime extraction.
            string solutionPath = Path.Combine(repositoryRoot, relativeSolutionPath);
            Directory.CreateDirectory(Path.GetDirectoryName(solutionPath)!);
            List<string> lines =
            [
                "Microsoft Visual Studio Solution File, Format Version 12.00",
                "# Visual Studio Version 17"
            ];

            foreach (ProjectDeclaration declaration in declarations)
            {
                lines.Add($"Project(\"{declaration.ProjectTypeGuid}\") = \"{declaration.Name}\", \"{declaration.DeclaredPath}\", \"{declaration.ProjectGuid}\"");
                lines.Add("EndProject");
            }

            lines.Add("Global");
            lines.Add("EndGlobal");
            File.WriteAllText(solutionPath, string.Join(Environment.NewLine, lines));
            return solutionPath;
        }

        /// <summary>
        /// Creates a project file with the supplied XML content.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that contains the project file.</param>
        /// <param name="relativeProjectPath">The repository-relative project path to write.</param>
        /// <param name="xml">The XML project file content.</param>
        private static void CreateProjectFile(string repositoryRoot, string relativeProjectPath, string xml)
        {
            // Project fixtures are plain XML files so extraction can verify deterministic parsing without invoking MSBuild.
            string projectPath = Path.Combine(repositoryRoot, relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(projectPath, xml);
        }

        /// <summary>
        /// Creates normalized extraction input for one focused stage test.
        /// </summary>
        /// <param name="repositoryRoot">The accepted repository root path.</param>
        /// <param name="solutionPaths">The accepted submitted solution paths.</param>
        /// <returns>Resolved extraction input for the production stage.</returns>
        private static ResolvedExtractionInput CreateResolvedInput(string repositoryRoot, IReadOnlyList<string> solutionPaths)
        {
            // The input mirrors WP004 validation output while avoiding the HTTP layer in project extractor tests.
            return new ResolvedExtractionInput(
                repositoryRoot,
                solutionPaths,
                BranchName: "main",
                CommitSha: "abcdef1234567890",
                RequestedBy: "developer@example.invalid",
                Metadata: new Dictionary<string, string>
                {
                    ["source"] = "wp005-project-metadata-test"
                });
        }

        /// <summary>
        /// Creates an accepted extraction run for direct stage execution.
        /// </summary>
        /// <param name="input">The resolved input represented by the run summary.</param>
        /// <returns>An extraction run suitable for stage context construction.</returns>
        private static ExtractionRun CreateRun(ResolvedExtractionInput input)
        {
            // The deterministic run id keeps snapshot-scoped keys stable in assertions when needed.
            return new ExtractionRun(
                new ExtractionRunId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
                ExtractionRunStatus.Queued,
                new ExtractionRunRequestSummary(
                    input.RepositoryRootDirectory,
                    input.SolutionPaths,
                    input.BranchName,
                    input.CommitSha,
                    input.RequestedBy,
                    input.Metadata.Keys.ToArray()),
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                completedUtc: null,
                new ExtractionRunProgress("Queued", "Queued for test execution.", 0, new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero)),
                warnings: null,
                errors: null,
                snapshotIdentity: null);
        }

        /// <summary>
        /// Represents a solution project declaration fixture.
        /// </summary>
        /// <param name="ProjectTypeGuid">The solution project type GUID to write.</param>
        /// <param name="Name">The project display name to write.</param>
        /// <param name="DeclaredPath">The project path text to write.</param>
        /// <param name="ProjectGuid">The project GUID to write.</param>
        private sealed record ProjectDeclaration(string ProjectTypeGuid, string Name, string DeclaredPath, string ProjectGuid)
        {
            /// <summary>
            /// Creates a C# project declaration fixture.
            /// </summary>
            /// <param name="name">The C# project display name.</param>
            /// <param name="declaredPath">The project path declared in the solution.</param>
            /// <returns>A C# project declaration.</returns>
            internal static ProjectDeclaration CSharp(string name, string declaredPath)
            {
                // The GUID matches the common C# project type GUID used by Visual Studio solution files.
                return new ProjectDeclaration("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}", name, declaredPath, "{11111111-1111-1111-1111-111111111111}");
            }

            /// <summary>
            /// Creates a VB.NET project declaration fixture.
            /// </summary>
            /// <param name="name">The VB.NET project display name.</param>
            /// <param name="declaredPath">The project path declared in the solution.</param>
            /// <returns>A VB.NET project declaration.</returns>
            internal static ProjectDeclaration VisualBasic(string name, string declaredPath)
            {
                // The GUID matches the common Visual Basic project type GUID used by Visual Studio solution files.
                return new ProjectDeclaration("{F184B08F-C81C-45F6-A57F-5ABD9991F28F}", name, declaredPath, "{22222222-2222-2222-2222-222222222222}");
            }

            /// <summary>
            /// Creates an unsupported project declaration fixture.
            /// </summary>
            /// <param name="name">The unsupported project display name.</param>
            /// <param name="declaredPath">The unsupported project path declared in the solution.</param>
            /// <returns>An unsupported project declaration.</returns>
            internal static ProjectDeclaration Unsupported(string name, string declaredPath)
            {
                // The arbitrary GUID lets tests verify unsupported declaration behavior without requiring a real project-system adapter.
                return new ProjectDeclaration("{930C7802-8A8C-48F9-8165-68863BCCD9DD}", name, declaredPath, "{33333333-3333-3333-3333-333333333333}");
            }
        }
    }
}
