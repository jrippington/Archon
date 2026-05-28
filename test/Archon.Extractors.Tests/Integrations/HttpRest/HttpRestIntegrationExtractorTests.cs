using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.Integrations.HttpRest;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Archon.Extractors.Tests.Integrations.HttpRest
{
    /// <summary>
    /// Verifies that the HTTP and REST extractor turns deterministic outbound client evidence into safe graph facts.
    /// </summary>
    public sealed class HttpRestIntegrationExtractorTests
    {
        /// <summary>
        /// Confirms direct, injected, factory-created, named, typed, and request-message HTTP client patterns emit external-service facts with method and path evidence.
        /// </summary>
        [Fact]
        public void Extract_WhenHttpClientPatternsExist_ShouldEmitServiceCallsWithEvidence()
        {
            // The fixture combines the supported HttpClient patterns in one source document so the extractor must rely on symbols and deterministic literal evidence rather than live calls.
            HttpRestIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureEdge> callEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.CallsExternalService)
                .ToArray();

            Assert.Empty(result.Errors);
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.ExternalService && node.DisplayName == "https://billing.example.test" && ContainsMetadata(node, "\"operation\":\"GET\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.ExternalService && node.DisplayName == "InventoryClient" && ContainsMetadata(node, "\"httpClientName\":\"InventoryClient\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.ExternalService && node.DisplayName == "https://catalog.example.test");
            Assert.Contains(callEdges, edge => edge.SourceNodeStableKey.Value == "method://Sample.App.BillingClient.LoadAsync" && ContainsMetadata(edge, "\"relativePath\":\"/v1/invoices\""));
            Assert.Contains(callEdges, edge => edge.SourceNodeStableKey.Value == "method://Sample.App.FactoryClient.SendAsync" && ContainsMetadata(edge, "\"operation\":\"POST\""));
            Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Sample.App/Clients.cs" && evidence.SnippetPreview?.Contains("GetAsync", StringComparison.Ordinal) == true);
        }

        /// <summary>
        /// Confirms configuration-backed endpoints create USES_CONFIG relationships while sensitive endpoint and header values are redacted from every externally visible output surface.
        /// </summary>
        [Fact]
        public void Extract_WhenConfigurationAndSecretsExist_ShouldEmitUsesConfigAndRedactSensitiveValues()
        {
            // Configuration correlation is literal-key based: the key is retained as a graph identity, but bearer tokens and API key values are never persisted.
            HttpRestIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureEdge> usesConfigEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.UsesConfig)
                .ToArray();

            Assert.Contains(usesConfigEdges, edge => edge.TargetNodeStableKey.Value == "config://Integrations:Billing:BaseUrl");
            Assert.Contains(usesConfigEdges, edge => edge.TargetNodeStableKey.Value == "config://Integrations:RestSharp:BaseUrl");
            Assert.DoesNotContain(result.Snapshot.Evidence, evidence => ContainsSensitiveText(evidence.SnippetPreview));
            Assert.DoesNotContain(result.Snapshot.Nodes, node => ContainsSensitiveText(node.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Snapshot.Edges, edge => ContainsSensitiveText(edge.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Warnings, ContainsSensitiveText);
            Assert.DoesNotContain(result.Errors, ContainsSensitiveText);
        }

        /// <summary>
        /// Confirms RestSharp and deterministic REST wrapper evidence is captured without inventing targets for dynamic resources or authentication details.
        /// </summary>
        [Fact]
        public void Extract_WhenRestSharpAndWrappersExist_ShouldEmitRestFactsAndUnknowns()
        {
            // RestSharp and wrapper calls are represented as REST integrations, with unknown nodes used whenever a target is computed or authentication is ambiguous.
            HttpRestIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureNode> externalServices = result.Snapshot.Nodes
                .Where(node => node.NodeKind == NodeKind.ExternalService)
                .ToArray();

            Assert.Contains(externalServices, node => node.DisplayName == "https://rest.example.test" && ContainsMetadata(node, "\"provider\":\"RestSharp\""));
            Assert.Contains(externalServices, node => node.DisplayName == "Sample.App.IApiClient" && ContainsMetadata(node, "\"provider\":\"RestAbstraction\""));
            Assert.Contains(externalServices, node => node.UnknownState.HasUnknownData && node.StableKey.Value.StartsWith("externalservice://unknown/src/Sample.App/Clients.cs/", StringComparison.Ordinal));
            Assert.Contains(result.Warnings, warning => warning.Contains("computed at runtime", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Confirms duplicated source patterns collapse to deterministic graph identities instead of emitting duplicate target relationships.
        /// </summary>
        [Fact]
        public void Extract_WhenDuplicateEvidenceExists_ShouldDeduplicateGraphFacts()
        {
            // Duplicate requests to the same target and operation prove stable-key accumulation rather than occurrence-order identity.
            HttpRestIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<string> nodeKeys = result.Snapshot.Nodes.Select(node => node.StableKey.Value).ToArray();
            IReadOnlyList<string> edgeKeys = result.Snapshot.Edges.Select(edge => edge.StableKey.Value).ToArray();

            Assert.Equal(nodeKeys.Count, nodeKeys.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(edgeKeys.Count, edgeKeys.Distinct(StringComparer.Ordinal).Count());
            Assert.Single(result.Snapshot.Nodes, node => node.StableKey.Value == "externalservice://https://billing.example.test");
        }

        /// <summary>
        /// Determines whether a node metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="node">The architecture node whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the node metadata.</param>
        /// <returns><see langword="true" /> when the metadata contains the fragment; otherwise, <see langword="false" />.</returns>
        private static bool ContainsMetadata(ArchitectureNode node, string expectedFragment)
        {
            // Canonical JSON comparisons keep assertions stable regardless of metadata dictionary construction order.
            return node.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether an edge metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="edge">The architecture edge whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the edge metadata.</param>
        /// <returns><see langword="true" /> when the metadata contains the fragment; otherwise, <see langword="false" />.</returns>
        private static bool ContainsMetadata(ArchitectureEdge edge, string expectedFragment)
        {
            // Edge metadata assertions verify usage classification without depending on object reference identity.
            return edge.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a value contains sensitive literals from the fixture.
        /// </summary>
        /// <param name="value">The output value to inspect.</param>
        /// <returns><see langword="true" /> when fixture secret text appears; otherwise, <see langword="false" />.</returns>
        private static bool ContainsSensitiveText(string? value)
        {
            // The test checks every graph and diagnostic surface that could accidentally leak secrets.
            return value?.Contains("SuperSecretBearerToken", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("ApiKeyValue", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("password=", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Builds the shared Roslyn fixture and invokes the production HTTP/REST integration extractor.
        /// </summary>
        /// <returns>The HTTP/REST extraction result for the fixture source.</returns>
        private static HttpRestIntegrationExtractionResult ExtractFixture()
        {
            // The source includes local API stubs so Roslyn can bind supported patterns without restoring external packages or touching the network.
            string source = """
                namespace System.Net.Http
                {
                    using System;
                    using System.Threading.Tasks;

                    public sealed class HttpMethod
                    {
                        public static HttpMethod Get { get; } = new("GET");
                        public static HttpMethod Post { get; } = new("POST");
                        public string Method { get; }
                        public HttpMethod(string method) { Method = method; }
                    }

                    public sealed class HttpRequestMessage
                    {
                        public HttpRequestMessage(HttpMethod method, string requestUri) { Method = method; RequestUri = requestUri; }
                        public HttpMethod Method { get; }
                        public string RequestUri { get; }
                        public System.Net.Http.Headers.HttpRequestHeaders Headers { get; } = new();
                    }

                    public sealed class HttpClient
                    {
                        public Uri? BaseAddress { get; set; }
                        public Task<string> GetAsync(string requestUri) => Task.FromResult(string.Empty);
                        public Task<string> PostAsync(string requestUri, object? content) => Task.FromResult(string.Empty);
                        public Task<string> SendAsync(HttpRequestMessage request) => Task.FromResult(string.Empty);
                    }

                    public interface IHttpClientFactory
                    {
                        HttpClient CreateClient(string name);
                    }
                }

                namespace System.Net.Http.Json
                {
                    using System.Net.Http;
                    using System.Threading.Tasks;

                    public static class HttpClientJsonExtensions
                    {
                        public static Task<T?> GetFromJsonAsync<T>(this HttpClient client, string requestUri) => Task.FromResult(default(T));
                        public static Task<string> PostAsJsonAsync<T>(this HttpClient client, string requestUri, T value) => Task.FromResult(string.Empty);
                    }
                }

                namespace System.Net.Http.Headers
                {
                    public sealed class HttpRequestHeaders
                    {
                        public void Add(string name, string value) { }
                    }
                }

                namespace Microsoft.Extensions.Configuration
                {
                    public interface IConfiguration
                    {
                        string? this[string key] { get; set; }
                    }
                }

                namespace Microsoft.Extensions.DependencyInjection
                {
                    using System;
                    using System.Net.Http;

                    public interface IServiceCollection { }

                    public interface IHttpClientBuilder { }

                    public static class HttpClientFactoryServiceCollectionExtensions
                    {
                        public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name, Action<HttpClient> configureClient) => default!;
                        public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services, Action<HttpClient> configureClient) where TClient : class => default!;
                    }
                }

                namespace RestSharp
                {
                    public enum Method { Get, Post }
                    public sealed class RestClient { public RestClient(string baseUrl) { } }
                    public sealed class RestRequest { public RestRequest(string resource, Method method) { } public void AddHeader(string name, string value) { } }
                    public static class RestClientExtensions { public static object Execute(this RestClient client, RestRequest request) => new(); }
                }

                namespace Sample.App
                {
                    using Microsoft.Extensions.Configuration;
                    using Microsoft.Extensions.DependencyInjection;
                    using RestSharp;
                    using System;
                    using System.Net.Http;
                    using System.Net.Http.Json;
                    using System.Threading.Tasks;

                    public interface IApiClient
                    {
                        Task GetAsync(string path);
                    }

                    public sealed class BillingClient
                    {
                        private readonly HttpClient _httpClient;
                        private readonly IConfiguration _configuration;

                        public BillingClient(HttpClient httpClient, IConfiguration configuration)
                        {
                            _httpClient = httpClient;
                            _configuration = configuration;
                            _httpClient.BaseAddress = new Uri(_configuration["Integrations:Billing:BaseUrl"]!);
                        }

                        public async Task LoadAsync(string dynamicPath)
                        {
                            await _httpClient.GetAsync("https://billing.example.test/v1/invoices");
                            await _httpClient.GetAsync("https://billing.example.test/v1/invoices");
                            await _httpClient.GetFromJsonAsync<object>("/v1/accounts");
                            await _httpClient.GetAsync(dynamicPath);
                            var request = new HttpRequestMessage(HttpMethod.Post, "/v1/payments");
                            request.Headers.Add("Authorization", "Bearer SuperSecretBearerToken");
                            await _httpClient.SendAsync(request);
                        }
                    }

                    public sealed class FactoryClient
                    {
                        private readonly IHttpClientFactory _factory;

                        public FactoryClient(IHttpClientFactory factory)
                        {
                            _factory = factory;
                        }

                        public async Task SendAsync()
                        {
                            HttpClient client = _factory.CreateClient("InventoryClient");
                            await client.PostAsJsonAsync("/v2/items", new { Name = "sample" });
                        }
                    }

                    public sealed class CatalogTypedClient
                    {
                        public CatalogTypedClient(HttpClient httpClient) { }
                    }

                    public static class Composition
                    {
                        public static void Register(IServiceCollection services, IConfiguration configuration)
                        {
                            services.AddHttpClient("InventoryClient", client => client.BaseAddress = new Uri(configuration["Integrations:Inventory:BaseUrl"]!));
                            services.AddHttpClient<CatalogTypedClient>(client => client.BaseAddress = new Uri("https://catalog.example.test"));
                        }
                    }

                    public sealed class RestSharpClient
                    {
                        private readonly IConfiguration _configuration;

                        public RestSharpClient(IConfiguration configuration)
                        {
                            _configuration = configuration;
                        }

                        public void Call(string dynamicResource)
                        {
                            var client = new RestClient(_configuration["Integrations:RestSharp:BaseUrl"]!);
                            var request = new RestRequest("/orders", Method.Post);
                            request.AddHeader("X-Api-Key", "ApiKeyValue");
                            client.Execute(request);
                            var dynamicRequest = new RestRequest(dynamicResource, Method.Get);
                            client.Execute(dynamicRequest);
                        }
                    }

                    public sealed class WrapperConsumer
                    {
                        private readonly IApiClient _apiClient;

                        public WrapperConsumer(IApiClient apiClient)
                        {
                            _apiClient = apiClient;
                        }

                        public Task UseAsync()
                        {
                            return _apiClient.GetAsync("/health");
                        }
                    }
                }
                """;

            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-http-rest-fixture"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample.App", "Clients.cs");
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: documentPath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Sample.App",
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Task).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            SemanticExtractionRequest semanticRequest = new(repositoryRoot, "src/Sample.App/Sample.App.csproj", documentPath, syntaxTree, semanticModel);
            HttpRestIntegrationExtractor extractor = new();

            return extractor.Extract(new HttpRestIntegrationExtractionRequest(StableKeyGenerator.ForRepository("Sample.App"), repositoryRoot, [semanticRequest]));
        }
    }
}
