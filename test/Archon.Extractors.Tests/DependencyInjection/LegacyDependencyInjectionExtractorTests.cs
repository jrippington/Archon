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
    /// Verifies legacy dependency-injection extraction for older containers, service locators, manual factories, and unsupported forms.
    /// </summary>
    public sealed class LegacyDependencyInjectionExtractorTests
    {
        /// <summary>
        /// Confirms representative legacy container registrations produce REGISTERED_AS_SERVICE relationships with container metadata.
        /// </summary>
        [Fact]
        public void ExtractAddsLegacyContainerRegistrationRelationshipsWithMetadata()
        {
            // The fixture contains one supported call per legacy container family so the catalog shape is validated without external packages.
            DependencyInjectionExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureEdge> registrationEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.RegisteredAsService)
                .ToArray();

            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.Legacy.UnityService" && edge.TargetNodeStableKey.Value == "type://Sample.Legacy.IUnityService" && ContainsMetadata(edge, "\"containerKind\":\"Unity\"") && ContainsMetadata(edge, "\"lifetime\":\"Transient\""));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.Legacy.AutofacService" && edge.TargetNodeStableKey.Value == "type://Sample.Legacy.IAutofacService" && ContainsMetadata(edge, "\"containerKind\":\"Autofac\"") && ContainsMetadata(edge, "\"lifetime\":\"Unknown\""));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.Legacy.WindsorService" && edge.TargetNodeStableKey.Value == "type://Sample.Legacy.IWindsorService" && ContainsMetadata(edge, "\"containerKind\":\"Castle Windsor\""));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.Legacy.StructureMapService" && edge.TargetNodeStableKey.Value == "type://Sample.Legacy.IStructureMapService" && ContainsMetadata(edge, "\"containerKind\":\"StructureMap\""));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.Legacy.NinjectService" && edge.TargetNodeStableKey.Value == "type://Sample.Legacy.INinjectService" && ContainsMetadata(edge, "\"containerKind\":\"Ninject\""));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.Legacy.SimpleInjectorService" && edge.TargetNodeStableKey.Value == "type://Sample.Legacy.ISimpleInjectorService" && ContainsMetadata(edge, "\"containerKind\":\"SimpleInjector\""));
        }

        /// <summary>
        /// Confirms service locator and manual factory patterns are represented conservatively with heuristic metadata and lower confidence.
        /// </summary>
        [Fact]
        public void ExtractAddsServiceLocatorAndManualFactoryHeuristicFacts()
        {
            // Heuristic patterns prove composition usage but not full container registration semantics, so confidence must remain conservative.
            DependencyInjectionExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureEdge> registrationEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.RegisteredAsService)
                .ToArray();

            ArchitectureEdge locatorEdge = Assert.Single(registrationEdges, edge => edge.TargetNodeStableKey.Value == "type://Sample.Legacy.ILocatedService");
            Assert.Equal(Confidence.Medium, locatorEdge.Confidence);
            Assert.True(ContainsMetadata(locatorEdge, "\"containerKind\":\"CommonServiceLocator\""));
            Assert.True(ContainsMetadata(locatorEdge, "\"heuristicDetection\":true"));

            ArchitectureEdge factoryEdge = Assert.Single(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.Legacy.ManualFactoryService");
            Assert.Equal(Confidence.Medium, factoryEdge.Confidence);
            Assert.True(ContainsMetadata(factoryEdge, "\"containerKind\":\"ManualFactory\""));
            Assert.True(ContainsMetadata(factoryEdge, "\"registrationFamily\":\"ManualFactory\""));
        }

        /// <summary>
        /// Confirms unsupported legacy forms become explicit unknown facts and warnings instead of guessed concrete registrations.
        /// </summary>
        [Fact]
        public void ExtractAddsUnknownsAndWarningsForUnsupportedLegacyForms()
        {
            // Assembly scanning proves container use but not individual mappings, so the graph must carry explicit unknown state.
            DependencyInjectionExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureEdge> registrationEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.RegisteredAsService)
                .ToArray();

            ArchitectureEdge scanningEdge = Assert.Single(registrationEdges, edge => edge.Metadata.ToCanonicalJson().Contains("RegisterAssemblyTypes", StringComparison.Ordinal));
            Assert.True(scanningEdge.UnknownState.HasUnknownData);
            Assert.Equal(Confidence.Medium, scanningEdge.Confidence);
            Assert.True(ContainsMetadata(scanningEdge, "\"unknownRegistration\":true"));
            Assert.Contains(result.Warnings, warning => warning.Contains("Unsupported legacy container registration", StringComparison.Ordinal));
        }

        /// <summary>
        /// Confirms legacy dependency-injection evidence is source-backed and repository-relative.
        /// </summary>
        [Fact]
        public void ExtractAddsSourceEvidenceForLegacyFacts()
        {
            // Evidence quality is required so later graph explanations can navigate back to each legacy composition point.
            DependencyInjectionExtractionResult result = ExtractFixture();

            Assert.All(
                result.Snapshot.Evidence,
                evidence =>
                {
                    Assert.Equal(EvidenceKind.SourceCode, evidence.EvidenceKind);
                    Assert.Equal("src/Sample.Legacy/LegacyComposition.cs", evidence.FilePath.Value);
                    Assert.True(evidence.StartLine > 0);
                    Assert.NotNull(evidence.SnippetPreview);
                    Assert.StartsWith("sha256:", evidence.SnippetHash, StringComparison.Ordinal);
                });
        }

        /// <summary>
        /// Determines whether an edge metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="edge">The architecture edge whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the metadata payload.</param>
        /// <returns><see langword="true"/> when the metadata contains the expected fragment; otherwise, <see langword="false"/>.</returns>
        private static bool ContainsMetadata(ArchitectureEdge edge, string expectedFragment)
        {
            // Canonical JSON comparisons keep assertions deterministic regardless of metadata dictionary construction order.
            return edge.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Builds the Roslyn semantic request used by the legacy dependency-injection tests and invokes the production extractor.
        /// </summary>
        /// <returns>The dependency-injection extraction result for the legacy fixture.</returns>
        private static DependencyInjectionExtractionResult ExtractFixture()
        {
            // The fixture declares local legacy container stubs so Roslyn can bind representative APIs without external NuGet packages.
            string source = """
                using System;

                namespace Microsoft.Practices.Unity
                {
                    public sealed class UnityContainer
                    {
                        public UnityContainer RegisterType<TService, TImplementation>() => this;
                        public UnityContainer RegisterTypes(params Type[] markerTypes) => this;
                    }
                }

                namespace Autofac
                {
                    public sealed class ContainerBuilder
                    {
                        public RegistrationBuilder<TImplementation> RegisterType<TImplementation>() => new RegistrationBuilder<TImplementation>();
                        public RegistrationBuilder<object> RegisterAssemblyTypes(params object[] assemblies) => new RegistrationBuilder<object>();
                    }

                    public sealed class RegistrationBuilder<TImplementation>
                    {
                        public RegistrationBuilder<TImplementation> As<TService>() => this;
                    }
                }

                namespace Castle.Windsor
                {
                    public sealed class WindsorContainer
                    {
                        public void Register(params Castle.MicroKernel.Registration.ComponentRegistration[] registrations) { }
                    }
                }

                namespace Castle.MicroKernel.Registration
                {
                    public sealed class ComponentRegistration { }

                    public static class Component
                    {
                        public static ComponentRegistration For<TService, TImplementation>() => new ComponentRegistration();
                    }
                }

                namespace StructureMap
                {
                    public sealed class ConfigurationExpression
                    {
                        public MappingExpression<TService> For<TService>() => new MappingExpression<TService>();
                    }

                    public sealed class MappingExpression<TService>
                    {
                        public void Use<TImplementation>() { }
                    }
                }

                namespace Ninject
                {
                    public sealed class StandardKernel
                    {
                        public BindingExpression<TService> Bind<TService>() => new BindingExpression<TService>();
                    }

                    public sealed class BindingExpression<TService>
                    {
                        public void To<TImplementation>() { }
                    }
                }

                namespace SimpleInjector
                {
                    public sealed class Container
                    {
                        public void Register<TService, TImplementation>() { }
                    }
                }

                namespace Microsoft.Practices.ServiceLocation
                {
                    public interface IServiceLocator
                    {
                        TService GetInstance<TService>();
                    }

                    public static class ServiceLocator
                    {
                        public static IServiceLocator Current { get; set; } = null!;
                    }
                }

                namespace Sample.Legacy
                {
                    public interface IUnityService { }
                    public sealed class UnityService : IUnityService { }
                    public interface IAutofacService { }
                    public sealed class AutofacService : IAutofacService { }
                    public interface IWindsorService { }
                    public sealed class WindsorService : IWindsorService { }
                    public interface IStructureMapService { }
                    public sealed class StructureMapService : IStructureMapService { }
                    public interface INinjectService { }
                    public sealed class NinjectService : INinjectService { }
                    public interface ISimpleInjectorService { }
                    public sealed class SimpleInjectorService : ISimpleInjectorService { }
                    public interface ILocatedService { }
                    public sealed class LocatedService : ILocatedService { }
                    public interface IManualFactoryService { }
                    public sealed class ManualFactoryService : IManualFactoryService { }

                    public sealed class ManualFactory
                    {
                        public IManualFactoryService CreateManualFactoryService()
                        {
                            return new ManualFactoryService();
                        }
                    }

                    public static class LegacyComposition
                    {
                        public static void RegisterAll()
                        {
                            var unity = new Microsoft.Practices.Unity.UnityContainer();
                            unity.RegisterType<IUnityService, UnityService>();
                            unity.RegisterTypes(typeof(LegacyComposition));

                            var autofac = new Autofac.ContainerBuilder();
                            autofac.RegisterType<AutofacService>().As<IAutofacService>();
                            autofac.RegisterAssemblyTypes(typeof(LegacyComposition));

                            var windsor = new Castle.Windsor.WindsorContainer();
                            windsor.Register(Castle.MicroKernel.Registration.Component.For<IWindsorService, WindsorService>());

                            var structureMap = new StructureMap.ConfigurationExpression();
                            structureMap.For<IStructureMapService>().Use<StructureMapService>();

                            var kernel = new Ninject.StandardKernel();
                            kernel.Bind<INinjectService>().To<NinjectService>();

                            var simpleInjector = new SimpleInjector.Container();
                            simpleInjector.Register<ISimpleInjectorService, SimpleInjectorService>();

                            ILocatedService located = Microsoft.Practices.ServiceLocation.ServiceLocator.Current.GetInstance<ILocatedService>();
                            var manual = new ManualFactory();
                            IManualFactoryService created = manual.CreateManualFactoryService();
                        }
                    }
                }
                """;

            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-di-legacy-fixture"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.Legacy", "LegacyComposition.cs");
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: documentPath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Sample.Legacy",
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            SemanticExtractionRequest request = new(repositoryRoot, "src/Sample.Legacy/Sample.Legacy.csproj", documentPath, syntaxTree, semanticModel);
            DirectMicrosoftDependencyInjectionExtractor extractor = new();

            return extractor.Extract(new DependencyInjectionExtractionRequest(StableKeyGenerator.ForRepository("Sample.Legacy"), request));
        }
    }
}
