using System.Security.Cryptography;
using System.Text;
using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Archon.Extractors.AspNet.Runtime
{
    /// <summary>
    /// Extracts graph-ready console runtime entry-point facts from C# and VB.NET semantic documents.
    /// </summary>
    /// <remarks>
    /// The extractor performs static analysis only. It detects source entry-point declarations inside the submitted target repository context and never executes target application code, evaluates MSBuild targets, starts an application process, or writes directly to persistence.
    /// </remarks>
    public sealed class ConsoleEntryPointRuntimeExtractor
    {
        /// <summary>
        /// Stores the runtime-kind metadata value used for console entry-point facts.
        /// </summary>
        private const string RuntimeKind = "ConsoleEntryPoint";

        /// <summary>
        /// Stores the runtime classification metadata value for console applications.
        /// </summary>
        private const string RuntimeClassification = "ConsoleApplication";

        /// <summary>
        /// Stores the detection-mode metadata value used for C# and VB.NET Main methods.
        /// </summary>
        private const string MainMethodDetectionMode = "MainMethodSymbol";

        /// <summary>
        /// Stores the detection-mode metadata value used for C# top-level statement documents.
        /// </summary>
        private const string TopLevelStatementsDetectionMode = "CSharpTopLevelStatements";

        /// <summary>
        /// Stores the confidence explanation for compiler-backed Main method entry points.
        /// </summary>
        private const string MainMethodConfidenceReason = "Console entry point detected from a static or shared Main method in the submitted target repository context.";

        /// <summary>
        /// Stores the confidence explanation for top-level statement entry points.
        /// </summary>
        private const string TopLevelStatementsConfidenceReason = "Console entry point detected from C# top-level statements in the submitted target repository context.";

        /// <summary>
        /// Stores the unknown reason attached when more than one candidate entry point is visible for a project.
        /// </summary>
        private const string AmbiguousEntryPointUnknownReason = "Multiple console entry-point candidates were detected for the project.";

        /// <summary>
        /// Extracts console entry-point runtime graph facts from the supplied semantic documents.
        /// </summary>
        /// <param name="request">The snapshot and semantic document request that scopes console entry-point extraction.</param>
        /// <param name="cancellationToken">A token that stops syntax traversal before or during source inspection.</param>
        /// <returns>An extraction result containing runtime nodes, relationships, source evidence, and diagnostics.</returns>
        public ConsoleEntryPointExtractionResult Extract(ConsoleEntryPointExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // The accumulator gives project, type, method, relationship, and evidence facts deterministic duplicate behavior inside one extraction slice.
            ArgumentNullException.ThrowIfNull(request);
            ArchitectureSnapshotAccumulator accumulator = new();
            Dictionary<StableKey, List<EntryPointDescriptor>> candidatesByProject = new();

            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (EntryPointDescriptor descriptor in AnalyzeDocument(semanticDocument, cancellationToken))
                {
                    if (!candidatesByProject.TryGetValue(descriptor.ProjectStableKey, out List<EntryPointDescriptor>? projectCandidates))
                    {
                        projectCandidates = [];
                        candidatesByProject.Add(descriptor.ProjectStableKey, projectCandidates);
                    }

                    projectCandidates.Add(descriptor);
                }
            }

            foreach (List<EntryPointDescriptor> projectCandidates in candidatesByProject.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool hasAmbiguity = projectCandidates.Count > 1;
                foreach (EntryPointDescriptor candidate in projectCandidates.OrderBy(static candidate => candidate.StableSortKey, StringComparer.Ordinal))
                {
                    AccumulateEntryPoint(request.SnapshotStableKey, accumulator, hasAmbiguity ? candidate.WithUnknown(AmbiguousEntryPointUnknownReason) : candidate);
                }
            }

            return new ConsoleEntryPointExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Analyzes one semantic document for supported C# or VB.NET entry-point candidates.
        /// </summary>
        /// <param name="semanticDocument">The semantic source document being inspected.</param>
        /// <param name="cancellationToken">A token that stops syntax traversal and semantic inspection.</param>
        /// <returns>The entry-point descriptors detected in the document.</returns>
        private static IReadOnlyList<EntryPointDescriptor> AnalyzeDocument(SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Language-specific Roslyn roots are inspected only for source documents supplied through the accepted extraction request.
            SourceText sourceText = semanticDocument.SyntaxTree.GetText(cancellationToken);
            DocumentContext context = CreateDocumentContext(semanticDocument);
            if (semanticDocument.SyntaxTree.Options.Language == LanguageNames.CSharp)
            {
                return AnalyzeCSharpDocument(semanticDocument, sourceText, context, cancellationToken);
            }

            if (semanticDocument.SyntaxTree.Options.Language == LanguageNames.VisualBasic)
            {
                return AnalyzeVisualBasicDocument(semanticDocument, sourceText, context, cancellationToken);
            }

            return [];
        }

        /// <summary>
        /// Analyzes one C# source document for <c>Main</c> methods and top-level statements.
        /// </summary>
        /// <param name="semanticDocument">The C# semantic source document being inspected.</param>
        /// <param name="sourceText">The source text used to derive evidence snippets.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="cancellationToken">A token that stops syntax traversal and semantic inspection.</param>
        /// <returns>The C# entry-point descriptors detected in the document.</returns>
        private static IReadOnlyList<EntryPointDescriptor> AnalyzeCSharpDocument(SemanticExtractionRequest semanticDocument, SourceText sourceText, DocumentContext context, CancellationToken cancellationToken)
        {
            // C# entry points can be explicit static Main methods or compiler-generated Main methods implied by top-level statements.
            Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax root = (Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax)semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            List<EntryPointDescriptor> descriptors = [];
            foreach (MethodDeclarationSyntax method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsCSharpMainCandidate(method, semanticDocument, cancellationToken))
                {
                    descriptors.Add(CreateCSharpMainDescriptor(semanticDocument, method, sourceText, context, cancellationToken));
                }
            }

            if (root.Members.OfType<GlobalStatementSyntax>().FirstOrDefault() is GlobalStatementSyntax globalStatement)
            {
                descriptors.Add(CreateCSharpTopLevelStatementDescriptor(semanticDocument, root, globalStatement, sourceText, context, cancellationToken));
            }

            return descriptors;
        }

        /// <summary>
        /// Analyzes one VB.NET source document for <c>Sub Main</c> or <c>Function Main</c> methods.
        /// </summary>
        /// <param name="semanticDocument">The VB.NET semantic source document being inspected.</param>
        /// <param name="sourceText">The source text used to derive evidence snippets.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="cancellationToken">A token that stops syntax traversal and semantic inspection.</param>
        /// <returns>The VB.NET entry-point descriptors detected in the document.</returns>
        private static IReadOnlyList<EntryPointDescriptor> AnalyzeVisualBasicDocument(SemanticExtractionRequest semanticDocument, SourceText sourceText, DocumentContext context, CancellationToken cancellationToken)
        {
            // VB.NET console entry points are declared as Shared Main members; modules are shared by compiler convention and are accepted when Roslyn identifies the method symbol accordingly.
            VisualBasicSyntaxNode root = (VisualBasicSyntaxNode)semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            List<EntryPointDescriptor> descriptors = [];
            foreach (MethodBlockSyntax methodBlock in root.DescendantNodes().OfType<MethodBlockSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                MethodStatementSyntax statement = methodBlock.SubOrFunctionStatement;
                if (IsVisualBasicMainCandidate(statement, semanticDocument, cancellationToken))
                {
                    descriptors.Add(CreateVisualBasicMainDescriptor(semanticDocument, methodBlock, sourceText, context, cancellationToken));
                }
            }

            return descriptors;
        }

        /// <summary>
        /// Determines whether a C# method declaration is a supported console <c>Main</c> candidate.
        /// </summary>
        /// <param name="method">The C# method declaration to inspect.</param>
        /// <param name="semanticDocument">The semantic document that can resolve the method symbol.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns><see langword="true" /> when the method is a static <c>Main</c> candidate; otherwise, <see langword="false" />.</returns>
        private static bool IsCSharpMainCandidate(MethodDeclarationSyntax method, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Roslyn symbols provide the authoritative static modifier where possible; syntax fallback keeps incomplete compilations useful.
            if (!string.Equals(method.Identifier.ValueText, "Main", StringComparison.Ordinal))
            {
                return false;
            }

            if (semanticDocument.SemanticModel.GetDeclaredSymbol(method, cancellationToken) is IMethodSymbol symbol)
            {
                return symbol.IsStatic && !symbol.IsGenericMethod;
            }

            return method.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword);
        }

        /// <summary>
        /// Determines whether a VB.NET method statement is a supported console <c>Main</c> candidate.
        /// </summary>
        /// <param name="statement">The VB.NET method statement to inspect.</param>
        /// <param name="semanticDocument">The semantic document that can resolve the method symbol.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns><see langword="true" /> when the method is a shared or module <c>Main</c> candidate; otherwise, <see langword="false" />.</returns>
        private static bool IsVisualBasicMainCandidate(MethodStatementSyntax statement, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // VB module members are shared by convention, so a resolved static symbol or a containing module is accepted as a console candidate.
            if (!string.Equals(statement.Identifier.ValueText, "Main", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (semanticDocument.SemanticModel.GetDeclaredSymbol(statement, cancellationToken) is IMethodSymbol symbol)
            {
                return symbol.IsStatic || symbol.ContainingType?.TypeKind == TypeKind.Module;
            }

            return statement.Modifiers.Any(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.SharedKeyword) || statement.Parent?.Parent is ModuleBlockSyntax;
        }

        /// <summary>
        /// Creates a descriptor for an explicit C# <c>Main</c> method.
        /// </summary>
        /// <param name="semanticDocument">The semantic document containing the method.</param>
        /// <param name="method">The C# method declaration that represents the entry point.</param>
        /// <param name="sourceText">The source text used to derive evidence snippets.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>A descriptor carrying graph, metadata, and evidence values for the C# entry point.</returns>
        private static EntryPointDescriptor CreateCSharpMainDescriptor(SemanticExtractionRequest semanticDocument, MethodDeclarationSyntax method, SourceText sourceText, DocumentContext context, CancellationToken cancellationToken)
        {
            // The explicit Main method maps naturally to a Type node and a Method node with the type containing the method.
            IMethodSymbol? methodSymbol = semanticDocument.SemanticModel.GetDeclaredSymbol(method, cancellationToken);
            string methodIdentity = methodSymbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? BuildCSharpMethodFallbackIdentity(method, context.RepositoryRelativeDocumentPath, cancellationToken);
            string containingTypeName = methodSymbol?.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? GetContainingCSharpTypeName(method);
            string displayName = methodSymbol?.Name ?? method.Identifier.ValueText;
            FileLinePositionSpan lineSpan = semanticDocument.SyntaxTree.GetLineSpan(method.Identifier.Span, cancellationToken);
            return CreateMethodDescriptor(
                context,
                sourceText,
                method,
                lineSpan,
                language: "C#",
                detectionMode: MainMethodDetectionMode,
                confidenceReason: MainMethodConfidenceReason,
                methodIdentity: methodIdentity,
                displayName: displayName,
                containingTypeName: containingTypeName,
                symbolName: displayName,
                topLevelFilePath: null);
        }

        /// <summary>
        /// Creates a descriptor for C# top-level statements.
        /// </summary>
        /// <param name="semanticDocument">The semantic document containing the top-level statements.</param>
        /// <param name="root">The C# compilation unit that contains the top-level statements.</param>
        /// <param name="globalStatement">The first global statement used as evidence anchor.</param>
        /// <param name="sourceText">The source text used to derive evidence snippets.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="cancellationToken">A token that stops source inspection.</param>
        /// <returns>A descriptor carrying graph, metadata, and evidence values for the top-level statement entry point.</returns>
        private static EntryPointDescriptor CreateCSharpTopLevelStatementDescriptor(SemanticExtractionRequest semanticDocument, Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax root, GlobalStatementSyntax globalStatement, SourceText sourceText, DocumentContext context, CancellationToken cancellationToken)
        {
            // Top-level statements compile into an implicit Main method, so graph identity uses the normalized repository-relative file path rather than a synthetic compiler name.
            FileLinePositionSpan lineSpan = semanticDocument.SyntaxTree.GetLineSpan(globalStatement.Span, cancellationToken);
            string methodIdentity = $"top-level://{context.ProjectStableKey.Value}/{context.RepositoryRelativeDocumentPath}";
            SyntaxNode evidenceNode = root.Members.OfType<GlobalStatementSyntax>().FirstOrDefault() ?? globalStatement;
            return CreateMethodDescriptor(
                context,
                sourceText,
                evidenceNode,
                lineSpan,
                language: "C#",
                detectionMode: TopLevelStatementsDetectionMode,
                confidenceReason: TopLevelStatementsConfidenceReason,
                methodIdentity: methodIdentity,
                displayName: "<top-level statements>",
                containingTypeName: null,
                symbolName: "<top-level statements>",
                topLevelFilePath: context.RepositoryRelativeDocumentPath);
        }

        /// <summary>
        /// Creates a descriptor for an explicit VB.NET <c>Main</c> method.
        /// </summary>
        /// <param name="semanticDocument">The semantic document containing the method.</param>
        /// <param name="methodBlock">The VB.NET method block that represents the entry point.</param>
        /// <param name="sourceText">The source text used to derive evidence snippets.</param>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="cancellationToken">A token that stops semantic inspection.</param>
        /// <returns>A descriptor carrying graph, metadata, and evidence values for the VB.NET entry point.</returns>
        private static EntryPointDescriptor CreateVisualBasicMainDescriptor(SemanticExtractionRequest semanticDocument, MethodBlockSyntax methodBlock, SourceText sourceText, DocumentContext context, CancellationToken cancellationToken)
        {
            // VB.NET Main detection follows Roslyn method symbols and falls back to source containing type text when references are incomplete.
            MethodStatementSyntax statement = methodBlock.SubOrFunctionStatement;
            IMethodSymbol? methodSymbol = semanticDocument.SemanticModel.GetDeclaredSymbol(statement, cancellationToken);
            string methodIdentity = methodSymbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? BuildVisualBasicMethodFallbackIdentity(statement, context.RepositoryRelativeDocumentPath, cancellationToken);
            string? containingTypeName = methodSymbol?.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? GetContainingVisualBasicTypeName(statement);
            FileLinePositionSpan lineSpan = semanticDocument.SyntaxTree.GetLineSpan(statement.Identifier.Span, cancellationToken);
            return CreateMethodDescriptor(
                context,
                sourceText,
                methodBlock,
                lineSpan,
                language: "VB.NET",
                detectionMode: MainMethodDetectionMode,
                confidenceReason: MainMethodConfidenceReason,
                methodIdentity: methodIdentity,
                displayName: statement.Identifier.ValueText,
                containingTypeName: containingTypeName,
                symbolName: statement.Identifier.ValueText,
                topLevelFilePath: null);
        }

        /// <summary>
        /// Creates the shared method descriptor shape used by all console entry-point detections.
        /// </summary>
        /// <param name="context">The normalized document context for project and evidence identity.</param>
        /// <param name="sourceText">The source text used to derive evidence snippets.</param>
        /// <param name="evidenceNode">The syntax node that supports the entry-point fact.</param>
        /// <param name="lineSpan">The line span used for evidence coordinates.</param>
        /// <param name="language">The source language recorded on emitted graph nodes.</param>
        /// <param name="detectionMode">The detection mode to record in metadata.</param>
        /// <param name="confidenceReason">The confidence explanation to record in metadata.</param>
        /// <param name="methodIdentity">The deterministic method identity or top-level file identity.</param>
        /// <param name="displayName">The developer-facing method node display name.</param>
        /// <param name="containingTypeName">The optional containing type name for explicit Main methods.</param>
        /// <param name="symbolName">The source symbol name for evidence records.</param>
        /// <param name="topLevelFilePath">The normalized top-level-statement file path when no explicit method symbol exists.</param>
        /// <returns>A descriptor carrying normalized graph, metadata, and evidence values.</returns>
        private static EntryPointDescriptor CreateMethodDescriptor(DocumentContext context, SourceText sourceText, SyntaxNode evidenceNode, FileLinePositionSpan lineSpan, string language, string detectionMode, string confidenceReason, string methodIdentity, string displayName, string? containingTypeName, string symbolName, string? topLevelFilePath)
        {
            // The descriptor is built once so ambiguity handling can later adjust confidence and unknown state without changing stable identity.
            int startLine = lineSpan.StartLinePosition.Line + 1;
            int endLine = lineSpan.EndLinePosition.Line + 1;
            string snippetPreview = CreateSnippetPreview(evidenceNode, sourceText);
            StableKey methodStableKey = CreateEntryPointStableKey(context.ProjectStableKey, methodIdentity, topLevelFilePath);
            StableKey? typeStableKey = containingTypeName is null ? null : CreateTypeStableKey(context.ProjectStableKey, containingTypeName);
            StableKey evidenceStableKey = CreateEvidenceStableKey(context.ProjectStableKey, context.RepositoryRelativeDocumentPath, startLine, endLine, symbolName);
            GraphMetadata metadata = CreateEntryPointMetadata(detectionMode, confidenceReason, methodIdentity, containingTypeName, topLevelFilePath, unknownReason: null);
            return new EntryPointDescriptor(
                context.ProjectStableKey,
                context.ProjectDisplayName,
                context.RepositoryRelativeDocumentPath,
                methodStableKey,
                typeStableKey,
                displayName,
                methodIdentity,
                containingTypeName,
                language,
                detectionMode,
                confidenceReason,
                symbolName,
                containingTypeName ?? context.ProjectDisplayName,
                startLine,
                endLine,
                evidenceStableKey,
                snippetPreview,
                CreateSha256Hash(snippetPreview),
                topLevelFilePath,
                KnowledgeKind.Fact,
                Confidence.High,
                null,
                metadata);
        }

        /// <summary>
        /// Accumulates project, optional type, method, containment relationship, and evidence facts for one entry point.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives extracted facts.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        /// <param name="descriptor">The entry-point descriptor to project.</param>
        private static void AccumulateEntryPoint(StableKey snapshotStableKey, ArchitectureSnapshotAccumulator accumulator, EntryPointDescriptor descriptor)
        {
            // Entry-point facts reuse existing Project, Type, and Method node kinds because the graph contract has no dedicated entry-point node kind.
            EvidenceRecord evidence = CreateEvidenceRecord(snapshotStableKey, descriptor);
            accumulator.AddEvidence(evidence);
            accumulator.AddNode(CreateProjectNode(snapshotStableKey, descriptor, evidence.StableKey));

            if (descriptor.TypeStableKey is StableKey typeStableKey && descriptor.ContainingTypeName is not null)
            {
                ArchitectureNode typeNode = CreateTypeNode(snapshotStableKey, descriptor, evidence.StableKey);
                accumulator.AddNode(typeNode);
                accumulator.AddEdge(CreateContainsEdge(snapshotStableKey, descriptor.ProjectStableKey, typeStableKey, evidence.StableKey, descriptor, "ProjectContainsEntryPointType"));
                accumulator.AddNode(CreateMethodNode(snapshotStableKey, descriptor, evidence.StableKey, parentStableKey: typeStableKey));
                accumulator.AddEdge(CreateContainsEdge(snapshotStableKey, typeStableKey, descriptor.MethodStableKey, evidence.StableKey, descriptor, "TypeContainsEntryPointMethod"));
                return;
            }

            accumulator.AddNode(CreateMethodNode(snapshotStableKey, descriptor, evidence.StableKey, parentStableKey: descriptor.ProjectStableKey));
            accumulator.AddEdge(CreateContainsEdge(snapshotStableKey, descriptor.ProjectStableKey, descriptor.MethodStableKey, evidence.StableKey, descriptor, "ProjectContainsTopLevelEntryPoint"));
        }

        /// <summary>
        /// Creates a project node enriched with console runtime classification metadata.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the project node.</param>
        /// <param name="descriptor">The entry-point descriptor that supports the project runtime fact.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the project runtime fact.</param>
        /// <returns>A project architecture node for the console entry-point contribution.</returns>
        private static ArchitectureNode CreateProjectNode(StableKey snapshotStableKey, EntryPointDescriptor descriptor, StableKey evidenceStableKey)
        {
            // The project node records runtime classification so consumers can identify console application boundaries even before query APIs exist.
            GraphMetadata metadata = CreateEntryPointMetadata(descriptor.DetectionMode, descriptor.ConfidenceReason, descriptor.MethodIdentity, descriptor.ContainingTypeName, descriptor.TopLevelFilePath, descriptor.UnknownReason, includeMethodIdentity: false);
            return new ArchitectureNode(
                snapshotStableKey,
                descriptor.ProjectStableKey,
                NodeKind.Project,
                descriptor.ProjectDisplayName,
                descriptor.ProjectDisplayName,
                descriptor.ProjectDisplayName.ToUpperInvariant(),
                descriptor.Language,
                descriptor.ProjectStableKey,
                null,
                descriptor.KnowledgeKind,
                null,
                null,
                descriptor.Confidence,
                UnknownStateFor(descriptor.UnknownReason),
                evidenceStableKey,
                metadata,
                FingerprintGenerator.ForNode(NodeKind.Project, descriptor.ProjectDisplayName, descriptor.ProjectDisplayName, descriptor.ProjectDisplayName.ToUpperInvariant(), descriptor.KnowledgeKind, metadata));
        }

        /// <summary>
        /// Creates a type node for an explicit entry-point containing type.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the type node.</param>
        /// <param name="descriptor">The entry-point descriptor that carries type identity.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the type fact.</param>
        /// <returns>A type architecture node for the entry-point containing type.</returns>
        private static ArchitectureNode CreateTypeNode(StableKey snapshotStableKey, EntryPointDescriptor descriptor, StableKey evidenceStableKey)
        {
            // The type node allows future consumers to navigate from the project boundary to the concrete source type that owns Main.
            string typeName = descriptor.ContainingTypeName ?? descriptor.ProjectDisplayName;
            GraphMetadata metadata = CreateEntryPointMetadata(descriptor.DetectionMode, descriptor.ConfidenceReason, descriptor.MethodIdentity, descriptor.ContainingTypeName, descriptor.TopLevelFilePath, descriptor.UnknownReason, includeMethodIdentity: false);
            return new ArchitectureNode(
                snapshotStableKey,
                descriptor.TypeStableKey ?? descriptor.ProjectStableKey,
                NodeKind.Type,
                typeName.Split('.').Last(),
                typeName,
                typeName.ToUpperInvariant(),
                descriptor.Language,
                descriptor.ProjectStableKey,
                descriptor.ProjectStableKey,
                descriptor.KnowledgeKind,
                null,
                null,
                descriptor.Confidence,
                UnknownStateFor(descriptor.UnknownReason),
                evidenceStableKey,
                metadata,
                FingerprintGenerator.ForNode(NodeKind.Type, typeName.Split('.').Last(), typeName, typeName.ToUpperInvariant(), descriptor.KnowledgeKind, metadata));
        }

        /// <summary>
        /// Creates a method node for an explicit or top-level entry point.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the method node.</param>
        /// <param name="descriptor">The entry-point descriptor that carries method identity.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the method fact.</param>
        /// <param name="parentStableKey">The parent project or type stable key for the method node.</param>
        /// <returns>A method architecture node for the console entry point.</returns>
        private static ArchitectureNode CreateMethodNode(StableKey snapshotStableKey, EntryPointDescriptor descriptor, StableKey evidenceStableKey, StableKey parentStableKey)
        {
            // Method metadata carries the entry-point-specific fields while the normalized node kind remains the shared Method kind.
            GraphMetadata metadata = descriptor.Metadata;
            return new ArchitectureNode(
                snapshotStableKey,
                descriptor.MethodStableKey,
                NodeKind.Method,
                descriptor.DisplayName,
                descriptor.MethodIdentity,
                descriptor.MethodIdentity.ToUpperInvariant(),
                descriptor.Language,
                descriptor.ProjectStableKey,
                parentStableKey,
                descriptor.KnowledgeKind,
                null,
                null,
                descriptor.Confidence,
                UnknownStateFor(descriptor.UnknownReason),
                evidenceStableKey,
                metadata,
                FingerprintGenerator.ForNode(NodeKind.Method, descriptor.DisplayName, descriptor.MethodIdentity, descriptor.MethodIdentity.ToUpperInvariant(), descriptor.KnowledgeKind, metadata));
        }

        /// <summary>
        /// Creates a direct containment relationship for projected entry-point nodes.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the edge.</param>
        /// <param name="sourceStableKey">The stable key of the containing node.</param>
        /// <param name="targetStableKey">The stable key of the contained node.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the relationship.</param>
        /// <param name="descriptor">The entry-point descriptor that carries metadata and confidence values.</param>
        /// <param name="relationshipRole">The runtime-specific role of the containment relationship.</param>
        /// <returns>A direct <c>CONTAINS</c> relationship for entry-point navigation.</returns>
        private static ArchitectureEdge CreateContainsEdge(StableKey snapshotStableKey, StableKey sourceStableKey, StableKey targetStableKey, StableKey evidenceStableKey, EntryPointDescriptor descriptor, string relationshipRole)
        {
            // CONTAINS is the established graph relationship for project/type/method hierarchy; runtime role metadata marks why this edge was emitted by runtime extraction.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = descriptor.DetectionMode,
                ["relationshipRole"] = relationshipRole,
                ["runtimeClassification"] = RuntimeClassification,
                ["runtimeKind"] = RuntimeKind
            });
            return new ArchitectureEdge(
                snapshotStableKey,
                new StableKey($"edge://CONTAINS:{sourceStableKey.Value}->{targetStableKey.Value}"),
                EdgeKind.Contains,
                sourceStableKey,
                targetStableKey,
                isDirect: true,
                descriptor.KnowledgeKind,
                descriptor.Confidence,
                UnknownStateFor(descriptor.UnknownReason),
                evidenceStableKey,
                metadata,
                FingerprintGenerator.ForEdge(EdgeKind.Contains, sourceStableKey, targetStableKey, isDirect: true, descriptor.KnowledgeKind, metadata));
        }

        /// <summary>
        /// Creates a source-code evidence record for one console entry-point fact.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that receives the evidence.</param>
        /// <param name="descriptor">The entry-point descriptor that carries source location details.</param>
        /// <returns>A source-code evidence record for the entry-point declaration.</returns>
        private static EvidenceRecord CreateEvidenceRecord(StableKey snapshotStableKey, EntryPointDescriptor descriptor)
        {
            // Evidence uses repository-relative paths and bounded source previews so entry-point facts remain explainable and machine-independent.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = descriptor.DetectionMode,
                ["runtimeClassification"] = RuntimeClassification,
                ["runtimeKind"] = RuntimeKind
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
                UnknownStateFor(descriptor.UnknownReason),
                metadata,
                FingerprintGenerator.ForEvidence(EvidenceKind.SourceCode, descriptor.EvidenceFilePath, descriptor.EvidenceStartLine, descriptor.EvidenceEndLine, descriptor.SymbolName, descriptor.KnowledgeKind, metadata));
        }

        /// <summary>
        /// Creates entry-point metadata using stable lower-camel-case field names.
        /// </summary>
        /// <param name="detectionMode">The detection mode used for the entry-point fact.</param>
        /// <param name="confidenceReason">The confidence explanation for the entry-point fact.</param>
        /// <param name="methodIdentity">The deterministic method identity or top-level file identity.</param>
        /// <param name="containingTypeName">The optional containing type name for explicit entry-point methods.</param>
        /// <param name="topLevelFilePath">The normalized top-level-statement file path when applicable.</param>
        /// <param name="unknownReason">The optional unknown reason for ambiguous entry-point candidates.</param>
        /// <param name="includeMethodIdentity">Whether to include method identity in the metadata payload.</param>
        /// <returns>Canonical graph metadata for entry-point nodes.</returns>
        private static GraphMetadata CreateEntryPointMetadata(string detectionMode, string confidenceReason, string methodIdentity, string? containingTypeName, string? topLevelFilePath, string? unknownReason, bool includeMethodIdentity = true)
        {
            // Optional values are omitted unless evidence supports them, and unknown reasons are duplicated into metadata for consumers that inspect metadata only.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["confidenceReason"] = unknownReason ?? confidenceReason,
                ["detectionMode"] = detectionMode,
                ["runtimeClassification"] = RuntimeClassification,
                ["runtimeKind"] = RuntimeKind
            };
            if (includeMethodIdentity)
            {
                values["entryPointSymbol"] = methodIdentity;
            }

            AddOptional(values, "handlerSymbol", methodIdentity);
            AddOptional(values, "containingType", containingTypeName);
            AddOptional(values, "topLevelStatementFilePath", topLevelFilePath);
            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Selects the unknown-state value for a descriptor.
        /// </summary>
        /// <param name="unknownReason">The optional unknown reason.</param>
        /// <returns>The graph unknown-state value for the descriptor.</returns>
        private static UnknownState UnknownStateFor(string? unknownReason)
        {
            // Ambiguous projects remain queryable facts while clearly marked as incomplete runtime certainty.
            return unknownReason is null ? UnknownState.Known : UnknownState.Unknown(unknownReason);
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
        /// Builds a fallback identity for a C# Main method when Roslyn cannot provide a method symbol.
        /// </summary>
        /// <param name="method">The C# method declaration to identify.</param>
        /// <param name="repositoryRelativeDocumentPath">The repository-relative source document path.</param>
        /// <param name="cancellationToken">A token that stops source location access.</param>
        /// <returns>A deterministic fallback method identity.</returns>
        private static string BuildCSharpMethodFallbackIdentity(MethodDeclarationSyntax method, string repositoryRelativeDocumentPath, CancellationToken cancellationToken)
        {
            // Source-coordinate fallback keeps incomplete compilations deterministic without using absolute paths.
            FileLinePositionSpan lineSpan = method.SyntaxTree.GetLineSpan(method.Identifier.Span, cancellationToken);
            return $"{GetContainingCSharpTypeName(method)}.Main@{repositoryRelativeDocumentPath}:{lineSpan.StartLinePosition.Line + 1}:{lineSpan.StartLinePosition.Character + 1}";
        }

        /// <summary>
        /// Builds a fallback identity for a VB.NET Main method when Roslyn cannot provide a method symbol.
        /// </summary>
        /// <param name="statement">The VB.NET method statement to identify.</param>
        /// <param name="repositoryRelativeDocumentPath">The repository-relative source document path.</param>
        /// <param name="cancellationToken">A token that stops source location access.</param>
        /// <returns>A deterministic fallback method identity.</returns>
        private static string BuildVisualBasicMethodFallbackIdentity(MethodStatementSyntax statement, string repositoryRelativeDocumentPath, CancellationToken cancellationToken)
        {
            // Source-coordinate fallback keeps incomplete VB compilations deterministic without using absolute paths.
            FileLinePositionSpan lineSpan = statement.SyntaxTree.GetLineSpan(statement.Identifier.Span, cancellationToken);
            return $"{GetContainingVisualBasicTypeName(statement) ?? "Module"}.Main@{repositoryRelativeDocumentPath}:{lineSpan.StartLinePosition.Line + 1}:{lineSpan.StartLinePosition.Character + 1}";
        }

        /// <summary>
        /// Gets the containing C# type name for a method declaration.
        /// </summary>
        /// <param name="method">The C# method declaration to inspect.</param>
        /// <returns>The containing type source name when present; otherwise, a fallback program type name.</returns>
        private static string GetContainingCSharpTypeName(MethodDeclarationSyntax method)
        {
            // Containing type fallback is used only when compiler binding is unavailable.
            return method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "Program";
        }

        /// <summary>
        /// Gets the containing VB.NET type or module name for a method statement.
        /// </summary>
        /// <param name="statement">The VB.NET method statement to inspect.</param>
        /// <returns>The containing type or module source name when present; otherwise, <see langword="null" />.</returns>
        private static string? GetContainingVisualBasicTypeName(MethodStatementSyntax statement)
        {
            // VB entry points commonly live in modules, but classes and structures are also supported by source ancestry.
            return statement.Ancestors().OfType<TypeBlockSyntax>().FirstOrDefault()?.BlockStatement.Identifier.ValueText
                ?? statement.Ancestors().OfType<ModuleBlockSyntax>().FirstOrDefault()?.ModuleStatement.Identifier.ValueText;
        }

        /// <summary>
        /// Creates a deterministic type stable key scoped by project and qualified type identity.
        /// </summary>
        /// <param name="projectStableKey">The stable key of the declaring project.</param>
        /// <param name="typeIdentity">The type identity to include in stable-key material.</param>
        /// <returns>A deterministic type stable key.</returns>
        private static StableKey CreateTypeStableKey(StableKey projectStableKey, string typeIdentity)
        {
            // Type identity follows the shared project-plus-symbol pattern without depending on database IDs or machine paths.
            return new StableKey($"type://{CreateSha256Hash(projectStableKey.Value + "|" + typeIdentity)}");
        }

        /// <summary>
        /// Creates a deterministic method stable key for an entry point.
        /// </summary>
        /// <param name="projectStableKey">The stable key of the declaring project.</param>
        /// <param name="methodIdentity">The method identity to include in stable-key material.</param>
        /// <param name="topLevelFilePath">The normalized top-level-statement file path when applicable.</param>
        /// <returns>A deterministic method stable key.</returns>
        private static StableKey CreateEntryPointStableKey(StableKey projectStableKey, string methodIdentity, string? topLevelFilePath)
        {
            // Entry-point identity uses project plus method identity, or project plus normalized top-level-statement file path for implicit Main.
            string keyMaterial = topLevelFilePath is null ? $"{projectStableKey.Value}|{methodIdentity}" : $"{projectStableKey.Value}|{topLevelFilePath}";
            return new StableKey($"method://console-entry-point/{CreateSha256Hash(keyMaterial)}");
        }

        /// <summary>
        /// Creates a deterministic evidence stable key from project identity and source line span.
        /// </summary>
        /// <param name="projectStableKey">The stable key of the project that owns the source document.</param>
        /// <param name="repositoryRelativeDocumentPath">The repository-relative source document path.</param>
        /// <param name="startLine">The one-based starting line of the source fact.</param>
        /// <param name="endLine">The one-based ending line of the source fact.</param>
        /// <param name="symbolName">The source symbol name.</param>
        /// <returns>A deterministic evidence stable key.</returns>
        private static StableKey CreateEvidenceStableKey(StableKey projectStableKey, string repositoryRelativeDocumentPath, int startLine, int endLine, string symbolName)
        {
            // Evidence identity uses source span and symbol text because multiple candidate entry points can exist in the same file.
            string keyMaterial = $"{projectStableKey.Value}|{repositoryRelativeDocumentPath}|{startLine}|{endLine}|{symbolName}";
            return new StableKey($"evidence://console-entry-point/{CreateSha256Hash(keyMaterial)}");
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
            // Repository-relative evidence paths keep entry-point facts deterministic across developer machines.
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
        /// Carries normalized console entry-point, evidence, and stable-key values shared by graph projection methods.
        /// </summary>
        /// <param name="ProjectStableKey">The stable key of the project that declares the entry point.</param>
        /// <param name="ProjectDisplayName">The display name for the declaring project.</param>
        /// <param name="EvidenceFilePath">The repository-relative evidence file path.</param>
        /// <param name="MethodStableKey">The stable key of the entry-point method node.</param>
        /// <param name="TypeStableKey">The optional stable key of the containing type node.</param>
        /// <param name="DisplayName">The method node display name.</param>
        /// <param name="MethodIdentity">The deterministic method or top-level file identity.</param>
        /// <param name="ContainingTypeName">The optional containing type name for explicit Main methods.</param>
        /// <param name="Language">The source language associated with the entry point.</param>
        /// <param name="DetectionMode">The detection mode used for metadata.</param>
        /// <param name="ConfidenceReason">The confidence explanation used for metadata.</param>
        /// <param name="SymbolName">The source symbol name for evidence.</param>
        /// <param name="ContainingSymbol">The containing symbol name for evidence.</param>
        /// <param name="EvidenceStartLine">The one-based evidence start line.</param>
        /// <param name="EvidenceEndLine">The one-based evidence end line.</param>
        /// <param name="EvidenceStableKey">The stable key of the source evidence record.</param>
        /// <param name="SnippetPreview">The bounded source snippet preview.</param>
        /// <param name="SnippetHash">The deterministic snippet hash.</param>
        /// <param name="TopLevelFilePath">The normalized top-level-statement file path when applicable.</param>
        /// <param name="KnowledgeKind">The graph knowledge kind for the fact.</param>
        /// <param name="Confidence">The graph confidence for the fact.</param>
        /// <param name="UnknownReason">The unknown reason when entry-point certainty is ambiguous.</param>
        /// <param name="Metadata">The method graph metadata.</param>
        private sealed record EntryPointDescriptor(StableKey ProjectStableKey, string ProjectDisplayName, string EvidenceFilePath, StableKey MethodStableKey, StableKey? TypeStableKey, string DisplayName, string MethodIdentity, string? ContainingTypeName, string Language, string DetectionMode, string ConfidenceReason, string SymbolName, string ContainingSymbol, int EvidenceStartLine, int EvidenceEndLine, StableKey EvidenceStableKey, string SnippetPreview, string SnippetHash, string? TopLevelFilePath, KnowledgeKind KnowledgeKind, Confidence Confidence, string? UnknownReason, GraphMetadata Metadata)
        {
            /// <summary>
            /// Gets a deterministic sort key used when projecting ambiguous project candidates.
            /// </summary>
            public string StableSortKey => MethodStableKey.Value;

            /// <summary>
            /// Creates a copy of the descriptor with explicit unknown-state metadata for ambiguous entry-point candidates.
            /// </summary>
            /// <param name="unknownReason">The unknown reason to attach to the descriptor.</param>
            /// <returns>A descriptor copy with medium confidence and unknown-state metadata.</returns>
            public EntryPointDescriptor WithUnknown(string unknownReason)
            {
                // Ambiguity changes confidence and unknown metadata but not stable identity or evidence coordinates.
                GraphMetadata metadata = CreateEntryPointMetadata(DetectionMode, ConfidenceReason, MethodIdentity, ContainingTypeName, TopLevelFilePath, unknownReason);
                return this with
                {
                    Confidence = Confidence.Medium,
                    KnowledgeKind = KnowledgeKind.Unknown,
                    UnknownReason = unknownReason,
                    Metadata = metadata
                };
            }
        }
    }
}
