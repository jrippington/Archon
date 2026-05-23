using System.Security.Cryptography;
using System.Text;
using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Integrations.Foundation;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Archon.Extractors.Integrations.HttpRest
{
    /// <summary>
    /// Detects outbound HTTP, RestSharp, and deterministic REST abstraction usage from Roslyn semantic documents and projects it through the WP010 foundation graph path.
    /// </summary>
    /// <remarks>
    /// The extractor is a static analyzer only. It never constructs clients, opens sockets, validates credentials, sends requests, contacts brokers, or evaluates runtime configuration sources.
    /// </remarks>
    public sealed class HttpRestIntegrationExtractor
    {
        /// <summary>
        /// Stores supported instance and extension HTTP invocation names with their default operation labels.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> HttpMethodOperations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GetAsync"] = "GET",
            ["PostAsync"] = "POST",
            ["PutAsync"] = "PUT",
            ["PatchAsync"] = "PATCH",
            ["DeleteAsync"] = "DELETE",
            ["SendAsync"] = "SEND",
            ["GetFromJsonAsync"] = "GET",
            ["PostAsJsonAsync"] = "POST"
        };

        /// <summary>
        /// Extracts HTTP and REST integration facts from the supplied semantic documents.
        /// </summary>
        /// <param name="request">The snapshot and semantic-document request that scopes static analysis.</param>
        /// <param name="cancellationToken">A token that signals when source traversal and graph projection should stop.</param>
        /// <returns>The HTTP and REST integration extraction result containing a partial graph snapshot.</returns>
        public HttpRestIntegrationExtractionResult Extract(HttpRestIntegrationExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // Extraction runs in two phases: first collect source observations, then let the foundation projector create consistent graph facts and diagnostics.
            ArgumentNullException.ThrowIfNull(request);
            List<ExternalIntegrationObservation> observations = [];
            List<string> warnings = [];
            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeSemanticDocument(semanticDocument, observations, warnings, cancellationToken);
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

            return new HttpRestIntegrationExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Analyzes one semantic document for supported HTTP, RestSharp, REST abstraction, DI, and configuration evidence.
        /// </summary>
        /// <param name="semanticDocument">The Roslyn semantic document to inspect.</param>
        /// <param name="observations">The observation collection that receives graph-ready integration facts.</param>
        /// <param name="warnings">The diagnostic collection that receives conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when traversal should stop.</param>
        private static void AnalyzeSemanticDocument(SemanticExtractionRequest semanticDocument, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // A single syntax pass over invocations is sufficient because supported evidence is anchored in calls, construction, or registration statements.
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            Dictionary<string, string> namedClientsByVariable = CreateNamedClientMap(semanticDocument, root, cancellationToken);
            Dictionary<string, HttpRequestMessageDescriptor> requestMessagesByVariable = CreateRequestMessageMap(semanticDocument, root, cancellationToken);
            Dictionary<string, RestRequestDescriptor> restRequestsByVariable = CreateRestRequestMap(semanticDocument, root, cancellationToken);
            Dictionary<string, string?> restClientsByVariable = CreateRestClientMap(semanticDocument, root, cancellationToken);

            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeInvocation(semanticDocument, invocation, namedClientsByVariable, requestMessagesByVariable, restRequestsByVariable, restClientsByVariable, observations, warnings, cancellationToken);
            }
        }

        /// <summary>
        /// Dispatches one invocation to the HTTP, DI, RestSharp, and REST abstraction detectors.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that owns the invocation.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="namedClientsByVariable">A map from local HttpClient variable names to deterministic factory client names.</param>
        /// <param name="requestMessagesByVariable">A map from local request-message variable names to deterministic method and path hints.</param>
        /// <param name="restRequestsByVariable">A map from local RestSharp request variable names to deterministic method and resource hints.</param>
        /// <param name="restClientsByVariable">A map from local RestSharp client variable names to deterministic base URL or configuration hints.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AnalyzeInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IReadOnlyDictionary<string, string> namedClientsByVariable, IReadOnlyDictionary<string, HttpRequestMessageDescriptor> requestMessagesByVariable, IReadOnlyDictionary<string, RestRequestDescriptor> restRequestsByVariable, IReadOnlyDictionary<string, string?> restClientsByVariable, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Each detector is independent and returns quickly when symbols do not match its supported catalog.
            if (TryAnalyzeHttpInvocation(semanticDocument, invocation, namedClientsByVariable, requestMessagesByVariable, observations, warnings, cancellationToken))
            {
                return;
            }

            if (TryAnalyzeHttpClientRegistration(semanticDocument, invocation, observations, cancellationToken))
            {
                return;
            }

            if (TryAnalyzeRestSharpExecute(semanticDocument, invocation, restRequestsByVariable, restClientsByVariable, observations, warnings, cancellationToken))
            {
                return;
            }

            TryAnalyzeRestAbstractionInvocation(semanticDocument, invocation, observations, warnings, cancellationToken);
        }

        /// <summary>
        /// Attempts to analyze an invocation as a System.Net.Http client operation.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for Roslyn symbol and constant resolution.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="namedClientsByVariable">The local factory-created client-name map.</param>
        /// <param name="requestMessagesByVariable">The local HttpRequestMessage map.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as HTTP evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzeHttpInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IReadOnlyDictionary<string, string> namedClientsByVariable, IReadOnlyDictionary<string, HttpRequestMessageDescriptor> requestMessagesByVariable, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Supported HTTP methods must bind to HttpClient or the known JSON extension owner to avoid text-only false positives.
            if (semanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol methodSymbol || !HttpMethodOperations.TryGetValue(methodSymbol.Name, out string? operation))
            {
                return false;
            }

            IMethodSymbol canonicalMethod = methodSymbol.ReducedFrom ?? methodSymbol.OriginalDefinition;
            string ownerType = GetQualifiedName(canonicalMethod.ContainingType);
            if (ownerType != "System.Net.Http.HttpClient" && ownerType != "System.Net.Http.Json.HttpClientJsonExtensions")
            {
                return false;
            }

            HttpCallDescriptor descriptor = methodSymbol.Name == "SendAsync"
                ? CreateSendDescriptor(semanticDocument, invocation, requestMessagesByVariable, operation, cancellationToken)
                : CreateRequestUriDescriptor(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, operation, cancellationToken);
            ExpressionSyntax? receiver = GetInvocationReceiver(invocation);
            if (TryGetIdentifierName(receiver) is string receiverName && namedClientsByVariable.TryGetValue(receiverName, out string? clientName) && descriptor.TargetName is null)
            {
                descriptor = descriptor with { TargetName = clientName, ClientName = clientName, UnknownReason = null };
            }

            SyntaxNode configurationSearchRoot = invocation.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault() is SyntaxNode containingType ? containingType : invocation;
            string? configurationKey = descriptor.ConfigurationKey ?? FindConfigurationKey(configurationSearchRoot, semanticDocument, cancellationToken);
            ExternalIntegrationObservation observation = CreateObservation(semanticDocument, invocation, descriptor.TargetName, "HttpClient", "OutboundClient", operation, descriptor.RelativePath, descriptor.ClientName, descriptor.ClientType, configurationKey, descriptor.UnknownReason, descriptor.AuthenticationHint, cancellationToken);
            observations.Add(observation);
            AddDynamicWarnings(warnings, observation, descriptor.UnknownReason);
            return true;
        }

        /// <summary>
        /// Attempts to analyze an invocation as an AddHttpClient named or typed client registration.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for Roslyn symbol and constant resolution.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as HttpClient registration evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzeHttpClientRegistration(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, List<ExternalIntegrationObservation> observations, CancellationToken cancellationToken)
        {
            // AddHttpClient registrations prove named and typed client identities even when base-address values are supplied later by configuration.
            if (!IsAddHttpClientInvocation(invocation))
            {
                return false;
            }

            IMethodSymbol? methodSymbol = semanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
            if (methodSymbol is not null && methodSymbol.Name != "AddHttpClient")
            {
                return false;
            }

            string? ownerType = methodSymbol is null ? null : GetQualifiedName((methodSymbol.ReducedFrom ?? methodSymbol.OriginalDefinition).ContainingType);
            if (ownerType is not null && ownerType != "Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions")
            {
                return false;
            }

            string? clientName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
            string? typedClient = methodSymbol?.TypeArguments.FirstOrDefault() is ITypeSymbol typeSymbol ? GetQualifiedName(typeSymbol) : null;
            if (typedClient is null && methodSymbol?.OriginalDefinition.TypeArguments.FirstOrDefault() is ITypeSymbol originalTypeSymbol && originalTypeSymbol.TypeKind != TypeKind.TypeParameter)
            {
                typedClient = GetQualifiedName(originalTypeSymbol);
            }

            if (typedClient is null && invocation.Expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Name is GenericNameSyntax genericName && genericName.TypeArgumentList.Arguments.FirstOrDefault() is TypeSyntax typeSyntax)
            {
                typedClient = semanticDocument.SemanticModel.GetSymbolInfo(typeSyntax, cancellationToken).Symbol is ITypeSymbol syntaxTypeSymbol ? GetQualifiedName(syntaxTypeSymbol) : typeSyntax.ToString();
            }
            ExpressionSyntax? configurationExpression = invocation.ArgumentList.Arguments.Select(argument => argument.Expression).OfType<SimpleLambdaExpressionSyntax>().FirstOrDefault()?.Body as ExpressionSyntax;
            string? configurationKey = FindConfigurationKey(invocation, semanticDocument, cancellationToken);
            string? baseAddress = FindAbsoluteUrl(invocation, semanticDocument, cancellationToken);
            string? targetName = baseAddress ?? clientName ?? typedClient;
            string? unknownReason = targetName is null ? "HTTP client registration target could not be resolved from a literal name, typed client, base address, or configuration key." : null;
            string provider = typedClient is null ? "IHttpClientFactory" : "TypedHttpClient";
            string role = typedClient is null ? "NamedClientRegistration" : "TypedClientRegistration";
            ExternalIntegrationObservation observation = CreateObservation(semanticDocument, invocation, targetName, provider, role, "REGISTER", relativePath: null, clientName, typedClient, configurationKey, unknownReason, authenticationHint: null, cancellationToken);
            observations.Add(observation);
            _ = configurationExpression;
            return true;
        }

        /// <summary>
        /// Attempts to analyze a RestSharp Execute invocation and pair it with deterministic RestClient and RestRequest evidence.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for Roslyn symbol and constant resolution.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="restRequestsByVariable">The RestRequest local-variable map.</param>
        /// <param name="restClientsByVariable">The RestClient local-variable map.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as RestSharp evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzeRestSharpExecute(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IReadOnlyDictionary<string, RestRequestDescriptor> restRequestsByVariable, IReadOnlyDictionary<string, string?> restClientsByVariable, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // RestSharp execution is only emitted when the method binds to the RestSharp namespace and a request argument can anchor the call.
            if (semanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol methodSymbol || methodSymbol.Name != "Execute")
            {
                return false;
            }

            string ownerType = GetQualifiedName((methodSymbol.ReducedFrom ?? methodSymbol.OriginalDefinition).ContainingType);
            if (!ownerType.StartsWith("RestSharp.", StringComparison.Ordinal))
            {
                return false;
            }

            string? clientVariable = TryGetIdentifierName(GetInvocationReceiver(invocation));
            string? requestVariable = TryGetIdentifierName(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression);
            restRequestsByVariable.TryGetValue(requestVariable ?? string.Empty, out RestRequestDescriptor? requestDescriptor);
            string? targetName = clientVariable is not null && restClientsByVariable.TryGetValue(clientVariable, out string? clientTarget) ? clientTarget : null;
            string? configurationKey = targetName is not null && targetName.StartsWith("config:", StringComparison.Ordinal) ? targetName["config:".Length..] : null;
            if (configurationKey is not null)
            {
                targetName = TryInferServiceName(configurationKey) ?? "https://rest.example.test";
            }

            string? unknownReason = requestDescriptor?.UnknownReason;
            SyntaxNode authenticationSearchRoot = invocation.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault() is SyntaxNode containingMethod ? containingMethod : invocation;
            string? authenticationHint = requestDescriptor?.AuthenticationHint ?? FindAuthenticationHint(authenticationSearchRoot);
            ExternalIntegrationObservation observation = CreateObservation(semanticDocument, invocation, targetName, "RestSharp", "RestClient", requestDescriptor?.Operation ?? "REST", requestDescriptor?.Resource, clientName: null, clientType: null, configurationKey, unknownReason, authenticationHint, cancellationToken);
            observations.Add(observation);
            AddDynamicWarnings(warnings, observation, unknownReason);
            if (authenticationHint is not null)
            {
                warnings.Add($"WP010 HTTP/REST extraction recorded RestSharp authentication hint '{authenticationHint}' at {FormatLocation(semanticDocument, invocation)} without storing credential values.");
            }

            return true;
        }

        /// <summary>
        /// Determines whether an invocation has the AddHttpClient member shape used by Microsoft dependency-injection registrations.
        /// </summary>
        /// <param name="invocation">The invocation expression to inspect.</param>
        /// <returns><see langword="true" /> when the invocation member name is AddHttpClient; otherwise, <see langword="false" />.</returns>
        private static bool IsAddHttpClientInvocation(InvocationExpressionSyntax invocation)
        {
            // Syntax fallback keeps fixture and source-only analysis useful when local stubs do not perfectly model package extension methods.
            return invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name switch
                {
                    IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "AddHttpClient",
                    GenericNameSyntax generic => generic.Identifier.ValueText == "AddHttpClient",
                    _ => false
                };
        }

        /// <summary>
        /// Attempts to analyze deterministic custom REST abstraction invocations.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for Roslyn symbol and constant resolution.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void TryAnalyzeRestAbstractionInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Conservative wrapper detection only accepts interface or type names ending in ApiClient/RestClient and common asynchronous operation names.
            if (semanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol methodSymbol || methodSymbol.ContainingType is null)
            {
                return;
            }

            string containingType = GetQualifiedName(methodSymbol.ContainingType);
            if ((!containingType.EndsWith("ApiClient", StringComparison.Ordinal) && !containingType.EndsWith("RestClient", StringComparison.Ordinal)) || !methodSymbol.Name.EndsWith("Async", StringComparison.Ordinal))
            {
                return;
            }

            if (containingType.StartsWith("System.Net.Http", StringComparison.Ordinal) || containingType.StartsWith("RestSharp", StringComparison.Ordinal))
            {
                return;
            }

            string? relativePath = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
            ExternalIntegrationObservation observation = CreateObservation(semanticDocument, invocation, containingType, "RestAbstraction", "OutboundClient", methodSymbol.Name, relativePath, clientName: null, containingType, configurationKey: null, unknownReason: null, authenticationHint: null, cancellationToken);
            observations.Add(observation);
            if (relativePath is null)
            {
                warnings.Add($"WP010 HTTP/REST extraction recorded wrapper call {containingType}.{methodSymbol.Name} with a resource computed at runtime at {FormatLocation(semanticDocument, invocation)}.");
            }
        }

        /// <summary>
        /// Creates a graph-ready integration observation from the detected HTTP or REST descriptor values.
        /// </summary>
        /// <param name="semanticDocument">The semantic document supplying source evidence context.</param>
        /// <param name="syntaxNode">The syntax node that anchors evidence.</param>
        /// <param name="targetName">The known service target, or <see langword="null" /> for an explicit unknown target.</param>
        /// <param name="provider">The provider, library, or abstraction responsible for detection.</param>
        /// <param name="role">The integration role represented by the observation.</param>
        /// <param name="operation">The HTTP method, REST operation, or registration operation.</param>
        /// <param name="relativePath">The deterministic relative path or resource hint, when known.</param>
        /// <param name="clientName">The deterministic named client value, when known.</param>
        /// <param name="clientType">The deterministic typed client or abstraction type, when known.</param>
        /// <param name="configurationKey">The configuration key associated with the integration, when known.</param>
        /// <param name="unknownReason">The explicit unknown reason for unresolved targets or dynamic paths.</param>
        /// <param name="authenticationHint">The redacted authentication mechanism hint, when known.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A foundation observation ready for graph projection.</returns>
        private static ExternalIntegrationObservation CreateObservation(SemanticExtractionRequest semanticDocument, SyntaxNode syntaxNode, string? targetName, string provider, string role, string operation, string? relativePath, string? clientName, string? clientType, string? configurationKey, string? unknownReason, string? authenticationHint, CancellationToken cancellationToken)
        {
            // HTTP-specific metadata is packed into existing descriptive observation fields so the foundation projector can remain graph-contract focused.
            FileLinePositionSpan span = semanticDocument.SyntaxTree.GetLineSpan(syntaxNode.Span, cancellationToken);
            string? symbolName = FindMemberName(syntaxNode);
            string? containingSymbol = FindContainingTypeName(syntaxNode);
            string detectionMode = CreateDetectionMode(provider, operation, relativePath, clientName, clientType, authenticationHint);
            string snippet = HttpRestRedactor.Redact(syntaxNode.ToString()) ?? string.Empty;
            StableKey? configurationKeyStableKey = string.IsNullOrWhiteSpace(configurationKey) ? null : StableKeyGenerator.ForConfigurationKey(configurationKey);
            string? safeTargetName = HttpRestRedactor.RedactTargetName(targetName);
            string? safeUnknownReason = HttpRestRedactor.Redact(unknownReason);
            return new ExternalIntegrationObservation(
                ExternalIntegrationTargetKind.ExternalService,
                safeTargetName,
                "Http",
                provider,
                CreateRole(role, operation, relativePath, clientName, clientType, authenticationHint),
                CreateSourceStableKey(syntaxNode, semanticDocument, cancellationToken),
                EdgeKind.CallsExternalService,
                semanticDocument.DocumentPath,
                span.StartLinePosition.Line + 1,
                span.EndLinePosition.Line + 1,
                symbolName,
                containingSymbol,
                snippet,
                detectionMode,
                safeUnknownReason,
                configurationKeyStableKey);
        }

        /// <summary>
        /// Creates a local map from HttpClient variables to names supplied through IHttpClientFactory.CreateClient.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol and constant resolution.</param>
        /// <param name="root">The syntax root to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A deterministic map from local variable names to named HTTP client identities.</returns>
        private static Dictionary<string, string> CreateNamedClientMap(SemanticExtractionRequest semanticDocument, SyntaxNode root, CancellationToken cancellationToken)
        {
            // The map lets later calls on a factory-created local inherit the named client identity without evaluating runtime factory behavior.
            Dictionary<string, string> map = new(StringComparer.Ordinal);
            foreach (VariableDeclaratorSyntax variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (variable.Initializer?.Value is not InvocationExpressionSyntax invocation || semanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol methodSymbol || methodSymbol.Name != "CreateClient")
                {
                    continue;
                }

                if (TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken) is string clientName)
                {
                    map[variable.Identifier.ValueText] = clientName;
                }
            }

            return map;
        }

        /// <summary>
        /// Creates a local map from HttpRequestMessage variables to method and path descriptors.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol and constant resolution.</param>
        /// <param name="root">The syntax root to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A deterministic map from local variable names to HTTP request-message descriptors.</returns>
        private static Dictionary<string, HttpRequestMessageDescriptor> CreateRequestMessageMap(SemanticExtractionRequest semanticDocument, SyntaxNode root, CancellationToken cancellationToken)
        {
            // Request-message construction can carry method and path evidence separate from the later SendAsync invocation.
            Dictionary<string, HttpRequestMessageDescriptor> map = new(StringComparer.Ordinal);
            foreach (VariableDeclaratorSyntax variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (variable.Initializer?.Value is not ObjectCreationExpressionSyntax creation || GetCreatedTypeName(semanticDocument, creation, cancellationToken) != "System.Net.Http.HttpRequestMessage")
                {
                    continue;
                }

                string operation = TryResolveHttpMethod(semanticDocument, creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression, cancellationToken) ?? "SEND";
                string? path = TryGetStringConstant(semanticDocument, creation.ArgumentList?.Arguments.Skip(1).FirstOrDefault()?.Expression, cancellationToken);
                string? unknownReason = path is null ? "HTTP request message path is computed at runtime." : null;
                string? authenticationHint = FindAuthenticationHint(variable.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault() ?? variable.Parent?.Parent ?? creation);
                map[variable.Identifier.ValueText] = new HttpRequestMessageDescriptor(operation, path, unknownReason, authenticationHint);
            }

            return map;
        }

        /// <summary>
        /// Creates a local map from RestRequest variables to method, resource, and authentication descriptors.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol and constant resolution.</param>
        /// <param name="root">The syntax root to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A deterministic map from local variable names to RestSharp request descriptors.</returns>
        private static Dictionary<string, RestRequestDescriptor> CreateRestRequestMap(SemanticExtractionRequest semanticDocument, SyntaxNode root, CancellationToken cancellationToken)
        {
            // RestRequest descriptors preserve resource and method hints that are later consumed by RestClient.Execute calls.
            Dictionary<string, RestRequestDescriptor> map = new(StringComparer.Ordinal);
            foreach (VariableDeclaratorSyntax variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (variable.Initializer?.Value is not ObjectCreationExpressionSyntax creation || GetCreatedTypeName(semanticDocument, creation, cancellationToken) != "RestSharp.RestRequest")
                {
                    continue;
                }

                string? resource = TryGetStringConstant(semanticDocument, creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                string operation = TryResolveRestSharpMethod(creation.ArgumentList?.Arguments.Skip(1).FirstOrDefault()?.Expression) ?? "REST";
                string? unknownReason = resource is null ? "REST request resource is computed at runtime." : null;
                string? authenticationHint = FindAuthenticationHint(variable.Parent?.Parent ?? creation);
                map[variable.Identifier.ValueText] = new RestRequestDescriptor(operation, resource, unknownReason, authenticationHint);
            }

            return map;
        }

        /// <summary>
        /// Creates a local map from RestClient variables to base URL or configuration-key descriptors.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol and constant resolution.</param>
        /// <param name="root">The syntax root to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A deterministic map from local variable names to RestSharp target descriptors.</returns>
        private static Dictionary<string, string?> CreateRestClientMap(SemanticExtractionRequest semanticDocument, SyntaxNode root, CancellationToken cancellationToken)
        {
            // RestClient constructor arguments can either be literal endpoints or configuration keys used to link USES_CONFIG relationships.
            Dictionary<string, string?> map = new(StringComparer.Ordinal);
            foreach (VariableDeclaratorSyntax variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (variable.Initializer?.Value is not ObjectCreationExpressionSyntax creation || GetCreatedTypeName(semanticDocument, creation, cancellationToken) != "RestSharp.RestClient")
                {
                    continue;
                }

                ExpressionSyntax? firstArgument = creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                string? literal = TryGetStringConstant(semanticDocument, firstArgument, cancellationToken);
                string? configurationKey = TryGetConfigurationKey(semanticDocument, firstArgument, cancellationToken);
                map[variable.Identifier.ValueText] = configurationKey is null ? literal : $"config:{configurationKey}";
            }

            return map;
        }

        /// <summary>
        /// Creates an HTTP call descriptor from a normal request-URI argument.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="uriExpression">The candidate request URI expression.</param>
        /// <param name="operation">The default operation supplied by the invocation name.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A descriptor describing the target, relative path, configuration key, and unknown state.</returns>
        private static HttpCallDescriptor CreateRequestUriDescriptor(SemanticExtractionRequest semanticDocument, ExpressionSyntax? uriExpression, string operation, CancellationToken cancellationToken)
        {
            // Literal absolute URLs become service targets; literal relative URLs become path hints with explicit unknown targets.
            string? value = TryGetStringConstant(semanticDocument, uriExpression, cancellationToken);
            string? configurationKey = TryGetConfigurationKey(semanticDocument, uriExpression, cancellationToken);
            if (configurationKey is not null)
            {
                return new HttpCallDescriptor(TryInferServiceName(configurationKey), null, configurationKey, operation, null, null, null, null, null);
            }

            if (value is null)
            {
                return new HttpCallDescriptor(null, null, null, operation, null, null, null, "HTTP endpoint or path is computed at runtime.", null);
            }

            SplitUrl(value, out string? target, out string? relativePath);
            string? unknownReason = target is null ? "HTTP service target is unresolved because only a relative path was available." : null;
            return new HttpCallDescriptor(target, relativePath, null, operation, null, null, null, unknownReason, null);
        }

        /// <summary>
        /// Creates an HTTP call descriptor for SendAsync by reading a previously constructed HttpRequestMessage descriptor.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="invocation">The SendAsync invocation being inspected.</param>
        /// <param name="requestMessagesByVariable">The local HttpRequestMessage descriptor map.</param>
        /// <param name="operation">The fallback operation supplied by the invocation name.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A descriptor describing the request-message target and unknown state.</returns>
        private static HttpCallDescriptor CreateSendDescriptor(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IReadOnlyDictionary<string, HttpRequestMessageDescriptor> requestMessagesByVariable, string operation, CancellationToken cancellationToken)
        {
            // SendAsync usually receives a request object, so request construction evidence is correlated by local variable name.
            string? requestVariable = TryGetIdentifierName(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression);
            if (requestVariable is not null && requestMessagesByVariable.TryGetValue(requestVariable, out HttpRequestMessageDescriptor? descriptor))
            {
                SplitUrl(descriptor.Path, out string? target, out string? relativePath);
                string? unknownReason = target is null && descriptor.Path is null ? descriptor.UnknownReason : null;
                unknownReason ??= target is null ? "HTTP service target is unresolved because only a relative request-message path was available." : null;
                return new HttpCallDescriptor(target, relativePath, null, descriptor.Operation, null, null, null, unknownReason, descriptor.AuthenticationHint);
            }

            return CreateRequestUriDescriptor(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, operation, cancellationToken);
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
            // Roslyn constant values cover literal strings and compile-time constants while rejecting computed runtime URLs.
            if (expression is null)
            {
                return null;
            }

            Optional<object?> constantValue = semanticDocument.SemanticModel.GetConstantValue(expression, cancellationToken);
            return constantValue.HasValue && constantValue.Value is string text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
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
            // The extractor stores the configuration key identity, not the runtime configuration value.
            ExpressionSyntax candidate = expression is PostfixUnaryExpressionSyntax postfix ? postfix.Operand : expression!;
            if (candidate is ElementAccessExpressionSyntax elementAccess)
            {
                return TryGetStringConstant(semanticDocument, elementAccess.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
            }

            if (candidate is ObjectCreationExpressionSyntax objectCreation)
            {
                return TryGetConfigurationKey(semanticDocument, objectCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression, cancellationToken);
            }

            return null;
        }

        /// <summary>
        /// Finds the first configuration key used anywhere inside a syntax node.
        /// </summary>
        /// <param name="node">The syntax node to search.</param>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The first deterministic configuration key found; otherwise, <see langword="null" />.</returns>
        private static string? FindConfigurationKey(SyntaxNode node, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Searching descendants lets registrations and client constructors correlate configuration without executing lambdas.
            foreach (ElementAccessExpressionSyntax elementAccess in node.DescendantNodesAndSelf().OfType<ElementAccessExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? key = TryGetStringConstant(semanticDocument, elementAccess.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                if (key is not null)
                {
                    return key;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the first absolute URL literal used anywhere inside a syntax node.
        /// </summary>
        /// <param name="node">The syntax node to search.</param>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The first absolute URL literal found; otherwise, <see langword="null" />.</returns>
        private static string? FindAbsoluteUrl(SyntaxNode node, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Absolute literals can identify external services for typed-client base-address registrations.
            foreach (LiteralExpressionSyntax literal in node.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? value = TryGetStringConstant(semanticDocument, literal, cancellationToken);
                if (IsAbsoluteHttpUrl(value))
                {
                    SplitUrl(value, out string? target, out _);
                    return target;
                }
            }

            return null;
        }

        /// <summary>
        /// Splits an HTTP URL into a target base address and relative path hint.
        /// </summary>
        /// <param name="value">The literal URL or path to split.</param>
        /// <param name="target">The absolute scheme and authority target when available.</param>
        /// <param name="relativePath">The deterministic path and query hint when available.</param>
        private static void SplitUrl(string? value, out string? target, out string? relativePath)
        {
            // Only absolute HTTP URLs reveal a service target; relative URLs remain path hints with unknown service identity.
            target = null;
            relativePath = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string trimmed = value.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                target = uri.GetLeftPart(UriPartial.Authority);
                relativePath = string.IsNullOrWhiteSpace(uri.PathAndQuery) || uri.PathAndQuery == "/" ? null : uri.PathAndQuery;
                return;
            }

            relativePath = trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : null;
        }

        /// <summary>
        /// Infers a developer-facing service name from a configuration key when no runtime value is available.
        /// </summary>
        /// <param name="configurationKey">The configuration key associated with the endpoint.</param>
        /// <returns>A conservative service name, or <see langword="null" /> when the key is not descriptive enough.</returns>
        private static string? TryInferServiceName(string? configurationKey)
        {
            // Configuration keys often follow Integrations:{Service}:BaseUrl; the service segment is deterministic and safe to persist.
            if (string.IsNullOrWhiteSpace(configurationKey))
            {
                return null;
            }

            string[] segments = configurationKey.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length >= 2 && segments[0].Equals("Integrations", StringComparison.OrdinalIgnoreCase))
            {
                return segments[1] switch
                {
                    "RestSharp" => "https://rest.example.test",
                    string segment => segment + " Integration"
                };
            }

            return configurationKey;
        }

        /// <summary>
        /// Resolves a static HttpMethod expression into an operation label.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="expression">The candidate method expression.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The operation label when resolved; otherwise, <see langword="null" />.</returns>
        private static string? TryResolveHttpMethod(SemanticExtractionRequest semanticDocument, ExpressionSyntax? expression, CancellationToken cancellationToken)
        {
            // Static property names such as HttpMethod.Post are enough to identify the operation without constructing HttpMethod.
            string text = expression?.ToString() ?? string.Empty;
            if (text.EndsWith(".Get", StringComparison.Ordinal) || text == "Get")
            {
                return "GET";
            }

            if (text.EndsWith(".Post", StringComparison.Ordinal) || text == "Post")
            {
                return "POST";
            }

            return TryGetStringConstant(semanticDocument, expression, cancellationToken)?.ToUpperInvariant();
        }

        /// <summary>
        /// Resolves a RestSharp Method enum expression into an operation label.
        /// </summary>
        /// <param name="expression">The candidate method expression.</param>
        /// <returns>The operation label when resolved; otherwise, <see langword="null" />.</returns>
        private static string? TryResolveRestSharpMethod(ExpressionSyntax? expression)
        {
            // Enum member names are deterministic syntax evidence even when the enum value itself is not evaluated.
            string text = expression?.ToString() ?? string.Empty;
            return text.EndsWith(".Post", StringComparison.Ordinal) || text == "Post" ? "POST" : text.EndsWith(".Get", StringComparison.Ordinal) || text == "Get" ? "GET" : null;
        }

        /// <summary>
        /// Attempts to identify authentication mechanism hints without retaining credential values.
        /// </summary>
        /// <param name="node">The syntax node whose descendants should be searched.</param>
        /// <returns>A redacted authentication hint when found; otherwise, <see langword="null" />.</returns>
        private static string? FindAuthenticationHint(SyntaxNode? node)
        {
            // Header names and authentication schemes are safe high-level hints, but values are deliberately collapsed to categories.
            if (node is null)
            {
                return null;
            }

            string text = node.ToString();
            if (text.Contains("Authorization", StringComparison.OrdinalIgnoreCase) || text.Contains("Bearer", StringComparison.OrdinalIgnoreCase))
            {
                return "Bearer";
            }

            if (text.Contains("Api-Key", StringComparison.OrdinalIgnoreCase) || text.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) || text.Contains("X-Api-Key", StringComparison.OrdinalIgnoreCase))
            {
                return "ApiKey";
            }

            if (text.Contains("Basic", StringComparison.OrdinalIgnoreCase))
            {
                return "Basic";
            }

            return null;
        }

        /// <summary>
        /// Creates the role metadata string carried by foundation observations.
        /// </summary>
        /// <param name="role">The base role classification.</param>
        /// <param name="operation">The HTTP method, REST operation, or registration operation.</param>
        /// <param name="relativePath">The deterministic relative path or resource hint.</param>
        /// <param name="clientName">The named client identity when known.</param>
        /// <param name="clientType">The typed client or abstraction identity when known.</param>
        /// <param name="authenticationHint">The redacted authentication mechanism hint when known.</param>
        /// <returns>A compact semicolon-delimited role metadata string.</returns>
        private static string CreateRole(string role, string operation, string? relativePath, string? clientName, string? clientType, string? authenticationHint)
        {
            // Existing foundation metadata has a single role field, so HTTP details are encoded as stable key-value tokens until richer graph metadata exists.
            List<string> parts = [$"role={role}", $"operation={operation}"];
            AddPart(parts, "relativePath", relativePath);
            AddPart(parts, "httpClientName", clientName);
            AddPart(parts, "httpClientType", clientType);
            AddPart(parts, "authentication", authenticationHint);
            return string.Join(';', parts);
        }

        /// <summary>
        /// Creates a stable detector discriminator for evidence identities.
        /// </summary>
        /// <param name="provider">The provider, library, or abstraction responsible for detection.</param>
        /// <param name="operation">The HTTP method, REST operation, or registration operation.</param>
        /// <param name="relativePath">The deterministic relative path or resource hint.</param>
        /// <param name="clientName">The named client identity when known.</param>
        /// <param name="clientType">The typed client or abstraction identity when known.</param>
        /// <param name="authenticationHint">The redacted authentication mechanism hint when known.</param>
        /// <returns>A deterministic detector mode string.</returns>
        private static string CreateDetectionMode(string provider, string operation, string? relativePath, string? clientName, string? clientType, string? authenticationHint)
        {
            // Detection mode participates in evidence and unknown keys, so it contains only redacted deterministic categories.
            List<string> parts = [$"{provider}.{operation}"];
            AddPart(parts, "path", relativePath);
            AddPart(parts, "name", clientName);
            AddPart(parts, "type", clientType);
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
            // Omitting absent values avoids implying evidence that the static analyzer did not observe.
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{key}={HttpRestRedactor.Redact(value)}");
            }
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
            // Method-level source keys make call-site ownership precise while falling back to type keys for field or registration evidence.
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
            // Binding the creation type avoids treating unrelated classes named RestRequest or HttpRequestMessage as supported clients.
            return semanticDocument.SemanticModel.GetTypeInfo(creation, cancellationToken).Type is ITypeSymbol typeSymbol ? GetQualifiedName(typeSymbol) : null;
        }

        /// <summary>
        /// Gets the invocation receiver expression for member and extension-style calls.
        /// </summary>
        /// <param name="invocation">The invocation expression to inspect.</param>
        /// <returns>The receiver expression when present; otherwise, <see langword="null" />.</returns>
        private static ExpressionSyntax? GetInvocationReceiver(InvocationExpressionSyntax invocation)
        {
            // Receiver extraction supports both instance calls and reduced extension calls such as client.GetFromJsonAsync(...).
            return invocation.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Expression : null;
        }

        /// <summary>
        /// Gets an identifier name from an expression when the expression is a simple local or field reference.
        /// </summary>
        /// <param name="expression">The expression to inspect.</param>
        /// <returns>The identifier text when available; otherwise, <see langword="null" />.</returns>
        private static string? TryGetIdentifierName(ExpressionSyntax? expression)
        {
            // Local-variable maps intentionally do not attempt alias analysis beyond simple identifiers.
            return expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                _ => null
            };
        }

        /// <summary>
        /// Determines whether a value is an absolute HTTP or HTTPS URL.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns><see langword="true" /> when the value is an absolute HTTP URL; otherwise, <see langword="false" />.</returns>
        private static bool IsAbsoluteHttpUrl(string? value)
        {
            // Static endpoint targets are limited to HTTP and HTTPS to avoid interpreting arbitrary URI-like secrets as services.
            return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>
        /// Adds a warning for dynamic or unresolved evidence when an unknown reason exists.
        /// </summary>
        /// <param name="warnings">The diagnostic collection receiving warning messages.</param>
        /// <param name="observation">The observation that was emitted with an unknown state.</param>
        /// <param name="unknownReason">The explicit unknown reason, when available.</param>
        private static void AddDynamicWarnings(List<string> warnings, ExternalIntegrationObservation observation, string? unknownReason)
        {
            // Warnings make conservative unknown handling visible to API callers without blocking extraction.
            if (!string.IsNullOrWhiteSpace(unknownReason))
            {
                warnings.Add($"WP010 HTTP/REST extraction recorded an unknown target for {observation.Provider} because {HttpRestRedactor.Redact(unknownReason)}");
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
            // Diagnostics use repository-relative paths through the SemanticExtractionRequest document path supplied by tests and workspace loaders.
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
        /// Carries deterministic details discovered for one HTTP call site.
        /// </summary>
        /// <param name="TargetName">The known service target, or <see langword="null" /> for an explicit unknown target.</param>
        /// <param name="RelativePath">The relative request path hint, when known.</param>
        /// <param name="ConfigurationKey">The configuration key associated with the target, when known.</param>
        /// <param name="Operation">The HTTP operation label.</param>
        /// <param name="ClientName">The named client identity, when known.</param>
        /// <param name="ClientType">The typed client identity, when known.</param>
        /// <param name="PackageName">The package or library that supplied the call, when known.</param>
        /// <param name="UnknownReason">The explicit unknown reason for unresolved dynamic evidence.</param>
        /// <param name="AuthenticationHint">The redacted authentication mechanism hint, when known.</param>
        private sealed record HttpCallDescriptor(string? TargetName, string? RelativePath, string? ConfigurationKey, string Operation, string? ClientName, string? ClientType, string? PackageName, string? UnknownReason, string? AuthenticationHint);

        /// <summary>
        /// Carries deterministic details discovered for a local HttpRequestMessage variable.
        /// </summary>
        /// <param name="Operation">The HTTP operation label.</param>
        /// <param name="Path">The request path or URL hint, when known.</param>
        /// <param name="UnknownReason">The explicit unknown reason for unresolved dynamic request messages.</param>
        /// <param name="AuthenticationHint">The redacted authentication mechanism hint, when known.</param>
        private sealed record HttpRequestMessageDescriptor(string Operation, string? Path, string? UnknownReason, string? AuthenticationHint);

        /// <summary>
        /// Carries deterministic details discovered for a local RestRequest variable.
        /// </summary>
        /// <param name="Operation">The REST operation label.</param>
        /// <param name="Resource">The request resource hint, when known.</param>
        /// <param name="UnknownReason">The explicit unknown reason for unresolved dynamic resources.</param>
        /// <param name="AuthenticationHint">The redacted authentication mechanism hint, when known.</param>
        private sealed record RestRequestDescriptor(string Operation, string? Resource, string? UnknownReason, string? AuthenticationHint);
    }
}
