using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Archon.Roslyn.CSharp
{
    /// <summary>
    /// Extracts graph-ready declaration, relationship, and dependency facts from a C# Roslyn semantic model.
    /// </summary>
    /// <remarks>
    /// This extractor uses Roslyn symbols for declarations and compiler-resolved symbol relationships, then emits language-neutral facts with deterministic evidence and confidence metadata.
    /// </remarks>
    public sealed class CSharpSemanticDocumentExtractor : ISemanticDocumentExtractor
    {
        /// <summary>
        /// Extracts declaration, containment, and relationship facts from one C# semantic document request.
        /// </summary>
        /// <param name="request">The document, semantic model, and repository context to analyze.</param>
        /// <param name="cancellationToken">A token that signals when extraction should stop before additional semantic work is performed.</param>
        /// <returns>The semantic extraction result containing C# declaration facts, relationship facts, and diagnostics.</returns>
        public SemanticExtractionResult Extract(SemanticExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // The extractor walks syntax once and resolves emitted facts through Roslyn symbols before creating graph-ready output.
            ArgumentNullException.ThrowIfNull(request);
            SyntaxNode root = request.SyntaxTree.GetRoot(cancellationToken);
            CSharpDeclarationCollector collector = new(request, cancellationToken);
            collector.Visit(root);
            return collector.ToResult();
        }

        /// <summary>
        /// Visits supported C# syntax and records semantic declaration, relationship, and dependency facts.
        /// </summary>
        /// <remarks>
        /// The collector is intentionally private to the extractor because it carries per-document state, duplicate tracking, source-member context, and parent-stack context during one traversal.
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
            /// Stores declaration facts by Roslyn symbol display identity for source-symbol endpoint lookup.
            /// </summary>
            private readonly Dictionary<string, SemanticDeclarationFact> _declarationsBySymbolKey = new(StringComparer.Ordinal);

            /// <summary>
            /// Stores compiler diagnostics that describe degraded semantic model quality for this document.
            /// </summary>
            private readonly List<SemanticDiagnosticFact> _diagnostics = [];

            /// <summary>
            /// Stores unknown semantic outcomes by stable key so repeated unresolved syntax sites de-duplicate deterministically.
            /// </summary>
            private readonly Dictionary<string, SemanticUnknownFact> _unknowns = new(StringComparer.Ordinal);

            /// <summary>
            /// Stores additional source evidence spans that contributed to merged declarations, partial symbols, or generated facts.
            /// </summary>
            private readonly List<SemanticEvidenceContribution> _evidenceContributions = [];

            /// <summary>
            /// Stores whether the current syntax tree is classified as generated source.
            /// </summary>
            private readonly bool _isGenerated;

            /// <summary>
            /// Stores the current declaration ancestry as declaration facts while the syntax walker descends.
            /// </summary>
            private readonly Stack<SemanticDeclarationFact> _parentFacts = new();

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
                _isGenerated = SemanticGeneratedCodeDetector.IsGenerated(request.SyntaxTree, cancellationToken);
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
                VisitMemberDeclarationWithBody(node, SemanticDeclarationKind.Method, node.Identifier.ValueText, static (semanticModel, declaration, cancellationToken) => semanticModel.GetDeclaredSymbol((ConstructorDeclarationSyntax)declaration, cancellationToken));
            }

            /// <summary>
            /// Visits a method declaration and records it as a method declaration.
            /// </summary>
            /// <param name="node">The method declaration syntax.</param>
            public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                // Methods are pushed as source-member context while their bodies are visited so invocation and member-access facts can use the method as the relationship source.
                VisitMemberDeclarationWithBody(node, SemanticDeclarationKind.Method, node.Identifier.ValueText, static (semanticModel, declaration, cancellationToken) => semanticModel.GetDeclaredSymbol((MethodDeclarationSyntax)declaration, cancellationToken));
            }

            /// <summary>
            /// Visits a property declaration and records it as a property declaration.
            /// </summary>
            /// <param name="node">The property declaration syntax.</param>
            public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
            {
                // Properties are graph nodes in their own right because later dependency slices may attach accessor evidence to them.
                VisitMemberDeclarationWithBody(node, SemanticDeclarationKind.Property, node.Identifier.ValueText, static (semanticModel, declaration, cancellationToken) => semanticModel.GetDeclaredSymbol((PropertyDeclarationSyntax)declaration, cancellationToken));
            }

            /// <summary>
            /// Visits an invocation expression and records a compiler-resolved call relationship when the source member and target method are known.
            /// </summary>
            /// <param name="node">The invocation expression syntax to resolve.</param>
            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                // Invocation expressions are relationship evidence sites; target symbols are resolved with GetSymbolInfo so extension methods and overloads use compiler binding.
                _cancellationToken.ThrowIfCancellationRequested();
                SymbolInfo symbolInfo = _request.SemanticModel.GetSymbolInfo(node, _cancellationToken);
                IMethodSymbol? targetMethod = symbolInfo.Symbol as IMethodSymbol;
                if (targetMethod is not null)
                {
                    RecordRelationshipFromCurrentMember(node, SemanticRelationshipKind.Calls, targetMethod.ReducedFrom ?? targetMethod, "Invocation");
                    RecordDependencyFromCurrentMember(node, targetMethod.ContainingType, "InvocationTargetType");
                    RecordReflectionUnknownIfNeeded(node, targetMethod);
                }
                else
                {
                    RecordUnknownForUnresolvedSymbolInfo(node, symbolInfo, "Invocation", IsDynamicInvocation(node) ? SemanticUnknownReason.DynamicDispatch : null);
                }

                base.VisitInvocationExpression(node);
            }

            /// <summary>
            /// Visits an object creation expression and records constructor call and created-type dependency facts.
            /// </summary>
            /// <param name="node">The object creation expression syntax to resolve.</param>
            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                // Object creation produces both a CALLS edge to the constructor and a DEPENDS_ON edge to the constructed type.
                _cancellationToken.ThrowIfCancellationRequested();
                SymbolInfo symbolInfo = _request.SemanticModel.GetSymbolInfo(node, _cancellationToken);
                IMethodSymbol? constructor = symbolInfo.Symbol as IMethodSymbol;
                if (constructor is not null)
                {
                    RecordRelationshipFromCurrentMember(node, SemanticRelationshipKind.Calls, constructor, "ObjectCreationConstructor");
                    RecordDependencyFromCurrentMember(node, constructor.ContainingType, "ObjectCreation");
                }
                else
                {
                    RecordUnknownForUnresolvedSymbolInfo(node, symbolInfo, "ObjectCreation", preferredReason: null);
                }

                base.VisitObjectCreationExpression(node);
            }

            /// <summary>
            /// Visits an implicit object creation expression and records constructor call and created-type dependency facts.
            /// </summary>
            /// <param name="node">The implicit object creation expression syntax to resolve.</param>
            public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
            {
                // Target-typed new expressions bind to constructor symbols in the semantic model even when the type text is omitted.
                _cancellationToken.ThrowIfCancellationRequested();
                SymbolInfo symbolInfo = _request.SemanticModel.GetSymbolInfo(node, _cancellationToken);
                IMethodSymbol? constructor = symbolInfo.Symbol as IMethodSymbol;
                if (constructor is not null)
                {
                    RecordRelationshipFromCurrentMember(node, SemanticRelationshipKind.Calls, constructor, "ObjectCreationConstructor");
                    RecordDependencyFromCurrentMember(node, constructor.ContainingType, "ObjectCreation");
                }
                else
                {
                    RecordUnknownForUnresolvedSymbolInfo(node, symbolInfo, "ObjectCreation", preferredReason: null);
                }

                base.VisitImplicitObjectCreationExpression(node);
            }

            /// <summary>
            /// Visits a member access expression and records property access dependency facts when Roslyn resolves a property symbol.
            /// </summary>
            /// <param name="node">The member access expression syntax to resolve.</param>
            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                // Property access is modelled as a dependency because it can signal architectural coupling even when no method call occurs.
                _cancellationToken.ThrowIfCancellationRequested();
                SymbolInfo symbolInfo = _request.SemanticModel.GetSymbolInfo(node, _cancellationToken);
                ISymbol? symbol = symbolInfo.Symbol;
                if (symbol is IPropertySymbol propertySymbol)
                {
                    RecordRelationshipFromCurrentMember(node, SemanticRelationshipKind.DependsOn, propertySymbol, "PropertyAccess");
                    RecordDependencyFromCurrentMember(node, propertySymbol.Type, "PropertyType");
                }
                else if (symbol is null && node.Parent is not InvocationExpressionSyntax)
                {
                    RecordUnknownForUnresolvedSymbolInfo(node, symbolInfo, "MemberAccess", preferredReason: null);
                }

                base.VisitMemberAccessExpression(node);
            }

            /// <summary>
            /// Visits an identifier name and records property access dependencies for simple property references.
            /// </summary>
            /// <param name="node">The identifier name syntax to resolve.</param>
            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                // Simple property access such as "Name" is not always represented by a member-access expression, so identifiers receive a focused semantic check.
                _cancellationToken.ThrowIfCancellationRequested();
                ISymbol? symbol = _request.SemanticModel.GetSymbolInfo(node, _cancellationToken).Symbol;
                if (symbol is IPropertySymbol propertySymbol)
                {
                    RecordRelationshipFromCurrentMember(node, SemanticRelationshipKind.DependsOn, propertySymbol, "PropertyAccess");
                    RecordDependencyFromCurrentMember(node, propertySymbol.Type, "PropertyType");
                }

                base.VisitIdentifierName(node);
            }

            /// <summary>
            /// Visits a generic type-parameter constraint clause and records explicit constraint dependencies.
            /// </summary>
            /// <param name="node">The type-parameter constraint clause syntax to resolve.</param>
            public override void VisitTypeParameterConstraintClause(TypeParameterConstraintClauseSyntax node)
            {
                // Constraint clauses are visited with the owning type or method on the parent stack, which makes them reliable evidence for generic dependency facts.
                _cancellationToken.ThrowIfCancellationRequested();
                SemanticDeclarationFact? sourceFact = GetCurrentSourceFact();
                if (sourceFact is not null)
                {
                    foreach (TypeConstraintSyntax typeConstraint in node.Constraints.OfType<TypeConstraintSyntax>())
                    {
                        ITypeSymbol? constraintType = _request.SemanticModel.GetTypeInfo(typeConstraint.Type, _cancellationToken).Type;
                        RecordDependency(sourceFact, typeConstraint, constraintType, "GenericConstraint");
                    }
                }

                base.VisitTypeParameterConstraintClause(node);
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
                CaptureDiagnostics();
                return new SemanticExtractionResult(
                    _declarations.Values.OrderBy(declaration => declaration.StableKey, StringComparer.Ordinal),
                    _relationships.Values.OrderBy(relationship => relationship.StableKey, StringComparer.Ordinal),
                    _warnings,
                    errors: null,
                    _diagnostics.OrderBy(diagnostic => SemanticStableKeyBuilder.ForDiagnostic(diagnostic.DiagnosticId, diagnostic.Evidence), StringComparer.Ordinal),
                    _unknowns.Values.OrderBy(unknown => unknown.StableKey, StringComparer.Ordinal),
                    _evidenceContributions.OrderBy(contribution => contribution.FactStableKey, StringComparer.Ordinal).ThenBy(contribution => contribution.Evidence.StartLine));
            }

            /// <summary>
            /// Captures compiler diagnostics for the current syntax tree without blocking partial semantic extraction.
            /// </summary>
            private void CaptureDiagnostics()
            {
                // Diagnostics are captured at result time so all extraction output can still be produced before degraded compiler state is reported.
                if (_diagnostics.Count > 0)
                {
                    return;
                }

                foreach (Diagnostic diagnostic in _request.SemanticModel.Compilation.GetDiagnostics(_cancellationToken).Where(diagnostic => diagnostic.Location == Location.None || diagnostic.Location.SourceTree == _request.SyntaxTree))
                {
                    _diagnostics.Add(SemanticDiagnosticMapper.FromDiagnostic(diagnostic, _request, "CSharp", _cancellationToken));
                }
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
                ISymbol? symbol = resolveSymbol(_request.SemanticModel, node, _cancellationToken);
                SemanticDeclarationFact? fact = RecordDeclaration(node, declarationKind, fallbackSymbolName, symbol);
                if (fact is null)
                {
                    VisitChildNodes(node);
                    return;
                }

                RecordTypeRelationships(node, symbol, fact);
                RecordAttributeDependencies(node, symbol, fact, "Attribute");
                RecordSignatureDependencies(node, symbol, fact);
                _parentFacts.Push(fact);
                VisitChildNodes(node);
                _parentFacts.Pop();
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
                ISymbol? symbol = resolveSymbol(_request.SemanticModel, node, _cancellationToken);
                SemanticDeclarationFact? fact = RecordDeclaration(node, declarationKind, fallbackSymbolName, symbol);
                if (fact is not null)
                {
                    RecordAttributeDependencies(node, symbol, fact, "Attribute");
                    RecordSignatureDependencies(node, symbol, fact);
                }
            }

            /// <summary>
            /// Records a member declaration and visits its body while the member is the active relationship source.
            /// </summary>
            /// <param name="node">The member declaration syntax node being processed.</param>
            /// <param name="declarationKind">The semantic declaration kind to emit.</param>
            /// <param name="fallbackSymbolName">The fallback symbol name to use if Roslyn cannot resolve the declaration.</param>
            /// <param name="resolveSymbol">The Roslyn symbol resolver for the declaration syntax node.</param>
            private void VisitMemberDeclarationWithBody<TNode>(
                TNode node,
                SemanticDeclarationKind declarationKind,
                string fallbackSymbolName,
                Func<Microsoft.CodeAnalysis.SemanticModel, SyntaxNode, CancellationToken, ISymbol?> resolveSymbol)
                where TNode : SyntaxNode
            {
                // Member declarations become both graph nodes and relationship source contexts for executable syntax nested inside them.
                _cancellationToken.ThrowIfCancellationRequested();
                ISymbol? symbol = resolveSymbol(_request.SemanticModel, node, _cancellationToken);
                SemanticDeclarationFact? fact = RecordDeclaration(node, declarationKind, fallbackSymbolName, symbol);
                if (fact is null)
                {
                    VisitChildNodes(node);
                    return;
                }

                RecordAttributeDependencies(node, symbol, fact, "Attribute");
                RecordReturnAttributeDependencies(node, symbol, fact);
                RecordSignatureDependencies(node, symbol, fact);
                RecordConstructorInjectionDependencies(node, symbol, fact);
                _parentFacts.Push(fact);
                VisitChildNodes(node);
                _parentFacts.Pop();
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
                SemanticDeclarationFact? fact = RecordDeclaration(node, declarationKind, fallbackSymbolName, symbol);
                if (fact is not null)
                {
                    RecordAttributeDependencies(node, symbol, fact, "Attribute");
                    RecordSignatureDependencies(node, symbol, fact);
                }
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
                string? parentStableKey = _parentFacts.Count == 0 ? null : _parentFacts.Peek().StableKey;
                SemanticEvidence evidence = CreateEvidence(node, symbolIdentity);
                Dictionary<string, string> metadata = new(StringComparer.Ordinal)
                {
                    ["generated"] = _isGenerated ? "true" : "false",
                    ["sourceOrigin"] = _isGenerated ? "Generated" : "Source"
                };
                SemanticFactConfidence confidence = _isGenerated ? SemanticFactConfidence.Generated : SemanticFactConfidence.CompilerResolved;
                SemanticDeclarationFact fact = new(stableKey, declarationKind, SourceLanguage.CSharp, symbolIdentity, _request.ProjectContext, parentStableKey, evidence, confidence, metadata);
                _declarations[stableKey] = fact;
                _declarationsBySymbolKey[GetSymbolKey(symbol)] = fact;
                RecordEvidenceContribution(stableKey, evidence, symbol);

                if (parentStableKey is not null)
                {
                    string relationshipStableKey = SemanticStableKeyBuilder.ForRelationship(SemanticRelationshipKind.Contains, parentStableKey, stableKey);
                    _relationships[relationshipStableKey] = new SemanticRelationshipFact(relationshipStableKey, SemanticRelationshipKind.Contains, parentStableKey, stableKey, evidence);
                }

                return fact;
            }

            /// <summary>
            /// Records inheritance and implementation relationships for a type declaration.
            /// </summary>
            /// <param name="node">The type declaration syntax that provides relationship evidence.</param>
            /// <param name="symbol">The declared symbol associated with the type declaration.</param>
            /// <param name="fact">The declaration fact emitted for the type.</param>
            private void RecordTypeRelationships(SyntaxNode node, ISymbol? symbol, SemanticDeclarationFact fact)
            {
                // Type relationships are emitted from named type symbols because Roslyn normalizes base-list syntax, generic substitution, and interface implementation details.
                if (symbol is not INamedTypeSymbol namedType || fact.DeclarationKind != SemanticDeclarationKind.Type)
                {
                    return;
                }

                if (namedType.BaseType is not null && namedType.BaseType.SpecialType != SpecialType.System_Object)
                {
                    RecordRelationship(fact, node, SemanticRelationshipKind.Inherits, namedType.BaseType, "BaseType");
                    RecordDependency(fact, node, namedType.BaseType, "BaseType");
                }

                foreach (INamedTypeSymbol interfaceSymbol in namedType.Interfaces)
                {
                    RecordRelationship(fact, node, SemanticRelationshipKind.Implements, interfaceSymbol, "InterfaceImplementation");
                    RecordDependency(fact, node, interfaceSymbol, "InterfaceImplementation");
                }

                foreach (ITypeParameterSymbol typeParameter in namedType.TypeParameters)
                {
                    RecordGenericConstraintDependencies(fact, node, typeParameter);
                }
            }

            /// <summary>
            /// Records signature-level dependencies for methods, properties, fields, and generic declarations.
            /// </summary>
            /// <param name="node">The declaration syntax that provides dependency evidence.</param>
            /// <param name="symbol">The declared symbol whose signature should be inspected.</param>
            /// <param name="fact">The declaration fact emitted for the symbol.</param>
            private void RecordSignatureDependencies(SyntaxNode node, ISymbol? symbol, SemanticDeclarationFact fact)
            {
                // Signature dependencies preserve architecture-relevant type usage that may never appear in executable statements.
                switch (symbol)
                {
                    case IMethodSymbol methodSymbol:
                        RecordDependency(fact, node, methodSymbol.ReturnType, "ReturnType");
                        foreach (IParameterSymbol parameter in methodSymbol.Parameters)
                        {
                            RecordDependency(fact, node, parameter.Type, "ParameterType");
                            RecordAttributeDependencies(node, parameter, fact, "ParameterAttribute");
                        }

                        foreach (ITypeParameterSymbol typeParameter in methodSymbol.TypeParameters)
                        {
                            RecordAttributeDependencies(node, typeParameter, fact, "TypeParameterAttribute");
                            RecordGenericConstraintDependencies(fact, node, typeParameter);
                        }

                        if (methodSymbol.OverriddenMethod is not null)
                        {
                            RecordRelationship(fact, node, SemanticRelationshipKind.Inherits, methodSymbol.OverriddenMethod, "Override");
                        }

                        foreach (IMethodSymbol implementedMember in methodSymbol.ExplicitInterfaceImplementations)
                        {
                            RecordRelationship(fact, node, SemanticRelationshipKind.Implements, implementedMember, "ExplicitInterfaceMemberImplementation");
                        }

                        break;
                    case IPropertySymbol propertySymbol:
                        RecordDependency(fact, node, propertySymbol.Type, "PropertyType");
                        if (propertySymbol.OverriddenProperty is not null)
                        {
                            RecordRelationship(fact, node, SemanticRelationshipKind.Inherits, propertySymbol.OverriddenProperty, "Override");
                        }

                        foreach (IPropertySymbol implementedMember in propertySymbol.ExplicitInterfaceImplementations)
                        {
                            RecordRelationship(fact, node, SemanticRelationshipKind.Implements, implementedMember, "ExplicitInterfaceMemberImplementation");
                        }

                        break;
                    case IFieldSymbol fieldSymbol:
                        RecordDependency(fact, node, fieldSymbol.Type, "FieldType");
                        break;
                }
            }

            /// <summary>
            /// Records constructor injection relationships for constructor parameter types.
            /// </summary>
            /// <param name="node">The constructor declaration syntax that provides injection evidence.</param>
            /// <param name="symbol">The constructor symbol to inspect.</param>
            /// <param name="fact">The constructor declaration fact emitted for the symbol.</param>
            private void RecordConstructorInjectionDependencies(SyntaxNode node, ISymbol? symbol, SemanticDeclarationFact fact)
            {
                // Constructor parameters are treated as high-confidence injection relationships only when the constructor symbol and parameter type are compiler-resolved.
                if (symbol is not IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
                {
                    return;
                }

                foreach (IParameterSymbol parameter in constructor.Parameters)
                {
                    RecordRelationship(fact, node, SemanticRelationshipKind.Injects, parameter.Type, "ConstructorParameter");
                    RecordDependency(fact, node, parameter.Type, "ConstructorParameter");
                }
            }

            /// <summary>
            /// Records dependencies for attributes applied to a symbol.
            /// </summary>
            /// <param name="node">The syntax node that provides evidence for the attribute relationship.</param>
            /// <param name="symbol">The symbol whose attributes should be inspected.</param>
            /// <param name="sourceFact">The declaration fact that owns the attribute dependency.</param>
            /// <param name="dependencySource">The deterministic metadata value describing the attribute source.</param>
            private void RecordAttributeDependencies(SyntaxNode node, ISymbol? symbol, SemanticDeclarationFact sourceFact, string dependencySource)
            {
                // Attributes are dependencies on attribute types; Roslyn exposes the constructed attribute class even when source uses short attribute names.
                if (symbol is null)
                {
                    return;
                }

                foreach (AttributeData attribute in symbol.GetAttributes())
                {
                    if (attribute.AttributeClass is not null)
                    {
                        RecordDependency(sourceFact, node, attribute.AttributeClass, dependencySource);
                    }
                }
            }

            /// <summary>
            /// Records dependencies for return-value attributes applied to a method.
            /// </summary>
            /// <param name="node">The method declaration syntax that provides evidence for the return attribute relationship.</param>
            /// <param name="symbol">The method symbol whose return attributes should be inspected.</param>
            /// <param name="sourceFact">The method declaration fact that owns the return attribute dependency.</param>
            private void RecordReturnAttributeDependencies(SyntaxNode node, ISymbol? symbol, SemanticDeclarationFact sourceFact)
            {
                // Return attributes are stored separately from method attributes by Roslyn and need a dedicated pass to avoid silently dropping them.
                if (symbol is not IMethodSymbol methodSymbol)
                {
                    return;
                }

                foreach (AttributeData attribute in methodSymbol.GetReturnTypeAttributes())
                {
                    if (attribute.AttributeClass is not null)
                    {
                        RecordDependency(sourceFact, node, attribute.AttributeClass, "ReturnAttribute");
                    }
                }
            }

            /// <summary>
            /// Records assembly-level attribute dependencies once per assembly attribute syntax site.
            /// </summary>
            /// <param name="node">The compilation unit syntax containing assembly attribute lists.</param>
            public override void VisitCompilationUnit(CompilationUnitSyntax node)
            {
                // Assembly attributes do not have a declaration node, so they are sourced from a deterministic project surrogate key.
                foreach (AttributeListSyntax attributeList in node.AttributeLists.Where(attributeList => attributeList.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) == true))
                {
                    foreach (AttributeSyntax attribute in attributeList.Attributes)
                    {
                        ISymbol? attributeConstructor = _request.SemanticModel.GetSymbolInfo(attribute, _cancellationToken).Symbol;
                        INamedTypeSymbol? attributeType = attributeConstructor is IMethodSymbol methodSymbol ? methodSymbol.ContainingType : attributeConstructor as INamedTypeSymbol;
                        if (attributeType is not null)
                        {
                            SemanticSymbolIdentity sourceIdentity = new(_request.ProjectContext, _request.ProjectContext, _request.ProjectContext, containingSymbolName: null);
                            string sourceStableKey = $"semantic-project://{_request.ProjectContext}";
                            RecordRelationship(sourceStableKey, sourceIdentity, attribute, SemanticRelationshipKind.DependsOn, attributeType, "AssemblyAttribute");
                        }
                    }
                }

                base.VisitCompilationUnit(node);
            }

            /// <summary>
            /// Records generic constraint dependencies declared on a type parameter.
            /// </summary>
            /// <param name="sourceFact">The declaration fact that owns the generic type parameter.</param>
            /// <param name="node">The declaration syntax that provides evidence for the constraints.</param>
            /// <param name="typeParameter">The type parameter symbol whose constraints should be inspected.</param>
            private void RecordGenericConstraintDependencies(SemanticDeclarationFact sourceFact, SyntaxNode node, ITypeParameterSymbol typeParameter)
            {
                // Constraint types often represent architectural coupling even though they are not executable dependencies.
                foreach (ITypeSymbol constraintType in typeParameter.ConstraintTypes)
                {
                    RecordDependency(sourceFact, node, constraintType, "GenericConstraint");
                }
            }

            /// <summary>
            /// Records a relationship from the active member context to a compiler-resolved target symbol.
            /// </summary>
            /// <param name="node">The syntax node that provides relationship evidence.</param>
            /// <param name="relationshipKind">The semantic relationship kind to emit.</param>
            /// <param name="targetSymbol">The resolved target symbol.</param>
            /// <param name="relationshipSource">The deterministic metadata value describing how the relationship was found.</param>
            private void RecordRelationshipFromCurrentMember(SyntaxNode node, SemanticRelationshipKind relationshipKind, ISymbol targetSymbol, string relationshipSource)
            {
                // Executable relationships need an active source member; syntax outside a member is ignored instead of inventing a source node.
                SemanticDeclarationFact? sourceFact = GetCurrentSourceFact();
                if (sourceFact is null)
                {
                    return;
                }

                RecordRelationship(sourceFact, node, relationshipKind, targetSymbol, relationshipSource);
            }

            /// <summary>
            /// Records a dependency from the active member context to a compiler-resolved target type.
            /// </summary>
            /// <param name="node">The syntax node that provides dependency evidence.</param>
            /// <param name="targetSymbol">The resolved dependency target symbol.</param>
            /// <param name="dependencySource">The deterministic metadata value describing how the dependency was found.</param>
            private void RecordDependencyFromCurrentMember(SyntaxNode node, ISymbol? targetSymbol, string dependencySource)
            {
                // This helper lets invocation and object-creation visitors attach type dependencies without duplicating active-member lookup logic.
                SemanticDeclarationFact? sourceFact = GetCurrentSourceFact();
                if (sourceFact is null || targetSymbol is null)
                {
                    return;
                }

                RecordDependency(sourceFact, node, targetSymbol, dependencySource);
            }

            /// <summary>
            /// Records a dependency relationship from a declaration fact to a resolved target symbol.
            /// </summary>
            /// <param name="sourceFact">The declaration fact that owns the dependency.</param>
            /// <param name="node">The syntax node that provides dependency evidence.</param>
            /// <param name="targetSymbol">The resolved dependency target symbol.</param>
            /// <param name="dependencySource">The deterministic metadata value describing how the dependency was found.</param>
            private void RecordDependency(SemanticDeclarationFact sourceFact, SyntaxNode node, ISymbol? targetSymbol, string dependencySource)
            {
                // The dependency helper turns unresolved targets into unknowns and skips placeholders that cannot become concrete graph endpoints.
                if (targetSymbol is null)
                {
                    RecordUnknown(sourceFact, node, SemanticUnknownReason.UnresolvedSymbol, dependencySource, "Dependency target could not be resolved.");
                    return;
                }

                if (IsUnsupportedDependencyTarget(targetSymbol))
                {
                    RecordUnknown(sourceFact, node, SemanticSymbolClassifier.IsUnresolved(targetSymbol) ? SemanticUnknownReason.MissingReference : SemanticUnknownReason.UnsupportedSemanticForm, dependencySource, $"Dependency target '{targetSymbol.Name}' could not be emitted as a resolved endpoint.");
                    return;
                }

                RecordRelationship(sourceFact, node, SemanticRelationshipKind.DependsOn, targetSymbol, dependencySource);
            }

            /// <summary>
            /// Records a relationship from a declaration fact to a resolved target symbol.
            /// </summary>
            /// <param name="sourceFact">The declaration fact that owns the relationship.</param>
            /// <param name="node">The syntax node that provides relationship evidence.</param>
            /// <param name="relationshipKind">The semantic relationship kind to emit.</param>
            /// <param name="targetSymbol">The resolved target symbol.</param>
            /// <param name="relationshipSource">The deterministic metadata value describing how the relationship was found.</param>
            private void RecordRelationship(SemanticDeclarationFact sourceFact, SyntaxNode node, SemanticRelationshipKind relationshipKind, ISymbol targetSymbol, string relationshipSource)
            {
                // Source declaration facts already carry stable identities, so this overload adapts them to the lower-level relationship creation path.
                RecordRelationship(sourceFact.StableKey, sourceFact.SymbolIdentity, node, relationshipKind, targetSymbol, relationshipSource);
            }

            /// <summary>
            /// Records a relationship from a source endpoint to a resolved target symbol.
            /// </summary>
            /// <param name="sourceStableKey">The stable key of the relationship source endpoint.</param>
            /// <param name="sourceIdentity">The symbol identity of the relationship source endpoint.</param>
            /// <param name="node">The syntax node that provides relationship evidence.</param>
            /// <param name="relationshipKind">The semantic relationship kind to emit.</param>
            /// <param name="targetSymbol">The resolved target symbol.</param>
            /// <param name="relationshipSource">The deterministic metadata value describing how the relationship was found.</param>
            private void RecordRelationship(string sourceStableKey, SemanticSymbolIdentity sourceIdentity, SyntaxNode node, SemanticRelationshipKind relationshipKind, ISymbol targetSymbol, string relationshipSource)
            {
                // Target endpoints prefer existing source declaration keys and fall back to symbol-reference keys for metadata or not-yet-declared symbols.
                if (IsUnsupportedDependencyTarget(targetSymbol))
                {
                    return;
                }

                SemanticSymbolIdentity targetIdentity = CreateSymbolIdentity(targetSymbol, GetDeclarationKindForSymbol(targetSymbol));
                string targetStableKey = TryGetDeclarationFact(targetSymbol)?.StableKey ?? SemanticStableKeyBuilder.ForSymbolReference(SourceLanguage.CSharp, _request.ProjectContext, targetIdentity);
                string stableKey = SemanticStableKeyBuilder.ForRelationship(relationshipKind, sourceStableKey, targetStableKey, relationshipSource);
                SemanticEvidence evidence = CreateEvidence(node, sourceIdentity);
                Dictionary<string, string> metadata = new(StringComparer.Ordinal)
                {
                    ["dependencySource"] = relationshipSource
                };
                SemanticSymbolClassifier.AddSymbolMetadata(metadata, targetSymbol, _isGenerated);
                SemanticFactConfidence confidence = SemanticSymbolClassifier.ClassifyResolvedConfidence(targetSymbol, _isGenerated);

                _relationships[stableKey] = new SemanticRelationshipFact(
                    stableKey,
                    relationshipKind,
                    sourceStableKey,
                    targetStableKey,
                    evidence,
                    confidence,
                    sourceIdentity,
                    targetIdentity,
                    metadata,
                    unknownReason: null);
            }

            /// <summary>
            /// Gets the active source fact for relationships inside executable member syntax.
            /// </summary>
            /// <returns>The active source declaration fact, or <see langword="null" /> when traversal is not inside a member.</returns>
            private SemanticDeclarationFact? GetCurrentSourceFact()
            {
                // The top of the parent stack is the innermost declaration currently being visited.
                return _parentFacts.Count == 0 ? null : _parentFacts.Peek();
            }

            /// <summary>
            /// Attempts to locate a declaration fact for a resolved symbol endpoint.
            /// </summary>
            /// <param name="symbol">The resolved target symbol.</param>
            /// <returns>The matching declaration fact, or <see langword="null" /> when the target has not been declared in this document traversal.</returns>
            private SemanticDeclarationFact? TryGetDeclarationFact(ISymbol symbol)
            {
                // Declaration lookup uses the same display-key normalization used when declarations are recorded.
                return _declarationsBySymbolKey.TryGetValue(GetSymbolKey(symbol), out SemanticDeclarationFact? fact) ? fact : null;
            }

            /// <summary>
            /// Determines whether a resolved target should be skipped for dependency relationship output.
            /// </summary>
            /// <param name="symbol">The symbol being considered as a dependency endpoint.</param>
            /// <returns><see langword="true" /> when the target is not useful as a graph dependency in this slice.</returns>
            private static bool IsUnsupportedDependencyTarget(ISymbol symbol)
            {
                // Shared classification ensures C# and Visual Basic skip the same unsupported resolved endpoint shapes.
                return SemanticSymbolClassifier.IsUnsupportedDependencyTarget(symbol);
            }

            /// <summary>
            /// Records an additional evidence contribution for a declaration, including each partial declaration syntax reference when Roslyn exposes them.
            /// </summary>
            /// <param name="stableKey">The stable key of the declaration fact receiving evidence.</param>
            /// <param name="evidence">The evidence span produced by the current syntax node.</param>
            /// <param name="symbol">The symbol whose declaring syntax references should be inspected.</param>
            private void RecordEvidenceContribution(string stableKey, SemanticEvidence evidence, ISymbol symbol)
            {
                // Partial declarations share one stable symbol identity, so each syntax reference is recorded as evidence rather than producing duplicate nodes.
                _evidenceContributions.Add(new SemanticEvidenceContribution(stableKey, evidence, _isGenerated, symbol.DeclaringSyntaxReferences.Length > 1 ? "PartialDeclaration" : "PrimaryDeclaration"));
                foreach (SyntaxReference syntaxReference in symbol.DeclaringSyntaxReferences)
                {
                    SyntaxNode declarationNode = syntaxReference.GetSyntax(_cancellationToken);
                    if (declarationNode.SyntaxTree != _request.SyntaxTree)
                    {
                        continue;
                    }

                    _evidenceContributions.Add(new SemanticEvidenceContribution(stableKey, CreateEvidence(declarationNode, CreateSymbolIdentity(symbol, GetDeclarationKindForSymbol(symbol))), _isGenerated, "PartialDeclaration"));
                }
            }

            /// <summary>
            /// Records an unknown for unresolved symbol info returned by Roslyn binding APIs.
            /// </summary>
            /// <param name="node">The syntax node that produced the unresolved binding.</param>
            /// <param name="symbolInfo">The Roslyn symbol info that describes candidates and failure reason.</param>
            /// <param name="operation">The deterministic operation label for metadata.</param>
            /// <param name="preferredReason">The preferred unknown reason when syntax gives a stronger signal than Roslyn candidate reason.</param>
            private void RecordUnknownForUnresolvedSymbolInfo(SyntaxNode node, SymbolInfo symbolInfo, string operation, SemanticUnknownReason? preferredReason)
            {
                // CandidateReason is preserved as metadata while the normalized reason gives graph consumers a stable query vocabulary.
                SemanticDeclarationFact? sourceFact = GetCurrentSourceFact();
                if (sourceFact is null)
                {
                    return;
                }

                SemanticUnknownReason reason = preferredReason ?? MapCandidateReason(symbolInfo.CandidateReason);
                string description = symbolInfo.CandidateSymbols.Length == 0
                    ? $"{operation} could not be resolved."
                    : $"{operation} could not be resolved uniquely.";
                RecordUnknown(sourceFact, node, reason, operation, description, symbolInfo.CandidateReason.ToString());
            }

            /// <summary>
            /// Records a reflection unknown for calls that take string-based type or member names.
            /// </summary>
            /// <param name="node">The invocation syntax that may represent reflection.</param>
            /// <param name="targetMethod">The resolved method symbol for the invocation.</param>
            private void RecordReflectionUnknownIfNeeded(InvocationExpressionSyntax node, IMethodSymbol targetMethod)
            {
                // Type.GetType and similar reflection APIs are resolved calls, but their string targets are not deterministic source symbols.
                if (targetMethod.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == "System.Type" && targetMethod.Name == "GetType")
                {
                    SemanticDeclarationFact? sourceFact = GetCurrentSourceFact();
                    if (sourceFact is not null)
                    {
                        RecordUnknown(sourceFact, node, SemanticUnknownReason.ReflectionTarget, "Reflection", "Reflection target is string-based and cannot be resolved statically.");
                    }
                }
            }

            /// <summary>
            /// Records an unknown fact for a source declaration and evidence node.
            /// </summary>
            /// <param name="sourceFact">The source declaration fact that owns the unknown.</param>
            /// <param name="node">The syntax node that provides unknown evidence.</param>
            /// <param name="reason">The normalized unknown reason.</param>
            /// <param name="operation">The deterministic operation label for metadata.</param>
            /// <param name="description">The human-readable unknown description.</param>
            /// <param name="candidateReason">The optional Roslyn candidate reason text.</param>
            private void RecordUnknown(SemanticDeclarationFact sourceFact, SyntaxNode node, SemanticUnknownReason reason, string operation, string description, string? candidateReason = null)
            {
                // Unknowns are keyed by evidence and reason so repeated syntax visits collapse without hiding distinct degraded patterns.
                SemanticEvidence evidence = CreateEvidence(node, sourceFact.SymbolIdentity);
                string stableKey = SemanticStableKeyBuilder.ForUnknown(SourceLanguage.CSharp, _request.ProjectContext, reason, evidence, description);
                Dictionary<string, string> metadata = new(StringComparer.Ordinal)
                {
                    ["operation"] = operation,
                    ["generated"] = _isGenerated ? "true" : "false"
                };
                if (!string.IsNullOrWhiteSpace(candidateReason))
                {
                    metadata["candidateReason"] = candidateReason;
                }

                _unknowns[stableKey] = new SemanticUnknownFact(stableKey, reason, SourceLanguage.CSharp, _request.ProjectContext, sourceFact.SymbolIdentity, description, evidence, SemanticFactConfidence.Unresolved, metadata);
            }

            /// <summary>
            /// Maps Roslyn candidate reasons into shared unknown reason categories.
            /// </summary>
            /// <param name="candidateReason">The Roslyn candidate reason to normalize.</param>
            /// <returns>The shared unknown reason closest to the Roslyn binding failure.</returns>
            private static SemanticUnknownReason MapCandidateReason(CandidateReason candidateReason)
            {
                // Roslyn exposes language-specific binding details; the shared enum is the graph query contract.
                return candidateReason switch
                {
                    CandidateReason.OverloadResolutionFailure => SemanticUnknownReason.AmbiguousOverload,
                    CandidateReason.Ambiguous => SemanticUnknownReason.AmbiguousOverload,
                    CandidateReason.NotATypeOrNamespace => SemanticUnknownReason.MissingReference,
                    CandidateReason.None => SemanticUnknownReason.UnresolvedSymbol,
                    _ => SemanticUnknownReason.UnsupportedSemanticForm
                };
            }

            /// <summary>
            /// Determines whether an invocation is made through a dynamic receiver or operation.
            /// </summary>
            /// <param name="node">The invocation syntax to inspect.</param>
            /// <returns><see langword="true" /> when the invocation target or receiver has dynamic type.</returns>
            private bool IsDynamicInvocation(InvocationExpressionSyntax node)
            {
                // Dynamic calls have no deterministic static target even though they may look like ordinary member calls in syntax.
                TypeInfo typeInfo = _request.SemanticModel.GetTypeInfo(node.Expression, _cancellationToken);
                if (typeInfo.Type?.TypeKind == TypeKind.Dynamic)
                {
                    return true;
                }

                if (node.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    TypeInfo receiverType = _request.SemanticModel.GetTypeInfo(memberAccess.Expression, _cancellationToken);
                    return receiverType.Type?.TypeKind == TypeKind.Dynamic;
                }

                return false;
            }

            /// <summary>
            /// Selects the closest declaration kind for a relationship endpoint symbol.
            /// </summary>
            /// <param name="symbol">The symbol being projected into endpoint identity.</param>
            /// <returns>The declaration kind used for symbol identity formatting.</returns>
            private static SemanticDeclarationKind GetDeclarationKindForSymbol(ISymbol symbol)
            {
                // Relationship targets can be metadata symbols; this mapping selects the graph node family that would represent the target if declared locally.
                return symbol switch
                {
                    INamespaceSymbol => SemanticDeclarationKind.Namespace,
                    INamedTypeSymbol => SemanticDeclarationKind.Type,
                    IMethodSymbol => SemanticDeclarationKind.Method,
                    IPropertySymbol => SemanticDeclarationKind.Property,
                    IFieldSymbol => SemanticDeclarationKind.Field,
                    _ => SemanticDeclarationKind.Type
                };
            }

            /// <summary>
            /// Creates a deterministic lookup key for Roslyn symbols encountered during one extraction traversal.
            /// </summary>
            /// <param name="symbol">The symbol to normalize.</param>
            /// <returns>A deterministic lookup key for source declaration matching.</returns>
            private static string GetSymbolKey(ISymbol symbol)
            {
                // Fully qualified display text is stable within one compilation and avoids relying on Roslyn reference identity.
                return symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
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
