using System.Security.Cryptography;
using System.Text;
using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Contracts;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Archon.Extractors.AspNet.Runtime
{
    /// <summary>
    /// Extracts graph-ready worker, hosted-service, and background-service runtime facts from C# semantic documents.
    /// </summary>
    /// <remarks>
    /// The extractor performs static source and snapshot analysis only. It recognizes hosted-service implementations, background-service implementations, execution methods, generic-host setup, and dependency-injection registration correlation without constructing or starting target services.
    /// </remarks>
    public sealed class WorkerHostedServiceRuntimeExtractor
    {
        /// <summary>
        /// Stores the runtime-kind metadata value for hosted-service facts.
        /// </summary>
        private const string HostedServiceRuntimeKind = "HostedService";

        /// <summary>
        /// Stores the runtime-kind metadata value for background-service facts.
        /// </summary>
        private const string BackgroundServiceRuntimeKind = "BackgroundService";

        /// <summary>
        /// Stores the runtime-kind metadata value for project-level worker host setup facts.
        /// </summary>
        private const string WorkerProjectRuntimeKind = "WorkerServiceHost";

        /// <summary>
        /// Stores the detection mode used when a class implements IHostedService.
        /// </summary>
        private const string HostedServiceImplementationDetectionMode = "HostedServiceImplementation";

        /// <summary>
        /// Stores the detection mode used when a class derives from BackgroundService.
        /// </summary>
        private const string BackgroundServiceImplementationDetectionMode = "BackgroundServiceInheritance";

        /// <summary>
        /// Stores the detection mode used when only AddHostedService registration evidence proves a hosted-service role.
        /// </summary>
        private const string HostedServiceRegistrationDetectionMode = "AddHostedServiceRegistration";

        /// <summary>
        /// Stores the detection mode used for generic-host or worker startup observations.
        /// </summary>
        private const string WorkerHostSetupDetectionMode = "WorkerHostSetup";

        /// <summary>
        /// Stores the fully qualified IHostedService abstraction name.
        /// </summary>
        private const string HostedServiceInterfaceName = "Microsoft.Extensions.Hosting.IHostedService";

        /// <summary>
        /// Stores the fully qualified BackgroundService base type name.
        /// </summary>
        private const string BackgroundServiceTypeName = "Microsoft.Extensions.Hosting.BackgroundService";

        /// <summary>
        /// Stores the confidence explanation for source-proven hosted-service implementations.
        /// </summary>
        private const string HostedServiceConfidenceReason = "Hosted service detected from source type semantics in the submitted target repository context.";

        /// <summary>
        /// Stores the confidence explanation for DI-correlated hosted-service facts.
        /// </summary>
        private const string CorrelatedHostedServiceConfidenceReason = "Hosted service correlated with AddHostedService or hosted-service dependency-injection registration evidence.";

        /// <summary>
        /// Stores the warning message used when a hosted-service source fact has no matching dependency-injection registration evidence.
        /// </summary>
        private const string MissingRegistrationUnknownReason = "Hosted service implementation was detected from source, but no matching AddHostedService registration fact was found in prior dependency-injection output.";

        /// <summary>
        /// Stores the warning message used when multiple hosted-service registration edges match one implementation type.
        /// </summary>
        private const string ConflictingRegistrationUnknownReason = "Multiple dependency-injection hosted-service registration facts matched the same implementation type.";

        /// <summary>
        /// Stores generic host and worker startup calls that indicate worker service host setup.
        /// </summary>
        private static readonly HashSet<string> s_workerHostSetupCalls = new(StringComparer.Ordinal)
        {
            "CreateDefaultBuilder",
            "CreateApplicationBuilder",
            "CreateBuilder",
            "ConfigureServices",
            "UseWindowsService",
            "UseSystemd",
            "Run",
            "RunAsync"
        };

        /// <summary>
        /// Stores execution method names that are meaningful for hosted-service runtime facts.
        /// </summary>
        private static readonly HashSet<string> s_executionMethodNames = new(StringComparer.Ordinal)
        {
            "StartAsync",
            "StopAsync",
            "ExecuteAsync"
        };

        /// <summary>
        /// Extracts worker and hosted-service runtime graph facts from supplied C# semantic documents.
        /// </summary>
        /// <param name="request">The snapshot, semantic document, and optional prior snapshot request that scopes extraction.</param>
        /// <param name="cancellationToken">A token that stops syntax traversal before or during source inspection.</param>
        /// <returns>An extraction result containing hosted-service nodes, relationships, source evidence, warnings, and diagnostics.</returns>
        public WorkerHostedServiceExtractionResult Extract(WorkerHostedServiceExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // The prior snapshot is read before source traversal so DI registration correlation stays deterministic and side-effect free.
            ArgumentNullException.ThrowIfNull(request);
            ArchitectureSnapshotAccumulator accumulator = new();
            IReadOnlyDictionary<string, IReadOnlyList<RegistrationCorrelation>> registrationsByImplementation = BuildRegistrationIndex(request.PriorSnapshot);
            HashSet<string> emittedHostedServiceKeys = new(StringComparer.Ordinal);

            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeDocument(request.SnapshotStableKey, semanticDocument, registrationsByImplementation, accumulator, emittedHostedServiceKeys, cancellationToken);
            }

            AccumulateRegistrationOnlyHostedServices(request.SnapshotStableKey, registrationsByImplementation, accumulator, emittedHostedServiceKeys);
            return new WorkerHostedServiceExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Analyzes one C# document for worker host setup and hosted-service implementation facts.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="semanticDocument">The semantic source document being inspected.</param>
        /// <param name="registrationsByImplementation">The DI registration index keyed by implementation type identity.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="emittedHostedServiceKeys">The set of hosted-service stable keys already emitted by source analysis.</param>
        /// <param name="cancellationToken">A token that stops syntax traversal and semantic inspection.</param>
        private static void AnalyzeDocument(StableKey snapshotStableKey, SemanticExtractionRequest semanticDocument, IReadOnlyDictionary<string, IReadOnlyList<RegistrationCorrelation>> registrationsByImplementation, ArchitectureSnapshotAccumulator accumulator, HashSet<string> emittedHostedServiceKeys, CancellationToken cancellationToken)
        {
            // Worker extraction is C#-only in this slice because generic host and hosted-service source patterns are implemented for C# semantic documents.
            if (semanticDocument.SyntaxTree.Options.Language != LanguageNames.CSharp)
            {
                return;
            }

            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            SourceText sourceText = semanticDocument.SyntaxTree.GetText(cancellationToken);
            DocumentContext context = CreateDocumentContext(semanticDocument);
            WorkerHostDescriptor? workerHost = TryCreateWorkerHostDescriptor(semanticDocument, root, sourceText, context, cancellationToken);
            if (workerHost is not null)
            {
                AccumulateWorkerHostProjectFact(snapshotStableKey, accumulator, workerHost);
            }

            foreach (ClassDeclarationSyntax classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                HostedServiceDescriptor? descriptor = TryCreateHostedServiceDescriptor(semanticDocument, classDeclaration, sourceText, context, registrationsByImplementation, cancellationToken);
                if (descriptor is null)
                {
                    continue;
                }

                AccumulateHostedService(snapshotStableKey, accumulator, descriptor);
                emittedHostedServiceKeys.Add(descriptor.HostedServiceStableKey.Value);
            }
        }

        /// <summary>
        /// Builds a hosted-service registration index from prior snapshot dependency-injection facts.
        /// </summary>
        /// <param name="priorSnapshot">The optional snapshot containing previously accumulated dependency-injection facts.</param>
        /// <returns>A lookup from implementation type identity to matching hosted-service registrations.</returns>
        private static IReadOnlyDictionary<string, IReadOnlyList<RegistrationCorrelation>> BuildRegistrationIndex(ExtractedArchitectureSnapshot? priorSnapshot)
        {
            // WP007 stores hosted-service registration detail on REGISTERED_AS_SERVICE edges, so correlation reads edge metadata without depending on extractor internals.
            if (priorSnapshot is null)
            {
                return new Dictionary<string, IReadOnlyList<RegistrationCorrelation>>(StringComparer.Ordinal);
            }

            Dictionary<string, List<RegistrationCorrelation>> registrations = new(StringComparer.Ordinal);
            foreach (ArchitectureEdge edge in priorSnapshot.Edges.Where(static edge => edge.EdgeKind == EdgeKind.RegisteredAsService))
            {
                string metadata = edge.Metadata.ToCanonicalJson();
                if (!metadata.Contains("\"hostedService\":true", StringComparison.Ordinal))
                {
                    continue;
                }

                string? implementationType = TryReadStringMetadata(metadata, "implementationType");
                if (string.IsNullOrWhiteSpace(implementationType) || string.Equals(implementationType, "Unknown", StringComparison.Ordinal))
                {
                    continue;
                }

                string registrationMethod = TryReadStringMetadata(metadata, "registrationMethod") ?? "AddHostedService";
                string? lifetime = TryReadStringMetadata(metadata, "lifetime");
                bool backgroundService = metadata.Contains("\"backgroundService\":true", StringComparison.Ordinal);
                RegistrationCorrelation correlation = new(implementationType, edge.SourceNodeStableKey, edge.StableKey, edge.PrimaryEvidenceStableKey, registrationMethod, lifetime, backgroundService);
                if (!registrations.TryGetValue(implementationType, out List<RegistrationCorrelation>? values))
                {
                    values = [];
                    registrations.Add(implementationType, values);
                }

                values.Add(correlation);
            }

            return registrations.ToDictionary(static pair => pair.Key, static pair => (IReadOnlyList<RegistrationCorrelation>)pair.Value.OrderBy(static correlation => correlation.RegistrationEdgeStableKey.Value, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        }

        /// <summary>
        /// Attempts to create a project-level worker host descriptor from generic host setup calls.
        /// </summary>
        /// <param name="semanticDocument">The semantic source document that contains startup code.</param>
        /// <param name="root">The syntax root of the source document.</param>
        /// <param name="sourceText">The source text used for evidence preview creation.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>A worker host descriptor when startup evidence exists; otherwise, <see langword="null" />.</returns>
        private static WorkerHostDescriptor? TryCreateWorkerHostDescriptor(SemanticExtractionRequest semanticDocument, SyntaxNode root, SourceText sourceText, DocumentContext context, CancellationToken cancellationToken)
        {
            // Generic host setup belongs on the project node because the graph contract has no dedicated host-builder node kind.
            List<string> setupCalls = [];
            List<InvocationExpressionSyntax> evidenceInvocations = [];
            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>().OrderBy(static invocation => invocation.SpanStart))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? methodName = GetInvocationMethodName(invocation);
                if (methodName is null || !s_workerHostSetupCalls.Contains(methodName))
                {
                    continue;
                }

                if (IsGenericHostInvocation(semanticDocument, invocation, methodName, cancellationToken))
                {
                    AddDistinct(setupCalls, methodName);
                    evidenceInvocations.Add(invocation);
                }
            }

            if (setupCalls.Count == 0)
            {
                return null;
            }

            InvocationExpressionSyntax primaryInvocation = evidenceInvocations[0];
            FileLinePositionSpan lineSpan = semanticDocument.SyntaxTree.GetLineSpan(primaryInvocation.Span, cancellationToken);
            string snippetPreview = CreateSnippetPreview(primaryInvocation, sourceText);
            StableKey evidenceStableKey = CreateEvidenceStableKey(context.ProjectStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, setupCalls[0], "worker-host");
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = "Worker service host setup detected from generic host startup calls in submitted source.",
                ["detectionMode"] = WorkerHostSetupDetectionMode,
                ["genericHostSetupCalls"] = setupCalls,
                ["runtimeKind"] = WorkerProjectRuntimeKind
            });
            return new WorkerHostDescriptor(context.ProjectStableKey, context.ProjectDisplayName, context.RepositoryRelativeDocumentPath, setupCalls, evidenceStableKey, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, setupCalls[0], "Program.cs", snippetPreview, CreateSha256Hash(snippetPreview), metadata);
        }

        /// <summary>
        /// Attempts to create a hosted-service descriptor from one class declaration.
        /// </summary>
        /// <param name="semanticDocument">The semantic source document that contains the class.</param>
        /// <param name="classDeclaration">The class declaration syntax to inspect.</param>
        /// <param name="sourceText">The source text used for evidence preview creation.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="registrationsByImplementation">The DI registration index keyed by implementation type identity.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>A hosted-service descriptor when the class proves a hosted-service role; otherwise, <see langword="null" />.</returns>
        private static HostedServiceDescriptor? TryCreateHostedServiceDescriptor(SemanticExtractionRequest semanticDocument, ClassDeclarationSyntax classDeclaration, SourceText sourceText, DocumentContext context, IReadOnlyDictionary<string, IReadOnlyList<RegistrationCorrelation>> registrationsByImplementation, CancellationToken cancellationToken)
        {
            // Source proof comes from Roslyn interfaces/base types first, then conservative source-text fallback for incomplete compilations.
            if (semanticDocument.SemanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken) is not INamedTypeSymbol typeSymbol)
            {
                return null;
            }

            string implementationType = GetQualifiedName(typeSymbol);
            if (implementationType.StartsWith("Microsoft.Extensions.Hosting.", StringComparison.Ordinal))
            {
                return null;
            }

            bool implementsHostedService = ImplementsInterface(typeSymbol, HostedServiceInterfaceName) || HasBaseOrInterfaceText(classDeclaration, "IHostedService");
            bool isBackgroundService = InheritsFrom(typeSymbol, BackgroundServiceTypeName) || HasBaseOrInterfaceText(classDeclaration, "BackgroundService");
            if (!implementsHostedService && !isBackgroundService)
            {
                return null;
            }

            IReadOnlyList<RegistrationCorrelation> registrations = registrationsByImplementation.TryGetValue(implementationType, out IReadOnlyList<RegistrationCorrelation>? matches) ? matches : [];
            string? unknownReason = registrations.Count switch
            {
                0 => MissingRegistrationUnknownReason,
                > 1 => ConflictingRegistrationUnknownReason,
                _ => null
            };
            RegistrationCorrelation? primaryRegistration = registrations.Count == 1 ? registrations[0] : null;
            IReadOnlyList<ExecutionMethodDescriptor> executionMethods = CreateExecutionMethodDescriptors(semanticDocument, classDeclaration, sourceText, context, cancellationToken);
            FileLinePositionSpan lineSpan = semanticDocument.SyntaxTree.GetLineSpan(classDeclaration.Identifier.Span, cancellationToken);
            string snippetPreview = CreateSnippetPreview(classDeclaration, sourceText);
            string runtimeKind = isBackgroundService ? BackgroundServiceRuntimeKind : HostedServiceRuntimeKind;
            string detectionMode = isBackgroundService ? BackgroundServiceImplementationDetectionMode : HostedServiceImplementationDetectionMode;
            if (!implementsHostedService && primaryRegistration is not null)
            {
                detectionMode = HostedServiceRegistrationDetectionMode;
            }

            StableKey hostedServiceStableKey = CreateHostedServiceStableKey(context.ProjectStableKey, implementationType);
            StableKey typeStableKey = StableKeyGenerator.ForType(implementationType);
            StableKey evidenceStableKey = CreateEvidenceStableKey(context.ProjectStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, classDeclaration.Identifier.ValueText, "hosted-service");
            GraphMetadata metadata = CreateHostedServiceMetadata(runtimeKind, detectionMode, unknownReason ?? (primaryRegistration is null ? HostedServiceConfidenceReason : CorrelatedHostedServiceConfidenceReason), implementationType, isBackgroundService, primaryRegistration, executionMethods, unknownReason);
            return new HostedServiceDescriptor(context.ProjectStableKey, context.ProjectDisplayName, hostedServiceStableKey, typeStableKey, classDeclaration.Identifier.ValueText, implementationType, runtimeKind, detectionMode, isBackgroundService, primaryRegistration, registrations, executionMethods, evidenceStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, classDeclaration.Identifier.ValueText, implementationType, snippetPreview, CreateSha256Hash(snippetPreview), unknownReason, metadata);
        }

        /// <summary>
        /// Creates execution method descriptors for StartAsync, StopAsync, and ExecuteAsync methods on a hosted-service type.
        /// </summary>
        /// <param name="semanticDocument">The semantic source document that contains the class.</param>
        /// <param name="classDeclaration">The hosted-service class declaration.</param>
        /// <param name="sourceText">The source text used for evidence preview creation.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>Execution method descriptors in source order.</returns>
        private static IReadOnlyList<ExecutionMethodDescriptor> CreateExecutionMethodDescriptors(SemanticExtractionRequest semanticDocument, ClassDeclarationSyntax classDeclaration, SourceText sourceText, DocumentContext context, CancellationToken cancellationToken)
        {
            // Execution methods are emitted as Method nodes so future consumers can navigate from HostedService to the lifecycle methods that do work.
            List<ExecutionMethodDescriptor> methods = [];
            foreach (MethodDeclarationSyntax method in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string methodName = method.Identifier.ValueText;
                if (!s_executionMethodNames.Contains(methodName))
                {
                    continue;
                }

                string methodIdentity = semanticDocument.SemanticModel.GetDeclaredSymbol(method, cancellationToken)?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? $"{classDeclaration.Identifier.ValueText}.{methodName}";
                FileLinePositionSpan lineSpan = semanticDocument.SyntaxTree.GetLineSpan(method.Identifier.Span, cancellationToken);
                string snippetPreview = CreateSnippetPreview(method, sourceText);
                StableKey methodStableKey = StableKeyGenerator.ForMethod(methodIdentity);
                StableKey evidenceStableKey = CreateEvidenceStableKey(context.ProjectStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, methodName, "execution-method");
                methods.Add(new ExecutionMethodDescriptor(methodStableKey, methodName, methodIdentity, evidenceStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, methodName, methodIdentity, snippetPreview, CreateSha256Hash(snippetPreview)));
            }

            return methods;
        }

        /// <summary>
        /// Accumulates project-level worker host setup metadata and evidence.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="descriptor">The worker host descriptor to project.</param>
        private static void AccumulateWorkerHostProjectFact(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, WorkerHostDescriptor descriptor)
        {
            // Project-level worker metadata records generic host setup without inventing a host-builder node kind.
            EvidenceRecord evidence = new(snapshotStableKey, descriptor.EvidenceStableKey, EvidenceKind.SourceCode, RepositoryRelativePath.Parse(descriptor.EvidenceFilePath), descriptor.EvidenceStartLine, descriptor.EvidenceEndLine, descriptor.SymbolName, descriptor.ContainingSymbol, descriptor.SnippetHash, descriptor.SnippetPreview, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = WorkerHostSetupDetectionMode,
                ["runtimeKind"] = WorkerProjectRuntimeKind
            }), FingerprintGenerator.ForEvidence(EvidenceKind.SourceCode, descriptor.EvidenceFilePath, descriptor.EvidenceStartLine, descriptor.EvidenceEndLine, descriptor.SymbolName, KnowledgeKind.Fact, GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = WorkerHostSetupDetectionMode,
                ["runtimeKind"] = WorkerProjectRuntimeKind
            })));
            ArchitectureNode projectNode = new(snapshotStableKey, descriptor.ProjectStableKey, NodeKind.Project, descriptor.ProjectDisplayName, descriptor.ProjectDisplayName, descriptor.ProjectDisplayName.ToUpperInvariant(), "C#", descriptor.ProjectStableKey, null, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidence.StableKey, descriptor.Metadata, FingerprintGenerator.ForNode(NodeKind.Project, descriptor.ProjectDisplayName, descriptor.ProjectDisplayName, descriptor.ProjectDisplayName.ToUpperInvariant(), KnowledgeKind.Fact, descriptor.Metadata));
            accumulator.AddEvidence(evidence).AddNode(projectNode);
        }

        /// <summary>
        /// Accumulates hosted-service nodes, implementation nodes, execution method nodes, relationships, evidence, and diagnostics.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="descriptor">The hosted-service descriptor to project.</param>
        private static void AccumulateHostedService(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, HostedServiceDescriptor descriptor)
        {
            // Source-proven hosted services are emitted even without DI registration; missing or conflicting registration evidence is explicit unknown state.
            EvidenceRecord evidence = CreateHostedServiceEvidence(snapshotStableKey, descriptor);
            ArchitectureNode hostedServiceNode = CreateHostedServiceNode(snapshotStableKey, descriptor, evidence.StableKey);
            ArchitectureNode implementationNode = CreateImplementationTypeNode(snapshotStableKey, descriptor, evidence.StableKey);
            accumulator.AddEvidence(evidence).AddNode(hostedServiceNode).AddNode(implementationNode).AddEdge(CreateDependsOnEdge(snapshotStableKey, descriptor.HostedServiceStableKey, descriptor.TypeStableKey, evidence.StableKey, descriptor, "HostedServiceImplementation"));

            if (descriptor.UnknownReason is not null)
            {
                accumulator.AddWarning(descriptor.UnknownReason + " " + descriptor.ImplementationTypeName);
            }

            foreach (ExecutionMethodDescriptor method in descriptor.ExecutionMethods)
            {
                EvidenceRecord methodEvidence = CreateExecutionMethodEvidence(snapshotStableKey, method, descriptor);
                ArchitectureNode methodNode = CreateExecutionMethodNode(snapshotStableKey, method, descriptor, methodEvidence.StableKey);
                accumulator.AddEvidence(methodEvidence).AddNode(methodNode).AddEdge(CreateDependsOnEdge(snapshotStableKey, descriptor.HostedServiceStableKey, method.MethodStableKey, methodEvidence.StableKey, descriptor, "HostedServiceExecutionMethod"));
            }
        }

        /// <summary>
        /// Accumulates hosted-service facts proven only by prior dependency-injection registration evidence.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="registrationsByImplementation">The DI registration index keyed by implementation type identity.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="emittedHostedServiceKeys">The set of hosted-service stable keys already emitted by source analysis.</param>
        private static void AccumulateRegistrationOnlyHostedServices(StableKey snapshotStableKey, IReadOnlyDictionary<string, IReadOnlyList<RegistrationCorrelation>> registrationsByImplementation, ArchitectureSnapshotAccumulator accumulator, HashSet<string> emittedHostedServiceKeys)
        {
            // DI-only facts preserve registered hosted services even when source bodies are unavailable to this runtime slice.
            foreach (KeyValuePair<string, IReadOnlyList<RegistrationCorrelation>> pair in registrationsByImplementation.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                RegistrationCorrelation registration = pair.Value[0];
                StableKey projectStableKey = registration.ProjectStableKey;
                StableKey hostedServiceStableKey = CreateHostedServiceStableKey(projectStableKey, pair.Key);
                if (!emittedHostedServiceKeys.Add(hostedServiceStableKey.Value))
                {
                    continue;
                }

                string displayName = pair.Key.Split('.').Last();
                string? unknownReason = pair.Value.Count > 1 ? ConflictingRegistrationUnknownReason : null;
                GraphMetadata metadata = CreateHostedServiceMetadata(registration.BackgroundService ? BackgroundServiceRuntimeKind : HostedServiceRuntimeKind, HostedServiceRegistrationDetectionMode, unknownReason ?? CorrelatedHostedServiceConfidenceReason, pair.Key, registration.BackgroundService, registration, [], unknownReason);
                ArchitectureNode hostedServiceNode = new(snapshotStableKey, hostedServiceStableKey, NodeKind.HostedService, displayName, pair.Key, pair.Key.ToUpperInvariant(), "C#", projectStableKey, projectStableKey, unknownReason is null ? KnowledgeKind.Fact : KnowledgeKind.Unknown, null, null, unknownReason is null ? Confidence.High : Confidence.Medium, unknownReason is null ? UnknownState.Known : UnknownState.Unknown(unknownReason), registration.PrimaryEvidenceStableKey, metadata, FingerprintGenerator.ForNode(NodeKind.HostedService, displayName, pair.Key, pair.Key.ToUpperInvariant(), unknownReason is null ? KnowledgeKind.Fact : KnowledgeKind.Unknown, metadata));
                accumulator.AddNode(hostedServiceNode);
                if (unknownReason is not null)
                {
                    accumulator.AddWarning(unknownReason + " " + pair.Key);
                }
            }
        }

        /// <summary>
        /// Creates source-code evidence for one hosted-service implementation fact.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives evidence.</param>
        /// <param name="descriptor">The hosted-service descriptor that carries source evidence values.</param>
        /// <returns>A source-code evidence record for the hosted-service implementation.</returns>
        private static EvidenceRecord CreateHostedServiceEvidence(StableKey snapshotStableKey, HostedServiceDescriptor descriptor)
        {
            // Evidence records point to the implementation type declaration because that is the source proof of the runtime role.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = descriptor.DetectionMode,
                ["runtimeKind"] = descriptor.RuntimeKind
            });
            return new EvidenceRecord(snapshotStableKey, descriptor.EvidenceStableKey, EvidenceKind.SourceCode, RepositoryRelativePath.Parse(descriptor.EvidenceFilePath), descriptor.EvidenceStartLine, descriptor.EvidenceEndLine, descriptor.SymbolName, descriptor.ContainingSymbol, descriptor.SnippetHash, descriptor.SnippetPreview, descriptor.UnknownReason is null ? KnowledgeKind.Fact : KnowledgeKind.Unknown, descriptor.UnknownReason is null ? Confidence.High : Confidence.Medium, descriptor.UnknownReason is null ? UnknownState.Known : UnknownState.Unknown(descriptor.UnknownReason), metadata, FingerprintGenerator.ForEvidence(EvidenceKind.SourceCode, descriptor.EvidenceFilePath, descriptor.EvidenceStartLine, descriptor.EvidenceEndLine, descriptor.SymbolName, descriptor.UnknownReason is null ? KnowledgeKind.Fact : KnowledgeKind.Unknown, metadata));
        }

        /// <summary>
        /// Creates source-code evidence for one hosted-service execution method.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives evidence.</param>
        /// <param name="method">The execution method descriptor that carries source evidence values.</param>
        /// <param name="descriptor">The hosted-service descriptor that owns the method.</param>
        /// <returns>A source-code evidence record for the execution method.</returns>
        private static EvidenceRecord CreateExecutionMethodEvidence(StableKey snapshotStableKey, ExecutionMethodDescriptor method, HostedServiceDescriptor descriptor)
        {
            // Execution method evidence lets future consumers navigate to StartAsync, StopAsync, or ExecuteAsync bodies directly.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = descriptor.DetectionMode,
                ["executionMethod"] = method.MethodName,
                ["runtimeKind"] = descriptor.RuntimeKind
            });
            return new EvidenceRecord(snapshotStableKey, method.EvidenceStableKey, EvidenceKind.SourceCode, RepositoryRelativePath.Parse(method.EvidenceFilePath), method.EvidenceStartLine, method.EvidenceEndLine, method.SymbolName, method.ContainingSymbol, method.SnippetHash, method.SnippetPreview, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, metadata, FingerprintGenerator.ForEvidence(EvidenceKind.SourceCode, method.EvidenceFilePath, method.EvidenceStartLine, method.EvidenceEndLine, method.SymbolName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a hosted-service architecture node from a descriptor.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the node.</param>
        /// <param name="descriptor">The hosted-service descriptor to project.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the node.</param>
        /// <returns>A hosted-service architecture node with runtime metadata.</returns>
        private static ArchitectureNode CreateHostedServiceNode(StableKey snapshotStableKey, HostedServiceDescriptor descriptor, StableKey evidenceStableKey)
        {
            // HostedService is a first-class WP008 node kind, with runtimeKind distinguishing hosted and background-service subtypes.
            KnowledgeKind knowledgeKind = descriptor.UnknownReason is null ? KnowledgeKind.Fact : KnowledgeKind.Unknown;
            Confidence confidence = descriptor.UnknownReason is null ? Confidence.High : Confidence.Medium;
            UnknownState unknownState = descriptor.UnknownReason is null ? UnknownState.Known : UnknownState.Unknown(descriptor.UnknownReason);
            return new ArchitectureNode(snapshotStableKey, descriptor.HostedServiceStableKey, NodeKind.HostedService, descriptor.DisplayName, descriptor.ImplementationTypeName, descriptor.ImplementationTypeName.ToUpperInvariant(), "C#", descriptor.ProjectStableKey, descriptor.ProjectStableKey, knowledgeKind, null, null, confidence, unknownState, evidenceStableKey, descriptor.Metadata, FingerprintGenerator.ForNode(NodeKind.HostedService, descriptor.DisplayName, descriptor.ImplementationTypeName, descriptor.ImplementationTypeName.ToUpperInvariant(), knowledgeKind, descriptor.Metadata));
        }

        /// <summary>
        /// Creates a type architecture node for the hosted-service implementation type.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the node.</param>
        /// <param name="descriptor">The hosted-service descriptor to project.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the node.</param>
        /// <returns>A type architecture node for the implementation type.</returns>
        private static ArchitectureNode CreateImplementationTypeNode(StableKey snapshotStableKey, HostedServiceDescriptor descriptor, StableKey evidenceStableKey)
        {
            // The implementation type node provides a familiar symbol node that the hosted-service runtime node can depend on.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["implementationType"] = descriptor.ImplementationTypeName,
                ["runtimeKind"] = descriptor.RuntimeKind,
                ["typeRole"] = "HostedServiceImplementation"
            });
            return new ArchitectureNode(snapshotStableKey, descriptor.TypeStableKey, NodeKind.Type, descriptor.DisplayName, descriptor.ImplementationTypeName, descriptor.ImplementationTypeName.ToUpperInvariant(), "C#", descriptor.ProjectStableKey, descriptor.ProjectStableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidenceStableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Type, descriptor.DisplayName, descriptor.ImplementationTypeName, descriptor.ImplementationTypeName.ToUpperInvariant(), KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a method architecture node for a hosted-service execution method.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the node.</param>
        /// <param name="method">The execution method descriptor to project.</param>
        /// <param name="descriptor">The hosted-service descriptor that owns the method.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the node.</param>
        /// <returns>A method architecture node for the execution method.</returns>
        private static ArchitectureNode CreateExecutionMethodNode(StableKey snapshotStableKey, ExecutionMethodDescriptor method, HostedServiceDescriptor descriptor, StableKey evidenceStableKey)
        {
            // Execution methods remain Method nodes while runtime metadata identifies their hosted-service role.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["executionMethod"] = method.MethodName,
                ["implementationType"] = descriptor.ImplementationTypeName,
                ["runtimeKind"] = descriptor.RuntimeKind
            });
            return new ArchitectureNode(snapshotStableKey, method.MethodStableKey, NodeKind.Method, method.MethodName, method.MethodIdentity, method.MethodIdentity.ToUpperInvariant(), "C#", descriptor.ProjectStableKey, descriptor.TypeStableKey, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, evidenceStableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Method, method.MethodName, method.MethodIdentity, method.MethodIdentity.ToUpperInvariant(), KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a direct dependency relationship from a hosted-service runtime node to an implementation or execution method node.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the edge.</param>
        /// <param name="sourceStableKey">The hosted-service node stable key.</param>
        /// <param name="targetStableKey">The dependency target node stable key.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the edge.</param>
        /// <param name="descriptor">The hosted-service descriptor that carries metadata and confidence values.</param>
        /// <param name="relationshipRole">The runtime-specific role of the dependency relationship.</param>
        /// <returns>A direct <c>DEPENDS_ON</c> relationship.</returns>
        private static ArchitectureEdge CreateDependsOnEdge(StableKey snapshotStableKey, StableKey sourceStableKey, StableKey targetStableKey, StableKey evidenceStableKey, HostedServiceDescriptor descriptor, string relationshipRole)
        {
            // DEPENDS_ON captures that the runtime hosted-service fact depends on a source type or lifecycle method fact.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = descriptor.DetectionMode,
                ["relationshipRole"] = relationshipRole,
                ["runtimeKind"] = descriptor.RuntimeKind
            });
            KnowledgeKind knowledgeKind = descriptor.UnknownReason is null ? KnowledgeKind.Fact : KnowledgeKind.Unknown;
            Confidence confidence = descriptor.UnknownReason is null ? Confidence.High : Confidence.Medium;
            UnknownState unknownState = descriptor.UnknownReason is null ? UnknownState.Known : UnknownState.Unknown(descriptor.UnknownReason);
            return new ArchitectureEdge(snapshotStableKey, new StableKey($"edge://DEPENDS_ON:{sourceStableKey.Value}->{targetStableKey.Value}"), EdgeKind.DependsOn, sourceStableKey, targetStableKey, isDirect: true, knowledgeKind, confidence, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForEdge(EdgeKind.DependsOn, sourceStableKey, targetStableKey, isDirect: true, knowledgeKind, metadata));
        }

        /// <summary>
        /// Creates hosted-service metadata using stable lower-camel-case field names.
        /// </summary>
        /// <param name="runtimeKind">The hosted-service runtime subtype metadata value.</param>
        /// <param name="detectionMode">The detection mode used for the hosted-service fact.</param>
        /// <param name="confidenceReason">The confidence explanation for the hosted-service fact.</param>
        /// <param name="implementationType">The fully qualified implementation type name.</param>
        /// <param name="backgroundService">Whether the implementation is a BackgroundService subtype.</param>
        /// <param name="registration">The optional primary dependency-injection registration correlation.</param>
        /// <param name="executionMethods">The execution methods detected on the implementation type.</param>
        /// <param name="unknownReason">The optional unknown reason for missing or conflicting registration evidence.</param>
        /// <returns>Canonical graph metadata for a hosted-service node.</returns>
        private static GraphMetadata CreateHostedServiceMetadata(string runtimeKind, string detectionMode, string confidenceReason, string implementationType, bool backgroundService, RegistrationCorrelation? registration, IReadOnlyList<ExecutionMethodDescriptor> executionMethods, string? unknownReason)
        {
            // Correlation metadata is included when available, while normalized UnknownState remains the source of truth for unknown reasons.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["backgroundService"] = backgroundService,
                ["confidenceReason"] = confidenceReason,
                ["detectionMode"] = detectionMode,
                ["executionMethods"] = executionMethods.Select(static method => method.MethodName).ToArray(),
                ["implementationType"] = implementationType,
                ["runtimeKind"] = runtimeKind
            };
            if (registration is not null)
            {
                values["registrationCorrelated"] = true;
                values["registrationEdgeStableKey"] = registration.RegistrationEdgeStableKey.Value;
                values["registrationMethod"] = registration.RegistrationMethod;
                AddOptional(values, "registrationLifetime", registration.Lifetime);
            }
            else
            {
                values["registrationCorrelated"] = false;
            }

            if (unknownReason is not null)
            {
                values["correlationStatus"] = unknownReason;
            }

            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Determines whether an invocation likely participates in generic host or worker setup.
        /// </summary>
        /// <param name="semanticDocument">The semantic source document that contains the invocation.</param>
        /// <param name="invocation">The invocation syntax node to inspect.</param>
        /// <param name="methodName">The syntactic method name already read from the invocation.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns><see langword="true" /> when the invocation is a supported generic-host setup signal; otherwise, <see langword="false" />.</returns>
        private static bool IsGenericHostInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, string methodName, CancellationToken cancellationToken)
        {
            // Symbol binding is preferred, with source-shape fallback for test fixtures and incomplete target compilations.
            SymbolInfo symbolInfo = semanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken);
            string? containingType = symbolInfo.Symbol?.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
            if (containingType is "Microsoft.Extensions.Hosting.Host" or "Microsoft.Extensions.Hosting.HostApplicationBuilder" or "Microsoft.Extensions.Hosting.IHostBuilder" or "Microsoft.Extensions.Hosting.HostingHostBuilderExtensions" or "Microsoft.Extensions.Hosting.WindowsServiceLifetimeHostBuilderExtensions")
            {
                return true;
            }

            string expressionText = invocation.Expression.ToString();
            return methodName is "UseWindowsService" or "UseSystemd" || expressionText.Contains("Host.", StringComparison.Ordinal) || expressionText.Contains("Host.Create", StringComparison.Ordinal) || expressionText.Contains("builder.Services", StringComparison.Ordinal) || expressionText.Contains("host.", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a type implements a specific interface by fully qualified type name.
        /// </summary>
        /// <param name="typeSymbol">The type symbol to inspect.</param>
        /// <param name="interfaceName">The fully qualified interface name to find.</param>
        /// <returns><see langword="true" /> when the interface is implemented; otherwise, <see langword="false" />.</returns>
        private static bool ImplementsInterface(INamedTypeSymbol typeSymbol, string interfaceName)
        {
            // Interface traversal handles direct and inherited interface implementation without requiring source text guesses.
            return typeSymbol.AllInterfaces.Any(candidate => GetQualifiedName(candidate) == interfaceName);
        }

        /// <summary>
        /// Determines whether a type derives from a specific base type by fully qualified type name.
        /// </summary>
        /// <param name="typeSymbol">The type symbol to inspect.</param>
        /// <param name="baseTypeName">The fully qualified base type name to find.</param>
        /// <returns><see langword="true" /> when the base type is found; otherwise, <see langword="false" />.</returns>
        private static bool InheritsFrom(INamedTypeSymbol typeSymbol, string baseTypeName)
        {
            // Base-type traversal detects BackgroundService even when the implementation does not mention IHostedService directly.
            for (ITypeSymbol? current = typeSymbol.BaseType; current is not null; current = current.BaseType)
            {
                if (GetQualifiedName(current) == baseTypeName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether base-list source text contains a supported hosted-service marker.
        /// </summary>
        /// <param name="classDeclaration">The class declaration whose base list should be inspected.</param>
        /// <param name="typeName">The simple or qualified type text to find.</param>
        /// <returns><see langword="true" /> when the base list contains the marker; otherwise, <see langword="false" />.</returns>
        private static bool HasBaseOrInterfaceText(ClassDeclarationSyntax classDeclaration, string typeName)
        {
            // Text fallback keeps extraction useful for lightweight compilations that omit Microsoft.Extensions.Hosting assemblies.
            return classDeclaration.BaseList?.Types.Any(baseType => baseType.Type.ToString().EndsWith(typeName, StringComparison.Ordinal)) == true;
        }

        /// <summary>
        /// Gets the simple method name from an invocation expression.
        /// </summary>
        /// <param name="invocation">The invocation syntax node.</param>
        /// <returns>The invocation method name when it can be read syntactically; otherwise, <see langword="null" />.</returns>
        private static string? GetInvocationMethodName(InvocationExpressionSyntax invocation)
        {
            // Syntactic names are sufficient for setup-call filtering and do not require full hosting references.
            return invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                GenericNameSyntax genericName => genericName.Identifier.ValueText,
                _ => null
            };
        }

        /// <summary>
        /// Reads a string metadata property from canonical JSON emitted by graph metadata.
        /// </summary>
        /// <param name="metadataJson">The canonical metadata JSON to inspect.</param>
        /// <param name="propertyName">The property name to read.</param>
        /// <returns>The unescaped string property value when present; otherwise, <see langword="null" />.</returns>
        private static string? TryReadStringMetadata(string metadataJson, string propertyName)
        {
            // Metadata values written by GraphMetadata are canonical and simple; JsonDocument avoids brittle substring parsing for values.
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(metadataJson);
            return document.RootElement.TryGetProperty(propertyName, out System.Text.Json.JsonElement value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
        }

        /// <summary>
        /// Converts a Roslyn symbol to a fully qualified display name without the Roslyn global prefix.
        /// </summary>
        /// <param name="symbol">The symbol to display.</param>
        /// <returns>A stable fully qualified display name, or <c>Unknown</c> when no symbol is available.</returns>
        private static string GetQualifiedName(ISymbol? symbol)
        {
            // Fully qualified names are stable-key material and bridge source symbols to graph facts.
            return symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal) ?? "Unknown";
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
        /// Creates a hosted-service stable key scoped by project and implementation type.
        /// </summary>
        /// <param name="projectStableKey">The stable key of the declaring project.</param>
        /// <param name="implementationType">The hosted-service implementation type identity.</param>
        /// <returns>A deterministic hosted-service stable key.</returns>
        private static StableKey CreateHostedServiceStableKey(StableKey projectStableKey, string implementationType)
        {
            // WP008 scopes hosted-service identity by project plus fully qualified implementation type name.
            return new StableKey($"hosted-service://{CreateSha256Hash(projectStableKey.Value + "|" + implementationType)}");
        }

        /// <summary>
        /// Creates a deterministic evidence stable key from project identity and source line span.
        /// </summary>
        /// <param name="projectStableKey">The stable key of the project that owns the source document.</param>
        /// <param name="repositoryRelativeDocumentPath">The repository-relative source document path.</param>
        /// <param name="startLine">The one-based starting line of the source fact.</param>
        /// <param name="endLine">The one-based ending line of the source fact.</param>
        /// <param name="symbolName">The source symbol name.</param>
        /// <param name="role">The role of the evidence within worker extraction.</param>
        /// <returns>A deterministic evidence stable key.</returns>
        private static StableKey CreateEvidenceStableKey(StableKey projectStableKey, string repositoryRelativeDocumentPath, int startLine, int endLine, string symbolName, string role)
        {
            // Evidence identity uses source span, symbol, and role because one class can support multiple runtime facts.
            string keyMaterial = $"{projectStableKey.Value}|{repositoryRelativeDocumentPath}|{startLine}|{endLine}|{symbolName}|{role}";
            return new StableKey($"evidence://worker-hosted-service/{CreateSha256Hash(keyMaterial)}");
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
            // Repository-relative evidence paths keep worker runtime facts deterministic across developer machines.
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
        /// Carries normalized project and evidence context for one source document.
        /// </summary>
        /// <param name="ProjectStableKey">The stable key of the project that owns the source document.</param>
        /// <param name="ProjectDisplayName">The display name for project nodes.</param>
        /// <param name="RepositoryRelativeDocumentPath">The repository-relative source document path.</param>
        private sealed record DocumentContext(StableKey ProjectStableKey, string ProjectDisplayName, string RepositoryRelativeDocumentPath);

        /// <summary>
        /// Carries dependency-injection registration data that can correlate with a hosted-service runtime fact.
        /// </summary>
        /// <param name="ImplementationType">The fully qualified implementation type name from DI metadata.</param>
        /// <param name="ProjectStableKey">The stable key of the project or registration source node that owns the service registration.</param>
        /// <param name="RegistrationEdgeStableKey">The stable key of the matching REGISTERED_AS_SERVICE edge.</param>
        /// <param name="PrimaryEvidenceStableKey">The optional evidence stable key from the matching registration edge.</param>
        /// <param name="RegistrationMethod">The DI registration method name.</param>
        /// <param name="Lifetime">The optional DI lifetime metadata value.</param>
        /// <param name="BackgroundService">Whether the DI registration classified the implementation as a background service.</param>
        private sealed record RegistrationCorrelation(string ImplementationType, StableKey ProjectStableKey, StableKey RegistrationEdgeStableKey, StableKey? PrimaryEvidenceStableKey, string RegistrationMethod, string? Lifetime, bool BackgroundService);

        /// <summary>
        /// Carries project-level generic host setup metadata and evidence.
        /// </summary>
        /// <param name="ProjectStableKey">The stable key of the project that owns the host setup.</param>
        /// <param name="ProjectDisplayName">The display name for the project node.</param>
        /// <param name="EvidenceFilePath">The repository-relative evidence file path.</param>
        /// <param name="SetupCalls">The generic host setup calls detected in source order.</param>
        /// <param name="EvidenceStableKey">The stable key of the source evidence record.</param>
        /// <param name="EvidenceStartLine">The one-based evidence start line.</param>
        /// <param name="EvidenceEndLine">The one-based evidence end line.</param>
        /// <param name="SymbolName">The source symbol name for evidence.</param>
        /// <param name="ContainingSymbol">The containing symbol name for evidence.</param>
        /// <param name="SnippetPreview">The bounded source snippet preview.</param>
        /// <param name="SnippetHash">The deterministic snippet hash.</param>
        /// <param name="Metadata">The project graph metadata.</param>
        private sealed record WorkerHostDescriptor(StableKey ProjectStableKey, string ProjectDisplayName, string EvidenceFilePath, IReadOnlyList<string> SetupCalls, StableKey EvidenceStableKey, int EvidenceStartLine, int EvidenceEndLine, string SymbolName, string ContainingSymbol, string SnippetPreview, string SnippetHash, GraphMetadata Metadata);

        /// <summary>
        /// Carries normalized hosted-service, evidence, correlation, and execution method values shared by graph projection methods.
        /// </summary>
        /// <param name="ProjectStableKey">The stable key of the project that declares the hosted service.</param>
        /// <param name="ProjectDisplayName">The display name for the declaring project.</param>
        /// <param name="HostedServiceStableKey">The stable key of the hosted-service node.</param>
        /// <param name="TypeStableKey">The stable key of the implementation type node.</param>
        /// <param name="DisplayName">The hosted-service display name.</param>
        /// <param name="ImplementationTypeName">The fully qualified implementation type name.</param>
        /// <param name="RuntimeKind">The hosted-service runtime subtype metadata value.</param>
        /// <param name="DetectionMode">The detection mode used for metadata.</param>
        /// <param name="BackgroundService">Whether the implementation is a BackgroundService subtype.</param>
        /// <param name="PrimaryRegistration">The primary DI registration correlation when exactly one exists.</param>
        /// <param name="Registrations">All DI registrations that matched the implementation type.</param>
        /// <param name="ExecutionMethods">The execution methods detected on the implementation type.</param>
        /// <param name="EvidenceStableKey">The stable key of the source evidence record.</param>
        /// <param name="EvidenceFilePath">The repository-relative evidence file path.</param>
        /// <param name="EvidenceStartLine">The one-based evidence start line.</param>
        /// <param name="EvidenceEndLine">The one-based evidence end line.</param>
        /// <param name="SymbolName">The source symbol name for evidence.</param>
        /// <param name="ContainingSymbol">The containing symbol name for evidence.</param>
        /// <param name="SnippetPreview">The bounded source snippet preview.</param>
        /// <param name="SnippetHash">The deterministic snippet hash.</param>
        /// <param name="UnknownReason">The unknown reason for missing or conflicting registration evidence.</param>
        /// <param name="Metadata">The hosted-service graph metadata.</param>
        private sealed record HostedServiceDescriptor(StableKey ProjectStableKey, string ProjectDisplayName, StableKey HostedServiceStableKey, StableKey TypeStableKey, string DisplayName, string ImplementationTypeName, string RuntimeKind, string DetectionMode, bool BackgroundService, RegistrationCorrelation? PrimaryRegistration, IReadOnlyList<RegistrationCorrelation> Registrations, IReadOnlyList<ExecutionMethodDescriptor> ExecutionMethods, StableKey EvidenceStableKey, string EvidenceFilePath, int EvidenceStartLine, int EvidenceEndLine, string SymbolName, string ContainingSymbol, string SnippetPreview, string SnippetHash, string? UnknownReason, GraphMetadata Metadata);

        /// <summary>
        /// Carries normalized execution method graph and evidence values for StartAsync, StopAsync, or ExecuteAsync methods.
        /// </summary>
        /// <param name="MethodStableKey">The stable key of the execution method node.</param>
        /// <param name="MethodName">The simple execution method name.</param>
        /// <param name="MethodIdentity">The fully qualified execution method identity.</param>
        /// <param name="EvidenceStableKey">The stable key of the source evidence record.</param>
        /// <param name="EvidenceFilePath">The repository-relative evidence file path.</param>
        /// <param name="EvidenceStartLine">The one-based evidence start line.</param>
        /// <param name="EvidenceEndLine">The one-based evidence end line.</param>
        /// <param name="SymbolName">The source symbol name for evidence.</param>
        /// <param name="ContainingSymbol">The containing symbol name for evidence.</param>
        /// <param name="SnippetPreview">The bounded source snippet preview.</param>
        /// <param name="SnippetHash">The deterministic snippet hash.</param>
        private sealed record ExecutionMethodDescriptor(StableKey MethodStableKey, string MethodName, string MethodIdentity, StableKey EvidenceStableKey, string EvidenceFilePath, int EvidenceStartLine, int EvidenceEndLine, string SymbolName, string ContainingSymbol, string SnippetPreview, string SnippetHash);
    }
}
