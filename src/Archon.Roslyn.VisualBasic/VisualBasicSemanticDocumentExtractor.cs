using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Archon.Roslyn.VisualBasic
{
    /// <summary>
    /// Extracts graph-ready declaration, relationship, and dependency facts from a Visual Basic Roslyn semantic model.
    /// </summary>
    /// <remarks>
    /// This extractor projects Visual Basic symbols into the same language-neutral semantic model used by C# so mixed-language repositories can be compared through one graph vocabulary.
    /// </remarks>
    public sealed class VisualBasicSemanticDocumentExtractor : ISemanticDocumentExtractor
    {
        /// <summary>
        /// Extracts declaration, containment, and relationship facts from one Visual Basic semantic document request.
        /// </summary>
        /// <param name="request">The document, semantic model, and repository context to analyze.</param>
        /// <param name="cancellationToken">A token that signals when extraction should stop before additional semantic work is performed.</param>
        /// <returns>The semantic extraction result containing Visual Basic declaration facts, relationship facts, and diagnostics.</returns>
        public SemanticExtractionResult Extract(SemanticExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // The extractor validates the request once, then lets the collector maintain traversal context and deterministic duplicate dictionaries for the document.
            ArgumentNullException.ThrowIfNull(request);
            SyntaxNode root = request.SyntaxTree.GetRoot(cancellationToken);
            VisualBasicSemanticCollector collector = new(request, cancellationToken);
            collector.Visit(root);
            return collector.ToResult();
        }

        /// <summary>
        /// Visits supported Visual Basic syntax and records semantic declaration, relationship, and dependency facts.
        /// </summary>
        /// <remarks>
        /// The collector is scoped to a single document because it carries mutable traversal state, parent declarations, active member context, and duplicate tracking.
        /// </remarks>
        private sealed class VisualBasicSemanticCollector : VisualBasicSyntaxWalker
        {
            /// <summary>
            /// Stores the extraction request that supplies Roslyn and repository context.
            /// </summary>
            private readonly SemanticExtractionRequest _request;

            /// <summary>
            /// Stores the cancellation token checked before semantic operations.
            /// </summary>
            private readonly CancellationToken _cancellationToken;

            /// <summary>
            /// Stores declaration facts by stable key so duplicate partial or repeated syntax discoveries collapse deterministically.
            /// </summary>
            private readonly Dictionary<string, SemanticDeclarationFact> _declarations = new(StringComparer.Ordinal);

            /// <summary>
            /// Stores relationship facts by stable key so repeated relationship discoveries collapse deterministically.
            /// </summary>
            private readonly Dictionary<string, SemanticRelationshipFact> _relationships = new(StringComparer.Ordinal);

            /// <summary>
            /// Stores non-fatal warnings encountered while resolving symbols.
            /// </summary>
            private readonly List<string> _warnings = [];

            /// <summary>
            /// Stores declaration facts by Roslyn symbol display identity for relationship endpoint lookup.
            /// </summary>
            private readonly Dictionary<string, SemanticDeclarationFact> _declarationsBySymbolKey = new(StringComparer.Ordinal);

            /// <summary>
            /// Stores the current declaration ancestry while the syntax walker descends.
            /// </summary>
            private readonly Stack<SemanticDeclarationFact> _parentFacts = new();

            /// <summary>
            /// Initializes a new Visual Basic semantic collector for one document.
            /// </summary>
            /// <param name="request">The semantic extraction request being processed.</param>
            /// <param name="cancellationToken">A token that signals when traversal should stop.</param>
            public VisualBasicSemanticCollector(SemanticExtractionRequest request, CancellationToken cancellationToken)
                : base(SyntaxWalkerDepth.Node)
            {
                // The collector is intentionally stateful and per-document so parent stacks and duplicate dictionaries never leak between extraction requests.
                _request = request;
                _cancellationToken = cancellationToken;
            }

            /// <summary>
            /// Visits a namespace block and records the namespace before visiting contained declarations.
            /// </summary>
            /// <param name="node">The namespace block syntax to process.</param>
            public override void VisitNamespaceBlock(NamespaceBlockSyntax node)
            {
                // Namespace blocks establish parent context for Visual Basic declarations and can include the project root namespace in the resolved symbol name.
                VisitDeclarationWithChildren(node, SemanticDeclarationKind.Namespace, node.NamespaceStatement.Name.ToString(), _request.SemanticModel.GetDeclaredSymbol(node, _cancellationToken));
            }

            /// <summary>
            /// Visits a class block and records the class type before visiting members.
            /// </summary>
            /// <param name="node">The class block syntax to process.</param>
            public override void VisitClassBlock(ClassBlockSyntax node)
            {
                // Classes are type declarations and can participate in inheritance, implementation, and member dependency relationships.
                VisitDeclarationWithChildren(node, SemanticDeclarationKind.Type, node.ClassStatement.Identifier.ValueText, _request.SemanticModel.GetDeclaredSymbol(node.ClassStatement, _cancellationToken));
            }

            /// <summary>
            /// Visits a structure block and records the structure type before visiting members.
            /// </summary>
            /// <param name="node">The structure block syntax to process.</param>
            public override void VisitStructureBlock(StructureBlockSyntax node)
            {
                // Structures project into the same Type graph vocabulary as classes and C# structs.
                VisitDeclarationWithChildren(node, SemanticDeclarationKind.Type, node.StructureStatement.Identifier.ValueText, _request.SemanticModel.GetDeclaredSymbol(node.StructureStatement, _cancellationToken));
            }

            /// <summary>
            /// Visits an interface block and records the interface type before visiting members.
            /// </summary>
            /// <param name="node">The interface block syntax to process.</param>
            public override void VisitInterfaceBlock(InterfaceBlockSyntax node)
            {
                // Interfaces are type declarations so later implementation relationships can target the same normalized endpoint shape.
                VisitDeclarationWithChildren(node, SemanticDeclarationKind.Type, node.InterfaceStatement.Identifier.ValueText, _request.SemanticModel.GetDeclaredSymbol(node.InterfaceStatement, _cancellationToken));
            }

            /// <summary>
            /// Visits a module block and records the module as a type declaration.
            /// </summary>
            /// <param name="node">The module block syntax to process.</param>
            public override void VisitModuleBlock(ModuleBlockSyntax node)
            {
                // Visual Basic modules compile to types with shared members, so they naturally project to Type declaration facts.
                VisitDeclarationWithChildren(node, SemanticDeclarationKind.Type, node.ModuleStatement.Identifier.ValueText, _request.SemanticModel.GetDeclaredSymbol(node.ModuleStatement, _cancellationToken));
            }

            /// <summary>
            /// Visits an enum block and records the enum as a type declaration.
            /// </summary>
            /// <param name="node">The enum block syntax to process.</param>
            public override void VisitEnumBlock(EnumBlockSyntax node)
            {
                // Enums are source-declared named types and share the normalized Type declaration kind.
                VisitDeclarationWithChildren(node, SemanticDeclarationKind.Type, node.EnumStatement.Identifier.ValueText, _request.SemanticModel.GetDeclaredSymbol(node.EnumStatement, _cancellationToken));
            }

            /// <summary>
            /// Visits a delegate statement and records the delegate as a type declaration.
            /// </summary>
            /// <param name="node">The delegate statement syntax to process.</param>
            public override void VisitDelegateStatement(DelegateStatementSyntax node)
            {
                // Delegates are named types even though they are represented by one statement rather than a block.
                VisitLeafDeclaration(node, SemanticDeclarationKind.Type, node.Identifier.ValueText, _request.SemanticModel.GetDeclaredSymbol(node, _cancellationToken));
            }

            /// <summary>
            /// Visits a constructor block and records the constructor as a method declaration.
            /// </summary>
            /// <param name="node">The constructor block syntax to process.</param>
            public override void VisitConstructorBlock(ConstructorBlockSyntax node)
            {
                // Constructors become Method declaration facts so object creation and constructor injection can target the same graph node family as C#.
                VisitMemberDeclarationWithBody(node, SemanticDeclarationKind.Method, "New", _request.SemanticModel.GetDeclaredSymbol(node, _cancellationToken));
            }

            /// <summary>
            /// Visits a method block and records the method before visiting executable statements.
            /// </summary>
            /// <param name="node">The method block syntax to process.</param>
            public override void VisitMethodBlock(MethodBlockSyntax node)
            {
                // Method blocks become active relationship sources while their bodies are visited.
                VisitMemberDeclarationWithBody(node, SemanticDeclarationKind.Method, node.SubOrFunctionStatement.Identifier.ValueText, _request.SemanticModel.GetDeclaredSymbol(node, _cancellationToken));
            }

            /// <summary>
            /// Visits an event block and records the event as a field-like declaration when Roslyn exposes an event symbol.
            /// </summary>
            /// <param name="node">The event block syntax to process.</param>
            public override void VisitEventBlock(EventBlockSyntax node)
            {
                // Custom events still represent member-level architecture surface and are projected as Field facts in the current shared declaration vocabulary.
                VisitMemberDeclarationWithBody(node, SemanticDeclarationKind.Field, node.EventStatement.Identifier.ValueText, _request.SemanticModel.GetDeclaredSymbol(node, _cancellationToken));
            }

            /// <summary>
            /// Visits a property block and records the property before visiting accessor bodies.
            /// </summary>
            /// <param name="node">The property block syntax to process.</param>
            public override void VisitPropertyBlock(PropertyBlockSyntax node)
            {
                // Default and ordinary properties share the Property declaration kind; default-ness remains available through symbol metadata and source evidence.
                VisitMemberDeclarationWithBody(node, SemanticDeclarationKind.Property, node.PropertyStatement.Identifier.ValueText, _request.SemanticModel.GetDeclaredSymbol(node, _cancellationToken));
            }

            /// <summary>
            /// Visits an auto-property statement and records the property as a leaf declaration.
            /// </summary>
            /// <param name="node">The auto-property statement syntax to process.</param>
            public override void VisitPropertyStatement(PropertyStatementSyntax node)
            {
                // Auto-properties do not have a property block, so the statement itself must be recorded as the declaration site.
                if (node.Parent is not PropertyBlockSyntax)
                {
                    VisitLeafDeclaration(node, SemanticDeclarationKind.Property, node.Identifier.ValueText, _request.SemanticModel.GetDeclaredSymbol(node, _cancellationToken));
                    return;
                }

                base.VisitPropertyStatement(node);
            }

            /// <summary>
            /// Visits a field declaration and records each declared field or constant variable.
            /// </summary>
            /// <param name="node">The field declaration syntax to process.</param>
            public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                // A Visual Basic field declaration can declare multiple names, so each modified identifier is resolved independently.
                _cancellationToken.ThrowIfCancellationRequested();
                foreach (VariableDeclaratorSyntax declarator in node.Declarators)
                {
                    foreach (ModifiedIdentifierSyntax name in declarator.Names)
                    {
                        VisitLeafDeclaration(name, SemanticDeclarationKind.Field, name.Identifier.ValueText, _request.SemanticModel.GetDeclaredSymbol(name, _cancellationToken));
                    }
                }
            }

            /// <summary>
            /// Visits an event statement and records ordinary events as field-like declarations.
            /// </summary>
            /// <param name="node">The event statement syntax to process.</param>
            public override void VisitEventStatement(EventStatementSyntax node)
            {
                // Ordinary events appear as statements, while custom events are handled by EventBlock and should not be recorded twice.
                if (node.Parent is not EventBlockSyntax)
                {
                    VisitLeafDeclaration(node, SemanticDeclarationKind.Field, node.Identifier.ValueText, _request.SemanticModel.GetDeclaredSymbol(node, _cancellationToken));
                    return;
                }

                base.VisitEventStatement(node);
            }

            /// <summary>
            /// Visits an invocation expression and records a compiler-resolved call relationship when source and target symbols are known.
            /// </summary>
            /// <param name="node">The invocation expression syntax to resolve.</param>
            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                // Visual Basic invocation expressions cover ordinary method calls, shared calls, default property invocations, and extension method calls when Roslyn can bind the target.
                _cancellationToken.ThrowIfCancellationRequested();
                SymbolInfo symbolInfo = _request.SemanticModel.GetSymbolInfo(node, _cancellationToken);
                ISymbol? targetSymbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
                if (targetSymbol is IMethodSymbol targetMethod)
                {
                    RecordRelationshipFromCurrentMember(node, SemanticRelationshipKind.Calls, targetMethod.ReducedFrom ?? targetMethod, "Invocation");
                    RecordDependencyFromCurrentMember(node, targetMethod.ContainingType, "InvocationTargetType");
                }
                else if (targetSymbol is IPropertySymbol propertySymbol)
                {
                    RecordRelationshipFromCurrentMember(node, SemanticRelationshipKind.DependsOn, propertySymbol, "PropertyAccess");
                    RecordDependencyFromCurrentMember(node, propertySymbol.Type, "PropertyType");
                }

                base.VisitInvocationExpression(node);
            }

            /// <summary>
            /// Visits an object creation expression and records constructor call plus created-type dependency facts.
            /// </summary>
            /// <param name="node">The object creation expression syntax to resolve.</param>
            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                // Object creation produces a CALLS relationship to the constructor and a DEPENDS_ON relationship to the constructed type.
                _cancellationToken.ThrowIfCancellationRequested();
                IMethodSymbol? constructor = _request.SemanticModel.GetSymbolInfo(node, _cancellationToken).Symbol as IMethodSymbol;
                if (constructor is not null)
                {
                    RecordRelationshipFromCurrentMember(node, SemanticRelationshipKind.Calls, constructor, "ObjectCreationConstructor");
                    RecordDependencyFromCurrentMember(node, constructor.ContainingType, "ObjectCreation");
                }

                base.VisitObjectCreationExpression(node);
            }

            /// <summary>
            /// Visits a member access expression and records property access dependency facts when Roslyn resolves a property symbol.
            /// </summary>
            /// <param name="node">The member access expression syntax to resolve.</param>
            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                // Property access can indicate architecture coupling even when the syntax is not an invocation.
                _cancellationToken.ThrowIfCancellationRequested();
                ISymbol? symbol = _request.SemanticModel.GetSymbolInfo(node, _cancellationToken).Symbol;
                if (symbol is IPropertySymbol propertySymbol)
                {
                    RecordRelationshipFromCurrentMember(node, SemanticRelationshipKind.DependsOn, propertySymbol, "PropertyAccess");
                    RecordDependencyFromCurrentMember(node, propertySymbol.Type, "PropertyType");
                }

                base.VisitMemberAccessExpression(node);
            }

            /// <summary>
            /// Visits an identifier name and records simple property access dependencies.
            /// </summary>
            /// <param name="node">The identifier name syntax to resolve.</param>
            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                // Simple property references can appear as identifiers rather than member-access expressions, so identifiers receive a focused semantic check.
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
            /// Visits a type-parameter constraint clause and records explicit generic constraint dependencies.
            /// </summary>
            /// <param name="node">The type-parameter constraint clause syntax to resolve.</param>
            public override void VisitTypeParameterSingleConstraintClause(TypeParameterSingleConstraintClauseSyntax node)
            {
                // Single generic constraints are evidence for dependencies on the constrained type when the compiler resolves that type.
                RecordConstraintDependencies(node);
                base.VisitTypeParameterSingleConstraintClause(node);
            }

            /// <summary>
            /// Visits a multiple-constraint clause and records explicit generic constraint dependencies.
            /// </summary>
            /// <param name="node">The type-parameter multiple-constraint clause syntax to resolve.</param>
            public override void VisitTypeParameterMultipleConstraintClause(TypeParameterMultipleConstraintClauseSyntax node)
            {
                // Multiple generic constraints use a different syntax node but have the same graph meaning as single constraints.
                RecordConstraintDependencies(node);
                base.VisitTypeParameterMultipleConstraintClause(node);
            }

            /// <summary>
            /// Visits a compilation unit and records assembly-level attribute dependencies.
            /// </summary>
            /// <param name="node">The compilation unit syntax containing assembly attributes.</param>
            public override void VisitCompilationUnit(CompilationUnitSyntax node)
            {
                // Assembly attributes do not belong to a source declaration node, so they are sourced from a deterministic project surrogate endpoint.
                foreach (AttributeSyntax attribute in node.DescendantNodes().OfType<AttributeSyntax>())
                {
                    if (attribute.Target?.ToString().Contains("Assembly", StringComparison.OrdinalIgnoreCase) == true)
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
            /// Creates the immutable extraction result after traversal has completed.
            /// </summary>
            /// <returns>The semantic extraction result ordered by stable key for deterministic assertions and accumulation.</returns>
            public SemanticExtractionResult ToResult()
            {
                // Stable-key ordering keeps test assertions and future accumulation deterministic independent of Roslyn traversal details.
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
            /// <param name="symbol">The Roslyn symbol resolved for the declaration.</param>
            private void VisitDeclarationWithChildren(SyntaxNode node, SemanticDeclarationKind declarationKind, string fallbackSymbolName, ISymbol? symbol)
            {
                // Parent context is pushed only when a declaration fact is available so unresolved declarations do not corrupt hierarchy.
                _cancellationToken.ThrowIfCancellationRequested();
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
            /// Visits direct child nodes without redispatching the current declaration node.
            /// </summary>
            /// <param name="node">The syntax node whose children should be visited.</param>
            private void VisitChildNodes(SyntaxNode node)
            {
                // Iterating child nodes avoids recursively revisiting the same block override while still walking nested declarations and statements.
                foreach (SyntaxNode childNode in node.ChildNodes())
                {
                    Visit(childNode);
                }
            }

            /// <summary>
            /// Records a declaration that does not need to push parent context.
            /// </summary>
            /// <param name="node">The declaration syntax node being processed.</param>
            /// <param name="declarationKind">The semantic declaration kind to emit.</param>
            /// <param name="fallbackSymbolName">The fallback symbol name to use if Roslyn cannot resolve the declaration.</param>
            /// <param name="symbol">The Roslyn symbol resolved for the declaration.</param>
            private void VisitLeafDeclaration(SyntaxNode node, SemanticDeclarationKind declarationKind, string fallbackSymbolName, ISymbol? symbol)
            {
                // Leaf declarations still receive attributes and signature dependencies, but they do not alter traversal parent context.
                _cancellationToken.ThrowIfCancellationRequested();
                SemanticDeclarationFact? fact = RecordDeclaration(node, declarationKind, fallbackSymbolName, symbol);
                if (fact is not null)
                {
                    RecordAttributeDependencies(node, symbol, fact, "Attribute");
                    RecordSignatureDependencies(node, symbol, fact);
                }
            }

            /// <summary>
            /// Records a member declaration and visits its child statements while the member is the active relationship source.
            /// </summary>
            /// <param name="node">The member declaration syntax node being processed.</param>
            /// <param name="declarationKind">The semantic declaration kind to emit.</param>
            /// <param name="fallbackSymbolName">The fallback symbol name to use if Roslyn cannot resolve the declaration.</param>
            /// <param name="symbol">The Roslyn symbol resolved for the declaration.</param>
            private void VisitMemberDeclarationWithBody(SyntaxNode node, SemanticDeclarationKind declarationKind, string fallbackSymbolName, ISymbol? symbol)
            {
                // Member declarations are active source endpoints for relationship visitors nested inside their executable bodies.
                _cancellationToken.ThrowIfCancellationRequested();
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
            /// Records a semantic declaration fact and any direct containment relationship.
            /// </summary>
            /// <param name="node">The syntax node that produced the declaration.</param>
            /// <param name="declarationKind">The semantic declaration kind to emit.</param>
            /// <param name="fallbackSymbolName">The fallback symbol name used when Roslyn cannot resolve the symbol.</param>
            /// <param name="symbol">The Roslyn symbol resolved for the declaration.</param>
            /// <returns>The recorded declaration fact, or <see langword="null" /> when Roslyn did not resolve a symbol.</returns>
            private SemanticDeclarationFact? RecordDeclaration(SyntaxNode node, SemanticDeclarationKind declarationKind, string fallbackSymbolName, ISymbol? symbol)
            {
                // Visual Basic extraction follows the C# rule: unresolved declarations produce warnings instead of text-only graph facts.
                if (symbol is null)
                {
                    _warnings.Add($"Visual Basic declaration '{fallbackSymbolName}' could not be resolved by Roslyn and was not emitted as a semantic fact.");
                    return null;
                }

                SemanticSymbolIdentity symbolIdentity = CreateSymbolIdentity(symbol, declarationKind);
                string stableKey = SemanticStableKeyBuilder.ForDeclaration(declarationKind, SourceLanguage.VisualBasic, _request.ProjectContext, symbolIdentity);
                string? parentStableKey = _parentFacts.Count == 0 ? null : _parentFacts.Peek().StableKey;
                SemanticEvidence evidence = CreateEvidence(node, symbolIdentity);
                SemanticDeclarationFact fact = new(stableKey, declarationKind, SourceLanguage.VisualBasic, symbolIdentity, _request.ProjectContext, parentStableKey, evidence);
                _declarations[stableKey] = fact;
                _declarationsBySymbolKey[GetSymbolKey(symbol)] = fact;

                if (parentStableKey is not null)
                {
                    string relationshipStableKey = SemanticStableKeyBuilder.ForRelationship(SemanticRelationshipKind.Contains, parentStableKey, stableKey);
                    _relationships[relationshipStableKey] = new SemanticRelationshipFact(relationshipStableKey, SemanticRelationshipKind.Contains, parentStableKey, stableKey, evidence);
                }

                return fact;
            }

            /// <summary>
            /// Records inheritance, implementation, and generic constraint dependencies for a type declaration.
            /// </summary>
            /// <param name="node">The type declaration syntax that provides relationship evidence.</param>
            /// <param name="symbol">The declared symbol associated with the type declaration.</param>
            /// <param name="fact">The declaration fact emitted for the type.</param>
            private void RecordTypeRelationships(SyntaxNode node, ISymbol? symbol, SemanticDeclarationFact fact)
            {
                // Named type symbols normalize Visual Basic Inherits and Implements clauses across syntax forms.
                if (symbol is not INamedTypeSymbol namedType || fact.DeclarationKind != SemanticDeclarationKind.Type)
                {
                    return;
                }

                if (namedType.BaseType is not null && namedType.BaseType.SpecialType != SpecialType.System_Object && namedType.TypeKind != TypeKind.Module)
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
            /// Records signature-level dependencies for methods, properties, fields, events, and generic declarations.
            /// </summary>
            /// <param name="node">The declaration syntax that provides dependency evidence.</param>
            /// <param name="symbol">The declared symbol whose signature should be inspected.</param>
            /// <param name="fact">The declaration fact emitted for the symbol.</param>
            private void RecordSignatureDependencies(SyntaxNode node, ISymbol? symbol, SemanticDeclarationFact fact)
            {
                // Signature dependencies preserve architecturally relevant type usage even when no executable statement references the type.
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
                        foreach (IParameterSymbol parameter in propertySymbol.Parameters)
                        {
                            RecordDependency(fact, node, parameter.Type, "ParameterType");
                        }

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
                    case IEventSymbol eventSymbol:
                        RecordDependency(fact, node, eventSymbol.Type, "EventType");
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
                // Constructor parameters are treated as compiler-resolved injection relationships when both the constructor and parameter type are bound.
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
                // Attribute syntax can use shortened names, but Roslyn exposes the constructed attribute class for deterministic dependency targets.
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
                // Return attributes are stored separately from method attributes by Roslyn and must be inspected explicitly.
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
            /// Records generic constraint dependencies declared on a type parameter.
            /// </summary>
            /// <param name="sourceFact">The declaration fact that owns the generic type parameter.</param>
            /// <param name="node">The declaration syntax that provides evidence for the constraints.</param>
            /// <param name="typeParameter">The type parameter symbol whose constraints should be inspected.</param>
            private void RecordGenericConstraintDependencies(SemanticDeclarationFact sourceFact, SyntaxNode node, ITypeParameterSymbol typeParameter)
            {
                // Constraint types represent architecture coupling even though they are not executable dependencies.
                foreach (ITypeSymbol constraintType in typeParameter.ConstraintTypes)
                {
                    RecordDependency(sourceFact, node, constraintType, "GenericConstraint");
                }
            }

            /// <summary>
            /// Records explicit generic constraint dependencies from a Visual Basic constraint clause syntax node.
            /// </summary>
            /// <param name="node">The constraint clause syntax that provides dependency evidence.</param>
            private void RecordConstraintDependencies(TypeParameterConstraintClauseSyntax node)
            {
                // Syntax-level constraint handling complements symbol-level type-parameter inspection and gives precise evidence spans for constraint clauses.
                _cancellationToken.ThrowIfCancellationRequested();
                SemanticDeclarationFact? sourceFact = GetCurrentSourceFact();
                if (sourceFact is null)
                {
                    return;
                }

                foreach (TypeSyntax typeSyntax in node.DescendantNodes().OfType<TypeSyntax>())
                {
                    ITypeSymbol? constraintType = _request.SemanticModel.GetTypeInfo(typeSyntax, _cancellationToken).Type;
                    RecordDependency(sourceFact, typeSyntax, constraintType, "GenericConstraint");
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
                // Executable relationships require an active source member; syntax outside members is ignored rather than inventing a source endpoint.
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
                // This helper lets expression visitors attach type dependencies without duplicating active-member lookup logic.
                SemanticDeclarationFact? sourceFact = GetCurrentSourceFact();
                if (sourceFact is null || targetSymbol is null)
                {
                    return;
                }

                RecordDependency(sourceFact, node, targetSymbol, dependencySource);
            }

            /// <summary>
            /// Gets the active declaration fact that should source executable relationships.
            /// </summary>
            /// <returns>The active source declaration fact, or <see langword="null" /> when traversal is outside a member.</returns>
            private SemanticDeclarationFact? GetCurrentSourceFact()
            {
                // The top of the parent stack is the innermost declaration currently being visited.
                return _parentFacts.Count == 0 ? null : _parentFacts.Peek();
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
                // The dependency helper skips void, null, type parameters, and error symbols because they do not create useful resolved endpoints in this slice.
                if (targetSymbol is null || IsUnsupportedDependencyTarget(targetSymbol))
                {
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
                // Target endpoints prefer locally declared facts and fall back to symbol-reference keys for metadata or external symbols.
                if (IsUnsupportedDependencyTarget(targetSymbol))
                {
                    return;
                }

                SemanticSymbolIdentity targetIdentity = CreateSymbolIdentity(targetSymbol, GetDeclarationKindForSymbol(targetSymbol));
                string targetStableKey = TryGetDeclarationFact(targetSymbol)?.StableKey ?? SemanticStableKeyBuilder.ForSymbolReference(SourceLanguage.VisualBasic, _request.ProjectContext, targetIdentity);
                string stableKey = SemanticStableKeyBuilder.ForRelationship(relationshipKind, sourceStableKey, targetStableKey, relationshipSource);
                SemanticEvidence evidence = CreateEvidence(node, sourceIdentity);
                Dictionary<string, string> metadata = new(StringComparer.Ordinal)
                {
                    ["dependencySource"] = relationshipSource,
                    ["targetKind"] = targetSymbol.Kind.ToString()
                };

                _relationships[stableKey] = new SemanticRelationshipFact(
                    stableKey,
                    relationshipKind,
                    sourceStableKey,
                    targetStableKey,
                    evidence,
                    SemanticFactConfidence.CompilerResolved,
                    sourceIdentity,
                    targetIdentity,
                    metadata,
                    unknownReason: null);
            }

            /// <summary>
            /// Attempts to locate a declaration fact for a resolved symbol endpoint.
            /// </summary>
            /// <param name="symbol">The resolved target symbol.</param>
            /// <returns>The matching declaration fact, or <see langword="null" /> when the target has not been declared in this document traversal.</returns>
            private SemanticDeclarationFact? TryGetDeclarationFact(ISymbol symbol)
            {
                // Lookup uses the same Visual Basic display-key normalization used when declarations are recorded.
                return _declarationsBySymbolKey.TryGetValue(GetSymbolKey(symbol), out SemanticDeclarationFact? fact) ? fact : null;
            }

            /// <summary>
            /// Determines whether a resolved target should be skipped for dependency relationship output.
            /// </summary>
            /// <param name="symbol">The symbol being considered as a dependency endpoint.</param>
            /// <returns><see langword="true" /> when the target is not useful as a graph dependency in this slice.</returns>
            private static bool IsUnsupportedDependencyTarget(ISymbol symbol)
            {
                // Void, error, and type-parameter targets do not represent concrete architecture dependencies for Work Item 3.
                return symbol switch
                {
                    ITypeSymbol { SpecialType: SpecialType.System_Void } => true,
                    IErrorTypeSymbol => true,
                    ITypeParameterSymbol => true,
                    _ => false
                };
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
                    IEventSymbol => SemanticDeclarationKind.Field,
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
                // Fully qualified Visual Basic display text is stable within one compilation and avoids relying on Roslyn reference identity.
                return symbol.ToDisplayString(SymbolDisplayFormat.VisualBasicErrorMessageFormat);
            }

            /// <summary>
            /// Creates a semantic symbol identity from a Visual Basic compiler symbol.
            /// </summary>
            /// <param name="symbol">The Roslyn symbol to project.</param>
            /// <param name="declarationKind">The declaration kind being emitted for the symbol.</param>
            /// <returns>A deterministic semantic symbol identity.</returns>
            private static SemanticSymbolIdentity CreateSymbolIdentity(ISymbol symbol, SemanticDeclarationKind declarationKind)
            {
                // Visual Basic display formatting gives contributor-readable names while still preserving fully qualified identity and overload signatures.
                string fullyQualifiedName = declarationKind == SemanticDeclarationKind.Namespace
                    ? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("Global.", string.Empty, StringComparison.Ordinal)
                    : symbol.ToDisplayString(SymbolDisplayFormat.VisualBasicErrorMessageFormat);
                string metadataName = symbol.ToDisplayString(SymbolDisplayFormat.VisualBasicErrorMessageFormat);
                string displayName = symbol.Kind == SymbolKind.Method && symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } method
                    ? method.ContainingType.Name
                    : symbol.Name;
                string? containingSymbol = symbol.ContainingSymbol is null or INamespaceSymbol { IsGlobalNamespace: true }
                    ? null
                    : symbol.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.VisualBasicErrorMessageFormat);

                return new SemanticSymbolIdentity(metadataName, displayName, fullyQualifiedName, containingSymbol);
            }

            /// <summary>
            /// Creates source evidence for a syntax node and semantic symbol identity.
            /// </summary>
            /// <param name="node">The syntax node that produced the declaration or relationship.</param>
            /// <param name="symbolIdentity">The semantic symbol identity associated with the node.</param>
            /// <returns>A semantic evidence record containing source path, line span, symbol context, preview, and hash.</returns>
            private SemanticEvidence CreateEvidence(SyntaxNode node, SemanticSymbolIdentity symbolIdentity)
            {
                // Evidence uses one-based line and column positions because they are intended for developer-facing source navigation.
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
