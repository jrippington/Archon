using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.DependencyInjection;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Archon.Extractors.DependencyInjection.Tests
{
    /// <summary>
    /// Verifies the expanded WP007 Microsoft dependency-injection slice for advanced registrations, hosted services, and HttpClient registrations.
    /// </summary>
    public sealed class AdvancedMicrosoftDependencyInjectionExtractorTests
    {
        /// <summary>
        /// Confirms service-only, typeof, factory, TryAdd, TryAddEnumerable, and Replace registration forms are extracted with family and unknown metadata.
        /// </summary>
        [Fact]
        public void ExtractAddsAdvancedRegistrationRelationshipsWithFamilyAndUnknownMetadata()
        {
            // The fixture keeps the advanced Microsoft DI API surface local so Roslyn can bind symbols without external package restore.
            DependencyInjectionExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureEdge> registrationEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.RegisteredAsService)
                .ToArray();

            Assert.Empty(result.Errors);
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.SelfRegisteredService" && edge.TargetNodeStableKey.Value == "type://Sample.App.SelfRegisteredService" && ContainsMetadata(edge, "\"registrationFamily\":\"Direct\"") && ContainsMetadata(edge, "\"lifetime\":\"Singleton\""));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.TypeofScopedWorker" && edge.TargetNodeStableKey.Value == "type://Sample.App.ITypeofScopedWorker" && ContainsMetadata(edge, "\"registrationFamily\":\"DirectTypeof\"") && ContainsMetadata(edge, "\"lifetime\":\"Scoped\""));
            Assert.Contains(registrationEdges, edge => edge.TargetNodeStableKey.Value == "type://Sample.App.IFactoryService" && edge.UnknownState.HasUnknownData && ContainsMetadata(edge, "\"registrationFamily\":\"Factory\"") && ContainsMetadata(edge, "\"implementationType\":\"Unknown\""));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.TryAddService" && edge.TargetNodeStableKey.Value == "type://Sample.App.ITryAddService" && ContainsMetadata(edge, "\"registrationFamily\":\"TryAdd\""));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.TryAddEnumerableService" && edge.TargetNodeStableKey.Value == "type://Sample.App.ITryAddEnumerableService" && ContainsMetadata(edge, "\"registrationFamily\":\"TryAddEnumerable\""));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.ReplacementService" && edge.TargetNodeStableKey.Value == "type://Sample.App.IReplacementService" && ContainsMetadata(edge, "\"registrationFamily\":\"Replace\""));
        }

        /// <summary>
        /// Confirms hosted service registrations and background service registrations preserve hosted-service metadata and graph direction.
        /// </summary>
        [Fact]
        public void ExtractAddsHostedServiceRelationshipsWithHostedMetadata()
        {
            // Hosted service facts are runtime-composition facts and should remain queryable through the same REGISTERED_AS_SERVICE edge kind.
            DependencyInjectionExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureEdge> registrationEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.RegisteredAsService)
                .ToArray();

            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.QueueHostedService" && edge.TargetNodeStableKey.Value == "type://Microsoft.Extensions.Hosting.IHostedService" && ContainsMetadata(edge, "\"hostedService\":true") && ContainsMetadata(edge, "\"registrationMethod\":\"AddHostedService\""));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.BackgroundWorker" && edge.TargetNodeStableKey.Value == "type://Sample.App.IBackgroundWorker" && ContainsMetadata(edge, "\"hostedService\":true") && ContainsMetadata(edge, "\"backgroundService\":true"));
        }

        /// <summary>
        /// Confirms named and typed HttpClient registrations preserve client-kind, client-name, typed-client, and unknown target metadata.
        /// </summary>
        [Fact]
        public void ExtractAddsHttpClientRelationshipsWithClientMetadataAndUnknownTarget()
        {
            // HttpClient registrations represent DI composition plus an external target that may not be statically knowable.
            DependencyInjectionExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureEdge> registrationEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.RegisteredAsService)
                .ToArray();

            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://System.Net.Http.IHttpClientFactory" && edge.TargetNodeStableKey.Value == "type://System.Net.Http.IHttpClientFactory" && ContainsMetadata(edge, "\"httpClientKind\":\"Default\""));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://System.Net.Http.HttpClient" && edge.TargetNodeStableKey.Value == "type://Sample.App.NamedHttpClient:orders" && edge.UnknownState.HasUnknownData && ContainsMetadata(edge, "\"httpClientKind\":\"Named\"") && ContainsMetadata(edge, "\"clientName\":\"orders\"") && ContainsMetadata(edge, "\"unknownTarget\":true"));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.TodoClient" && edge.TargetNodeStableKey.Value == "type://Sample.App.TodoClient" && edge.UnknownState.HasUnknownData && ContainsMetadata(edge, "\"httpClientKind\":\"Typed\"") && ContainsMetadata(edge, "\"typedClientType\":\"Sample.App.TodoClient\""));
            Assert.Contains(registrationEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.TodoClientImplementation" && edge.TargetNodeStableKey.Value == "type://Sample.App.ITodoClient" && edge.UnknownState.HasUnknownData && ContainsMetadata(edge, "\"httpClientKind\":\"TypedImplementation\"") && ContainsMetadata(edge, "\"typedClientType\":\"Sample.App.ITodoClient\""));
        }

        /// <summary>
        /// Determines whether an edge metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="edge">The architecture edge whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the metadata payload.</param>
        /// <returns><see langword="true"/> when the metadata contains the expected fragment; otherwise, <see langword="false"/>.</returns>
        private static bool ContainsMetadata(ArchitectureEdge edge, string expectedFragment)
        {
            // Metadata comparisons use canonical JSON so assertions remain independent of dictionary insertion order.
            return edge.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Builds the Roslyn semantic request used by the advanced Microsoft DI tests and invokes the production extractor.
        /// </summary>
        /// <returns>The dependency-injection extraction result for the advanced fixture.</returns>
        private static DependencyInjectionExtractionResult ExtractFixture()
        {
            // The source declares minimal Microsoft API stubs and application types needed to bind every Work Item 2 pattern.
            string source = """
                using System;
                using System.Net.Http;

                namespace System.Net.Http
                {
                    public sealed class HttpClient
                    {
                        public Uri? BaseAddress { get; set; }
                    }

                    public interface IHttpClientFactory
                    {
                    }
                }

                namespace Microsoft.Extensions.Hosting
                {
                    public interface IHostedService
                    {
                    }

                    public abstract class BackgroundService : IHostedService
                    {
                    }
                }

                namespace Microsoft.Extensions.DependencyInjection
                {
                    public interface IServiceCollection
                    {
                    }

                    public sealed class ServiceDescriptor
                    {
                        public static ServiceDescriptor Singleton<TService, TImplementation>() => new ServiceDescriptor();
                        public static ServiceDescriptor Scoped<TService, TImplementation>() => new ServiceDescriptor();
                        public static ServiceDescriptor Transient<TService, TImplementation>() => new ServiceDescriptor();
                    }

                    public interface IHttpClientBuilder
                    {
                    }

                    public static class ServiceCollectionServiceExtensions
                    {
                        public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
                        public static IServiceCollection AddScoped(this IServiceCollection services, Type serviceType, Type implementationType) => services;
                        public static IServiceCollection AddTransient<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory) => services;
                        public static IServiceCollection AddSingleton<TService, TImplementation>(this IServiceCollection services) => services;
                    }

                    public static class ServiceCollectionDescriptorExtensions
                    {
                        public static void TryAdd(this IServiceCollection services, ServiceDescriptor descriptor) { }
                        public static void TryAddEnumerable(this IServiceCollection services, ServiceDescriptor descriptor) { }
                        public static void Replace(this IServiceCollection services, ServiceDescriptor descriptor) { }
                    }

                    public static class ServiceCollectionHostedServiceExtensions
                    {
                        public static IServiceCollection AddHostedService<THostedService>(this IServiceCollection services)
                            where THostedService : class, Microsoft.Extensions.Hosting.IHostedService => services;
                    }

                    public static class HttpClientFactoryServiceCollectionExtensions
                    {
                        public static IServiceCollection AddHttpClient(this IServiceCollection services) => services;
                        public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name, Action<HttpClient> configureClient) => new Builder();
                        public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services, Action<HttpClient> configureClient) => new Builder();
                        public static IHttpClientBuilder AddHttpClient<TClient, TImplementation>(this IServiceCollection services, Action<HttpClient> configureClient) => new Builder();

                        private sealed class Builder : IHttpClientBuilder
                        {
                        }
                    }
                }

                namespace Sample.App
                {
                    using Microsoft.Extensions.DependencyInjection;
                    using Microsoft.Extensions.Hosting;

                    public sealed class SelfRegisteredService { }
                    public interface ITypeofScopedWorker { }
                    public sealed class TypeofScopedWorker : ITypeofScopedWorker { }
                    public interface IFactoryService { }
                    public sealed class FactoryService : IFactoryService { }
                    public interface ITryAddService { }
                    public sealed class TryAddService : ITryAddService { }
                    public interface ITryAddEnumerableService { }
                    public sealed class TryAddEnumerableService : ITryAddEnumerableService { }
                    public interface IReplacementService { }
                    public sealed class ReplacementService : IReplacementService { }
                    public sealed class QueueHostedService : IHostedService { }
                    public interface IBackgroundWorker { }
                    public sealed class BackgroundWorker : BackgroundService, IBackgroundWorker { }
                    public sealed class TodoClient { }
                    public interface ITodoClient { }
                    public sealed class TodoClientImplementation : ITodoClient { }

                    public static class AdvancedComposition
                    {
                        public static void Register(IServiceCollection services)
                        {
                            services.AddSingleton<SelfRegisteredService>();
                            services.AddScoped(typeof(ITypeofScopedWorker), typeof(TypeofScopedWorker));
                            services.AddTransient<IFactoryService>(_ => new FactoryService());
                            services.TryAdd(ServiceDescriptor.Singleton<ITryAddService, TryAddService>());
                            services.TryAddEnumerable(ServiceDescriptor.Scoped<ITryAddEnumerableService, TryAddEnumerableService>());
                            services.Replace(ServiceDescriptor.Transient<IReplacementService, ReplacementService>());
                            services.AddHostedService<QueueHostedService>();
                            services.AddSingleton<IBackgroundWorker, BackgroundWorker>();
                            services.AddHttpClient();
                            services.AddHttpClient("orders", client => client.BaseAddress = new Uri("https://orders.example.invalid"));
                            services.AddHttpClient<TodoClient>(client => client.BaseAddress = new Uri("https://todo.example.invalid"));
                            services.AddHttpClient<ITodoClient, TodoClientImplementation>(client => client.BaseAddress = new Uri("https://todo.example.invalid"));
                        }
                    }
                }
                """;

            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-di-advanced-fixture"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "AdvancedComposition.cs");
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: documentPath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Sample.App",
                [syntaxTree],
                [
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location)
                ],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            SemanticExtractionRequest request = new(repositoryRoot, "src/Sample.App/Sample.App.csproj", documentPath, syntaxTree, semanticModel);
            DirectMicrosoftDependencyInjectionExtractor extractor = new();

            return extractor.Extract(new DependencyInjectionExtractionRequest(StableKeyGenerator.ForRepository("Sample.App"), request));
        }
    }
}
