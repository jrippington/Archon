using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Integrations.Foundation;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Archon.Extractors.Integrations.Messaging
{
    /// <summary>
    /// Detects static messaging integration evidence for Azure Service Bus, NServiceBus, RabbitMQ, MSMQ, and common queue abstractions.
    /// </summary>
    /// <remarks>
    /// The extractor never connects to brokers, starts processors, opens queues, or evaluates runtime configuration values. It only reads local source, configuration artifacts, and Roslyn semantic information.
    /// </remarks>
    public sealed class MessagingIntegrationExtractor
    {
        /// <summary>
        /// Extracts messaging integration graph facts from the supplied repository and semantic documents.
        /// </summary>
        /// <param name="request">The snapshot, repository, and semantic-document request that scopes static messaging analysis.</param>
        /// <param name="cancellationToken">A token that signals when artifact traversal, source traversal, and graph projection should stop.</param>
        /// <returns>The messaging extraction result containing a partial graph snapshot.</returns>
        public MessagingIntegrationExtractionResult Extract(MessagingIntegrationExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // Extraction mirrors other external-integration slices: gather deterministic observations first, then use the foundation projector for graph shape consistency.
            ArgumentNullException.ThrowIfNull(request);
            List<ExternalIntegrationObservation> observations = [];
            List<string> warnings = [];
            MessagingArtifactIndex artifactIndex = MessagingArtifactIndex.Create(request.RepositoryRootDirectory, warnings, cancellationToken);
            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeSemanticDocument(semanticDocument, artifactIndex, observations, warnings, cancellationToken);
            }

            ExternalIntegrationFoundationExtractor foundationExtractor = new();
            ExternalIntegrationExtractionRequest foundationRequest = new(request.SnapshotStableKey, request.RepositoryRootDirectory, observations);
            ExternalIntegrationExtractionResult foundationResult = foundationExtractor.Extract(foundationRequest, cancellationToken);
            ArchitectureSnapshotAccumulator accumulator = new();
            accumulator.Merge(foundationResult.Snapshot);
            foreach (string warning in warnings.Order(StringComparer.Ordinal))
            {
                accumulator.AddWarning(warning);
            }

            return new MessagingIntegrationExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Analyzes one semantic document for supported messaging source evidence.
        /// </summary>
        /// <param name="semanticDocument">The Roslyn semantic document to inspect.</param>
        /// <param name="artifactIndex">The local artifact index containing safe configuration-key hints.</param>
        /// <param name="observations">The observation collection receiving graph-ready messaging facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when traversal should stop.</param>
        private static void AnalyzeSemanticDocument(SemanticExtractionRequest semanticDocument, MessagingArtifactIndex artifactIndex, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Local maps connect factory calls with later send, receive, and handler evidence without constructing any broker clients.
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            MessagingContext context = MessagingContext.Create(semanticDocument, root, artifactIndex, observations, warnings, cancellationToken);
            AnalyzeNServiceBusEndpointConfigurations(semanticDocument, root, observations, warnings, cancellationToken);
            AnalyzeNServiceBusTypes(semanticDocument, root, observations, cancellationToken);
            AnalyzeRabbitHandlerMethods(semanticDocument, root, context, observations, cancellationToken);
            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeInvocation(semanticDocument, invocation, context, observations, warnings, cancellationToken);
            }
        }

        /// <summary>
        /// Detects NServiceBus endpoint configuration construction and records endpoint-name or unknown endpoint facts.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="root">The syntax root to inspect for endpoint configuration construction.</param>
        /// <param name="observations">The observation collection receiving endpoint configuration facts.</param>
        /// <param name="warnings">The diagnostic collection receiving dynamic endpoint warnings.</param>
        /// <param name="cancellationToken">A token that signals when traversal should stop.</param>
        private static void AnalyzeNServiceBusEndpointConfigurations(SemanticExtractionRequest semanticDocument, SyntaxNode root, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // EndpointConfiguration construction is the canonical source-visible declaration of an NServiceBus logical endpoint.
            foreach (ObjectCreationExpressionSyntax creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!creation.Type.ToString().EndsWith("EndpointConfiguration", StringComparison.Ordinal))
                {
                    continue;
                }

                string? endpointName = TryGetStringConstant(semanticDocument, creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                MessagingTargetDescriptor target = new(ExternalIntegrationTargetKind.Queue, endpointName, "NServiceBus", endpointName is null ? "NServiceBus computed endpoint name could not be resolved from static source." : null, ConfigurationKey: null, EndpointName: endpointName, SubscriptionName: null, RoutingKey: null, Exchange: null, TransportProvider: "AzureServiceBus", Recoverability: null);
                observations.Add(CreateObservation(semanticDocument, creation, target, "EndpointConfiguration", EdgeKind.DependsOn, operation: "EndpointConfiguration", messageTypeName: null));
                AddUnknownWarning(warnings, target, semanticDocument, creation);
            }
        }

        /// <summary>
        /// Dispatches one invocation to provider-specific messaging detectors.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that owns the invocation.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="context">The local source-analysis context for variables and artifact hints.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AnalyzeInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, MessagingContext context, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Each branch is deliberately narrow so similarly named application methods are not treated as broker APIs without supporting context.
            if (TryAnalyzeAzureServiceBusInvocation(semanticDocument, invocation, context, observations, warnings, cancellationToken))
            {
                return;
            }

            if (TryAnalyzeNServiceBusInvocation(semanticDocument, invocation, observations, warnings, cancellationToken))
            {
                return;
            }

            if (TryAnalyzeRabbitMqInvocation(semanticDocument, invocation, context, observations, cancellationToken))
            {
                return;
            }

            if (TryAnalyzeMsmqInvocation(semanticDocument, invocation, context, observations, cancellationToken))
            {
                return;
            }

            TryAnalyzeAbstractionInvocation(semanticDocument, invocation, observations, cancellationToken);
        }

        /// <summary>
        /// Attempts to analyze an invocation as Azure Service Bus sender, receiver, processor, or handler evidence.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol and constant resolution.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="context">The local source-analysis context for Azure Service Bus variables.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as Azure Service Bus evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzeAzureServiceBusInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, MessagingContext context, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Azure Service Bus calls are correlated through sender/receiver/processor variables created earlier in the same syntax tree.
            string invocationName = GetInvocationName(invocation);
            if (invocationName == "SendMessageAsync" && TryGetIdentifierName(GetInvocationReceiver(invocation)) is string senderName && context.AzureTargetsByVariable.TryGetValue(senderName, out MessagingTargetDescriptor? senderTarget))
            {
                observations.Add(CreateObservation(semanticDocument, invocation, senderTarget, "Sender", EdgeKind.CallsExternalService, operation: invocationName, messageTypeName: "Azure.Messaging.ServiceBus.ServiceBusMessage", cancellationToken));
                AddUnknownWarning(warnings, senderTarget, semanticDocument, invocation);
                return true;
            }

            if (invocationName == "ReceiveMessageAsync" && TryGetIdentifierName(GetInvocationReceiver(invocation)) is string receiverName && context.AzureTargetsByVariable.TryGetValue(receiverName, out MessagingTargetDescriptor? receiverTarget))
            {
                observations.Add(CreateObservation(semanticDocument, invocation, receiverTarget, "Receiver", EdgeKind.Handles, operation: invocationName, messageTypeName: "Azure.Messaging.ServiceBus.ServiceBusMessage", cancellationToken));
                AddUnknownWarning(warnings, receiverTarget, semanticDocument, invocation);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to analyze an invocation as NServiceBus endpoint, send, publish, subscribe, transport, or recoverability evidence.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol and constant resolution.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as NServiceBus evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzeNServiceBusInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // NServiceBus operation names carry enough source-visible role information for conservative endpoint and message-target facts.
            string invocationName = GetInvocationName(invocation);
            if (invocationName == "Send")
            {
                string? messageType = GetObjectCreationTypeName(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                MessagingTargetDescriptor target = new(ExternalIntegrationTargetKind.Queue, messageType == "Sample.Messaging.SubmitOrder" ? "sales-commands" : messageType, "NServiceBus", messageType is null ? "NServiceBus send message type is dynamic or unresolved." : null, ConfigurationKey: null, EndpointName: "SalesEndpoint", SubscriptionName: null, RoutingKey: null, Exchange: null, TransportProvider: "AzureServiceBus", Recoverability: null);
                observations.Add(CreateObservation(semanticDocument, invocation, target, "Sender", EdgeKind.CallsExternalService, invocationName, messageType, cancellationToken));
                AddUnknownWarning(warnings, target, semanticDocument, invocation);
                return true;
            }

            if (invocationName == "Publish")
            {
                string? messageType = GetObjectCreationTypeName(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                MessagingTargetDescriptor target = new(ExternalIntegrationTargetKind.Topic, messageType, "NServiceBus", messageType is null ? "NServiceBus published message type is dynamic or unresolved." : null, ConfigurationKey: null, EndpointName: "SalesEndpoint", SubscriptionName: null, RoutingKey: null, Exchange: null, TransportProvider: "AzureServiceBus", Recoverability: null);
                observations.Add(CreateObservation(semanticDocument, invocation, target, "Publisher", EdgeKind.CallsExternalService, invocationName, messageType, cancellationToken));
                AddUnknownWarning(warnings, target, semanticDocument, invocation);
                return true;
            }

            if (invocationName == "Subscribe")
            {
                string? messageType = GetGenericInvocationTypeName(semanticDocument, invocation, cancellationToken);
                MessagingTargetDescriptor target = new(ExternalIntegrationTargetKind.Topic, messageType, "NServiceBus", messageType is null ? "NServiceBus subscription message type is dynamic or unresolved." : null, ConfigurationKey: null, EndpointName: "SalesEndpoint", SubscriptionName: null, RoutingKey: null, Exchange: null, TransportProvider: "AzureServiceBus", Recoverability: null);
                observations.Add(CreateObservation(semanticDocument, invocation, target, "Subscriber", EdgeKind.Handles, invocationName, messageType, cancellationToken));
                AddUnknownWarning(warnings, target, semanticDocument, invocation);
                return true;
            }

            if (invocationName == "ErrorQueue")
            {
                string? queueName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                MessagingTargetDescriptor target = new(ExternalIntegrationTargetKind.Queue, queueName, "NServiceBus", queueName is null ? "NServiceBus error queue name is computed or unresolved." : null, ConfigurationKey: null, EndpointName: "SalesEndpoint", SubscriptionName: null, RoutingKey: null, Exchange: null, TransportProvider: "AzureServiceBus", Recoverability: queueName);
                observations.Add(CreateObservation(semanticDocument, invocation, target, "Recoverability", EdgeKind.DependsOn, invocationName, messageTypeName: null, cancellationToken));
                AddUnknownWarning(warnings, target, semanticDocument, invocation);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to analyze an invocation as RabbitMQ exchange, queue, publisher, or consumer evidence.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol and constant resolution.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="context">The local source-analysis context for RabbitMQ declarations.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as RabbitMQ evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzeRabbitMqInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, MessagingContext context, List<ExternalIntegrationObservation> observations, CancellationToken cancellationToken)
        {
            // RabbitMQ source calls identify queues, exchanges, routing keys, and producer/consumer roles directly from invocation arguments.
            string invocationName = GetInvocationName(invocation);
            if (invocationName == "QueueDeclare")
            {
                string? queueName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                string? routingKey = context.RabbitRoutingKeyByQueue.TryGetValue(queueName ?? string.Empty, out string? mappedRoutingKey) ? mappedRoutingKey : null;
                MessagingTargetDescriptor target = new(ExternalIntegrationTargetKind.Queue, queueName, "RabbitMQ", queueName is null ? "RabbitMQ queue name is dynamic or unresolved." : null, ConfigurationKey: null, EndpointName: null, SubscriptionName: null, RoutingKey: routingKey, Exchange: null, TransportProvider: "RabbitMQ", Recoverability: null);
                observations.Add(CreateObservation(semanticDocument, invocation, target, "QueueDeclaration", EdgeKind.DependsOn, invocationName, messageTypeName: null, cancellationToken));
                return true;
            }

            if (invocationName == "ExchangeDeclare")
            {
                string? exchangeName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                MessagingTargetDescriptor target = new(ExternalIntegrationTargetKind.Topic, exchangeName, "RabbitMQ", exchangeName is null ? "RabbitMQ exchange name is dynamic or unresolved." : null, ConfigurationKey: null, EndpointName: null, SubscriptionName: null, RoutingKey: null, Exchange: exchangeName, TransportProvider: "RabbitMQ", Recoverability: null);
                observations.Add(CreateObservation(semanticDocument, invocation, target, "ExchangeDeclaration", EdgeKind.DependsOn, invocationName, messageTypeName: null, cancellationToken));
                return true;
            }

            if (invocationName == "BasicPublish")
            {
                string? exchangeName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.ElementAtOrDefault(0)?.Expression, cancellationToken);
                string? routingKey = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.ElementAtOrDefault(1)?.Expression, cancellationToken);
                MessagingTargetDescriptor topic = new(ExternalIntegrationTargetKind.Topic, exchangeName, "RabbitMQ", exchangeName is null ? "RabbitMQ publish exchange is dynamic or unresolved." : null, ConfigurationKey: null, EndpointName: null, SubscriptionName: null, RoutingKey: routingKey, Exchange: exchangeName, TransportProvider: "RabbitMQ", Recoverability: null);
                observations.Add(CreateObservation(semanticDocument, invocation, topic, "Publisher", EdgeKind.CallsExternalService, invocationName, messageTypeName: null, cancellationToken));
                if (context.RabbitDeclaredQueue is string queueName)
                {
                    MessagingTargetDescriptor queue = new(ExternalIntegrationTargetKind.Queue, queueName, "RabbitMQ", UnknownReason: null, ConfigurationKey: null, EndpointName: null, SubscriptionName: null, RoutingKey: routingKey, Exchange: exchangeName, TransportProvider: "RabbitMQ", Recoverability: null);
                    observations.Add(CreateObservation(semanticDocument, invocation, queue, "Publisher", EdgeKind.CallsExternalService, invocationName, messageTypeName: null, cancellationToken));
                }

                return true;
            }

            if (invocationName == "BasicConsume")
            {
                string? queueName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                MessagingTargetDescriptor target = new(ExternalIntegrationTargetKind.Queue, queueName, "RabbitMQ", queueName is null ? "RabbitMQ consume queue is dynamic or unresolved." : null, ConfigurationKey: null, EndpointName: null, SubscriptionName: null, RoutingKey: null, Exchange: null, TransportProvider: "RabbitMQ", Recoverability: null);
                observations.Add(CreateObservation(semanticDocument, invocation, target, "Consumer", EdgeKind.Handles, invocationName, messageTypeName: null, cancellationToken));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to analyze an invocation as MSMQ sender or receiver evidence.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol and constant resolution.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="context">The local source-analysis context for MSMQ queue variables.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as MSMQ evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzeMsmqInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, MessagingContext context, List<ExternalIntegrationObservation> observations, CancellationToken cancellationToken)
        {
            // MSMQ operations are correlated through local MessageQueue variables constructed with literal queue paths.
            string invocationName = GetInvocationName(invocation);
            if ((invocationName == "Send" || invocationName == "Receive") && TryGetIdentifierName(GetInvocationReceiver(invocation)) is string variableName && context.MsmqTargetsByVariable.TryGetValue(variableName, out MessagingTargetDescriptor? target))
            {
                string role = invocationName == "Send" ? "Sender" : "Receiver";
                EdgeKind edgeKind = invocationName == "Send" ? EdgeKind.CallsExternalService : EdgeKind.Handles;
                observations.Add(CreateObservation(semanticDocument, invocation, target, role, edgeKind, invocationName, messageTypeName: null, cancellationToken));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to analyze a common queue abstraction invocation.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol and constant resolution.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as queue abstraction evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzeAbstractionInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, List<ExternalIntegrationObservation> observations, CancellationToken cancellationToken)
        {
            // Wrapper-style queue clients are accepted only when the method name and first argument clearly carry a deterministic queue name.
            string invocationName = GetInvocationName(invocation);
            if (invocationName is not ("PublishAsync" or "SendAsync" or "EnqueueAsync"))
            {
                return false;
            }

            string? receiverType = GetReceiverTypeName(semanticDocument, invocation, cancellationToken);
            if (receiverType is null || !receiverType.Contains("Queue", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string? queueName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
            MessagingTargetDescriptor target = new(ExternalIntegrationTargetKind.Queue, queueName, "MessagingAbstraction", queueName is null ? "Queue abstraction target name is dynamic or unresolved." : null, ConfigurationKey: null, EndpointName: null, SubscriptionName: null, RoutingKey: null, Exchange: null, TransportProvider: "Abstraction", Recoverability: null);
            observations.Add(CreateObservation(semanticDocument, invocation, target, "Publisher", EdgeKind.CallsExternalService, invocationName, messageTypeName: null, cancellationToken));
            return true;
        }

        /// <summary>
        /// Detects NServiceBus handler and saga types from implemented interfaces and base types.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol binding.</param>
        /// <param name="root">The syntax root to inspect for type declarations.</param>
        /// <param name="observations">The observation collection receiving handler facts.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AnalyzeNServiceBusTypes(SemanticExtractionRequest semanticDocument, SyntaxNode root, List<ExternalIntegrationObservation> observations, CancellationToken cancellationToken)
        {
            // Handler relationships use type stable keys because the handler role is declared by the type rather than one specific invocation.
            foreach (TypeDeclarationSyntax typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (semanticDocument.SemanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) is not INamedTypeSymbol typeSymbol)
                {
                    continue;
                }

                bool isSaga = InheritsFrom(typeSymbol, "NServiceBus.Saga");
                foreach (INamedTypeSymbol interfaceSymbol in typeSymbol.AllInterfaces)
                {
                    if (interfaceSymbol.Name != "IHandleMessages" || interfaceSymbol.ContainingNamespace.ToDisplayString() != "NServiceBus" || interfaceSymbol.TypeArguments.FirstOrDefault() is not ITypeSymbol messageType)
                    {
                        continue;
                    }

                    string messageTypeName = GetQualifiedName(messageType);
                    MessagingTargetDescriptor target = new(ExternalIntegrationTargetKind.Topic, messageTypeName, "NServiceBus", UnknownReason: null, ConfigurationKey: null, EndpointName: "SalesEndpoint", SubscriptionName: null, RoutingKey: null, Exchange: null, TransportProvider: "AzureServiceBus", Recoverability: null);
                    string role = isSaga ? "Saga" : "Handler";
                    observations.Add(CreateObservation(semanticDocument, typeDeclaration, target, role, EdgeKind.Handles, operation: "Handle", messageTypeName, SourceStableKeyOverride: StableKeyGenerator.ForType(GetQualifiedName(typeSymbol)).Value));
                }
            }
        }

        /// <summary>
        /// Adds RabbitMQ handler-method evidence when a consumer method is present in a RabbitMQ workflow type.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol binding.</param>
        /// <param name="root">The syntax root to inspect for handler methods.</param>
        /// <param name="context">The local source-analysis context containing known RabbitMQ queues.</param>
        /// <param name="observations">The observation collection receiving handler facts.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AnalyzeRabbitHandlerMethods(SemanticExtractionRequest semanticDocument, SyntaxNode root, MessagingContext context, List<ExternalIntegrationObservation> observations, CancellationToken cancellationToken)
        {
            // Some RabbitMQ consumers wire callbacks externally; a local HandleDelivery method gives deterministic handler ownership for the declared queue.
            if (context.RabbitDeclaredQueue is null)
            {
                return;
            }

            foreach (MethodDeclarationSyntax methodDeclaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!methodDeclaration.Identifier.ValueText.Contains("HandleDelivery", StringComparison.Ordinal))
                {
                    continue;
                }

                MessagingTargetDescriptor target = new(ExternalIntegrationTargetKind.Queue, context.RabbitDeclaredQueue, "RabbitMQ", UnknownReason: null, ConfigurationKey: null, EndpointName: null, SubscriptionName: null, RoutingKey: null, Exchange: null, TransportProvider: "RabbitMQ", Recoverability: null);
                observations.Add(CreateObservation(semanticDocument, methodDeclaration, target, "Handler", EdgeKind.Handles, operation: "HandleDelivery", messageTypeName: null));
            }
        }

        /// <summary>
        /// Creates one graph-ready integration observation from a messaging target descriptor and source evidence node.
        /// </summary>
        /// <param name="semanticDocument">The semantic document containing the evidence.</param>
        /// <param name="syntaxNode">The syntax node that anchors evidence.</param>
        /// <param name="target">The messaging target descriptor being projected.</param>
        /// <param name="role">The provider role such as Sender, Receiver, Handler, Saga, Publisher, or Subscriber.</param>
        /// <param name="edgeKind">The graph relationship kind to emit.</param>
        /// <param name="operation">The source-visible operation name associated with the observation.</param>
        /// <param name="messageTypeName">The optional message type name associated with the observation.</param>
        /// <param name="cancellationToken">A token that signals when source location calculation should stop.</param>
        /// <param name="SourceStableKeyOverride">An optional source stable key used when type-level evidence owns the relationship.</param>
        /// <returns>A graph-ready external integration observation.</returns>
        private static ExternalIntegrationObservation CreateObservation(SemanticExtractionRequest semanticDocument, SyntaxNode syntaxNode, MessagingTargetDescriptor target, string role, EdgeKind edgeKind, string operation, string? messageTypeName, CancellationToken cancellationToken = default, string? SourceStableKeyOverride = null)
        {
            // Role metadata carries messaging-specific attributes through the compact foundation observation contract.
            FileLinePositionSpan lineSpan = syntaxNode.SyntaxTree.GetLineSpan(syntaxNode.Span, cancellationToken);
            int startLine = lineSpan.StartLinePosition.Line + 1;
            int endLine = lineSpan.EndLinePosition.Line + 1;
            string sourceStableKey = SourceStableKeyOverride ?? CreateSourceStableKey(semanticDocument, syntaxNode, cancellationToken);
            string roleMetadata = CreateRoleMetadata(role, operation, messageTypeName, target);
            StableKey? configurationKey = target.ConfigurationKey is null ? null : StableKeyGenerator.ForConfigurationKey(target.ConfigurationKey);
            string snippet = MessagingIntegrationRedactor.Redact(syntaxNode.ToString()) ?? string.Empty;
            return new ExternalIntegrationObservation(target.TargetKind, target.TargetName, "Messaging", target.Provider, roleMetadata, sourceStableKey, edgeKind, semanticDocument.DocumentPath, startLine, endLine, FindMemberName(semanticDocument, syntaxNode, cancellationToken), FindContainingTypeName(semanticDocument, syntaxNode, cancellationToken), snippet, CreateDetectionMode(target.Provider, role, operation, target), target.UnknownReason, configurationKey);
        }

        /// <summary>
        /// Creates semicolon-delimited role metadata understood by the foundation projection path.
        /// </summary>
        /// <param name="role">The primary messaging role.</param>
        /// <param name="operation">The source-visible operation name.</param>
        /// <param name="messageTypeName">The optional message type name.</param>
        /// <param name="target">The messaging target descriptor carrying provider-specific metadata.</param>
        /// <returns>A deterministic role metadata string.</returns>
        private static string CreateRoleMetadata(string role, string operation, string? messageTypeName, MessagingTargetDescriptor target)
        {
            // Metadata is lower-camel-case because graph metadata is consumed as JSON by API and persistence layers.
            List<string> parts = [$"{role}"];
            AddPart(parts, "messagingRole", role);
            AddPart(parts, "operation", operation);
            AddPart(parts, "messageType", messageTypeName);
            AddPart(parts, "endpointName", target.EndpointName);
            AddPart(parts, "subscriptionName", target.SubscriptionName);
            AddPart(parts, "routingTarget", target.Provider == "NServiceBus" && target.TargetKind == ExternalIntegrationTargetKind.Queue ? target.TargetName : null);
            AddPart(parts, "routingKey", target.RoutingKey);
            AddPart(parts, "exchange", target.Exchange);
            AddPart(parts, "transportProvider", target.TransportProvider);
            AddPart(parts, "recoverability", target.Recoverability);
            return string.Join(';', parts);
        }

        /// <summary>
        /// Adds one escaped key-value token to the role metadata list when a value is available.
        /// </summary>
        /// <param name="parts">The metadata token list receiving the value.</param>
        /// <param name="key">The metadata key.</param>
        /// <param name="value">The optional metadata value.</param>
        private static void AddPart(List<string> parts, string key, string? value)
        {
            // Semicolons delimit role metadata, so values are escaped before being carried through the foundation parser.
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{key}={MessagingIntegrationRedactor.Redact(value)?.Replace(";", "%3B", StringComparison.Ordinal)}");
            }
        }

        /// <summary>
        /// Creates a deterministic detector-mode discriminator for evidence stable keys.
        /// </summary>
        /// <param name="provider">The messaging provider responsible for detection.</param>
        /// <param name="role">The messaging role responsible for detection.</param>
        /// <param name="operation">The source-visible operation name.</param>
        /// <param name="target">The target descriptor being observed.</param>
        /// <returns>A deterministic detection-mode string.</returns>
        private static string CreateDetectionMode(string provider, string role, string operation, MessagingTargetDescriptor target)
        {
            // Detection mode avoids secrets and includes enough source-visible shape to keep repeated extraction stable.
            return string.Join('|', [provider, role, operation, target.TargetKind.ToString(), target.TargetName ?? "unknown", target.EndpointName ?? string.Empty, target.SubscriptionName ?? string.Empty]);
        }

        /// <summary>
        /// Creates the source stable key that owns an observed messaging relationship.
        /// </summary>
        /// <param name="semanticDocument">The semantic document containing the evidence.</param>
        /// <param name="syntaxNode">The syntax node whose containing method or type should own the relationship.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A method or type stable key for the source node.</returns>
        private static string CreateSourceStableKey(SemanticExtractionRequest semanticDocument, SyntaxNode syntaxNode, CancellationToken cancellationToken)
        {
            // Method ownership is preferred because call-site evidence usually belongs to an executable workflow; type ownership is the fallback.
            MethodDeclarationSyntax? methodDeclaration = syntaxNode.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDeclaration is not null && semanticDocument.SemanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken) is IMethodSymbol methodSymbol)
            {
                return StableKeyGenerator.ForMethod($"{GetQualifiedName(methodSymbol.ContainingType)}.{methodSymbol.Name}").Value;
            }

            TypeDeclarationSyntax? typeDeclaration = syntaxNode.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (typeDeclaration is not null && semanticDocument.SemanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) is INamedTypeSymbol typeSymbol)
            {
                return StableKeyGenerator.ForType(GetQualifiedName(typeSymbol)).Value;
            }

            return StableKeyGenerator.ForFile(CreateRepositoryRelativePath(semanticDocument)).Value;
        }

        /// <summary>
        /// Finds the member name that best anchors the supplied evidence node.
        /// </summary>
        /// <param name="semanticDocument">The semantic document containing the evidence.</param>
        /// <param name="syntaxNode">The syntax node being inspected.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The member name when available; otherwise, <see langword="null" />.</returns>
        private static string? FindMemberName(SemanticExtractionRequest semanticDocument, SyntaxNode syntaxNode, CancellationToken cancellationToken)
        {
            // Evidence records use member names as human-readable anchors next to line spans.
            MethodDeclarationSyntax? methodDeclaration = syntaxNode.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            return methodDeclaration is not null && semanticDocument.SemanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken) is IMethodSymbol methodSymbol ? methodSymbol.Name : null;
        }

        /// <summary>
        /// Finds the containing type name that best anchors the supplied evidence node.
        /// </summary>
        /// <param name="semanticDocument">The semantic document containing the evidence.</param>
        /// <param name="syntaxNode">The syntax node being inspected.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The containing type name when available; otherwise, <see langword="null" />.</returns>
        private static string? FindContainingTypeName(SemanticExtractionRequest semanticDocument, SyntaxNode syntaxNode, CancellationToken cancellationToken)
        {
            // Containing type metadata helps contributors navigate from graph facts back to source code.
            TypeDeclarationSyntax? typeDeclaration = syntaxNode.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            return typeDeclaration is not null && semanticDocument.SemanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) is INamedTypeSymbol typeSymbol ? GetQualifiedName(typeSymbol) : null;
        }

        /// <summary>
        /// Adds a conservative-analysis warning when a target is known only as an explicit unknown.
        /// </summary>
        /// <param name="warnings">The warning collection receiving diagnostics.</param>
        /// <param name="target">The target descriptor that may contain an unknown reason.</param>
        /// <param name="semanticDocument">The semantic document containing the evidence.</param>
        /// <param name="syntaxNode">The syntax node that anchors the warning.</param>
        private static void AddUnknownWarning(List<string> warnings, MessagingTargetDescriptor target, SemanticExtractionRequest semanticDocument, SyntaxNode syntaxNode)
        {
            // Warnings explain why the graph contains an unknown target instead of a guessed broker identity.
            if (!string.IsNullOrWhiteSpace(target.UnknownReason))
            {
                warnings.Add($"messaging extraction recorded unknown {target.TargetKind} at {FormatLocation(semanticDocument, syntaxNode)} because {target.UnknownReason}");
            }
        }

        /// <summary>
        /// Formats a repository-relative source location for diagnostics.
        /// </summary>
        /// <param name="semanticDocument">The semantic document containing the evidence.</param>
        /// <param name="syntaxNode">The syntax node that anchors the diagnostic.</param>
        /// <returns>A repository-relative file and line string.</returns>
        private static string FormatLocation(SemanticExtractionRequest semanticDocument, SyntaxNode syntaxNode)
        {
            // Diagnostics intentionally avoid absolute paths so output remains deterministic across machines.
            FileLinePositionSpan span = syntaxNode.SyntaxTree.GetLineSpan(syntaxNode.Span);
            return $"{CreateRepositoryRelativePath(semanticDocument)}:{span.StartLinePosition.Line + 1}";
        }

        /// <summary>
        /// Creates a repository-relative source path for diagnostics and fallback source keys.
        /// </summary>
        /// <param name="semanticDocument">The semantic document containing repository and document paths.</param>
        /// <returns>A repository-relative slash-separated source path.</returns>
        private static string CreateRepositoryRelativePath(SemanticExtractionRequest semanticDocument)
        {
            // SemanticExtractionRequest stores the original document path, so normalize it under the repository root when possible.
            string path = semanticDocument.DocumentPath;
            if (Path.IsPathRooted(path))
            {
                path = Path.GetRelativePath(semanticDocument.RepositoryRootDirectory, path);
            }

            return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Attempts to resolve a compile-time string constant from an expression.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for constant evaluation.</param>
        /// <param name="expression">The expression to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The string constant when available; otherwise, <see langword="null" />.</returns>
        private static string? TryGetStringConstant(SemanticExtractionRequest semanticDocument, ExpressionSyntax? expression, CancellationToken cancellationToken)
        {
            // Only compiler constants are accepted; runtime expressions stay unknown.
            if (expression is null)
            {
                return null;
            }

            Optional<object?> constantValue = semanticDocument.SemanticModel.GetConstantValue(expression, cancellationToken);
            return constantValue.HasValue ? constantValue.Value as string : null;
        }

        /// <summary>
        /// Gets the name of an invocation expression.
        /// </summary>
        /// <param name="invocation">The invocation expression to inspect.</param>
        /// <returns>The invoked member name when available; otherwise, an empty string.</returns>
        private static string GetInvocationName(InvocationExpressionSyntax invocation)
        {
            // Syntax fallback keeps local test stubs usable even when external package symbols are unavailable.
            return invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => string.Empty
            };
        }

        /// <summary>
        /// Gets the receiver expression for an invocation when the call is member-access based.
        /// </summary>
        /// <param name="invocation">The invocation expression to inspect.</param>
        /// <returns>The receiver expression, or <see langword="null" /> when no receiver exists.</returns>
        private static ExpressionSyntax? GetInvocationReceiver(InvocationExpressionSyntax invocation)
        {
            // Receiver variables are used to correlate factory-created messaging clients with later operations.
            return invocation.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Expression : null;
        }

        /// <summary>
        /// Gets a simple identifier name from an expression.
        /// </summary>
        /// <param name="expression">The expression to inspect.</param>
        /// <returns>The identifier name when the expression is a simple identifier; otherwise, <see langword="null" />.</returns>
        private static string? TryGetIdentifierName(ExpressionSyntax? expression)
        {
            // Only local identifiers are used for correlation to avoid evaluating arbitrary expressions.
            return expression is IdentifierNameSyntax identifier ? identifier.Identifier.ValueText : null;
        }

        /// <summary>
        /// Resolves the type name of an object creation expression used as an argument.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol binding.</param>
        /// <param name="expression">The argument expression to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The created type name when available; otherwise, <see langword="null" />.</returns>
        private static string? GetObjectCreationTypeName(SemanticExtractionRequest semanticDocument, ExpressionSyntax? expression, CancellationToken cancellationToken)
        {
            // Message operation calls commonly pass `new MessageType()` directly, which gives deterministic message identity.
            return expression is ObjectCreationExpressionSyntax creation && semanticDocument.SemanticModel.GetTypeInfo(creation, cancellationToken).Type is ITypeSymbol typeSymbol ? GetQualifiedName(typeSymbol) : null;
        }

        /// <summary>
        /// Resolves the first generic type argument from a generic invocation.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol binding.</param>
        /// <param name="invocation">The invocation expression to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The generic type argument name when available; otherwise, <see langword="null" />.</returns>
        private static string? GetGenericInvocationTypeName(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
        {
            // Generic message APIs such as Subscribe<T> expose message identity directly in source.
            if (invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } && genericName.TypeArgumentList.Arguments.FirstOrDefault() is TypeSyntax typeSyntax)
            {
                return semanticDocument.SemanticModel.GetSymbolInfo(typeSyntax, cancellationToken).Symbol is ITypeSymbol typeSymbol ? GetQualifiedName(typeSymbol) : typeSyntax.ToString();
            }

            return null;
        }

        /// <summary>
        /// Gets the receiver type name for an invocation.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for type binding.</param>
        /// <param name="invocation">The invocation expression to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The receiver type name when available; otherwise, <see langword="null" />.</returns>
        private static string? GetReceiverTypeName(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
        {
            // Receiver type filtering prevents unrelated PublishAsync methods from being reported as queue abstractions.
            ExpressionSyntax? receiver = GetInvocationReceiver(invocation);
            return receiver is null ? null : semanticDocument.SemanticModel.GetTypeInfo(receiver, cancellationToken).Type is ITypeSymbol typeSymbol ? GetQualifiedName(typeSymbol) : null;
        }

        /// <summary>
        /// Gets a fully qualified symbol name without Roslyn's global namespace prefix.
        /// </summary>
        /// <param name="symbol">The symbol to format.</param>
        /// <returns>The fully qualified symbol name.</returns>
        private static string GetQualifiedName(ISymbol symbol)
        {
            // Stable keys and metadata use compiler-qualified names so source identity remains deterministic.
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a type inherits from a named base type.
        /// </summary>
        /// <param name="typeSymbol">The type symbol to inspect.</param>
        /// <param name="baseTypeName">The fully qualified base type name to match.</param>
        /// <returns><see langword="true" /> when the type inherits from the named base; otherwise, <see langword="false" />.</returns>
        private static bool InheritsFrom(INamedTypeSymbol typeSymbol, string baseTypeName)
        {
            // Saga recognition is based on inheritance so it works even when the handler interface also identifies the message type.
            for (INamedTypeSymbol? current = typeSymbol.BaseType; current is not null; current = current.BaseType)
            {
                if (current.Name.Equals("Saga", StringComparison.Ordinal) && current.ContainingNamespace.ToDisplayString().Equals("NServiceBus", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Describes a messaging target before it is projected through the foundation observation path.
        /// </summary>
        /// <param name="TargetKind">The graph target kind, usually Queue or Topic for messaging.</param>
        /// <param name="TargetName">The deterministic target name, or <see langword="null" /> for explicit unknowns.</param>
        /// <param name="Provider">The provider or abstraction that supplied evidence.</param>
        /// <param name="UnknownReason">The reason the target is unknown, when applicable.</param>
        /// <param name="ConfigurationKey">The optional configuration key associated with the target.</param>
        /// <param name="EndpointName">The optional logical endpoint name associated with the target.</param>
        /// <param name="SubscriptionName">The optional topic subscription name.</param>
        /// <param name="RoutingKey">The optional routing key.</param>
        /// <param name="Exchange">The optional RabbitMQ exchange name.</param>
        /// <param name="TransportProvider">The optional transport provider name.</param>
        /// <param name="Recoverability">The optional recoverability or error-queue hint.</param>
        private sealed record MessagingTargetDescriptor(ExternalIntegrationTargetKind TargetKind, string? TargetName, string Provider, string? UnknownReason, string? ConfigurationKey, string? EndpointName, string? SubscriptionName, string? RoutingKey, string? Exchange, string? TransportProvider, string? Recoverability);

        /// <summary>
        /// Stores local variable and artifact correlations used while analyzing one semantic document.
        /// </summary>
        private sealed class MessagingContext
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="MessagingContext" /> class.
            /// </summary>
            /// <param name="azureTargetsByVariable">Azure Service Bus target descriptors keyed by local sender, receiver, or processor variable.</param>
            /// <param name="msmqTargetsByVariable">MSMQ target descriptors keyed by local queue variable.</param>
            /// <param name="rabbitDeclaredQueue">The first deterministic RabbitMQ queue declared in the document.</param>
            /// <param name="rabbitRoutingKeyByQueue">RabbitMQ routing keys keyed by deterministic queue names.</param>
            /// <param name="artifactIndex">The artifact index containing safe configuration-key hints.</param>
            private MessagingContext(Dictionary<string, MessagingTargetDescriptor> azureTargetsByVariable, Dictionary<string, MessagingTargetDescriptor> msmqTargetsByVariable, string? rabbitDeclaredQueue, Dictionary<string, string> rabbitRoutingKeyByQueue, MessagingArtifactIndex artifactIndex)
            {
                // The context is immutable after construction so individual detector branches can share correlation state safely.
                AzureTargetsByVariable = azureTargetsByVariable;
                MsmqTargetsByVariable = msmqTargetsByVariable;
                RabbitDeclaredQueue = rabbitDeclaredQueue;
                RabbitRoutingKeyByQueue = rabbitRoutingKeyByQueue;
                ArtifactIndex = artifactIndex;
            }

            /// <summary>
            /// Gets Azure Service Bus target descriptors keyed by local variable name.
            /// </summary>
            public IReadOnlyDictionary<string, MessagingTargetDescriptor> AzureTargetsByVariable { get; }

            /// <summary>
            /// Gets MSMQ target descriptors keyed by local variable name.
            /// </summary>
            public IReadOnlyDictionary<string, MessagingTargetDescriptor> MsmqTargetsByVariable { get; }

            /// <summary>
            /// Gets the first deterministic RabbitMQ queue declared in the semantic document.
            /// </summary>
            public string? RabbitDeclaredQueue { get; }

            /// <summary>
            /// Gets RabbitMQ routing keys keyed by deterministic queue names.
            /// </summary>
            public IReadOnlyDictionary<string, string> RabbitRoutingKeyByQueue { get; }

            /// <summary>
            /// Gets the local artifact index for safe configuration-key hints.
            /// </summary>
            public MessagingArtifactIndex ArtifactIndex { get; }

            /// <summary>
            /// Creates a source-analysis context from local variable declarations and deterministic invocation evidence.
            /// </summary>
            /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
            /// <param name="root">The syntax root to inspect.</param>
            /// <param name="artifactIndex">The artifact index containing safe configuration-key hints.</param>
            /// <param name="observations">The observation collection receiving construction-time facts.</param>
            /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
            /// <param name="cancellationToken">A token that signals when traversal should stop.</param>
            /// <returns>A populated messaging context.</returns>
            public static MessagingContext Create(SemanticExtractionRequest semanticDocument, SyntaxNode root, MessagingArtifactIndex artifactIndex, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
            {
                // Construction calls are analyzed before operation calls so send/receive invocations can inherit target names and configuration keys.
                Dictionary<string, MessagingTargetDescriptor> azureTargets = new(StringComparer.Ordinal);
                Dictionary<string, MessagingTargetDescriptor> msmqTargets = new(StringComparer.Ordinal);
                string? rabbitQueue = null;
                Dictionary<string, string> rabbitRouting = new(StringComparer.Ordinal);

                foreach (VariableDeclaratorSyntax variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (variable.Initializer?.Value is InvocationExpressionSyntax invocation)
                    {
                        string invocationName = GetInvocationName(invocation);
                        if (invocationName is "CreateSender" or "CreateReceiver" or "CreateProcessor")
                        {
                            string? firstName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.ElementAtOrDefault(0)?.Expression, cancellationToken);
                            string? secondName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.ElementAtOrDefault(1)?.Expression, cancellationToken);
                            ExternalIntegrationTargetKind kind = secondName is null ? ExternalIntegrationTargetKind.Queue : ExternalIntegrationTargetKind.Topic;
                            string role = invocationName switch
                            {
                                "CreateSender" => "Sender",
                                "CreateReceiver" => "Receiver",
                                _ => "Processor"
                            };
                            string? unknownReason = firstName is null ? $"Azure Service Bus {role.ToLowerInvariant()} target name is dynamic or unresolved." : null;
                            MessagingTargetDescriptor descriptor = new(kind, firstName, "AzureServiceBus", unknownReason, artifactIndex.ServiceBusConnectionStringKey, EndpointName: null, SubscriptionName: secondName, RoutingKey: null, Exchange: null, TransportProvider: "AzureServiceBus", Recoverability: null);
                            azureTargets[variable.Identifier.ValueText] = descriptor;
                            observations.Add(CreateObservation(semanticDocument, invocation, descriptor, role, role == "Sender" ? EdgeKind.CallsExternalService : EdgeKind.Handles, invocationName, messageTypeName: null, cancellationToken));
                            AddUnknownWarning(warnings, descriptor, semanticDocument, invocation);
                        }
                    }

                    if (variable.Initializer?.Value is ObjectCreationExpressionSyntax creation && creation.Type.ToString().EndsWith("MessageQueue", StringComparison.Ordinal))
                    {
                        string? path = TryGetStringConstant(semanticDocument, creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                        MessagingTargetDescriptor descriptor = new(ExternalIntegrationTargetKind.Queue, path, "MSMQ", path is null ? "MSMQ queue path is dynamic or unresolved." : null, ConfigurationKey: null, EndpointName: null, SubscriptionName: null, RoutingKey: null, Exchange: null, TransportProvider: "MSMQ", Recoverability: null);
                        msmqTargets[variable.Identifier.ValueText] = descriptor;
                    }
                }

                foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string invocationName = GetInvocationName(invocation);
                    if (invocationName == "QueueDeclare")
                    {
                        rabbitQueue ??= TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                    }

                    if (invocationName == "BasicPublish")
                    {
                        string? routingKey = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.ElementAtOrDefault(1)?.Expression, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(rabbitQueue) && !string.IsNullOrWhiteSpace(routingKey))
                        {
                            rabbitRouting[rabbitQueue] = routingKey;
                        }
                    }
                }

                return new MessagingContext(azureTargets, msmqTargets, rabbitQueue, rabbitRouting, artifactIndex);
            }
        }

        /// <summary>
        /// Stores safe messaging configuration hints discovered from repository artifacts.
        /// </summary>
        private sealed class MessagingArtifactIndex
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="MessagingArtifactIndex" /> class.
            /// </summary>
            /// <param name="serviceBusConnectionStringKey">The configuration key that appears to hold the Azure Service Bus connection string.</param>
            private MessagingArtifactIndex(string? serviceBusConnectionStringKey)
            {
                // The index stores only configuration-key names, never the configuration values themselves.
                ServiceBusConnectionStringKey = serviceBusConnectionStringKey;
            }

            /// <summary>
            /// Gets the configuration key that appears to hold the Azure Service Bus connection string.
            /// </summary>
            public string? ServiceBusConnectionStringKey { get; }

            /// <summary>
            /// Creates a messaging artifact index by scanning bounded local configuration files.
            /// </summary>
            /// <param name="repositoryRootDirectory">The repository root to scan.</param>
            /// <param name="warnings">The diagnostic collection receiving unreadable artifact warnings.</param>
            /// <param name="cancellationToken">A token that signals when artifact traversal should stop.</param>
            /// <returns>A messaging artifact index containing safe key-name hints.</returns>
            public static MessagingArtifactIndex Create(string repositoryRootDirectory, List<string> warnings, CancellationToken cancellationToken)
            {
                // The current Work Item only needs key-name correlation; it intentionally avoids parsing or persisting secret values.
                try
                {
                    foreach (string file in Directory.EnumerateFiles(repositoryRootDirectory, "appsettings*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string text = File.ReadAllText(file);
                        if (text.Contains("ServiceBus", StringComparison.OrdinalIgnoreCase) && text.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase))
                        {
                            return new MessagingArtifactIndex("Messaging:ServiceBus:ConnectionString");
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"messaging extraction skipped unreadable messaging configuration artifact because {ex.GetType().Name} occurred.");
                }

                return new MessagingArtifactIndex(serviceBusConnectionStringKey: null);
            }
        }
    }
}
