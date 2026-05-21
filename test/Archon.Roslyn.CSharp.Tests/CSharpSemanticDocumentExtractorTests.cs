using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
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
        /// Confirms that compiler-resolved C# symbol usage emits method call, object creation, property access, dependency, inheritance, implementation, and injection relationships.
        /// </summary>
        [Fact]
        public void ExtractProducesResolvedRelationshipAndDependencyFacts()
        {
            // The fixture keeps all source symbols in one document so assertions can compare source and target identities without requiring cross-project infrastructure.
            string source = """
                using System;

                namespace Sample.App
                {
                    public interface IService
                    {
                        void Execute();
                    }

                    public abstract class BaseWidget
                    {
                        public virtual void Template()
                        {
                        }
                    }

                    public sealed class Service : IService
                    {
                        public void Execute()
                        {
                        }
                    }

                    [Obsolete]
                    public sealed class Consumer : BaseWidget, IService
                    {
                        private readonly IService _service;

                        public Consumer(IService service)
                        {
                            _service = service;
                        }

                        public string Name { get; } = "sample";

                        public override void Template()
                        {
                            base.Template();
                        }

                        public void Execute()
                        {
                            _service.Execute();
                            Name.ToString();
                            StaticHelper.Help();
                            new Service().Execute();
                            this.ExtensionCall();
                        }
                    }

                    public static class StaticHelper
                    {
                        public static void Help()
                        {
                        }

                        public static void ExtensionCall(this Consumer consumer)
                        {
                        }
                    }
                }
                """;
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-csharp-relationships"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "Consumer.cs");

            SemanticExtractionResult result = ExtractSource(source, repositoryRoot, documentPath, "src/Sample.App/Sample.App.csproj");

            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Inherits && relationship.TargetSymbolIdentity?.FullyQualifiedName == "Sample.App.BaseWidget");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Implements && relationship.TargetSymbolIdentity?.FullyQualifiedName == "Sample.App.IService");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Injects && relationship.TargetSymbolIdentity?.FullyQualifiedName == "Sample.App.IService");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Calls && relationship.TargetSymbolIdentity?.FullyQualifiedName.Contains("IService.Execute()", StringComparison.Ordinal) == true);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Calls && relationship.TargetSymbolIdentity?.FullyQualifiedName.Contains("StaticHelper.Help()", StringComparison.Ordinal) == true);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Calls && relationship.TargetSymbolIdentity?.FullyQualifiedName.Contains("StaticHelper.ExtensionCall", StringComparison.Ordinal) == true);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.TargetSymbolIdentity?.FullyQualifiedName == "Sample.App.Service");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.Metadata["dependencySource"] == "PropertyAccess");
            Assert.All(
                result.Relationships.Where(relationship => relationship.RelationshipKind != SemanticRelationshipKind.Contains),
                relationship =>
                {
                    Assert.Equal(SemanticFactConfidence.CompilerResolved, relationship.Confidence);
                    Assert.NotNull(relationship.SourceSymbolIdentity);
                    Assert.NotNull(relationship.TargetSymbolIdentity);
                    Assert.StartsWith("src/Sample.App/Consumer.cs", relationship.Evidence.RepositoryRelativeFilePath, StringComparison.Ordinal);
                });
        }

        /// <summary>
        /// Confirms that attributes, parameters, return types, generic type parameters, and generic constraints produce graph-visible dependency metadata.
        /// </summary>
        [Fact]
        public void ExtractProducesAttributeSignatureAndGenericDependencyFacts()
        {
            // The fixture exercises signature-level dependencies that do not necessarily appear inside executable method bodies.
            string source = """
                using System;

                [assembly: CLSCompliant(true)]

                namespace Sample.App
                {
                    public interface IRepository<TModel>
                    {
                    }

                    public sealed class Entity
                    {
                    }

                    public sealed class EntityRepository : IRepository<Entity>
                    {
                    }


                    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
                    public sealed class MarkerAttribute : Attribute
                    {
                    }

                    [Marker]
                    public sealed class GenericConsumer<TModel>
                        where TModel : Entity
                    {
                        [return: Marker]
                        [Marker]
                        public EntityRepository Create<[Marker] TOther>([Marker] IRepository<TModel> repository, TOther other)
                            where TOther : Entity
                        {
                            return new EntityRepository();
                        }
                    }
                }
                """;
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-csharp-signatures"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "GenericConsumer.cs");

            SemanticExtractionResult result = ExtractSource(source, repositoryRoot, documentPath, "src/Sample.App/Sample.App.csproj");

            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.Metadata["dependencySource"] == "AssemblyAttribute" && relationship.TargetSymbolIdentity?.FullyQualifiedName.Contains("System.CLSCompliantAttribute", StringComparison.Ordinal) == true);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.Metadata["dependencySource"] == "Attribute" && relationship.TargetSymbolIdentity?.FullyQualifiedName == "Sample.App.MarkerAttribute");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.Metadata["dependencySource"] == "ParameterType" && relationship.TargetSymbolIdentity?.FullyQualifiedName.Contains("IRepository", StringComparison.Ordinal) == true);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.Metadata["dependencySource"] == "ReturnType" && relationship.TargetSymbolIdentity?.FullyQualifiedName == "Sample.App.EntityRepository");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.Metadata["dependencySource"] == "GenericConstraint" && relationship.TargetSymbolIdentity?.FullyQualifiedName == "Sample.App.Entity");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Implements && relationship.TargetSymbolIdentity?.FullyQualifiedName.Contains("IRepository", StringComparison.Ordinal) == true);
        }

        /// <summary>
        /// Confirms that repeated syntax discoveries collapse into deterministic relationship facts rather than duplicated graph edges.
        /// </summary>
        [Fact]
        public void ExtractDeduplicatesRepeatedRelationshipFacts()
        {
            // Two identical calls in one method should produce one CALLS fact because relationship identity is endpoint-derived.
            string source = """
                namespace Sample.App
                {
                    public sealed class Worker
                    {
                        public void Caller()
                        {
                            Callee();
                            Callee();
                        }

                        public void Callee()
                        {
                        }
                    }
                }
                """;
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-csharp-deduplicate"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "Worker.cs");

            SemanticExtractionResult result = ExtractSource(source, repositoryRoot, documentPath, "src/Sample.App/Sample.App.csproj");

            IReadOnlyList<SemanticRelationshipFact> calls = result.Relationships.Where(relationship => relationship.RelationshipKind == SemanticRelationshipKind.Calls).ToArray();

            Assert.Single(calls);
            Assert.Equal(calls.Select(relationship => relationship.StableKey).Distinct(StringComparer.Ordinal).Count(), calls.Count);
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
            string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? throw new InvalidOperationException("Runtime metadata directory could not be located for Roslyn test compilation.");
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Sample.App",
                [syntaxTree],
                [
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Assembly).Assembly.Location),
                    MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll"))
                ],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            SemanticExtractionRequest request = new(repositoryRoot, projectContext, documentPath, syntaxTree, semanticModel);
            CSharpSemanticDocumentExtractor extractor = new();

            return extractor.Extract(request);
        }
    }
}
