using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Integrations.Foundation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Archon.Extractors.Integrations.InternalServices
{
    /// <summary>
    /// Correlates deterministic client-side HTTP calls with endpoint facts owned by other analyzed projects.
    /// </summary>
    /// <remarks>
    /// The extractor only uses static source, semantic information, and prior endpoint facts. It never starts an application, resolves DNS, sends HTTP requests, or infers ownership from naming alone.
    /// </remarks>
    public sealed class InternalServiceIntegrationExtractor
    {
        /// <summary>
        /// Extracts internal service correlation facts from the supplied source documents and prior endpoint facts.
        /// </summary>
        /// <param name="request">The extraction request containing snapshot identity, source documents, and deterministic endpoint facts.</param>
        /// <param name="cancellationToken">A token that signals when endpoint indexing, source traversal, or graph projection should stop.</param>
        /// <returns>The internal service correlation result containing a partial graph snapshot.</returns>
        public InternalServiceIntegrationExtractionResult Extract(InternalServiceIntegrationExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // Internal correlation follows the same collect-then-project shape as other WP010 slices so quality behavior stays consistent.
            ArgumentNullException.ThrowIfNull(request);
            InternalServiceEndpointIndex endpointIndex = InternalServiceEndpointIndex.Create(request.Endpoints, cancellationToken);
            List<ExternalIntegrationObservation> observations = [];
            List<string> warnings = [];
            foreach (Archon.Roslyn.SemanticModel.SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeSemanticDocument(semanticDocument, endpointIndex, observations, warnings, cancellationToken);
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

            return new InternalServiceIntegrationExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Analyzes one C# semantic document for deterministic internal service client calls.
        /// </summary>
        /// <param name="semanticDocument">The Roslyn semantic document being inspected.</param>
        /// <param name="endpointIndex">The prior endpoint fact index used for deterministic route ownership lookup.</param>
        /// <param name="observations">The observation collection receiving graph-ready internal service facts.</param>
        /// <param name="warnings">The diagnostic collection receiving unresolved ownership and route warnings.</param>
        /// <param name="cancellationToken">A token that signals when traversal should stop.</param>
        private static void AnalyzeSemanticDocument(Archon.Roslyn.SemanticModel.SemanticExtractionRequest semanticDocument, InternalServiceEndpointIndex endpointIndex, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Work Item 6 starts with C# syntax support; non-C# documents are explicitly reported as a parity limit rather than silently pretending support.
            if (semanticDocument.SyntaxTree.GetRoot(cancellationToken) is not Microsoft.CodeAnalysis.CSharp.CSharpSyntaxNode root)
            {
                warnings.Add("WP010 internal service correlation skipped a non-C# document because this slice currently supports C# syntax for client call extraction while preserving Roslyn semantic parity limits.");
                return;
            }

            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeInvocation(semanticDocument, invocation, endpointIndex, observations, warnings, cancellationToken);
            }
        }

        /// <summary>
        /// Attempts to correlate one invocation with an internal endpoint fact.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol and constant resolution.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="endpointIndex">The deterministic endpoint fact index.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving unknown ownership details.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AnalyzeInvocation(Archon.Roslyn.SemanticModel.SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, InternalServiceEndpointIndex endpointIndex, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // A call is internal only when the client invocation, route evidence, and prior endpoint fact all agree deterministically.
            if (!TryCreateCallDescriptor(semanticDocument, invocation, cancellationToken, out InternalServiceCallDescriptor? descriptor))
            {
                return;
            }

            if (descriptor is null)
            {
                return;
            }

            if (descriptor.UnknownReason is not null)
            {
                ExternalIntegrationObservation unknownObservation = CreateObservation(semanticDocument, invocation, descriptor, endpoint: null, descriptor.UnknownReason, cancellationToken);
                observations.Add(unknownObservation);
                warnings.Add($"WP010 internal service correlation recorded an unknown target because {descriptor.UnknownReason}");
                return;
            }

            InternalServiceEndpointFact? endpoint = endpointIndex.Find(descriptor.HttpMethod, descriptor.RelativePath, descriptor.BaseUrl, descriptor.ConfigurationKey);
            if (endpoint is null)
            {
                string unknownReason = "Internal service ownership could not be resolved from prior endpoint facts and deterministic route or configuration evidence.";
                ExternalIntegrationObservation unknownObservation = CreateObservation(semanticDocument, invocation, descriptor, endpoint: null, unknownReason, cancellationToken);
                observations.Add(unknownObservation);
                warnings.Add($"WP010 internal service correlation recorded an unknown target because {unknownReason}");
                return;
            }

            observations.Add(CreateObservation(semanticDocument, invocation, descriptor, endpoint, unknownReason: null, cancellationToken));
        }

        /// <summary>
        /// Attempts to create a static call descriptor for supported HTTP-style internal client invocations.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for constant and symbol resolution.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <param name="descriptor">The resolved call descriptor when supported evidence is found.</param>
        /// <returns><see langword="true" /> when the invocation is a supported internal-correlation candidate; otherwise, <see langword="false" />.</returns>
        private static bool TryCreateCallDescriptor(Archon.Roslyn.SemanticModel.SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, CancellationToken cancellationToken, out InternalServiceCallDescriptor? descriptor)
        {
            // Supported invocations are intentionally narrow and must expose an HTTP operation plus route evidence.
            descriptor = null;
            if (semanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol methodSymbol)
            {
                return false;
            }

            string operation = methodSymbol.Name switch
            {
                "GetAsync" or "GetFromJsonAsync" => "GET",
                "PostAsync" or "PostAsJsonAsync" => "POST",
                "PutAsync" => "PUT",
                "DeleteAsync" => "DELETE",
                _ => string.Empty
            };
            if (string.IsNullOrWhiteSpace(operation))
            {
                return false;
            }

            string ownerType = GetQualifiedName((methodSymbol.ReducedFrom ?? methodSymbol.OriginalDefinition).ContainingType);
            if (ownerType != "System.Net.Http.HttpClient" && ownerType != "System.Net.Http.Json.HttpClientJsonExtensions")
            {
                return false;
            }

            string? requestValue = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
            if (requestValue is null)
            {
                descriptor = new InternalServiceCallDescriptor(operation, null, null, null, "Internal service route target is computed at runtime.");
                return true;
            }

            SplitUrl(requestValue, out string? baseUrl, out string? relativePath);
            SyntaxNode configurationSearchRoot = invocation.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault() is SyntaxNode containingType ? containingType : invocation;
            string? configurationKey = FindConfigurationKey(configurationSearchRoot, semanticDocument, cancellationToken);
            string? unknownReason = relativePath is null ? "Internal service route target could not be resolved from a literal path or URL." : null;
            descriptor = new InternalServiceCallDescriptor(operation, relativePath, baseUrl, configurationKey, unknownReason);
            return true;
        }

        /// <summary>
        /// Creates a foundation observation for a known or unknown internal service correlation candidate.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that owns the invocation evidence.</param>
        /// <param name="invocation">The invocation expression anchoring evidence.</param>
        /// <param name="descriptor">The deterministic call descriptor.</param>
        /// <param name="endpoint">The matched endpoint fact, or <see langword="null" /> when ownership is unknown.</param>
        /// <param name="unknownReason">The explicit unknown reason when no deterministic endpoint match exists.</param>
        /// <param name="cancellationToken">A token that signals when source location binding should stop.</param>
        /// <returns>A graph-ready foundation observation.</returns>
        private static ExternalIntegrationObservation CreateObservation(Archon.Roslyn.SemanticModel.SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, InternalServiceCallDescriptor descriptor, InternalServiceEndpointFact? endpoint, string? unknownReason, CancellationToken cancellationToken)
        {
            // Internal service calls still project to ExternalService nodes so existing WP010 consumers can display all outbound dependencies consistently.
            FileLinePositionSpan span = semanticDocument.SyntaxTree.GetLineSpan(invocation.Span, cancellationToken);
            string targetName = endpoint?.ProjectName ?? descriptor.BaseUrl ?? "Internal service";
            string role = CreateRole(descriptor, endpoint);
            StableKey? configurationKey = descriptor.ConfigurationKey is null ? null : StableKeyGenerator.ForConfigurationKey(descriptor.ConfigurationKey);
            return new ExternalIntegrationObservation(
                ExternalIntegrationTargetKind.ExternalService,
                unknownReason is null ? targetName : null,
                "InternalService",
                "InternalServiceCorrelation",
                role,
                CreateSourceStableKey(invocation, semanticDocument, cancellationToken),
                EdgeKind.CallsExternalService,
                semanticDocument.DocumentPath,
                span.StartLinePosition.Line + 1,
                span.EndLinePosition.Line + 1,
                FindMemberName(invocation),
                FindContainingTypeName(invocation),
                InternalServiceIntegrationRedactor.Redact(invocation.ToString()),
                CreateDetectionMode(descriptor, endpoint),
                unknownReason,
                configurationKey);
        }

        /// <summary>
        /// Creates structured role metadata for foundation projection.
        /// </summary>
        /// <param name="descriptor">The call descriptor supplying method, path, and configuration evidence.</param>
        /// <param name="endpoint">The matched endpoint fact, when known.</param>
        /// <returns>A semicolon-delimited metadata role string.</returns>
        private static string CreateRole(InternalServiceCallDescriptor descriptor, InternalServiceEndpointFact? endpoint)
        {
            // Metadata records both the client-side route evidence and the provider-side graph identities used for correlation.
            List<string> parts = ["role=InternalClient", $"operation={descriptor.HttpMethod}", "isInternalService=true"];
            if (endpoint is not null)
            {
                parts.Add("confidenceReason=Internal service target is correlated by deterministic route evidence and prior endpoint facts.");
            }

            AddPart(parts, "relativePath", descriptor.RelativePath);
            AddPart(parts, "baseUrl", descriptor.BaseUrl);
            AddPart(parts, "baseUrlKey", descriptor.ConfigurationKey);
            AddPart(parts, "endpointStableKey", endpoint?.EndpointStableKey.Value);
            AddPart(parts, "controllerStableKey", endpoint?.ControllerStableKey?.Value);
            AddPart(parts, "methodStableKey", endpoint?.MethodStableKey?.Value);
            AddPart(parts, "projectStableKey", endpoint?.ProjectStableKey.Value);
            AddPart(parts, "internalServiceName", endpoint?.ProjectName);
            return string.Join(';', parts);
        }

        /// <summary>
        /// Creates a deterministic detection-mode discriminator for evidence and unknown stable keys.
        /// </summary>
        /// <param name="descriptor">The call descriptor supplying route evidence.</param>
        /// <param name="endpoint">The matched endpoint fact, when known.</param>
        /// <returns>A deterministic detection mode string.</returns>
        private static string CreateDetectionMode(InternalServiceCallDescriptor descriptor, InternalServiceEndpointFact? endpoint)
        {
            // Detection mode avoids secret values and uses stable graph identities where available.
            List<string> parts = [$"InternalService.{descriptor.HttpMethod}"];
            AddPart(parts, "path", descriptor.RelativePath);
            AddPart(parts, "baseUrlKey", descriptor.ConfigurationKey);
            AddPart(parts, "endpoint", endpoint?.EndpointStableKey.Value);
            return string.Join('|', parts);
        }

        /// <summary>
        /// Finds the first configuration key used inside a syntax node.
        /// </summary>
        /// <param name="node">The syntax node to inspect.</param>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="cancellationToken">A token that signals when traversal should stop.</param>
        /// <returns>The first deterministic configuration key found; otherwise, <see langword="null" />.</returns>
        private static string? FindConfigurationKey(SyntaxNode node, Archon.Roslyn.SemanticModel.SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Configuration keys are deterministic evidence for base-URL ownership, but runtime values are never read.
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
        /// Resolves a compile-time string constant without evaluating runtime code.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="expression">The expression that may contain a constant string.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The trimmed constant string, or <see langword="null" /> when the expression is not deterministic.</returns>
        private static string? TryGetStringConstant(Archon.Roslyn.SemanticModel.SemanticExtractionRequest semanticDocument, ExpressionSyntax? expression, CancellationToken cancellationToken)
        {
            // Roslyn constants include literals and compile-time constants while rejecting runtime-computed paths.
            if (expression is null)
            {
                return null;
            }

            Optional<object?> constantValue = semanticDocument.SemanticModel.GetConstantValue(expression, cancellationToken);
            return constantValue.HasValue && constantValue.Value is string text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
        }

        /// <summary>
        /// Splits a literal request value into base URL and route components.
        /// </summary>
        /// <param name="value">The literal request URL or path.</param>
        /// <param name="baseUrl">The absolute service base URL when the value contains one.</param>
        /// <param name="relativePath">The normalized relative route path when available.</param>
        private static void SplitUrl(string? value, out string? baseUrl, out string? relativePath)
        {
            // Absolute URLs prove both base URL and route; relative URLs can still be matched when endpoint facts make the route unique.
            baseUrl = null;
            relativePath = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string trimmed = value.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                baseUrl = uri.GetLeftPart(UriPartial.Authority);
                relativePath = string.IsNullOrWhiteSpace(uri.PathAndQuery) || uri.PathAndQuery == "/" ? null : uri.AbsolutePath;
                return;
            }

            relativePath = trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed.Split('?', StringSplitOptions.None)[0] : null;
        }

        /// <summary>
        /// Creates the stable key for the source method or type that owns the client call.
        /// </summary>
        /// <param name="syntaxNode">The syntax node anchoring the call.</param>
        /// <param name="semanticDocument">The semantic document used for symbol binding.</param>
        /// <param name="cancellationToken">A token that signals when symbol binding should stop.</param>
        /// <returns>A stable source-node key for the caller.</returns>
        private static string CreateSourceStableKey(SyntaxNode syntaxNode, Archon.Roslyn.SemanticModel.SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Source keys match the existing WP010 method/type key convention so cross-slice deduplication stays stable.
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

            return StableKeyGenerator.ForProject(semanticDocument.ProjectContext).Value;
        }

        /// <summary>
        /// Finds the nearest member name for evidence display.
        /// </summary>
        /// <param name="syntaxNode">The syntax node anchoring evidence.</param>
        /// <returns>The member name when available; otherwise, <see langword="null" />.</returns>
        private static string? FindMemberName(SyntaxNode syntaxNode)
        {
            // Evidence labels are source-navigation aids and do not influence correlation identity.
            return syntaxNode.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText
                ?? syntaxNode.AncestorsAndSelf().OfType<ConstructorDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
        }

        /// <summary>
        /// Finds the nearest containing type name for evidence display.
        /// </summary>
        /// <param name="syntaxNode">The syntax node anchoring evidence.</param>
        /// <returns>The containing type name when available; otherwise, <see langword="null" />.</returns>
        private static string? FindContainingTypeName(SyntaxNode syntaxNode)
        {
            // The simple type name is sufficient for evidence display because stable identities are stored separately.
            return syntaxNode.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
        }

        /// <summary>
        /// Gets a fully qualified metadata name for a Roslyn symbol.
        /// </summary>
        /// <param name="symbol">The symbol to format.</param>
        /// <returns>The fully qualified symbol name without the global namespace prefix.</returns>
        private static string GetQualifiedName(ISymbol symbol)
        {
            // Fully qualified names keep source identities independent of using directives and aliases.
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
        }

        /// <summary>
        /// Adds a key-value metadata part when a deterministic value exists.
        /// </summary>
        /// <param name="parts">The metadata part collection receiving the value.</param>
        /// <param name="key">The metadata key.</param>
        /// <param name="value">The optional metadata value.</param>
        private static void AddPart(List<string> parts, string key, string? value)
        {
            // All values pass through the redactor before they can reach metadata, evidence, diagnostics, or test output.
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{key}={InternalServiceIntegrationRedactor.Redact(value)}");
            }
        }

        /// <summary>
        /// Carries the client-side evidence required for deterministic internal service correlation.
        /// </summary>
        /// <param name="HttpMethod">The HTTP operation used by the client call.</param>
        /// <param name="RelativePath">The deterministic request route path.</param>
        /// <param name="BaseUrl">The deterministic base URL when the client call includes one.</param>
        /// <param name="ConfigurationKey">The configuration key that supplies base URL ownership evidence, when available.</param>
        /// <param name="UnknownReason">The explicit reason the call cannot be correlated, when applicable.</param>
        private sealed record InternalServiceCallDescriptor(string HttpMethod, string? RelativePath, string? BaseUrl, string? ConfigurationKey, string? UnknownReason);
    }
}
