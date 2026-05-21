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
        /// Verifies direct SDK-style `PackageReference` items create package nodes, `USES_PACKAGE` edges, asset metadata, and evidence.
        /// </summary>
        /// <returns>A task that completes after direct package-reference graph assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenProjectDeclaresDirectPackageReference_ShouldCreatePackageNodeUseEdgeAndEvidence()
        {
            // Direct package versions are the simplest SDK-style dependency path and should not require package restore.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "PackageSuite.sln", [ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj")]);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Serilog" Version="4.0.0" PrivateAssets="all" IncludeAssets="runtime; build" ExcludeAssets="contentFiles" Aliases="Logging" />
                  </ItemGroup>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            var packageNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Package);
            Assert.Equal("package://serilog/version/4.0.0", packageNode.StableKey.Value);
            Assert.Contains("\"package.versionSource\":\"Direct\"", packageNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            var packageEdge = Assert.Single(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesPackage);
            Assert.Equal("project://src/Customer.Api/Customer.Api.csproj", packageEdge.SourceNodeStableKey.Value);
            Assert.Equal(packageNode.StableKey.Value, packageEdge.TargetNodeStableKey.Value);
            Assert.Contains("\"packageReference.privateAssets\":\"all\"", packageEdge.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("\"packageReference.aliases\":\"Logging\"", packageEdge.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Customer.Api/Customer.Api.csproj" && evidence.SymbolName == "Serilog");
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies local central package versions are resolved from repository-contained `Directory.Packages.props` files.
        /// </summary>
        /// <returns>A task that completes after central package version assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenCentralPackageVersionIsDeclared_ShouldResolveLocalCentralVersion()
        {
            // Central Package Management resolution reads only local props XML and never performs NuGet feed access.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "CentralPackageSuite.sln", [ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj")]);
            CreateProjectFile(repositoryRoot, "Directory.Packages.props", """
                <Project>
                  <ItemGroup>
                    <PackageVersion Include="Microsoft.Extensions.Logging" Version="10.0.0" />
                  </ItemGroup>
                </Project>
                """);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Microsoft.Extensions.Logging" />
                  </ItemGroup>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            var packageNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Package);
            Assert.Equal("package://microsoft.extensions.logging/version/10.0.0", packageNode.StableKey.Value);
            Assert.Contains("\"package.versionSource\":\"Central\"", packageNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("\"packageReference.versionSource\":\"Central\"", snapshot.Edges.Single(edge => edge.EdgeKind == EdgeKind.UsesPackage).Metadata.ToCanonicalJson(), StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies package references without direct or local central versions are retained with explicit unknown version state.
        /// </summary>
        /// <returns>A task that completes after unknown-version package assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenPackageVersionCannotBeResolved_ShouldRetainUnknownVersionState()
        {
            // Unknown versions should remain graph facts so later workflows can explain incomplete dependency information.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "UnknownPackageSuite.sln", [ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj")]);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Newtonsoft.Json" />
                  </ItemGroup>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            var packageNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Package);
            Assert.Equal("package://newtonsoft.json/version-source/unknown", packageNode.StableKey.Value);
            Assert.Contains("\"package.versionSource\":\"Unknown\"", packageNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("\"packageReference.versionSource\":\"Unknown\"", snapshot.Edges.Single(edge => edge.EdgeKind == EdgeKind.UsesPackage).Metadata.ToCanonicalJson(), StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies repository-contained imported `.props` files are inspected for package declarations without traversing external imports.
        /// </summary>
        /// <returns>A task that completes after imported package-reference assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenRepositoryContainedPropsImportDeclaresPackageReference_ShouldExtractImportedPackageReference()
        {
            // Imported package references are supported only when the import is explicit, local, repository-contained, and static XML.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "ImportedPackageSuite.sln", [ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj")]);
            CreateProjectFile(repositoryRoot, "build/Packages.props", """
                <Project>
                  <ItemGroup>
                    <PackageReference Include="Polly" Version="8.0.0" />
                  </ItemGroup>
                </Project>
                """);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="..\..\build\Packages.props" />
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Package && node.StableKey.Value == "package://polly/version/8.0.0");
            var packageUseEdge = Assert.Single(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesPackage);
            Assert.Contains("\"packageReference.isImported\":true", packageUseEdge.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "build/Packages.props" && evidence.SymbolName == "Polly");
        }

        /// <summary>
        /// Verifies duplicate package references collapse to one package-use edge and do not require restore or external feeds.
        /// </summary>
        /// <returns>A task that completes after duplicate package-reference assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenPackageReferenceIsDuplicated_ShouldDeduplicateUsesPackageEdge()
        {
            // Duplicate package declarations should not create duplicate project-to-package graph dependencies.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "DuplicatePackageSuite.sln", [ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj")]);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Dapper" Version="2.1.66" />
                    <PackageReference Include="dapper" Version="2.1.66" />
                  </ItemGroup>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Package);
            Assert.Single(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesPackage);
            Assert.Equal(2, snapshot.Evidence.Count(evidence => evidence.FilePath.Value == "src/Customer.Api/Customer.Api.csproj" && string.Equals(evidence.SymbolName, "Dapper", StringComparison.OrdinalIgnoreCase)));
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies old-style projects with an associated `packages.config` file contribute package nodes, package-use edges, target framework metadata, and line evidence.
        /// </summary>
        /// <returns>A task that completes after legacy package graph assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenOldStyleProjectHasPackagesConfig_ShouldExtractLegacyPackageDependencies()
        {
            // Legacy .NET Framework estates commonly store NuGet dependencies beside the project file in packages.config rather than PackageReference items.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "LegacyPackageSuite.sln", [ProjectDeclaration.CSharp("Legacy.Library", "src/Legacy.Library/Legacy.Library.csproj")]);
            CreateProjectFile(repositoryRoot, "src/Legacy.Library/Legacy.Library.csproj", """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
                    <OutputType>Library</OutputType>
                  </PropertyGroup>
                </Project>
                """);
            CreateProjectFile(repositoryRoot, "src/Legacy.Library/packages.config", """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net472" />
                  <package id="Castle.Core" version="5.1.1" targetFramework="net472" />
                </packages>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Package && node.StableKey.Value == "package://newtonsoft.json/version/13.0.3");
            Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Package && node.StableKey.Value == "package://castle.core/version/5.1.1");
            Assert.Equal(2, snapshot.Edges.Count(edge => edge.EdgeKind == EdgeKind.UsesPackage));
            var newtonsoftEdge = Assert.Single(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesPackage && edge.TargetNodeStableKey.Value == "package://newtonsoft.json/version/13.0.3");
            Assert.Contains("\"packageReference.sourceType\":\"packages.config\"", newtonsoftEdge.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("\"packageReference.targetFramework\":\"net472\"", newtonsoftEdge.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Legacy.Library/packages.config" && evidence.SymbolName == "Newtonsoft.Json" && evidence.StartLine == 3);
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies old-style projects without an associated `packages.config` file continue extracting project metadata without package warnings.
        /// </summary>
        /// <returns>A task that completes after missing legacy package file behavior has been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenOldStyleProjectHasNoPackagesConfig_ShouldNotWarnOrCreateLegacyPackages()
        {
            // A non-SDK-style project does not prove that packages.config is expected, so absence alone should not become a noisy warning.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "LegacyNoPackageSuite.sln", [ProjectDeclaration.CSharp("Legacy.Library", "src/Legacy.Library/Legacy.Library.csproj")]);
            CreateProjectFile(repositoryRoot, "src/Legacy.Library/Legacy.Library.csproj", """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
            Assert.DoesNotContain(snapshot.Nodes, node => node.NodeKind == NodeKind.Package);
            Assert.DoesNotContain(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesPackage);
            Assert.DoesNotContain(snapshot.Warnings, warning => warning.Contains("packages.config", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies malformed `packages.config` files produce safe warnings and file evidence instead of blocking extraction with raw XML exceptions.
        /// </summary>
        /// <returns>A task that completes after malformed legacy package handling has been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenPackagesConfigIsMalformed_ShouldWarnAndPreserveFileEvidence()
        {
            // Malformed legacy package files should remain actionable diagnostics without exposing parser exception types or local absolute paths.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "MalformedLegacyPackageSuite.sln", [ProjectDeclaration.CSharp("Legacy.Library", "src/Legacy.Library/Legacy.Library.csproj")]);
            CreateProjectFile(repositoryRoot, "src/Legacy.Library/Legacy.Library.csproj", """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
                  </PropertyGroup>
                </Project>
                """);
            CreateProjectFile(repositoryRoot, "src/Legacy.Library/packages.config", """
                <packages>
                  <package id="Broken.Package" version="1.0.0">
                </packages>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
            Assert.DoesNotContain(snapshot.Nodes, node => node.NodeKind == NodeKind.Package);
            Assert.Contains(snapshot.Warnings, warning => warning.Contains("packages.config", StringComparison.OrdinalIgnoreCase) && !warning.Contains(repositoryRoot, StringComparison.OrdinalIgnoreCase) && !warning.Contains("XmlException", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Legacy.Library/packages.config" && evidence.SnippetPreview == "Malformed packages.config file could not be parsed.");
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies analyzer declarations are extracted as project metadata with analyzer evidence and repository artifact nodes.
        /// </summary>
        /// <returns>A task that completes after analyzer metadata and evidence assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenProjectDeclaresAnalyzer_ShouldPreserveAnalyzerMetadataEvidenceAndFilePathNode()
        {
            // Analyzer references are build-time inputs that should be visible without loading Roslyn workspaces or executing targets.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "AnalyzerSuite.sln", [ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj")]);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Analyzer Include="..\..\analyzers\Customer.Analyzers.dll" />
                  </ItemGroup>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            CreateProjectFile(repositoryRoot, "analyzers/Customer.Analyzers.dll", string.Empty);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            var projectNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
            Assert.Contains("\"project.analyzerReferenceCount\":1", projectNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("Customer.Analyzers.dll", projectNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Customer.Api/Customer.Api.csproj" && evidence.SymbolName == "analyzers/Customer.Analyzers.dll" && evidence.SnippetPreview is not null && evidence.SnippetPreview.Contains("Analyzer", StringComparison.Ordinal) && evidence.SnippetPreview.Contains("Customer.Analyzers.dll", StringComparison.Ordinal));
            Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.FilePath && node.StableKey.Value == "file://analyzers/Customer.Analyzers.dll");
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies relevant source artifacts are represented as deterministic `FilePath` nodes when they support extracted facts.
        /// </summary>
        /// <returns>A task that completes after file-path node assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenExtractionUsesProjectPackageAndImportedArtifacts_ShouldContributeFilePathNodes()
        {
            // FilePath nodes make the physical artifacts behind solution, project, package, central package, and imported-build facts queryable.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "ArtifactSuite.sln", [ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj")]);
            CreateProjectFile(repositoryRoot, "Directory.Packages.props", """
                <Project>
                  <ItemGroup>
                    <PackageVersion Include="Serilog" Version="4.0.0" />
                  </ItemGroup>
                </Project>
                """);
            CreateProjectFile(repositoryRoot, "Directory.Build.props", """
                <Project>
                  <PropertyGroup>
                    <RepositoryBuild>true</RepositoryBuild>
                  </PropertyGroup>
                </Project>
                """);
            CreateProjectFile(repositoryRoot, "build/Packages.targets", """
                <Project>
                  <ItemGroup>
                    <PackageReference Include="Polly" Version="8.0.0" />
                  </ItemGroup>
                </Project>
                """);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="..\..\build\Packages.targets" />
                  <ItemGroup>
                    <PackageReference Include="Serilog" />
                  </ItemGroup>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            string[] expectedFileKeys =
            [
                "file://ArtifactSuite.sln",
                "file://src/Customer.Api/Customer.Api.csproj",
                "file://Directory.Packages.props",
                "file://Directory.Build.props",
                "file://build/Packages.targets"
            ];

            foreach (string expectedFileKey in expectedFileKeys)
            {
                Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.FilePath && node.StableKey.Value == expectedFileKey);
            }

            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies external or unsafe imports do not produce imported artifact nodes or imported package evidence.
        /// </summary>
        /// <returns>A task that completes after unsafe import exclusion assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenProjectDeclaresExternalOrDynamicImports_ShouldExcludeImportedArtifacts()
        {
            // Imports requiring property expansion or outside-repository traversal are excluded because they would require unsafe build evaluation.
            string repositoryRoot = CreateRepositoryRoot();
            string externalRoot = CreateRepositoryRoot();
            string externalImport = Path.Combine(externalRoot, "External.targets");
            File.WriteAllText(externalImport, "<Project />");
            string solutionPath = CreateSolutionFile(repositoryRoot, "ExternalImportSuite.sln", [ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj")]);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="$(MSBuildThisFileDirectory)Dynamic.targets" />
                  <Import Project="{{externalImport}}" />
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            Assert.DoesNotContain(snapshot.Nodes, node => node.NodeKind == NodeKind.FilePath && node.DisplayName.Contains("External.targets", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(snapshot.Evidence, evidence => evidence.FilePath.Value.Contains("External.targets", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies XML-backed evidence includes line spans plus deterministic snippet hashes and concise previews.
        /// </summary>
        /// <returns>A task that completes after strengthened evidence assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenXmlEvidenceIsCaptured_ShouldIncludeSnippetHashAndPreview()
        {
            // Evidence precision lets later troubleshooting explain the exact XML fact without copying complete source files into metadata.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "EvidencePrecisionSuite.sln", [ProjectDeclaration.CSharp("Customer.Api", "src/Customer.Api/Customer.Api.csproj")]);
            CreateProjectFile(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Serilog" Version="4.0.0" />
                  </ItemGroup>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            var packageEvidence = Assert.Single(snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Customer.Api/Customer.Api.csproj" && evidence.SymbolName == "Serilog");
            Assert.Equal(3, packageEvidence.StartLine);
            Assert.Equal(3, packageEvidence.EndLine);
            Assert.StartsWith("sha256:", packageEvidence.SnippetHash, StringComparison.Ordinal);
            Assert.Contains("PackageReference", packageEvidence.SnippetPreview, StringComparison.Ordinal);
            Assert.Contains("Serilog", packageEvidence.SnippetPreview, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies application classification covers every required high-confidence project category through deterministic metadata indicators.
        /// </summary>
        /// <param name="projectName">The project display name written to the solution fixture.</param>
        /// <param name="relativeProjectPath">The repository-relative project path written to the solution fixture.</param>
        /// <param name="projectXml">The project XML containing the classification indicators.</param>
        /// <param name="expectedApplicationType">The expected application type metadata value.</param>
        /// <returns>A task that completes after classification metadata has been asserted.</returns>
        [Theory]
        [InlineData("Modern.Web", "src/Modern.Web/Modern.Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>", "AspNetCoreWebApp")]
        [InlineData("Modern.Api", "src/Modern.Api/Modern.Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Swashbuckle.AspNetCore\" Version=\"7.0.0\" /></ItemGroup></Project>", "AspNetCoreWebApi")]
        [InlineData("Classic.Web", "src/Classic.Web/Classic.Web.csproj", "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><PropertyGroup><TargetFrameworkVersion>v4.8</TargetFrameworkVersion><ProjectTypeGuids>{349C5851-65DF-11DA-9384-00065B846F21};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids></PropertyGroup></Project>", "ClassicAspNetWebApp")]
        [InlineData("Legacy.Forms", "src/Legacy.Forms/Legacy.Forms.csproj", "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><ItemGroup><Content Include=\"Default.aspx\" /></ItemGroup><PropertyGroup><TargetFrameworkVersion>v4.8</TargetFrameworkVersion></PropertyGroup></Project>", "WebFormsApp")]
        [InlineData("Legacy.Mvc", "src/Legacy.Mvc/Legacy.Mvc.csproj", "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><ItemGroup><Reference Include=\"System.Web.Mvc\" /></ItemGroup><PropertyGroup><TargetFrameworkVersion>v4.8</TargetFrameworkVersion></PropertyGroup></Project>", "MvcApp")]
        [InlineData("Legacy.Api", "src/Legacy.Api/Legacy.Api.csproj", "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><ItemGroup><PackageReference Include=\"Microsoft.AspNet.WebApi.Core\" Version=\"5.2.9\" /></ItemGroup><PropertyGroup><TargetFrameworkVersion>v4.8</TargetFrameworkVersion></PropertyGroup></Project>", "WebApi2App")]
        [InlineData("Modern.Console", "src/Modern.Console/Modern.Console.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>", "ConsoleApp")]
        [InlineData("Modern.Worker", "src/Modern.Worker/Modern.Worker.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Worker\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>", "WorkerService")]
        [InlineData("Modern.Library", "src/Modern.Library/Modern.Library.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Library</OutputType></PropertyGroup></Project>", "ClassLibrary")]
        [InlineData("Modern.Tests", "test/Modern.Tests/Modern.Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"18.0.1\" /></ItemGroup></Project>", "TestProject")]
        [InlineData("Modern.Tools", "tools/Modern.Tools/Modern.Tools.csproj", "<Project Sdk=\"Microsoft.Build.NoTargets\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>", "ToolingProject")]
        public async Task ExecuteAsync_WhenProjectHasApplicationTypeIndicators_ShouldClassifyRequiredApplicationType(string projectName, string relativeProjectPath, string projectXml, string expectedApplicationType)
        {
            // The table exercises direct SDK, project GUID, output type, package, content, and reference indicators without invoking build evaluation.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "ClassificationSuite.sln", [ProjectDeclaration.CSharp(projectName, relativeProjectPath)]);
            CreateProjectFile(repositoryRoot, relativeProjectPath, projectXml);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            var projectNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
            string metadata = projectNode.Metadata.ToCanonicalJson();
            Assert.Contains($"\"project.applicationType\":\"{expectedApplicationType}\"", metadata, StringComparison.Ordinal);
            Assert.Contains("\"project.applicationTypeConfidence\":\"High\"", metadata, StringComparison.Ordinal);
            Assert.Contains("\"project.applicationTypeConfidenceValue\":0.9", metadata, StringComparison.Ordinal);
            Assert.Contains("\"project.applicationTypeEvidence\"", metadata, StringComparison.Ordinal);
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies repository-contained source indicators can produce medium-confidence classification when direct project-file metadata is absent.
        /// </summary>
        /// <returns>A task that completes after medium-confidence worker classification has been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenSourceArtifactIndicatesWorkerService_ShouldClassifyWithMediumConfidence()
        {
            // WP005 may safely inspect small repository-contained source artifacts for strong textual indicators without performing Roslyn semantic analysis.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "WorkerArtifactSuite.sln", [ProjectDeclaration.CSharp("BackgroundProcessor", "src/BackgroundProcessor/BackgroundProcessor.csproj")]);
            CreateProjectFile(repositoryRoot, "src/BackgroundProcessor/BackgroundProcessor.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            CreateTextFile(repositoryRoot, "src/BackgroundProcessor/Worker.cs", "public sealed class Worker : BackgroundService { }");

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            var projectNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
            string metadata = projectNode.Metadata.ToCanonicalJson();
            Assert.Contains("\"project.applicationType\":\"WorkerService\"", metadata, StringComparison.Ordinal);
            Assert.Contains("\"project.applicationTypeConfidence\":\"Medium\"", metadata, StringComparison.Ordinal);
            Assert.Contains("\"project.applicationTypeConfidenceValue\":0.5", metadata, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies weak name-based indicators remain low confidence and do not masquerade as direct project metadata evidence.
        /// </summary>
        /// <returns>A task that completes after low-confidence tooling classification has been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenOnlyProjectNameIndicatesTooling_ShouldClassifyWithLowConfidence()
        {
            // Naming is intentionally the weakest classification source because repository naming conventions can be inconsistent.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "NamingHeuristicSuite.sln", [ProjectDeclaration.CSharp("Repository.Tools", "src/Repository.Tools/Repository.Tools.csproj")]);
            CreateProjectFile(repositoryRoot, "src/Repository.Tools/Repository.Tools.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            var projectNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
            string metadata = projectNode.Metadata.ToCanonicalJson();
            Assert.Contains("\"project.applicationType\":\"ToolingProject\"", metadata, StringComparison.Ordinal);
            Assert.Contains("\"project.applicationTypeConfidence\":\"Low\"", metadata, StringComparison.Ordinal);
            Assert.Contains("\"project.applicationTypeConfidenceValue\":0.25", metadata, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies contradictory high-confidence indicators produce Unknown instead of an arbitrary category guess.
        /// </summary>
        /// <returns>A task that completes after contradictory classification metadata has been asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenProjectHasContradictoryApplicationTypeIndicators_ShouldClassifyAsUnknown()
        {
            // A web SDK combined with explicit test project packages is contradictory for WP005 classification, so Unknown is safer than guessing.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, "ContradictorySuite.sln", [ProjectDeclaration.CSharp("Conflicted.Project", "src/Conflicted.Project/Conflicted.Project.csproj")]);
            CreateProjectFile(repositoryRoot, "src/Conflicted.Project/Conflicted.Project.csproj", """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
                  </ItemGroup>
                </Project>
                """);

            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);

            var projectNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
            string metadata = projectNode.Metadata.ToCanonicalJson();
            Assert.Contains("\"project.applicationType\":\"Unknown\"", metadata, StringComparison.Ordinal);
            Assert.Contains("\"project.applicationTypeUnknown\":true", metadata, StringComparison.Ordinal);
            Assert.Contains("\"project.applicationTypeUnknownReason\":\"Contradictory high-confidence indicators were found.\"", metadata, StringComparison.Ordinal);
            Assert.Contains("\"project.applicationTypeContradictions\"", metadata, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies insufficient evidence produces deterministic Unknown metadata and stable repeated results.
        /// </summary>
        /// <returns>A task that completes after Unknown and deterministic metadata assertions have run.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenApplicationTypeEvidenceIsInsufficient_ShouldClassifyAsUnknownDeterministically()
        {
            // Projects without direct, artifact, or justified naming indicators should preserve uncertainty for downstream consumers.
            string projectXml = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """;
            string firstMetadata = await ExtractSingleProjectMetadataAsync("UnknownSuite.sln", "Neutral.Component", "src/Neutral.Component/Neutral.Component.csproj", projectXml);
            string secondMetadata = await ExtractSingleProjectMetadataAsync("UnknownSuite.sln", "Neutral.Component", "src/Neutral.Component/Neutral.Component.csproj", projectXml);

            Assert.Contains("\"project.applicationType\":\"Unknown\"", firstMetadata, StringComparison.Ordinal);
            Assert.Contains("\"project.applicationTypeUnknown\":true", firstMetadata, StringComparison.Ordinal);
            Assert.Contains("\"project.applicationTypeUnknownReason\":\"No supported application type indicators were found.\"", firstMetadata, StringComparison.Ordinal);
            Assert.Equal(firstMetadata, secondMetadata);
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
        /// Extracts canonical project-node metadata for one temporary project fixture.
        /// </summary>
        /// <param name="solutionName">The repository-relative solution file name to create.</param>
        /// <param name="projectName">The solution project display name.</param>
        /// <param name="relativeProjectPath">The repository-relative project path.</param>
        /// <param name="projectXml">The project XML content to write.</param>
        /// <returns>The canonical metadata JSON for the single extracted project node.</returns>
        private async Task<string> ExtractSingleProjectMetadataAsync(string solutionName, string projectName, string relativeProjectPath, string projectXml)
        {
            // This helper makes deterministic comparisons concise while still exercising the production stage and accumulator.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot, solutionName, [ProjectDeclaration.CSharp(projectName, relativeProjectPath)]);
            CreateProjectFile(repositoryRoot, relativeProjectPath, projectXml);
            ExtractedArchitectureSnapshot snapshot = await ExecuteStageAsync(repositoryRoot, [solutionPath]);
            var projectNode = Assert.Single(snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
            return projectNode.Metadata.ToCanonicalJson();
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
        /// Creates a text source or configuration fixture with the supplied content.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that contains the file.</param>
        /// <param name="relativePath">The repository-relative file path to write.</param>
        /// <param name="content">The text content to write.</param>
        private static void CreateTextFile(string repositoryRoot, string relativePath, string content)
        {
            // Source and configuration fixtures let classification tests exercise safe repository-contained artifact indicators.
            string filePath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, content);
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
