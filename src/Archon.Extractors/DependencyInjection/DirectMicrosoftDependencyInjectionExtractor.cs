using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Archon.Extractors.DependencyInjection
{
    /// <summary>
    /// Extracts Microsoft dependency-injection, hosted-service, and HttpClient registration facts from a C# semantic document.
    /// </summary>
    /// <remarks>
    /// The extractor keeps the Work Item 1 public entry point while expanding the symbol catalog for Work Item 2. It relies on Roslyn symbol binding instead of textual method names alone, then emits graph-ready type nodes, registration edges, source evidence, deterministic metadata, and explicit unknown state through the shared snapshot accumulator.
    /// </remarks>
    public sealed class DirectMicrosoftDependencyInjectionExtractor
    {
        /// <summary>
        /// Defines the maximum number of wrapper method hops the extractor will follow from a startup invocation.
        /// </summary>
        private const int MaximumWrapperDepth = 4;

        /// <summary>
        /// Stores the fully qualified type that owns standard Microsoft service collection registration extension methods.
        /// </summary>
        private const string MicrosoftRegistrationExtensionType = "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions";

        /// <summary>
        /// Stores the fully qualified type that owns Microsoft descriptor-based registration extension methods.
        /// </summary>
        private const string MicrosoftDescriptorExtensionType = "Microsoft.Extensions.DependencyInjection.ServiceCollectionDescriptorExtensions";

        /// <summary>
        /// Stores the fully qualified type that owns Microsoft hosted-service registration extension methods.
        /// </summary>
        private const string MicrosoftHostedServiceExtensionType = "Microsoft.Extensions.DependencyInjection.ServiceCollectionHostedServiceExtensions";

        /// <summary>
        /// Stores the fully qualified type that owns Microsoft HttpClientFactory service collection extension methods.
        /// </summary>
        private const string MicrosoftHttpClientExtensionType = "Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions";

        /// <summary>
        /// Stores the fully qualified type used as the canonical hosted-service service abstraction.
        /// </summary>
        private const string HostedServiceTypeName = "Microsoft.Extensions.Hosting.IHostedService";

        /// <summary>
        /// Stores the fully qualified type used as the canonical HttpClientFactory service abstraction.
        /// </summary>
        private const string HttpClientFactoryTypeName = "System.Net.Http.IHttpClientFactory";

        /// <summary>
        /// Stores the fully qualified type used for named HttpClient implementation nodes.
        /// </summary>
        private const string HttpClientTypeName = "System.Net.Http.HttpClient";

        /// <summary>
        /// Stores the extractor-local node name used when a legacy container registration proves container use but not concrete endpoints.
        /// </summary>
        private const string UnknownLegacyServiceTypeName = "LegacyContainer.UnknownService";

        /// <summary>
        /// Stores the extractor-local implementation placeholder used for unsupported legacy container shapes.
        /// </summary>
        private const string UnknownLegacyImplementationTypeName = "LegacyContainer.UnknownImplementation";

        /// <summary>
        /// Extracts Microsoft dependency-injection registration facts from the supplied semantic document.
        /// </summary>
        /// <param name="request">The snapshot and semantic document context that scopes extraction and source evidence.</param>
        /// <param name="cancellationToken">A token that signals when syntax traversal and semantic binding should stop.</param>
        /// <returns>A dependency-injection extraction result containing graph-ready snapshot contributions and diagnostics.</returns>
        public DependencyInjectionExtractionResult Extract(DependencyInjectionExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // One syntax pass keeps the extractor deterministic; each supported invocation contributes through stable-keyed accumulation.
            ArgumentNullException.ThrowIfNull(request);
            ArchitectureSnapshotAccumulator accumulator = new();
            SyntaxNode root = request.SemanticDocument.SyntaxTree.GetRoot(cancellationToken);
            WrapperTraversalContext rootContext = WrapperTraversalContext.Root();

            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeInvocation(request, accumulator, invocation, rootContext, cancellationToken);
            }

            return new DependencyInjectionExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Analyzes one invocation as either a direct registration or a wrapper call that may contain registrations.
        /// </summary>
        /// <param name="request">The snapshot and semantic document context that scopes extraction and source evidence.</param>
        /// <param name="accumulator">The shared snapshot accumulator receiving graph facts and diagnostics.</param>
        /// <param name="invocation">The invocation syntax node being inspected.</param>
        /// <param name="context">The wrapper traversal context that describes the current invocation chain.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AnalyzeInvocation(DependencyInjectionExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, InvocationExpressionSyntax invocation, WrapperTraversalContext context, CancellationToken cancellationToken)
        {
            // Direct registrations are emitted immediately; non-registration invocations are then considered as wrapper calls.
            if (TryAccumulateRegistration(request, accumulator, invocation, context, cancellationToken))
            {
                return;
            }

            TryAnalyzeWrapperInvocation(request, accumulator, invocation, context, cancellationToken);
        }

        /// <summary>
        /// Attempts to classify and accumulate one supported registration invocation.
        /// </summary>
        /// <param name="request">The snapshot and semantic document context that scopes extraction and source evidence.</param>
        /// <param name="accumulator">The shared snapshot accumulator receiving graph facts and diagnostics.</param>
        /// <param name="invocation">The invocation syntax node being inspected.</param>
        /// <param name="context">The wrapper traversal context that describes how this registration was reached.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true"/> when a supported registration was accumulated; otherwise, <see langword="false"/>.</returns>
        private static bool TryAccumulateRegistration(DependencyInjectionExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, InvocationExpressionSyntax invocation, WrapperTraversalContext context, CancellationToken cancellationToken)
        {
            // Unsupported or unbound invocations are ignored so the extractor never turns text-only guesses into graph facts.
            SymbolInfo symbolInfo = request.SemanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            {
                return false;
            }

            IMethodSymbol canonicalMethod = methodSymbol.ReducedFrom ?? methodSymbol.OriginalDefinition;
            RegistrationDescriptor? descriptor = TryCreateRegistrationDescriptor(request.SemanticDocument, invocation, canonicalMethod, methodSymbol, context, cancellationToken);
            if (descriptor is null)
            {
                return false;
            }

            SemanticEvidence evidence = CreateEvidence(request.SemanticDocument, invocation, methodSymbol, cancellationToken);
            EvidenceRecord evidenceRecord = CreateEvidenceRecord(request.SnapshotStableKey, evidence, descriptor, "RegistrationInvocation");
            ArchitectureNode serviceNode = CreateTypeNode(request.SnapshotStableKey, descriptor.ServiceTypeName, descriptor.ServiceDisplayName, descriptor.ProjectStableKey, evidenceRecord.StableKey, descriptor.ServiceNodeKind, descriptor.ServiceNodeMetadataSource);
            ArchitectureNode implementationNode = CreateTypeNode(request.SnapshotStableKey, descriptor.ImplementationTypeName, descriptor.ImplementationDisplayName, descriptor.ProjectStableKey, evidenceRecord.StableKey, descriptor.ImplementationNodeKind, descriptor.ImplementationNodeMetadataSource);
            ArchitectureEdge registrationEdge = CreateRegistrationEdge(request.SnapshotStableKey, descriptor, implementationNode.StableKey, serviceNode.StableKey, evidenceRecord.StableKey);

            accumulator
                .AddEvidence(evidenceRecord)
                .AddNode(serviceNode)
                .AddNode(implementationNode)
                .AddEdge(registrationEdge);

            if (descriptor.UnknownRegistration)
            {
                // Unsupported legacy scanning APIs are retained as explicit unknown graph facts and also surfaced as warnings so orchestration callers can see why the mapping is incomplete.
                accumulator.AddWarning($"Unsupported legacy container registration '{descriptor.ContainerKind}.{descriptor.RegistrationMethod}' was recorded with unknown service and implementation targets at {FormatInvocationLocation(invocation)}.");
            }

            foreach (EvidenceRecord wrapperEvidence in CreateWrapperEvidenceRecords(request, descriptor, cancellationToken))
            {
                accumulator.AddEvidence(wrapperEvidence);
            }

            AccumulateConstructorCorrelation(request, accumulator, descriptor, implementationNode.StableKey, evidenceRecord.StableKey, cancellationToken);

            return true;
        }

        /// <summary>
        /// Attempts to traverse an invocation that calls a wrapper method accepting IServiceCollection.
        /// </summary>
        /// <param name="request">The snapshot and semantic document context that scopes extraction and source evidence.</param>
        /// <param name="accumulator">The shared snapshot accumulator receiving graph facts and diagnostics.</param>
        /// <param name="invocation">The potential wrapper invocation syntax node.</param>
        /// <param name="context">The current wrapper traversal context.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void TryAnalyzeWrapperInvocation(DependencyInjectionExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, InvocationExpressionSyntax invocation, WrapperTraversalContext context, CancellationToken cancellationToken)
        {
            // Dynamic invocations cannot be bound to a deterministic method body and must remain diagnostic-only.
            SymbolInfo symbolInfo = request.SemanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            {
                if (request.SemanticDocument.SemanticModel.GetTypeInfo(invocation.Expression, cancellationToken).Type?.TypeKind == TypeKind.Dynamic)
                {
                    accumulator.AddWarning($"Unsupported dynamic service-registration invocation at {FormatInvocationLocation(invocation)}.");
                }

                return;
            }

            IMethodSymbol canonicalMethod = methodSymbol.ReducedFrom ?? methodSymbol.OriginalDefinition;
            if (!IsServiceCollectionWrapperMethod(canonicalMethod))
            {
                return;
            }

            if (context.Depth >= MaximumWrapperDepth)
            {
                accumulator.AddWarning($"Wrapper recursion depth limit reached for {GetQualifiedName(methodSymbol)} at {FormatInvocationLocation(invocation)}.");
                return;
            }

            string wrapperKey = GetQualifiedName(methodSymbol.OriginalDefinition);
            if (context.Contains(wrapperKey))
            {
                accumulator.AddWarning($"Wrapper cycle detected for {wrapperKey} at {FormatInvocationLocation(invocation)}.");
                return;
            }

            SyntaxReference? sourceReference = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault(reference => ReferenceEquals(reference.SyntaxTree, request.SemanticDocument.SyntaxTree));
            if (sourceReference is null)
            {
                accumulator.AddWarning($"Wrapper source unavailable for {GetQualifiedName(methodSymbol)} at {FormatInvocationLocation(invocation)}.");
                return;
            }

            if (sourceReference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax methodDeclaration)
            {
                accumulator.AddWarning($"Wrapper source unavailable for {GetQualifiedName(methodSymbol)} at {FormatInvocationLocation(invocation)}.");
                return;
            }

            if (methodDeclaration.Body is null && methodDeclaration.ExpressionBody is null)
            {
                accumulator.AddWarning($"Wrapper source unavailable for {GetQualifiedName(methodSymbol)} at {FormatInvocationLocation(invocation)}.");
                return;
            }

            WrapperInvocation wrapperInvocation = CreateWrapperInvocation(request.SemanticDocument, invocation, methodSymbol, cancellationToken);
            WrapperTraversalContext nextContext = context.Enter(wrapperKey, wrapperInvocation);

            foreach (InvocationExpressionSyntax nestedInvocation in methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeInvocation(request, accumulator, nestedInvocation, nextContext, cancellationToken);
            }
        }

        /// <summary>
        /// Creates a registration descriptor for the supported Work Item 2 Microsoft DI method catalog.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for type and argument resolution.</param>
        /// <param name="invocation">The invocation being classified.</param>
        /// <param name="canonicalMethod">The unreduced or original method definition used to identify extension-method ownership.</param>
        /// <param name="invokedMethod">The invocation-specific method symbol that carries concrete type arguments.</param>
        /// <param name="context">The wrapper traversal context that should annotate registrations discovered inside wrappers.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A registration descriptor when the invocation is supported; otherwise, <see langword="null"/>.</returns>
        private static RegistrationDescriptor? TryCreateRegistrationDescriptor(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IMethodSymbol canonicalMethod, IMethodSymbol invokedMethod, WrapperTraversalContext context, CancellationToken cancellationToken)
        {
            // The catalog dispatches by owner type first because method names such as AddSingleton can exist outside Microsoft DI.
            string ownerType = GetQualifiedName(canonicalMethod.ContainingType);
            RegistrationDescriptor? descriptor = ownerType switch
            {
                MicrosoftRegistrationExtensionType => TryCreateStandardRegistrationDescriptor(semanticDocument, invocation, canonicalMethod, invokedMethod, cancellationToken),
                MicrosoftDescriptorExtensionType => TryCreateDescriptorRegistrationDescriptor(semanticDocument, invocation, canonicalMethod, invokedMethod, cancellationToken),
                MicrosoftHostedServiceExtensionType => TryCreateHostedServiceRegistrationDescriptor(canonicalMethod, invokedMethod),
                MicrosoftHttpClientExtensionType => TryCreateHttpClientRegistrationDescriptor(semanticDocument, invocation, canonicalMethod, invokedMethod, cancellationToken),
                _ => TryCreateLegacyContainerRegistrationDescriptor(semanticDocument, invocation, canonicalMethod, invokedMethod, cancellationToken)
            };

            return descriptor?.WithWrapperContext(context);
        }

        /// <summary>
        /// Creates descriptors for supported legacy container registration APIs and unsupported-but-detectable container forms.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for chained-call and nested-invocation resolution.</param>
        /// <param name="invocation">The invocation being classified as a possible legacy container registration.</param>
        /// <param name="canonicalMethod">The original method definition used to identify container ownership.</param>
        /// <param name="invokedMethod">The concrete invocation method symbol that carries resolved type arguments.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A legacy registration descriptor when the invocation matches a supported or explicitly unknown legacy pattern; otherwise, <see langword="null"/>.</returns>
        private static RegistrationDescriptor? TryCreateLegacyContainerRegistrationDescriptor(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IMethodSymbol canonicalMethod, IMethodSymbol invokedMethod, CancellationToken cancellationToken)
        {
            // Legacy containers use different fluent APIs, so dispatch combines owner identity, method name, and chained invocation context.
            string ownerType = GetQualifiedName(canonicalMethod.ContainingType);
            if (ownerType == "Microsoft.Practices.Unity.UnityContainer" && canonicalMethod.Name == "RegisterType" && invokedMethod.TypeArguments.Length == 2)
            {
                return RegistrationDescriptor.CreateLegacyKnown(invokedMethod.TypeArguments[0], invokedMethod.TypeArguments[1], "Transient", canonicalMethod.Name, ownerType, "Unity", "SymbolResolved");
            }

            if (ownerType == "Microsoft.Practices.Unity.UnityContainer" && canonicalMethod.Name == "RegisterTypes")
            {
                return RegistrationDescriptor.CreateLegacyUnknown("Unity", canonicalMethod.Name, ownerType, "Assembly scanning registration does not expose deterministic service and implementation targets.");
            }

            if (IsAutofacAsRegistration(semanticDocument, invocation, canonicalMethod, invokedMethod, cancellationToken, out ITypeSymbol? autofacImplementationType))
            {
                return RegistrationDescriptor.CreateLegacyKnown(invokedMethod.TypeArguments[0], autofacImplementationType, "Unknown", canonicalMethod.Name, ownerType, "Autofac", "SymbolResolvedChained");
            }

            if (ownerType == "Autofac.ContainerBuilder" && canonicalMethod.Name == "RegisterAssemblyTypes")
            {
                return RegistrationDescriptor.CreateLegacyUnknown("Autofac", canonicalMethod.Name, ownerType, "Assembly scanning registration does not expose deterministic service and implementation targets.");
            }

            if (ownerType == "Castle.Windsor.WindsorContainer" && canonicalMethod.Name == "Register" && TryResolveCastleComponentRegistration(semanticDocument, invocation, cancellationToken, out ITypeSymbol? windsorServiceType, out ITypeSymbol? windsorImplementationType))
            {
                return RegistrationDescriptor.CreateLegacyKnown(windsorServiceType, windsorImplementationType, "Unknown", canonicalMethod.Name, ownerType, "Castle Windsor", "SymbolResolvedNested");
            }

            if (IsStructureMapUseRegistration(semanticDocument, invocation, canonicalMethod, invokedMethod, cancellationToken, out ITypeSymbol? structureMapServiceType))
            {
                return RegistrationDescriptor.CreateLegacyKnown(structureMapServiceType, invokedMethod.TypeArguments[0], "Unknown", canonicalMethod.Name, ownerType, "StructureMap", "SymbolResolvedChained");
            }

            if (IsNinjectToRegistration(semanticDocument, invocation, canonicalMethod, invokedMethod, cancellationToken, out ITypeSymbol? ninjectServiceType))
            {
                return RegistrationDescriptor.CreateLegacyKnown(ninjectServiceType, invokedMethod.TypeArguments[0], "Transient", canonicalMethod.Name, ownerType, "Ninject", "SymbolResolvedChained");
            }

            if (ownerType == "SimpleInjector.Container" && canonicalMethod.Name == "Register" && invokedMethod.TypeArguments.Length == 2)
            {
                return RegistrationDescriptor.CreateLegacyKnown(invokedMethod.TypeArguments[0], invokedMethod.TypeArguments[1], "Unknown", canonicalMethod.Name, ownerType, "SimpleInjector", "SymbolResolved");
            }

            if (TryCreateCommonServiceLocatorDescriptor(invokedMethod, out RegistrationDescriptor? serviceLocatorDescriptor))
            {
                return serviceLocatorDescriptor;
            }

            if (TryCreateManualFactoryDescriptor(semanticDocument, invocation, canonicalMethod, invokedMethod, cancellationToken, out RegistrationDescriptor? manualFactoryDescriptor))
            {
                return manualFactoryDescriptor;
            }

            return null;
        }

        /// <summary>
        /// Creates a conservative descriptor for CommonServiceLocator resolution calls.
        /// </summary>
        /// <param name="invokedMethod">The concrete invocation method symbol carrying the requested service type.</param>
        /// <param name="descriptor">The created service-locator descriptor when the invocation is supported.</param>
        /// <returns><see langword="true"/> when a supported CommonServiceLocator usage was detected; otherwise, <see langword="false"/>.</returns>
        private static bool TryCreateCommonServiceLocatorDescriptor(IMethodSymbol invokedMethod, out RegistrationDescriptor descriptor)
        {
            // Service locator calls prove runtime resolution of an abstraction but not the registered implementation, so a synthetic located implementation is used conservatively.
            descriptor = null!;
            string ownerType = GetQualifiedName(invokedMethod.ContainingType);
            if (ownerType != "Microsoft.Practices.ServiceLocation.IServiceLocator" || invokedMethod.Name != "GetInstance" || invokedMethod.TypeArguments.Length != 1)
            {
                return false;
            }

            ITypeSymbol serviceType = invokedMethod.TypeArguments[0];
            descriptor = RegistrationDescriptor.CreateHeuristic(GetQualifiedName(serviceType), serviceType.Name, $"ServiceLocatorResolved:{GetQualifiedName(serviceType)}", $"Located {serviceType.Name}", "Unknown", invokedMethod.Name, ownerType, "CommonServiceLocator", "ServiceLocator", "CommonServiceLocator resolution proves service-location usage but does not expose the concrete implementation deterministically.");
            return true;
        }

        /// <summary>
        /// Creates a conservative descriptor for manual factory methods that return an abstraction while constructing a concrete implementation.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used to inspect factory method declarations.</param>
        /// <param name="invocation">The candidate manual factory invocation.</param>
        /// <param name="canonicalMethod">The original method definition for the candidate invocation.</param>
        /// <param name="invokedMethod">The concrete invocation method symbol used to resolve the returned service type.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <param name="descriptor">The created manual-factory descriptor when the invocation is supported.</param>
        /// <returns><see langword="true"/> when a deterministic manual factory pattern was detected; otherwise, <see langword="false"/>.</returns>
        private static bool TryCreateManualFactoryDescriptor(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IMethodSymbol canonicalMethod, IMethodSymbol invokedMethod, CancellationToken cancellationToken, out RegistrationDescriptor descriptor)
        {
            // Manual factories are intentionally limited to source methods that return an abstraction and contain a direct new concrete implementation expression.
            _ = invocation;
            descriptor = null!;
            if (!canonicalMethod.Name.Contains("Factory", StringComparison.OrdinalIgnoreCase) && !canonicalMethod.Name.StartsWith("Create", StringComparison.Ordinal))
            {
                return false;
            }

            if (invokedMethod.ReturnType is not INamedTypeSymbol serviceType || serviceType.TypeKind != TypeKind.Interface)
            {
                return false;
            }

            SyntaxReference? sourceReference = invokedMethod.DeclaringSyntaxReferences.FirstOrDefault(reference => ReferenceEquals(reference.SyntaxTree, semanticDocument.SyntaxTree));
            if (sourceReference?.GetSyntax(cancellationToken) is not MethodDeclarationSyntax methodDeclaration)
            {
                return false;
            }

            foreach (ObjectCreationExpressionSyntax creation in methodDeclaration.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                ITypeSymbol? implementationType = semanticDocument.SemanticModel.GetTypeInfo(creation, cancellationToken).Type;
                if (implementationType is not null && ImplementsInterface(implementationType, serviceType))
                {
                    descriptor = RegistrationDescriptor.CreateHeuristic(GetQualifiedName(serviceType), serviceType.Name, GetQualifiedName(implementationType), implementationType.Name, "Unknown", canonicalMethod.Name, GetQualifiedName(invokedMethod.ContainingType), "ManualFactory", "ManualFactory", "Manual factory creates an implementation behind an abstraction, but the pattern is heuristic composition rather than container registration.", implementationType);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether a candidate implementation type implements a specific abstraction interface.
        /// </summary>
        /// <param name="implementationType">The implementation type found in a manual factory body.</param>
        /// <param name="serviceType">The abstraction returned by the manual factory method.</param>
        /// <returns><see langword="true"/> when the implementation satisfies the returned abstraction; otherwise, <see langword="false"/>.</returns>
        private static bool ImplementsInterface(ITypeSymbol implementationType, INamedTypeSymbol serviceType)
        {
            // Symbol equality is preferred because metadata display strings can be ambiguous for nested or generic interface names.
            return implementationType.AllInterfaces.Any(interfaceType => SymbolEqualityComparer.Default.Equals(interfaceType, serviceType));
        }

        /// <summary>
        /// Determines whether an Autofac <c>As&lt;TService&gt;</c> invocation has a preceding <c>RegisterType&lt;TImplementation&gt;</c> call.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used to inspect the preceding fluent invocation.</param>
        /// <param name="invocation">The candidate <c>As</c> invocation.</param>
        /// <param name="canonicalMethod">The original method symbol for the candidate invocation.</param>
        /// <param name="invokedMethod">The concrete method symbol that carries the service type argument.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <param name="implementationType">The implementation type resolved from <c>RegisterType</c> when the chain is supported.</param>
        /// <returns><see langword="true"/> when the invocation is a supported Autofac registration chain; otherwise, <see langword="false"/>.</returns>
        private static bool IsAutofacAsRegistration(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IMethodSymbol canonicalMethod, IMethodSymbol invokedMethod, CancellationToken cancellationToken, out ITypeSymbol implementationType)
        {
            // Autofac separates implementation and service across chained calls; both sides must be symbol-resolved before emitting a fact.
            implementationType = null!;
            if (canonicalMethod.Name != "As" || invokedMethod.TypeArguments.Length != 1 || invocation.Expression is not MemberAccessExpressionSyntax memberAccess || memberAccess.Expression is not InvocationExpressionSyntax precedingInvocation)
            {
                return false;
            }

            SymbolInfo precedingSymbolInfo = semanticDocument.SemanticModel.GetSymbolInfo(precedingInvocation, cancellationToken);
            if (precedingSymbolInfo.Symbol is not IMethodSymbol precedingMethod || precedingMethod.Name != "RegisterType" || GetQualifiedName(precedingMethod.ContainingType) != "Autofac.ContainerBuilder" || precedingMethod.TypeArguments.Length != 1)
            {
                return false;
            }

            implementationType = precedingMethod.TypeArguments[0];
            return true;
        }

        /// <summary>
        /// Resolves Castle Windsor component registrations passed to a container <c>Register</c> invocation.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used to bind nested component factory calls.</param>
        /// <param name="invocation">The Windsor <c>Register</c> invocation.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <param name="serviceType">The service type resolved from <c>Component.For</c> when available.</param>
        /// <param name="implementationType">The implementation type resolved from <c>Component.For</c> when available.</param>
        /// <returns><see langword="true"/> when a supported component registration was resolved; otherwise, <see langword="false"/>.</returns>
        private static bool TryResolveCastleComponentRegistration(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, CancellationToken cancellationToken, out ITypeSymbol serviceType, out ITypeSymbol implementationType)
        {
            // Castle registrations are nested factory calls inside Register(...), so only the first deterministic two-type component shape is emitted.
            serviceType = null!;
            implementationType = null!;
            foreach (InvocationExpressionSyntax nestedInvocation in invocation.ArgumentList.Arguments.SelectMany(argument => argument.Expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()))
            {
                SymbolInfo nestedSymbolInfo = semanticDocument.SemanticModel.GetSymbolInfo(nestedInvocation, cancellationToken);
                if (nestedSymbolInfo.Symbol is IMethodSymbol nestedMethod && nestedMethod.Name == "For" && GetQualifiedName(nestedMethod.ContainingType) == "Castle.MicroKernel.Registration.Component" && nestedMethod.TypeArguments.Length == 2)
                {
                    serviceType = nestedMethod.TypeArguments[0];
                    implementationType = nestedMethod.TypeArguments[1];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether a StructureMap <c>Use&lt;TImplementation&gt;</c> invocation has a preceding <c>For&lt;TService&gt;</c> call.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used to inspect the preceding fluent invocation.</param>
        /// <param name="invocation">The candidate <c>Use</c> invocation.</param>
        /// <param name="canonicalMethod">The original method symbol for the candidate invocation.</param>
        /// <param name="invokedMethod">The concrete method symbol that carries the implementation type argument.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <param name="serviceType">The service type resolved from <c>For</c> when the chain is supported.</param>
        /// <returns><see langword="true"/> when the invocation is a supported StructureMap registration chain; otherwise, <see langword="false"/>.</returns>
        private static bool IsStructureMapUseRegistration(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IMethodSymbol canonicalMethod, IMethodSymbol invokedMethod, CancellationToken cancellationToken, out ITypeSymbol serviceType)
        {
            // StructureMap encodes service and implementation in adjacent fluent methods.
            serviceType = null!;
            if (canonicalMethod.Name != "Use" || invokedMethod.TypeArguments.Length != 1 || invocation.Expression is not MemberAccessExpressionSyntax memberAccess || memberAccess.Expression is not InvocationExpressionSyntax precedingInvocation)
            {
                return false;
            }

            SymbolInfo precedingSymbolInfo = semanticDocument.SemanticModel.GetSymbolInfo(precedingInvocation, cancellationToken);
            if (precedingSymbolInfo.Symbol is not IMethodSymbol precedingMethod || precedingMethod.Name != "For" || GetQualifiedName(precedingMethod.ContainingType) != "StructureMap.ConfigurationExpression" || precedingMethod.TypeArguments.Length != 1)
            {
                return false;
            }

            serviceType = precedingMethod.TypeArguments[0];
            return true;
        }

        /// <summary>
        /// Determines whether a Ninject <c>To&lt;TImplementation&gt;</c> invocation has a preceding <c>Bind&lt;TService&gt;</c> call.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used to inspect the preceding fluent invocation.</param>
        /// <param name="invocation">The candidate <c>To</c> invocation.</param>
        /// <param name="canonicalMethod">The original method symbol for the candidate invocation.</param>
        /// <param name="invokedMethod">The concrete method symbol that carries the implementation type argument.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <param name="serviceType">The service type resolved from <c>Bind</c> when the chain is supported.</param>
        /// <returns><see langword="true"/> when the invocation is a supported Ninject registration chain; otherwise, <see langword="false"/>.</returns>
        private static bool IsNinjectToRegistration(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IMethodSymbol canonicalMethod, IMethodSymbol invokedMethod, CancellationToken cancellationToken, out ITypeSymbol serviceType)
        {
            // Ninject binding chains are emitted only when both Bind and To are bound to known generic methods.
            serviceType = null!;
            if (canonicalMethod.Name != "To" || invokedMethod.TypeArguments.Length != 1 || invocation.Expression is not MemberAccessExpressionSyntax memberAccess || memberAccess.Expression is not InvocationExpressionSyntax precedingInvocation)
            {
                return false;
            }

            SymbolInfo precedingSymbolInfo = semanticDocument.SemanticModel.GetSymbolInfo(precedingInvocation, cancellationToken);
            if (precedingSymbolInfo.Symbol is not IMethodSymbol precedingMethod || precedingMethod.Name != "Bind" || GetQualifiedName(precedingMethod.ContainingType) != "Ninject.StandardKernel" || precedingMethod.TypeArguments.Length != 1)
            {
                return false;
            }

            serviceType = precedingMethod.TypeArguments[0];
            return true;
        }

        /// <summary>
        /// Creates a descriptor for standard AddSingleton, AddScoped, and AddTransient overloads.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for typeof argument resolution.</param>
        /// <param name="invocation">The invocation being classified.</param>
        /// <param name="canonicalMethod">The original Microsoft registration method definition.</param>
        /// <param name="invokedMethod">The concrete invocation method symbol.</param>
        /// <param name="cancellationToken">A token that signals when type resolution should stop.</param>
        /// <returns>A registration descriptor for supported standard registrations; otherwise, <see langword="null"/>.</returns>
        private static RegistrationDescriptor? TryCreateStandardRegistrationDescriptor(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IMethodSymbol canonicalMethod, IMethodSymbol invokedMethod, CancellationToken cancellationToken)
        {
            // Standard registrations are grouped by lifetime method name, then refined by overload shape.
            string? lifetime = TryGetLifetimeFromRegistrationMethod(canonicalMethod.Name);
            if (lifetime is null)
            {
                return null;
            }

            if (invokedMethod.TypeArguments.Length == 2)
            {
                ITypeSymbol serviceType = invokedMethod.TypeArguments[0];
                ITypeSymbol implementationType = invokedMethod.TypeArguments[1];
                return RegistrationDescriptor.CreateKnown(serviceType, implementationType, lifetime, canonicalMethod.Name, MicrosoftRegistrationExtensionType, "Direct", IsHostedImplementation(implementationType), IsBackgroundServiceImplementation(implementationType));
            }

            if (invokedMethod.TypeArguments.Length == 1 && invocation.ArgumentList.Arguments.Count == 0)
            {
                ITypeSymbol serviceType = invokedMethod.TypeArguments[0];
                return RegistrationDescriptor.CreateKnown(serviceType, serviceType, lifetime, canonicalMethod.Name, MicrosoftRegistrationExtensionType, "Direct");
            }

            if (invocation.ArgumentList.Arguments.Count >= 2
                && TryGetTypeofArgument(semanticDocument, invocation.ArgumentList.Arguments[^2], cancellationToken, out ITypeSymbol? typeofService)
                && TryGetTypeofArgument(semanticDocument, invocation.ArgumentList.Arguments[^1], cancellationToken, out ITypeSymbol? typeofImplementation))
            {
                return RegistrationDescriptor.CreateKnown(typeofService, typeofImplementation, lifetime, canonicalMethod.Name, MicrosoftRegistrationExtensionType, "DirectTypeof", IsHostedImplementation(typeofImplementation), IsBackgroundServiceImplementation(typeofImplementation));
            }

            if (invokedMethod.TypeArguments.Length == 1 && invocation.ArgumentList.Arguments.Count > 0)
            {
                ITypeSymbol serviceType = invokedMethod.TypeArguments[0];
                return RegistrationDescriptor.CreateUnknownImplementation(serviceType, lifetime, canonicalMethod.Name, MicrosoftRegistrationExtensionType, "Factory", "Factory registration does not expose a deterministic implementation type in this slice.");
            }

            return null;
        }

        /// <summary>
        /// Creates a descriptor for TryAdd, TryAddEnumerable, and Replace descriptor-based registrations.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for descriptor invocation resolution.</param>
        /// <param name="invocation">The descriptor-extension invocation being classified.</param>
        /// <param name="canonicalMethod">The original descriptor-extension method definition.</param>
        /// <param name="invokedMethod">The concrete invocation method symbol.</param>
        /// <param name="cancellationToken">A token that signals when descriptor argument resolution should stop.</param>
        /// <returns>A registration descriptor for supported descriptor registrations; otherwise, <see langword="null"/>.</returns>
        private static RegistrationDescriptor? TryCreateDescriptorRegistrationDescriptor(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IMethodSymbol canonicalMethod, IMethodSymbol invokedMethod, CancellationToken cancellationToken)
        {
            // Descriptor APIs wrap a ServiceDescriptor factory call, so the registration facts are taken from the first descriptor argument.
            string family = canonicalMethod.Name switch
            {
                "TryAdd" => "TryAdd",
                "TryAddEnumerable" => "TryAddEnumerable",
                "Replace" => "Replace",
                _ => string.Empty
            };

            if (family.Length == 0 || invocation.ArgumentList.Arguments.Count == 0)
            {
                return null;
            }

            ExpressionSyntax descriptorExpression = invocation.ArgumentList.Arguments[^1].Expression;
            SymbolInfo descriptorSymbolInfo = semanticDocument.SemanticModel.GetSymbolInfo(descriptorExpression, cancellationToken);
            if (descriptorSymbolInfo.Symbol is not IMethodSymbol descriptorMethod || descriptorMethod.TypeArguments.Length != 2)
            {
                return null;
            }

            string? lifetime = TryGetLifetimeFromDescriptorMethod(descriptorMethod.Name);
            if (lifetime is null)
            {
                return null;
            }

            ITypeSymbol serviceType = descriptorMethod.TypeArguments[0];
            ITypeSymbol implementationType = descriptorMethod.TypeArguments[1];
            return RegistrationDescriptor.CreateKnown(serviceType, implementationType, lifetime, canonicalMethod.Name, GetQualifiedName(invokedMethod.ContainingType), family, IsHostedImplementation(implementationType), IsBackgroundServiceImplementation(implementationType));
        }

        /// <summary>
        /// Creates a descriptor for AddHostedService registrations.
        /// </summary>
        /// <param name="canonicalMethod">The original hosted-service extension method definition.</param>
        /// <param name="invokedMethod">The concrete invocation method symbol.</param>
        /// <returns>A hosted-service registration descriptor when supported; otherwise, <see langword="null"/>.</returns>
        private static RegistrationDescriptor? TryCreateHostedServiceRegistrationDescriptor(IMethodSymbol canonicalMethod, IMethodSymbol invokedMethod)
        {
            // AddHostedService registers THostedService as IHostedService and always behaves as singleton composition in Microsoft DI.
            if (canonicalMethod.Name != "AddHostedService" || invokedMethod.TypeArguments.Length != 1)
            {
                return null;
            }

            ITypeSymbol hostedType = invokedMethod.TypeArguments[0];
            return RegistrationDescriptor.CreateKnown(hostedType, HostedServiceTypeName, "IHostedService", "Singleton", canonicalMethod.Name, MicrosoftHostedServiceExtensionType, "HostedService", true, IsBackgroundServiceImplementation(hostedType));
        }

        /// <summary>
        /// Creates a descriptor for default, named, typed, and typed-implementation HttpClient registrations.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used to inspect HttpClient arguments.</param>
        /// <param name="invocation">The HttpClient registration invocation being classified.</param>
        /// <param name="canonicalMethod">The original HttpClient registration method definition.</param>
        /// <param name="invokedMethod">The concrete invocation method symbol.</param>
        /// <param name="cancellationToken">A token that signals when argument inspection should stop.</param>
        /// <returns>A HttpClient registration descriptor when supported; otherwise, <see langword="null"/>.</returns>
        private static RegistrationDescriptor? TryCreateHttpClientRegistrationDescriptor(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IMethodSymbol canonicalMethod, IMethodSymbol invokedMethod, CancellationToken cancellationToken)
        {
            // HttpClientFactory registrations are DI facts with an external-target uncertainty unless a later configuration slice resolves the target.
            if (canonicalMethod.Name != "AddHttpClient")
            {
                return null;
            }

            string? clientName = TryGetLiteralStringArgument(invocation);
            string? delegatePreview = TryGetDelegatePreview(invocation, cancellationToken);

            if (invokedMethod.TypeArguments.Length == 2)
            {
                return RegistrationDescriptor.CreateHttpClient(invokedMethod.TypeArguments[0], invokedMethod.TypeArguments[1], "TypedImplementation", clientName, delegatePreview, "Typed HttpClient target cannot be resolved from registration alone.");
            }

            if (invokedMethod.TypeArguments.Length == 1)
            {
                ITypeSymbol typedClient = invokedMethod.TypeArguments[0];
                return RegistrationDescriptor.CreateHttpClient(typedClient, typedClient, "Typed", clientName, delegatePreview, "Typed HttpClient target cannot be resolved from registration alone.");
            }

            if (clientName is not null)
            {
                return RegistrationDescriptor.CreateHttpClientNamed(clientName, delegatePreview, "Named HttpClient target cannot be resolved from registration alone.");
            }

            return RegistrationDescriptor.CreateHttpClientDefault();
        }

        /// <summary>
        /// Maps standard registration method names to normalized lifetime metadata.
        /// </summary>
        /// <param name="methodName">The registration method name to classify.</param>
        /// <returns>The normalized lifetime value when supported; otherwise, <see langword="null"/>.</returns>
        private static string? TryGetLifetimeFromRegistrationMethod(string methodName)
        {
            // Microsoft DI uses lifetime in the method name for the standard Add* registration family.
            return methodName switch
            {
                "AddSingleton" => "Singleton",
                "AddScoped" => "Scoped",
                "AddTransient" => "Transient",
                _ => null
            };
        }

        /// <summary>
        /// Maps ServiceDescriptor factory method names to normalized lifetime metadata.
        /// </summary>
        /// <param name="methodName">The descriptor factory method name to classify.</param>
        /// <returns>The normalized lifetime value when supported; otherwise, <see langword="null"/>.</returns>
        private static string? TryGetLifetimeFromDescriptorMethod(string methodName)
        {
            // Descriptor-based APIs carry lifetime in the ServiceDescriptor factory method rather than the outer extension method.
            return methodName switch
            {
                "Singleton" => "Singleton",
                "Scoped" => "Scoped",
                "Transient" => "Transient",
                _ => null
            };
        }

        /// <summary>
        /// Attempts to resolve a typeof argument expression to a Roslyn type symbol.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that owns the argument expression.</param>
        /// <param name="argument">The argument syntax expected to contain a typeof expression.</param>
        /// <param name="cancellationToken">A token that signals when type binding should stop.</param>
        /// <param name="typeSymbol">The resolved type symbol when the argument contains a supported typeof expression.</param>
        /// <returns><see langword="true"/> when a type symbol was resolved; otherwise, <see langword="false"/>.</returns>
        private static bool TryGetTypeofArgument(SemanticExtractionRequest semanticDocument, ArgumentSyntax argument, CancellationToken cancellationToken, out ITypeSymbol typeSymbol)
        {
            // typeof overloads are deterministic only when Roslyn can bind the type syntax to a concrete type symbol.
            typeSymbol = null!;
            if (argument.Expression is not TypeOfExpressionSyntax typeOfExpression)
            {
                return false;
            }

            ITypeSymbol? resolvedType = semanticDocument.SemanticModel.GetTypeInfo(typeOfExpression.Type, cancellationToken).Type;
            if (resolvedType is null)
            {
                return false;
            }

            typeSymbol = resolvedType;
            return true;
        }

        /// <summary>
        /// Attempts to read the first string-literal argument from an invocation.
        /// </summary>
        /// <param name="invocation">The invocation whose arguments should be inspected.</param>
        /// <returns>The literal string value when present; otherwise, <see langword="null"/>.</returns>
        private static string? TryGetLiteralStringArgument(InvocationExpressionSyntax invocation)
        {
            // Named HttpClient registrations use a string client name; dynamic names are intentionally left unknown for later slices.
            foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
            {
                if (argument.Expression is LiteralExpressionSyntax literal && literal.Token.Value is string value && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// Attempts to create a compact preview of a configuration delegate argument.
        /// </summary>
        /// <param name="invocation">The invocation whose arguments should be inspected.</param>
        /// <param name="cancellationToken">A token that signals when source text access should stop.</param>
        /// <returns>A normalized preview of the first lambda or anonymous delegate argument; otherwise, <see langword="null"/>.</returns>
        private static string? TryGetDelegatePreview(InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
        {
            // The preview preserves configuration-delegate evidence without trying to infer a complete external target from arbitrary code.
            foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
            {
                if (argument.Expression is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
                {
                    (string? preview, _) = SemanticSnippetBuilder.CreateSnippet(invocation.SyntaxTree, argument.Expression.Span, cancellationToken);
                    return preview;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines whether a method can act as a service-registration wrapper for this work item.
        /// </summary>
        /// <param name="methodSymbol">The method symbol that may accept or extend IServiceCollection.</param>
        /// <returns><see langword="true"/> when the method accepts IServiceCollection and is not a known Microsoft registration API; otherwise, <see langword="false"/>.</returns>
        private static bool IsServiceCollectionWrapperMethod(IMethodSymbol methodSymbol)
        {
            // Wrapper traversal is intentionally conservative: it follows user or module methods that accept IServiceCollection, not the Microsoft APIs already handled as registrations.
            string ownerType = GetQualifiedName(methodSymbol.ContainingType);
            if (ownerType is MicrosoftRegistrationExtensionType or MicrosoftDescriptorExtensionType or MicrosoftHostedServiceExtensionType or MicrosoftHttpClientExtensionType)
            {
                return false;
            }

            return methodSymbol.Parameters.Any(parameter => GetQualifiedName(parameter.Type) == "Microsoft.Extensions.DependencyInjection.IServiceCollection");
        }

        /// <summary>
        /// Creates wrapper invocation evidence descriptors for the current wrapper traversal chain.
        /// </summary>
        /// <param name="request">The snapshot and semantic document context that scopes evidence identity.</param>
        /// <param name="descriptor">The registration descriptor containing wrapper invocation context.</param>
        /// <param name="cancellationToken">A token that signals when source text access should stop.</param>
        /// <returns>Evidence records representing wrapper call sites that led to the inner registration.</returns>
        private static IEnumerable<EvidenceRecord> CreateWrapperEvidenceRecords(DependencyInjectionExtractionRequest request, RegistrationDescriptor descriptor, CancellationToken cancellationToken)
        {
            // Wrapper evidence is emitted separately from registration evidence so explanations can show both the startup call and inner registration call.
            foreach (WrapperInvocation wrapperInvocation in descriptor.WrapperInvocations)
            {
                SemanticEvidence evidence = CreateEvidence(request.SemanticDocument, wrapperInvocation.Invocation, wrapperInvocation.MethodSymbol, cancellationToken);
                yield return CreateEvidenceRecord(request.SnapshotStableKey, evidence, descriptor, "WrapperInvocation");
            }
        }

        /// <summary>
        /// Creates an immutable wrapper invocation descriptor from source and symbol context.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that owns the invocation syntax and semantic model.</param>
        /// <param name="invocation">The wrapper invocation syntax node.</param>
        /// <param name="methodSymbol">The resolved wrapper method symbol.</param>
        /// <param name="cancellationToken">A token that signals when source text access should stop.</param>
        /// <returns>A wrapper invocation descriptor for chain metadata and evidence creation.</returns>
        private static WrapperInvocation CreateWrapperInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IMethodSymbol methodSymbol, CancellationToken cancellationToken)
        {
            // Wrapper names use fully qualified method symbols, with explicit interface-extension reduction hidden for readability.
            _ = semanticDocument.SyntaxTree.GetLineSpan(invocation.Span, cancellationToken);
            return new WrapperInvocation(GetQualifiedName(methodSymbol), invocation, methodSymbol);
        }

        /// <summary>
        /// Formats a compact source location for extractor diagnostics.
        /// </summary>
        /// <param name="invocation">The invocation whose source location should be reported.</param>
        /// <returns>A path and line location suitable for warning diagnostics.</returns>
        private static string FormatInvocationLocation(InvocationExpressionSyntax invocation)
        {
            // Diagnostics use syntax-tree paths because they may be emitted before semantic evidence exists.
            FileLinePositionSpan lineSpan = invocation.SyntaxTree.GetLineSpan(invocation.Span);
            return $"{invocation.SyntaxTree.FilePath}:{lineSpan.StartLinePosition.Line + 1}";
        }

        /// <summary>
        /// Determines whether a type is assignable to IHostedService.
        /// </summary>
        /// <param name="typeSymbol">The type symbol to inspect.</param>
        /// <returns><see langword="true"/> when the type implements `IHostedService`; otherwise, <see langword="false"/>.</returns>
        private static bool IsHostedImplementation(ITypeSymbol typeSymbol)
        {
            // Interface traversal uses fully qualified display names so tests and real projects share the same classification rule.
            return typeSymbol.AllInterfaces.Any(interfaceType => GetQualifiedName(interfaceType) == HostedServiceTypeName);
        }

        /// <summary>
        /// Determines whether a type derives from Microsoft.Extensions.Hosting.BackgroundService.
        /// </summary>
        /// <param name="typeSymbol">The type symbol to inspect.</param>
        /// <returns><see langword="true"/> when the type derives from BackgroundService; otherwise, <see langword="false"/>.</returns>
        private static bool IsBackgroundServiceImplementation(ITypeSymbol typeSymbol)
        {
            // BackgroundService is detected through base-type traversal because implementations may not mention IHostedService directly.
            for (ITypeSymbol? current = typeSymbol.BaseType; current is not null; current = current.BaseType)
            {
                if (GetQualifiedName(current) == "Microsoft.Extensions.Hosting.BackgroundService")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates source evidence for a registration invocation using repository-relative path, line span, symbol context, and snippet details.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that owns the invocation syntax and semantic model.</param>
        /// <param name="invocation">The invocation syntax node that provides the source span.</param>
        /// <param name="methodSymbol">The resolved registration method symbol associated with the invocation.</param>
        /// <param name="cancellationToken">A token that signals when source text access should stop.</param>
        /// <returns>A source evidence model suitable for conversion to domain evidence.</returns>
        private static SemanticEvidence CreateEvidence(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, IMethodSymbol methodSymbol, CancellationToken cancellationToken)
        {
            // Evidence uses the invocation span rather than the whole containing method so snippets point directly at the registration statement.
            FileLinePositionSpan lineSpan = invocation.SyntaxTree.GetLineSpan(invocation.Span, cancellationToken);
            (string? preview, string? hash) = SemanticSnippetBuilder.CreateSnippet(invocation.SyntaxTree, invocation.Span, cancellationToken);
            string repositoryRelativePath = SemanticPathNormalizer.ToRepositoryRelativePath(semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath);
            string? containingSymbol = semanticDocument.SemanticModel.GetEnclosingSymbol(invocation.SpanStart, cancellationToken)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);

            return new SemanticEvidence(repositoryRelativePath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, lineSpan.StartLinePosition.Character + 1, lineSpan.EndLinePosition.Character + 1, methodSymbol.Name, containingSymbol, preview, hash);
        }

        /// <summary>
        /// Converts semantic source evidence into the domain evidence record used by graph snapshots.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the evidence.</param>
        /// <param name="evidence">The semantic evidence captured for the registration invocation.</param>
        /// <param name="descriptor">The registration descriptor used to enrich evidence metadata and identity.</param>
        /// <param name="evidenceRole">The evidence role that distinguishes registration evidence from wrapper call-site evidence.</param>
        /// <returns>A domain evidence record for the registration or wrapper invocation.</returns>
        private static EvidenceRecord CreateEvidenceRecord(StableKey snapshotStableKey, SemanticEvidence evidence, RegistrationDescriptor descriptor, string evidenceRole)
        {
            // Evidence keys include the registration endpoint pair and source span so repeated identical extraction remains deterministic.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["evidenceRole"] = evidenceRole,
                ["extractor"] = nameof(DirectMicrosoftDependencyInjectionExtractor),
                ["registrationFamily"] = descriptor.RegistrationFamily,
                ["registrationMethod"] = descriptor.RegistrationMethod,
                ["serviceType"] = descriptor.ServiceTypeName,
                ["implementationType"] = descriptor.ImplementationTypeName
            });
            StableKey stableKey = new($"di-evidence://{HashStablePayload(evidence.RepositoryRelativeFilePath, evidence.StartLine.ToString(), evidence.StartColumn.ToString(), descriptor.ServiceTypeName, descriptor.ImplementationTypeName, descriptor.Lifetime, descriptor.RegistrationFamily, evidenceRole)}");

            return new EvidenceRecord(snapshotStableKey, stableKey, EvidenceKind.SourceCode, RepositoryRelativePath.Parse(evidence.RepositoryRelativeFilePath), evidence.StartLine, evidence.EndLine, evidence.SymbolName, evidence.ContainingSymbolName, evidence.SnippetHash, evidence.SnippetPreview, KnowledgeKind.Fact, descriptor.Confidence, descriptor.UnknownState, metadata, FingerprintGenerator.ForEvidence(EvidenceKind.SourceCode, evidence.RepositoryRelativeFilePath, evidence.StartLine, evidence.EndLine, evidence.SymbolName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a graph node for a service or implementation type referenced by a registration.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the node.</param>
        /// <param name="qualifiedTypeName">The fully qualified type name resolved or synthesized by the extractor.</param>
        /// <param name="displayName">The developer-facing type name.</param>
        /// <param name="projectStableKey">The stable key of the project or assembly context associated with the type.</param>
        /// <param name="primaryEvidenceStableKey">The stable key of the registration evidence explaining why the node is emitted by this slice.</param>
        /// <param name="nodeKind">The graph node kind for the extracted architecture concept.</param>
        /// <param name="metadataSource">The metadata source label explaining why this node was emitted.</param>
        /// <returns>A graph node for the registered service, implementation, hosted-service, or HttpClient concept.</returns>
        private static ArchitectureNode CreateTypeNode(StableKey snapshotStableKey, string qualifiedTypeName, string displayName, StableKey projectStableKey, StableKey primaryEvidenceStableKey, NodeKind nodeKind, string metadataSource)
        {
            // Nodes reuse stable graph vocabulary where possible and synthesize readable names only for DI concepts without a source type.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["extractor"] = nameof(DirectMicrosoftDependencyInjectionExtractor),
                ["nodeSource"] = metadataSource
            });
            StableKey stableKey = StableKeyGenerator.ForType(qualifiedTypeName);

            return new ArchitectureNode(snapshotStableKey, stableKey, nodeKind, displayName, qualifiedTypeName, qualifiedTypeName, "C#", projectStableKey, null, KnowledgeKind.Fact, null, null, Confidence.Certain, UnknownState.Known, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForNode(nodeKind, displayName, qualifiedTypeName, qualifiedTypeName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates the implementation-to-service registration relationship required by the dependency-injection graph contract.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the edge.</param>
        /// <param name="descriptor">The registration descriptor containing lifetime and classification metadata.</param>
        /// <param name="implementationStableKey">The stable key of the implementation node that is the relationship source.</param>
        /// <param name="serviceStableKey">The stable key of the service abstraction node that is the relationship target.</param>
        /// <param name="primaryEvidenceStableKey">The stable key of the source evidence explaining the edge.</param>
        /// <returns>A direct `REGISTERED_AS_SERVICE` graph edge.</returns>
        private static ArchitectureEdge CreateRegistrationEdge(StableKey snapshotStableKey, RegistrationDescriptor descriptor, StableKey implementationStableKey, StableKey serviceStableKey, StableKey primaryEvidenceStableKey)
        {
            // Edge metadata carries DI-specific classification while normalized fields keep graph traversal consistent.
            GraphMetadata metadata = GraphMetadata.From(descriptor.ToMetadata());
            StableKey stableKey = new($"di-registration://{HashStablePayload(descriptor.Lifetime, descriptor.ServiceTypeName, descriptor.ImplementationTypeName, descriptor.RegistrationMethod, descriptor.RegistrationFamily, descriptor.ClientName ?? "none")}");

            return new ArchitectureEdge(snapshotStableKey, stableKey, EdgeKind.RegisteredAsService, implementationStableKey, serviceStableKey, true, KnowledgeKind.Fact, descriptor.Confidence, descriptor.UnknownState, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForEdge(EdgeKind.RegisteredAsService, implementationStableKey, serviceStableKey, true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Emits constructor dependency correlation facts for a known registered implementation type.
        /// </summary>
        /// <param name="request">The snapshot and semantic document context that scopes graph facts.</param>
        /// <param name="accumulator">The shared snapshot accumulator receiving dependency-correlation facts.</param>
        /// <param name="descriptor">The registration descriptor whose implementation type should be inspected.</param>
        /// <param name="implementationStableKey">The stable key of the registered implementation node.</param>
        /// <param name="primaryEvidenceStableKey">The stable key of the registration evidence that explains the correlation.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AccumulateConstructorCorrelation(DependencyInjectionExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, RegistrationDescriptor descriptor, StableKey implementationStableKey, StableKey primaryEvidenceStableKey, CancellationToken cancellationToken)
        {
            // Unknown implementation descriptors cannot be matched to constructors and are intentionally skipped.
            if (descriptor.ImplementationTypeSymbol is not INamedTypeSymbol implementationType || descriptor.UnknownState.HasUnknownData)
            {
                return;
            }

            foreach (IParameterSymbol parameter in GetConstructorDependencyParameters(implementationType))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string dependencyTypeName = GetQualifiedName(parameter.Type);
                StableKey dependencyStableKey = StableKeyGenerator.ForType(dependencyTypeName);
                ArchitectureNode dependencyNode = CreateTypeNode(request.SnapshotStableKey, dependencyTypeName, parameter.Type.Name, descriptor.ProjectStableKey, primaryEvidenceStableKey, NodeKind.Type, "MicrosoftDI.ConstructorDependency");

                accumulator
                    .AddNode(dependencyNode)
                    .AddEdge(CreateConstructorDependencyEdge(request.SnapshotStableKey, EdgeKind.Injects, implementationStableKey, dependencyStableKey, primaryEvidenceStableKey, implementationType, parameter))
                    .AddEdge(CreateConstructorDependencyEdge(request.SnapshotStableKey, EdgeKind.DependsOn, implementationStableKey, dependencyStableKey, primaryEvidenceStableKey, implementationType, parameter));
            }
        }

        /// <summary>
        /// Selects constructor parameters that represent dependency-injection dependencies for a registered implementation type.
        /// </summary>
        /// <param name="implementationType">The registered implementation type whose constructors should be inspected.</param>
        /// <returns>The parameters from the selected public or longest constructor.</returns>
        private static IEnumerable<IParameterSymbol> GetConstructorDependencyParameters(INamedTypeSymbol implementationType)
        {
            // Microsoft DI selects public constructors; this correlation slice uses the longest public constructor as the most dependency-rich candidate.
            IMethodSymbol? constructor = implementationType.Constructors
                .Where(candidate => candidate.DeclaredAccessibility == Accessibility.Public && !candidate.IsStatic)
                .OrderByDescending(candidate => candidate.Parameters.Length)
                .FirstOrDefault();

            return constructor?.Parameters ?? [];
        }

        /// <summary>
        /// Creates one constructor-correlation edge for either INJECTS or DEPENDS_ON graph semantics.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the edge.</param>
        /// <param name="edgeKind">The dependency edge kind to create.</param>
        /// <param name="implementationStableKey">The stable key of the registered implementation node.</param>
        /// <param name="dependencyStableKey">The stable key of the constructor dependency node.</param>
        /// <param name="primaryEvidenceStableKey">The stable key of the registration evidence explaining why correlation ran.</param>
        /// <param name="implementationType">The registered implementation type whose constructor declares the dependency.</param>
        /// <param name="parameter">The constructor parameter that references the dependency type.</param>
        /// <returns>A deterministic constructor-correlation edge.</returns>
        private static ArchitectureEdge CreateConstructorDependencyEdge(StableKey snapshotStableKey, EdgeKind edgeKind, StableKey implementationStableKey, StableKey dependencyStableKey, StableKey primaryEvidenceStableKey, INamedTypeSymbol implementationType, IParameterSymbol parameter)
        {
            // Stable keys deliberately omit registration method details so equivalent semantic and DI correlation facts collapse on the same implementation/dependency pair.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["constructorParameter"] = parameter.Name,
                ["dependencyType"] = GetQualifiedName(parameter.Type),
                ["extractor"] = nameof(DirectMicrosoftDependencyInjectionExtractor),
                ["implementationType"] = GetQualifiedName(implementationType),
                ["relationshipSource"] = "MicrosoftDI.ConstructorCorrelation"
            });
            StableKey stableKey = new($"di-constructor-{edgeKind.Value.ToLowerInvariant()}://{HashStablePayload(GetQualifiedName(implementationType), GetQualifiedName(parameter.Type), parameter.Name)}");

            return new ArchitectureEdge(snapshotStableKey, stableKey, edgeKind, implementationStableKey, dependencyStableKey, true, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForEdge(edgeKind, implementationStableKey, dependencyStableKey, true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Converts a Roslyn symbol to a fully qualified display name without the Roslyn `global::` prefix.
        /// </summary>
        /// <param name="symbol">The symbol to display.</param>
        /// <returns>A stable fully qualified display name, or `Unknown` when no symbol is available.</returns>
        private static string GetQualifiedName(ISymbol? symbol)
        {
            // Fully qualified names are the bridge from Roslyn symbols to graph stable keys.
            return symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal) ?? "Unknown";
        }

        /// <summary>
        /// Hashes deterministic payload segments for extractor-local stable keys.
        /// </summary>
        /// <param name="segments">The payload segments that identify the extractor fact.</param>
        /// <returns>A lowercase SHA-256 hash of the length-prefixed payload segments.</returns>
        private static string HashStablePayload(params string[] segments)
        {
            // Length-prefixing mirrors the semantic key approach and prevents delimiter collisions between type names and metadata values.
            string payload = string.Join("|", segments.Select(segment => $"{segment.Length}:{segment}"));
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(payload);
            byte[] hash = System.Security.Cryptography.SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Holds the compiler-resolved service registration details needed to create graph facts.
        /// </summary>
        private sealed class RegistrationDescriptor
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="RegistrationDescriptor"/> class.
            /// </summary>
            /// <param name="serviceTypeName">The fully qualified service abstraction type name.</param>
            /// <param name="serviceDisplayName">The developer-facing service abstraction type name.</param>
            /// <param name="implementationTypeName">The fully qualified implementation type name or unknown implementation placeholder.</param>
            /// <param name="implementationDisplayName">The developer-facing implementation type name or unknown implementation placeholder.</param>
            /// <param name="lifetime">The normalized registration lifetime value.</param>
            /// <param name="registrationMethod">The registration method name that created the registration.</param>
            /// <param name="registrationSource">The fully qualified type that owns the registration method.</param>
            /// <param name="registrationFamily">The registration family classification used by contributor guidance and later query work.</param>
            /// <param name="implementationTypeSymbol">The compiler-resolved implementation type used for constructor correlation when available.</param>
            public RegistrationDescriptor(string serviceTypeName, string serviceDisplayName, string implementationTypeName, string implementationDisplayName, string lifetime, string registrationMethod, string registrationSource, string registrationFamily, ITypeSymbol? implementationTypeSymbol = null)
            {
                // The descriptor centralizes validation-light normalized values before graph contracts are created.
                ServiceTypeName = serviceTypeName;
                ServiceDisplayName = serviceDisplayName;
                ImplementationTypeName = implementationTypeName;
                ImplementationDisplayName = implementationDisplayName;
                Lifetime = lifetime;
                RegistrationMethod = registrationMethod;
                RegistrationSource = registrationSource;
                RegistrationFamily = registrationFamily;
                ImplementationTypeSymbol = implementationTypeSymbol;
                ProjectStableKey = StableKeyGenerator.ForProject("Sample.App");
            }

            /// <summary>Gets the fully qualified service abstraction type name.</summary>
            public string ServiceTypeName { get; }

            /// <summary>Gets the developer-facing service abstraction type name.</summary>
            public string ServiceDisplayName { get; }

            /// <summary>Gets the fully qualified implementation type name or unknown implementation placeholder.</summary>
            public string ImplementationTypeName { get; }

            /// <summary>Gets the developer-facing implementation type name or unknown implementation placeholder.</summary>
            public string ImplementationDisplayName { get; }

            /// <summary>Gets the normalized registration lifetime value.</summary>
            public string Lifetime { get; }

            /// <summary>Gets the registration method name that created the registration.</summary>
            public string RegistrationMethod { get; }

            /// <summary>Gets the fully qualified type that owns the registration method.</summary>
            public string RegistrationSource { get; }

            /// <summary>Gets the registration family classification used by contributor guidance and later query work.</summary>
            public string RegistrationFamily { get; }

            /// <summary>Gets the compiler-resolved implementation type used for constructor correlation when available.</summary>
            public ITypeSymbol? ImplementationTypeSymbol { get; }

            /// <summary>Gets the stable project context currently associated with extractor-emitted type nodes.</summary>
            public StableKey ProjectStableKey { get; }

            /// <summary>Gets the graph node kind used for the service-side node.</summary>
            public NodeKind ServiceNodeKind { get; init; } = NodeKind.Type;

            /// <summary>Gets the graph node kind used for the implementation-side node.</summary>
            public NodeKind ImplementationNodeKind { get; init; } = NodeKind.Type;

            /// <summary>Gets the node metadata source used for the service-side node.</summary>
            public string ServiceNodeMetadataSource { get; init; } = "MicrosoftDI.ServiceRegistration";

            /// <summary>Gets the node metadata source used for the implementation-side node.</summary>
            public string ImplementationNodeMetadataSource { get; init; } = "MicrosoftDI.ServiceRegistration";

            /// <summary>Gets a value indicating whether the registration represents hosted-service composition.</summary>
            public bool HostedService { get; init; }

            /// <summary>Gets a value indicating whether the implementation derives from BackgroundService.</summary>
            public bool BackgroundService { get; init; }

            /// <summary>Gets a value indicating whether the registration represents HttpClientFactory composition.</summary>
            public bool HttpClient { get; init; }

            /// <summary>Gets the HttpClient client kind for HttpClientFactory registrations.</summary>
            public string? HttpClientKind { get; init; }

            /// <summary>Gets the named HttpClient name when the registration has one.</summary>
            public string? ClientName { get; init; }

            /// <summary>Gets the typed HttpClient service type when the registration has one.</summary>
            public string? TypedClientType { get; init; }

            /// <summary>Gets a compact preview of the HttpClient configuration delegate when present.</summary>
            public string? ConfigurationDelegatePreview { get; init; }

            /// <summary>Gets a value indicating whether the external target is unknown for this registration.</summary>
            public bool UnknownTarget { get; init; }

            /// <summary>Gets the normalized confidence assigned to the registration edge and evidence.</summary>
            public Confidence Confidence { get; init; } = Confidence.Certain;

            /// <summary>Gets the explicit unknown-state representation for registrations with unresolved implementation or external targets.</summary>
            public UnknownState UnknownState { get; init; } = UnknownState.Known;

            /// <summary>Gets the wrapper invocation chain that led to this registration.</summary>
            public IReadOnlyList<WrapperInvocation> WrapperInvocations { get; init; } = [];

            /// <summary>Gets a value indicating whether this registration was discovered inside a wrapper traversal.</summary>
            public bool FromWrapper { get; init; }

            /// <summary>Gets the formatted invocation chain from startup wrapper call to innermost wrapper.</summary>
            public string? InvocationChain { get; init; }

            /// <summary>Gets the number of wrapper hops followed before discovering this registration.</summary>
            public int WrapperDepth { get; init; }

            /// <summary>Gets the dependency-injection container family that produced this descriptor when it is not Microsoft DI.</summary>
            public string? ContainerKind { get; init; }

            /// <summary>Gets the detection strategy used to classify registrations that are not direct Microsoft DI calls.</summary>
            public string? DetectionMode { get; init; }

            /// <summary>Gets a value indicating whether this descriptor came from conservative service-locator or factory heuristics.</summary>
            public bool HeuristicDetection { get; init; }

            /// <summary>Gets a value indicating whether the descriptor represents known container usage with unknown registration endpoints.</summary>
            public bool UnknownRegistration { get; init; }

            /// <summary>
            /// Creates a descriptor for a compiler-resolved service and implementation pair.
            /// </summary>
            /// <param name="serviceType">The compiler-resolved service type.</param>
            /// <param name="implementationType">The compiler-resolved implementation type.</param>
            /// <param name="lifetime">The normalized lifetime value.</param>
            /// <param name="registrationMethod">The method that produced the registration.</param>
            /// <param name="registrationSource">The type that owns the registration method.</param>
            /// <param name="registrationFamily">The registration family classification.</param>
            /// <param name="hostedService">A value indicating whether the implementation is hosted-service assignable.</param>
            /// <param name="backgroundService">A value indicating whether the implementation derives from BackgroundService.</param>
            /// <returns>A registration descriptor for a known implementation mapping.</returns>
            public static RegistrationDescriptor CreateKnown(ITypeSymbol serviceType, ITypeSymbol implementationType, string lifetime, string registrationMethod, string registrationSource, string registrationFamily, bool hostedService = false, bool backgroundService = false)
            {
                // Known mappings use Roslyn type names directly, producing certain implementation-to-service edges.
                return CreateKnown(implementationType, GetQualifiedName(serviceType), serviceType.Name, lifetime, registrationMethod, registrationSource, registrationFamily, hostedService, backgroundService);
            }

            /// <summary>
            /// Creates a descriptor for a compiler-resolved implementation and explicit service type name.
            /// </summary>
            /// <param name="implementationType">The compiler-resolved implementation type.</param>
            /// <param name="serviceTypeName">The fully qualified service type name.</param>
            /// <param name="serviceDisplayName">The service display name.</param>
            /// <param name="lifetime">The normalized lifetime value.</param>
            /// <param name="registrationMethod">The method that produced the registration.</param>
            /// <param name="registrationSource">The type that owns the registration method.</param>
            /// <param name="registrationFamily">The registration family classification.</param>
            /// <param name="hostedService">A value indicating whether the implementation is hosted-service assignable.</param>
            /// <param name="backgroundService">A value indicating whether the implementation derives from BackgroundService.</param>
            /// <returns>A registration descriptor for a known implementation mapping.</returns>
            public static RegistrationDescriptor CreateKnown(ITypeSymbol implementationType, string serviceTypeName, string serviceDisplayName, string lifetime, string registrationMethod, string registrationSource, string registrationFamily, bool hostedService = false, bool backgroundService = false)
            {
                // This overload supports AddHostedService where the service abstraction is the framework IHostedService type.
                return new RegistrationDescriptor(serviceTypeName, serviceDisplayName, GetQualifiedName(implementationType), implementationType.Name, lifetime, registrationMethod, registrationSource, registrationFamily, implementationType)
                {
                    HostedService = hostedService,
                    BackgroundService = backgroundService
                };
            }

            /// <summary>
            /// Creates a descriptor for a factory registration with an unresolved concrete implementation.
            /// </summary>
            /// <param name="serviceType">The compiler-resolved service type.</param>
            /// <param name="lifetime">The normalized lifetime value.</param>
            /// <param name="registrationMethod">The method that produced the registration.</param>
            /// <param name="registrationSource">The type that owns the registration method.</param>
            /// <param name="registrationFamily">The registration family classification.</param>
            /// <param name="unknownReason">The reason the concrete implementation is unknown.</param>
            /// <returns>A registration descriptor with explicit unknown implementation state.</returns>
            public static RegistrationDescriptor CreateUnknownImplementation(ITypeSymbol serviceType, string lifetime, string registrationMethod, string registrationSource, string registrationFamily, string unknownReason)
            {
                // Factory delegates can return many shapes, so this slice records the service fact without inventing an implementation type.
                return new RegistrationDescriptor(GetQualifiedName(serviceType), serviceType.Name, $"UnknownImplementation:{GetQualifiedName(serviceType)}", "Unknown", lifetime, registrationMethod, registrationSource, registrationFamily)
                {
                    Confidence = Confidence.Medium,
                    UnknownState = UnknownState.Unknown(unknownReason)
                };
            }

            /// <summary>
            /// Creates a descriptor for a compiler-resolved legacy container registration mapping.
            /// </summary>
            /// <param name="serviceType">The compiler-resolved service abstraction type.</param>
            /// <param name="implementationType">The compiler-resolved implementation type.</param>
            /// <param name="lifetime">The normalized lifetime value when the legacy container exposes one.</param>
            /// <param name="registrationMethod">The legacy container method that produced the registration.</param>
            /// <param name="registrationSource">The type that owns the legacy container registration method.</param>
            /// <param name="containerKind">The legacy container family name used in graph metadata.</param>
            /// <param name="detectionMode">The detection mode explaining how the legacy registration was resolved.</param>
            /// <returns>A registration descriptor for a known legacy service-to-implementation mapping.</returns>
            public static RegistrationDescriptor CreateLegacyKnown(ITypeSymbol serviceType, ITypeSymbol implementationType, string lifetime, string registrationMethod, string registrationSource, string containerKind, string detectionMode)
            {
                // Legacy mappings are high confidence when Roslyn resolves both generic endpoint types, but they remain separated from Microsoft DI by container metadata.
                return new RegistrationDescriptor(GetQualifiedName(serviceType), serviceType.Name, GetQualifiedName(implementationType), implementationType.Name, lifetime, registrationMethod, registrationSource, "LegacyContainer", implementationType)
                {
                    ContainerKind = containerKind,
                    DetectionMode = detectionMode,
                    Confidence = Confidence.High
                };
            }

            /// <summary>
            /// Creates a descriptor for a detected legacy container API whose concrete registrations are not deterministic.
            /// </summary>
            /// <param name="containerKind">The legacy container family name used in graph metadata.</param>
            /// <param name="registrationMethod">The legacy container method that proved unsupported container usage.</param>
            /// <param name="registrationSource">The type that owns the legacy container method.</param>
            /// <param name="unknownReason">The reason service and implementation endpoints cannot be resolved.</param>
            /// <returns>A registration descriptor with explicit unknown state for the unsupported legacy form.</returns>
            public static RegistrationDescriptor CreateLegacyUnknown(string containerKind, string registrationMethod, string registrationSource, string unknownReason)
            {
                // Unsupported forms become explicit graph facts so container usage is visible without overclaiming individual mappings.
                return new RegistrationDescriptor(UnknownLegacyServiceTypeName, "UnknownLegacyService", UnknownLegacyImplementationTypeName, "UnknownLegacyImplementation", "Unknown", registrationMethod, registrationSource, "LegacyContainer")
                {
                    ContainerKind = containerKind,
                    DetectionMode = "UnsupportedContainerApi",
                    Confidence = Confidence.Medium,
                    UnknownRegistration = true,
                    UnknownState = UnknownState.Unknown(unknownReason)
                };
            }

            /// <summary>
            /// Creates a descriptor for conservative service-locator or manual-factory detections.
            /// </summary>
            /// <param name="serviceTypeName">The service abstraction type name resolved from the heuristic pattern.</param>
            /// <param name="serviceDisplayName">The developer-facing service abstraction name.</param>
            /// <param name="implementationTypeName">The implementation type name or synthetic placeholder resolved by the heuristic.</param>
            /// <param name="implementationDisplayName">The developer-facing implementation name or synthetic placeholder.</param>
            /// <param name="lifetime">The normalized lifetime value, usually unknown for heuristic detections.</param>
            /// <param name="registrationMethod">The method that exposed the heuristic composition pattern.</param>
            /// <param name="registrationSource">The type that owns the heuristic composition method.</param>
            /// <param name="containerKind">The service-locator or manual-factory family name.</param>
            /// <param name="registrationFamily">The registration family classification.</param>
            /// <param name="unknownReason">The reason the detection remains lower-confidence or partially unknown.</param>
            /// <param name="implementationTypeSymbol">The implementation type symbol when the heuristic resolves a concrete type.</param>
            /// <returns>A registration descriptor for the conservative heuristic detection.</returns>
            public static RegistrationDescriptor CreateHeuristic(string serviceTypeName, string serviceDisplayName, string implementationTypeName, string implementationDisplayName, string lifetime, string registrationMethod, string registrationSource, string containerKind, string registrationFamily, string unknownReason, ITypeSymbol? implementationTypeSymbol = null)
            {
                // Heuristic facts are useful for risk discovery, but they intentionally use medium confidence and explicit metadata so consumers do not treat them as exact container registrations.
                return new RegistrationDescriptor(serviceTypeName, serviceDisplayName, implementationTypeName, implementationDisplayName, lifetime, registrationMethod, registrationSource, registrationFamily, implementationTypeSymbol)
                {
                    ContainerKind = containerKind,
                    DetectionMode = "Heuristic",
                    Confidence = Confidence.Medium,
                    HeuristicDetection = true,
                    UnknownState = UnknownState.Unknown(unknownReason)
                };
            }

            /// <summary>
            /// Creates a descriptor for default HttpClientFactory registration.
            /// </summary>
            /// <returns>A registration descriptor for the default IHttpClientFactory service.</returns>
            public static RegistrationDescriptor CreateHttpClientDefault()
            {
                // The default registration means the factory service itself is available from DI.
                return new RegistrationDescriptor(HttpClientFactoryTypeName, "IHttpClientFactory", HttpClientFactoryTypeName, "IHttpClientFactory", "Unknown", "AddHttpClient", MicrosoftHttpClientExtensionType, "HttpClient")
                {
                    HttpClient = true,
                    HttpClientKind = "Default"
                };
            }

            /// <summary>
            /// Creates a descriptor for a named HttpClient registration.
            /// </summary>
            /// <param name="clientName">The literal client name supplied to AddHttpClient.</param>
            /// <param name="delegatePreview">The optional configuration delegate preview.</param>
            /// <param name="unknownReason">The reason the external target remains unknown.</param>
            /// <returns>A registration descriptor for a named HttpClient registration.</returns>
            public static RegistrationDescriptor CreateHttpClientNamed(string clientName, string? delegatePreview, string unknownReason)
            {
                // Named clients use a synthetic service node so the client name remains queryable without a dedicated node kind.
                return new RegistrationDescriptor($"Sample.App.NamedHttpClient:{clientName}", clientName, HttpClientTypeName, "HttpClient", "Unknown", "AddHttpClient", MicrosoftHttpClientExtensionType, "HttpClient")
                {
                    HttpClient = true,
                    HttpClientKind = "Named",
                    ClientName = clientName,
                    ConfigurationDelegatePreview = delegatePreview,
                    UnknownTarget = true,
                    Confidence = Confidence.Medium,
                    UnknownState = UnknownState.Unknown(unknownReason)
                };
            }

            /// <summary>
            /// Creates a descriptor for a typed HttpClient registration.
            /// </summary>
            /// <param name="serviceType">The typed client service type.</param>
            /// <param name="implementationType">The typed client implementation type.</param>
            /// <param name="httpClientKind">The typed-client classification.</param>
            /// <param name="clientName">The optional named client value.</param>
            /// <param name="delegatePreview">The optional configuration delegate preview.</param>
            /// <param name="unknownReason">The reason the external target remains unknown.</param>
            /// <returns>A registration descriptor for a typed HttpClient registration.</returns>
            public static RegistrationDescriptor CreateHttpClient(ITypeSymbol serviceType, ITypeSymbol implementationType, string httpClientKind, string? clientName, string? delegatePreview, string unknownReason)
            {
                // Typed clients prove DI composition but not the remote endpoint; target uncertainty is explicit.
                return new RegistrationDescriptor(GetQualifiedName(serviceType), serviceType.Name, GetQualifiedName(implementationType), implementationType.Name, "Transient", "AddHttpClient", MicrosoftHttpClientExtensionType, "HttpClient", implementationType)
                {
                    HttpClient = true,
                    HttpClientKind = httpClientKind,
                    ClientName = clientName,
                    TypedClientType = GetQualifiedName(serviceType),
                    ConfigurationDelegatePreview = delegatePreview,
                    UnknownTarget = true,
                    Confidence = Confidence.Medium,
                    UnknownState = UnknownState.Unknown(unknownReason)
                };
            }

            /// <summary>
            /// Creates a descriptor copy annotated with wrapper traversal metadata.
            /// </summary>
            /// <param name="context">The wrapper traversal context that should annotate this descriptor.</param>
            /// <returns>The current descriptor when there is no wrapper context; otherwise, a wrapper-annotated copy.</returns>
            public RegistrationDescriptor WithWrapperContext(WrapperTraversalContext context)
            {
                // Wrapper metadata changes classification and evidence details without changing the resolved registration endpoints.
                if (context.Depth == 0)
                {
                    return this;
                }

                return new RegistrationDescriptor(ServiceTypeName, ServiceDisplayName, ImplementationTypeName, ImplementationDisplayName, Lifetime, RegistrationMethod, RegistrationSource, "Wrapper", ImplementationTypeSymbol)
                {
                    BackgroundService = BackgroundService,
                    ClientName = ClientName,
                    Confidence = Confidence,
                    ConfigurationDelegatePreview = ConfigurationDelegatePreview,
                    FromWrapper = true,
                    HostedService = HostedService,
                    HttpClient = HttpClient,
                    HttpClientKind = HttpClientKind,
                    ImplementationNodeKind = ImplementationNodeKind,
                    ImplementationNodeMetadataSource = ImplementationNodeMetadataSource,
                    InvocationChain = context.InvocationChain,
                    ServiceNodeKind = ServiceNodeKind,
                    ServiceNodeMetadataSource = ServiceNodeMetadataSource,
                    TypedClientType = TypedClientType,
                    UnknownState = UnknownState,
                    UnknownTarget = UnknownTarget,
                    WrapperDepth = context.Depth,
                    WrapperInvocations = context.Invocations
                };
            }

            /// <summary>
            /// Converts descriptor values into deterministic graph metadata.
            /// </summary>
            /// <returns>A dictionary of JSON-compatible metadata values for the registration edge.</returns>
            public IReadOnlyDictionary<string, object?> ToMetadata()
            {
                // Optional metadata is included only when meaningful so canonical JSON remains concise and deterministic.
                Dictionary<string, object?> metadata = new(StringComparer.Ordinal)
                {
                    ["containerKind"] = "Microsoft.Extensions.DependencyInjection",
                    ["extractor"] = nameof(DirectMicrosoftDependencyInjectionExtractor),
                    ["implementationType"] = ImplementationTypeName.StartsWith("UnknownImplementation:", StringComparison.Ordinal) ? "Unknown" : ImplementationTypeName,
                    ["lifetime"] = Lifetime,
                    ["registrationFamily"] = RegistrationFamily,
                    ["registrationMethod"] = RegistrationMethod,
                    ["registrationSource"] = RegistrationSource,
                    ["serviceType"] = ServiceTypeName
                };

                if (ContainerKind is not null)
                {
                    metadata["containerKind"] = ContainerKind;
                }

                if (DetectionMode is not null)
                {
                    metadata["detectionMode"] = DetectionMode;
                }

                if (HeuristicDetection)
                {
                    metadata["heuristicDetection"] = true;
                }

                if (UnknownRegistration)
                {
                    metadata["unknownRegistration"] = true;
                }

                if (FromWrapper)
                {
                    metadata["invocationChain"] = InvocationChain;
                    metadata["wrapperDepth"] = WrapperDepth;
                    metadata["wrapperRegistration"] = true;
                }

                if (HostedService)
                {
                    metadata["hostedService"] = true;
                }

                if (BackgroundService)
                {
                    metadata["backgroundService"] = true;
                }

                if (HttpClient)
                {
                    metadata["httpClient"] = true;
                    metadata["httpClientKind"] = HttpClientKind;
                }

                if (ClientName is not null)
                {
                    metadata["clientName"] = ClientName;
                }

                if (TypedClientType is not null)
                {
                    metadata["typedClientType"] = TypedClientType;
                }

                if (ConfigurationDelegatePreview is not null)
                {
                    metadata["configurationDelegatePreview"] = ConfigurationDelegatePreview;
                }

                if (UnknownTarget)
                {
                    metadata["unknownTarget"] = true;
                }

                return metadata;
            }
        }

        /// <summary>
        /// Carries wrapper traversal state while recursively inspecting IServiceCollection wrapper methods.
        /// </summary>
        private sealed class WrapperTraversalContext
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="WrapperTraversalContext"/> class.
            /// </summary>
            /// <param name="visitedWrapperKeys">The wrapper method keys already visited in the current chain.</param>
            /// <param name="invocations">The wrapper invocation descriptors in outer-to-inner order.</param>
            private WrapperTraversalContext(IReadOnlySet<string> visitedWrapperKeys, IReadOnlyList<WrapperInvocation> invocations)
            {
                // The context is immutable so each recursive branch can extend its own chain without affecting siblings.
                VisitedWrapperKeys = visitedWrapperKeys;
                Invocations = invocations;
            }

            /// <summary>Gets the wrapper invocation descriptors in outer-to-inner order.</summary>
            public IReadOnlyList<WrapperInvocation> Invocations { get; }

            /// <summary>Gets the number of wrapper hops represented by this context.</summary>
            public int Depth => Invocations.Count;

            /// <summary>Gets the formatted invocation chain from outer startup call to innermost wrapper.</summary>
            public string? InvocationChain => Depth == 0 ? null : string.Join(" -> ", Invocations.Select(invocation => invocation.MethodName));

            /// <summary>Gets the wrapper method keys already visited in the current chain.</summary>
            private IReadOnlySet<string> VisitedWrapperKeys { get; }

            /// <summary>
            /// Creates the root traversal context used for top-level syntax scanning.
            /// </summary>
            /// <returns>An empty wrapper traversal context.</returns>
            public static WrapperTraversalContext Root()
            {
                // Root context has no visited methods and therefore no wrapper metadata on direct registrations.
                return new WrapperTraversalContext(new HashSet<string>(StringComparer.Ordinal), []);
            }

            /// <summary>
            /// Determines whether a wrapper method has already been visited in this branch.
            /// </summary>
            /// <param name="wrapperKey">The stable wrapper method key to check.</param>
            /// <returns><see langword="true"/> when the wrapper has already been followed; otherwise, <see langword="false"/>.</returns>
            public bool Contains(string wrapperKey)
            {
                // Cycle detection is branch-local so separate startup calls can traverse the same wrapper independently.
                return VisitedWrapperKeys.Contains(wrapperKey);
            }

            /// <summary>
            /// Creates a child context that includes one additional wrapper invocation.
            /// </summary>
            /// <param name="wrapperKey">The stable wrapper method key being entered.</param>
            /// <param name="invocation">The source invocation descriptor for the wrapper call.</param>
            /// <returns>A child traversal context for nested wrapper analysis.</returns>
            public WrapperTraversalContext Enter(string wrapperKey, WrapperInvocation invocation)
            {
                // Copies keep recursion state explicit and avoid mutation during nested traversal.
                HashSet<string> visited = new(VisitedWrapperKeys, StringComparer.Ordinal)
                {
                    wrapperKey
                };
                return new WrapperTraversalContext(visited, [.. Invocations, invocation]);
            }
        }

        /// <summary>
        /// Describes one wrapper method invocation that led to a discovered registration.
        /// </summary>
        /// <param name="MethodName">The fully qualified wrapper method name used in invocation-chain metadata.</param>
        /// <param name="Invocation">The source invocation syntax node that called the wrapper.</param>
        /// <param name="MethodSymbol">The resolved wrapper method symbol used for evidence.</param>
        private sealed record WrapperInvocation(string MethodName, InvocationExpressionSyntax Invocation, IMethodSymbol MethodSymbol);
    }
}
