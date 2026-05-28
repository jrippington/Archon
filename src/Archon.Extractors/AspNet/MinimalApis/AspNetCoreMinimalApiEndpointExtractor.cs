using System.Security.Cryptography;
using System.Text;
using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Archon.Extractors.AspNet.MinimalApis
{
    /// <summary>
    /// Extracts graph-ready ASP.NET Core runtime facts from C# semantic documents.
    /// </summary>
    /// <remarks>
    /// The public type name remains stable from Work Item 1, but the implementation now covers the Work Item 2 ASP.NET Core slice: minimal API mappings, simple endpoint groups, attributed controllers and actions, MVC setup, controller mapping, middleware ordering, filter metadata, authorization metadata, and OpenAPI setup. The extractor performs static analysis only; it does not execute target application code, run MSBuild targets, restore packages, start Kestrel, or write directly to Neo4j.
    /// </remarks>
    public sealed class AspNetCoreMinimalApiEndpointExtractor
    {
        /// <summary>
        /// Stores the metadata value that identifies ASP.NET Core runtime facts.
        /// </summary>
        private const string Framework = "ASP.NET Core";

        /// <summary>
        /// Stores the runtime-kind metadata value for minimal API endpoint facts.
        /// </summary>
        private const string MinimalApiRuntimeKind = "MinimalApi";

        /// <summary>
        /// Stores the runtime-kind metadata value for controller action endpoint facts.
        /// </summary>
        private const string ControllerActionRuntimeKind = "ControllerAction";

        /// <summary>
        /// Stores the runtime-kind metadata value for project-level ASP.NET Core pipeline facts.
        /// </summary>
        private const string ProjectRuntimeKind = "AspNetCorePipeline";

        /// <summary>
        /// Stores the controller framework metadata value.
        /// </summary>
        private const string ControllerFramework = "ASP.NET Core MVC";

        /// <summary>
        /// Stores the detection-mode metadata value for direct minimal API invocations.
        /// </summary>
        private const string DirectMinimalApiDetectionMode = "DirectEndpointInvocation";

        /// <summary>
        /// Stores the detection-mode metadata value for controller action endpoints.
        /// </summary>
        private const string ControllerActionDetectionMode = "ControllerActionAttributes";

        /// <summary>
        /// Stores the detection-mode metadata value for project-level startup pipeline observations.
        /// </summary>
        private const string PipelineDetectionMode = "AspNetCoreStartupPipeline";

        /// <summary>
        /// Stores the confidence explanation for direct literal minimal API route extraction.
        /// </summary>
        private const string DirectLiteralConfidenceReason = "Direct MapGet invocation with literal route template in Program.cs.";

        /// <summary>
        /// Stores the confidence explanation for attributed controller action extraction.
        /// </summary>
        private const string ControllerActionConfidenceReason = "Controller action with deterministic route and HTTP verb attributes.";

        /// <summary>
        /// Stores the confidence explanation for project-level pipeline metadata.
        /// </summary>
        private const string PipelineConfidenceReason = "ASP.NET Core startup pipeline calls detected in Program.cs source order.";

        /// <summary>
        /// Stores the explicit unknown reason used when a route cannot be read as a compile-time string literal.
        /// </summary>
        private const string ComputedRouteUnknownReason = "MapGet route template is not a compile-time string literal.";

        /// <summary>
        /// Stores the explicit unknown reason used when an endpoint group prefix cannot be read as a compile-time string literal.
        /// </summary>
        private const string ComputedGroupUnknownReason = "Endpoint group route prefix is not a compile-time string literal.";

        /// <summary>
        /// Maps supported direct minimal API mapping method names to HTTP method metadata values.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> s_minimalApiMethodMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MapGet"] = "GET",
            ["MapPost"] = "POST",
            ["MapPut"] = "PUT",
            ["MapDelete"] = "DELETE",
            ["MapPatch"] = "PATCH",
            ["MapFallback"] = "FALLBACK"
        };

        /// <summary>
        /// Stores MVC service-registration calls that indicate controller or MVC setup.
        /// </summary>
        private static readonly HashSet<string> s_mvcSetupCalls = new(StringComparer.Ordinal)
        {
            "AddControllers",
            "AddControllersWithViews",
            "AddMvc"
        };

        /// <summary>
        /// Stores endpoint mapping calls that indicate controller route mapping setup.
        /// </summary>
        private static readonly HashSet<string> s_controllerMappingCalls = new(StringComparer.Ordinal)
        {
            "MapControllers",
            "MapControllerRoute",
            "MapDefaultControllerRoute"
        };

        /// <summary>
        /// Stores service-registration and middleware calls that indicate OpenAPI or Swagger setup.
        /// </summary>
        private static readonly HashSet<string> s_openApiCalls = new(StringComparer.Ordinal)
        {
            "AddEndpointsApiExplorer",
            "AddSwaggerGen",
            "AddOpenApi",
            "UseSwagger",
            "UseSwaggerUI",
            "MapOpenApi"
        };

        /// <summary>
        /// Stores common middleware invocation names that should be retained in project source order.
        /// </summary>
        private static readonly HashSet<string> s_middlewareCalls = new(StringComparer.Ordinal)
        {
            "UseRouting",
            "UseAuthentication",
            "UseAuthorization",
            "UseCors",
            "UseStaticFiles",
            "UseExceptionHandler",
            "UseDeveloperExceptionPage",
            "UseHttpsRedirection",
            "UseMiddleware",
            "UseSwagger",
            "UseSwaggerUI"
        };

        /// <summary>
        /// Extracts ASP.NET Core runtime graph facts from the supplied semantic documents.
        /// </summary>
        /// <param name="request">The snapshot and semantic document request that scopes runtime extraction.</param>
        /// <param name="cancellationToken">A token that stops syntax traversal before or during source inspection.</param>
        /// <returns>An extraction result containing runtime nodes, relationships, source evidence, and diagnostics.</returns>
        public MinimalApiEndpointExtractionResult Extract(MinimalApiEndpointExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // A single accumulator lets minimal APIs, controllers, and project pipeline metadata de-duplicate through the shared graph contract.
            ArgumentNullException.ThrowIfNull(request);
            ArchitectureSnapshotAccumulator accumulator = new();

            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeDocument(request.SnapshotStableKey, semanticDocument, accumulator, cancellationToken);
            }

            return new MinimalApiEndpointExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Analyzes one C# semantic document for ASP.NET Core runtime facts.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="semanticDocument">The semantic source document being inspected.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="cancellationToken">A token that stops syntax traversal and source-text access.</param>
        private static void AnalyzeDocument(StableKey snapshotStableKey, SemanticExtractionRequest semanticDocument, ArchitectureSnapshotAccumulator accumulator, CancellationToken cancellationToken)
        {
            // The document analysis deliberately keeps each source file independent because accumulation handles deterministic duplicate replacement.
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            SourceText sourceText = semanticDocument.SyntaxTree.GetText(cancellationToken);
            DocumentContext context = CreateDocumentContext(semanticDocument);

            if (semanticDocument.DocumentPath.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase))
            {
                AnalyzeProgramDocument(snapshotStableKey, semanticDocument, accumulator, root, sourceText, context, cancellationToken);
            }

            AnalyzeControllerDocument(snapshotStableKey, semanticDocument, accumulator, root, sourceText, context, cancellationToken);
        }

        /// <summary>
        /// Analyzes a <c>Program.cs</c> source file for endpoint mappings and startup pipeline calls.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="semanticDocument">The semantic source document being inspected.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="root">The syntax root of the source document.</param>
        /// <param name="sourceText">The source text used to derive evidence snippets.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="cancellationToken">A token that stops syntax traversal and semantic inspection.</param>
        private static void AnalyzeProgramDocument(StableKey snapshotStableKey, SemanticExtractionRequest semanticDocument, ArchitectureSnapshotAccumulator accumulator, SyntaxNode root, SourceText sourceText, DocumentContext context, CancellationToken cancellationToken)
        {
            // Program.cs owns startup-level observations: group variables, endpoint mappings, MVC setup, middleware order, and OpenAPI setup.
            IReadOnlyDictionary<string, GroupDescriptor> groups = DiscoverEndpointGroups(root);
            PipelineDescriptor pipeline = DiscoverPipeline(root, semanticDocument, cancellationToken);

            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                EndpointDescriptor? endpoint = TryCreateMinimalApiEndpointDescriptor(semanticDocument, invocation, sourceText, context, groups, cancellationToken);
                if (endpoint is not null)
                {
                    AccumulateEndpoint(snapshotStableKey, accumulator, endpoint, includeProjectNode: true);
                }
            }

            if (pipeline.HasFacts)
            {
                AccumulatePipelineProjectFact(snapshotStableKey, accumulator, context, pipeline);
            }
        }

        /// <summary>
        /// Analyzes a source file for ASP.NET Core controllers and action endpoints.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="semanticDocument">The semantic source document being inspected.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="root">The syntax root of the source document.</param>
        /// <param name="sourceText">The source text used to derive evidence snippets.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="cancellationToken">A token that stops syntax traversal and semantic inspection.</param>
        private static void AnalyzeControllerDocument(StableKey snapshotStableKey, SemanticExtractionRequest semanticDocument, ArchitectureSnapshotAccumulator accumulator, SyntaxNode root, SourceText sourceText, DocumentContext context, CancellationToken cancellationToken)
        {
            // Controller extraction scans every C# document because controllers commonly live outside Program.cs.
            foreach (ClassDeclarationSyntax classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ControllerDescriptor? controller = TryCreateControllerDescriptor(semanticDocument, classDeclaration, sourceText, context, cancellationToken);
                if (controller is null)
                {
                    continue;
                }

                AccumulateController(snapshotStableKey, accumulator, controller);
                foreach (MethodDeclarationSyntax methodDeclaration in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
                {
                    EndpointDescriptor? endpoint = TryCreateControllerActionEndpointDescriptor(semanticDocument, methodDeclaration, sourceText, context, controller, cancellationToken);
                    if (endpoint is not null)
                    {
                        AccumulateEndpoint(snapshotStableKey, accumulator, endpoint, includeProjectNode: false);
                    }
                }
            }
        }

        /// <summary>
        /// Creates normalized context values shared by all facts from one source document.
        /// </summary>
        /// <param name="semanticDocument">The semantic source document being inspected.</param>
        /// <returns>A document context carrying project and evidence identity values.</returns>
        private static DocumentContext CreateDocumentContext(SemanticExtractionRequest semanticDocument)
        {
            // Repository-relative paths keep stable keys and evidence independent of developer machine roots.
            string repositoryRelativeDocumentPath = GetRepositoryRelativePath(semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath);
            string projectPath = NormalizeRepositoryRelativePath(semanticDocument.ProjectContext);
            StableKey projectStableKey = StableKeyGenerator.ForProject(projectPath);
            return new DocumentContext(projectStableKey, Path.GetFileNameWithoutExtension(projectPath), repositoryRelativeDocumentPath);
        }

        /// <summary>
        /// Discovers literal and computed endpoint group variables declared in <c>Program.cs</c>.
        /// </summary>
        /// <param name="root">The syntax root of the source document.</param>
        /// <returns>A deterministic map from variable names to group descriptors.</returns>
        private static IReadOnlyDictionary<string, GroupDescriptor> DiscoverEndpointGroups(SyntaxNode root)
        {
            // Group discovery is intentionally simple: it follows local variables initialized directly from MapGroup calls.
            Dictionary<string, GroupDescriptor> groups = new(StringComparer.Ordinal);
            foreach (VariableDeclaratorSyntax variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (variable.Initializer?.Value is not InvocationExpressionSyntax invocation || GetInvocationMethodName(invocation) != "MapGroup")
                {
                    continue;
                }

                RouteTemplateResult route = TryReadRouteTemplate(invocation, argumentIndex: 0, ComputedGroupUnknownReason);
                groups[variable.Identifier.ValueText] = new GroupDescriptor(route.MetadataRouteTemplate, route.IsKnown, route.UnknownReason);
            }

            return groups;
        }

        /// <summary>
        /// Discovers MVC setup, controller mapping, middleware, custom middleware targets, and OpenAPI setup calls in source order.
        /// </summary>
        /// <param name="root">The syntax root of the source document.</param>
        /// <param name="semanticDocument">The semantic source document being inspected.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>A pipeline descriptor containing project-level ASP.NET Core runtime metadata.</returns>
        private static PipelineDescriptor DiscoverPipeline(SyntaxNode root, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Pipeline facts are project-level because the compiled graph model does not yet have a dedicated middleware node kind.
            List<string> mvcSetupCalls = [];
            List<string> controllerMappingCalls = [];
            List<string> middlewareOrder = [];
            List<string> middlewareTypes = [];
            bool openApiEnabled = false;
            List<InvocationExpressionSyntax> evidenceInvocations = [];

            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>().OrderBy(static invocation => invocation.SpanStart))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? methodName = GetInvocationMethodName(invocation);
                if (methodName is null)
                {
                    continue;
                }

                if (s_mvcSetupCalls.Contains(methodName))
                {
                    AddDistinct(mvcSetupCalls, methodName);
                    evidenceInvocations.Add(invocation);
                }

                if (s_controllerMappingCalls.Contains(methodName))
                {
                    AddDistinct(controllerMappingCalls, methodName);
                    evidenceInvocations.Add(invocation);
                }

                if (s_openApiCalls.Contains(methodName))
                {
                    openApiEnabled = true;
                    evidenceInvocations.Add(invocation);
                }

                if (s_middlewareCalls.Contains(methodName))
                {
                    middlewareOrder.Add(methodName);
                    evidenceInvocations.Add(invocation);
                    if (methodName == "UseMiddleware")
                    {
                        foreach (string typeName in ReadGenericTypeNames(invocation, semanticDocument, cancellationToken))
                        {
                            AddDistinct(middlewareTypes, typeName);
                        }
                    }
                }
            }

            return new PipelineDescriptor(mvcSetupCalls, controllerMappingCalls, middlewareOrder, middlewareTypes, openApiEnabled, evidenceInvocations);
        }

        /// <summary>
        /// Attempts to create a minimal API endpoint descriptor from one invocation.
        /// </summary>
        /// <param name="semanticDocument">The semantic source document that contains the invocation.</param>
        /// <param name="invocation">The invocation syntax node to inspect.</param>
        /// <param name="sourceText">The source text used for evidence preview creation.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="groups">The endpoint group descriptors discovered in the same source file.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>An endpoint descriptor when the invocation is supported; otherwise, <see langword="null" />.</returns>
        private static EndpointDescriptor? TryCreateMinimalApiEndpointDescriptor(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, SourceText sourceText, DocumentContext context, IReadOnlyDictionary<string, GroupDescriptor> groups, CancellationToken cancellationToken)
        {
            // Supported minimal API calls are converted into the shared endpoint descriptor used by graph projection.
            string? methodName = GetInvocationMethodName(invocation);
            if (methodName is null)
            {
                return null;
            }

            string? httpMethod = TryGetMinimalApiHttpMethod(methodName, invocation);
            if (httpMethod is null)
            {
                return null;
            }

            RouteTemplateResult route = TryReadRouteTemplate(invocation, argumentIndex: 0, ComputedRouteUnknownReason);
            GroupDescriptor? group = TryGetInvocationGroup(invocation, groups);
            string routeTemplate = CombineRouteTemplates(group?.RouteTemplate, route.MetadataRouteTemplate);
            string? unknownReason = group is { IsKnown: false } ? group.UnknownReason : route.UnknownReason;
            string handlerSymbol = CreateHandlerIdentity(semanticDocument, invocation, context.RepositoryRelativeDocumentPath, GetHandlerArgumentIndex(methodName), cancellationToken);
            FileLinePositionSpan lineSpan = semanticDocument.SyntaxTree.GetLineSpan(invocation.Span, cancellationToken);
            string displayName = $"{httpMethod} {(unknownReason is null ? routeTemplate : "<unknown>")}";
            string snippetPreview = CreateSnippetPreview(invocation, sourceText);
            GraphMetadata metadata = CreateEndpointMetadata(
                runtimeKind: MinimalApiRuntimeKind,
                routeTemplate: routeTemplate,
                httpMethod: httpMethod,
                handlerSymbol: handlerSymbol,
                detectionMode: methodName == "MapGet" ? "DirectMapGetInvocation" : DirectMinimalApiDetectionMode,
                confidenceReason: unknownReason is null ? DirectLiteralConfidenceReason : unknownReason,
                controllerName: null,
                actionName: null,
                authorizationPolicy: null,
                allowsAnonymous: null,
                filterTypes: [],
                endpointGroupPrefix: group?.IsKnown == true ? group.RouteTemplate : null);
            StableKey endpointStableKey = CreateEndpointStableKey(context.ProjectStableKey, routeTemplate, httpMethod, handlerSymbol);
            StableKey evidenceStableKey = CreateEvidenceStableKey(context.ProjectStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, methodName);

            return new EndpointDescriptor(
                context.ProjectStableKey,
                null,
                endpointStableKey,
                displayName,
                displayName.ToUpperInvariant(),
                routeTemplate,
                httpMethod,
                handlerSymbol,
                unknownReason,
                evidenceStableKey,
                context.RepositoryRelativeDocumentPath,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.EndLinePosition.Line + 1,
                methodName,
                "Program.cs",
                snippetPreview,
                CreateSha256Hash(snippetPreview),
                metadata,
                KnowledgeKindForUnknown(unknownReason),
                ConfidenceForUnknown(unknownReason));
        }

        /// <summary>
        /// Attempts to create a controller descriptor from one class declaration.
        /// </summary>
        /// <param name="semanticDocument">The semantic source document that contains the class.</param>
        /// <param name="classDeclaration">The class declaration syntax to inspect.</param>
        /// <param name="sourceText">The source text used for evidence preview creation.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>A controller descriptor when the class is an ASP.NET Core controller; otherwise, <see langword="null" />.</returns>
        private static ControllerDescriptor? TryCreateControllerDescriptor(SemanticExtractionRequest semanticDocument, ClassDeclarationSyntax classDeclaration, SourceText sourceText, DocumentContext context, CancellationToken cancellationToken)
        {
            // Controllers are recognized by common ASP.NET Core controller naming, base-type, or attributes to avoid requiring runtime assemblies.
            string typeName = GetTypeName(semanticDocument, classDeclaration, cancellationToken);
            string controllerName = TrimControllerSuffix(classDeclaration.Identifier.ValueText);
            bool hasControllerAttribute = classDeclaration.AttributeLists.SelectMany(static list => list.Attributes).Any(IsControllerMarkerAttribute);
            bool hasControllerBase = classDeclaration.BaseList?.Types.Any(static baseType => baseType.Type.ToString().EndsWith("ControllerBase", StringComparison.Ordinal) || baseType.Type.ToString().EndsWith("Controller", StringComparison.Ordinal)) == true;
            bool hasControllerName = classDeclaration.Identifier.ValueText.EndsWith("Controller", StringComparison.Ordinal);
            if (!hasControllerAttribute && !hasControllerBase && !hasControllerName)
            {
                return null;
            }

            string? routeTemplate = ReadRouteAttribute(classDeclaration.AttributeLists);
            AuthorizationDescriptor authorization = ReadAuthorization(classDeclaration.AttributeLists);
            IReadOnlyList<string> filters = ReadFilterTypes(classDeclaration.AttributeLists);
            FileLinePositionSpan lineSpan = semanticDocument.SyntaxTree.GetLineSpan(classDeclaration.Identifier.Span, cancellationToken);
            string snippetPreview = CreateSnippetPreview(classDeclaration, sourceText);
            StableKey controllerStableKey = CreateControllerStableKey(context.ProjectStableKey, typeName);
            StableKey evidenceStableKey = CreateEvidenceStableKey(context.ProjectStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, classDeclaration.Identifier.ValueText);
            GraphMetadata metadata = CreateControllerMetadata(controllerName, routeTemplate, authorization, filters);

            return new ControllerDescriptor(
                context.ProjectStableKey,
                controllerStableKey,
                classDeclaration.Identifier.ValueText,
                controllerName,
                typeName,
                routeTemplate,
                authorization,
                filters,
                evidenceStableKey,
                context.RepositoryRelativeDocumentPath,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.EndLinePosition.Line + 1,
                classDeclaration.Identifier.ValueText,
                classDeclaration.Identifier.ValueText,
                snippetPreview,
                CreateSha256Hash(snippetPreview),
                metadata);
        }

        /// <summary>
        /// Attempts to create an endpoint descriptor for one controller action method.
        /// </summary>
        /// <param name="semanticDocument">The semantic source document that contains the method.</param>
        /// <param name="methodDeclaration">The method declaration syntax to inspect.</param>
        /// <param name="sourceText">The source text used for evidence preview creation.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="controller">The controller descriptor that owns the action.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>An endpoint descriptor when the method has route or HTTP verb attributes; otherwise, <see langword="null" />.</returns>
        private static EndpointDescriptor? TryCreateControllerActionEndpointDescriptor(SemanticExtractionRequest semanticDocument, MethodDeclarationSyntax methodDeclaration, SourceText sourceText, DocumentContext context, ControllerDescriptor controller, CancellationToken cancellationToken)
        {
            // Action extraction requires an HTTP verb or route attribute because convention-only action discovery is represented later as explicit setup metadata.
            ActionRouteDescriptor? actionRoute = ReadActionRoute(methodDeclaration.AttributeLists);
            if (actionRoute is null)
            {
                return null;
            }

            AuthorizationDescriptor methodAuthorization = ReadAuthorization(methodDeclaration.AttributeLists);
            AuthorizationDescriptor authorization = methodAuthorization.HasAuthorizationData ? methodAuthorization : controller.Authorization;
            IReadOnlyList<string> filterTypes = MergeDistinct(controller.FilterTypes, ReadFilterTypes(methodDeclaration.AttributeLists));
            string routeTemplate = ReplaceControllerTokens(CombineRouteTemplates(controller.RouteTemplate, actionRoute.RouteTemplate), controller.ControllerName);
            string handlerSymbol = GetMethodName(semanticDocument, methodDeclaration, cancellationToken);
            string displayName = $"{actionRoute.HttpMethod} {routeTemplate}";
            FileLinePositionSpan lineSpan = semanticDocument.SyntaxTree.GetLineSpan(methodDeclaration.Identifier.Span, cancellationToken);
            string snippetPreview = CreateSnippetPreview(methodDeclaration, sourceText);
            StableKey endpointStableKey = CreateEndpointStableKey(context.ProjectStableKey, routeTemplate, actionRoute.HttpMethod, handlerSymbol);
            StableKey evidenceStableKey = CreateEvidenceStableKey(context.ProjectStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, methodDeclaration.Identifier.ValueText);
            GraphMetadata metadata = CreateEndpointMetadata(
                runtimeKind: ControllerActionRuntimeKind,
                routeTemplate: routeTemplate,
                httpMethod: actionRoute.HttpMethod,
                handlerSymbol: handlerSymbol,
                detectionMode: ControllerActionDetectionMode,
                confidenceReason: ControllerActionConfidenceReason,
                controllerName: controller.ControllerName,
                actionName: methodDeclaration.Identifier.ValueText,
                authorizationPolicy: authorization.AuthorizationPolicy,
                allowsAnonymous: authorization.AllowsAnonymous,
                filterTypes: filterTypes,
                endpointGroupPrefix: null);

            return new EndpointDescriptor(
                context.ProjectStableKey,
                controller.ControllerStableKey,
                endpointStableKey,
                displayName,
                displayName.ToUpperInvariant(),
                routeTemplate,
                actionRoute.HttpMethod,
                handlerSymbol,
                null,
                evidenceStableKey,
                context.RepositoryRelativeDocumentPath,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.EndLinePosition.Line + 1,
                methodDeclaration.Identifier.ValueText,
                controller.QualifiedName,
                snippetPreview,
                CreateSha256Hash(snippetPreview),
                metadata,
                KnowledgeKind.Fact,
                Confidence.High);
        }

        /// <summary>
        /// Accumulates an endpoint node, evidence record, and declaration relationship.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="descriptor">The endpoint descriptor to project.</param>
        /// <param name="includeProjectNode">Whether to emit a fallback project node for extractor-only output.</param>
        private static void AccumulateEndpoint(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, EndpointDescriptor descriptor, bool includeProjectNode)
        {
            // All endpoint shapes use one projection path so duplicate handling and evidence behavior remain consistent.
            EvidenceRecord evidence = CreateEvidenceRecord(snapshotStableKey, descriptor);
            ArchitectureNode endpointNode = CreateEndpointNode(snapshotStableKey, descriptor, evidence.StableKey);
            ArchitectureEdge declarationEdge = CreateDeclaresEndpointEdge(snapshotStableKey, descriptor, evidence.StableKey);
            accumulator.AddEvidence(evidence).AddNode(endpointNode).AddEdge(declarationEdge);
            if (includeProjectNode)
            {
                accumulator.AddNode(CreateProjectNode(snapshotStableKey, descriptor.ProjectStableKey, Path.GetFileNameWithoutExtension(descriptor.ProjectStableKey.Value), evidence.StableKey, GraphMetadata.From(new Dictionary<string, object?>
                {
                    ["detectionMode"] = descriptor.Metadata.ToCanonicalJson().Contains(PipelineDetectionMode, StringComparison.Ordinal) ? PipelineDetectionMode : DirectMinimalApiDetectionMode,
                    ["runtimeKind"] = MinimalApiRuntimeKind
                })));
            }
        }

        /// <summary>
        /// Accumulates a controller node and source evidence record.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="descriptor">The controller descriptor to project.</param>
        private static void AccumulateController(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, ControllerDescriptor descriptor)
        {
            // Controller facts are separated from action endpoint facts so future consumers can query controller ownership directly.
            EvidenceRecord evidence = new(
                snapshotStableKey,
                descriptor.EvidenceStableKey,
                EvidenceKind.SourceCode,
                RepositoryRelativePath.Parse(descriptor.EvidenceFilePath),
                descriptor.EvidenceStartLine,
                descriptor.EvidenceEndLine,
                descriptor.SymbolName,
                descriptor.ContainingSymbol,
                descriptor.SnippetHash,
                descriptor.SnippetPreview,
                KnowledgeKind.Fact,
                Confidence.High,
                UnknownState.Known,
                GraphMetadata.From(new Dictionary<string, object?>
                {
                    ["detectionMode"] = ControllerActionDetectionMode,
                    ["framework"] = Framework,
                    ["runtimeKind"] = "Controller"
                }),
                FingerprintGenerator.ForEvidence(EvidenceKind.SourceCode, descriptor.EvidenceFilePath, descriptor.EvidenceStartLine, descriptor.EvidenceEndLine, descriptor.SymbolName, KnowledgeKind.Fact, GraphMetadata.From(new Dictionary<string, object?>
                {
                    ["detectionMode"] = ControllerActionDetectionMode,
                    ["framework"] = Framework,
                    ["runtimeKind"] = "Controller"
                })));
            ArchitectureNode controllerNode = new(
                snapshotStableKey,
                descriptor.ControllerStableKey,
                NodeKind.Controller,
                descriptor.DisplayName,
                descriptor.QualifiedName,
                descriptor.DisplayName.ToUpperInvariant(),
                "C#",
                descriptor.ProjectStableKey,
                descriptor.ProjectStableKey,
                KnowledgeKind.Fact,
                null,
                null,
                Confidence.High,
                UnknownState.Known,
                evidence.StableKey,
                descriptor.Metadata,
                FingerprintGenerator.ForNode(NodeKind.Controller, descriptor.DisplayName, descriptor.QualifiedName, descriptor.DisplayName.ToUpperInvariant(), KnowledgeKind.Fact, descriptor.Metadata));
            accumulator.AddEvidence(evidence).AddNode(controllerNode);
        }

        /// <summary>
        /// Accumulates project-level startup pipeline metadata and source evidence.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="pipeline">The pipeline descriptor to project.</param>
        private static void AccumulatePipelineProjectFact(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, DocumentContext context, PipelineDescriptor pipeline)
        {
            // Project-level metadata gives contributors visibility into middleware and OpenAPI setup before dedicated runtime node kinds exist.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = PipelineConfidenceReason,
                ["controllerMappingCalls"] = pipeline.ControllerMappingCalls,
                ["detectionMode"] = PipelineDetectionMode,
                ["framework"] = Framework,
                ["middlewareOrder"] = pipeline.MiddlewareOrder,
                ["middlewareTypes"] = pipeline.MiddlewareTypes,
                ["mvcSetupCalls"] = pipeline.MvcSetupCalls,
                ["openApiEnabled"] = pipeline.OpenApiEnabled,
                ["runtimeKind"] = ProjectRuntimeKind
            });
            EvidenceRecord? primaryEvidence = null;
            foreach (InvocationExpressionSyntax invocation in pipeline.EvidenceInvocations)
            {
                SourceText sourceText = invocation.SyntaxTree.GetText();
                FileLinePositionSpan lineSpan = invocation.SyntaxTree.GetLineSpan(invocation.Span);
                string symbolName = GetInvocationMethodName(invocation) ?? "AspNetCorePipeline";
                string snippetPreview = CreateSnippetPreview(invocation, sourceText);
                EvidenceRecord evidence = new(
                    snapshotStableKey,
                    CreateEvidenceStableKey(context.ProjectStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, symbolName),
                    EvidenceKind.SourceCode,
                    RepositoryRelativePath.Parse(context.RepositoryRelativeDocumentPath),
                    lineSpan.StartLinePosition.Line + 1,
                    lineSpan.EndLinePosition.Line + 1,
                    symbolName,
                    "Program.cs",
                    CreateSha256Hash(snippetPreview),
                    snippetPreview,
                    KnowledgeKind.Fact,
                    Confidence.High,
                    UnknownState.Known,
                    GraphMetadata.From(new Dictionary<string, object?>
                    {
                        ["detectionMode"] = PipelineDetectionMode,
                        ["framework"] = Framework,
                        ["runtimeKind"] = ProjectRuntimeKind
                    }),
                    FingerprintGenerator.ForEvidence(EvidenceKind.SourceCode, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, symbolName, KnowledgeKind.Fact, GraphMetadata.From(new Dictionary<string, object?>
                    {
                        ["detectionMode"] = PipelineDetectionMode,
                        ["framework"] = Framework,
                        ["runtimeKind"] = ProjectRuntimeKind
                    })));
                primaryEvidence ??= evidence;
                accumulator.AddEvidence(evidence);
            }

            accumulator.AddNode(CreateProjectNode(snapshotStableKey, context.ProjectStableKey, context.ProjectDisplayName, primaryEvidence?.StableKey, metadata));
        }

        /// <summary>
        /// Creates a fallback or metadata-enriched project node.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the project node.</param>
        /// <param name="projectStableKey">The stable key of the project node.</param>
        /// <param name="projectDisplayName">The display name derived from the project path.</param>
        /// <param name="evidenceStableKey">The optional evidence stable key that explains the project runtime fact.</param>
        /// <param name="metadata">The project metadata to attach to the node.</param>
        /// <returns>A project architecture node scoped to the current snapshot.</returns>
        private static ArchitectureNode CreateProjectNode(StableKey snapshotStableKey, StableKey projectStableKey, string projectDisplayName, StableKey? evidenceStableKey, GraphMetadata metadata)
        {
            // The project extraction stage also emits project nodes in API runs; accumulator de-duplication keeps this fallback deterministic.
            return new ArchitectureNode(
                snapshotStableKey,
                projectStableKey,
                NodeKind.Project,
                projectDisplayName,
                projectDisplayName,
                projectDisplayName,
                "C#",
                projectStableKey,
                null,
                KnowledgeKind.Fact,
                null,
                null,
                Confidence.High,
                UnknownState.Known,
                evidenceStableKey,
                metadata,
                FingerprintGenerator.ForNode(NodeKind.Project, projectDisplayName, projectDisplayName, projectDisplayName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates an endpoint architecture node from a normalized endpoint descriptor.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the endpoint node.</param>
        /// <param name="descriptor">The normalized endpoint descriptor.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the endpoint node.</param>
        /// <returns>An endpoint architecture node with runtime metadata and unknown state when applicable.</returns>
        private static ArchitectureNode CreateEndpointNode(StableKey snapshotStableKey, EndpointDescriptor descriptor, StableKey evidenceStableKey)
        {
            // Runtime metadata uses lower-camel-case field names while unknown-state data remains in normalized model fields.
            UnknownState unknownState = descriptor.UnknownReason is null ? UnknownState.Known : UnknownState.Unknown(descriptor.UnknownReason);
            return new ArchitectureNode(
                snapshotStableKey,
                descriptor.EndpointStableKey,
                NodeKind.Endpoint,
                descriptor.DisplayName,
                descriptor.DisplayName,
                descriptor.SearchName,
                "C#",
                descriptor.ProjectStableKey,
                descriptor.ParentStableKey,
                descriptor.KnowledgeKind,
                null,
                null,
                descriptor.Confidence,
                unknownState,
                evidenceStableKey,
                descriptor.Metadata,
                FingerprintGenerator.ForNode(NodeKind.Endpoint, descriptor.DisplayName, descriptor.DisplayName, descriptor.SearchName, descriptor.KnowledgeKind, descriptor.Metadata));
        }

        /// <summary>
        /// Creates a direct declaration relationship for an endpoint.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the edge.</param>
        /// <param name="descriptor">The normalized endpoint descriptor.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the edge.</param>
        /// <returns>A direct <c>DECLARES_ENDPOINT</c> relationship.</returns>
        private static ArchitectureEdge CreateDeclaresEndpointEdge(StableKey snapshotStableKey, EndpointDescriptor descriptor, StableKey evidenceStableKey)
        {
            // The source is the controller for controller actions and the project for minimal APIs, matching runtime relationship direction.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = descriptor.Metadata.ToCanonicalJson().Contains(ControllerActionDetectionMode, StringComparison.Ordinal) ? ControllerActionDetectionMode : DirectMinimalApiDetectionMode,
                ["framework"] = Framework,
                ["runtimeKind"] = descriptor.ParentStableKey == descriptor.ProjectStableKey ? MinimalApiRuntimeKind : ControllerActionRuntimeKind
            });
            StableKey sourceStableKey = descriptor.ParentStableKey ?? descriptor.ProjectStableKey;
            StableKey edgeStableKey = new($"edge://DECLARES_ENDPOINT:{sourceStableKey.Value}->{descriptor.EndpointStableKey.Value}");
            return new ArchitectureEdge(
                snapshotStableKey,
                edgeStableKey,
                EdgeKind.DeclaresEndpoint,
                sourceStableKey,
                descriptor.EndpointStableKey,
                isDirect: true,
                descriptor.KnowledgeKind,
                descriptor.Confidence,
                descriptor.UnknownReason is null ? UnknownState.Known : UnknownState.Unknown(descriptor.UnknownReason),
                evidenceStableKey,
                metadata,
                FingerprintGenerator.ForEdge(EdgeKind.DeclaresEndpoint, sourceStableKey, descriptor.EndpointStableKey, isDirect: true, descriptor.KnowledgeKind, metadata));
        }

        /// <summary>
        /// Creates a source-code evidence record for one endpoint fact.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the evidence.</param>
        /// <param name="descriptor">The normalized endpoint descriptor that carries source location details.</param>
        /// <returns>A source-code evidence record for the endpoint declaration.</returns>
        private static EvidenceRecord CreateEvidenceRecord(StableKey snapshotStableKey, EndpointDescriptor descriptor)
        {
            // Evidence is recorded before nodes and edges so those facts can point at one explanation record.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = descriptor.Metadata.ToCanonicalJson().Contains(ControllerActionDetectionMode, StringComparison.Ordinal) ? ControllerActionDetectionMode : DirectMinimalApiDetectionMode,
                ["framework"] = Framework,
                ["runtimeKind"] = descriptor.ParentStableKey == descriptor.ProjectStableKey ? MinimalApiRuntimeKind : ControllerActionRuntimeKind
            });
            return new EvidenceRecord(
                snapshotStableKey,
                descriptor.EvidenceStableKey,
                EvidenceKind.SourceCode,
                RepositoryRelativePath.Parse(descriptor.EvidenceFilePath),
                descriptor.EvidenceStartLine,
                descriptor.EvidenceEndLine,
                descriptor.SymbolName,
                descriptor.ContainingSymbol,
                descriptor.SnippetHash,
                descriptor.SnippetPreview,
                descriptor.KnowledgeKind,
                descriptor.Confidence,
                descriptor.UnknownReason is null ? UnknownState.Known : UnknownState.Unknown(descriptor.UnknownReason),
                metadata,
                FingerprintGenerator.ForEvidence(EvidenceKind.SourceCode, descriptor.EvidenceFilePath, descriptor.EvidenceStartLine, descriptor.EvidenceEndLine, descriptor.SymbolName, descriptor.KnowledgeKind, metadata));
        }

        /// <summary>
        /// Creates endpoint metadata using stable lower-camel-case field names.
        /// </summary>
        /// <param name="runtimeKind">The endpoint runtime kind metadata value.</param>
        /// <param name="routeTemplate">The endpoint route template or explicit unknown placeholder.</param>
        /// <param name="httpMethod">The endpoint HTTP method.</param>
        /// <param name="handlerSymbol">The endpoint handler identity.</param>
        /// <param name="detectionMode">The detection mode used for the endpoint.</param>
        /// <param name="confidenceReason">The confidence reason for the endpoint.</param>
        /// <param name="controllerName">The optional controller name for controller action endpoints.</param>
        /// <param name="actionName">The optional action name for controller action endpoints.</param>
        /// <param name="authorizationPolicy">The optional authorization policy name.</param>
        /// <param name="allowsAnonymous">Whether the endpoint allows anonymous access when known.</param>
        /// <param name="filterTypes">The filter attribute types associated with the endpoint.</param>
        /// <param name="endpointGroupPrefix">The optional literal endpoint group prefix.</param>
        /// <returns>Canonical graph metadata for the endpoint node.</returns>
        private static GraphMetadata CreateEndpointMetadata(string runtimeKind, string routeTemplate, string httpMethod, string handlerSymbol, string detectionMode, string confidenceReason, string? controllerName, string? actionName, string? authorizationPolicy, bool? allowsAnonymous, IReadOnlyList<string> filterTypes, string? endpointGroupPrefix)
        {
            // Optional metadata is included only when supported by evidence so consumers do not confuse absence with a false assertion.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["confidenceReason"] = confidenceReason,
                ["detectionMode"] = detectionMode,
                ["framework"] = Framework,
                ["handlerSymbol"] = handlerSymbol,
                ["httpMethod"] = httpMethod,
                ["routeTemplate"] = routeTemplate,
                ["runtimeKind"] = runtimeKind
            };
            AddOptional(values, "controllerName", controllerName);
            AddOptional(values, "actionName", actionName);
            AddOptional(values, "authorizationPolicy", authorizationPolicy);
            AddOptional(values, "allowsAnonymous", allowsAnonymous);
            AddOptional(values, "endpointGroupPrefix", endpointGroupPrefix);
            if (filterTypes.Count > 0)
            {
                values["filterTypes"] = filterTypes;
            }

            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Creates controller metadata using stable lower-camel-case field names.
        /// </summary>
        /// <param name="controllerName">The normalized controller name without the Controller suffix.</param>
        /// <param name="routeTemplate">The optional controller-level route template.</param>
        /// <param name="authorization">The controller-level authorization descriptor.</param>
        /// <param name="filterTypes">The controller-level filter attribute types.</param>
        /// <returns>Canonical graph metadata for the controller node.</returns>
        private static GraphMetadata CreateControllerMetadata(string controllerName, string? routeTemplate, AuthorizationDescriptor authorization, IReadOnlyList<string> filterTypes)
        {
            // Controller metadata captures class-level behavior that action endpoint metadata can inherit.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["confidenceReason"] = "ASP.NET Core controller detected from controller naming, base type, or route attributes.",
                ["controllerName"] = controllerName,
                ["detectionMode"] = ControllerActionDetectionMode,
                ["framework"] = Framework,
                ["runtimeKind"] = "Controller"
            };
            AddOptional(values, "routeTemplate", routeTemplate);
            AddOptional(values, "authorizationPolicy", authorization.AuthorizationPolicy);
            AddOptional(values, "allowsAnonymous", authorization.AllowsAnonymous);
            if (filterTypes.Count > 0)
            {
                values["filterTypes"] = filterTypes;
            }

            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Reads the HTTP method and action route from method attributes.
        /// </summary>
        /// <param name="attributeLists">The method attribute lists to inspect.</param>
        /// <returns>An action route descriptor when route or verb evidence exists; otherwise, <see langword="null" />.</returns>
        private static ActionRouteDescriptor? ReadActionRoute(SyntaxList<AttributeListSyntax> attributeLists)
        {
            // HTTP verb attributes supply both the method and, optionally, an action route segment.
            foreach (AttributeSyntax attribute in attributeLists.SelectMany(static list => list.Attributes))
            {
                string name = NormalizeAttributeName(attribute.Name.ToString());
                string? httpMethod = name switch
                {
                    "HttpGet" => "GET",
                    "HttpPost" => "POST",
                    "HttpPut" => "PUT",
                    "HttpDelete" => "DELETE",
                    "HttpPatch" => "PATCH",
                    _ => null
                };
                if (httpMethod is not null)
                {
                    return new ActionRouteDescriptor(httpMethod, ReadStringArgument(attribute, 0) ?? string.Empty);
                }
            }

            string? route = ReadRouteAttribute(attributeLists);
            return route is null ? null : new ActionRouteDescriptor("ANY", route);
        }

        /// <summary>
        /// Reads a route template from route attributes.
        /// </summary>
        /// <param name="attributeLists">The attribute lists to inspect.</param>
        /// <returns>The route template when a route attribute contains a string literal; otherwise, <see langword="null" />.</returns>
        private static string? ReadRouteAttribute(SyntaxList<AttributeListSyntax> attributeLists)
        {
            // Route attributes use a string constructor argument in the supported deterministic shape.
            foreach (AttributeSyntax attribute in attributeLists.SelectMany(static list => list.Attributes))
            {
                if (NormalizeAttributeName(attribute.Name.ToString()) == "Route")
                {
                    return ReadStringArgument(attribute, 0);
                }
            }

            return null;
        }

        /// <summary>
        /// Reads authorization and anonymous-access metadata from attributes.
        /// </summary>
        /// <param name="attributeLists">The attribute lists to inspect.</param>
        /// <returns>An authorization descriptor for the supplied attributes.</returns>
        private static AuthorizationDescriptor ReadAuthorization(SyntaxList<AttributeListSyntax> attributeLists)
        {
            // Attribute syntax is enough for policy names and anonymous access flags in the supported source shapes.
            string? policy = null;
            bool? allowsAnonymous = null;
            foreach (AttributeSyntax attribute in attributeLists.SelectMany(static list => list.Attributes))
            {
                string name = NormalizeAttributeName(attribute.Name.ToString());
                if (name == "AllowAnonymous")
                {
                    allowsAnonymous = true;
                }

                if (name == "Authorize")
                {
                    policy = ReadNamedStringArgument(attribute, "Policy") ?? ReadStringArgument(attribute, 0) ?? policy;
                }
            }

            return new AuthorizationDescriptor(policy, allowsAnonymous);
        }

        /// <summary>
        /// Reads filter attribute type names from controller or action attributes.
        /// </summary>
        /// <param name="attributeLists">The attribute lists to inspect.</param>
        /// <returns>Distinct filter attribute names in source order.</returns>
        private static IReadOnlyList<string> ReadFilterTypes(SyntaxList<AttributeListSyntax> attributeLists)
        {
            // Filters are represented as attribute type metadata because no dedicated graph node kind exists for filters yet.
            List<string> filters = [];
            foreach (AttributeSyntax attribute in attributeLists.SelectMany(static list => list.Attributes))
            {
                string name = NormalizeAttributeName(attribute.Name.ToString());
                if (name.EndsWith("Filter", StringComparison.Ordinal) || name.EndsWith("FilterAttribute", StringComparison.Ordinal))
                {
                    AddDistinct(filters, name.EndsWith("Attribute", StringComparison.Ordinal) ? name : name + "Attribute");
                }
            }

            return filters;
        }

        /// <summary>
        /// Determines whether an attribute is a controller marker.
        /// </summary>
        /// <param name="attribute">The attribute syntax to inspect.</param>
        /// <returns><see langword="true" /> when the attribute indicates controller behavior; otherwise, <see langword="false" />.</returns>
        private static bool IsControllerMarkerAttribute(AttributeSyntax attribute)
        {
            // ApiController and Route attributes are deterministic controller signals for this source-level extractor.
            string name = NormalizeAttributeName(attribute.Name.ToString());
            return name == "ApiController" || name == "Route";
        }

        /// <summary>
        /// Gets a minimal API HTTP method value for an invocation method.
        /// </summary>
        /// <param name="methodName">The invocation method name.</param>
        /// <param name="invocation">The invocation syntax node.</param>
        /// <returns>The HTTP method when the invocation is supported; otherwise, <see langword="null" />.</returns>
        private static string? TryGetMinimalApiHttpMethod(string methodName, InvocationExpressionSyntax invocation)
        {
            // MapMethods reads the first literal method from the supplied method collection; unsupported dynamic collections remain explicit ANY metadata.
            if (s_minimalApiMethodMap.TryGetValue(methodName, out string? httpMethod))
            {
                return httpMethod;
            }

            if (methodName == "MapMethods")
            {
                return TryReadMapMethodsHttpMethod(invocation) ?? "ANY";
            }

            return null;
        }

        /// <summary>
        /// Reads the first HTTP method literal from a <c>MapMethods</c> invocation.
        /// </summary>
        /// <param name="invocation">The <c>MapMethods</c> invocation syntax node.</param>
        /// <returns>The first HTTP method literal when available; otherwise, <see langword="null" />.</returns>
        private static string? TryReadMapMethodsHttpMethod(InvocationExpressionSyntax invocation)
        {
            // The second MapMethods argument is commonly an array or collection expression containing method names.
            ExpressionSyntax? methodsExpression = invocation.ArgumentList.Arguments.Skip(1).FirstOrDefault()?.Expression;
            IEnumerable<ExpressionSyntax> candidates = methodsExpression switch
            {
                InitializerExpressionSyntax initializer => initializer.Expressions,
                ArrayCreationExpressionSyntax arrayCreation when arrayCreation.Initializer is not null => arrayCreation.Initializer.Expressions,
                ImplicitArrayCreationExpressionSyntax implicitArray when implicitArray.Initializer is not null => implicitArray.Initializer.Expressions,
                _ => []
            };
            return candidates.OfType<LiteralExpressionSyntax>().Select(static literal => literal.Token.Value as string).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.ToUpperInvariant();
        }

        /// <summary>
        /// Gets the handler argument index for a supported minimal API invocation.
        /// </summary>
        /// <param name="methodName">The endpoint mapping method name.</param>
        /// <returns>The zero-based argument index expected to contain the handler expression.</returns>
        private static int GetHandlerArgumentIndex(string methodName)
        {
            // MapMethods has route and method-list arguments before the handler; the other supported calls have route then handler.
            return methodName == "MapMethods" ? 2 : 1;
        }

        /// <summary>
        /// Attempts to read one route-template argument as a compile-time string literal.
        /// </summary>
        /// <param name="invocation">The invocation syntax node.</param>
        /// <param name="argumentIndex">The zero-based route argument index.</param>
        /// <param name="unknownReason">The unknown reason to use when the route is not a literal.</param>
        /// <returns>A route-template result that carries either the known route or an explicit unknown placeholder.</returns>
        private static RouteTemplateResult TryReadRouteTemplate(InvocationExpressionSyntax invocation, int argumentIndex, string unknownReason)
        {
            // Only literal route templates are deterministic in this slice; other expressions become explicit unknowns.
            ExpressionSyntax? routeExpression = invocation.ArgumentList.Arguments.Skip(argumentIndex).FirstOrDefault()?.Expression;
            if (routeExpression is LiteralExpressionSyntax literal && literal.Token.Value is string literalValue && !string.IsNullOrWhiteSpace(literalValue))
            {
                return new RouteTemplateResult(NormalizeRouteTemplate(literalValue), IsKnown: true, UnknownReason: null);
            }

            return new RouteTemplateResult("<unknown>", IsKnown: false, UnknownReason: unknownReason);
        }

        /// <summary>
        /// Attempts to resolve the endpoint group associated with a minimal API invocation receiver.
        /// </summary>
        /// <param name="invocation">The endpoint mapping invocation.</param>
        /// <param name="groups">The discovered endpoint group descriptors.</param>
        /// <returns>The group descriptor when the invocation receiver is a known group variable; otherwise, <see langword="null" />.</returns>
        private static GroupDescriptor? TryGetInvocationGroup(InvocationExpressionSyntax invocation, IReadOnlyDictionary<string, GroupDescriptor> groups)
        {
            // Group support follows simple variable receivers such as api.MapPost(...), which covers common minimal API grouping patterns.
            if (invocation.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver })
            {
                return groups.TryGetValue(receiver.Identifier.ValueText, out GroupDescriptor? group) ? group : null;
            }

            return null;
        }

        /// <summary>
        /// Combines route template segments while preserving unknown placeholders.
        /// </summary>
        /// <param name="prefix">The optional route prefix.</param>
        /// <param name="route">The route segment.</param>
        /// <returns>A normalized combined route template.</returns>
        private static string CombineRouteTemplates(string? prefix, string? route)
        {
            // Route composition avoids double slashes and preserves explicit unknowns as queryable metadata.
            if (string.Equals(prefix, "<unknown>", StringComparison.Ordinal) || string.Equals(route, "<unknown>", StringComparison.Ordinal))
            {
                return "<unknown>";
            }

            string normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? string.Empty : NormalizeRouteTemplate(prefix).TrimEnd('/');
            string normalizedRoute = string.IsNullOrWhiteSpace(route) ? string.Empty : NormalizeRouteTemplate(route).TrimStart('/');
            if (normalizedPrefix.Length == 0 && normalizedRoute.Length == 0)
            {
                return "/";
            }

            if (normalizedPrefix.Length == 0)
            {
                return "/" + normalizedRoute;
            }

            if (normalizedRoute.Length == 0)
            {
                return normalizedPrefix;
            }

            return normalizedPrefix + "/" + normalizedRoute;
        }

        /// <summary>
        /// Replaces ASP.NET Core controller route tokens in a route template.
        /// </summary>
        /// <param name="routeTemplate">The route template that may contain tokens.</param>
        /// <param name="controllerName">The normalized controller name.</param>
        /// <returns>The route template with supported tokens replaced.</returns>
        private static string ReplaceControllerTokens(string routeTemplate, string controllerName)
        {
            // Token replacement is intentionally deterministic and limited to the common controller token needed for Work Item 2.
            return routeTemplate.Replace("[controller]", controllerName.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Normalizes a route template for metadata and stable identity.
        /// </summary>
        /// <param name="routeTemplate">The route template read from source.</param>
        /// <returns>The route template with one leading slash and no surrounding whitespace.</returns>
        private static string NormalizeRouteTemplate(string routeTemplate)
        {
            // Endpoint route identity should not change when source authors include or omit a leading slash.
            string trimmedRoute = routeTemplate.Trim();
            return "/" + trimmedRoute.TrimStart('/');
        }

        /// <summary>
        /// Creates the handler identity for a minimal API endpoint mapping.
        /// </summary>
        /// <param name="semanticDocument">The semantic source document that contains the invocation.</param>
        /// <param name="invocation">The endpoint invocation syntax node.</param>
        /// <param name="repositoryRelativeDocumentPath">The repository-relative source document path.</param>
        /// <param name="handlerArgumentIndex">The zero-based argument index containing the handler expression.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>A deterministic handler identity suitable for endpoint stable-key input and metadata.</returns>
        private static string CreateHandlerIdentity(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, string repositoryRelativeDocumentPath, int handlerArgumentIndex, CancellationToken cancellationToken)
        {
            // The handler argument commonly carries a lambda, method group, or delegate; symbol binding is used when available and source location otherwise.
            ExpressionSyntax? handlerExpression = invocation.ArgumentList.Arguments.Skip(handlerArgumentIndex).FirstOrDefault()?.Expression;
            if (handlerExpression is null)
            {
                return $"handler@{repositoryRelativeDocumentPath}:{invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";
            }

            SymbolInfo symbolInfo = semanticDocument.SemanticModel.GetSymbolInfo(handlerExpression, cancellationToken);
            if (symbolInfo.Symbol is ISymbol symbol)
            {
                return symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            }

            FileLinePositionSpan lineSpan = handlerExpression.SyntaxTree.GetLineSpan(handlerExpression.Span, cancellationToken);
            string handlerKind = handlerExpression is LambdaExpressionSyntax ? "lambda" : "handler";
            return $"{handlerKind}@{repositoryRelativeDocumentPath}:{lineSpan.StartLinePosition.Line + 1}:{lineSpan.StartLinePosition.Character + 1}";
        }

        /// <summary>
        /// Gets the simple method name from an invocation expression.
        /// </summary>
        /// <param name="invocation">The invocation syntax node.</param>
        /// <returns>The invocation method name when it can be read syntactically; otherwise, <see langword="null" />.</returns>
        private static string? GetInvocationMethodName(InvocationExpressionSyntax invocation)
        {
            // Syntactic names are sufficient for the supported static-analysis patterns and do not require ASP.NET Core references.
            return invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                GenericNameSyntax genericName => genericName.Identifier.ValueText,
                _ => null
            };
        }

        /// <summary>
        /// Reads generic type argument names from an invocation expression.
        /// </summary>
        /// <param name="invocation">The invocation syntax node.</param>
        /// <param name="semanticDocument">The semantic source document that contains the invocation.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>Distinct generic type argument names in source order.</returns>
        private static IReadOnlyList<string> ReadGenericTypeNames(InvocationExpressionSyntax invocation, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Generic names may bind to fully qualified symbols; when binding is unavailable the source spelling is still useful evidence.
            if (invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName })
            {
                return [];
            }

            List<string> typeNames = [];
            foreach (TypeSyntax typeSyntax in genericName.TypeArgumentList.Arguments)
            {
                SymbolInfo symbolInfo = semanticDocument.SemanticModel.GetSymbolInfo(typeSyntax, cancellationToken);
                AddDistinct(typeNames, symbolInfo.Symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? typeSyntax.ToString());
            }

            return typeNames;
        }

        /// <summary>
        /// Reads a string constructor argument from an attribute.
        /// </summary>
        /// <param name="attribute">The attribute syntax to inspect.</param>
        /// <param name="argumentIndex">The zero-based argument index to read.</param>
        /// <returns>The string literal argument value when available; otherwise, <see langword="null" />.</returns>
        private static string? ReadStringArgument(AttributeSyntax attribute, int argumentIndex)
        {
            // Attribute route and policy values are deterministic only when they are literal strings.
            ExpressionSyntax? expression = attribute.ArgumentList?.Arguments.Skip(argumentIndex).FirstOrDefault()?.Expression;
            return expression is LiteralExpressionSyntax literal && literal.Token.Value is string value ? value : null;
        }

        /// <summary>
        /// Reads a named string argument from an attribute.
        /// </summary>
        /// <param name="attribute">The attribute syntax to inspect.</param>
        /// <param name="argumentName">The named argument to read.</param>
        /// <returns>The string literal argument value when available; otherwise, <see langword="null" />.</returns>
        private static string? ReadNamedStringArgument(AttributeSyntax attribute, string argumentName)
        {
            // Named arguments such as Policy = "Orders.Read" are common for authorization metadata.
            AttributeArgumentSyntax? argument = attribute.ArgumentList?.Arguments.FirstOrDefault(candidate => string.Equals(candidate.NameEquals?.Name.Identifier.ValueText, argumentName, StringComparison.Ordinal));
            return argument?.Expression is LiteralExpressionSyntax literal && literal.Token.Value is string value ? value : null;
        }

        /// <summary>
        /// Normalizes an attribute type name by removing namespace and optional Attribute suffix for matching.
        /// </summary>
        /// <param name="attributeName">The source attribute name.</param>
        /// <returns>The normalized attribute name without namespace qualification.</returns>
        private static string NormalizeAttributeName(string attributeName)
        {
            // Matching on the simple name keeps fixtures and real code independent of using-directive choices.
            string simpleName = attributeName.Split('.').Last();
            return simpleName.EndsWith("Attribute", StringComparison.Ordinal) ? simpleName[..^"Attribute".Length] : simpleName;
        }

        /// <summary>
        /// Gets a fully qualified type name for a class declaration when Roslyn can provide one.
        /// </summary>
        /// <param name="semanticDocument">The semantic source document that contains the class.</param>
        /// <param name="classDeclaration">The class declaration syntax.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>A qualified type name or source identifier fallback.</returns>
        private static string GetTypeName(SemanticExtractionRequest semanticDocument, ClassDeclarationSyntax classDeclaration, CancellationToken cancellationToken)
        {
            // Symbol binding improves stable keys, but source identifiers keep extraction useful when references are incomplete.
            ISymbol? symbol = semanticDocument.SemanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken);
            return symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? classDeclaration.Identifier.ValueText;
        }

        /// <summary>
        /// Gets a fully qualified method name for a method declaration when Roslyn can provide one.
        /// </summary>
        /// <param name="semanticDocument">The semantic source document that contains the method.</param>
        /// <param name="methodDeclaration">The method declaration syntax.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>A qualified method name or source identifier fallback.</returns>
        private static string GetMethodName(SemanticExtractionRequest semanticDocument, MethodDeclarationSyntax methodDeclaration, CancellationToken cancellationToken)
        {
            // Method identity is used as handler metadata and endpoint stable-key material.
            ISymbol? symbol = semanticDocument.SemanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken);
            return symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? methodDeclaration.Identifier.ValueText;
        }

        /// <summary>
        /// Trims the conventional Controller suffix from a controller type name.
        /// </summary>
        /// <param name="typeName">The controller type name.</param>
        /// <returns>The normalized controller name.</returns>
        private static string TrimControllerSuffix(string typeName)
        {
            // Controller metadata and route-token replacement use the conventional name without the suffix.
            return typeName.EndsWith("Controller", StringComparison.Ordinal) ? typeName[..^"Controller".Length] : typeName;
        }

        /// <summary>
        /// Merges two string lists while preserving first-seen order.
        /// </summary>
        /// <param name="first">The first list of values.</param>
        /// <param name="second">The second list of values.</param>
        /// <returns>A distinct merged list.</returns>
        private static IReadOnlyList<string> MergeDistinct(IReadOnlyList<string> first, IReadOnlyList<string> second)
        {
            // Filter metadata can come from both controller and action scopes and should not duplicate inherited values.
            List<string> result = [];
            foreach (string value in first.Concat(second))
            {
                AddDistinct(result, value);
            }

            return result;
        }

        /// <summary>
        /// Adds a value to a list when it is non-blank and not already present.
        /// </summary>
        /// <param name="values">The list receiving the value.</param>
        /// <param name="value">The candidate value.</param>
        private static void AddDistinct(List<string> values, string? value)
        {
            // Source-order metadata should stay compact and deterministic.
            if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.Ordinal))
            {
                values.Add(value.Trim());
            }
        }

        /// <summary>
        /// Adds an optional metadata value when the value is present.
        /// </summary>
        /// <param name="values">The metadata dictionary receiving the value.</param>
        /// <param name="key">The metadata key.</param>
        /// <param name="value">The optional metadata value.</param>
        private static void AddOptional(Dictionary<string, object?> values, string key, object? value)
        {
            // Optional values are omitted rather than serialized as null so absence does not imply a false fact.
            if (value is string text)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    values[key] = text;
                }

                return;
            }

            if (value is not null)
            {
                values[key] = value;
            }
        }

        /// <summary>
        /// Selects the knowledge kind for a fact based on unknown-state presence.
        /// </summary>
        /// <param name="unknownReason">The optional unknown reason.</param>
        /// <returns>The graph knowledge kind for the fact.</returns>
        private static KnowledgeKind KnowledgeKindForUnknown(string? unknownReason)
        {
            // Unknown facts stay queryable while clearly marked as incomplete.
            return unknownReason is null ? KnowledgeKind.Fact : KnowledgeKind.Unknown;
        }

        /// <summary>
        /// Selects confidence for a fact based on unknown-state presence.
        /// </summary>
        /// <param name="unknownReason">The optional unknown reason.</param>
        /// <returns>The graph confidence value for the fact.</returns>
        private static Confidence ConfidenceForUnknown(string? unknownReason)
        {
            // Known direct facts use high confidence, while explicit unknowns remain useful but less certain.
            return unknownReason is null ? Confidence.High : Confidence.Medium;
        }

        /// <summary>
        /// Creates a controller stable key scoped by project and qualified controller type.
        /// </summary>
        /// <param name="projectStableKey">The stable key of the declaring project.</param>
        /// <param name="qualifiedControllerName">The controller type identity.</param>
        /// <returns>A deterministic controller stable key.</returns>
        private static StableKey CreateControllerStableKey(StableKey projectStableKey, string qualifiedControllerName)
        {
            // Runtime extraction scopes runtime controller identity by project plus fully qualified controller type name.
            return new StableKey($"controller://{CreateSha256Hash(projectStableKey.Value + "|" + qualifiedControllerName)}");
        }

        /// <summary>
        /// Creates an endpoint stable key scoped by project, route, HTTP method, and handler identity.
        /// </summary>
        /// <param name="projectStableKey">The stable key of the project that declares the endpoint.</param>
        /// <param name="routeTemplate">The normalized route template or explicit unknown placeholder.</param>
        /// <param name="httpMethod">The endpoint HTTP method.</param>
        /// <param name="handlerSymbol">The handler identity derived from symbol binding or source location.</param>
        /// <returns>A deterministic endpoint stable key.</returns>
        private static StableKey CreateEndpointStableKey(StableKey projectStableKey, string routeTemplate, string httpMethod, string handlerSymbol)
        {
            // The key shape follows runtime extraction by including project identity, route, method, and handler identity without machine paths or enumeration order.
            string normalizedRoute = routeTemplate.Trim().TrimStart('/');
            string keyMaterial = $"{projectStableKey.Value}|{httpMethod.ToUpperInvariant()}|/{normalizedRoute}|{handlerSymbol}";
            return new StableKey($"endpoint://{CreateSha256Hash(keyMaterial)}");
        }

        /// <summary>
        /// Creates a deterministic evidence stable key from project identity and source line span.
        /// </summary>
        /// <param name="projectStableKey">The stable key of the project that owns the source document.</param>
        /// <param name="repositoryRelativeDocumentPath">The repository-relative source document path.</param>
        /// <param name="startLine">The one-based starting line of the source fact.</param>
        /// <param name="endLine">The one-based ending line of the source fact.</param>
        /// <param name="symbolName">The source symbol or invocation name.</param>
        /// <returns>A deterministic evidence stable key.</returns>
        private static StableKey CreateEvidenceStableKey(StableKey projectStableKey, string repositoryRelativeDocumentPath, int startLine, int endLine, string symbolName)
        {
            // Evidence identity uses line spans because multiple runtime facts can appear in the same file.
            string keyMaterial = $"{projectStableKey.Value}|{repositoryRelativeDocumentPath}|{startLine}|{endLine}|{symbolName}";
            return new StableKey($"evidence://aspnet-core-runtime/{CreateSha256Hash(keyMaterial)}");
        }

        /// <summary>
        /// Creates a bounded source preview for evidence records.
        /// </summary>
        /// <param name="node">The syntax node that supports the runtime fact.</param>
        /// <param name="sourceText">The source text containing the node.</param>
        /// <returns>A normalized preview suitable for evidence display.</returns>
        private static string CreateSnippetPreview(SyntaxNode node, SourceText sourceText)
        {
            // Preview content is normalized and bounded so graph evidence stays useful without embedding large source regions.
            string snippet = sourceText.ToString(node.Span).ReplaceLineEndings(" ").Trim();
            return snippet.Length <= 240 ? snippet : snippet[..240];
        }

        /// <summary>
        /// Creates a deterministic SHA-256 hash string for stable-key and snippet-hash inputs.
        /// </summary>
        /// <param name="value">The canonical value to hash.</param>
        /// <returns>A lowercase hexadecimal SHA-256 hash with a <c>sha256:</c> prefix.</returns>
        private static string CreateSha256Hash(string value)
        {
            // SHA-256 provides deterministic compact identity for key material and source previews.
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Converts an absolute repository-contained path to a repository-relative path with forward slashes.
        /// </summary>
        /// <param name="repositoryRootDirectory">The absolute repository root directory.</param>
        /// <param name="absolutePath">The absolute source path.</param>
        /// <returns>A repository-relative path using forward slashes.</returns>
        private static string GetRepositoryRelativePath(string repositoryRootDirectory, string absolutePath)
        {
            // Repository-relative evidence paths keep endpoint facts deterministic across developer machines.
            return NormalizeRepositoryRelativePath(Path.GetRelativePath(repositoryRootDirectory, absolutePath));
        }

        /// <summary>
        /// Normalizes a repository-relative path for stable key and evidence usage.
        /// </summary>
        /// <param name="path">The path to normalize.</param>
        /// <returns>The normalized repository-relative path.</returns>
        private static string NormalizeRepositoryRelativePath(string path)
        {
            // Domain parsing performs validation while this helper ensures callers use slash separators consistently.
            return RepositoryRelativePath.Parse(path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/')).Value;
        }

        /// <summary>
        /// Carries normalized project and evidence context for one source document.
        /// </summary>
        /// <param name="ProjectStableKey">The stable key of the project that owns the source document.</param>
        /// <param name="ProjectDisplayName">The display name for fallback project nodes.</param>
        /// <param name="RepositoryRelativeDocumentPath">The repository-relative source document path.</param>
        private sealed record DocumentContext(StableKey ProjectStableKey, string ProjectDisplayName, string RepositoryRelativeDocumentPath);

        /// <summary>
        /// Carries the route-template extraction result for an endpoint or group invocation.
        /// </summary>
        /// <param name="MetadataRouteTemplate">The known route template or explicit unknown placeholder used in graph metadata.</param>
        /// <param name="IsKnown">Whether the route template was read deterministically from source.</param>
        /// <param name="UnknownReason">The explicit unknown reason when the route could not be read deterministically.</param>
        private sealed record RouteTemplateResult(string MetadataRouteTemplate, bool IsKnown, string? UnknownReason);

        /// <summary>
        /// Carries endpoint group prefix information discovered from a local variable declaration.
        /// </summary>
        /// <param name="RouteTemplate">The literal prefix or explicit unknown placeholder.</param>
        /// <param name="IsKnown">Whether the prefix is known deterministically.</param>
        /// <param name="UnknownReason">The explicit unknown reason when the prefix could not be read deterministically.</param>
        private sealed record GroupDescriptor(string RouteTemplate, bool IsKnown, string? UnknownReason);

        /// <summary>
        /// Carries authorization metadata read from controller or action attributes.
        /// </summary>
        /// <param name="AuthorizationPolicy">The optional authorization policy value.</param>
        /// <param name="AllowsAnonymous">Whether anonymous access is explicitly allowed.</param>
        private sealed record AuthorizationDescriptor(string? AuthorizationPolicy, bool? AllowsAnonymous)
        {
            /// <summary>
            /// Gets a value indicating whether any authorization metadata was read from the source scope.
            /// </summary>
            public bool HasAuthorizationData => AuthorizationPolicy is not null || AllowsAnonymous is not null;
        }

        /// <summary>
        /// Carries controller graph and inherited action metadata.
        /// </summary>
        /// <param name="ProjectStableKey">The stable key of the project that declares the controller.</param>
        /// <param name="ControllerStableKey">The stable key of the controller node.</param>
        /// <param name="DisplayName">The controller display name.</param>
        /// <param name="ControllerName">The normalized controller name without suffix.</param>
        /// <param name="QualifiedName">The qualified controller type name.</param>
        /// <param name="RouteTemplate">The optional controller route template.</param>
        /// <param name="Authorization">The controller-level authorization metadata.</param>
        /// <param name="FilterTypes">The controller-level filter attribute types.</param>
        /// <param name="EvidenceStableKey">The stable key of the source evidence record.</param>
        /// <param name="EvidenceFilePath">The repository-relative evidence file path.</param>
        /// <param name="EvidenceStartLine">The one-based evidence start line.</param>
        /// <param name="EvidenceEndLine">The one-based evidence end line.</param>
        /// <param name="SymbolName">The source symbol name.</param>
        /// <param name="ContainingSymbol">The containing symbol name for evidence.</param>
        /// <param name="SnippetPreview">The bounded source snippet preview.</param>
        /// <param name="SnippetHash">The deterministic snippet hash.</param>
        /// <param name="Metadata">The controller graph metadata.</param>
        private sealed record ControllerDescriptor(StableKey ProjectStableKey, StableKey ControllerStableKey, string DisplayName, string ControllerName, string QualifiedName, string? RouteTemplate, AuthorizationDescriptor Authorization, IReadOnlyList<string> FilterTypes, StableKey EvidenceStableKey, string EvidenceFilePath, int EvidenceStartLine, int EvidenceEndLine, string SymbolName, string ContainingSymbol, string SnippetPreview, string SnippetHash, GraphMetadata Metadata);

        /// <summary>
        /// Carries normalized endpoint, evidence, and stable-key values shared by graph projection methods.
        /// </summary>
        /// <param name="ProjectStableKey">The stable key of the project that declares the endpoint.</param>
        /// <param name="ParentStableKey">The stable key of the declaring parent node.</param>
        /// <param name="EndpointStableKey">The stable key of the endpoint node.</param>
        /// <param name="DisplayName">The endpoint display name.</param>
        /// <param name="SearchName">The endpoint search name.</param>
        /// <param name="RouteTemplate">The endpoint route template or explicit unknown placeholder.</param>
        /// <param name="HttpMethod">The endpoint HTTP method.</param>
        /// <param name="HandlerSymbol">The endpoint handler identity.</param>
        /// <param name="UnknownReason">The unknown reason when route data could not be resolved.</param>
        /// <param name="EvidenceStableKey">The stable key of the source evidence record.</param>
        /// <param name="EvidenceFilePath">The repository-relative evidence file path.</param>
        /// <param name="EvidenceStartLine">The one-based evidence start line.</param>
        /// <param name="EvidenceEndLine">The one-based evidence end line.</param>
        /// <param name="SymbolName">The source symbol or invocation name.</param>
        /// <param name="ContainingSymbol">The containing symbol name for evidence.</param>
        /// <param name="SnippetPreview">The bounded source snippet preview.</param>
        /// <param name="SnippetHash">The deterministic snippet hash.</param>
        /// <param name="Metadata">The endpoint graph metadata.</param>
        /// <param name="KnowledgeKind">The endpoint knowledge kind.</param>
        /// <param name="Confidence">The endpoint confidence.</param>
        private sealed record EndpointDescriptor(StableKey ProjectStableKey, StableKey? ParentStableKey, StableKey EndpointStableKey, string DisplayName, string SearchName, string RouteTemplate, string HttpMethod, string HandlerSymbol, string? UnknownReason, StableKey EvidenceStableKey, string EvidenceFilePath, int EvidenceStartLine, int EvidenceEndLine, string SymbolName, string ContainingSymbol, string SnippetPreview, string SnippetHash, GraphMetadata Metadata, KnowledgeKind KnowledgeKind, Confidence Confidence);

        /// <summary>
        /// Carries an action route and HTTP method read from action attributes.
        /// </summary>
        /// <param name="HttpMethod">The HTTP method associated with the action.</param>
        /// <param name="RouteTemplate">The action route template segment.</param>
        private sealed record ActionRouteDescriptor(string HttpMethod, string RouteTemplate);

        /// <summary>
        /// Carries project-level startup pipeline metadata and evidence invocation nodes.
        /// </summary>
        /// <param name="MvcSetupCalls">The MVC setup calls detected in source order.</param>
        /// <param name="ControllerMappingCalls">The controller mapping calls detected in source order.</param>
        /// <param name="MiddlewareOrder">The middleware calls detected in source order.</param>
        /// <param name="MiddlewareTypes">The custom middleware type names detected from generic UseMiddleware calls.</param>
        /// <param name="OpenApiEnabled">Whether OpenAPI or Swagger setup calls were detected.</param>
        /// <param name="EvidenceInvocations">The invocation nodes that support the pipeline metadata.</param>
        private sealed record PipelineDescriptor(IReadOnlyList<string> MvcSetupCalls, IReadOnlyList<string> ControllerMappingCalls, IReadOnlyList<string> MiddlewareOrder, IReadOnlyList<string> MiddlewareTypes, bool OpenApiEnabled, IReadOnlyList<InvocationExpressionSyntax> EvidenceInvocations)
        {
            /// <summary>
            /// Gets a value indicating whether this descriptor contains any project-level runtime facts.
            /// </summary>
            public bool HasFacts => MvcSetupCalls.Count > 0 || ControllerMappingCalls.Count > 0 || MiddlewareOrder.Count > 0 || MiddlewareTypes.Count > 0 || OpenApiEnabled;
        }
    }
}
