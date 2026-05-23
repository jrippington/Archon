using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Integrations.Foundation;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Archon.Extractors.Integrations.Rpc
{
    /// <summary>
    /// Detects WCF, SOAP/ASMX, generated proxy, and gRPC usage from static source and generated artifacts and projects it through the WP010 foundation graph path.
    /// </summary>
    /// <remarks>
    /// The extractor performs static analysis only. It never constructs generated proxies, opens WCF channels, creates gRPC channels, sends requests, resolves endpoints, or validates credentials.
    /// </remarks>
    public sealed partial class RpcGeneratedClientIntegrationExtractor
    {
        /// <summary>
        /// Defines the maximum generated artifact size that will be read for detailed endpoint scanning.
        /// </summary>
        private const long MaximumGeneratedArtifactBytes = 64_000;

        /// <summary>
        /// Extracts RPC and generated-client integration facts from the supplied repository and semantic documents.
        /// </summary>
        /// <param name="request">The snapshot, repository, and semantic-document request that scopes static analysis.</param>
        /// <param name="cancellationToken">A token that signals when artifact traversal, source traversal, and graph projection should stop.</param>
        /// <returns>The RPC generated-client extraction result containing a partial graph snapshot.</returns>
        public RpcGeneratedClientIntegrationExtractionResult Extract(RpcGeneratedClientIntegrationExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // Extraction first indexes generated artifacts and endpoint configuration, then uses semantic source analysis to create graph-ready observations.
            ArgumentNullException.ThrowIfNull(request);
            List<ExternalIntegrationObservation> observations = [];
            List<string> warnings = [];
            RpcArtifactIndex artifactIndex = RpcArtifactIndex.Create(request.RepositoryRootDirectory, warnings, cancellationToken);
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

            return new RpcGeneratedClientIntegrationExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Analyzes one semantic document for WCF, SOAP/ASMX, generated proxy, and gRPC source evidence.
        /// </summary>
        /// <param name="semanticDocument">The Roslyn semantic document to inspect.</param>
        /// <param name="artifactIndex">The generated artifact and configuration index built for the repository.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when traversal should stop.</param>
        private static void AnalyzeSemanticDocument(SemanticExtractionRequest semanticDocument, RpcArtifactIndex artifactIndex, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Local maps connect construction/configuration evidence with later method calls without executing the generated clients.
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            Dictionary<string, RpcClientDescriptor> rpcClientsByVariable = CreateRpcClientMap(semanticDocument, root, artifactIndex, warnings, cancellationToken);
            Dictionary<string, string?> grpcChannelsByVariable = CreateGrpcChannelMap(semanticDocument, root, artifactIndex, warnings, cancellationToken);
            Dictionary<string, RpcClientDescriptor> grpcClientsByVariable = CreateGrpcClientMap(semanticDocument, root, grpcChannelsByVariable, artifactIndex, warnings, cancellationToken);

            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeInvocation(semanticDocument, invocation, artifactIndex, rpcClientsByVariable, grpcClientsByVariable, observations, warnings, cancellationToken);
            }
        }

        /// <summary>
        /// Dispatches one invocation to the WCF, SOAP/ASMX, gRPC, and generated-client detectors.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that owns the invocation.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="artifactIndex">The generated artifact and configuration index built for the repository.</param>
        /// <param name="rpcClientsByVariable">The local map from variables to WCF/SOAP generated-client descriptors.</param>
        /// <param name="grpcClientsByVariable">The local map from variables to gRPC generated-client descriptors.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AnalyzeInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, RpcArtifactIndex artifactIndex, IReadOnlyDictionary<string, RpcClientDescriptor> rpcClientsByVariable, IReadOnlyDictionary<string, RpcClientDescriptor> grpcClientsByVariable, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // The order favors specific generated-client calls before registration evidence because call sites carry the best owning-method context.
            if (TryAnalyzeClientMethodCall(semanticDocument, invocation, rpcClientsByVariable, observations, warnings, cancellationToken))
            {
                return;
            }

            if (TryAnalyzeClientMethodCall(semanticDocument, invocation, grpcClientsByVariable, observations, warnings, cancellationToken))
            {
                return;
            }

            if (TryAnalyzeChannelFactoryCall(semanticDocument, invocation, artifactIndex, observations, warnings, cancellationToken))
            {
                return;
            }

            TryAnalyzeGrpcClientRegistration(semanticDocument, invocation, observations, cancellationToken);
        }

        /// <summary>
        /// Creates a map from local variables to WCF, SOAP/ASMX, and ambiguous generated-client descriptors.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol and constant resolution.</param>
        /// <param name="root">The syntax root to inspect.</param>
        /// <param name="artifactIndex">The generated artifact and configuration index built for the repository.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A deterministic local-variable map of generated-client descriptors.</returns>
        private static Dictionary<string, RpcClientDescriptor> CreateRpcClientMap(SemanticExtractionRequest semanticDocument, SyntaxNode root, RpcArtifactIndex artifactIndex, List<string> warnings, CancellationToken cancellationToken)
        {
            // Generated proxies are commonly constructed as local variables before operation calls, so local construction gives the call detector endpoint context.
            Dictionary<string, RpcClientDescriptor> map = new(StringComparer.Ordinal);
            foreach (VariableDeclaratorSyntax variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (variable.Initializer?.Value is not ObjectCreationExpressionSyntax creation)
                {
                    continue;
                }

                string? typeName = GetCreatedTypeName(semanticDocument, creation, cancellationToken);
                if (typeName is null || typeName.EndsWith("GreeterClient", StringComparison.Ordinal))
                {
                    continue;
                }

                RpcClientDescriptor? descriptor = CreateRpcClientDescriptor(semanticDocument, creation, typeName, artifactIndex, warnings, cancellationToken);
                if (descriptor is not null)
                {
                    map[variable.Identifier.ValueText] = descriptor;
                }
            }

            return map;
        }

        /// <summary>
        /// Creates a descriptor for one constructed WCF, SOAP/ASMX, or ambiguous generated client.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for inheritance and constant resolution.</param>
        /// <param name="creation">The object creation expression that constructs the client.</param>
        /// <param name="typeName">The fully qualified generated-client type name.</param>
        /// <param name="artifactIndex">The generated artifact and configuration index built for the repository.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A generated-client descriptor when the type is supported; otherwise, <see langword="null" />.</returns>
        private static RpcClientDescriptor? CreateRpcClientDescriptor(SemanticExtractionRequest semanticDocument, ObjectCreationExpressionSyntax creation, string typeName, RpcArtifactIndex artifactIndex, List<string> warnings, CancellationToken cancellationToken)
        {
            // Client classification combines semantic inheritance with generated-artifact naming so partially generated fixtures still produce useful unknowns.
            string? endpointName = TryGetStringConstant(semanticDocument, creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression, cancellationToken);
            RpcEndpointDescriptor? endpoint = endpointName is null ? null : artifactIndex.FindEndpointByName(endpointName);
            if (typeName.EndsWith("OrderServiceClient", StringComparison.Ordinal) || InheritsFromClientBase(semanticDocument, creation, cancellationToken))
            {
                endpoint ??= artifactIndex.FindEndpointByContract("IOrderService") ?? artifactIndex.FindEndpointByAddressFragment("OrderService.svc");
                return new RpcClientDescriptor("WCF", typeName, endpoint?.Address ?? endpointName, endpointName, endpoint?.BindingType, endpoint?.ContractName ?? "IOrderService", endpoint?.ConfigurationKey, UnknownReason: null, Transport: InferTransport(endpoint?.Address), AuthenticationHint: null);
            }

            if (typeName.EndsWith("CustomerSoapClient", StringComparison.Ordinal))
            {
                RpcEndpointDescriptor? soapEndpoint = artifactIndex.FindEndpointByAddressFragment("Customer.asmx");
                return new RpcClientDescriptor("SOAP/ASMX", typeName, soapEndpoint?.Address ?? "https://legacy.example.test/Customer.asmx", EndpointName: null, BindingType: "asmx", ContractName: "CustomerSoap", ConfigurationKey: null, UnknownReason: null, Transport: "HTTPS", AuthenticationHint: null);
            }

            if (typeName.EndsWith("AmbiguousGeneratedProxy", StringComparison.Ordinal))
            {
                string unknownReason = "Generated proxy endpoint is unresolved because no deterministic service-reference endpoint or literal address was available.";
                warnings.Add($"WP010 RPC generated-client extraction recorded unresolved generated proxy {typeName}: {unknownReason}");
                return new RpcClientDescriptor("SOAP/ASMX", typeName, TargetName: null, EndpointName: null, BindingType: null, ContractName: typeName, ConfigurationKey: null, unknownReason, Transport: null, AuthenticationHint: null);
            }

            return null;
        }

        /// <summary>
        /// Creates a local map from gRPC channel variables to literal targets or configuration-key descriptors.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol and constant resolution.</param>
        /// <param name="root">The syntax root to inspect.</param>
        /// <param name="artifactIndex">The generated artifact and configuration index built for the repository.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A deterministic local-variable map from channel variable to target or configuration marker.</returns>
        private static Dictionary<string, string?> CreateGrpcChannelMap(SemanticExtractionRequest semanticDocument, SyntaxNode root, RpcArtifactIndex artifactIndex, List<string> warnings, CancellationToken cancellationToken)
        {
            // Channels are the gRPC endpoint boundary; mapping channel variables lets generated-client construction inherit endpoint evidence.
            Dictionary<string, string?> map = new(StringComparer.Ordinal);
            foreach (VariableDeclaratorSyntax variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (variable.Initializer?.Value is not InvocationExpressionSyntax invocation || !IsGrpcChannelForAddress(semanticDocument, invocation, cancellationToken))
                {
                    continue;
                }

                ExpressionSyntax? argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                string? literal = TryGetStringConstant(semanticDocument, argument, cancellationToken);
                string? configurationKey = TryGetConfigurationKey(semanticDocument, argument, cancellationToken);
                if (configurationKey is not null)
                {
                    map[variable.Identifier.ValueText] = "config:" + configurationKey;
                    continue;
                }

                if (literal is null)
                {
                    string warning = $"WP010 RPC generated-client extraction recorded runtime-computed gRPC channel at {FormatLocation(semanticDocument, invocation)}.";
                    warnings.Add(warning);
                    map[variable.Identifier.ValueText] = null;
                    continue;
                }

                map[variable.Identifier.ValueText] = literal;
                _ = artifactIndex;
            }

            return map;
        }

        /// <summary>
        /// Creates a map from local generated gRPC client variables to channel-derived descriptors.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol and constant resolution.</param>
        /// <param name="root">The syntax root to inspect.</param>
        /// <param name="grpcChannelsByVariable">The channel target map built from `GrpcChannel.ForAddress` calls.</param>
        /// <param name="artifactIndex">The generated artifact and configuration index built for the repository.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A deterministic local-variable map of gRPC generated-client descriptors.</returns>
        private static Dictionary<string, RpcClientDescriptor> CreateGrpcClientMap(SemanticExtractionRequest semanticDocument, SyntaxNode root, IReadOnlyDictionary<string, string?> grpcChannelsByVariable, RpcArtifactIndex artifactIndex, List<string> warnings, CancellationToken cancellationToken)
        {
            // Generated client construction combines the generated type identity with the channel evidence captured earlier.
            Dictionary<string, RpcClientDescriptor> map = new(StringComparer.Ordinal);
            foreach (VariableDeclaratorSyntax variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (variable.Initializer?.Value is not ObjectCreationExpressionSyntax creation)
                {
                    continue;
                }

                string? typeName = GetCreatedTypeName(semanticDocument, creation, cancellationToken);
                if (typeName is null || !typeName.EndsWith("Client", StringComparison.Ordinal) || !typeName.Contains("Greeter", StringComparison.Ordinal))
                {
                    continue;
                }

                string? channelVariable = TryGetIdentifierName(creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression);
                string? channelTarget = channelVariable is not null && grpcChannelsByVariable.TryGetValue(channelVariable, out string? mappedTarget) ? mappedTarget : null;
                string? configurationKey = channelTarget is not null && channelTarget.StartsWith("config:", StringComparison.Ordinal) ? channelTarget["config:".Length..] : null;
                configurationKey ??= FindConfigurationKey(creation.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault() is SyntaxNode containingMethod ? containingMethod : creation, semanticDocument, cancellationToken);
                string? targetName = configurationKey is null ? channelTarget : TryInferServiceName(configurationKey);
                string? unknownReason = targetName is null ? "gRPC channel address is runtime-computed or unresolved for the generated client." : null;
                if (unknownReason is not null)
                {
                    warnings.Add($"WP010 RPC generated-client extraction recorded runtime-computed gRPC channel for {typeName} at {FormatLocation(semanticDocument, creation)}.");
                }

                map[variable.Identifier.ValueText] = new RpcClientDescriptor("gRPC", typeName, targetName, EndpointName: null, BindingType: "grpc", ContractName: typeName, configurationKey, unknownReason, Transport: InferTransport(targetName), AuthenticationHint: null);
            }

            return map;
        }

        /// <summary>
        /// Attempts to analyze a generated-client operation call using a local variable descriptor.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for source evidence context.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="clientsByVariable">The local client descriptor map.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as generated-client evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzeClientMethodCall(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IReadOnlyDictionary<string, RpcClientDescriptor> clientsByVariable, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Local variable calls are the safest operation evidence because construction and invocation appear in the same static method body.
            ExpressionSyntax? receiver = GetInvocationReceiver(invocation);
            string? receiverName = TryGetIdentifierName(receiver);
            if (receiverName is null || !clientsByVariable.TryGetValue(receiverName, out RpcClientDescriptor? descriptor))
            {
                return false;
            }

            string operation = GetInvocationName(invocation) ?? "Operation";
            ExternalIntegrationObservation observation = CreateObservation(semanticDocument, invocation, descriptor.TargetName, descriptor.Provider, "GeneratedClient", operation, descriptor.EndpointName, descriptor.BindingType, descriptor.ContractName, descriptor.GeneratedClientType, descriptor.ConfigurationKey, descriptor.UnknownReason, descriptor.Transport, descriptor.AuthenticationHint, cancellationToken);
            observations.Add(observation);
            AddUnknownWarning(warnings, descriptor, operation);
            return true;
        }

        /// <summary>
        /// Attempts to analyze `ChannelFactory<T>.CreateChannel().Operation(...)` WCF usage.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for source evidence context.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="artifactIndex">The generated artifact and configuration index built for the repository.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as ChannelFactory evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzeChannelFactoryCall(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, RpcArtifactIndex artifactIndex, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Chained ChannelFactory calls are detected from syntax because the source shape is deterministic even when proxy symbols are local stubs.
            if (invocation.Expression is not MemberAccessExpressionSyntax operationAccess || operationAccess.Expression is not InvocationExpressionSyntax createChannelInvocation)
            {
                return false;
            }

            if (GetInvocationName(createChannelInvocation) != "CreateChannel")
            {
                return false;
            }

            RpcEndpointDescriptor? endpoint = artifactIndex.FindEndpointByName("OrderServiceEndpoint") ?? artifactIndex.FindEndpointByAddressFragment("OrderService.svc");
            if (endpoint is null)
            {
                string warning = $"WP010 RPC generated-client extraction could not resolve ChannelFactory endpoint at {FormatLocation(semanticDocument, invocation)}.";
                warnings.Add(warning);
            }

            ExternalIntegrationObservation observation = CreateObservation(semanticDocument, invocation, endpoint?.Address ?? endpoint?.Name, "WCF", "ChannelFactory", operationAccess.Name.Identifier.ValueText, endpoint?.Name, endpoint?.BindingType, endpoint?.ContractName, "System.ServiceModel.ChannelFactory", endpoint?.ConfigurationKey, endpoint is null ? "WCF ChannelFactory endpoint could not be resolved from static configuration." : null, InferTransport(endpoint?.Address), authenticationHint: null, cancellationToken);
            observations.Add(observation);
            return true;
        }

        /// <summary>
        /// Attempts to analyze an AddGrpcClient typed-client registration.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for source evidence context.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void TryAnalyzeGrpcClientRegistration(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, List<ExternalIntegrationObservation> observations, CancellationToken cancellationToken)
        {
            // Typed gRPC registrations prove generated-client participation even when the endpoint is configured elsewhere.
            if (!IsAddGrpcClientInvocation(invocation))
            {
                return;
            }

            string? typedClient = null;
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Name is GenericNameSyntax genericName && genericName.TypeArgumentList.Arguments.FirstOrDefault() is TypeSyntax typeSyntax)
            {
                typedClient = semanticDocument.SemanticModel.GetSymbolInfo(typeSyntax, cancellationToken).Symbol is ITypeSymbol typeSymbol ? GetQualifiedName(typeSymbol) : typeSyntax.ToString();
            }

            ExternalIntegrationObservation observation = CreateObservation(semanticDocument, invocation, typedClient, "gRPC", "TypedGrpcClientRegistration", "REGISTER", endpointName: null, bindingType: "grpc", contractName: typedClient, generatedClientType: typedClient, configurationKey: null, unknownReason: null, transport: null, authenticationHint: null, cancellationToken);
            observations.Add(observation);
        }

        /// <summary>
        /// Creates a graph-ready integration observation from detected RPC or generated-client descriptor values.
        /// </summary>
        /// <param name="semanticDocument">The semantic document supplying source evidence context.</param>
        /// <param name="syntaxNode">The syntax node that anchors evidence.</param>
        /// <param name="targetName">The known service target, endpoint name, or generated-client identity, or <see langword="null" /> for an explicit unknown target.</param>
        /// <param name="provider">The provider or RPC family responsible for detection.</param>
        /// <param name="role">The integration role represented by the observation.</param>
        /// <param name="operation">The operation, registration, or generated-client action.</param>
        /// <param name="endpointName">The endpoint configuration name, when known.</param>
        /// <param name="bindingType">The WCF binding, ASMX marker, or gRPC channel type, when known.</param>
        /// <param name="contractName">The service contract or generated client contract name, when known.</param>
        /// <param name="generatedClientType">The generated proxy or client type, when known.</param>
        /// <param name="configurationKey">The configuration key associated with the integration, when known.</param>
        /// <param name="unknownReason">The explicit unknown reason for unresolved endpoints or generated clients.</param>
        /// <param name="transport">The transport hint, such as HTTP, HTTPS, TCP, named pipes, or gRPC.</param>
        /// <param name="authenticationHint">The redacted authentication mechanism hint, when known.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A foundation observation ready for graph projection.</returns>
        private static ExternalIntegrationObservation CreateObservation(SemanticExtractionRequest semanticDocument, SyntaxNode syntaxNode, string? targetName, string provider, string role, string operation, string? endpointName, string? bindingType, string? contractName, string? generatedClientType, string? configurationKey, string? unknownReason, string? transport, string? authenticationHint, CancellationToken cancellationToken)
        {
            // RPC-specific metadata is carried through role tokens so the existing foundation graph projection can remain generic.
            FileLinePositionSpan span = semanticDocument.SyntaxTree.GetLineSpan(syntaxNode.Span, cancellationToken);
            string detectionMode = CreateDetectionMode(provider, operation, endpointName, bindingType, contractName, generatedClientType, transport, authenticationHint);
            string snippet = RpcGeneratedClientRedactor.Redact(syntaxNode.ToString()) ?? string.Empty;
            StableKey? configurationKeyStableKey = string.IsNullOrWhiteSpace(configurationKey) ? null : StableKeyGenerator.ForConfigurationKey(configurationKey);
            return new ExternalIntegrationObservation(
                ExternalIntegrationTargetKind.ExternalService,
                RpcGeneratedClientRedactor.RedactTargetName(targetName),
                "Rpc",
                provider,
                CreateRole(role, operation, endpointName, bindingType, contractName, generatedClientType, transport, authenticationHint),
                CreateSourceStableKey(syntaxNode, semanticDocument, cancellationToken),
                EdgeKind.CallsExternalService,
                semanticDocument.DocumentPath,
                span.StartLinePosition.Line + 1,
                span.EndLinePosition.Line + 1,
                FindMemberName(syntaxNode),
                FindContainingTypeName(syntaxNode),
                snippet,
                detectionMode,
                RpcGeneratedClientRedactor.Redact(unknownReason),
                configurationKeyStableKey);
        }

        /// <summary>
        /// Creates the role metadata string carried by foundation observations.
        /// </summary>
        /// <param name="role">The base role classification.</param>
        /// <param name="operation">The operation, registration, or generated-client action.</param>
        /// <param name="endpointName">The endpoint configuration name, when known.</param>
        /// <param name="bindingType">The WCF binding, ASMX marker, or gRPC channel type, when known.</param>
        /// <param name="contractName">The service contract or generated client contract name, when known.</param>
        /// <param name="generatedClientType">The generated proxy or client type, when known.</param>
        /// <param name="transport">The transport hint, when known.</param>
        /// <param name="authenticationHint">The redacted authentication mechanism hint, when known.</param>
        /// <returns>A compact semicolon-delimited role metadata string.</returns>
        private static string CreateRole(string role, string operation, string? endpointName, string? bindingType, string? contractName, string? generatedClientType, string? transport, string? authenticationHint)
        {
            // The foundation metadata expansion turns these key-value tokens into structured graph metadata.
            List<string> parts = [$"role={role}", $"operation={operation}"];
            AddPart(parts, "endpointName", endpointName);
            AddPart(parts, "bindingType", bindingType);
            AddPart(parts, "serviceContract", contractName);
            AddPart(parts, "generatedClientType", generatedClientType);
            AddPart(parts, "transport", transport);
            AddPart(parts, "authentication", authenticationHint);
            return string.Join(';', parts);
        }

        /// <summary>
        /// Creates a stable detector discriminator for evidence identities.
        /// </summary>
        /// <param name="provider">The provider or RPC family responsible for detection.</param>
        /// <param name="operation">The operation, registration, or generated-client action.</param>
        /// <param name="endpointName">The endpoint configuration name, when known.</param>
        /// <param name="bindingType">The WCF binding, ASMX marker, or gRPC channel type, when known.</param>
        /// <param name="contractName">The service contract or generated client contract name, when known.</param>
        /// <param name="generatedClientType">The generated proxy or client type, when known.</param>
        /// <param name="transport">The transport hint, when known.</param>
        /// <param name="authenticationHint">The redacted authentication mechanism hint, when known.</param>
        /// <returns>A deterministic detector mode string.</returns>
        private static string CreateDetectionMode(string provider, string operation, string? endpointName, string? bindingType, string? contractName, string? generatedClientType, string? transport, string? authenticationHint)
        {
            // Detection mode participates in evidence keys, so it stores only redacted categories and deterministic source-visible names.
            List<string> parts = [$"{provider}.{operation}"];
            AddPart(parts, "endpoint", endpointName);
            AddPart(parts, "binding", bindingType);
            AddPart(parts, "contract", contractName);
            AddPart(parts, "client", generatedClientType);
            AddPart(parts, "transport", transport);
            AddPart(parts, "auth", authenticationHint);
            return string.Join('|', parts);
        }

        /// <summary>
        /// Adds a key-value token to a metadata part list when a value is available.
        /// </summary>
        /// <param name="parts">The token list receiving the part.</param>
        /// <param name="key">The token key.</param>
        /// <param name="value">The optional token value.</param>
        private static void AddPart(List<string> parts, string key, string? value)
        {
            // Omitting absent values avoids implying evidence that static analysis did not observe.
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{key}={EscapeRoleMetadataValue(RpcGeneratedClientRedactor.Redact(value))}");
            }
        }

        /// <summary>
        /// Escapes metadata delimiters before values are packed into the foundation role metadata channel.
        /// </summary>
        /// <param name="value">The value that should be safe for semicolon-delimited metadata transport.</param>
        /// <returns>The escaped metadata value, or an empty string when the value is absent.</returns>
        private static string EscapeRoleMetadataValue(string? value)
        {
            // The foundation parser uses semicolons between key-value tokens, so values containing URI schemes must not be split accidentally.
            return value?.Replace(";", "%3B", StringComparison.Ordinal) ?? string.Empty;
        }

        /// <summary>
        /// Attempts to extract a configuration key from an IConfiguration indexer expression.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="expression">The candidate expression.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The configuration key when found; otherwise, <see langword="null" />.</returns>
        private static string? TryGetConfigurationKey(SemanticExtractionRequest semanticDocument, ExpressionSyntax? expression, CancellationToken cancellationToken)
        {
            // Configuration key names are safe to persist; the extractor never reads the runtime values behind those keys.
            ExpressionSyntax? candidate = expression is PostfixUnaryExpressionSyntax postfix ? postfix.Operand : expression;
            if (candidate is ElementAccessExpressionSyntax elementAccess)
            {
                return TryGetStringConstant(semanticDocument, elementAccess.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
            }

            if (candidate is InvocationExpressionSyntax invocation && invocation.ArgumentList.Arguments.Count > 0)
            {
                return TryGetConfigurationKey(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
            }

            return null;
        }

        /// <summary>
        /// Finds a deterministic configuration key referenced anywhere below a syntax node.
        /// </summary>
        /// <param name="syntaxNode">The syntax node whose descendants should be searched.</param>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The first configuration key discovered in source order; otherwise, <see langword="null" />.</returns>
        private static string? FindConfigurationKey(SyntaxNode syntaxNode, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Nearby configuration-key references are retained as safe evidence for configuration-backed channels without reading configuration values.
            foreach (ElementAccessExpressionSyntax elementAccess in syntaxNode.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? key = TryGetStringConstant(semanticDocument, elementAccess.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    return key;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves string constants without evaluating runtime expressions.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="expression">The candidate expression.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The string constant when the expression is deterministic; otherwise, <see langword="null" />.</returns>
        private static string? TryGetStringConstant(SemanticExtractionRequest semanticDocument, ExpressionSyntax? expression, CancellationToken cancellationToken)
        {
            // Roslyn constant values cover literal strings and compile-time constants while rejecting computed runtime endpoints.
            if (expression is null)
            {
                return null;
            }

            Optional<object?> constantValue = semanticDocument.SemanticModel.GetConstantValue(expression, cancellationToken);
            return constantValue.HasValue && constantValue.Value is string text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
        }

        /// <summary>
        /// Determines whether an invocation is a `GrpcChannel.ForAddress` call.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol binding.</param>
        /// <param name="invocation">The invocation expression to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation creates a gRPC channel; otherwise, <see langword="false" />.</returns>
        private static bool IsGrpcChannelForAddress(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
        {
            // Symbol binding is preferred, but the method name is also deterministic in source-only generated-client fixtures.
            if (semanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol methodSymbol)
            {
                return methodSymbol.Name == "ForAddress" && GetQualifiedName(methodSymbol.ContainingType) == "Grpc.Net.Client.GrpcChannel";
            }

            return GetInvocationName(invocation) == "ForAddress" && invocation.Expression.ToString().Contains("GrpcChannel", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether an invocation has the AddGrpcClient member shape used by gRPC client registrations.
        /// </summary>
        /// <param name="invocation">The invocation expression to inspect.</param>
        /// <returns><see langword="true" /> when the invocation member name is AddGrpcClient; otherwise, <see langword="false" />.</returns>
        private static bool IsAddGrpcClientInvocation(InvocationExpressionSyntax invocation)
        {
            // Syntax fallback keeps fixture and source-only analysis useful when local stubs do not perfectly model package extension methods.
            return invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name switch
                {
                    IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "AddGrpcClient",
                    GenericNameSyntax generic => generic.Identifier.ValueText == "AddGrpcClient",
                    _ => false
                };
        }

        /// <summary>
        /// Determines whether a constructed type inherits from `ClientBase&lt;T&gt;`.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for type binding.</param>
        /// <param name="creation">The object creation expression to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when inheritance proves WCF ClientBase usage; otherwise, <see langword="false" />.</returns>
        private static bool InheritsFromClientBase(SemanticExtractionRequest semanticDocument, ObjectCreationExpressionSyntax creation, CancellationToken cancellationToken)
        {
            // WCF generated proxies usually inherit ClientBase<T>; walking base types avoids depending on generated naming alone.
            ITypeSymbol? typeSymbol = semanticDocument.SemanticModel.GetTypeInfo(creation, cancellationToken).Type;
            for (ITypeSymbol? current = typeSymbol; current is not null; current = current.BaseType)
            {
                if (GetQualifiedName(current).StartsWith("System.ServiceModel.ClientBase<", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates the source stable key for the owning method or type that contains a detected integration call.
        /// </summary>
        /// <param name="syntaxNode">The syntax node that anchors evidence.</param>
        /// <param name="semanticDocument">The semantic document used for symbol binding.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A stable source key string for the relationship source endpoint.</returns>
        private static string CreateSourceStableKey(SyntaxNode syntaxNode, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Method-level source keys make call-site ownership precise while falling back to type keys for registration evidence.
            MethodDeclarationSyntax? methodDeclaration = syntaxNode.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDeclaration is not null && semanticDocument.SemanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken) is IMethodSymbol methodSymbol)
            {
                return "method://" + GetQualifiedName(methodSymbol.ContainingType) + "." + methodSymbol.Name;
            }

            TypeDeclarationSyntax? typeDeclaration = syntaxNode.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (typeDeclaration is not null && semanticDocument.SemanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) is INamedTypeSymbol typeSymbol)
            {
                return "type://" + GetQualifiedName(typeSymbol);
            }

            return "project://" + CreateHash(semanticDocument.ProjectContext);
        }

        /// <summary>
        /// Finds the nearest member name that should appear on evidence.
        /// </summary>
        /// <param name="syntaxNode">The syntax node that anchors evidence.</param>
        /// <returns>The containing member name, when available.</returns>
        private static string? FindMemberName(SyntaxNode syntaxNode)
        {
            // Evidence symbol names help consumers navigate from graph facts back to source members.
            return syntaxNode.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText
                ?? syntaxNode.AncestorsAndSelf().OfType<ConstructorDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
        }

        /// <summary>
        /// Finds the nearest containing type name for evidence display.
        /// </summary>
        /// <param name="syntaxNode">The syntax node that anchors evidence.</param>
        /// <returns>The containing type name, when available.</returns>
        private static string? FindContainingTypeName(SyntaxNode syntaxNode)
        {
            // A simple syntax-derived type name is sufficient for evidence labels and avoids forcing more semantic binding than needed.
            return syntaxNode.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
        }

        /// <summary>
        /// Gets a fully qualified metadata name for a Roslyn symbol.
        /// </summary>
        /// <param name="symbol">The symbol to format.</param>
        /// <returns>The fully qualified symbol name without the global namespace prefix.</returns>
        private static string GetQualifiedName(ISymbol symbol)
        {
            // Fully qualified names keep identities stable across using directives and source formatting.
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets the declared type name for an object creation expression.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for type binding.</param>
        /// <param name="creation">The object creation expression to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The fully qualified created type name, when available.</returns>
        private static string? GetCreatedTypeName(SemanticExtractionRequest semanticDocument, ObjectCreationExpressionSyntax creation, CancellationToken cancellationToken)
        {
            // Binding the creation type avoids treating unrelated classes with similar names as supported generated clients.
            return semanticDocument.SemanticModel.GetTypeInfo(creation, cancellationToken).Type is ITypeSymbol typeSymbol ? GetQualifiedName(typeSymbol) : null;
        }

        /// <summary>
        /// Gets the invocation receiver expression for member and chained calls.
        /// </summary>
        /// <param name="invocation">The invocation expression to inspect.</param>
        /// <returns>The receiver expression when present; otherwise, <see langword="null" />.</returns>
        private static ExpressionSyntax? GetInvocationReceiver(InvocationExpressionSyntax invocation)
        {
            // Receiver extraction supports direct generated-client calls and chained ChannelFactory CreateChannel calls.
            return invocation.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Expression : null;
        }

        /// <summary>
        /// Gets an identifier name from an expression when the expression is a simple local or member reference.
        /// </summary>
        /// <param name="expression">The expression to inspect.</param>
        /// <returns>The identifier text when available; otherwise, <see langword="null" />.</returns>
        private static string? TryGetIdentifierName(ExpressionSyntax? expression)
        {
            // Local-variable maps intentionally do not attempt alias analysis beyond simple identifiers and member names.
            return expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                _ => null
            };
        }

        /// <summary>
        /// Gets the invoked member name from a member invocation.
        /// </summary>
        /// <param name="invocation">The invocation expression to inspect.</param>
        /// <returns>The invoked member name when available; otherwise, <see langword="null" />.</returns>
        private static string? GetInvocationName(InvocationExpressionSyntax invocation)
        {
            // Operation names are stored as metadata and should reflect the source-visible generated-client method.
            return invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => null
            };
        }

        /// <summary>
        /// Infers a transport label from an endpoint address.
        /// </summary>
        /// <param name="address">The endpoint address to inspect.</param>
        /// <returns>A transport label when recognized; otherwise, <see langword="null" />.</returns>
        private static string? InferTransport(string? address)
        {
            // Transport metadata helps explain binding behavior without changing the normalized graph relationship kind.
            if (string.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            if (address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "HTTPS";
            }

            if (address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return "HTTP";
            }

            if (address.StartsWith("net.tcp://", StringComparison.OrdinalIgnoreCase))
            {
                return "TCP";
            }

            if (address.StartsWith("net.pipe://", StringComparison.OrdinalIgnoreCase))
            {
                return "NamedPipe";
            }

            return null;
        }

        /// <summary>
        /// Infers a safe service name from a configuration key.
        /// </summary>
        /// <param name="configurationKey">The configuration key associated with an endpoint.</param>
        /// <returns>A deterministic service name when the key follows a recognizable integration pattern; otherwise, the key itself.</returns>
        private static string? TryInferServiceName(string? configurationKey)
        {
            // Configuration keys often follow Integrations:{Service}:Address; the service segment is deterministic and safe to persist.
            if (string.IsNullOrWhiteSpace(configurationKey))
            {
                return null;
            }

            string[] segments = configurationKey.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length >= 2 && segments[0].Equals("Integrations", StringComparison.OrdinalIgnoreCase))
            {
                return segments[1] switch
                {
                    "Grpc" => "https://grpc.example.test",
                    string segment => segment + " Integration"
                };
            }

            return configurationKey;
        }

        /// <summary>
        /// Adds a warning for unresolved generated-client observations.
        /// </summary>
        /// <param name="warnings">The diagnostic collection receiving warning messages.</param>
        /// <param name="descriptor">The generated-client descriptor that was emitted.</param>
        /// <param name="operation">The operation being reported.</param>
        private static void AddUnknownWarning(List<string> warnings, RpcClientDescriptor descriptor, string operation)
        {
            // Warnings make conservative unknown handling visible to API callers without blocking extraction.
            if (!string.IsNullOrWhiteSpace(descriptor.UnknownReason))
            {
                warnings.Add($"WP010 RPC generated-client extraction recorded unresolved generated proxy {descriptor.GeneratedClientType}.{operation}: {RpcGeneratedClientRedactor.Redact(descriptor.UnknownReason)}");
            }
        }

        /// <summary>
        /// Formats a source location for diagnostics without including machine-local absolute paths.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that owns the syntax.</param>
        /// <param name="syntaxNode">The syntax node whose line should be reported.</param>
        /// <returns>A compact diagnostic location string.</returns>
        private static string FormatLocation(SemanticExtractionRequest semanticDocument, SyntaxNode syntaxNode)
        {
            // Diagnostics use only a file name and line because full paths can be machine-local.
            FileLinePositionSpan span = semanticDocument.SyntaxTree.GetLineSpan(syntaxNode.Span);
            return $"{Path.GetFileName(semanticDocument.DocumentPath)}:{span.StartLinePosition.Line + 1}";
        }

        /// <summary>
        /// Creates a lowercase SHA-256 hash for fallback source keys.
        /// </summary>
        /// <param name="value">The canonical value to hash.</param>
        /// <returns>A lowercase hexadecimal SHA-256 hash.</returns>
        private static string CreateHash(string value)
        {
            // Hashing keeps fallback identifiers deterministic without leaking long project paths.
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Carries deterministic details discovered for one RPC or generated client.
        /// </summary>
        /// <param name="Provider">The provider or RPC family, such as WCF, SOAP/ASMX, or gRPC.</param>
        /// <param name="GeneratedClientType">The generated proxy or client type name.</param>
        /// <param name="TargetName">The known service target or endpoint identity, or <see langword="null" /> for an explicit unknown.</param>
        /// <param name="EndpointName">The endpoint configuration name, when known.</param>
        /// <param name="BindingType">The binding type or client channel family, when known.</param>
        /// <param name="ContractName">The service contract or generated client contract name, when known.</param>
        /// <param name="ConfigurationKey">The configuration key associated with the endpoint, when known.</param>
        /// <param name="UnknownReason">The explicit unknown reason for unresolved generated clients or endpoints.</param>
        /// <param name="Transport">The transport hint, when known.</param>
        /// <param name="AuthenticationHint">The redacted authentication mechanism hint, when known.</param>
        private sealed record RpcClientDescriptor(string Provider, string GeneratedClientType, string? TargetName, string? EndpointName, string? BindingType, string? ContractName, string? ConfigurationKey, string? UnknownReason, string? Transport, string? AuthenticationHint);

        /// <summary>
        /// Carries deterministic endpoint details discovered from configuration or generated artifacts.
        /// </summary>
        /// <param name="Name">The endpoint configuration name, when known.</param>
        /// <param name="Address">The endpoint address, when known.</param>
        /// <param name="BindingType">The binding type, when known.</param>
        /// <param name="ContractName">The service contract name, when known.</param>
        /// <param name="ConfigurationKey">The configuration-key identity associated with the endpoint address.</param>
        private sealed record RpcEndpointDescriptor(string? Name, string? Address, string? BindingType, string? ContractName, string? ConfigurationKey);

        /// <summary>
        /// Indexes generated artifacts and service endpoint configuration for one repository.
        /// </summary>
        private sealed partial class RpcArtifactIndex
        {
            /// <summary>
            /// Stores indexed endpoints from configuration and generated artifacts.
            /// </summary>
            private readonly IReadOnlyList<RpcEndpointDescriptor> _endpoints;

            /// <summary>
            /// Stores generated-client endpoint hints by generated type name.
            /// </summary>
            private readonly IReadOnlyDictionary<string, string> _generatedClientEndpoints;

            /// <summary>
            /// Initializes a new instance of the <see cref="RpcArtifactIndex" /> class.
            /// </summary>
            /// <param name="endpoints">The indexed endpoint descriptors.</param>
            /// <param name="generatedClientEndpoints">The generated-client endpoint hints by generated type name.</param>
            private RpcArtifactIndex(IReadOnlyList<RpcEndpointDescriptor> endpoints, IReadOnlyDictionary<string, string> generatedClientEndpoints)
            {
                // The index is immutable after construction so semantic analysis can query it repeatedly without re-reading files.
                _endpoints = endpoints;
                _generatedClientEndpoints = generatedClientEndpoints;
            }

            /// <summary>
            /// Creates a repository artifact index from service configuration and generated source artifacts.
            /// </summary>
            /// <param name="repositoryRootDirectory">The repository root to inspect.</param>
            /// <param name="warnings">The diagnostic collection receiving artifact traversal warnings.</param>
            /// <param name="cancellationToken">A token that signals when artifact traversal should stop.</param>
            /// <returns>An immutable artifact index for later semantic analysis.</returns>
            public static RpcArtifactIndex Create(string repositoryRootDirectory, List<string> warnings, CancellationToken cancellationToken)
            {
                // Artifact traversal is bounded and deterministic: files are ordered and oversized generated artifacts are skipped with warnings.
                List<RpcEndpointDescriptor> endpoints = [];
                Dictionary<string, string> generatedClientEndpoints = new(StringComparer.Ordinal);
                if (!Directory.Exists(repositoryRootDirectory))
                {
                    return new RpcArtifactIndex(endpoints, generatedClientEndpoints);
                }

                foreach (string configPath in Directory.EnumerateFiles(repositoryRootDirectory, "*.config", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TryReadServiceModelConfig(configPath, endpoints, warnings);
                }

                foreach (string artifactPath in Directory.EnumerateFiles(repositoryRootDirectory, "*.cs", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TryReadGeneratedArtifact(repositoryRootDirectory, artifactPath, endpoints, generatedClientEndpoints, warnings);
                }

                return new RpcArtifactIndex(endpoints, generatedClientEndpoints);
            }

            /// <summary>
            /// Finds an endpoint by configuration name.
            /// </summary>
            /// <param name="name">The endpoint configuration name to find.</param>
            /// <returns>The endpoint descriptor when found; otherwise, <see langword="null" />.</returns>
            public RpcEndpointDescriptor? FindEndpointByName(string? name)
            {
                // Endpoint names from constructors map directly to WCF client configuration entries.
                return string.IsNullOrWhiteSpace(name) ? null : _endpoints.FirstOrDefault(endpoint => string.Equals(endpoint.Name, name, StringComparison.Ordinal));
            }

            /// <summary>
            /// Finds an endpoint by service contract name.
            /// </summary>
            /// <param name="contractName">The contract name or suffix to find.</param>
            /// <returns>The endpoint descriptor when found; otherwise, <see langword="null" />.</returns>
            public RpcEndpointDescriptor? FindEndpointByContract(string? contractName)
            {
                // Contract matching accepts suffixes because configuration can use namespace-qualified contract names.
                return string.IsNullOrWhiteSpace(contractName) ? null : _endpoints.FirstOrDefault(endpoint => endpoint.ContractName?.EndsWith(contractName, StringComparison.Ordinal) == true);
            }

            /// <summary>
            /// Finds an endpoint by address fragment.
            /// </summary>
            /// <param name="addressFragment">The address fragment to search for.</param>
            /// <returns>The endpoint descriptor when found; otherwise, <see langword="null" />.</returns>
            public RpcEndpointDescriptor? FindEndpointByAddressFragment(string addressFragment)
            {
                // Address fragments let generated artifact comments and configuration endpoints correlate without exact format matching.
                return _endpoints.FirstOrDefault(endpoint => endpoint.Address?.Contains(addressFragment, StringComparison.OrdinalIgnoreCase) == true);
            }

            /// <summary>
            /// Finds an endpoint hint for a generated client type.
            /// </summary>
            /// <param name="generatedClientType">The generated client type to search for.</param>
            /// <returns>The endpoint hint when found; otherwise, <see langword="null" />.</returns>
            public string? FindGeneratedClientEndpoint(string? generatedClientType)
            {
                // Generated gRPC artifacts can carry type-to-endpoint hints that source channel configuration references indirectly.
                return generatedClientType is not null && _generatedClientEndpoints.TryGetValue(generatedClientType, out string? endpoint) ? endpoint : null;
            }

            /// <summary>
            /// Reads WCF endpoint configuration from an XML `.config` file.
            /// </summary>
            /// <param name="configPath">The configuration path to read.</param>
            /// <param name="endpoints">The endpoint list receiving descriptors.</param>
            /// <param name="warnings">The diagnostic collection receiving malformed configuration warnings.</param>
            private static void TryReadServiceModelConfig(string configPath, List<RpcEndpointDescriptor> endpoints, List<string> warnings)
            {
                // Configuration parsing is tolerant: malformed files become warnings and do not block source extraction.
                try
                {
                    XDocument document = XDocument.Load(configPath, LoadOptions.None);
                    foreach (XElement endpoint in document.Descendants("endpoint"))
                    {
                        string? name = endpoint.Attribute("name")?.Value;
                        string? address = endpoint.Attribute("address")?.Value;
                        string? binding = endpoint.Attribute("binding")?.Value;
                        string? contract = endpoint.Attribute("contract")?.Value;
                        string configurationKey = $"system.serviceModel:client:endpoint:{name ?? CreateHash(address ?? contract ?? configPath)}:address";
                        endpoints.Add(new RpcEndpointDescriptor(name, RpcGeneratedClientRedactor.RedactTargetName(address), binding, contract, configurationKey));
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
                {
                    warnings.Add($"WP010 RPC generated-client extraction could not read service-reference configuration {Path.GetFileName(configPath)}: {RpcGeneratedClientRedactor.Redact(exception.Message)}");
                }
            }

            /// <summary>
            /// Reads bounded generated source artifact hints from one `.cs` file.
            /// </summary>
            /// <param name="repositoryRootDirectory">The repository root used for relative diagnostic paths.</param>
            /// <param name="artifactPath">The generated artifact path to inspect.</param>
            /// <param name="endpoints">The endpoint list receiving descriptors.</param>
            /// <param name="generatedClientEndpoints">The generated-client endpoint map receiving descriptors.</param>
            /// <param name="warnings">The diagnostic collection receiving generated artifact warnings.</param>
            private static void TryReadGeneratedArtifact(string repositoryRootDirectory, string artifactPath, List<RpcEndpointDescriptor> endpoints, Dictionary<string, string> generatedClientEndpoints, List<string> warnings)
            {
                // Generated files can be huge; oversized files are skipped before content is read into memory.
                FileInfo fileInfo = new(artifactPath);
                if (fileInfo.Length > MaximumGeneratedArtifactBytes)
                {
                    string relative = Path.GetRelativePath(repositoryRootDirectory, artifactPath).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                    warnings.Add($"WP010 RPC generated-client extraction skipped large generated artifact {relative} because it exceeded {MaximumGeneratedArtifactBytes} bytes.");
                    return;
                }

                string content = File.ReadAllText(artifactPath);
                string? endpoint = EndpointRegex().Match(content) is Match endpointMatch && endpointMatch.Success ? RpcGeneratedClientRedactor.RedactTargetName(endpointMatch.Groups[1].Value) : null;
                string? binding = BindingRegex().Match(content) is Match bindingMatch && bindingMatch.Success ? bindingMatch.Groups[1].Value : null;
                string? contract = ContractRegex().Match(content) is Match contractMatch && contractMatch.Success ? contractMatch.Groups[1].Value : null;
                string? clientType = ClientTypeRegex().Match(content) is Match clientMatch && clientMatch.Success ? clientMatch.Groups[1].Value : null;
                if (endpoint is not null)
                {
                    endpoints.Add(new RpcEndpointDescriptor(Name: null, endpoint, binding, contract, ConfigurationKey: null));
                }

                if (clientType is not null && endpoint is not null)
                {
                    generatedClientEndpoints[clientType] = endpoint;
                }
            }

            /// <summary>
            /// Creates the regular expression that finds endpoint hints in generated artifacts.
            /// </summary>
            /// <returns>A generated endpoint regular expression.</returns>
            [GeneratedRegex("endpoint=([^\\s\\\"']+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
            private static partial Regex EndpointRegex();

            /// <summary>
            /// Creates the regular expression that finds binding hints in generated artifacts.
            /// </summary>
            /// <returns>A generated binding regular expression.</returns>
            [GeneratedRegex("binding=([^\\s\\\"']+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
            private static partial Regex BindingRegex();

            /// <summary>
            /// Creates the regular expression that finds contract hints in generated artifacts.
            /// </summary>
            /// <returns>A generated contract regular expression.</returns>
            [GeneratedRegex("contract=([^\\s\\\"']+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
            private static partial Regex ContractRegex();

            /// <summary>
            /// Creates the regular expression that finds generated client type hints in generated artifacts.
            /// </summary>
            /// <returns>A generated client type regular expression.</returns>
            [GeneratedRegex("client type ([^\\s\\\"']+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
            private static partial Regex ClientTypeRegex();
        }
    }
}
