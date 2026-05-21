using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Archon.Roslyn.CSharp
{
    /// <summary>
    /// Extracts graph-ready declaration facts from a C# Roslyn semantic model.
    /// </summary>
    /// <remarks>
    /// This extractor implements the first WP006 vertical slice. It uses Roslyn symbols for namespaces, types, constructors, methods, properties, and fields, then emits language-neutral declaration and containment facts with deterministic evidence.
    /// </remarks>
    public sealed class CSharpSemanticDocumentExtractor : ISemanticDocumentExtractor
    {
        /// <summary>
        /// Extracts declaration and containment facts from one C# semantic document request.
        /// </summary>
        /// <param name="request">The document, semantic model, and repository context to analyze.</param>
        /// <param name="cancellationToken">A token that signals when extraction should stop before additional semantic work is performed.</param>
        /// <returns>The semantic extraction result containing C# declaration facts, containment relationship facts, and diagnostics.</returns>
        public SemanticExtractionResult Extract(SemanticExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // The extractor walks syntax declarations but resolves every emitted declaration through Roslyn symbols before creating graph facts.
            ArgumentNullException.ThrowIfNull(request);
            SyntaxNode root = request.SyntaxTree.GetRoot(cancellationToken);
            CSharpDeclarationCollector collector = new(request, cancellationToken);
            collector.Visit(root);
            return collector.ToResult();
        }

        /// <summary>
        /// Visits supported C# declaration syntax and records semantic declaration facts.
        /// </summary>
        /// <remarks>
        /// The collector is intentionally private to the extractor because it carries per-document state, duplicate tracking, and parent-stack context during one traversal.
        /// </remarks>
        private sealed class CSharpDeclarationCollector : CSharpSyntaxWalker
        {
            /// <summary>
            /// Stores the extraction request that supplies Roslyn and repository context.
            /// </summary>
            private readonly SemanticExtractionRequest _request;

            /// <summary>
            /// Stores the cancellation token checked between declaration visits.
            /// </summary>
            private readonly CancellationToken _cancellationToken;

            /// <summary>
            /// Stores declaration facts by stable key so repeated partial or nested syntax visits de-duplicate deterministically.
            /// </summary>
            private readonly Dictionary<string, SemanticDeclarationFact> _declarations = new(StringComparer.Ordinal);

            /// <summary>
            /// Stores containment relationship facts by stable key for deterministic duplicate removal.
            /// </summary>
            private readonly Dictionary<string, SemanticRelationshipFact> _relationships = new(StringComparer.Ordinal);

            /// <summary>
            /// Stores non-fatal warnings encountered while resolving symbols.
            /// </summary>
            private readonly List<string> _warnings = [];

            /// <summary>
            /// Stores the current declaration ancestry as stable keys while the syntax walker descends.
            /// </summary>
            private readonly Stack<string> _parentStableKeys = new();

            /// <summary>
            /// Initializes a new declaration collector for one C# document.
            /// </summary>
            /// <param name="request">The semantic extraction request being processed.</param>
            /// <param name="cancellationToken">A token that signals when traversal should stop.</param>
            public CSharpDeclarationCollector(SemanticExtractionRequest request, CancellationToken cancellationToken)
                : base(SyntaxWalkerDepth.Node)
            {
                // The collector is per-document state and must not be reused across requests because parent stacks and duplicate dictionaries are mutable.
                _request = request;
                _cancellationToken = cancellationToken;
            }

            /// <summary>
            /// Visits a namespace declaration and records the namespace before visiting child declarations.
            /// </summary>
            /// <param name="node">The block-scoped namespace declaration syntax.</param>
            public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
            {
                // Namespace declarations establish the parent for contained types and nested namespaces.
                VisitDeclarationWithChildren(node, SemanticDeclarationKind.Namespace, node.Name.ToString(), static (semanticModel, declaration, cancellationToken) => semanticModel.GetDeclaredSymbol((NamespaceDeclarationSyntax)declaration, cancellationToken));
            }

            /// <summary>
            /// Visits a file-scoped namespace declaration and records the namespace before visiting child declarations.
            /// </summary>
            /// <param name="node">The file-scoped namespace declaration syntax.</param>
            public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
            {
                // File-scoped namespaces have the same graph meaning as block-scoped namespaces even though their syntax shape is different.
                VisitDeclarationWithChildren(node, SemanticDeclarationKind.Namespace, node.Name.ToString(), static (semanticModel, declaration, cancellationToken) => semanticModel.GetDeclaredSymbol((FileScopedNamespaceDeclarationSyntax)declaration, cancellationToken));
            }

            /// <summary>
            /// Visits a class declaration and records the type before visiting members.
            /// </summary>
            /// <param name="node">The class declaration syntax.</param>
            public override void VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                // Types are resolved through the semantic model so the fact uses compiler-qualified identity rather than textual names alone.
                VisitDeclarationWithChildren(node, SemanticDeclarationKind.Type, node.Identifier.ValueText, static (semanticModel, declaration, cancellationToken) => semanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)declaration, cancellationToken));
            }

            /// <summary>
            /// Visits a record declaration and records the type before visiting members.
            /// </summary>
            /// <param name="node">The record declaration syntax.</param>
            public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
            {
                // Records are type declarations and participate in the same graph node kind as classes and structs.
                VisitDeclarationWithChildren(node, SemanticDeclarationKind.Type, node.Identifier.ValueText, static (semanticModel, declaration, cancellationToken) => semanticModel.GetDeclaredSymbol((RecordDeclarationSyntax)declaration, cancellationToken));
            }

            /// <summary>
            /// Visits a struct declaration and records the type before visiting members.
            /// </summary>
            /// <param name="node">The struct declaration syntax.</param>
            public override void VisitStructDeclaration(StructDeclarationSyntax node)
            {
                // Structs use named-type symbols and therefore share the type declaration path.
                VisitDeclarationWithChildren(node, SemanticDeclarationKind.Type, node.Identifier.ValueText, static (semanticModel, declaration, cancellationToken) => semanticModel.GetDeclaredSymbol((StructDeclarationSyntax)declaration, cancellationToken));
            }

            /// <summary>
            /// Visits an interface declaration and records the type before visiting members.
            /// </summary>
            /// <param name="node">The interface declaration syntax.</param>
            public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
            {
                // Interfaces are emitted as type nodes so later implementation relationships can target the same graph vocabulary.
                VisitDeclarationWithChildren(node, SemanticDeclarationKind.Type, node.Identifier.ValueText, static (semanticModel, declaration, cancellationToken) => semanticModel.GetDeclaredSymbol((InterfaceDeclarationSyntax)declaration, cancellationToken));
            }

            /// <summary>
            /// Visits an enum declaration and records the type before visiting members.
            /// </summary>
            /// <param name="node">The enum declaration syntax.</param>
            public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
            {
                // Enums are source-declared types and are included in the minimal type extraction path.
                VisitDeclarationWithChildren(node, SemanticDeclarationKind.Type, node.Identifier.ValueText, static (semanticModel, declaration, cancellationToken) => semanticModel.GetDeclaredSymbol((EnumDeclarationSyntax)declaration, cancellationToken));
            }

            /// <summary>
            /// Visits a delegate declaration and records it as a type declaration.
            /// </summary>
            /// <param name="node">The delegate declaration syntax.</param>
            public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
            {
                // Delegates have named-type symbols even though their declaration syntax does not contain child member declarations.
                VisitLeafDeclaration(node, SemanticDeclarationKind.Type, node.Identifier.ValueText, static (semanticModel, declaration, cancellationToken) => semanticModel.GetDeclaredSymbol((DelegateDeclarationSyntax)declaration, cancellationToken));
            }

            /// <summary>
            /// Visits a constructor declaration and records it as a method declaration.
            /// </summary>
            /// <param name="node">The constructor declaration syntax.</param>
            public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
            {
                // Constructors are represented as method graph nodes so callers can reason about object creation and injection in later slices.
                VisitLeafDeclaration(node, SemanticDeclarationKind.Method, node.Identifier.ValueText, static (semanticModel, declaration, cancellationToken) => semanticModel.GetDeclaredSymbol((ConstructorDeclarationSyntax)declaration, cancellationToken));
            }

            /// <summary>
            /// Visits a method declaration and records it as a method declaration.
            /// </summary>
            /// <param name="node">The method declaration syntax.</param>
            public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                // Methods are leaf declarations for this slice because local functions and invocation dependencies are assigned to later WP006 work.
                VisitLeafDeclaration(node, SemanticDeclarationKind.Method, node.Identifier.ValueText, static (semanticModel, declaration, cancellationToken) => semanticModel.GetDeclaredSymbol((MethodDeclarationSyntax)declaration, cancellationToken));
            }

            /// <summary>
            /// Visits a property declaration and records it as a property declaration.
            /// </summary>
            /// <param name="node">The property declaration syntax.</param>
            public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
            {
                // Properties are graph nodes in their own right because later dependency slices may attach accessor evidence to them.
                VisitLeafDeclaration(node, SemanticDeclarationKind.Property, node.Identifier.ValueText, static (semanticModel, declaration, cancellationToken) => semanticModel.GetDeclaredSymbol((PropertyDeclarationSyntax)declaration, cancellationToken));
            }

            /// <summary>
            /// Visits a field declaration and records each declared field variable.
            /// </summary>
            /// <param name="node">The field declaration syntax.</param>
            public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                // A single field declaration can declare multiple variables, and Roslyn exposes one field symbol for each variable.
                _cancellationToken.ThrowIfCancellationRequested();
                foreach (VariableDeclaratorSyntax variable in node.Declaration.Variables)
                {
                    RecordLeafDeclaration(variable, SemanticDeclarationKind.Field, variable.Identifier.ValueText, _request.SemanticModel.GetDeclaredSymbol(variable, _cancellationToken));
                }
            }

            /// <summary>
            /// Creates the immutable extraction result after traversal has completed.
            /// </summary>
            /// <returns>The semantic extraction result ordered by stable key for deterministic assertions and accumulation.</returns>
            public SemanticExtractionResult ToResult()
            {
                // Ordering by stable key keeps output stable even if Roslyn changes child collection ordering for equivalent code.
                return new SemanticExtractionResult(
                    _declarations.Values.OrderBy(declaration => declaration.StableKey, StringComparer.Ordinal),
                    _relationships.Values.OrderBy(relationship => relationship.StableKey, StringComparer.Ordinal),
                    _warnings,
                    errors: null);
            }

            /// <summary>
            /// Records a declaration, pushes it as parent context, and visits child declarations.
            /// </summary>
            /// <param name="node">The declaration syntax node being processed.</param>
            /// <param name="declarationKind">The semantic declaration kind to emit.</param>
            /// <param name="fallbackSymbolName">The fallback symbol name to use if Roslyn cannot resolve the declaration.</param>
            /// <param name="resolveSymbol">The Roslyn symbol resolver for the declaration syntax node.</param>
            private void VisitDeclarationWithChildren<TNode>(
                TNode node,
                SemanticDeclarationKind declarationKind,
                string fallbackSymbolName,
                Func<Microsoft.CodeAnalysis.SemanticModel, SyntaxNode, CancellationToken, ISymbol?> resolveSymbol)
                where TNode : SyntaxNode
            {
                // Parent context is pushed only when a declaration fact is successfully created, preventing unresolved symbols from corrupting hierarchy.
                _cancellationToken.ThrowIfCancellationRequested();
                SemanticDeclarationFact? fact = RecordDeclaration(node, declarationKind, fallbackSymbolName, resolveSymbol(_request.SemanticModel, node, _cancellationToken));
                if (fact is null)
                {
                    VisitChildNodes(node);
                    return;
                }

                _parentStableKeys.Push(fact.StableKey);
                VisitChildNodes(node);
                _parentStableKeys.Pop();
            }

            /// <summary>
            /// Visits child nodes without redispatching the current declaration node to the same override.
            /// </summary>
            /// <param name="node">The syntax node whose children should be visited.</param>
            private void VisitChildNodes(SyntaxNode node)
            {
                // CSharpSyntaxWalker.Visit would revisit the current node and recurse; iterating children advances traversal safely.
                foreach (SyntaxNode childNode in node.ChildNodes())
                {
                    Visit(childNode);
                }
            }

            /// <summary>
            /// Records a declaration that does not need to push parent context for child traversal.
            /// </summary>
            /// <param name="node">The declaration syntax node being processed.</param>
            /// <param name="declarationKind">The semantic declaration kind to emit.</param>
            /// <param name="fallbackSymbolName">The fallback symbol name to use if Roslyn cannot resolve the declaration.</param>
            /// <param name="resolveSymbol">The Roslyn symbol resolver for the declaration syntax node.</param>
            private void VisitLeafDeclaration<TNode>(
                TNode node,
                SemanticDeclarationKind declarationKind,
                string fallbackSymbolName,
                Func<Microsoft.CodeAnalysis.SemanticModel, SyntaxNode, CancellationToken, ISymbol?> resolveSymbol)
                where TNode : SyntaxNode
            {
                // Leaf declarations still call RecordDeclaration so evidence and containment behavior is identical to parent declarations.
                _cancellationToken.ThrowIfCancellationRequested();
                _ = RecordDeclaration(node, declarationKind, fallbackSymbolName, resolveSymbol(_request.SemanticModel, node, _cancellationToken));
            }

            /// <summary>
            /// Records a leaf declaration from a variable declarator and Roslyn symbol.
            /// </summary>
            /// <param name="node">The variable declarator syntax node being processed.</param>
            /// <param name="declarationKind">The semantic declaration kind to emit.</param>
            /// <param name="fallbackSymbolName">The fallback symbol name to use if Roslyn cannot resolve the declaration.</param>
            /// <param name="symbol">The Roslyn symbol resolved for the declaration.</param>
            private void RecordLeafDeclaration(SyntaxNode node, SemanticDeclarationKind declarationKind, string fallbackSymbolName, ISymbol? symbol)
            {
                // This wrapper gives multi-variable field declarations the same validation and containment behavior as other leaf members.
                _ = RecordDeclaration(node, declarationKind, fallbackSymbolName, symbol);
            }

            /// <summary>
            /// Records a semantic declaration fact and any direct containment relationship.
            /// </summary>
            /// <param name="node">The syntax node that produced the declaration.</param>
            /// <param name="declarationKind">The semantic declaration kind to emit.</param>
            /// <param name="fallbackSymbolName">The fallback symbol name to use when diagnostics are emitted.</param>
            /// <param name="symbol">The Roslyn symbol resolved for the declaration.</param>
            /// <returns>The recorded declaration fact, or <see langword="null" /> when Roslyn did not resolve a symbol.</returns>
            private SemanticDeclarationFact? RecordDeclaration(SyntaxNode node, SemanticDeclarationKind declarationKind, string fallbackSymbolName, ISymbol? symbol)
            {
                // Work Item 1 avoids text-only discovery when symbols are unavailable; unresolved declarations become warnings rather than invented facts.
                if (symbol is null)
                {
                    _warnings.Add($"C# declaration '{fallbackSymbolName}' could not be resolved by Roslyn and was not emitted as a semantic fact.");
                    return null;
                }

                SemanticSymbolIdentity symbolIdentity = CreateSymbolIdentity(symbol, declarationKind);
                string stableKey = SemanticStableKeyBuilder.ForDeclaration(declarationKind, SourceLanguage.CSharp, _request.ProjectContext, symbolIdentity);
                string? parentStableKey = _parentStableKeys.Count == 0 ? null : _parentStableKeys.Peek();
                SemanticEvidence evidence = CreateEvidence(node, symbolIdentity);
                SemanticDeclarationFact fact = new(stableKey, declarationKind, SourceLanguage.CSharp, symbolIdentity, _request.ProjectContext, parentStableKey, evidence);
                _declarations[stableKey] = fact;

                if (parentStableKey is not null)
                {
                    string relationshipStableKey = SemanticStableKeyBuilder.ForRelationship(SemanticRelationshipKind.Contains, parentStableKey, stableKey);
                    _relationships[relationshipStableKey] = new SemanticRelationshipFact(relationshipStableKey, SemanticRelationshipKind.Contains, parentStableKey, stableKey, evidence);
                }

                return fact;
            }

            /// <summary>
            /// Creates a semantic symbol identity from a compiler symbol.
            /// </summary>
            /// <param name="symbol">The Roslyn symbol to project.</param>
            /// <param name="declarationKind">The declaration kind being emitted for the symbol.</param>
            /// <returns>A deterministic semantic symbol identity.</returns>
            private static SemanticSymbolIdentity CreateSymbolIdentity(ISymbol symbol, SemanticDeclarationKind declarationKind)
            {
                // Fully qualified names come from Roslyn display formatting so overload and containing-type information is compiler-derived.
                string fullyQualifiedName = declarationKind == SemanticDeclarationKind.Namespace
                    ? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal)
                    : symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                string metadataName = symbol is IMethodSymbol methodSymbol
                    ? methodSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                    : symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                string displayName = symbol.Kind == SymbolKind.Method && symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } method
                    ? method.ContainingType.Name
                    : symbol.Name;
                string? containingSymbol = symbol.ContainingSymbol is null or INamespaceSymbol { IsGlobalNamespace: true }
                    ? null
                    : symbol.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

                return new SemanticSymbolIdentity(metadataName, displayName, fullyQualifiedName, containingSymbol);
            }

            /// <summary>
            /// Creates source evidence for a declaration syntax node and semantic symbol identity.
            /// </summary>
            /// <param name="node">The syntax node that produced the declaration.</param>
            /// <param name="symbolIdentity">The semantic symbol identity associated with the node.</param>
            /// <returns>A semantic evidence record containing source path, line span, symbol context, preview, and hash.</returns>
            private SemanticEvidence CreateEvidence(SyntaxNode node, SemanticSymbolIdentity symbolIdentity)
            {
                // Evidence uses one-based line and column positions because they are intended for developer-facing navigation.
                FileLinePositionSpan lineSpan = _request.SyntaxTree.GetLineSpan(node.Span, _cancellationToken);
                LinePosition start = lineSpan.StartLinePosition;
                LinePosition end = lineSpan.EndLinePosition;
                (string? preview, string? hash) = SemanticSnippetBuilder.CreateSnippet(_request.SyntaxTree, node.Span, _cancellationToken);
                string repositoryRelativePath = SemanticPathNormalizer.ToRepositoryRelativePath(_request.RepositoryRootDirectory, _request.DocumentPath);

                return new SemanticEvidence(
                    repositoryRelativePath,
                    start.Line + 1,
                    end.Line + 1,
                    start.Character + 1,
                    end.Character + 1,
                    symbolIdentity.DisplayName,
                    symbolIdentity.ContainingSymbolName,
                    preview,
                    hash);
            }
        }
    }
}
