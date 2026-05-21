using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Archon.Roslyn.CSharp.Tests
{
    /// <summary>
    /// Verifies the minimal WP006 C# semantic declaration extraction slice.
    /// </summary>
    public sealed class CSharpSemanticDocumentExtractorTests
    {
        /// <summary>
        /// Confirms that a C# document containing namespace, type, constructor, method, property, and field declarations produces graph-ready facts and containment relationships.
        /// </summary>
        [Fact]
        public void ExtractProducesDeclarationFactsAndContainmentRelationships()
        {
            // The fixture deliberately includes every declaration shape required by WP006 Work Item 1 acceptance criteria.
            string source = """
                namespace Sample.App
                {
                    public sealed class Widget
                    {
                        private readonly string _name;

                        public Widget(string name)
                        {
                            _name = name;
                        }

                        public string Name { get; }

                        public void Run()
                        {
                        }
                    }
                }
                """;
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-csharp-fixture"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "Widget.cs");
            SemanticExtractionResult result = ExtractSource(source, repositoryRoot, documentPath, "src/Sample.App/Sample.App.csproj");

            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Namespace && declaration.SymbolIdentity.FullyQualifiedName == "Sample.App");
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Type && declaration.SymbolIdentity.FullyQualifiedName == "Sample.App.Widget");
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Method && declaration.SymbolIdentity.FullyQualifiedName.Contains("Widget.Widget(string)", StringComparison.Ordinal));
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Method && declaration.SymbolIdentity.FullyQualifiedName.Contains("Widget.Run()", StringComparison.Ordinal));
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Property && declaration.SymbolIdentity.FullyQualifiedName == "Sample.App.Widget.Name");
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Field && declaration.SymbolIdentity.FullyQualifiedName == "Sample.App.Widget._name");

            SemanticDeclarationFact typeFact = Assert.Single(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Type);
            SemanticDeclarationFact fieldFact = Assert.Single(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Field);
            SemanticDeclarationFact propertyFact = Assert.Single(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Property);
            Assert.Equal(typeFact.StableKey, fieldFact.ParentStableKey);
            Assert.Equal(typeFact.StableKey, propertyFact.ParentStableKey);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Contains && relationship.SourceStableKey == typeFact.StableKey && relationship.TargetStableKey == fieldFact.StableKey);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Contains && relationship.SourceStableKey == typeFact.StableKey && relationship.TargetStableKey == propertyFact.StableKey);
        }

        /// <summary>
        /// Confirms that extracted evidence contains repository-relative path, line span, symbol context, snippet preview, and snippet hash.
        /// </summary>
        [Fact]
        public void ExtractProducesSourceEvidenceForDeclarations()
        {
            // Evidence assertions focus on a method declaration because it has a compact source span and a containing type symbol.
            string source = """
                namespace Sample.App
                {
                    public sealed class Widget
                    {
                        public void Run()
                        {
                        }
                    }
                }
                """;
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-csharp-evidence"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "Widget.cs");
            SemanticExtractionResult result = ExtractSource(source, repositoryRoot, documentPath, "src/Sample.App/Sample.App.csproj");

            SemanticDeclarationFact methodFact = Assert.Single(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Method);

            Assert.Equal("src/Sample.App/Widget.cs", methodFact.Evidence.RepositoryRelativeFilePath);
            Assert.Equal("Run", methodFact.Evidence.SymbolName);
            Assert.Equal("Sample.App.Widget", methodFact.Evidence.ContainingSymbolName);
            Assert.True(methodFact.Evidence.StartLine > 0);
            Assert.True(methodFact.Evidence.EndLine >= methodFact.Evidence.StartLine);
            Assert.Contains("public void Run()", methodFact.Evidence.SnippetPreview, StringComparison.Ordinal);
            Assert.StartsWith("sha256:", methodFact.Evidence.SnippetHash, StringComparison.Ordinal);
        }

        /// <summary>
        /// Confirms that stable keys are deterministic across equivalent extraction runs.
        /// </summary>
        [Fact]
        public void ExtractProducesDeterministicStableKeys()
        {
            // Deterministic stable keys are the foundation for repeatable graph updates and snapshot comparisons.
            string source = """
                namespace Sample.App
                {
                    public sealed class Widget
                    {
                        public string Name { get; }
                    }
                }
                """;
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-csharp-determinism"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "Widget.cs");

            SemanticExtractionResult firstResult = ExtractSource(source, repositoryRoot, documentPath, "src/Sample.App/Sample.App.csproj");
            SemanticExtractionResult secondResult = ExtractSource(source, repositoryRoot, documentPath, "src/Sample.App/Sample.App.csproj");

            Assert.Equal(firstResult.Declarations.Select(declaration => declaration.StableKey), secondResult.Declarations.Select(declaration => declaration.StableKey));
            Assert.Equal(firstResult.Relationships.Select(relationship => relationship.StableKey), secondResult.Relationships.Select(relationship => relationship.StableKey));
        }

        /// <summary>
        /// Extracts semantic facts from an in-memory C# source document using a real Roslyn compilation and semantic model.
        /// </summary>
        /// <param name="source">The C# source code to parse and bind.</param>
        /// <param name="repositoryRoot">The repository root used for evidence path normalization.</param>
        /// <param name="documentPath">The document path assigned to the syntax tree.</param>
        /// <param name="projectContext">The logical project context used to scope stable keys.</param>
        /// <returns>The semantic extraction result produced by the C# extractor.</returns>
        private static SemanticExtractionResult ExtractSource(string source, string repositoryRoot, string documentPath, string projectContext)
        {
            // The helper mirrors infrastructure responsibilities: parse source, create a compilation, obtain a semantic model, and invoke the language extractor.
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: documentPath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Sample.App",
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            SemanticExtractionRequest request = new(repositoryRoot, projectContext, documentPath, syntaxTree, semanticModel);
            CSharpSemanticDocumentExtractor extractor = new();

            return extractor.Extract(request);
        }
    }
}
