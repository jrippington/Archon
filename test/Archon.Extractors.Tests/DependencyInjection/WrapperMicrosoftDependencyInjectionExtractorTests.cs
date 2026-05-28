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
    /// Verifies the wrapper registration and constructor-correlation slice for Microsoft dependency-injection extraction.
    /// </summary>
    public sealed class WrapperMicrosoftDependencyInjectionExtractorTests
    {
        /// <summary>
        /// Confirms wrapper methods accepting IServiceCollection are traversed and preserve wrapper-chain metadata on inner registrations.
        /// </summary>
        [Fact]
        public void ExtractTraversesWrapperMethodsAndPreservesInvocationChainMetadata()
        {
            // The fixture routes startup composition through nested wrappers so registration facts must be found from method bodies rather than the startup call text alone.
            DependencyInjectionExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureEdge> registrationEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.RegisteredAsService)
                .ToArray();

            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.PrimaryService" && edge.TargetNodeStableKey.Value == "type://Sample.App.IPrimaryService" && ContainsMetadata(edge, "\"registrationFamily\":\"Wrapper\"") && ContainsMetadata(edge, "\"wrapperDepth\":1") && ContainsMetadata(edge, "\"invocationChain\":\"AddPrimary\""));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.SecondaryService" && edge.TargetNodeStableKey.Value == "type://Sample.App.ISecondaryService" && ContainsMetadata(edge, "\"wrapperDepth\":1") && ContainsMetadata(edge, "\"invocationChain\":\"AddSecondary\""));
            Assert.Contains(result.Snapshot.Evidence, evidence => evidence.SnippetPreview?.Contains("services.AddPrimary()", StringComparison.Ordinal) == true && ContainsEvidenceMetadata(evidence, "\"evidenceRole\":\"WrapperInvocation\""));
            Assert.Contains(result.Snapshot.Evidence, evidence => evidence.SnippetPreview?.Contains("services.AddSingleton<IPrimaryService, PrimaryService>()", StringComparison.Ordinal) == true && ContainsEvidenceMetadata(evidence, "\"evidenceRole\":\"RegistrationInvocation\""));
        }

        /// <summary>
        /// Confirms wrapper traversal emits diagnostics for cycle, missing source, dynamic invocation, and recursion-depth safeguards without inventing registrations.
        /// </summary>
        [Fact]
        public void ExtractEmitsWrapperSafeguardWarningsWithoutInventingRegistrations()
        {
            // Safeguards protect real projects from recursive composition helpers and invocations whose method bodies cannot be inspected deterministically.
            DependencyInjectionExtractionResult result = ExtractFixture();
            IReadOnlyList<string> warningPayloads = result.Warnings.ToArray();

            Assert.Contains(warningPayloads, message => message.Contains("Wrapper cycle detected", StringComparison.Ordinal));
            Assert.Contains(warningPayloads, message => message.Contains("Wrapper recursion depth limit", StringComparison.Ordinal));
            Assert.Contains(warningPayloads, message => message.Contains("Wrapper source unavailable", StringComparison.Ordinal));
            Assert.Contains(warningPayloads, message => message.Contains("Unsupported dynamic service-registration invocation", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Snapshot.Edges, edge => edge.SourceNodeStableKey.Value.Contains("ExternalRegistration", StringComparison.Ordinal));
        }

        /// <summary>
        /// Confirms registered implementation types are correlated with constructor dependencies through deterministic INJECTS and DEPENDS_ON relationships.
        /// </summary>
        [Fact]
        public void ExtractCorrelatesRegisteredImplementationsWithConstructorDependenciesAndDeduplicatesEdges()
        {
            // Constructor correlation connects DI registrations to type-level dependencies without requiring the API orchestration path to be active in this fixture.
            DependencyInjectionExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureEdge> injectsEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.Injects)
                .ToArray();
            IReadOnlyList<ArchitectureEdge> dependsOnEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.DependsOn)
                .ToArray();

            Assert.Contains(injectsEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.PrimaryService" && edge.TargetNodeStableKey.Value == "type://Sample.App.IRepository" && ContainsMetadata(edge, "\"constructorParameter\":\"repository\""));
            Assert.Contains(dependsOnEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.PrimaryService" && edge.TargetNodeStableKey.Value == "type://Sample.App.IRepository" && ContainsMetadata(edge, "\"constructorParameter\":\"repository\""));
            Assert.Contains(injectsEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.SecondaryService" && edge.TargetNodeStableKey.Value == "type://Sample.App.IClock" && ContainsMetadata(edge, "\"constructorParameter\":\"clock\""));
            Assert.Equal(injectsEdges.Count, injectsEdges.Select(edge => edge.StableKey.Value).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(dependsOnEdges.Count, dependsOnEdges.Select(edge => edge.StableKey.Value).Distinct(StringComparer.Ordinal).Count());
        }

        /// <summary>
        /// Determines whether an edge metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="edge">The architecture edge whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the metadata payload.</param>
        /// <returns><see langword="true"/> when the metadata contains the expected fragment; otherwise, <see langword="false"/>.</returns>
        private static bool ContainsMetadata(ArchitectureEdge edge, string expectedFragment)
        {
            // Metadata comparisons use canonical JSON so tests remain stable even if dictionary insertion order changes.
            return edge.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether an evidence metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="evidence">The evidence record whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the metadata payload.</param>
        /// <returns><see langword="true"/> when the metadata contains the expected fragment; otherwise, <see langword="false"/>.</returns>
        private static bool ContainsEvidenceMetadata(EvidenceRecord evidence, string expectedFragment)
        {
            // Evidence metadata distinguishes wrapper call-site evidence from inner registration evidence.
            return evidence.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Builds the Roslyn semantic request used by the wrapper and constructor-correlation tests and invokes the production extractor.
        /// </summary>
        /// <returns>The dependency-injection extraction result for the wrapper fixture.</returns>
        private static DependencyInjectionExtractionResult ExtractFixture()
        {
            // The fixture includes local Microsoft DI stubs, application wrappers, external wrapper declarations, and constructor dependencies.
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
                    }
                }

                namespace External.Packages
                {
                    using Microsoft.Extensions.DependencyInjection;

                    public static class ExternalRegistration
                    {
                        public static IServiceCollection AddExternal(this IServiceCollection services);
                    }
                }

                namespace Sample.App
                {
                    using External.Packages;
                    using Microsoft.Extensions.DependencyInjection;

                    public interface IRepository
                    {
                    }

                    public sealed class Repository : IRepository
                    {
                    }

                    public interface IClock
                    {
                    }

                    public sealed class Clock : IClock
                    {
                    }

                    public interface IPrimaryService
                    {
                    }

                    public sealed class PrimaryService : IPrimaryService
                    {
                        public PrimaryService(IRepository repository)
                        {
                        }
                    }

                    public interface ISecondaryService
                    {
                    }

                    public sealed class SecondaryService : ISecondaryService
                    {
                        public SecondaryService(IClock clock)
                        {
                        }
                    }

                    public static class Composition
                    {
                        public static void Configure(IServiceCollection services)
                        {
                            services.AddPrimary();
                            services.AddComposite();
                            services.AddCycleA();
                            services.AddDepth1();
                            services.AddExternal();
                            dynamic dynamicServices = services;
                            dynamicServices.AddSingleton<IClock, Clock>();
                        }
                    }

                    public static class ServiceModules
                    {
                        public static IServiceCollection AddPrimary(this IServiceCollection services)
                        {
                            services.AddSingleton<IRepository, Repository>();
                            services.AddSingleton<IPrimaryService, PrimaryService>();
                            return services;
                        }

                        public static IServiceCollection AddComposite(this IServiceCollection services)
                        {
                            return services.AddSecondary();
                        }

                        public static IServiceCollection AddSecondary(this IServiceCollection services)
                        {
                            services.AddScoped<IClock, Clock>();
                            services.AddScoped<ISecondaryService, SecondaryService>();
                            return services;
                        }

                        public static IServiceCollection AddCycleA(this IServiceCollection services)
                        {
                            return services.AddCycleB();
                        }

                        public static IServiceCollection AddCycleB(this IServiceCollection services)
                        {
                            return services.AddCycleA();
                        }

                        public static IServiceCollection AddDepth1(this IServiceCollection services) => services.AddDepth2();
                        public static IServiceCollection AddDepth2(this IServiceCollection services) => services.AddDepth3();
                        public static IServiceCollection AddDepth3(this IServiceCollection services) => services.AddDepth4();
                        public static IServiceCollection AddDepth4(this IServiceCollection services) => services.AddDepth5();
                        public static IServiceCollection AddDepth5(this IServiceCollection services) => services.AddDepth6();
                        public static IServiceCollection AddDepth6(this IServiceCollection services) => services;
                    }
                }
                """;

            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-di-wrapper-fixture"));
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
