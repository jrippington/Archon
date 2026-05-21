using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using System.Reflection;
using Xunit;

namespace Archon.Roslyn.VisualBasic.Tests
{
    /// <summary>
    /// Verifies the WP006 Visual Basic semantic declaration and relationship extraction slice.
    /// </summary>
    public sealed class VisualBasicSemanticDocumentExtractorTests
    {
        /// <summary>
        /// Confirms that a Visual Basic document containing core declaration forms produces graph-ready facts and containment relationships.
        /// </summary>
        [Fact]
        public void ExtractProducesDeclarationFactsAndContainmentRelationships()
        {
            // The fixture includes namespace, module, class, structure, interface, enum, delegate, constructor, method, property, default property, field, event, and constant shapes required by Work Item 3.
            string source = """
                Namespace Sample.App
                    Public Interface IService
                        Sub Execute()
                    End Interface

                    Public Module UtilityModule
                        Public Sub Help()
                        End Sub
                    End Module

                    Public Structure Coordinates
                        Public X As Integer
                    End Structure

                    Public Enum WidgetState
                        Ready
                    End Enum

                    Public Delegate Sub WidgetHandler(sender As Object)

                    Public Class Widget
                        Private ReadOnly _service As IService
                        Public Event Changed As WidgetHandler
                        Public Const DefaultName As String = "sample"

                        Public Sub New(service As IService)
                            _service = service
                        End Sub

                        Default Public ReadOnly Property Item(index As Integer) As String
                            Get
                                Return DefaultName
                            End Get
                        End Property

                        Public Property Name As String

                        Public Sub Run()
                        End Sub
                    End Class
                End Namespace
                """;
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-vb-declarations"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "Widget.vb");

            SemanticExtractionResult result = ExtractSource(source, repositoryRoot, documentPath, "src/Sample.App/Sample.App.vbproj");

            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
            Assert.Contains(result.Declarations, declaration => declaration.SourceLanguage == SourceLanguage.VisualBasic && declaration.DeclarationKind == SemanticDeclarationKind.Namespace && declaration.SymbolIdentity.FullyQualifiedName == "Sample.App");
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Type && declaration.SymbolIdentity.FullyQualifiedName == "Sample.App.UtilityModule");
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Type && declaration.SymbolIdentity.FullyQualifiedName == "Sample.App.Coordinates");
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Type && declaration.SymbolIdentity.FullyQualifiedName == "Sample.App.WidgetState");
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Type && declaration.SymbolIdentity.FullyQualifiedName == "Sample.App.WidgetHandler");
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Method && declaration.SymbolIdentity.DisplayName == "Widget");
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Method && declaration.SymbolIdentity.FullyQualifiedName.Contains("Run", StringComparison.Ordinal));
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Property && declaration.SymbolIdentity.DisplayName == "Item");
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Property && declaration.SymbolIdentity.DisplayName == "Name");
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Field && declaration.SymbolIdentity.DisplayName == "_service");
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Field && declaration.SymbolIdentity.DisplayName == "DefaultName");
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Field && declaration.SymbolIdentity.DisplayName.Contains("Changed", StringComparison.Ordinal));

            SemanticDeclarationFact widgetFact = Assert.Single(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Type && declaration.SymbolIdentity.FullyQualifiedName == "Sample.App.Widget");
            SemanticDeclarationFact runFact = Assert.Single(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Method && declaration.SymbolIdentity.FullyQualifiedName.Contains("Run", StringComparison.Ordinal));
            Assert.Equal(widgetFact.StableKey, runFact.ParentStableKey);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Contains && relationship.SourceStableKey == widgetFact.StableKey && relationship.TargetStableKey == runFact.StableKey);
        }

        /// <summary>
        /// Confirms that compiler-resolved Visual Basic symbols emit the shared relationship vocabulary with confidence, evidence, and endpoint identities.
        /// </summary>
        [Fact]
        public void ExtractProducesRelationshipAndDependencyFacts()
        {
            // The fixture mirrors the C# relationship coverage using Visual Basic syntax, including inheritance, implementation, constructor injection, shared calls, object creation, default property access, and extension methods.
            string source = """
                Imports System
                Imports System.Runtime.CompilerServices

                Namespace Sample.App
                    Public Interface IService
                        Sub Execute()
                    End Interface

                    Public MustInherit Class BaseWidget
                        Public Overridable Sub Template()
                        End Sub
                    End Class

                    Public Class Service
                        Implements IService

                        Public Sub Execute() Implements IService.Execute
                        End Sub
                    End Class

                    <Obsolete>
                    Public Class Consumer
                        Inherits BaseWidget
                        Implements IService

                        Private ReadOnly _service As IService

                        Public Sub New(service As IService)
                            _service = service
                        End Sub

                        Default Public ReadOnly Property Item(index As Integer) As String
                            Get
                                Return "value"
                            End Get
                        End Property

                        Public Overrides Sub Template()
                            MyBase.Template()
                        End Sub

                        Public Sub Execute() Implements IService.Execute
                            _service.Execute()
                            Dim text = Me.Item(0)
                            UtilityModule.Help()
                            Dim created = New Service()
                            created.Execute()
                            Me.ExtensionCall()
                        End Sub
                    End Class

                    Public Module UtilityModule
                        Public Sub Help()
                        End Sub

                        <Extension>
                        Public Sub ExtensionCall(consumer As Consumer)
                        End Sub
                    End Module
                End Namespace
                """;
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-vb-relationships"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "Consumer.vb");

            SemanticExtractionResult result = ExtractSource(source, repositoryRoot, documentPath, "src/Sample.App/Sample.App.vbproj");

            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Inherits && relationship.TargetSymbolIdentity?.FullyQualifiedName == "Sample.App.BaseWidget");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Implements && relationship.TargetSymbolIdentity?.FullyQualifiedName == "Sample.App.IService");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Injects && relationship.TargetSymbolIdentity?.FullyQualifiedName == "Sample.App.IService");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Calls && relationship.TargetSymbolIdentity?.FullyQualifiedName.Contains("Execute", StringComparison.Ordinal) == true);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Calls && relationship.TargetSymbolIdentity?.FullyQualifiedName.Contains("Help", StringComparison.Ordinal) == true);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Calls && relationship.TargetSymbolIdentity?.FullyQualifiedName.Contains("ExtensionCall", StringComparison.Ordinal) == true);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.TargetSymbolIdentity?.FullyQualifiedName == "Sample.App.Service");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.Metadata["dependencySource"] == "PropertyAccess");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.Metadata["dependencySource"] == "Attribute" && relationship.TargetSymbolIdentity?.FullyQualifiedName.Contains("System.ObsoleteAttribute", StringComparison.Ordinal) == true);
            Assert.All(
                result.Relationships.Where(relationship => relationship.RelationshipKind != SemanticRelationshipKind.Contains),
                relationship =>
                {
                    Assert.Contains(relationship.Confidence, new[] { SemanticFactConfidence.CompilerResolved, SemanticFactConfidence.MetadataOnly });
                    Assert.NotNull(relationship.SourceSymbolIdentity);
                    Assert.NotNull(relationship.TargetSymbolIdentity);
                    Assert.StartsWith("src/Sample.App/Consumer.vb", relationship.Evidence.RepositoryRelativeFilePath, StringComparison.Ordinal);
                });
        }

        /// <summary>
        /// Confirms that Visual Basic signature dependencies, generic constraints, attributes, and root namespace effects project into the shared model.
        /// </summary>
        [Fact]
        public void ExtractProducesSignatureGenericAttributeAndRootNamespaceFacts()
        {
            // Visual Basic projects commonly add a root namespace outside source syntax; the test passes that project context through parse options so the extractor can include it where Roslyn exposes the namespace symbol.
            string source = """
                Imports System

                <Assembly: CLSCompliant(True)>

                Namespace Features
                    Public Interface IRepository(Of TModel)
                    End Interface

                    Public Class Entity
                    End Class

                    Public Class EntityRepository
                        Implements IRepository(Of Entity)
                    End Class

                    <AttributeUsage(AttributeTargets.Class Or AttributeTargets.Method Or AttributeTargets.Parameter Or AttributeTargets.ReturnValue)>
                    Public Class MarkerAttribute
                        Inherits Attribute
                    End Class

                    <Marker>
                    Public Class GenericConsumer(Of TModel As Entity)
                        <Marker>
                        Public Function Create(Of TOther As Entity)(<Marker> repository As IRepository(Of TModel), other As TOther) As EntityRepository
                            Return New EntityRepository()
                        End Function
                    End Class
                End Namespace
                """;
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-vb-signatures"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "GenericConsumer.vb");

            SemanticExtractionResult result = ExtractSource(source, repositoryRoot, documentPath, "src/Sample.App/Sample.App.vbproj", rootNamespace: "SampleRoot");

            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Namespace && declaration.SymbolIdentity.FullyQualifiedName == "SampleRoot.Features");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.TargetSymbolIdentity?.FullyQualifiedName.Contains("Compliant", StringComparison.OrdinalIgnoreCase) == true);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.Metadata["dependencySource"] == "Attribute" && relationship.TargetSymbolIdentity?.FullyQualifiedName == "SampleRoot.Features.MarkerAttribute");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.Metadata["dependencySource"] == "ParameterType" && relationship.TargetSymbolIdentity?.FullyQualifiedName.Contains("IRepository", StringComparison.Ordinal) == true);
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.Metadata["dependencySource"] == "ReturnType" && relationship.TargetSymbolIdentity?.FullyQualifiedName == "SampleRoot.Features.EntityRepository");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn && relationship.Metadata["dependencySource"] == "GenericConstraint" && relationship.TargetSymbolIdentity?.FullyQualifiedName == "SampleRoot.Features.Entity");
            Assert.Contains(result.Relationships, relationship => relationship.RelationshipKind == SemanticRelationshipKind.Implements && relationship.TargetSymbolIdentity?.FullyQualifiedName.Contains("IRepository", StringComparison.Ordinal) == true);
        }

        /// <summary>
        /// Confirms that repeated Visual Basic relationship discoveries collapse into deterministic facts.
        /// </summary>
        [Fact]
        public void ExtractDeduplicatesRepeatedRelationshipFacts()
        {
            // Repeated calls to the same target should become one CALLS fact because the shared relationship key is endpoint-derived.
            string source = """
                Namespace Sample.App
                    Public Class Worker
                        Public Sub Caller()
                            Callee()
                            Callee()
                        End Sub

                        Public Sub Callee()
                        End Sub
                    End Class
                End Namespace
                """;
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-vb-deduplicate"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "Worker.vb");

            SemanticExtractionResult result = ExtractSource(source, repositoryRoot, documentPath, "src/Sample.App/Sample.App.vbproj");
            IReadOnlyList<SemanticRelationshipFact> calls = result.Relationships.Where(relationship => relationship.RelationshipKind == SemanticRelationshipKind.Calls).ToArray();

            Assert.Single(calls);
            Assert.Equal(calls.Select(relationship => relationship.StableKey).Distinct(StringComparer.Ordinal).Count(), calls.Count);
        }

        /// <summary>
        /// Confirms that Visual Basic extraction preserves resolvable facts while recording diagnostics and unknowns for degraded compilations.
        /// </summary>
        [Fact]
        public void ExtractCapturesDiagnosticsAndUnknownsForDegradedCompilation()
        {
            // The fixture combines missing references, late-bound object calls, ambiguous overload resolution, and reflection so degraded extraction remains queryable.
            string source = """
                Imports System

                Option Strict Off

                Namespace Sample.App
                    Public Class Worker
                        Public Sub Run(missing As MissingService, target As Object)
                            target.Execute()
                            MissingStatic.Execute()
                            Dim typeName As String = "Sample.App.Worker"
                            Type.GetType(typeName)
                        End Sub

                        Public Sub Ambiguous(value As Integer)
                        End Sub

                        Public Sub Ambiguous(value As String)
                        End Sub

                        Public Sub Caller()
                            Ambiguous(Nothing)
                        End Sub
                    End Class
                End Namespace
                """;
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-vb-degraded"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "Worker.vb");

            SemanticExtractionResult result = ExtractSource(source, repositoryRoot, documentPath, "src/Sample.App/Sample.App.vbproj");

            Assert.Empty(result.Errors);
            Assert.Contains(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Type && declaration.SymbolIdentity.FullyQualifiedName == "Sample.App.Worker");
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.DiagnosticId.StartsWith("BC", StringComparison.Ordinal) && diagnostic.Severity == SemanticDiagnosticSeverity.Error);
            Assert.Contains(result.Unknowns, unknown => unknown.Reason == SemanticUnknownReason.MissingReference || unknown.Reason == SemanticUnknownReason.UnresolvedSymbol);
            Assert.Contains(result.Unknowns, unknown => unknown.Reason == SemanticUnknownReason.LateBoundCall);
            Assert.Contains(result.Unknowns, unknown => unknown.Reason == SemanticUnknownReason.ReflectionTarget);
            Assert.Contains(result.Unknowns, unknown => unknown.Reason == SemanticUnknownReason.AmbiguousOverload || unknown.Reason == SemanticUnknownReason.UnsupportedSemanticForm);
            Assert.All(result.Unknowns, unknown => Assert.Equal(SemanticFactConfidence.Unresolved, unknown.Confidence));
        }

        /// <summary>
        /// Confirms that generated partial Visual Basic declarations merge into stable identities while preserving generated evidence contribution metadata.
        /// </summary>
        [Fact]
        public void ExtractMarksGeneratedPartialDeclarationsAndPreservesEvidenceContributions()
        {
            // Generated Visual Basic partial files should mark facts as generated and preserve multiple declaration evidence spans without duplicate type nodes.
            string source = """
                ' <auto-generated />
                Namespace Sample.App
                    Partial Public Class GeneratedWidget
                        Public Sub GeneratedPart()
                        End Sub
                    End Class

                    Partial Public Class GeneratedWidget
                        Public Sub ManualPart()
                        End Sub
                    End Class
                End Namespace
                """;
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-vb-generated"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "GeneratedWidget.g.vb");

            SemanticExtractionResult result = ExtractSource(source, repositoryRoot, documentPath, "src/Sample.App/Sample.App.vbproj");

            SemanticDeclarationFact typeFact = Assert.Single(result.Declarations, declaration => declaration.DeclarationKind == SemanticDeclarationKind.Type && declaration.SymbolIdentity.FullyQualifiedName == "Sample.App.GeneratedWidget");
            Assert.Equal(SemanticFactConfidence.Generated, typeFact.Confidence);
            Assert.Equal("true", typeFact.Metadata["generated"]);
            Assert.True(result.EvidenceContributions.Count(contribution => contribution.FactStableKey == typeFact.StableKey) >= 2);
            Assert.All(result.EvidenceContributions.Where(contribution => contribution.FactStableKey == typeFact.StableKey), contribution => Assert.True(contribution.Generated));
        }

        /// <summary>
        /// Confirms that Visual Basic dependencies on metadata-only framework symbols are classified without inventing repository source declaration nodes.
        /// </summary>
        [Fact]
        public void ExtractClassifiesMetadataSymbolDependencies()
        {
            // DateTime and Console are framework metadata symbols, so relationships should target symbol references with metadata-only confidence.
            string source = """
                Imports System

                Namespace Sample.App
                    Public Class Worker
                        Public Property Created As DateTime

                        Public Sub Run()
                            Console.WriteLine(Created.ToString())
                        End Sub
                    End Class
                End Namespace
                """;
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-vb-metadata"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "Worker.vb");

            SemanticExtractionResult result = ExtractSource(source, repositoryRoot, documentPath, "src/Sample.App/Sample.App.vbproj");

            Assert.Contains(
                result.Relationships,
                relationship => relationship.RelationshipKind == SemanticRelationshipKind.DependsOn
                    && relationship.Confidence == SemanticFactConfidence.MetadataOnly
                    && relationship.TargetStableKey.StartsWith("semantic-symbol-reference://", StringComparison.Ordinal)
                    && relationship.Metadata["targetOrigin"] == "Metadata"
                    && relationship.TargetSymbolIdentity?.FullyQualifiedName.Contains("Date", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(result.Declarations, declaration => declaration.SymbolIdentity.FullyQualifiedName.Contains("System.DateTime", StringComparison.Ordinal));
        }

        /// <summary>
        /// Extracts semantic facts from an in-memory Visual Basic source document using a real Roslyn compilation and semantic model.
        /// </summary>
        /// <param name="source">The Visual Basic source code to parse and bind.</param>
        /// <param name="repositoryRoot">The repository root used for evidence path normalization.</param>
        /// <param name="documentPath">The document path assigned to the syntax tree.</param>
        /// <param name="projectContext">The logical project context used to scope stable keys.</param>
        /// <param name="rootNamespace">The optional Visual Basic root namespace to apply through parse options.</param>
        /// <returns>The semantic extraction result produced by the Visual Basic extractor.</returns>
        private static SemanticExtractionResult ExtractSource(string source, string repositoryRoot, string documentPath, string projectContext, string? rootNamespace = null)
        {
            // The helper mirrors infrastructure responsibilities: parse VB source, create a compilation, obtain a semantic model, and invoke the language extractor.
            SyntaxTree syntaxTree = VisualBasicSyntaxTree.ParseText(source, VisualBasicParseOptions.Default, path: documentPath);
            string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? throw new InvalidOperationException("Runtime metadata directory could not be located for Roslyn test compilation.");
            VisualBasicCompilation compilation = VisualBasicCompilation.Create(
                "Sample.App",
                [syntaxTree],
                [
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Assembly).Assembly.Location),
                    MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll"))
                ],
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, rootNamespace: rootNamespace ?? string.Empty));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            SemanticExtractionRequest request = new(repositoryRoot, projectContext, documentPath, syntaxTree, semanticModel);
            VisualBasicSemanticDocumentExtractor extractor = new();

            return extractor.Extract(request);
        }
    }
}
