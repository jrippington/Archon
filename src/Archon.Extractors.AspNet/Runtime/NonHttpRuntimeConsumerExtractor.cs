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

namespace Archon.Extractors.AspNet.Runtime
{
    /// <summary>
    /// Extracts scheduled-job, queue/topic consumer, message-handler, service-style host, and custom-loop runtime facts from C# semantic documents.
    /// </summary>
    /// <remarks>
    /// The extractor is deliberately static and evidence-first. It recognizes common source shapes for schedulers, messaging APIs, Windows-service-style hosting, Topshelf-style hosting, and long-running loops without connecting to brokers, schedulers, Windows services, or executing the analyzed application.
    /// </remarks>
    public sealed class NonHttpRuntimeConsumerExtractor
    {
        /// <summary>
        /// Stores the unknown reason used when a queue or topic name is not a compile-time string literal.
        /// </summary>
        private const string UnknownQueueOrTopicNameReason = "Queue or topic name was not a compile-time string literal.";

        /// <summary>
        /// Stores the unknown reason used when a scheduled-job expression cannot be resolved to a handler method.
        /// </summary>
        private const string UnknownScheduledJobTargetReason = "Scheduled job target method could not be resolved from source evidence.";

        /// <summary>
        /// Extracts non-HTTP runtime consumer graph facts from supplied C# semantic documents.
        /// </summary>
        /// <param name="request">The snapshot and semantic document request that scopes extraction.</param>
        /// <param name="cancellationToken">A token that stops syntax traversal before or during source inspection.</param>
        /// <returns>An extraction result containing non-HTTP runtime nodes, relationships, evidence, warnings, and diagnostics.</returns>
        public NonHttpRuntimeConsumerExtractionResult Extract(NonHttpRuntimeConsumerExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // The extractor uses only the accepted semantic document list provided by the caller, preserving the API-triggered repository boundary.
            ArgumentNullException.ThrowIfNull(request);
            ArchitectureSnapshotAccumulator accumulator = new();
            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeDocument(request.SnapshotStableKey, semanticDocument, accumulator, cancellationToken);
            }

            return new NonHttpRuntimeConsumerExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Analyzes one C# semantic document for non-HTTP runtime consumer source patterns.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="semanticDocument">The semantic source document being inspected.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="cancellationToken">A token that stops syntax traversal and semantic inspection.</param>
        private static void AnalyzeDocument(StableKey snapshotStableKey, SemanticExtractionRequest semanticDocument, ArchitectureSnapshotAccumulator accumulator, CancellationToken cancellationToken)
        {
            // Work Item 6 currently implements C# source-pattern extraction because the existing WP008 runtime orchestration loads C# semantic inputs for runtime consumers.
            if (semanticDocument.SyntaxTree.Options.Language != LanguageNames.CSharp)
            {
                return;
            }

            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            SourceText sourceText = semanticDocument.SyntaxTree.GetText(cancellationToken);
            DocumentContext context = CreateDocumentContext(semanticDocument);
            Dictionary<string, RuntimeTargetDescriptor> targetsByVariable = new(StringComparer.Ordinal);

            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>().OrderBy(static invocation => invocation.SpanStart))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeInvocation(snapshotStableKey, semanticDocument, invocation, sourceText, context, accumulator, targetsByVariable, cancellationToken);
            }

