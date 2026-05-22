using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.AspNet.Runtime;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using Xunit;

namespace Archon.Extractors.AspNet.Tests
{
    /// <summary>
    /// Verifies the console entry-point runtime extractor contributes graph-ready facts for C# and VB.NET entry-point source shapes.
    /// </summary>
    public sealed class ConsoleEntryPointRuntimeExtractorTests
    {
        /// <summary>
        /// Verifies an explicit C# <c>static Main</c> method produces project, type, method, relationship, and source evidence facts.
        /// </summary>
        [Fact]
        public void Extract_WhenCSharpStaticMainExists_ShouldContributeMethodTypeProjectAndEvidenceFacts()
        {
            // The fixture is a target repository document, not Archon host code, so the extractor should classify it as a console entry point.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-wp008-console-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.Console"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "Customer.Console", "Program.cs");
                File.WriteAllText(documentPath, CreateCSharpMainSource());
                SemanticExtractionRequest semanticRequest = CreateCSharpSemanticRequest(repositoryRoot, "src/Customer.Console/Customer.Console.csproj", documentPath);
                ConsoleEntryPointRuntimeExtractor extractor = new();

                ConsoleEntryPointExtractionResult result = extractor.Extract(new ConsoleEntryPointExtractionRequest(new StableKey("snapshot://console-test"), [semanticRequest]), CancellationToken.None);

                ArchitectureNode methodNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method);
                Assert.Equal("Main", methodNode.DisplayName);
                Assert.Equal("project://src/Customer.Console/Customer.Console.csproj", methodNode.ProjectStableKey?.Value);
                Assert.Contains("\"runtimeKind\":\"ConsoleEntryPoint\"", methodNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"runtimeClassification\":\"ConsoleApplication\"", methodNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"detectionMode\":\"MainMethodSymbol\"", methodNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"handlerSymbol\":", methodNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.False(methodNode.UnknownState.HasUnknownData);

                ArchitectureNode typeNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Type);
                Assert.Equal("Program", typeNode.DisplayName);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Contains && edge.SourceNodeStableKey.Value == "project://src/Customer.Console/Customer.Console.csproj" && edge.TargetNodeStableKey == typeNode.StableKey);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Contains && edge.SourceNodeStableKey == typeNode.StableKey && edge.TargetNodeStableKey == methodNode.StableKey);

                EvidenceRecord evidence = Assert.Single(result.Snapshot.Evidence);
                Assert.Equal(EvidenceKind.SourceCode, evidence.EvidenceKind);
                Assert.Equal("src/Customer.Console/Program.cs", evidence.FilePath.Value);
                Assert.Equal("Main", evidence.SymbolName);
                Assert.NotNull(evidence.SnippetHash);
                Assert.Contains("static int Main", evidence.SnippetPreview, StringComparison.Ordinal);
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated test runs isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies a C# top-level statement document is represented as an implicit entry-point method scoped by repository-relative file path.
        /// </summary>
        [Fact]
        public void Extract_WhenCSharpTopLevelStatementsExist_ShouldContributeTopLevelEntryPointFact()
        {
            // Top-level statements compile to an implicit Main method, so the graph fact must use file-path identity rather than a generated compiler name.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-wp008-console-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "TopLevel.Console"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "TopLevel.Console", "Program.cs");
                File.WriteAllText(documentPath, CreateTopLevelSource());
                SemanticExtractionRequest semanticRequest = CreateCSharpSemanticRequest(repositoryRoot, "src/TopLevel.Console/TopLevel.Console.csproj", documentPath);
                ConsoleEntryPointRuntimeExtractor extractor = new();

                ConsoleEntryPointExtractionResult result = extractor.Extract(new ConsoleEntryPointExtractionRequest(new StableKey("snapshot://console-test"), [semanticRequest]), CancellationToken.None);

                ArchitectureNode methodNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method);
                Assert.Equal("<top-level statements>", methodNode.DisplayName);
                Assert.Contains("\"detectionMode\":\"CSharpTopLevelStatements\"", methodNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"topLevelStatementFilePath\":\"src/TopLevel.Console/Program.cs\"", methodNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.DoesNotContain(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Type);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Contains && edge.SourceNodeStableKey.Value == "project://src/TopLevel.Console/TopLevel.Console.csproj" && edge.TargetNodeStableKey == methodNode.StableKey);
                Assert.Single(result.Snapshot.Evidence, evidence => evidence.SymbolName == "<top-level statements>");
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated test runs isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies a VB.NET <c>Sub Main</c> method is detected as a console entry point with VB.NET source evidence.
        /// </summary>
        [Fact]
        public void Extract_WhenVisualBasicSubMainExists_ShouldContributeVisualBasicEntryPointFact()
        {
            // The VB fixture proves Work Item 4 uses the language-neutral semantic request contract rather than a C#-only runtime path.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-wp008-console-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.VbConsole"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "Customer.VbConsole", "Program.vb");
                File.WriteAllText(documentPath, CreateVisualBasicMainSource());
                SemanticExtractionRequest semanticRequest = CreateVisualBasicSemanticRequest(repositoryRoot, "src/Customer.VbConsole/Customer.VbConsole.vbproj", documentPath);
                ConsoleEntryPointRuntimeExtractor extractor = new();

                ConsoleEntryPointExtractionResult result = extractor.Extract(new ConsoleEntryPointExtractionRequest(new StableKey("snapshot://console-test"), [semanticRequest]), CancellationToken.None);

                ArchitectureNode methodNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method);
                Assert.Equal("Main", methodNode.DisplayName);
                Assert.Equal("VB.NET", methodNode.Language);
                Assert.Contains("\"detectionMode\":\"MainMethodSymbol\"", methodNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"containingType\":", methodNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Type);
                EvidenceRecord evidence = Assert.Single(result.Snapshot.Evidence);
                Assert.Equal("src/Customer.VbConsole/Program.vb", evidence.FilePath.Value);
                Assert.Contains("Sub Main", evidence.SnippetPreview, StringComparison.Ordinal);
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated test runs isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies multiple candidate entry points in one project are emitted with explicit ambiguity unknown state instead of guessed winner selection.
        /// </summary>
        [Fact]
        public void Extract_WhenProjectContainsAmbiguousEntryPoints_ShouldMarkCandidatesUnknown()
        {
            // Ambiguity is represented on the facts themselves so future consumers can review both candidates and the uncertainty reason.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-wp008-console-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Ambiguous.Console"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "Ambiguous.Console", "Program.cs");
                File.WriteAllText(documentPath, CreateAmbiguousCSharpMainSource());
                SemanticExtractionRequest semanticRequest = CreateCSharpSemanticRequest(repositoryRoot, "src/Ambiguous.Console/Ambiguous.Console.csproj", documentPath);
                ConsoleEntryPointRuntimeExtractor extractor = new();

                ConsoleEntryPointExtractionResult result = extractor.Extract(new ConsoleEntryPointExtractionRequest(new StableKey("snapshot://console-test"), [semanticRequest]), CancellationToken.None);

                ArchitectureNode[] methodNodes = result.Snapshot.Nodes.Where(node => node.NodeKind == NodeKind.Method).ToArray();
                Assert.Equal(2, methodNodes.Length);
                Assert.All(methodNodes, node =>
                {
                    Assert.True(node.UnknownState.HasUnknownData);
                    Assert.Equal("Multiple console entry-point candidates were detected for the project.", node.UnknownState.UnknownReason);
                    Assert.Contains("\"confidenceReason\":\"Multiple console entry-point candidates were detected for the project.\"", node.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                });
                Assert.Equal(2, result.Snapshot.Evidence.Count);
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated test runs isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Creates a semantic extraction request for one C# source document.
        /// </summary>
        /// <param name="repositoryRoot">The absolute repository root that scopes repository-relative evidence paths.</param>
        /// <param name="projectContext">The repository-relative project path used to scope project and method stable keys.</param>
        /// <param name="documentPath">The absolute source document path to parse.</param>
        /// <returns>A semantic extraction request with a C# syntax tree and semantic model.</returns>
        private static SemanticExtractionRequest CreateCSharpSemanticRequest(string repositoryRoot, string projectContext, string documentPath)
        {
            // The lightweight compilation is enough for source-declared Main method binding and keeps tests independent of target project builds.
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(documentPath), path: documentPath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(projectContext),
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.ConsoleApplication));
            return new SemanticExtractionRequest(repositoryRoot, projectContext, documentPath, syntaxTree, compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true));
        }

        /// <summary>
        /// Creates a semantic extraction request for one VB.NET source document.
        /// </summary>
        /// <param name="repositoryRoot">The absolute repository root that scopes repository-relative evidence paths.</param>
        /// <param name="projectContext">The repository-relative project path used to scope project and method stable keys.</param>
        /// <param name="documentPath">The absolute source document path to parse.</param>
        /// <returns>A semantic extraction request with a VB.NET syntax tree and semantic model.</returns>
        private static SemanticExtractionRequest CreateVisualBasicSemanticRequest(string repositoryRoot, string projectContext, string documentPath)
        {
            // The lightweight compilation is enough for module Main binding and keeps tests independent of target project builds.
            SyntaxTree syntaxTree = VisualBasicSyntaxTree.ParseText(File.ReadAllText(documentPath), path: documentPath);
            VisualBasicCompilation compilation = VisualBasicCompilation.Create(
                Path.GetFileNameWithoutExtension(projectContext),
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)],
                new VisualBasicCompilationOptions(OutputKind.ConsoleApplication));
            return new SemanticExtractionRequest(repositoryRoot, projectContext, documentPath, syntaxTree, compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true));
        }

        /// <summary>
        /// Creates fixture source containing one explicit C# Main method.
        /// </summary>
        /// <returns>The C# source text for the explicit Main fixture.</returns>
        private static string CreateCSharpMainSource()
        {
            // The fixture mirrors the common class-based console Program shape.
            return string.Join(
                Environment.NewLine,
                "namespace Customer.Console;",
                "public static class Program",
                "{",
                "    public static int Main(string[] args)",
                "    {",
                "        System.Console.WriteLine(args.Length);",
                "        return 0;",
                "    }",
                "}");
        }

        /// <summary>
        /// Creates fixture source containing C# top-level statements.
        /// </summary>
        /// <returns>The C# source text for the top-level statement fixture.</returns>
        private static string CreateTopLevelSource()
        {
            // The fixture uses a direct statement so the extractor can detect the implicit entry point without an explicit Program type.
            return string.Join(
                Environment.NewLine,
                "System.Console.WriteLine(\"hello\");",
                "return;");
        }

        /// <summary>
        /// Creates fixture source containing one VB.NET Sub Main method.
        /// </summary>
        /// <returns>The VB.NET source text for the explicit Main fixture.</returns>
        private static string CreateVisualBasicMainSource()
        {
            // The module shape is the common VB.NET console entry-point pattern.
            return string.Join(
                Environment.NewLine,
                "Module Program",
                "    Sub Main(args As String())",
                "        System.Console.WriteLine(args.Length)",
                "    End Sub",
                "End Module");
        }

        /// <summary>
        /// Creates fixture source containing two C# Main candidates in one project.
        /// </summary>
        /// <returns>The C# source text for the ambiguous entry-point fixture.</returns>
        private static string CreateAmbiguousCSharpMainSource()
        {
            // Two static Main methods in one project should produce explicit unknown state rather than an invented winning entry point.
            return string.Join(
                Environment.NewLine,
                "public static class Program",
                "{",
                "    public static void Main()",
                "    {",
                "    }",
                "}",
                "public static class AlternateProgram",
                "{",
                "    public static void Main(string[] args)",
                "    {",
                "    }",
                "}");
        }
    }
}
