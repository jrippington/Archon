using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.Integrations.Messaging;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Archon.Extractors.Tests.Integrations.Messaging
{
    /// <summary>
    /// Verifies that the messaging extractor turns static broker, handler, and abstraction evidence into safe graph facts.
    /// </summary>
    public sealed class MessagingIntegrationExtractorTests
    {
        /// <summary>
        /// Confirms Azure Service Bus producers, processors, topic senders, configuration keys, and handler callbacks are projected as queue and topic facts.
        /// </summary>
        [Fact]
        public void Extract_WhenAzureServiceBusEvidenceExists_ShouldEmitQueueTopicAndHandlerFacts()
        {
            // The fixture models Azure Service Bus source patterns without creating clients, connecting to Azure, or starting processors.
            MessagingIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureNode> queues = result.Snapshot.Nodes
                .Where(node => node.NodeKind == NodeKind.Queue)
                .ToArray();
            IReadOnlyList<ArchitectureNode> topics = result.Snapshot.Nodes
                .Where(node => node.NodeKind == NodeKind.Topic)
                .ToArray();

            Assert.Empty(result.Errors);
            Assert.Contains(queues, node => node.DisplayName == "orders" && ContainsMetadata(node, "\"provider\":\"AzureServiceBus\"") && ContainsMetadata(node, "\"messagingRole\":\"Sender\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Handles && edge.TargetNodeStableKey.Value == "queue://AzureServiceBus:orders" && ContainsMetadata(edge, "\"messagingRole\":\"Processor\""));
            Assert.Contains(topics, node => node.DisplayName == "invoices" && ContainsMetadata(node, "\"subscriptionName\":\"billing\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesConfig && edge.TargetNodeStableKey.Value == "config://Messaging:ServiceBus:ConnectionString");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Handles && edge.TargetNodeStableKey.Value == "queue://AzureServiceBus:orders");
        }

        /// <summary>
        /// Confirms NServiceBus endpoint configuration, handlers, sagas, send/publish/subscribe operations, routing, and recoverability hints are captured.
        /// </summary>
        [Fact]
        public void Extract_WhenNServiceBusEvidenceExists_ShouldEmitEndpointHandlerSagaAndRoutingFacts()
        {
            // NServiceBus source evidence contains both endpoint configuration and message operation calls, so the graph should include roles for configuration, handlers, sagas, senders, publishers, and subscribers.
            MessagingIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureNode> queues = result.Snapshot.Nodes
                .Where(node => node.NodeKind == NodeKind.Queue)
                .ToArray();
            IReadOnlyList<ArchitectureNode> topics = result.Snapshot.Nodes
                .Where(node => node.NodeKind == NodeKind.Topic)
                .ToArray();

            Assert.Contains(queues, node => node.DisplayName == "SalesEndpoint" && ContainsMetadata(node, "\"provider\":\"NServiceBus\"") && ContainsMetadata(node, "\"transportProvider\":\"AzureServiceBus\""));
            Assert.Contains(queues, node => node.DisplayName == "sales-commands" && ContainsMetadata(node, "\"routingTarget\":\"sales-commands\""));
            Assert.Contains(queues, node => node.DisplayName == "error" && ContainsMetadata(node, "\"recoverability\":\"error\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.TargetNodeStableKey.Value == "topic://NServiceBus:Sample.Messaging.OrderSubmitted" && ContainsMetadata(edge, "\"messagingRole\":\"Publisher\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Handles && edge.TargetNodeStableKey.Value == "topic://NServiceBus:Sample.Messaging.OrderSubmitted" && ContainsMetadata(edge, "\"messagingRole\":\"Handler\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Handles && edge.TargetNodeStableKey.Value == "topic://NServiceBus:Sample.Messaging.OrderSubmitted" && ContainsMetadata(edge, "\"messagingRole\":\"Saga\""));
            Assert.Contains(result.Warnings, warning => warning.Contains("computed endpoint", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Confirms RabbitMQ, MSMQ, and generic abstraction source evidence produces deterministic queue and topic graph facts.
        /// </summary>
        [Fact]
        public void Extract_WhenRabbitMsmqAndAbstractionEvidenceExists_ShouldEmitMessagingFacts()
        {
            // The fixture includes broker-specific and wrapper-style calls so the detector must preserve provider and role metadata without opening broker connections.
            MessagingIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureNode> queues = result.Snapshot.Nodes
                .Where(node => node.NodeKind == NodeKind.Queue)
                .ToArray();
            IReadOnlyList<ArchitectureNode> topics = result.Snapshot.Nodes
                .Where(node => node.NodeKind == NodeKind.Topic)
                .ToArray();

            Assert.Contains(queues, node => node.DisplayName == "billing-queue" && ContainsMetadata(node, "\"provider\":\"RabbitMQ\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.TargetNodeStableKey.Value == "queue://RabbitMQ:billing-queue" && ContainsMetadata(edge, "\"routingKey\":\"billing.created\""));
            Assert.Contains(topics, node => node.DisplayName == "billing-exchange" && ContainsMetadata(node, "\"exchange\":\"billing-exchange\""));
            Assert.Contains(queues, node => node.DisplayName == @".\\private$\\orders" && ContainsMetadata(node, "\"provider\":\"MSMQ\""));
            Assert.Contains(queues, node => node.DisplayName == "audit-events" && ContainsMetadata(node, "\"provider\":\"MessagingAbstraction\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Handles && edge.SourceNodeStableKey.Value == "method://Sample.Messaging.RabbitWorkflow.HandleDelivery");
        }

        /// <summary>
        /// Confirms dynamic names, duplicate evidence, and secret-bearing snippets are handled conservatively and safely.
        /// </summary>
        [Fact]
        public void Extract_WhenDynamicDuplicateAndSecretEvidenceExists_ShouldWarnDeduplicateAndRedact()
        {
            // Dynamic broker names should become explicit unknowns, duplicate observations should collapse by stable key, and secret-like evidence must not leak to graph output.
            MessagingIntegrationExtractionResult result = ExtractFixture();
            IReadOnlyList<string> nodeKeys = result.Snapshot.Nodes.Select(node => node.StableKey.Value).ToArray();
            IReadOnlyList<string> edgeKeys = result.Snapshot.Edges.Select(edge => edge.StableKey.Value).ToArray();

            Assert.Equal(nodeKeys.Count, nodeKeys.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(edgeKeys.Count, edgeKeys.Distinct(StringComparer.Ordinal).Count());
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Queue && node.UnknownState.HasUnknownData);
            Assert.Contains(result.Warnings, warning => warning.Contains("dynamic", StringComparison.OrdinalIgnoreCase) || warning.Contains("computed", StringComparison.OrdinalIgnoreCase));
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
        /// Determines whether a value contains sensitive literals from the messaging fixture.
        /// </summary>
        /// <param name="value">The output value to inspect.</param>
        /// <returns><see langword="true" /> when fixture secret text appears; otherwise, <see langword="false" />.</returns>
        private static bool ContainsSensitiveText(string? value)
        {
            // Messaging source and configuration can carry broker connection strings, so every graph output surface is checked.
            return value?.Contains("Endpoint=sb://secret", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("SharedAccessKey", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("RabbitSecret", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("password=", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Builds the shared repository/Roslyn fixture and invokes the production messaging extractor.
        /// </summary>
        /// <returns>The messaging extraction result for the fixture repository.</returns>
        private static MessagingIntegrationExtractionResult ExtractFixture()
        {
            // The fixture writes local source and configuration artifacts under a temporary repository so analysis remains repository-relative and side-effect free.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-messaging-integration-fixture", Guid.NewGuid().ToString("N"));
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Messaging");
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(Path.Combine(projectDirectory, "appsettings.json"), "{ \"Messaging\": { \"ServiceBus\": { \"ConnectionString\": \"Endpoint=sb://secret/;SharedAccessKey=RabbitSecret\" } } }");
            string sourcePath = Path.Combine(projectDirectory, "MessagingClients.cs");
            string source = """
                namespace Azure.Messaging.ServiceBus
                {
                    using System;
                    using System.Threading.Tasks;

                    public sealed class ServiceBusClient
                    {
                        public ServiceBusClient(string connectionString) { }
                        public ServiceBusSender CreateSender(string queueOrTopicName) => new();
                        public ServiceBusReceiver CreateReceiver(string queueName) => new();
                        public ServiceBusProcessor CreateProcessor(string queueName) => new();
                        public ServiceBusProcessor CreateProcessor(string topicName, string subscriptionName) => new();
                    }

                    public sealed class ServiceBusSender
                    {
                        public Task SendMessageAsync(ServiceBusMessage message) => Task.CompletedTask;
                    }

                    public sealed class ServiceBusReceiver
                    {
                        public Task<ServiceBusMessage> ReceiveMessageAsync() => Task.FromResult(new ServiceBusMessage());
                    }

                    public sealed class ServiceBusProcessor
                    {
                        public event Func<ProcessMessageEventArgs, Task>? ProcessMessageAsync;
                    }

                    public sealed class ServiceBusMessage { }
                    public sealed class ProcessMessageEventArgs { }
                }

                namespace NServiceBus
                {
                    using System.Threading.Tasks;

                    public sealed class EndpointConfiguration
                    {
                        public EndpointConfiguration(string endpointName) { }
                        public TransportExtensions UseTransport<T>() => new();
                        public RecoverabilitySettings Recoverability() => new();
                    }

                    public sealed class TransportExtensions
                    {
                        public void Routing() { }
                    }

                    public sealed class RecoverabilitySettings
                    {
                        public void ErrorQueue(string queueName) { }
                    }

                    public interface IMessageSession
                    {
                        Task Send(object message);
                        Task Publish(object message);
                        Task Subscribe<TMessage>();
                    }

                    public interface IHandleMessages<TMessage>
                    {
                        Task Handle(TMessage message);
                    }

                    public abstract class Saga<TData> { }
                    public sealed class AzureServiceBusTransport { }
                }

                namespace RabbitMQ.Client
                {
                    using System;
                    using System.Text;

                    public interface IConnection
                    {
                        IModel CreateModel();
                    }

                    public interface IModel
                    {
                        void QueueDeclare(string queue, bool durable, bool exclusive, bool autoDelete, object? arguments);
                        void ExchangeDeclare(string exchange, string type);
                        void BasicPublish(string exchange, string routingKey, object? basicProperties, byte[] body);
                        string BasicConsume(string queue, bool autoAck, object consumer);
                    }

                    public sealed class ConnectionFactory
                    {
                        public string? Uri { get; set; }
                        public IConnection CreateConnection() => default!;
                    }
                }

                namespace System.Messaging
                {
                    public sealed class MessageQueue
                    {
                        public MessageQueue(string path) { }
                        public void Send(object message) { }
                        public object Receive() => new();
                    }
                }

                namespace Sample.Messaging
                {
                    using Azure.Messaging.ServiceBus;
                    using NServiceBus;
                    using RabbitMQ.Client;
                    using System.Messaging;
                    using System.Text;
                    using System.Threading.Tasks;

                    public sealed class SubmitOrder { }
                    public sealed class OrderSubmitted { }
                    public sealed class BillingSagaData { }

                    public sealed class AzureBusWorkflow
                    {
                        public async Task SendAsync(string dynamicQueue)
                        {
                            var client = new ServiceBusClient("Endpoint=sb://secret/;SharedAccessKey=RabbitSecret");
                            var sender = client.CreateSender("orders");
                            await sender.SendMessageAsync(new ServiceBusMessage());
                            await sender.SendMessageAsync(new ServiceBusMessage());
                            var processor = client.CreateProcessor("orders");
                            processor.ProcessMessageAsync += HandleMessageAsync;
                            var topicProcessor = client.CreateProcessor("invoices", "billing");
                            topicProcessor.ProcessMessageAsync += HandleMessageAsync;
                            var dynamicSender = client.CreateSender(dynamicQueue);
                            await dynamicSender.SendMessageAsync(new ServiceBusMessage());
                        }

                        public Task HandleMessageAsync(ProcessMessageEventArgs args) => Task.CompletedTask;
                    }

                    public sealed class NServiceBusWorkflow
                    {
                        public async Task ConfigureAndSendAsync(IMessageSession session, string computedEndpoint)
                        {
                            var endpointConfiguration = new EndpointConfiguration("SalesEndpoint");
                            endpointConfiguration.UseTransport<AzureServiceBusTransport>();
                            endpointConfiguration.Recoverability().ErrorQueue("error");
                            var computedConfiguration = new EndpointConfiguration(computedEndpoint);
                            await session.Send(new SubmitOrder());
                            await session.Publish(new OrderSubmitted());
                            await session.Subscribe<OrderSubmitted>();
                        }
                    }

                    public sealed class OrderSubmittedHandler : IHandleMessages<OrderSubmitted>
                    {
                        public Task Handle(OrderSubmitted message) => Task.CompletedTask;
                    }

                    public sealed class BillingSaga : Saga<BillingSagaData>, IHandleMessages<OrderSubmitted>
                    {
                        public Task Handle(OrderSubmitted message) => Task.CompletedTask;
                    }

                    public sealed class RabbitWorkflow
                    {
                        public void PublishAndConsume(IConnection connection)
                        {
                            var model = connection.CreateModel();
                            model.QueueDeclare("billing-queue", durable: true, exclusive: false, autoDelete: false, arguments: null);
                            model.ExchangeDeclare("billing-exchange", "topic");
                            model.BasicPublish("billing-exchange", "billing.created", basicProperties: null, Encoding.UTF8.GetBytes("{}"));
                            model.BasicConsume("billing-queue", autoAck: false, consumer: this);
                        }

                        public Task HandleDelivery() => Task.CompletedTask;
                    }

                    public sealed class MsmqWorkflow
                    {
                        public void SendAndReceive()
                        {
                            var queue = new MessageQueue(@".\\private$\\orders");
                            queue.Send(new SubmitOrder());
                            queue.Receive();
                        }
                    }

                    public interface IQueuePublisher
                    {
                        Task PublishAsync(string queueName, object message);
                    }

                    public sealed class AbstractionWorkflow
                    {
                        public Task PublishAsync(IQueuePublisher publisher) => publisher.PublishAsync("audit-events", new OrderSubmitted());
                    }
                }
                """;
            File.WriteAllText(sourcePath, source);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Sample.Messaging",
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Task).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            SemanticExtractionRequest semanticRequest = new(repositoryRoot, "src/Sample.Messaging/Sample.Messaging.csproj", sourcePath, syntaxTree, semanticModel);
            MessagingIntegrationExtractor extractor = new();

            return extractor.Extract(new MessagingIntegrationExtractionRequest(StableKeyGenerator.ForRepository("Sample.Messaging"), repositoryRoot, [semanticRequest]));
        }
    }
}