            foreach (AssignmentExpressionSyntax assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>().OrderBy(static assignment => assignment.SpanStart))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeHandlerAssignment(snapshotStableKey, semanticDocument, assignment, sourceText, context, accumulator, targetsByVariable, cancellationToken);
            }

            foreach (MethodDeclarationSyntax method in root.DescendantNodes().OfType<MethodDeclarationSyntax>().OrderBy(static method => method.SpanStart))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeCustomHostLoop(snapshotStableKey, semanticDocument, method, sourceText, context, accumulator, cancellationToken);
            }
        }

        /// <summary>
        /// Analyzes a single invocation for scheduler, messaging, and service-style host patterns.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="semanticDocument">The semantic source document that contains the invocation.</param>
        /// <param name="invocation">The invocation syntax node to inspect.</param>
        /// <param name="sourceText">The source text used for evidence preview creation.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="targetsByVariable">The map from local processor/receiver variables to queue or topic target descriptors.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        private static void AnalyzeInvocation(StableKey snapshotStableKey, SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, SourceText sourceText, DocumentContext context, ArchitectureSnapshotAccumulator accumulator, Dictionary<string, RuntimeTargetDescriptor> targetsByVariable, CancellationToken cancellationToken)
        {
            // Each recognized invocation contributes at most one primary runtime pattern so facts stay traceable to the exact source call.
            string? methodName = GetInvocationMethodName(invocation);
            if (methodName is null)
            {
                return;
            }

            if (methodName == "AddOrUpdate" && invocation.Expression.ToString().Contains("RecurringJob", StringComparison.Ordinal))
            {
                AccumulateScheduledJob(snapshotStableKey, semanticDocument, invocation, sourceText, context, accumulator, "Hangfire", "HangfireRecurringJob", cancellationToken);
                return;
            }

            if ((methodName == "ScheduleJob" || methodName == "AddJob" || methodName == "AddTrigger") && invocation.Expression.ToString().Contains("Quartz", StringComparison.Ordinal))
            {
                AccumulateScheduledJob(snapshotStableKey, semanticDocument, invocation, sourceText, context, accumulator, "Quartz", "QuartzSchedulerRegistration", cancellationToken);
                return;
            }

            if (methodName is "CreateProcessor" or "CreateReceiver")
            {
                RuntimeTargetDescriptor target = CreateMessagingTargetDescriptor(snapshotStableKey, semanticDocument, invocation, sourceText, context, methodName, cancellationToken);
                AccumulateMessagingTarget(snapshotStableKey, target, accumulator);
                AddTargetVariable(invocation, target, targetsByVariable);
                return;
            }

            if (methodName is "ReceiveEndpoint" or "Subscribe" or "SubscriptionEndpoint")
            {
                RuntimeTargetDescriptor target = CreateMessagingTargetDescriptor(snapshotStableKey, semanticDocument, invocation, sourceText, context, methodName, cancellationToken);
                AccumulateMessagingTarget(snapshotStableKey, target, accumulator);
                return;
            }

            if (methodName is "UseWindowsService" or "RunAsService")
            {
                AccumulateServiceHostProjectFact(snapshotStableKey, semanticDocument, invocation, sourceText, context, accumulator, "WindowsServiceHost", "WindowsServiceSetup", cancellationToken);
                return;
            }

            if (methodName == "UseSystemd")
            {
                AccumulateServiceHostProjectFact(snapshotStableKey, semanticDocument, invocation, sourceText, context, accumulator, "SystemdServiceHost", "SystemdServiceSetup", cancellationToken);
                return;
            }

            if (methodName == "Run" && invocation.Expression.ToString().Contains("HostFactory", StringComparison.Ordinal))
            {
                AccumulateServiceHostProjectFact(snapshotStableKey, semanticDocument, invocation, sourceText, context, accumulator, "TopshelfHost", "TopshelfHostFactory", cancellationToken);
            }
        }

        /// <summary>
        /// Analyzes event subscription assignments that connect message processors to handler methods.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="semanticDocument">The semantic source document that contains the assignment.</param>
        /// <param name="assignment">The assignment syntax node to inspect.</param>
        /// <param name="sourceText">The source text used for evidence preview creation.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="targetsByVariable">The map from local processor/receiver variables to queue or topic target descriptors.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        private static void AnalyzeHandlerAssignment(StableKey snapshotStableKey, SemanticExtractionRequest semanticDocument, AssignmentExpressionSyntax assignment, SourceText sourceText, DocumentContext context, ArchitectureSnapshotAccumulator accumulator, IReadOnlyDictionary<string, RuntimeTargetDescriptor> targetsByVariable, CancellationToken cancellationToken)
        {
            // Message processor event assignments identify handler methods after the queue/topic target was discovered from the processor creation call.
            if (assignment.Left is not MemberAccessExpressionSyntax memberAccess || memberAccess.Expression is not IdentifierNameSyntax receiver || !targetsByVariable.TryGetValue(receiver.Identifier.ValueText, out RuntimeTargetDescriptor? target))
            {
                return;
            }

            if (!memberAccess.Name.Identifier.ValueText.Contains("Message", StringComparison.OrdinalIgnoreCase) && !memberAccess.Name.Identifier.ValueText.Contains("Handle", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string? handlerName = assignment.Right switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax handlerMember => handlerMember.Name.Identifier.ValueText,
                _ => null
            };
            if (string.IsNullOrWhiteSpace(handlerName))
            {
                return;
            }

            MethodDeclarationSyntax? handlerMethod = assignment.SyntaxTree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault(method => method.Identifier.ValueText == handlerName);
            HandlerDescriptor handler = CreateHandlerDescriptor(semanticDocument, handlerMethod, assignment, sourceText, context, handlerName, "MessageHandler", "MessageHandlerEventSubscription", cancellationToken);
            AccumulateHandler(snapshotStableKey, handler, target, accumulator);
        }

        /// <summary>
        /// Analyzes a method body for conservative long-running custom host-loop patterns.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="semanticDocument">The semantic source document that contains the method.</param>
        /// <param name="method">The method declaration syntax to inspect.</param>
        /// <param name="sourceText">The source text used for evidence preview creation.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        private static void AnalyzeCustomHostLoop(StableKey snapshotStableKey, SemanticExtractionRequest semanticDocument, MethodDeclarationSyntax method, SourceText sourceText, DocumentContext context, ArchitectureSnapshotAccumulator accumulator, CancellationToken cancellationToken)
        {
            // Long-running loops are heuristic facts, so detection requires both an explicit loop and a delay/cancellation signal commonly found in service runners.
            bool containsLoop = method.DescendantNodes().OfType<WhileStatementSyntax>().Any() || method.DescendantNodes().OfType<ForStatementSyntax>().Any(static forStatement => forStatement.Condition is null || forStatement.Condition.ToString().Contains("true", StringComparison.OrdinalIgnoreCase));
            if (!containsLoop)
            {
                return;
            }

            string methodText = method.ToString();
            bool hasRuntimeLoopSignal = methodText.Contains("Task.Delay", StringComparison.Ordinal) || methodText.Contains("IsCancellationRequested", StringComparison.Ordinal) || methodText.Contains("Thread.Sleep", StringComparison.Ordinal);
            if (!hasRuntimeLoopSignal)
            {
                return;
            }

            HandlerDescriptor handler = CreateHandlerDescriptor(semanticDocument, method, method, sourceText, context, method.Identifier.ValueText, "CustomHostLoop", "CustomHostLoopHeuristic", cancellationToken);
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, handler.EvidenceStableKey, handler.EvidenceFilePath, handler.EvidenceStartLine, handler.EvidenceEndLine, handler.SymbolName, handler.ContainingSymbol, handler.SnippetPreview, handler.SnippetHash, KnowledgeKind.Fact, Confidence.Medium, UnknownState.Known, GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "CustomHostLoopHeuristic",
                ["runtimeKind"] = "CustomHostLoop"
            }));
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = "Long-running loop detected from source loop and delay or cancellation evidence; classification is conservative.",
                ["detectionMode"] = "CustomHostLoopHeuristic",
                ["runtimeKind"] = "CustomHostLoop"
            });
            ArchitectureNode node = CreateNode(snapshotStableKey, handler.StableKey, NodeKind.Method, handler.DisplayName, handler.QualifiedName, "C#", context.ProjectStableKey, KnowledgeKind.Fact, Confidence.Medium, UnknownState.Known, evidence.StableKey, metadata);
            accumulator.AddEvidence(evidence).AddNode(node);
        }

        /// <summary>
        /// Accumulates a scheduled-job runtime fact from a scheduler registration invocation.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="semanticDocument">The semantic source document that contains the invocation.</param>
        /// <param name="invocation">The scheduler registration invocation.</param>
        /// <param name="sourceText">The source text used for evidence preview creation.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="schedulerTechnology">The scheduler technology label recorded in metadata.</param>
        /// <param name="detectionMode">The detection mode recorded in metadata.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        private static void AccumulateScheduledJob(StableKey snapshotStableKey, SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, SourceText sourceText, DocumentContext context, ArchitectureSnapshotAccumulator accumulator, string schedulerTechnology, string detectionMode, CancellationToken cancellationToken)
        {
            // Scheduled jobs are represented as Method nodes because the current graph contract has no dedicated ScheduledJob node kind.
            string? scheduleExpression = TryGetSchedulerScheduleArgument(invocation);
            string? handlerMethodName = TryGetLambdaInvocationMethodName(invocation) ?? TryGetGenericTypeArgumentName(invocation);
            bool known = !string.IsNullOrWhiteSpace(handlerMethodName);
            string displayName = known ? handlerMethodName! : "<unknown scheduled job>";
            FileLinePositionSpan lineSpan = semanticDocument.SyntaxTree.GetLineSpan(invocation.Span, cancellationToken);
            string snippetPreview = CreateSnippetPreview(invocation, sourceText);
            StableKey evidenceStableKey = CreateEvidenceStableKey(context.ProjectStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, displayName, detectionMode);
            KnowledgeKind knowledgeKind = known ? KnowledgeKind.Fact : KnowledgeKind.Unknown;
            Confidence confidence = known ? Confidence.High : Confidence.Medium;
            UnknownState unknownState = known ? UnknownState.Known : UnknownState.Unknown(UnknownScheduledJobTargetReason);
            GraphMetadata evidenceMetadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = detectionMode,
                ["runtimeKind"] = "ScheduledJob",
                ["schedulerTechnology"] = schedulerTechnology
            });
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, evidenceStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, GetInvocationMethodName(invocation), "ScheduledJobRegistration", snippetPreview, CreateSha256Hash(snippetPreview), knowledgeKind, confidence, unknownState, evidenceMetadata);
            Dictionary<string, object?> metadataValues = new(StringComparer.Ordinal)
            {
                ["confidenceReason"] = known ? "Scheduled job registration detected from known scheduler API source evidence." : UnknownScheduledJobTargetReason,
                ["detectionMode"] = detectionMode,
                ["runtimeKind"] = "ScheduledJob",
                ["schedulerTechnology"] = schedulerTechnology
            };
            AddOptional(metadataValues, "scheduleExpression", scheduleExpression);
            GraphMetadata metadata = GraphMetadata.From(metadataValues);
            StableKey methodStableKey = StableKeyGenerator.ForMethod($"{context.ProjectStableKey.Value}:{displayName}:scheduled-job:{schedulerTechnology}");
            ArchitectureNode node = CreateNode(snapshotStableKey, methodStableKey, NodeKind.Method, displayName, displayName, "C#", context.ProjectStableKey, knowledgeKind, confidence, unknownState, evidence.StableKey, metadata);
            accumulator.AddEvidence(evidence).AddNode(node);
            if (!known)
            {
                accumulator.AddWarning(UnknownScheduledJobTargetReason + " " + context.RepositoryRelativeDocumentPath);
            }
        }

        /// <summary>
        /// Creates a messaging target descriptor for queue or topic consumer source evidence.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="semanticDocument">The semantic source document that contains the invocation.</param>
        /// <param name="invocation">The messaging invocation syntax node.</param>
        /// <param name="sourceText">The source text used for evidence preview creation.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="methodName">The messaging method name that triggered detection.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>A runtime target descriptor for the queue or topic.</returns>
        private static RuntimeTargetDescriptor CreateMessagingTargetDescriptor(StableKey snapshotStableKey, SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, SourceText sourceText, DocumentContext context, string methodName, CancellationToken cancellationToken)
        {
            // Literal names become high-confidence facts; computed names become explicit unknowns with source evidence.
            bool topic = methodName.Contains("Subscribe", StringComparison.OrdinalIgnoreCase) || methodName.Contains("Subscription", StringComparison.OrdinalIgnoreCase) || invocation.ArgumentList.Arguments.Count > 1;
            string? name = TryGetStringArgument(invocation, 0);
            string? subscriptionName = topic ? TryGetStringArgument(invocation, 1) : null;
            bool known = !string.IsNullOrWhiteSpace(name);
            string displayName = known ? name! : (topic ? "<unknown topic>" : "<unknown queue>");
            FileLinePositionSpan lineSpan = semanticDocument.SyntaxTree.GetLineSpan(invocation.Span, cancellationToken);
            string snippetPreview = CreateSnippetPreview(invocation, sourceText);
            string transportKind = DetermineTransportKind(invocation, methodName);
            string detectionMode = transportKind == "AzureServiceBus" ? "AzureServiceBusProcessor" : methodName + "Consumer";
            StableKey stableKey = known
                ? (topic ? StableKeyGenerator.ForTopic($"{transportKind}:{displayName}") : StableKeyGenerator.ForQueue($"{transportKind}:{displayName}"))
                : new StableKey($"{(topic ? "topic" : "queue")}://unknown/{CreateSha256Hash(context.ProjectStableKey.Value + context.RepositoryRelativeDocumentPath + lineSpan.StartLinePosition.Line + methodName)}");
            StableKey evidenceStableKey = CreateEvidenceStableKey(context.ProjectStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, displayName, detectionMode);
            return new RuntimeTargetDescriptor(stableKey, topic ? NodeKind.Topic : NodeKind.Queue, displayName, displayName, context.ProjectStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, methodName, "MessageConsumerRegistration", snippetPreview, CreateSha256Hash(snippetPreview), evidenceStableKey, transportKind, detectionMode, subscriptionName, known ? null : UnknownQueueOrTopicNameReason);
        }

        /// <summary>
        /// Accumulates a queue or topic node and its evidence.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="target">The queue or topic descriptor to project.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        private static void AccumulateMessagingTarget(StableKey snapshotStableKey, RuntimeTargetDescriptor target, ArchitectureSnapshotAccumulator accumulator)
        {
            // Queue and topic facts preserve unknown state when the source proves a consumer but not a deterministic name.
            KnowledgeKind knowledgeKind = target.UnknownReason is null ? KnowledgeKind.Fact : KnowledgeKind.Unknown;
            Confidence confidence = target.UnknownReason is null ? Confidence.High : Confidence.Medium;
            UnknownState unknownState = target.UnknownReason is null ? UnknownState.Known : UnknownState.Unknown(target.UnknownReason);
            GraphMetadata metadata = CreateTargetMetadata(target);
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, target.EvidenceStableKey, target.EvidenceFilePath, target.EvidenceStartLine, target.EvidenceEndLine, target.SymbolName, target.ContainingSymbol, target.SnippetPreview, target.SnippetHash, knowledgeKind, confidence, unknownState, metadata);
            ArchitectureNode node = CreateNode(snapshotStableKey, target.StableKey, target.NodeKind, target.DisplayName, target.QualifiedName, null, target.ProjectStableKey, knowledgeKind, confidence, unknownState, evidence.StableKey, metadata);
            accumulator.AddEvidence(evidence).AddNode(node);
            if (target.UnknownReason is not null)
            {
                accumulator.AddWarning(target.UnknownReason + " " + target.EvidenceFilePath);
            }
        }

        /// <summary>
        /// Accumulates a message handler node and its direct HANDLES relationship to a queue or topic target.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="handler">The message handler descriptor to project.</param>
        /// <param name="target">The queue or topic descriptor handled by the handler.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        private static void AccumulateHandler(StableKey snapshotStableKey, HandlerDescriptor handler, RuntimeTargetDescriptor target, ArchitectureSnapshotAccumulator accumulator)
        {
            // Handler facts use Method nodes and HANDLES edges because the current graph model already defines both concepts.
            GraphMetadata handlerMetadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = "Message handler correlated with queue or topic processor event subscription source evidence.",
                ["detectionMode"] = handler.DetectionMode,
                ["runtimeKind"] = handler.RuntimeKind,
                ["transportKind"] = target.TransportKind
            });
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, handler.EvidenceStableKey, handler.EvidenceFilePath, handler.EvidenceStartLine, handler.EvidenceEndLine, handler.SymbolName, handler.ContainingSymbol, handler.SnippetPreview, handler.SnippetHash, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, handlerMetadata);
            ArchitectureNode handlerNode = CreateNode(snapshotStableKey, handler.StableKey, NodeKind.Method, handler.DisplayName, handler.QualifiedName, "C#", handler.ProjectStableKey, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, evidence.StableKey, handlerMetadata);
            ArchitectureEdge edge = CreateEdge(snapshotStableKey, EdgeKind.Handles, handler.StableKey, target.StableKey, evidence.StableKey, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = handler.DetectionMode,
                ["relationshipRole"] = "MessageConsumerHandlesTarget",
                ["runtimeKind"] = handler.RuntimeKind,
                ["transportKind"] = target.TransportKind
            }));
            accumulator.AddEvidence(evidence).AddNode(handlerNode).AddEdge(edge);
        }

        /// <summary>
        /// Accumulates project-level service host metadata and evidence.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="semanticDocument">The semantic source document that contains the invocation.</param>
        /// <param name="invocation">The service host setup invocation.</param>
        /// <param name="sourceText">The source text used for evidence preview creation.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="runtimeKind">The service host runtime kind metadata value.</param>
        /// <param name="detectionMode">The detection mode metadata value.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        private static void AccumulateServiceHostProjectFact(StableKey snapshotStableKey, SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, SourceText sourceText, DocumentContext context, ArchitectureSnapshotAccumulator accumulator, string runtimeKind, string detectionMode, CancellationToken cancellationToken)
        {
            // Service-style host setup is represented on the project node because the graph model has no separate service host node kind.
            FileLinePositionSpan lineSpan = semanticDocument.SyntaxTree.GetLineSpan(invocation.Span, cancellationToken);
            string snippetPreview = CreateSnippetPreview(invocation, sourceText);
            StableKey evidenceStableKey = CreateEvidenceStableKey(context.ProjectStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, GetInvocationMethodName(invocation) ?? runtimeKind, detectionMode);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = "Service-style host setup detected from source invocation evidence.",
                ["detectionMode"] = detectionMode,
                ["runtimeKind"] = runtimeKind
            });
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, evidenceStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, GetInvocationMethodName(invocation), "ServiceHostSetup", snippetPreview, CreateSha256Hash(snippetPreview), KnowledgeKind.Fact, Confidence.High, UnknownState.Known, metadata);
            ArchitectureNode projectNode = CreateNode(snapshotStableKey, context.ProjectStableKey, NodeKind.Project, context.ProjectDisplayName, context.ProjectDisplayName, "C#", context.ProjectStableKey, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, evidence.StableKey, metadata);
            accumulator.AddEvidence(evidence).AddNode(projectNode);
        }

        /// <summary>
        /// Creates a handler descriptor from a method declaration when available or from fallback evidence otherwise.
        /// </summary>
        /// <param name="semanticDocument">The semantic source document that contains the handler evidence.</param>
        /// <param name="method">The optional handler method declaration.</param>
        /// <param name="fallbackNode">The fallback syntax node used when the handler declaration is unavailable.</param>
        /// <param name="sourceText">The source text used for evidence preview creation.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="handlerName">The handler display name.</param>
        /// <param name="runtimeKind">The runtime kind metadata value.</param>
        /// <param name="detectionMode">The detection mode metadata value.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>A handler descriptor with stable graph and evidence identity.</returns>
        private static HandlerDescriptor CreateHandlerDescriptor(SemanticExtractionRequest semanticDocument, MethodDeclarationSyntax? method, SyntaxNode fallbackNode, SourceText sourceText, DocumentContext context, string handlerName, string runtimeKind, string detectionMode, CancellationToken cancellationToken)
        {
            // Symbol binding is used when a handler declaration is available; otherwise the fallback source location keeps the fact evidence-backed.
            SyntaxNode evidenceNode = method ?? fallbackNode;
            FileLinePositionSpan lineSpan = semanticDocument.SyntaxTree.GetLineSpan(evidenceNode.Span, cancellationToken);
            string qualifiedName = method is null ? $"{context.ProjectDisplayName}.{handlerName}" : semanticDocument.SemanticModel.GetDeclaredSymbol(method, cancellationToken)?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? handlerName;
            string snippetPreview = CreateSnippetPreview(evidenceNode, sourceText);
            StableKey stableKey = StableKeyGenerator.ForMethod(qualifiedName);
            StableKey evidenceStableKey = CreateEvidenceStableKey(context.ProjectStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, handlerName, detectionMode);
            return new HandlerDescriptor(stableKey, handlerName, qualifiedName, context.ProjectStableKey, context.RepositoryRelativeDocumentPath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, handlerName, qualifiedName, snippetPreview, CreateSha256Hash(snippetPreview), evidenceStableKey, runtimeKind, detectionMode);
        }

        /// <summary>
        /// Creates deterministic metadata for a queue or topic target node.
        /// </summary>
        /// <param name="target">The queue or topic target descriptor.</param>
        /// <returns>Graph metadata describing the target and detection source.</returns>
        private static GraphMetadata CreateTargetMetadata(RuntimeTargetDescriptor target)
        {
            // The current graph contract stores runtime subtypes and transport detail as metadata rather than specialized node kinds.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["confidenceReason"] = target.UnknownReason is null ? "Message consumer target detected from known messaging API source evidence." : UnknownQueueOrTopicNameReason,
                ["detectionMode"] = target.DetectionMode,
                ["runtimeKind"] = target.NodeKind == NodeKind.Topic ? "TopicConsumer" : "QueueConsumer",
                ["transportKind"] = target.TransportKind
            };
            if (target.NodeKind == NodeKind.Topic)
            {
                values["topicName"] = target.UnknownReason is null ? target.DisplayName : null;
                AddOptional(values, "subscriptionName", target.SubscriptionName);
            }
            else
            {
                values["queueName"] = target.UnknownReason is null ? target.DisplayName : null;
            }

            if (target.UnknownReason is not null)
            {
                values["correlationStatus"] = target.UnknownReason;
            }

            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Creates an evidence record from normalized evidence values.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives evidence.</param>
        /// <param name="evidenceStableKey">The stable key of the evidence record.</param>
        /// <param name="filePath">The repository-relative evidence file path.</param>
        /// <param name="startLine">The one-based starting line for evidence.</param>
        /// <param name="endLine">The one-based ending line for evidence.</param>
        /// <param name="symbolName">The source symbol name associated with the evidence.</param>
        /// <param name="containingSymbol">The containing symbol name associated with the evidence.</param>
        /// <param name="snippetPreview">The bounded source snippet preview.</param>
        /// <param name="snippetHash">The deterministic snippet hash.</param>
        /// <param name="knowledgeKind">The knowledge classification for the evidence.</param>
        /// <param name="confidence">The confidence for the evidence.</param>
        /// <param name="unknownState">The unknown state for the evidence.</param>
        /// <param name="metadata">The deterministic evidence metadata.</param>
        /// <returns>A source-code evidence record.</returns>
        private static EvidenceRecord CreateEvidence(StableKey snapshotStableKey, StableKey evidenceStableKey, string filePath, int startLine, int endLine, string? symbolName, string? containingSymbol, string snippetPreview, string snippetHash, KnowledgeKind knowledgeKind, Confidence confidence, UnknownState unknownState, GraphMetadata metadata)
        {
            // Evidence fingerprints use the same normalized fields that contributors use to locate and compare source facts.
            return new EvidenceRecord(snapshotStableKey, evidenceStableKey, EvidenceKind.SourceCode, RepositoryRelativePath.Parse(filePath), startLine, endLine, symbolName, containingSymbol, snippetHash, snippetPreview, knowledgeKind, confidence, unknownState, metadata, FingerprintGenerator.ForEvidence(EvidenceKind.SourceCode, filePath, startLine, endLine, symbolName, knowledgeKind, metadata));
        }

        /// <summary>
        /// Creates an architecture node from normalized runtime fact values.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the node.</param>
        /// <param name="stableKey">The stable key of the node.</param>
        /// <param name="nodeKind">The graph node kind.</param>
        /// <param name="displayName">The display name for the node.</param>
        /// <param name="qualifiedName">The qualified name or logical identity for the node.</param>
        /// <param name="language">The optional language associated with the node.</param>
        /// <param name="projectStableKey">The stable key of the declaring project.</param>
        /// <param name="knowledgeKind">The knowledge classification for the node.</param>
        /// <param name="confidence">The confidence for the node.</param>
        /// <param name="unknownState">The unknown state for the node.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key.</param>
        /// <param name="metadata">The deterministic node metadata.</param>
        /// <returns>An architecture node.</returns>
        private static ArchitectureNode CreateNode(StableKey snapshotStableKey, StableKey stableKey, NodeKind nodeKind, string displayName, string? qualifiedName, string? language, StableKey projectStableKey, KnowledgeKind knowledgeKind, Confidence confidence, UnknownState unknownState, StableKey evidenceStableKey, GraphMetadata metadata)
        {
            // Search names are upper-invariant to match the existing graph-node pattern used by WP008 extractors.
            return new ArchitectureNode(snapshotStableKey, stableKey, nodeKind, displayName, qualifiedName, (qualifiedName ?? displayName).ToUpperInvariant(), language, projectStableKey, projectStableKey, knowledgeKind, null, null, confidence, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForNode(nodeKind, displayName, qualifiedName, (qualifiedName ?? displayName).ToUpperInvariant(), knowledgeKind, metadata));
        }

        /// <summary>
        /// Creates an architecture edge from normalized relationship values.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the edge.</param>
        /// <param name="edgeKind">The graph edge kind.</param>
        /// <param name="sourceStableKey">The source node stable key.</param>
        /// <param name="targetStableKey">The target node stable key.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key.</param>
        /// <param name="knowledgeKind">The knowledge classification for the edge.</param>
        /// <param name="confidence">The confidence for the edge.</param>
        /// <param name="unknownState">The unknown state for the edge.</param>
        /// <param name="metadata">The deterministic edge metadata.</param>
        /// <returns>An architecture edge.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, EdgeKind edgeKind, StableKey sourceStableKey, StableKey targetStableKey, StableKey evidenceStableKey, KnowledgeKind knowledgeKind, Confidence confidence, UnknownState unknownState, GraphMetadata metadata)
        {
            // Edge identity includes direction and edge kind so repeated source observations collapse deterministically in the accumulator.
            return new ArchitectureEdge(snapshotStableKey, new StableKey($"edge://{edgeKind.Value}:{sourceStableKey.Value}->{targetStableKey.Value}"), edgeKind, sourceStableKey, targetStableKey, isDirect: true, knowledgeKind, confidence, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForEdge(edgeKind, sourceStableKey, targetStableKey, isDirect: true, knowledgeKind, metadata));
        }

        /// <summary>
        /// Adds a queue or topic target to the variable map when the invocation result is assigned to a local variable.
        /// </summary>
        /// <param name="invocation">The invocation whose assignment context is inspected.</param>
        /// <param name="target">The target descriptor associated with the invocation result.</param>
        /// <param name="targetsByVariable">The mutable variable-to-target map.</param>
        private static void AddTargetVariable(InvocationExpressionSyntax invocation, RuntimeTargetDescriptor target, Dictionary<string, RuntimeTargetDescriptor> targetsByVariable)
        {
            // Event subscriptions usually occur on a processor variable created by the preceding CreateProcessor/CreateReceiver call.
            if (invocation.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable })
            {
                targetsByVariable[variable.Identifier.ValueText] = target;
            }
        }

        /// <summary>
        /// Gets the simple method name from an invocation expression.
        /// </summary>
        /// <param name="invocation">The invocation syntax node.</param>
        /// <returns>The invocation method name when it can be read syntactically; otherwise, <see langword="null" />.</returns>
        private static string? GetInvocationMethodName(InvocationExpressionSyntax invocation)
        {
            // Syntactic names are sufficient for Work Item 6 pattern classification and keep extraction useful for incomplete compilations.
            return invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name switch
                {
                    GenericNameSyntax genericName => genericName.Identifier.ValueText,
                    IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
                    _ => memberAccess.Name.ToString()
                },
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                GenericNameSyntax genericName => genericName.Identifier.ValueText,
                _ => null
            };
        }

        /// <summary>
        /// Attempts to read a string literal argument from an invocation.
        /// </summary>
        /// <param name="invocation">The invocation syntax node.</param>
        /// <param name="index">The zero-based argument index.</param>
        /// <returns>The string literal value when present; otherwise, <see langword="null" />.</returns>
        private static string? TryGetStringArgument(InvocationExpressionSyntax invocation, int index)
        {
            // Only compile-time string literals are treated as deterministic runtime target names.
            if (invocation.ArgumentList.Arguments.Count <= index)
            {
                return null;
            }

            return invocation.ArgumentList.Arguments[index].Expression is LiteralExpressionSyntax literal && literal.Token.Value is string value ? value : null;
        }

        /// <summary>
        /// Attempts to read the last string literal argument from an invocation.
        /// </summary>
        /// <param name="invocation">The invocation syntax node.</param>
        /// <returns>The last string literal value when present; otherwise, <see langword="null" />.</returns>
        private static string? TryGetLastStringArgument(InvocationExpressionSyntax invocation)
        {
            // Scheduler APIs commonly place cron expressions at the end of the argument list.
            return invocation.ArgumentList.Arguments.Select(static argument => argument.Expression).OfType<LiteralExpressionSyntax>().Select(static literal => literal.Token.Value as string).LastOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        }

        /// <summary>
        /// Attempts to read the scheduler expression argument without confusing a job identifier with a schedule value.
        /// </summary>
        /// <param name="invocation">The scheduler invocation syntax node.</param>
        /// <returns>The deterministic schedule expression when the scheduler argument is a literal; otherwise, <see langword="null" />.</returns>
        private static string? TryGetSchedulerScheduleArgument(InvocationExpressionSyntax invocation)
        {
            // Hangfire AddOrUpdate commonly stores the schedule as the last argument; this helper intentionally ignores earlier literal job identifiers.
            if (invocation.ArgumentList.Arguments.LastOrDefault()?.Expression is LiteralExpressionSyntax literal && literal.Token.Value is string value && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return null;
        }

        /// <summary>
        /// Attempts to read the invoked method name inside a scheduler lambda expression.
        /// </summary>
        /// <param name="invocation">The scheduler invocation syntax node.</param>
        /// <returns>The scheduled method name when one is visible; otherwise, <see langword="null" />.</returns>
        private static string? TryGetLambdaInvocationMethodName(InvocationExpressionSyntax invocation)
        {
            // Hangfire registrations commonly use lambdas such as job => job.SyncCustomers().
            foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
            {
                InvocationExpressionSyntax? lambdaInvocation = argument.Expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().LastOrDefault(inner => inner != invocation);
                if (lambdaInvocation is not null)
                {
                    return GetInvocationMethodName(lambdaInvocation);
                }
            }

            return null;
        }

        /// <summary>
        /// Attempts to read the final generic type argument name from an invocation.
        /// </summary>
        /// <param name="invocation">The invocation syntax node.</param>
        /// <returns>The generic type argument name when present; otherwise, <see langword="null" />.</returns>
        private static string? TryGetGenericTypeArgumentName(InvocationExpressionSyntax invocation)
        {
            // Some scheduler registrations identify the job type rather than the specific method in the invocation arguments.
            GenericNameSyntax? genericName = invocation.Expression.DescendantNodesAndSelf().OfType<GenericNameSyntax>().LastOrDefault();
            return genericName?.TypeArgumentList.Arguments.LastOrDefault()?.ToString().Split('.').Last();
        }

        /// <summary>
        /// Determines the messaging transport kind from invocation source shape.
        /// </summary>
        /// <param name="invocation">The invocation syntax node.</param>
        /// <param name="methodName">The method name that triggered target detection.</param>
        /// <returns>A stable transport-kind metadata value.</returns>
        private static string DetermineTransportKind(InvocationExpressionSyntax invocation, string methodName)
        {
            // Transport names are conservative hints derived from common API names and source text.
            string expression = invocation.Expression.ToString();
            if (methodName is "CreateProcessor" or "CreateReceiver" || expression.Contains("ServiceBus", StringComparison.OrdinalIgnoreCase))
            {
                return "AzureServiceBus";
            }

            if (expression.Contains("Rabbit", StringComparison.OrdinalIgnoreCase))
            {
                return "RabbitMQ";
            }

            if (expression.Contains("MassTransit", StringComparison.OrdinalIgnoreCase) || methodName == "ReceiveEndpoint")
            {
                return "MassTransit";
            }

            if (expression.Contains("NServiceBus", StringComparison.OrdinalIgnoreCase))
            {
                return "NServiceBus";
            }

            return "UnknownMessagingTransport";
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
        /// Creates a deterministic evidence stable key from project identity and source line span.
        /// </summary>
        /// <param name="projectStableKey">The stable key of the project that owns the source document.</param>
        /// <param name="repositoryRelativeDocumentPath">The repository-relative source document path.</param>
        /// <param name="startLine">The one-based starting line of the source fact.</param>
        /// <param name="endLine">The one-based ending line of the source fact.</param>
        /// <param name="symbolName">The source symbol name.</param>
        /// <param name="role">The role of the evidence within non-HTTP runtime extraction.</param>
        /// <returns>A deterministic evidence stable key.</returns>
        private static StableKey CreateEvidenceStableKey(StableKey projectStableKey, string repositoryRelativeDocumentPath, int startLine, int endLine, string symbolName, string role)
        {
            // Evidence identity uses source span, symbol, and role because one invocation can support multiple runtime facts.
            string keyMaterial = $"{projectStableKey.Value}|{repositoryRelativeDocumentPath}|{startLine}|{endLine}|{symbolName}|{role}";
            return new StableKey($"evidence://non-http-runtime/{CreateSha256Hash(keyMaterial)}");
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
            // Repository-relative evidence paths keep runtime facts deterministic across developer machines.
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
        /// Carries queue or topic graph, transport, evidence, and unknown-state values between detection and graph projection.
        /// </summary>
        /// <param name="StableKey">The stable key of the queue or topic node.</param>
        /// <param name="NodeKind">The queue or topic node kind.</param>
        /// <param name="DisplayName">The display name for the queue or topic.</param>
        /// <param name="QualifiedName">The qualified logical name for search and comparison.</param>
        /// <param name="ProjectStableKey">The stable key of the declaring project.</param>
        /// <param name="EvidenceFilePath">The repository-relative evidence file path.</param>
        /// <param name="EvidenceStartLine">The one-based evidence start line.</param>
        /// <param name="EvidenceEndLine">The one-based evidence end line.</param>
        /// <param name="SymbolName">The source symbol name for evidence.</param>
        /// <param name="ContainingSymbol">The containing symbol name for evidence.</param>
        /// <param name="SnippetPreview">The bounded source snippet preview.</param>
        /// <param name="SnippetHash">The deterministic snippet hash.</param>
        /// <param name="EvidenceStableKey">The stable key of the evidence record.</param>
        /// <param name="TransportKind">The messaging transport hint.</param>
        /// <param name="DetectionMode">The detection mode metadata value.</param>
        /// <param name="SubscriptionName">The optional topic subscription name.</param>
        /// <param name="UnknownReason">The optional unknown reason for computed or unresolved names.</param>
        private sealed record RuntimeTargetDescriptor(StableKey StableKey, NodeKind NodeKind, string DisplayName, string QualifiedName, StableKey ProjectStableKey, string EvidenceFilePath, int EvidenceStartLine, int EvidenceEndLine, string? SymbolName, string? ContainingSymbol, string SnippetPreview, string SnippetHash, StableKey EvidenceStableKey, string TransportKind, string DetectionMode, string? SubscriptionName, string? UnknownReason);

        /// <summary>
        /// Carries message-handler or custom-loop graph and evidence values between detection and graph projection.
        /// </summary>
        /// <param name="StableKey">The stable key of the method node.</param>
        /// <param name="DisplayName">The display name for the handler or loop method.</param>
        /// <param name="QualifiedName">The qualified method name.</param>
        /// <param name="ProjectStableKey">The stable key of the declaring project.</param>
        /// <param name="EvidenceFilePath">The repository-relative evidence file path.</param>
        /// <param name="EvidenceStartLine">The one-based evidence start line.</param>
        /// <param name="EvidenceEndLine">The one-based evidence end line.</param>
        /// <param name="SymbolName">The source symbol name for evidence.</param>
        /// <param name="ContainingSymbol">The containing symbol name for evidence.</param>
        /// <param name="SnippetPreview">The bounded source snippet preview.</param>
        /// <param name="SnippetHash">The deterministic snippet hash.</param>
        /// <param name="EvidenceStableKey">The stable key of the evidence record.</param>
        /// <param name="RuntimeKind">The runtime kind metadata value.</param>
        /// <param name="DetectionMode">The detection mode metadata value.</param>
        private sealed record HandlerDescriptor(StableKey StableKey, string DisplayName, string QualifiedName, StableKey ProjectStableKey, string EvidenceFilePath, int EvidenceStartLine, int EvidenceEndLine, string SymbolName, string ContainingSymbol, string SnippetPreview, string SnippetHash, StableKey EvidenceStableKey, string RuntimeKind, string DetectionMode);
    }
}
