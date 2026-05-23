using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.Integrations.Rpc;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Archon.Extractors.Integrations.Tests.Rpc
{
    /// <summary>
    /// Verifies that the WP010 RPC and generated-client extractor turns WCF, SOAP/ASMX, and gRPC static evidence into safe graph facts.
    /// </summary>
    public sealed class RpcGeneratedClientIntegrationExtractorTests
    {
        /// <summary>
        /// Confirms WCF generated proxies, ClientBase calls, ChannelFactory calls, endpoint configuration, and binding metadata are projected as external-service facts.
        /// </summary>
        [Fact]
        public void Extract_WhenWcfProxyAndChannelFactoryEvidenceExists_ShouldEmitExternalServiceFacts()
        {
            // The fixture models classic WCF usage without executing generated proxy constructors or opening channels.
            RpcGeneratedClientIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureEdge> callEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.CallsExternalService)
                .ToArray();

            Assert.Empty(result.Errors);
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.ExternalService && node.DisplayName == "https://orders.example.test/OrderService.svc" && ContainsMetadata(node, "\"provider\":\"WCF\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.ExternalService && node.DisplayName == "https://orders.example.test/OrderService.svc" && ContainsMetadata(node, "\"bindingType\":\"basicHttpBinding\""));
            Assert.Contains(callEdges, edge => edge.SourceNodeStableKey.Value == "method://Sample.App.OrderWorkflow.SubmitAsync" && ContainsMetadata(edge, "\"operation\":\"SubmitOrderAsync\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesConfig && edge.TargetNodeStableKey.Value == "config://system.serviceModel:client:endpoint:OrderServiceEndpoint:address");
        }

        /// <summary>
        /// Confirms SOAP and ASMX generated proxy evidence is captured while generated endpoint ambiguity remains explicit and warning-backed.
        /// </summary>
        [Fact]
        public void Extract_WhenSoapAsmxGeneratedProxyEvidenceExists_ShouldEmitProxyFactsAndUnknowns()
        {
            // The SOAP fixture includes a generated web-service proxy and an ambiguous generated proxy so the extractor must distinguish known and unknown targets.
            RpcGeneratedClientIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureNode> externalServices = result.Snapshot.Nodes
                .Where(node => node.NodeKind == NodeKind.ExternalService)
                .ToArray();

            Assert.Contains(externalServices, node => node.DisplayName == "https://legacy.example.test/Customer.asmx" && ContainsMetadata(node, "\"provider\":\"SOAP/ASMX\""));
            Assert.Contains(externalServices, node => node.UnknownState.HasUnknownData && ContainsMetadata(node, "\"generatedClientType\":\"Sample.App.AmbiguousGeneratedProxy\""));
            Assert.Contains(result.Warnings, warning => warning.Contains("generated proxy", StringComparison.OrdinalIgnoreCase) && warning.Contains("unresolved", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Confirms gRPC channels, generated clients, typed-client registrations, configuration keys, and runtime-computed channels are represented deterministically.
        /// </summary>
        [Fact]
        public void Extract_WhenGrpcEvidenceExists_ShouldEmitGrpcFactsAndUnknowns()
        {
            // gRPC evidence can appear at channel creation, generated client construction, typed registration, and generated client method calls.
            RpcGeneratedClientIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureNode> externalServices = result.Snapshot.Nodes
                .Where(node => node.NodeKind == NodeKind.ExternalService)
                .ToArray();

            Assert.Contains(externalServices, node => node.DisplayName == "Sample.App.Greeter.GreeterClient" && ContainsMetadata(node, "\"provider\":\"gRPC\""));
            Assert.Contains(externalServices, node => node.DisplayName == "Sample.App.Greeter.GreeterClient" && ContainsMetadata(node, "\"generatedClientType\":\"Sample.App.Greeter.GreeterClient\""));
            Assert.Contains(result.Snapshot.Nodes, node => ContainsMetadata(node, "\"configurationKeyStableKey\":\"config://Integrations:Grpc:Address\""));
            Assert.Contains(externalServices, node => node.UnknownState.HasUnknownData && ContainsMetadata(node, "\"targetKind\":\"ExternalService\""));
            Assert.Contains(result.Warnings, warning => warning.Contains("runtime-computed", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Confirms duplicate generated-client observations are stable-key deduplicated and large artifact safeguards produce bounded diagnostics.
        /// </summary>
        [Fact]
        public void Extract_WhenDuplicateAndLargeArtifactEvidenceExists_ShouldDeduplicateAndWarn()
        {
            // The fixture repeats the same WCF call and includes an intentionally oversized artifact so the extractor must de-duplicate graph facts and avoid unbounded generated-file traversal.
            RpcGeneratedClientIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<string> nodeKeys = result.Snapshot.Nodes.Select(node => node.StableKey.Value).ToArray();
            IReadOnlyList<string> edgeKeys = result.Snapshot.Edges.Select(edge => edge.StableKey.Value).ToArray();

            Assert.Equal(nodeKeys.Count, nodeKeys.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(edgeKeys.Count, edgeKeys.Distinct(StringComparer.Ordinal).Count());
            Assert.Single(result.Snapshot.Nodes, node => node.StableKey.Value == "externalservice://https://orders.example.test/OrderService.svc");
            Assert.Contains(result.Warnings, warning => warning.Contains("large generated", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Snapshot.Evidence, evidence => ContainsSensitiveText(evidence.SnippetPreview));
            Assert.DoesNotContain(result.Snapshot.Nodes, node => ContainsSensitiveText(node.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Warnings, ContainsSensitiveText);
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
        /// Determines whether a value contains sensitive literals from the RPC fixture.
        /// </summary>
        /// <param name="value">The output value to inspect.</param>
        /// <returns><see langword="true" /> when fixture secret text appears; otherwise, <see langword="false" />.</returns>
        private static bool ContainsSensitiveText(string? value)
        {
            // Generated artifacts and configuration can contain endpoint credentials, so every external output surface is checked.
            return value?.Contains("ProxySecretToken", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("password=", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("ApiKeyValue", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Builds the shared repository/Roslyn fixture and invokes the production RPC generated-client extractor.
        /// </summary>
        /// <returns>The RPC generated-client extraction result for the fixture repository.</returns>
        private static RpcGeneratedClientIntegrationExtractionResult ExtractFixture()
        {
            // The fixture writes static generated artifacts and source stubs under a temporary repository so artifact traversal remains repository-relative and side-effect free.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-rpc-generated-client-fixture", Guid.NewGuid().ToString("N"));
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.App");
            Directory.CreateDirectory(Path.Combine(projectDirectory, "Connected Services", "OrderService"));
            Directory.CreateDirectory(Path.Combine(projectDirectory, "Service References", "CustomerService"));
            Directory.CreateDirectory(Path.Combine(projectDirectory, "Generated", "Grpc"));
            File.WriteAllText(Path.Combine(projectDirectory, "Connected Services", "OrderService", "Reference.cs"), "// generated WCF proxy endpoint=https://orders.example.test/OrderService.svc binding=basicHttpBinding contract=IOrderService token=ProxySecretToken");
            File.WriteAllText(Path.Combine(projectDirectory, "Connected Services", "OrderService", "ConnectedService.json"), "{ \"ProviderId\": \"Microsoft WCF Web Service Reference Provider\", \"Endpoint\": \"https://orders.example.test/OrderService.svc\" }");
            File.WriteAllText(Path.Combine(projectDirectory, "Service References", "CustomerService", "Reference.cs"), "// generated ASMX proxy endpoint=https://legacy.example.test/Customer.asmx contract=CustomerSoap");
            File.WriteAllText(Path.Combine(projectDirectory, "Generated", "Grpc", "GreeterGrpc.cs"), "// generated gRPC client type Sample.App.Greeter.GreeterClient endpoint=https://grpc.example.test");
            File.WriteAllText(Path.Combine(projectDirectory, "Generated", "Grpc", "HugeGrpc.cs"), new string('x', 80_000));
            File.WriteAllText(Path.Combine(projectDirectory, "app.config"), """
                <configuration>
                  <system.serviceModel>
                    <client>
                      <endpoint name="OrderServiceEndpoint" address="https://orders.example.test/OrderService.svc" binding="basicHttpBinding" contract="Sample.App.IOrderService" />
                    </client>
                  </system.serviceModel>
                </configuration>
                """);
            string sourcePath = Path.Combine(projectDirectory, "Clients.cs");
            string source = """
                namespace System.ServiceModel
                {
                    using System;

                    public class ClientBase<TChannel>
                    {
                        public ClientBase() { }
                        public ClientBase(string endpointConfigurationName) { }
                        public TChannel Channel => default!;
                    }

                    public sealed class ChannelFactory<TChannel>
                    {
                        public ChannelFactory(string endpointConfigurationName) { }
                        public TChannel CreateChannel() => default!;
                    }
                }

                namespace Grpc.Net.Client
                {
                    public sealed class GrpcChannel
                    {
                        public static GrpcChannel ForAddress(string address) => new();
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
                    using Grpc.Net.Client;

                    public interface IServiceCollection { }
                    public interface IHttpClientBuilder { }
                    public static class GrpcClientServiceExtensions
                    {
                        public static IHttpClientBuilder AddGrpcClient<TClient>(this IServiceCollection services, Action<object> configureClient) where TClient : class => default!;
                    }
                }

                namespace Sample.App
                {
                    using Grpc.Net.Client;
                    using Microsoft.Extensions.Configuration;
                    using Microsoft.Extensions.DependencyInjection;
                    using System.ServiceModel;
                    using System.Threading.Tasks;

                    public interface IOrderService
                    {
                        Task SubmitOrderAsync(string id);
                    }

                    public sealed class OrderServiceClient : ClientBase<IOrderService>, IOrderService
                    {
                        public OrderServiceClient() { }
                        public OrderServiceClient(string endpointConfigurationName) : base(endpointConfigurationName) { }
                        public Task SubmitOrderAsync(string id) => Channel.SubmitOrderAsync(id);
                    }

                    public sealed class CustomerSoapClient
                    {
                        public string Url { get; set; } = "https://legacy.example.test/Customer.asmx";
                        public Task GetCustomerAsync(string id) => Task.CompletedTask;
                    }

                    public sealed class AmbiguousGeneratedProxy
                    {
                        public Task PingAsync(string endpointName) => Task.CompletedTask;
                    }

                    public sealed class Greeter
                    {
                        public sealed class GreeterClient
                        {
                            public GreeterClient(GrpcChannel channel) { }
                            public Task SayHelloAsync(string name) => Task.CompletedTask;
                        }
                    }

                    public sealed class OrderWorkflow
                    {
                        public async Task SubmitAsync()
                        {
                            var client = new OrderServiceClient("OrderServiceEndpoint");
                            await client.SubmitOrderAsync("42");
                            await client.SubmitOrderAsync("42");
                            var factory = new ChannelFactory<IOrderService>("OrderServiceEndpoint");
                            await factory.CreateChannel().SubmitOrderAsync("43");
                        }
                    }

                    public sealed class LegacyCustomerWorkflow
                    {
                        public async Task LoadAsync(string dynamicEndpoint)
                        {
                            var client = new CustomerSoapClient();
                            await client.GetCustomerAsync("abc");
                            var ambiguous = new AmbiguousGeneratedProxy();
                            await ambiguous.PingAsync(dynamicEndpoint);
                        }
                    }

                    public sealed class GrpcWorkflow
                    {
                        private readonly IConfiguration _configuration;

                        public GrpcWorkflow(IConfiguration configuration)
                        {
                            _configuration = configuration;
                        }

                        public async Task CallAsync(string runtimeAddress)
                        {
                            var channel = GrpcChannel.ForAddress("https://grpc.example.test");
                            _ = _configuration["Integrations:Grpc:Address"];
                            var client = new Greeter.GreeterClient(channel);
                            await client.SayHelloAsync("world");
                            var dynamicChannel = GrpcChannel.ForAddress(runtimeAddress);
                            var dynamicClient = new Greeter.GreeterClient(dynamicChannel);
                            await dynamicClient.SayHelloAsync("unknown");
                        }
                    }

                    public static class Composition
                    {
                        public static void Register(IServiceCollection services)
                        {
                            services.AddGrpcClient<Greeter.GreeterClient>(options => { });
                        }
                    }
                }
                """;
            File.WriteAllText(sourcePath, source);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Sample.App",
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Task).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            SemanticExtractionRequest semanticRequest = new(repositoryRoot, "src/Sample.App/Sample.App.csproj", sourcePath, syntaxTree, semanticModel);
            RpcGeneratedClientIntegrationExtractor extractor = new();

            return extractor.Extract(new RpcGeneratedClientIntegrationExtractionRequest(StableKeyGenerator.ForRepository("Sample.App"), repositoryRoot, [semanticRequest]));
        }
    }
}
