using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Domain.Graph.ControlledValues;
using Archon.Infrastructure.Roslyn.Extraction;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Archon.Infrastructure.Roslyn.Tests
{
    /// <summary>
    /// Verifies the Roslyn infrastructure semantic extraction stage loads fixture solutions and contributes graph facts through application accumulation.
    /// </summary>
    public sealed class RoslynSemanticExtractionStageTests : IDisposable
    {
        /// <summary>
        /// Stores temporary repositories created by the semantic infrastructure tests.
        /// </summary>
        private readonly List<string> _temporaryDirectories = [];

        /// <summary>
        /// Deletes temporary repositories created by the semantic infrastructure tests.
        /// </summary>
        public void Dispose()
        {
            // Each test owns isolated source files so cleanup can remove the whole temporary repository tree.
            foreach (string temporaryDirectory in _temporaryDirectories)
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies the semantic stage loads both C# and Visual Basic projects declared by one solution.
        /// </summary>
        /// <returns>A task that completes after graph contributions are asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenSolutionContainsCSharpAndVisualBasicProjects_ShouldContributeSemanticFacts()
        {
            // The fixture uses real files but no MSBuildWorkspace or Aspire AppHost, matching the Work Item 5 validation boundary.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot);
            CreateCSharpProject(repositoryRoot);
            CreateVisualBasicProject(repositoryRoot);
            ArchitectureSnapshotAccumulator accumulation = new();
            ExtractionRun run = CreateRun(repositoryRoot, solutionPath);
            ResolvedExtractionInput input = CreateResolvedInput(repositoryRoot, solutionPath);
            RoslynSemanticExtractionStage stage = new(NullLogger<RoslynSemanticExtractionStage>.Instance);

            ExtractionStageResult result = await stage.ExecuteAsync(new ExtractionStageContext(input, run, accumulation), CancellationToken.None);

            var snapshot = accumulation.ToSnapshot();
            Assert.False(result.HasBlockingError);
            Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Type && node.DisplayName == "CustomerService" && node.Language == "C#");
            Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Type && node.DisplayName == "LedgerService" && node.Language == "VB.NET");
            Assert.Contains(snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Contains && edge.SourceNodeStableKey.Value.Contains("namespace", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Customer.Api/CustomerService.cs" && evidence.SymbolName == "CustomerService");
            Assert.Contains(snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Ledger.Worker/LedgerService.vb" && evidence.SymbolName == "LedgerService");
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies repeated semantic extraction over unchanged inputs produces deterministic graph identities.
        /// </summary>
        /// <returns>A task that completes after two extraction outputs are compared.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenInputsAreUnchanged_ShouldProduceDeterministicStableKeys()
        {
            // Determinism is measured using separate accumulators with the same run id so stable keys and relationship identities should match exactly.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot);
            CreateCSharpProject(repositoryRoot);
            ResolvedExtractionInput input = CreateResolvedInput(repositoryRoot, solutionPath);
            ExtractionRun run = CreateRun(repositoryRoot, solutionPath);
            RoslynSemanticExtractionStage stage = new(NullLogger<RoslynSemanticExtractionStage>.Instance);
            ArchitectureSnapshotAccumulator firstAccumulation = new();
            ArchitectureSnapshotAccumulator secondAccumulation = new();

            await stage.ExecuteAsync(new ExtractionStageContext(input, run, firstAccumulation), CancellationToken.None);
            await stage.ExecuteAsync(new ExtractionStageContext(input, run, secondAccumulation), CancellationToken.None);

            var firstSnapshot = firstAccumulation.ToSnapshot();
            var secondSnapshot = secondAccumulation.ToSnapshot();
            Assert.Equal(firstSnapshot.Nodes.Select(node => node.StableKey.Value), secondSnapshot.Nodes.Select(node => node.StableKey.Value));
            Assert.Equal(firstSnapshot.Edges.Select(edge => edge.StableKey.Value), secondSnapshot.Edges.Select(edge => edge.StableKey.Value));
            Assert.Equal(firstSnapshot.Evidence.Select(evidence => evidence.StableKey.Value), secondSnapshot.Evidence.Select(evidence => evidence.StableKey.Value));
        }

        /// <summary>
        /// Verifies compiler diagnostics are recorded as non-blocking warnings and evidence while resolvable declarations still persist.
        /// </summary>
        /// <returns>A task that completes after degraded extraction output is asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenSourceHasMissingReference_ShouldPreserveResolvableFactsAndDiagnostics()
        {
            // A missing dependency exercises degraded semantic behavior: the class declaration should survive and diagnostics should be visible.
            string repositoryRoot = CreateRepositoryRoot();
            string solutionPath = CreateSolutionFile(repositoryRoot);
            CreateCSharpProject(repositoryRoot, includeMissingReference: true);
            ResolvedExtractionInput input = CreateResolvedInput(repositoryRoot, solutionPath);
            ExtractionRun run = CreateRun(repositoryRoot, solutionPath);
            ArchitectureSnapshotAccumulator accumulation = new();
            RoslynSemanticExtractionStage stage = new(NullLogger<RoslynSemanticExtractionStage>.Instance);

            ExtractionStageResult result = await stage.ExecuteAsync(new ExtractionStageContext(input, run, accumulation), CancellationToken.None);

            var snapshot = accumulation.ToSnapshot();
            Assert.False(result.HasBlockingError);
            Assert.Contains(snapshot.Nodes, node => node.NodeKind == NodeKind.Type && node.DisplayName == "CustomerService");
            Assert.Contains(snapshot.Warnings, warning => warning.Contains("Semantic diagnostic", StringComparison.Ordinal));
            Assert.Contains(snapshot.Evidence, evidence => evidence.EvidenceKind == EvidenceKind.CompilerDiagnostic);
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Creates a temporary repository root for one semantic infrastructure test.
        /// </summary>
        /// <returns>The absolute repository root path.</returns>
        private string CreateRepositoryRoot()
        {
            // Temporary roots avoid absolute-path coupling and match the repository-relative evidence requirements.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-wp006-roslyn-infrastructure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repositoryRoot);
            _temporaryDirectories.Add(repositoryRoot);
            return repositoryRoot;
        }

        /// <summary>
        /// Creates a solution file that declares the fixture C# and Visual Basic projects.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that should contain the solution.</param>
        /// <returns>The absolute solution path.</returns>
        private static string CreateSolutionFile(string repositoryRoot)
        {
            // The lightweight semantic stage parser consumes standard Project lines from the solution file.
            string solutionPath = Path.Combine(repositoryRoot, "ArchonFixture.sln");
            File.WriteAllText(
                solutionPath,
                string.Join(
                    Environment.NewLine,
                    [
                        "Microsoft Visual Studio Solution File, Format Version 12.00",
                        "# Visual Studio Version 17",
                        "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Customer.Api\", \"src\\Customer.Api\\Customer.Api.csproj\", \"{11111111-1111-1111-1111-111111111111}\"",
                        "EndProject",
                        "Project(\"{F184B08F-C81C-45F6-A57F-5ABD9991F28F}\") = \"Ledger.Worker\", \"src\\Ledger.Worker\\Ledger.Worker.vbproj\", \"{22222222-2222-2222-2222-222222222222}\"",
                        "EndProject",
                        "Global",
                        "EndGlobal"
                    ]));
            return solutionPath;
        }

        /// <summary>
        /// Creates a C# project fixture with one source file.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that should contain the project.</param>
        /// <param name="includeMissingReference">Whether the source should include a deliberately missing reference.</param>
        private static void CreateCSharpProject(string repositoryRoot, bool includeMissingReference = false)
        {
            // Explicit compile includes make the lightweight project loader deterministic.
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Customer.Api");
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(
                Path.Combine(projectDirectory, "Customer.Api.csproj"),
                string.Join(
                    Environment.NewLine,
                    [
                        "<Project Sdk=\"Microsoft.NET.Sdk\">",
                        "  <PropertyGroup>",
                        "    <TargetFramework>net10.0</TargetFramework>",
                        "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
                        "  </PropertyGroup>",
                        "  <ItemGroup>",
                        "    <Compile Include=\"CustomerService.cs\" />",
                        "  </ItemGroup>",
                        "</Project>"
                    ]));
            File.WriteAllText(Path.Combine(projectDirectory, "CustomerService.cs"), CreateCSharpSource(includeMissingReference));
        }

        /// <summary>
        /// Creates a Visual Basic project fixture with one source file.
        /// </summary>
        /// <param name="repositoryRoot">The repository root that should contain the project.</param>
        private static void CreateVisualBasicProject(string repositoryRoot)
        {
            // The VB.NET fixture verifies mixed-language orchestration through the same semantic stage.
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Ledger.Worker");
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(
                Path.Combine(projectDirectory, "Ledger.Worker.vbproj"),
                string.Join(
                    Environment.NewLine,
                    [
                        "<Project Sdk=\"Microsoft.NET.Sdk\">",
                        "  <PropertyGroup>",
                        "    <TargetFramework>net10.0</TargetFramework>",
                        "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
                        "  </PropertyGroup>",
                        "  <ItemGroup>",
                        "    <Compile Include=\"LedgerService.vb\" />",
                        "  </ItemGroup>",
                        "</Project>"
                    ]));
            File.WriteAllText(
                Path.Combine(projectDirectory, "LedgerService.vb"),
                string.Join(
                    Environment.NewLine,
                    [
                        "Namespace Ledger.Worker",
                        "    Public Class LedgerService",
                        "        Private ReadOnly _name As String",
                        "",
                        "        Public Sub New()",
                        "            _name = \"Ledger\"",
                        "        End Sub",
                        "",
                        "        Public ReadOnly Property Name As String",
                        "            Get",
                        "                Return _name",
                        "            End Get",
                        "        End Property",
                        "    End Class",
                        "End Namespace"
                    ]));
        }

        /// <summary>
        /// Creates C# fixture source with optional degraded reference behavior.
        /// </summary>
        /// <param name="includeMissingReference">Whether to include a missing type reference.</param>
        /// <returns>The C# source text.</returns>
        private static string CreateCSharpSource(bool includeMissingReference)
        {
            // The missing reference is isolated to a field so the containing type remains extractable.
            string missingReferenceField = includeMissingReference ? "    private Missing.Dependency? _missing;" : "    private readonly string _name;";
            return string.Join(
                Environment.NewLine,
                [
                    "namespace Customer.Api;",
                    "",
                    "public sealed class CustomerService",
                    "{",
                    missingReferenceField,
                    "",
                    "    public CustomerService()",
                    "    {",
                    includeMissingReference ? "        _missing = null;" : "        _name = \"Ada\";",
                    "    }",
                    "",
                    includeMissingReference ? "    public string Name => \"Unknown\";" : "    public string Name => _name;",
                    "}"
                ]);
        }

        /// <summary>
        /// Creates normalized resolved input for the semantic stage.
        /// </summary>
        /// <param name="repositoryRoot">The accepted repository root.</param>
        /// <param name="solutionPath">The accepted solution path.</param>
        /// <returns>The resolved input supplied to pipeline execution.</returns>
        private static ResolvedExtractionInput CreateResolvedInput(string repositoryRoot, string solutionPath)
        {
            // The semantic stage consumes already validated application input, so the test supplies absolute paths directly.
            return new ResolvedExtractionInput(repositoryRoot, [solutionPath], "main", "abcdef", "test", new Dictionary<string, string>());
        }

        /// <summary>
        /// Creates an accepted extraction run associated with the fixture input.
        /// </summary>
        /// <param name="repositoryRoot">The accepted repository root.</param>
        /// <param name="solutionPath">The accepted solution path.</param>
        /// <returns>An extraction run with a deterministic identifier.</returns>
        private static ExtractionRun CreateRun(string repositoryRoot, string solutionPath)
        {
            // A fixed run id lets deterministic stable-key assertions compare separate accumulators.
            return new ExtractionRun(
                new ExtractionRunId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
                ExtractionRunStatus.Queued,
                new ExtractionRunRequestSummary(repositoryRoot, [solutionPath], "main", "abcdef", "test", []),
                new DateTimeOffset(2026, 5, 21, 8, 0, 0, TimeSpan.Zero),
                null,
                new ExtractionRunProgress("Queued", "Queued for semantic test execution.", 0, new DateTimeOffset(2026, 5, 21, 8, 0, 0, TimeSpan.Zero)),
                null,
                null,
                null,
                null);
        }
    }
}
