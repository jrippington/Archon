using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Integrations.Foundation;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Archon.Extractors.Integrations.ExternalServices
{
    /// <summary>
    /// Detects static storage, SMTP/email, and payment-provider integration evidence without contacting external systems.
    /// </summary>
    /// <remarks>
    /// The extractor uses conservative Roslyn and local artifact analysis only. It never opens storage accounts, sends SMTP messages, calls payment providers, validates credentials, or evaluates runtime-computed target values.
    /// </remarks>
    public sealed class ExternalServiceIntegrationExtractor
    {
        /// <summary>
        /// Extracts storage, SMTP/email, and payment-provider graph facts from the supplied repository and semantic documents.
        /// </summary>
        /// <param name="request">The snapshot, repository, and semantic-document request that scopes static external-service analysis.</param>
        /// <param name="cancellationToken">A token that signals when artifact traversal, source traversal, and graph projection should stop.</param>
        /// <returns>The external-service extraction result containing a partial graph snapshot.</returns>
        public ExternalServiceIntegrationExtractionResult Extract(ExternalServiceIntegrationExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // Follow the external-integration detector pattern: collect safe observations first, then reuse the foundation projector for graph consistency.
            ArgumentNullException.ThrowIfNull(request);
            List<ExternalIntegrationObservation> observations = [];
            List<string> warnings = [];
            ExternalServiceArtifactIndex artifactIndex = ExternalServiceArtifactIndex.Create(request.RepositoryRootDirectory, warnings, cancellationToken);
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

            return new ExternalServiceIntegrationExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Analyzes one semantic document for supported storage, SMTP/email, and payment source evidence.
        /// </summary>
        /// <param name="semanticDocument">The Roslyn semantic document to inspect.</param>
        /// <param name="artifactIndex">The local artifact index containing safe configuration-key hints.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when traversal should stop.</param>
        private static void AnalyzeSemanticDocument(SemanticExtractionRequest semanticDocument, ExternalServiceArtifactIndex artifactIndex, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // A lightweight document context correlates variables created by factory calls with later operation calls in the same source tree.
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            ExternalServiceContext context = ExternalServiceContext.Create(semanticDocument, artifactIndex, observations, warnings, cancellationToken);
            foreach (ObjectCreationExpressionSyntax creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeObjectCreation(semanticDocument, creation, context, observations, warnings, cancellationToken);
            }

            foreach (AssignmentExpressionSyntax assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeAssignment(semanticDocument, assignment, context, cancellationToken);
            }

            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeInvocation(semanticDocument, invocation, context, observations, warnings, cancellationToken);
            }
        }

        /// <summary>
        /// Detects supported client construction patterns, such as BlobServiceClient, ShareClient, SmtpClient, and Stripe ChargeService.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol and constant resolution.</param>
        /// <param name="creation">The object creation syntax being inspected.</param>
        /// <param name="context">The local source-analysis context for variable correlation.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AnalyzeObjectCreation(SemanticExtractionRequest semanticDocument, ObjectCreationExpressionSyntax creation, ExternalServiceContext context, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Constructors establish provider and configuration context but do not perform live work during analysis.
            string typeName = creation.Type.ToString();
            string? variableName = FindAssignedVariableName(creation);
            if (typeName.EndsWith("BlobServiceClient", StringComparison.Ordinal) && variableName is not null)
            {
                context.BlobServiceVariables[variableName] = new StorageDescriptor("AzureBlobStorage", "Storage", null, null, null, null, "BlobServiceClient", "Client", null, context.ArtifactIndex.FindConfigurationKey("Storage", "Blob", "ConnectionString"));
                return;
            }

            if (typeName.EndsWith("ShareClient", StringComparison.Ordinal))
            {
                string? shareName = TryGetStringConstant(semanticDocument, creation.ArgumentList?.Arguments.Skip(1).FirstOrDefault()?.Expression, cancellationToken);
                string? targetName = shareName is null ? null : $"archive-account/{shareName}";
                StorageDescriptor descriptor = new("AzureFileStorage", "Storage", null, shareName, null, null, "ShareClient", "Client", shareName is null ? "Azure File Storage share name is runtime-computed or unresolved." : null, context.ArtifactIndex.FindConfigurationKey("Storage", "File", "ConnectionString") ?? context.ArtifactIndex.FindConfigurationKey("Storage", "Blob", "ConnectionString"));
                if (variableName is not null)
                {
                    context.ShareVariables[variableName] = descriptor;
                }

                observations.Add(CreateObservation(semanticDocument, creation, targetName, descriptor, EdgeKind.CallsExternalService, operation: "ShareClient", role: "Client", cancellationToken));
                AddUnknownWarning(warnings, descriptor, semanticDocument, creation);
                return;
            }

            if (typeName.EndsWith("SmtpClient", StringComparison.Ordinal))
            {
                string? host = TryGetStringConstant(semanticDocument, creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression, cancellationToken) ?? context.ArtifactIndex.FindConfigurationValue("Email:Smtp:Host");
                EmailDescriptor descriptor = new("SMTP", host, "SmtpClient", "Client", host is null ? "SMTP host is runtime-computed or unresolved." : null, context.ArtifactIndex.FindConfigurationKey("Email", "Smtp", "Host"), AuthenticationHint: null);
                if (variableName is not null)
                {
                    context.SmtpVariables[variableName] = descriptor;
                }

                observations.Add(CreateObservation(semanticDocument, creation, descriptor.TargetName, descriptor, EdgeKind.CallsExternalService, operation: "SmtpClient", role: "Client", cancellationToken));
                AddUnknownWarning(warnings, descriptor, semanticDocument, creation);
                return;
            }

            if (typeName.EndsWith("ChargeService", StringComparison.Ordinal))
            {
                PaymentDescriptor descriptor = new("Stripe", "Stripe", "ChargeService", "Client", null, context.ArtifactIndex.FindConfigurationKey("Payments", "Stripe", "ApiKey"), AuthenticationHint: "ApiKey");
                if (variableName is not null)
                {
                    context.PaymentVariables[variableName] = descriptor;
                }

                observations.Add(CreateObservation(semanticDocument, creation, descriptor.TargetName, descriptor, EdgeKind.CallsExternalService, operation: "ChargeService", role: "Client", cancellationToken));
            }
        }

        /// <summary>
        /// Detects credential assignment hints without recording credential values.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for source span information.</param>
        /// <param name="assignment">The assignment expression being inspected.</param>
        /// <param name="context">The local source-analysis context containing SMTP variables.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AnalyzeAssignment(SemanticExtractionRequest semanticDocument, AssignmentExpressionSyntax assignment, ExternalServiceContext context, CancellationToken cancellationToken)
        {
            // Only the presence of credentials is useful for graph metadata; usernames and passwords are intentionally discarded.
            _ = semanticDocument;
            _ = cancellationToken;
            if (assignment.Left is MemberAccessExpressionSyntax memberAccess && memberAccess.Name.Identifier.ValueText == "Credentials" && TryGetIdentifierName(memberAccess.Expression) is string variableName && context.SmtpVariables.TryGetValue(variableName, out EmailDescriptor descriptor))
            {
                context.SmtpVariables[variableName] = descriptor with { AuthenticationHint = "NetworkCredential" };
            }
        }

        /// <summary>
        /// Dispatches one invocation to storage, email, payment SDK, or payment-wrapper detectors.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that owns the invocation.</param>
        /// <param name="invocation">The invocation expression being inspected.</param>
        /// <param name="context">The local source-analysis context for variables and artifact hints.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AnalyzeInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, ExternalServiceContext context, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Provider-specific checks are deliberately narrow to avoid interpreting ordinary application methods as integrations.
            string invocationName = GetInvocationName(invocation);
            if (TryAnalyzeAzureBlobInvocation(semanticDocument, invocation, invocationName, context, observations, warnings, cancellationToken))
            {
                return;
            }

            if (TryAnalyzeAzureFileInvocation(semanticDocument, invocation, invocationName, context, observations, warnings, cancellationToken))
            {
                return;
            }

            if (TryAnalyzeEmailInvocation(semanticDocument, invocation, invocationName, context, observations, warnings, cancellationToken))
            {
                return;
            }

            if (TryAnalyzePaymentInvocation(semanticDocument, invocation, invocationName, context, observations, warnings, cancellationToken))
            {
                return;
            }

            TryAnalyzeGenericStorageInvocation(semanticDocument, invocation, invocationName, observations, cancellationToken);
        }

        /// <summary>
        /// Attempts to analyze an invocation as Azure Blob Storage client, container, blob, or operation evidence.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="invocation">The invocation being inspected.</param>
        /// <param name="invocationName">The simple invocation method name.</param>
        /// <param name="context">The local source-analysis context containing blob variables.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as blob storage evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzeAzureBlobInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, string invocationName, ExternalServiceContext context, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Container and blob factory calls are correlated by variable so later operations can inherit deterministic target hints.
            if (invocationName == "GetBlobContainerClient" && TryGetIdentifierName(GetInvocationReceiver(invocation)) is string serviceVariable && context.BlobServiceVariables.TryGetValue(serviceVariable, out StorageDescriptor serviceDescriptor))
            {
                string? containerName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                StorageDescriptor containerDescriptor = serviceDescriptor with { ContainerName = containerName, ClientType = "BlobContainerClient", Role = "Container", UnknownReason = containerName is null ? "Azure Blob Storage container name is runtime-computed or unresolved." : null };
                if (FindAssignedVariableName(invocation) is string containerVariable)
                {
                    context.BlobContainerVariables[containerVariable] = containerDescriptor;
                }

                observations.Add(CreateObservation(semanticDocument, invocation, CreateStorageTargetName(containerDescriptor), containerDescriptor, EdgeKind.CallsExternalService, operation: invocationName, role: "Container", cancellationToken));
                AddUnknownWarning(warnings, containerDescriptor, semanticDocument, invocation);
                return true;
            }

            if (invocationName == "GetBlobClient" && TryResolveBlobContainerDescriptor(GetInvocationReceiver(invocation), context, out StorageDescriptor resolvedContainerDescriptor))
            {
                string? blobName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                StorageDescriptor resolvedBlobDescriptor = resolvedContainerDescriptor with { BlobOrFilePath = blobName, ClientType = "BlobClient", Role = "Blob", UnknownReason = resolvedContainerDescriptor.UnknownReason ?? (blobName is null ? "Azure Blob Storage blob path is runtime-computed or unresolved." : null) };
                if (FindAssignedVariableName(invocation) is string blobVariable)
                {
                    context.BlobVariables[blobVariable] = resolvedBlobDescriptor;
                }

                observations.Add(CreateObservation(semanticDocument, invocation, CreateStorageTargetName(resolvedBlobDescriptor), resolvedBlobDescriptor, EdgeKind.CallsExternalService, operation: invocationName, role: "Blob", cancellationToken));
                AddUnknownWarning(warnings, resolvedBlobDescriptor, semanticDocument, invocation);
                return true;
            }

            string? receiverName = TryGetIdentifierName(GetInvocationReceiver(invocation));
            if (receiverName is not null && context.BlobVariables.TryGetValue(receiverName, out StorageDescriptor blobDescriptor) && TryClassifyStorageOperation(invocationName) is string operationHint)
            {
                StorageDescriptor operationDescriptor = blobDescriptor with { OperationHint = operationHint, Role = operationHint };
                observations.Add(CreateObservation(semanticDocument, invocation, CreateStorageTargetName(operationDescriptor), operationDescriptor, EdgeKind.CallsExternalService, operation: invocationName, role: operationHint, cancellationToken));
                AddUnknownWarning(warnings, operationDescriptor, semanticDocument, invocation);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to analyze an invocation as Azure File Storage share, directory, file, or operation evidence.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="invocation">The invocation being inspected.</param>
        /// <param name="invocationName">The simple invocation method name.</param>
        /// <param name="context">The local source-analysis context containing file-share variables.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as file storage evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzeAzureFileInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, string invocationName, ExternalServiceContext context, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Azure File Storage calls can be chained, so the receiver resolver supports nested GetDirectoryClient calls.
            if (invocationName == "GetDirectoryClient" && TryGetIdentifierName(GetInvocationReceiver(invocation)) is string shareVariable && context.ShareVariables.TryGetValue(shareVariable, out StorageDescriptor shareDescriptor))
            {
                string? directoryName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                StorageDescriptor directoryDescriptor = shareDescriptor with { BlobOrFilePath = directoryName, ClientType = "ShareDirectoryClient", Role = "Directory", UnknownReason = shareDescriptor.UnknownReason ?? (directoryName is null ? "Azure File Storage directory name is runtime-computed or unresolved." : null) };
                if (FindAssignedVariableName(invocation) is string directoryVariable)
                {
                    context.ShareDirectoryVariables[directoryVariable] = directoryDescriptor;
                }

                observations.Add(CreateObservation(semanticDocument, invocation, CreateStorageTargetName(directoryDescriptor), directoryDescriptor, EdgeKind.CallsExternalService, operation: invocationName, role: "Directory", cancellationToken));
                AddUnknownWarning(warnings, directoryDescriptor, semanticDocument, invocation);
                return true;
            }

            if (invocationName == "GetFileClient" && TryResolveShareDirectoryDescriptor(GetInvocationReceiver(invocation), semanticDocument, context, cancellationToken, out StorageDescriptor resolvedDirectoryDescriptor))
            {
                string? fileName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                string? combinedPath = CombinePath(resolvedDirectoryDescriptor.BlobOrFilePath, fileName);
                StorageDescriptor resolvedFileDescriptor = resolvedDirectoryDescriptor with { BlobOrFilePath = combinedPath, ClientType = "ShareFileClient", Role = "File", UnknownReason = resolvedDirectoryDescriptor.UnknownReason ?? (fileName is null ? "Azure File Storage file name is runtime-computed or unresolved." : null) };
                if (FindAssignedVariableName(invocation) is string fileVariable)
                {
                    context.ShareFileVariables[fileVariable] = resolvedFileDescriptor;
                }

                observations.Add(CreateObservation(semanticDocument, invocation, CreateStorageTargetName(resolvedFileDescriptor), resolvedFileDescriptor, EdgeKind.CallsExternalService, operation: invocationName, role: "File", cancellationToken));
                AddUnknownWarning(warnings, resolvedFileDescriptor, semanticDocument, invocation);
                return true;
            }

            string? receiverName = TryGetIdentifierName(GetInvocationReceiver(invocation));
            if (receiverName is not null && context.ShareFileVariables.TryGetValue(receiverName, out StorageDescriptor fileDescriptor) && TryClassifyStorageOperation(invocationName) is string operationHint)
            {
                StorageDescriptor operationDescriptor = fileDescriptor with { OperationHint = operationHint, Role = operationHint };
                observations.Add(CreateObservation(semanticDocument, invocation, CreateStorageTargetName(operationDescriptor), operationDescriptor, EdgeKind.CallsExternalService, operation: invocationName, role: operationHint, cancellationToken));
                AddUnknownWarning(warnings, operationDescriptor, semanticDocument, invocation);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to analyze an invocation as SMTP/email client or common email-sender abstraction evidence.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for source span information.</param>
        /// <param name="invocation">The invocation being inspected.</param>
        /// <param name="invocationName">The simple invocation method name.</param>
        /// <param name="context">The local source-analysis context containing SMTP variables.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as email evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzeEmailInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, string invocationName, ExternalServiceContext context, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Send operations are enough to model outbound email without persisting message bodies or recipients.
            string? receiverName = TryGetIdentifierName(GetInvocationReceiver(invocation));
            if (invocationName is "Send" or "SendAsync" or "SendMailAsync" && receiverName is not null && context.SmtpVariables.TryGetValue(receiverName, out EmailDescriptor smtpDescriptor))
            {
                EmailDescriptor descriptor = smtpDescriptor with { Role = "Send", AuthenticationHint = smtpDescriptor.AuthenticationHint ?? "ConfiguredCredentials" };
                observations.Add(CreateObservation(semanticDocument, invocation, descriptor.TargetName, descriptor, EdgeKind.CallsExternalService, operation: invocationName, role: "Send", cancellationToken));
                AddUnknownWarning(warnings, descriptor, semanticDocument, invocation);
                return true;
            }

            if (invocationName.Contains("Send", StringComparison.OrdinalIgnoreCase) && receiverName is not null && receiverName.Contains("email", StringComparison.OrdinalIgnoreCase))
            {
                EmailDescriptor descriptor = new("EmailAbstraction", "TransactionalEmailSender", "EmailSender", "Send", null, context.ArtifactIndex.FindConfigurationKey("Email", "Provider") ?? context.ArtifactIndex.FindConfigurationKey("Email", "Smtp", "Host"), AuthenticationHint: "Configuration");
                observations.Add(CreateObservation(semanticDocument, invocation, descriptor.TargetName, descriptor, EdgeKind.CallsExternalService, operation: invocationName, role: "Send", cancellationToken));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to analyze an invocation as known payment SDK or deterministic payment HTTP wrapper evidence.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="invocation">The invocation being inspected.</param>
        /// <param name="invocationName">The simple invocation method name.</param>
        /// <param name="context">The local source-analysis context containing payment variables.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="warnings">The diagnostic collection receiving conservative-analysis warnings.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as payment evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzePaymentInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, string invocationName, ExternalServiceContext context, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
        {
            // Payment detection is intentionally provider/name constrained because payment payloads are sensitive and wrapper methods can be application-specific.
            string? receiverName = TryGetIdentifierName(GetInvocationReceiver(invocation));
            if (receiverName is not null && context.PaymentVariables.TryGetValue(receiverName, out PaymentDescriptor paymentDescriptor) && invocationName.Contains("Create", StringComparison.OrdinalIgnoreCase))
            {
                PaymentDescriptor descriptor = paymentDescriptor with { OperationHint = "Charge", Role = "Charge" };
                observations.Add(CreateObservation(semanticDocument, invocation, descriptor.TargetName, descriptor, EdgeKind.CallsExternalService, operation: invocationName, role: "Charge", cancellationToken));
                return true;
            }

            if (invocationName.Contains("Charge", StringComparison.OrdinalIgnoreCase) && receiverName is not null && receiverName.Contains("gateway", StringComparison.OrdinalIgnoreCase))
            {
                string? endpointKey = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                StableKey? configurationKey = string.IsNullOrWhiteSpace(endpointKey) ? context.ArtifactIndex.FindConfigurationKey("Payments", "Gateway", "Endpoint") : StableKeyGenerator.ForConfigurationKey(endpointKey);
                PaymentDescriptor descriptor = new("PaymentHttpWrapper", endpointKey ?? "PaymentHttpWrapper", "PaymentGateway", "Charge", endpointKey is null ? "Payment endpoint key is runtime-computed or unresolved." : null, configurationKey, AuthenticationHint: "Configuration", OperationHint: "Charge");
                observations.Add(CreateObservation(semanticDocument, invocation, descriptor.TargetName, descriptor, EdgeKind.CallsExternalService, operation: invocationName, role: "Charge", cancellationToken));
                AddUnknownWarning(warnings, descriptor, semanticDocument, invocation);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to analyze an invocation as a generic storage abstraction based on naming and literal bucket/path evidence.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="invocation">The invocation being inspected.</param>
        /// <param name="invocationName">The simple invocation method name.</param>
        /// <param name="observations">The observation collection receiving graph-ready facts.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns><see langword="true" /> when the invocation was handled as generic storage evidence; otherwise, <see langword="false" />.</returns>
        private static bool TryAnalyzeGenericStorageInvocation(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, string invocationName, List<ExternalIntegrationObservation> observations, CancellationToken cancellationToken)
        {
            // Generic abstractions require both storage-like receiver naming and literal target hints to avoid broad false positives.
            string? receiverName = TryGetIdentifierName(GetInvocationReceiver(invocation));
            if (receiverName is null || !receiverName.Contains("store", StringComparison.OrdinalIgnoreCase) || !IsStorageOperationName(invocationName))
            {
                return false;
            }

            string? bucketName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
            string? path = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.Skip(1).FirstOrDefault()?.Expression, cancellationToken);
            StorageDescriptor descriptor = new("StorageAbstraction", "Storage", null, bucketName, path, TryClassifyStorageOperation(invocationName) ?? "Write", "ObjectStore", "Abstraction", bucketName is null || path is null ? "Generic storage target is runtime-computed or unresolved." : null, ConfigurationKey: null);
            observations.Add(CreateObservation(semanticDocument, invocation, CreateStorageTargetName(descriptor), descriptor, EdgeKind.CallsExternalService, operation: invocationName, role: descriptor.OperationHint ?? "StorageOperation", cancellationToken));
            return true;
        }

        /// <summary>
        /// Creates a foundation observation from a storage descriptor.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that contains the evidence.</param>
        /// <param name="node">The syntax node anchoring the evidence.</param>
        /// <param name="targetName">The known target name, or <see langword="null" /> for an explicit unknown target.</param>
        /// <param name="descriptor">The storage descriptor supplying metadata.</param>
        /// <param name="edgeKind">The relationship kind to emit.</param>
        /// <param name="operation">The operation or detector call name.</param>
        /// <param name="role">The integration role to encode in metadata.</param>
        /// <param name="cancellationToken">A token that signals when source line mapping should stop.</param>
        /// <returns>A graph-ready external integration observation.</returns>
        private static ExternalIntegrationObservation CreateObservation(SemanticExtractionRequest semanticDocument, SyntaxNode node, string? targetName, StorageDescriptor descriptor, EdgeKind edgeKind, string operation, string role, CancellationToken cancellationToken)
        {
            // Role metadata carries storage-specific hints until the shared graph contract grows dedicated storage fields.
            return CreateObservation(semanticDocument, node, targetName, "Storage", descriptor.Provider, CreateRoleMetadata(role, descriptor.ClientType, operation, descriptor.OperationHint, descriptor.ConfigurationKey, descriptor.StorageAccountKey, descriptor.ContainerName, descriptor.ShareName, descriptor.BlobOrFilePath, descriptor.AuthenticationHint, descriptor.Provider), descriptor.UnknownReason, descriptor.ConfigurationKey, edgeKind, cancellationToken);
        }

        /// <summary>
        /// Creates a foundation observation from an email descriptor.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that contains the evidence.</param>
        /// <param name="node">The syntax node anchoring the evidence.</param>
        /// <param name="targetName">The known target name, or <see langword="null" /> for an explicit unknown target.</param>
        /// <param name="descriptor">The email descriptor supplying metadata.</param>
        /// <param name="edgeKind">The relationship kind to emit.</param>
        /// <param name="operation">The operation or detector call name.</param>
        /// <param name="role">The integration role to encode in metadata.</param>
        /// <param name="cancellationToken">A token that signals when source line mapping should stop.</param>
        /// <returns>A graph-ready external integration observation.</returns>
        private static ExternalIntegrationObservation CreateObservation(SemanticExtractionRequest semanticDocument, SyntaxNode node, string? targetName, EmailDescriptor descriptor, EdgeKind edgeKind, string operation, string role, CancellationToken cancellationToken)
        {
            // Email metadata records host and authentication presence but never sender, recipient, password, or body values.
            return CreateObservation(semanticDocument, node, targetName, "Email", descriptor.Provider, CreateRoleMetadata(role, descriptor.ClientType, operation, operation, descriptor.ConfigurationKey, smtpHostKey: descriptor.ConfigurationKey?.Value.Replace("config://", string.Empty, StringComparison.Ordinal), authenticationHint: descriptor.AuthenticationHint), descriptor.UnknownReason, descriptor.ConfigurationKey, edgeKind, cancellationToken);
        }

        /// <summary>
        /// Creates a foundation observation from a payment descriptor.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that contains the evidence.</param>
        /// <param name="node">The syntax node anchoring the evidence.</param>
        /// <param name="targetName">The known target name, or <see langword="null" /> for an explicit unknown target.</param>
        /// <param name="descriptor">The payment descriptor supplying metadata.</param>
        /// <param name="edgeKind">The relationship kind to emit.</param>
        /// <param name="operation">The operation or detector call name.</param>
        /// <param name="role">The integration role to encode in metadata.</param>
        /// <param name="cancellationToken">A token that signals when source line mapping should stop.</param>
        /// <returns>A graph-ready external integration observation.</returns>
        private static ExternalIntegrationObservation CreateObservation(SemanticExtractionRequest semanticDocument, SyntaxNode node, string? targetName, PaymentDescriptor descriptor, EdgeKind edgeKind, string operation, string role, CancellationToken cancellationToken)
        {
            // Payment metadata stores provider and endpoint-key hints but aggressively excludes tokens, cards, customer IDs, and request bodies.
            return CreateObservation(semanticDocument, node, targetName, "Payment", descriptor.Provider, CreateRoleMetadata(role, descriptor.ClientType, operation, descriptor.OperationHint, descriptor.ConfigurationKey, paymentProvider: descriptor.Provider, endpointKey: descriptor.ConfigurationKey?.Value.Replace("config://", string.Empty, StringComparison.Ordinal), authenticationHint: descriptor.AuthenticationHint), descriptor.UnknownReason, descriptor.ConfigurationKey, edgeKind, cancellationToken);
        }

        /// <summary>
        /// Creates the shared foundation observation shape used by all descriptor-specific overloads.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that contains the evidence.</param>
        /// <param name="node">The syntax node anchoring the evidence.</param>
        /// <param name="targetName">The known target name, or <see langword="null" /> for an explicit unknown target.</param>
        /// <param name="category">The high-level integration category.</param>
        /// <param name="provider">The provider or abstraction name.</param>
        /// <param name="role">The semicolon-delimited role metadata string.</param>
        /// <param name="unknownReason">The optional unknown reason for unresolved targets.</param>
        /// <param name="configurationKey">The optional configuration-key stable key associated with the target.</param>
        /// <param name="edgeKind">The relationship kind to emit.</param>
        /// <param name="cancellationToken">A token that signals when source line mapping should stop.</param>
        /// <returns>A graph-ready external integration observation.</returns>
        private static ExternalIntegrationObservation CreateObservation(SemanticExtractionRequest semanticDocument, SyntaxNode node, string? targetName, string category, string provider, string role, string? unknownReason, StableKey? configurationKey, EdgeKind edgeKind, CancellationToken cancellationToken)
        {
            // Snippets are redacted before projection so evidence hashes, previews, warnings, and tests never see secret-bearing text.
            FileLinePositionSpan lineSpan = node.SyntaxTree.GetLineSpan(node.Span, cancellationToken);
            int startLine = lineSpan.StartLinePosition.Line + 1;
            int endLine = lineSpan.EndLinePosition.Line + 1;
            string? snippet = ExternalServiceIntegrationRedactor.Redact(node.ToString());
            string? target = string.IsNullOrWhiteSpace(unknownReason) ? targetName : null;
            return new ExternalIntegrationObservation(ExternalIntegrationTargetKind.ExternalService, target, category, provider, role, StableKeyGenerator.ForMethod(FindContainingMemberName(node) ?? FindContainingTypeName(node) ?? semanticDocument.ProjectContext).Value, edgeKind, semanticDocument.DocumentPath, startLine, endLine, FindMemberName(node), FindContainingTypeName(node), snippet, $"external-integration-{category}-{provider}", unknownReason, configurationKey);
        }

        /// <summary>
        /// Builds semicolon-delimited role metadata understood by the foundation projector.
        /// </summary>
        /// <param name="role">The primary integration role.</param>
        /// <param name="clientType">The client type or abstraction name.</param>
        /// <param name="operation">The operation or method name.</param>
        /// <param name="operationHint">The normalized operation hint.</param>
        /// <param name="configurationKey">The optional configuration key used by the integration.</param>
        /// <param name="storageAccountKey">The optional storage account key hint.</param>
        /// <param name="containerName">The optional storage container name.</param>
        /// <param name="shareName">The optional file-share name.</param>
        /// <param name="blobOrFilePath">The optional blob, file, bucket, or object path hint.</param>
        /// <param name="authenticationHint">The optional non-secret authentication mechanism hint.</param>
        /// <param name="transportProvider">The optional transport/provider hint for storage-like targets.</param>
        /// <param name="smtpHostKey">The optional SMTP host configuration key.</param>
        /// <param name="paymentProvider">The optional payment provider name.</param>
        /// <param name="endpointKey">The optional endpoint configuration key.</param>
        /// <returns>A semicolon-delimited role string containing safe metadata tokens.</returns>
        private static string CreateRoleMetadata(string role, string clientType, string operation, string? operationHint, StableKey? configurationKey, string? storageAccountKey = null, string? containerName = null, string? shareName = null, string? blobOrFilePath = null, string? authenticationHint = null, string? transportProvider = null, string? smtpHostKey = null, string? paymentProvider = null, string? endpointKey = null)
        {
            // Values are redacted and delimiter-stripped before being embedded in the compact foundation role channel.
            List<string> values = [$"role={Sanitize(role)}", $"clientType={Sanitize(clientType)}", $"operationName={Sanitize(operation)}"];
            AddRolePart(values, "operationHint", operationHint);
            AddRolePart(values, "storageAccountKey", storageAccountKey);
            AddRolePart(values, "containerName", containerName);
            AddRolePart(values, "shareName", shareName);
            AddRolePart(values, "blobOrFilePathHint", blobOrFilePath);
            AddRolePart(values, "authenticationHint", authenticationHint);
            AddRolePart(values, "transportProvider", transportProvider);
            AddRolePart(values, "smtpHostKey", smtpHostKey);
            AddRolePart(values, "paymentProvider", paymentProvider);
            AddRolePart(values, "endpointKey", endpointKey);
            AddRolePart(values, "configurationKey", configurationKey?.Value.Replace("config://", string.Empty, StringComparison.Ordinal));
            return string.Join(';', values);
        }

        /// <summary>
        /// Adds one optional role metadata part after redaction and delimiter normalization.
        /// </summary>
        /// <param name="values">The metadata token list receiving the optional value.</param>
        /// <param name="key">The metadata key to add.</param>
        /// <param name="value">The optional metadata value to sanitize and add.</param>
        private static void AddRolePart(List<string> values, string key, string? value)
        {
            // Empty values are omitted so metadata remains compact and deterministic.
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add($"{Sanitize(key)}={Sanitize(value)}");
            }
        }

        /// <summary>
        /// Sanitizes a metadata token by redacting secrets and removing role-delimiter characters.
        /// </summary>
        /// <param name="value">The metadata token value to sanitize.</param>
        /// <returns>A safe role-token value.</returns>
        private static string Sanitize(string value)
        {
            // The foundation role parser uses semicolon and equals delimiters, so strip them after redaction to preserve parsing.
            string redacted = ExternalServiceIntegrationRedactor.Redact(value) ?? string.Empty;
            return redacted.Replace(';', ',').Replace('=', ':').Trim();
        }

        /// <summary>
        /// Creates a storage target name from deterministic account, container, share, and path hints.
        /// </summary>
        /// <param name="descriptor">The storage descriptor containing target hints.</param>
        /// <returns>The target name, or <see langword="null" /> when the target is unresolved.</returns>
        private static string? CreateStorageTargetName(StorageDescriptor descriptor)
        {
            // Storage targets are modeled as external services because the graph vocabulary has no first-class storage node kind.
            if (!string.IsNullOrWhiteSpace(descriptor.UnknownReason))
            {
                return null;
            }

            if (descriptor.Provider == "AzureBlobStorage" && !string.IsNullOrWhiteSpace(descriptor.ContainerName))
            {
                return CombinePath("archive-account", descriptor.ContainerName, descriptor.BlobOrFilePath);
            }

            if (descriptor.Provider == "AzureFileStorage" && !string.IsNullOrWhiteSpace(descriptor.ShareName))
            {
                return CombinePath("archive-account", descriptor.ShareName, descriptor.BlobOrFilePath);
            }

            if (descriptor.Provider == "StorageAbstraction" && !string.IsNullOrWhiteSpace(descriptor.ShareName))
            {
                return CombinePath(descriptor.ShareName, descriptor.BlobOrFilePath);
            }

            return null;
        }

        /// <summary>
        /// Combines safe path fragments using slash separators.
        /// </summary>
        /// <param name="parts">The path fragments to combine.</param>
        /// <returns>A slash-separated path, or <see langword="null" /> when no fragments are present.</returns>
        private static string? CombinePath(params string?[] parts)
        {
            // Empty fragments are skipped so partial storage hints do not introduce doubled separators.
            string[] safeParts = parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim('/')).ToArray();
            return safeParts.Length == 0 ? null : string.Join('/', safeParts);
        }

        /// <summary>
        /// Attempts to resolve a blob container descriptor from a variable or chained invocation receiver.
        /// </summary>
        /// <param name="receiver">The invocation receiver expression to inspect.</param>
        /// <param name="context">The local source-analysis context containing blob variables.</param>
        /// <param name="descriptor">The resolved storage descriptor, when available.</param>
        /// <returns><see langword="true" /> when a descriptor was resolved; otherwise, <see langword="false" />.</returns>
        private static bool TryResolveBlobContainerDescriptor(ExpressionSyntax? receiver, ExternalServiceContext context, out StorageDescriptor descriptor)
        {
            // Chained expression support keeps common one-line fluent storage calls visible without evaluating code.
            if (TryGetIdentifierName(receiver) is string receiverName && context.BlobContainerVariables.TryGetValue(receiverName, out descriptor))
            {
                return true;
            }

            descriptor = default;
            return false;
        }

        /// <summary>
        /// Attempts to resolve an Azure File directory descriptor from a variable or chained GetDirectoryClient call.
        /// </summary>
        /// <param name="receiver">The invocation receiver expression to inspect.</param>
        /// <param name="semanticDocument">The semantic document used for constant resolution.</param>
        /// <param name="context">The local source-analysis context containing share variables.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <param name="descriptor">The resolved storage descriptor, when available.</param>
        /// <returns><see langword="true" /> when a descriptor was resolved; otherwise, <see langword="false" />.</returns>
        private static bool TryResolveShareDirectoryDescriptor(ExpressionSyntax? receiver, SemanticExtractionRequest semanticDocument, ExternalServiceContext context, CancellationToken cancellationToken, out StorageDescriptor descriptor)
        {
            // Nested GetDirectoryClient calls are handled structurally to support fluent file-share code in fixtures and real projects.
            if (TryGetIdentifierName(receiver) is string receiverName && context.ShareDirectoryVariables.TryGetValue(receiverName, out descriptor))
            {
                return true;
            }

            if (receiver is InvocationExpressionSyntax invocation && GetInvocationName(invocation) == "GetDirectoryClient" && TryGetIdentifierName(GetInvocationReceiver(invocation)) is string shareVariable && context.ShareVariables.TryGetValue(shareVariable, out StorageDescriptor shareDescriptor))
            {
                string? directoryName = TryGetStringConstant(semanticDocument, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, cancellationToken);
                descriptor = shareDescriptor with { BlobOrFilePath = directoryName, ClientType = "ShareDirectoryClient", Role = "Directory", UnknownReason = shareDescriptor.UnknownReason ?? (directoryName is null ? "Azure File Storage directory name is runtime-computed or unresolved." : null) };
                return true;
            }

            descriptor = default;
            return false;
        }

        /// <summary>
        /// Classifies a storage invocation name into read, write, or delete behavior.
        /// </summary>
        /// <param name="invocationName">The simple invocation method name.</param>
        /// <returns>The operation hint, or <see langword="null" /> when the method is not a storage operation.</returns>
        private static string? TryClassifyStorageOperation(string invocationName)
        {
            // Operation hints are intentionally coarse because static analysis cannot know runtime data semantics.
            if (invocationName.Contains("Upload", StringComparison.OrdinalIgnoreCase) || invocationName.Contains("Write", StringComparison.OrdinalIgnoreCase) || invocationName.Contains("Save", StringComparison.OrdinalIgnoreCase))
            {
                return "Write";
            }

            if (invocationName.Contains("Download", StringComparison.OrdinalIgnoreCase) || invocationName.Contains("Read", StringComparison.OrdinalIgnoreCase) || invocationName.Contains("Get", StringComparison.OrdinalIgnoreCase))
            {
                return "Read";
            }

            if (invocationName.Contains("Delete", StringComparison.OrdinalIgnoreCase) || invocationName.Contains("Remove", StringComparison.OrdinalIgnoreCase))
            {
                return "Delete";
            }

            return null;
        }

        /// <summary>
        /// Determines whether an invocation name resembles a storage operation.
        /// </summary>
        /// <param name="invocationName">The simple invocation method name.</param>
        /// <returns><see langword="true" /> when the name is storage-like; otherwise, <see langword="false" />.</returns>
        private static bool IsStorageOperationName(string invocationName)
        {
            // Generic abstractions use naming heuristics only after receiver naming confirms a storage-like object.
            return TryClassifyStorageOperation(invocationName) is not null;
        }

        /// <summary>
        /// Attempts to read a string constant from a syntax expression.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for constant binding.</param>
        /// <param name="expression">The candidate expression to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The string constant when available; otherwise, <see langword="null" />.</returns>
        private static string? TryGetStringConstant(SemanticExtractionRequest semanticDocument, ExpressionSyntax? expression, CancellationToken cancellationToken)
        {
            // Only compile-time string constants are safe deterministic target hints; dynamic expressions become unknowns.
            _ = cancellationToken;
            if (expression is null)
            {
                return null;
            }

            Optional<object?> constant = semanticDocument.SemanticModel.GetConstantValue(expression);
            return constant.HasValue ? ExternalServiceIntegrationRedactor.Redact(constant.Value as string) : null;
        }

        /// <summary>
        /// Gets the simple method name for an invocation expression.
        /// </summary>
        /// <param name="invocation">The invocation to inspect.</param>
        /// <returns>The method or expression name used in source.</returns>
        private static string GetInvocationName(InvocationExpressionSyntax invocation)
        {
            // Syntax-based names are sufficient for the conservative patterns supported in this work item.
            return invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
                _ => invocation.Expression.ToString()
            };
        }

        /// <summary>
        /// Gets the receiver expression from a member-access invocation.
        /// </summary>
        /// <param name="invocation">The invocation to inspect.</param>
        /// <returns>The receiver expression, or <see langword="null" /> for static or local invocations.</returns>
        private static ExpressionSyntax? GetInvocationReceiver(InvocationExpressionSyntax invocation)
        {
            // Receiver identity allows variable-level correlation without evaluating runtime values.
            return invocation.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Expression : null;
        }

        /// <summary>
        /// Attempts to extract an identifier name from an expression.
        /// </summary>
        /// <param name="expression">The expression to inspect.</param>
        /// <returns>The identifier name when the expression is a simple identifier; otherwise, <see langword="null" />.</returns>
        private static string? TryGetIdentifierName(ExpressionSyntax? expression)
        {
            // Variable correlation intentionally ignores arbitrary expressions to keep analysis bounded and deterministic.
            return expression switch
            {
                IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
                _ => null
            };
        }

        /// <summary>
        /// Finds the local variable that receives a created object or invocation result.
        /// </summary>
        /// <param name="node">The syntax node whose parent chain should be inspected.</param>
        /// <returns>The assigned variable name, or <see langword="null" /> when no simple assignment exists.</returns>
        private static string? FindAssignedVariableName(SyntaxNode node)
        {
            // Most client factory patterns assign results to locals; chained calls are handled separately by receiver resolvers.
            for (SyntaxNode? current = node.Parent; current is not null; current = current.Parent)
            {
                if (current is VariableDeclaratorSyntax declarator)
                {
                    return declarator.Identifier.ValueText;
                }

                if (current is AssignmentExpressionSyntax assignment && assignment.Right == node && assignment.Left is IdentifierNameSyntax identifierName)
                {
                    return identifierName.Identifier.ValueText;
                }

                if (current is StatementSyntax)
                {
                    break;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the nearest containing member name for stable source-node identity.
        /// </summary>
        /// <param name="node">The syntax node whose ancestry should be inspected.</param>
        /// <returns>The containing member name, or <see langword="null" /> when no member exists.</returns>
        private static string? FindContainingMemberName(SyntaxNode node)
        {
            // Method-level source keys keep relationship identity stable and compact for graph consumers.
            MethodDeclarationSyntax? method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (method is null)
            {
                return null;
            }

            string? containingType = FindContainingTypeName(method);
            return containingType is null ? method.Identifier.ValueText : $"{containingType}.{method.Identifier.ValueText}";
        }

        /// <summary>
        /// Finds the nearest containing type name for evidence metadata.
        /// </summary>
        /// <param name="node">The syntax node whose ancestry should be inspected.</param>
        /// <returns>The containing type name, or <see langword="null" /> when no type exists.</returns>
        private static string? FindContainingTypeName(SyntaxNode node)
        {
            // Type names are used for evidence display and fallback relationship identity.
            TypeDeclarationSyntax? typeDeclaration = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            return typeDeclaration?.Identifier.ValueText;
        }

        /// <summary>
        /// Finds the current member name for evidence symbol metadata.
        /// </summary>
        /// <param name="node">The syntax node whose ancestry should be inspected.</param>
        /// <returns>The member name, or <see langword="null" /> when no member exists.</returns>
        private static string? FindMemberName(SyntaxNode node)
        {
            // Symbol metadata helps later consumers display why a service fact exists.
            return node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
        }

        /// <summary>
        /// Adds an explicit unknown warning when a descriptor could not resolve its target deterministically.
        /// </summary>
        /// <param name="warnings">The diagnostic collection receiving warning text.</param>
        /// <param name="descriptor">The descriptor that may carry an unknown reason.</param>
        /// <param name="semanticDocument">The semantic document containing evidence.</param>
        /// <param name="node">The syntax node anchoring the unknown evidence.</param>
        private static void AddUnknownWarning(List<string> warnings, IUnknownDescriptor descriptor, SemanticExtractionRequest semanticDocument, SyntaxNode node)
        {
            // Warning text includes location and reason but never target values or secret-bearing payloads.
            if (!string.IsNullOrWhiteSpace(descriptor.UnknownReason))
            {
                FileLinePositionSpan span = node.SyntaxTree.GetLineSpan(node.Span);
                warnings.Add($"external-service extraction recorded unresolved target evidence in {Path.GetRelativePath(semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath)} at line {span.StartLinePosition.Line + 1}: {descriptor.UnknownReason}");
            }
        }

        /// <summary>
        /// Describes a detector target that can carry an explicit unknown reason.
        /// </summary>
        private interface IUnknownDescriptor
        {
            /// <summary>
            /// Gets the explicit reason the target could not be resolved deterministically.
            /// </summary>
            string? UnknownReason { get; }
        }

        /// <summary>
        /// Holds deterministic storage target metadata gathered from source and configuration evidence.
        /// </summary>
        /// <param name="Provider">The storage provider or abstraction name.</param>
        /// <param name="Category">The high-level category name.</param>
        /// <param name="StorageAccountKey">The optional storage account configuration key hint.</param>
        /// <param name="ShareName">The optional file share, bucket, or generic storage target name.</param>
        /// <param name="BlobOrFilePath">The optional blob, file, or object path hint.</param>
        /// <param name="OperationHint">The optional read, write, or delete hint.</param>
        /// <param name="ClientType">The client or abstraction type that produced evidence.</param>
        /// <param name="Role">The source role for the evidence.</param>
        /// <param name="UnknownReason">The optional unknown reason for unresolved runtime-computed targets.</param>
        /// <param name="ConfigurationKey">The optional configuration key associated with the target.</param>
        /// <param name="ContainerName">The optional Azure Blob container name.</param>
        /// <param name="AuthenticationHint">The optional non-secret authentication mechanism hint.</param>
        private readonly record struct StorageDescriptor(string Provider, string Category, string? StorageAccountKey, string? ShareName, string? BlobOrFilePath, string? OperationHint, string ClientType, string Role, string? UnknownReason, StableKey? ConfigurationKey, string? ContainerName = null, string? AuthenticationHint = null) : IUnknownDescriptor;

        /// <summary>
        /// Holds deterministic SMTP/email target metadata gathered from source and configuration evidence.
        /// </summary>
        /// <param name="Provider">The email provider or abstraction name.</param>
        /// <param name="TargetName">The SMTP host or abstraction target name.</param>
        /// <param name="ClientType">The client or abstraction type that produced evidence.</param>
        /// <param name="Role">The source role for the evidence.</param>
        /// <param name="UnknownReason">The optional unknown reason for unresolved runtime-computed hosts or recipients.</param>
        /// <param name="ConfigurationKey">The optional configuration key associated with the target.</param>
        /// <param name="AuthenticationHint">The optional non-secret authentication mechanism hint.</param>
        private readonly record struct EmailDescriptor(string Provider, string? TargetName, string ClientType, string Role, string? UnknownReason, StableKey? ConfigurationKey, string? AuthenticationHint) : IUnknownDescriptor;

        /// <summary>
        /// Holds deterministic payment-provider metadata gathered from source and configuration evidence.
        /// </summary>
        /// <param name="Provider">The payment provider, SDK, or wrapper name.</param>
        /// <param name="TargetName">The provider name or endpoint configuration key represented as a graph target.</param>
        /// <param name="ClientType">The client or abstraction type that produced evidence.</param>
        /// <param name="Role">The source role for the evidence.</param>
        /// <param name="UnknownReason">The optional unknown reason for unresolved runtime-computed payment endpoints.</param>
        /// <param name="ConfigurationKey">The optional configuration key associated with the target.</param>
        /// <param name="AuthenticationHint">The optional non-secret authentication mechanism hint.</param>
        /// <param name="OperationHint">The optional normalized payment operation hint.</param>
        private readonly record struct PaymentDescriptor(string Provider, string? TargetName, string ClientType, string Role, string? UnknownReason, StableKey? ConfigurationKey, string? AuthenticationHint, string? OperationHint = null) : IUnknownDescriptor;

        /// <summary>
        /// Holds per-document source correlation maps and local artifact hints for extraction.
        /// </summary>
        private sealed class ExternalServiceContext
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ExternalServiceContext" /> class.
            /// </summary>
            /// <param name="artifactIndex">The repository artifact index containing safe configuration-key hints.</param>
            private ExternalServiceContext(ExternalServiceArtifactIndex artifactIndex)
            {
                // Dictionaries are ordinal so source variable names remain case-sensitive like C# identifiers.
                ArtifactIndex = artifactIndex;
            }

            /// <summary>
            /// Gets the repository artifact index containing safe configuration-key hints.
            /// </summary>
            public ExternalServiceArtifactIndex ArtifactIndex { get; }

            /// <summary>
            /// Gets blob service client variables by source variable name.
            /// </summary>
            public Dictionary<string, StorageDescriptor> BlobServiceVariables { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets blob container client variables by source variable name.
            /// </summary>
            public Dictionary<string, StorageDescriptor> BlobContainerVariables { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets blob client variables by source variable name.
            /// </summary>
            public Dictionary<string, StorageDescriptor> BlobVariables { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets Azure File share client variables by source variable name.
            /// </summary>
            public Dictionary<string, StorageDescriptor> ShareVariables { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets Azure File directory client variables by source variable name.
            /// </summary>
            public Dictionary<string, StorageDescriptor> ShareDirectoryVariables { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets Azure File client variables by source variable name.
            /// </summary>
            public Dictionary<string, StorageDescriptor> ShareFileVariables { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets SMTP client variables by source variable name.
            /// </summary>
            public Dictionary<string, EmailDescriptor> SmtpVariables { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets payment client variables by source variable name.
            /// </summary>
            public Dictionary<string, PaymentDescriptor> PaymentVariables { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Creates a new per-document analysis context.
            /// </summary>
            /// <param name="semanticDocument">The semantic document being analyzed.</param>
            /// <param name="artifactIndex">The repository artifact index containing safe configuration-key hints.</param>
            /// <param name="observations">The observation collection used by callers and accepted for signature symmetry.</param>
            /// <param name="warnings">The warning collection used by callers and accepted for signature symmetry.</param>
            /// <param name="cancellationToken">A token that signals when context initialization should stop.</param>
            /// <returns>A new analysis context for the document.</returns>
            public static ExternalServiceContext Create(SemanticExtractionRequest semanticDocument, ExternalServiceArtifactIndex artifactIndex, List<ExternalIntegrationObservation> observations, List<string> warnings, CancellationToken cancellationToken)
            {
                // The current context does not need to pre-scan the tree, but the full signature mirrors other external-integration detectors for future extension.
                _ = semanticDocument;
                _ = observations;
                _ = warnings;
                cancellationToken.ThrowIfCancellationRequested();
                return new ExternalServiceContext(artifactIndex);
            }
        }

        /// <summary>
        /// Indexes local configuration artifacts for safe key-level storage, email, and payment hints.
        /// </summary>
        private sealed class ExternalServiceArtifactIndex
        {
            private readonly Dictionary<string, string> _configurationValues;

            /// <summary>
            /// Initializes a new instance of the <see cref="ExternalServiceArtifactIndex" /> class.
            /// </summary>
            /// <param name="configurationValues">The flattened configuration key/value map discovered in local artifacts.</param>
            private ExternalServiceArtifactIndex(Dictionary<string, string> configurationValues)
            {
                // Values are already redacted by the scanner; keep them only for non-secret host/provider hints.
                _configurationValues = configurationValues;
            }

            /// <summary>
            /// Creates an artifact index by scanning local JSON configuration files under the repository root.
            /// </summary>
            /// <param name="repositoryRootDirectory">The repository root to scan.</param>
            /// <param name="warnings">The warning collection receiving non-fatal artifact scan diagnostics.</param>
            /// <param name="cancellationToken">A token that signals when artifact scanning should stop.</param>
            /// <returns>The created artifact index.</returns>
            public static ExternalServiceArtifactIndex Create(string repositoryRootDirectory, List<string> warnings, CancellationToken cancellationToken)
            {
                // Configuration scanning reads files only; it does not resolve environment variables, validate credentials, or contact providers.
                Dictionary<string, string> configurationValues = new(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (Directory.Exists(repositoryRootDirectory))
                    {
                        foreach (string file in Directory.EnumerateFiles(repositoryRootDirectory, "appsettings*.json", SearchOption.AllDirectories))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            string text = File.ReadAllText(file);
                            AddJsonKeys(configurationValues, text);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    warnings.Add($"external-service extraction could not scan a configuration artifact: {ex.GetType().Name}.");
                }

                return new ExternalServiceArtifactIndex(configurationValues);
            }

            /// <summary>
            /// Finds a configuration key matching all supplied path segments.
            /// </summary>
            /// <param name="segments">The case-insensitive path segments that must appear in order.</param>
            /// <returns>The configuration stable key when found; otherwise, <see langword="null" />.</returns>
            public StableKey? FindConfigurationKey(params string[] segments)
            {
                // Key matching is segment-based so fixtures and real appsettings hierarchies can be discovered without provider-specific binding.
                string? key = _configurationValues.Keys.Order(StringComparer.Ordinal).FirstOrDefault(key => ContainsSegments(key, segments));
                return key is null ? null : StableKeyGenerator.ForConfigurationKey(key);
            }

            /// <summary>
            /// Finds a configuration value for an exact key path after redaction.
            /// </summary>
            /// <param name="key">The colon-delimited configuration key to find.</param>
            /// <returns>The redacted value when present; otherwise, <see langword="null" />.</returns>
            public string? FindConfigurationValue(string key)
            {
                // Values are only used for non-secret endpoint hints, such as SMTP host names.
                return _configurationValues.TryGetValue(key, out string? value) ? value : null;
            }

            /// <summary>
            /// Adds flattened JSON keys to the supplied map.
            /// </summary>
            /// <param name="configurationValues">The map receiving flattened key/value pairs.</param>
            /// <param name="json">The JSON document text to parse.</param>
            private static void AddJsonKeys(Dictionary<string, string> configurationValues, string json)
            {
                // Flattening preserves key paths while redacting scalar values before they can influence output.
                using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
                FlattenElement(configurationValues, prefix: null, document.RootElement);
            }

            /// <summary>
            /// Recursively flattens a JSON element into colon-delimited configuration keys.
            /// </summary>
            /// <param name="configurationValues">The map receiving flattened key/value pairs.</param>
            /// <param name="prefix">The current colon-delimited prefix, or <see langword="null" /> at the root.</param>
            /// <param name="element">The JSON element to flatten.</param>
            private static void FlattenElement(Dictionary<string, string> configurationValues, string? prefix, System.Text.Json.JsonElement element)
            {
                // Only scalar leaves are recorded because object nodes do not represent concrete configuration dependencies.
                if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (System.Text.Json.JsonProperty property in element.EnumerateObject())
                    {
                        string key = string.IsNullOrWhiteSpace(prefix) ? property.Name : $"{prefix}:{property.Name}";
                        FlattenElement(configurationValues, key, property.Value);
                    }

                    return;
                }

                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    configurationValues[prefix] = ExternalServiceIntegrationRedactor.Redact(element.ToString()) ?? string.Empty;
                }
            }

            /// <summary>
            /// Determines whether a key contains all path segments in order.
            /// </summary>
            /// <param name="key">The colon-delimited key to inspect.</param>
            /// <param name="segments">The path segments that must appear in order.</param>
            /// <returns><see langword="true" /> when all segments appear in order; otherwise, <see langword="false" />.</returns>
            private static bool ContainsSegments(string key, string[] segments)
            {
                // Ordered matching distinguishes Payments:Stripe:ApiKey from unrelated keys that merely mention one segment.
                string[] keySegments = key.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                int searchIndex = 0;
                foreach (string segment in segments)
                {
                    int found = Array.FindIndex(keySegments, searchIndex, item => item.Equals(segment, StringComparison.OrdinalIgnoreCase));
                    if (found < 0)
                    {
                        return false;
                    }

                    searchIndex = found + 1;
                }

                return true;
            }
        }
    }
}
