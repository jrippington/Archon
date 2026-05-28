using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.DependencyInjection;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Archon.Extractors.Tests.DependencyInjection
{
    /// <summary>
    /// Verifies that the minimal Microsoft dependency-injection extractor slice turns direct generic service registrations into graph facts.
    /// </summary>
    public sealed class DirectMicrosoftDependencyInjectionExtractorTests
    {
        /// <summary>
        /// Confirms direct singleton, scoped, and transient registrations emit implementation-to-service relationships with evidence and lifetime metadata.
        /// </summary>
        [Fact]
        public void ExtractAddsDirectRegistrationRelationshipsWithEvidenceAndMetadata()
        {
            // The fixture uses direct Microsoft DI extension calls so the test describes Work Item 1 without wrapper or factory behavior.
            DependencyInjectionExtractionResult result = ExtractFixture();

            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);

            IReadOnlyList<ArchitectureEdge> registrationEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.RegisteredAsService)
                .OrderBy(edge => edge.Metadata.ToCanonicalJson(), StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(3, registrationEdges.Count);
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.PrimaryService" && edge.TargetNodeStableKey.Value == "type://Sample.App.IPrimaryService" && edge.Metadata.ToCanonicalJson().Contains("\"lifetime\":\"Singleton\"", StringComparison.Ordinal));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.ScopedWorker" && edge.TargetNodeStableKey.Value == "type://Sample.App.IScopedWorker" && edge.Metadata.ToCanonicalJson().Contains("\"lifetime\":\"Scoped\"", StringComparison.Ordinal));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.TransientWorker" && edge.TargetNodeStableKey.Value == "type://Sample.App.ITransientWorker" && edge.Metadata.ToCanonicalJson().Contains("\"lifetime\":\"Transient\"", StringComparison.Ordinal));

            Assert.All(
                registrationEdges,
                edge =>
                {
                    // Direct compiler-resolved registration calls are known facts with source-backed evidence.
                    Assert.True(edge.IsDirect);
                    Assert.Equal(KnowledgeKind.Fact, edge.KnowledgeKind);
                    Assert.Equal(Confidence.Certain, edge.Confidence);
                    Assert.Equal(UnknownState.Known, edge.UnknownState);
                    Assert.NotNull(edge.PrimaryEvidenceStableKey);
                    Assert.Contains("\"registrationMethod\":", edge.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                    Assert.Contains("\"registrationSource\":\"Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions\"", edge.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                });

            Assert.Equal(6, result.Snapshot.Nodes.Count(node => node.NodeKind == NodeKind.Type));
            Assert.Equal(3, result.Snapshot.Evidence.Count);
            Assert.All(
                result.Snapshot.Evidence,
                evidence =>
                {
                    // Evidence must be repository-relative and detailed enough for later explanations.
                    Assert.Equal(EvidenceKind.SourceCode, evidence.EvidenceKind);
                    Assert.Equal("src/Sample.App/Composition.cs", evidence.FilePath.Value);
                    Assert.True(evidence.StartLine > 0);
                    Assert.True(evidence.EndLine >= evidence.StartLine);
                    Assert.Contains("services.Add", evidence.SnippetPreview, StringComparison.Ordinal);
                    Assert.StartsWith("sha256:", evidence.SnippetHash, StringComparison.Ordinal);
                    Assert.NotNull(evidence.SymbolName);
                    Assert.Equal("Register", evidence.ContainingSymbol);
                });
        }

        /// <summary>
        /// Confirms repeated extraction of the same source emits deterministic keys and relies on the accumulator to remove duplicate graph facts.
        /// </summary>
        [Fact]
        public void ExtractProducesDeterministicKeysAndNoDuplicateRelationships()
        {
            // Deterministic keys prove that the slice can be re-run safely for snapshot updates and duplicate registration passes.
            DependencyInjectionExtractionResult firstResult = ExtractFixture();
            DependencyInjectionExtractionResult secondResult = ExtractFixture();

            Assert.Equal(
                firstResult.Snapshot.Edges.Select(edge => edge.StableKey.Value).Order(StringComparer.Ordinal),
                secondResult.Snapshot.Edges.Select(edge => edge.StableKey.Value).Order(StringComparer.Ordinal));
            Assert.Equal(
                firstResult.Snapshot.Nodes.Select(node => node.StableKey.Value).Order(StringComparer.Ordinal),
                secondResult.Snapshot.Nodes.Select(node => node.StableKey.Value).Order(StringComparer.Ordinal));
            Assert.Equal(
                firstResult.Snapshot.Evidence.Select(evidence => evidence.StableKey.Value).Order(StringComparer.Ordinal),
                secondResult.Snapshot.Evidence.Select(evidence => evidence.StableKey.Value).Order(StringComparer.Ordinal));
            Assert.Equal(firstResult.Snapshot.Edges.Count, firstResult.Snapshot.Edges.Select(edge => edge.StableKey.Value).Distinct(StringComparer.Ordinal).Count());
        }

        /// <summary>
        /// Builds the Roslyn semantic request used by the direct-registration tests and invokes the production extractor.
        /// </summary>
        /// <returns>The dependency-injection extraction result for the shared fixture.</returns>
        private static DependencyInjectionExtractionResult ExtractFixture()
        {
            // The source includes local IServiceCollection extension stubs so Roslyn can bind calls without external package restore.
            string source = """
                namespace Microsoft.Extensions.DependencyInjection
                {
                    public interface IServiceCollection
                    {
                    }

                    public static class ServiceCollectionServiceExtensions
                    {
                        public static IServiceCollection AddSingleton<TService, TImplementation>(this IServiceCollection services)
                        {
                            return services;
                        }

                        public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services)
                        {
                            return services;
                        }

                        public static IServiceCollection AddTransient<TService, TImplementation>(this IServiceCollection services)
                        {
                            return services;
                        }
                    }
                }

                namespace Sample.App
                {
                    using Microsoft.Extensions.DependencyInjection;

                    public interface IPrimaryService
                    {
                    }

                    public sealed class PrimaryService : IPrimaryService
                    {
                    }

                    public interface IScopedWorker
                    {
                    }

                    public sealed class ScopedWorker : IScopedWorker
                    {
                    }

                    public interface ITransientWorker
                    {
                    }

                    public sealed class TransientWorker : ITransientWorker
                    {
                    }

                    public static class Composition
                    {
                        public static void Register(IServiceCollection services)
                        {
                            services.AddSingleton<IPrimaryService, PrimaryService>();
                            services.AddScoped<IScopedWorker, ScopedWorker>();
                            services.AddTransient<ITransientWorker, TransientWorker>();
                        }
                    }
                }
                """;

            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-di-fixture"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "Composition.cs");
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: documentPath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Sample.App",
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            SemanticExtractionRequest request = new(repositoryRoot, "src/Sample.App/Sample.App.csproj", documentPath, syntaxTree, semanticModel);
            DirectMicrosoftDependencyInjectionExtractor extractor = new();

            return extractor.Extract(new DependencyInjectionExtractionRequest(StableKeyGenerator.ForRepository("Sample.App"), request));
        }
    }
}
