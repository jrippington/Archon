using System.Security.Cryptography;
using System.Text;
using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Extractors.DataAccess.LinqToSql;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Archon.Extractors.DataAccess.EntityFramework
{
    /// <summary>
    /// Extracts Entity Framework 6 and Entity Framework Core source artifacts into graph-ready data-access facts through static Roslyn analysis.
    /// </summary>
    public sealed class EntityFrameworkModelExtractor
    {
        /// <summary>
        /// Adds EF6 and EF Core context, entity, mapping, migration, provider, usage, raw SQL, unknown, and evidence facts to the supplied accumulator.
        /// </summary>
        /// <param name="request">The repository-scoped data-access extraction request that owns semantic documents and snapshot identity.</param>
        /// <param name="accumulator">The shared architecture snapshot accumulator that receives EF graph contributions.</param>
        /// <param name="cancellationToken">A token that signals when semantic traversal should stop.</param>
        public void Accumulate(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, CancellationToken cancellationToken = default)
        {
            // EF extraction runs after LINQ to SQL extraction in the same data-access entry path so callers receive one accumulated data-access snapshot.
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(accumulator);

            EntityFrameworkExtractionState state = new();
            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateModelFacts(request, accumulator, state, semanticDocument, cancellationToken);
            }

            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateUsageFacts(request, accumulator, state, semanticDocument, cancellationToken);
            }
        }

        /// <summary>
        /// Extracts context, entity, table, column, provider, and migration facts from one semantic source document.
        /// </summary>
        /// <param name="request">The extraction request that scopes stable keys and repository paths.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="state">The EF state used to correlate source model and usage facts.</param>
        /// <param name="semanticDocument">The Roslyn semantic document to inspect.</param>
        /// <param name="cancellationToken">A token that signals when model traversal should stop.</param>
        private static void AccumulateModelFacts(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, EntityFrameworkExtractionState state, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Model extraction first records all context and entity identities, then usage extraction can resolve method calls back to those stable keys.
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(request.RepositoryRootDirectory, semanticDocument.DocumentPath);
            string sourceText = semanticDocument.SyntaxTree.GetText(cancellationToken).ToString();
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            IReadOnlyDictionary<string, EfEntityMapping> fluentMappings = DiscoverFluentMappings(root, semanticDocument.SemanticModel, cancellationToken);
            EfProviderFact documentProvider = DiscoverDocumentProviderFact(root, semanticDocument.SemanticModel, cancellationToken);

            foreach (ClassDeclarationSyntax classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                INamedTypeSymbol? typeSymbol = semanticDocument.SemanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken) as INamedTypeSymbol;
                if (typeSymbol is null)
                {
                    continue;
                }

                EfTechnology technology = ClassifyEfTechnology(typeSymbol);
                if (technology.IsEntityFramework && !IsFrameworkStubType(typeSymbol))
                {
                    AccumulateContext(request, accumulator, state, semanticDocument, relativePath, sourceText, classDeclaration, typeSymbol, technology, documentProvider, fluentMappings, cancellationToken);
                }

                AccumulateAttributedEntity(request, accumulator, state, semanticDocument, relativePath, sourceText, classDeclaration, typeSymbol, fluentMappings);
                AccumulateMigration(request, accumulator, state, semanticDocument, relativePath, sourceText, classDeclaration, typeSymbol, technology);
            }
        }

        /// <summary>
        /// Extracts method-level EF context usage, table read/write hints, raw SQL execution, and unknowns from one semantic source document.
        /// </summary>
        /// <param name="request">The extraction request that scopes stable keys and repository paths.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="state">The EF state that resolves contexts and mapped tables.</param>
        /// <param name="semanticDocument">The Roslyn semantic document to inspect.</param>
        /// <param name="cancellationToken">A token that signals when usage traversal should stop.</param>
        private static void AccumulateUsageFacts(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, EntityFrameworkExtractionState state, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Usage extraction is conservative: it only links to known contexts and mapped tables or emits explicit unknowns for computed SQL.
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(request.RepositoryRootDirectory, semanticDocument.DocumentPath);
            string sourceText = semanticDocument.SyntaxTree.GetText(cancellationToken).ToString();
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            foreach (MethodDeclarationSyntax methodDeclaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (semanticDocument.SemanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken) is not IMethodSymbol methodSymbol || methodDeclaration.Body is null && methodDeclaration.ExpressionBody is null)
                {
                    continue;
                }

                EfMethodUsageState usageState = EfMethodUsageState.FromMethod(methodSymbol, request.SnapshotStableKey, relativePath, semanticDocument.ProjectContext);
                SeedContextParameters(state, usageState, methodSymbol);
                foreach (ObjectCreationExpressionSyntax objectCreation in methodDeclaration.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                {
                    INamedTypeSymbol? createdType = semanticDocument.SemanticModel.GetTypeInfo(objectCreation, cancellationToken).Type as INamedTypeSymbol;
                    if (createdType is not null && state.ContextKeysByTypeName.TryGetValue(createdType.ToDisplayString(), out StableKey contextStableKey))
                    {
                        EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, objectCreation, "EfContextConstruction", createdType.Name, methodSymbol.ToDisplayString(), Confidence.High, UnknownState.Known);
                        accumulator.AddEvidence(evidence).AddNode(CreateMethodNode(request.SnapshotStableKey, usageState.MethodStableKey, methodSymbol, semanticDocument.ProjectContext, evidence.StableKey, CreateMethodMetadata(relativePath, semanticDocument.ProjectContext, state.TechnologyByContextKey[contextStableKey.Value]))).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesDbContext, usageState.MethodStableKey, contextStableKey, evidence.StableKey, CreateUsageRelationshipMetadata("ContextConstruction", relativePath, semanticDocument.ProjectContext, state.TechnologyByContextKey[contextStableKey.Value], null, null, null), Confidence.High, UnknownState.Known));
                        string? variableName = GetAssignedVariableName(objectCreation);
                        if (!string.IsNullOrWhiteSpace(variableName))
                        {
                            usageState.ContextKeysByVariable[variableName] = contextStableKey;
                        }
                    }
                }

                foreach (MemberAccessExpressionSyntax memberAccess in methodDeclaration.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                {
                    AccumulateDbSetReadFromMemberAccess(request, accumulator, state, semanticDocument, relativePath, sourceText, methodSymbol, usageState, memberAccess, cancellationToken);
                }

                foreach (InvocationExpressionSyntax invocation in methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    IOperation? operation = semanticDocument.SemanticModel.GetOperation(invocation, cancellationToken);
                    IMethodSymbol? invokedMethod = operation is IInvocationOperation invocationOperation ? invocationOperation.TargetMethod : semanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                    if (invokedMethod is null)
                    {
                        continue;
                    }

                    AccumulateInvocationUsage(request, accumulator, state, semanticDocument, relativePath, sourceText, methodSymbol, usageState, invocation, invokedMethod);
                }
            }
        }

        /// <summary>
        /// Emits a DbContext or ObjectContext node and the DbSet/entity/table/column relationships declared by the context.
        /// </summary>
        private static void AccumulateContext(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, EntityFrameworkExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, ClassDeclarationSyntax classDeclaration, INamedTypeSymbol typeSymbol, EfTechnology technology, EfProviderFact documentProvider, IReadOnlyDictionary<string, EfEntityMapping> fluentMappings, CancellationToken cancellationToken)
        {
            // Context identity follows the data-access project-plus-type stable key rule and captures provider hints without storing raw connection strings.
            EfProviderFact provider = MergeProviderFacts(ExtractProviderFact(classDeclaration, typeSymbol, semanticDocument.SemanticModel, cancellationToken), documentProvider);
            EvidenceRecord contextEvidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, classDeclaration, "EfContext", typeSymbol.Name, typeSymbol.ContainingNamespace.ToDisplayString(), Confidence.Certain, UnknownState.Known);
            StableKey contextStableKey = CreateProjectScopedKey("dbcontext", semanticDocument.ProjectContext, typeSymbol.ToDisplayString());
            GraphMetadata contextMetadata = CreateContextMetadata(relativePath, semanticDocument.ProjectContext, technology, typeSymbol, provider);
            ArchitectureNode contextNode = CreateNode(request.SnapshotStableKey, contextStableKey, NodeKind.DbContext, typeSymbol.Name, typeSymbol.ToDisplayString(), "C#", null, contextEvidence.StableKey, Confidence.Certain, UnknownState.Known, contextMetadata);
            accumulator.AddEvidence(contextEvidence).AddNode(contextNode);
            state.ContextKeysByTypeName[typeSymbol.ToDisplayString()] = contextStableKey;
            state.TechnologyByContextKey[contextStableKey.Value] = technology.Value;

            foreach (IPropertySymbol propertySymbol in typeSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                INamedTypeSymbol? entityTypeSymbol = TryGetDbSetEntityTypeSymbol(propertySymbol.Type);
                string? entityTypeName = entityTypeSymbol?.ToDisplayString();
                if (entityTypeName is null)
                {
                    continue;
                }

                EfEntityMapping mapping = ResolveEntityMapping(entityTypeName, propertySymbol.Name, fluentMappings, entityTypeSymbol);
                SyntaxNode propertySyntax = propertySymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) ?? classDeclaration;
                AccumulateEntityMapping(request, accumulator, state, semanticDocument, relativePath, sourceText, propertySyntax, contextStableKey, technology, mapping);
            }

            foreach (EfEntityMapping mapping in fluentMappings.Values.Where(mapping => !state.EntityKeysByTypeName.ContainsKey(mapping.EntityTypeName)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsEntityDeclaredInDocument(mapping.EntityTypeName, semanticDocument.SemanticModel, semanticDocument.SyntaxTree, cancellationToken))
                {
                    continue;
                }

                AccumulateEntityMapping(request, accumulator, state, semanticDocument, relativePath, sourceText, classDeclaration, contextStableKey, technology, mapping);
            }
        }

        /// <summary>
        /// Emits an entity mapping from class-level table and column attributes when the entity is not already known through a context DbSet.
        /// </summary>
        private static void AccumulateAttributedEntity(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, EntityFrameworkExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, ClassDeclarationSyntax classDeclaration, INamedTypeSymbol typeSymbol, IReadOnlyDictionary<string, EfEntityMapping> fluentMappings)
        {
            // Attribute-only entities still describe useful database mapping facts even when a DbSet property is absent from the same file.
            string? tableName = GetAttributeNamedValue(typeSymbol, "Table", "Name");
            if (string.IsNullOrWhiteSpace(tableName) || state.EntityKeysByTypeName.ContainsKey(typeSymbol.ToDisplayString()))
            {
                return;
            }

            EfEntityMapping mapping = ResolveEntityMapping(typeSymbol.ToDisplayString(), typeSymbol.Name, fluentMappings, typeSymbol);
            EvidenceRecord entityEvidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, classDeclaration, "EfEntity", typeSymbol.Name, typeSymbol.ContainingNamespace.ToDisplayString(), Confidence.High, UnknownState.Known);
            AccumulateEntityAndTable(request, accumulator, state, semanticDocument.ProjectContext, relativePath, typeSymbol.ToDisplayString(), typeSymbol.Name, mapping, null, entityEvidence, technology: null);
        }

        /// <summary>
        /// Emits entity, table, column, and relationship facts for one EF entity mapping.
        /// </summary>
        private static void AccumulateEntityMapping(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, EntityFrameworkExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, SyntaxNode evidenceNode, StableKey contextStableKey, EfTechnology technology, EfEntityMapping mapping)
        {
            // A DbSet establishes that a context maps an entity; table and column confidence depends on whether explicit mapping evidence was present.
            EvidenceRecord entityEvidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, evidenceNode, "EfEntityMapping", mapping.EntityDisplayName, mapping.EntityTypeName, Confidence.High, UnknownState.Known);
            EntityTableKeys keys = AccumulateEntityAndTable(request, accumulator, state, semanticDocument.ProjectContext, relativePath, mapping.EntityTypeName, mapping.EntityDisplayName, mapping, contextStableKey, entityEvidence, technology.Value);
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsEntity, contextStableKey, keys.EntityStableKey, entityEvidence.StableKey, CreateMappingRelationshipMetadata(relativePath, semanticDocument.ProjectContext, technology.Value, "DbContextEntityMapping", null), Confidence.High, UnknownState.Known));
            if (keys.TableStableKey is not null)
            {
                accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsTable, contextStableKey, keys.TableStableKey.Value, entityEvidence.StableKey, CreateMappingRelationshipMetadata(relativePath, semanticDocument.ProjectContext, technology.Value, "DbContextTableMapping", null), Confidence.Medium, mapping.HasExplicitTable ? UnknownState.Known : UnknownState.Unknown("EF table name was inferred by convention.")));
            }

            foreach (EfRelationshipFact relationship in mapping.Relationships)
            {
                GraphMetadata relationshipMetadata = CreateMappingRelationshipMetadata(relativePath, semanticDocument.ProjectContext, technology.Value, "FluentRelationship", relationship.TargetEntityType);
                accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsEntity, keys.EntityStableKey, keys.EntityStableKey, entityEvidence.StableKey, relationshipMetadata, Confidence.Medium, UnknownState.Known));
            }
        }

        /// <summary>
        /// Emits entity, table, and column nodes and returns the stable keys needed by context and usage relationships.
        /// </summary>
        private static EntityTableKeys AccumulateEntityAndTable(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, EntityFrameworkExtractionState state, string projectContext, string relativePath, string entityTypeName, string entityDisplayName, EfEntityMapping mapping, StableKey? contextStableKey, EvidenceRecord entityEvidence, string? technology)
        {
            // Stable keys use project context rather than source file path so partial classes and multiple mapping files deduplicate deterministically.
            StableKey entityStableKey = CreateProjectScopedKey("entity", projectContext, entityTypeName);
            ArchitectureNode entityNode = CreateNode(request.SnapshotStableKey, entityStableKey, NodeKind.Entity, entityDisplayName, entityTypeName, "C#", contextStableKey, entityEvidence.StableKey, Confidence.High, UnknownState.Known, CreateEntityMetadata(relativePath, projectContext, technology, entityTypeName, mapping));
            accumulator.AddEvidence(entityEvidence).AddNode(entityNode);
            state.EntityKeysByTypeName[entityTypeName] = entityStableKey;

            StableKey? tableStableKey = null;
            if (!string.IsNullOrWhiteSpace(mapping.TableName.ObjectName))
            {
                UnknownState tableUnknown = mapping.HasExplicitTable ? UnknownState.Known : UnknownState.Unknown("EF table name was inferred from DbSet or entity convention.");
                Confidence tableConfidence = mapping.HasExplicitTable ? Confidence.High : Confidence.Medium;
                tableStableKey = CreateProjectScopedKey("dbtable", projectContext, mapping.TableName.QualifiedName);
                ArchitectureNode tableNode = CreateNode(request.SnapshotStableKey, tableStableKey.Value, NodeKind.DatabaseTable, mapping.TableName.ObjectName, mapping.TableName.QualifiedName, "Database", contextStableKey, entityEvidence.StableKey, tableConfidence, tableUnknown, CreateTableMetadata(relativePath, projectContext, technology, mapping));
                accumulator.AddNode(tableNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsTable, entityStableKey, tableStableKey.Value, entityEvidence.StableKey, CreateMappingRelationshipMetadata(relativePath, projectContext, technology, "EntityTableMapping", null), tableConfidence, tableUnknown));
                state.TableKeysByEntityTypeName[entityTypeName] = tableStableKey.Value;
                state.TableKeysByPropertyName[mapping.DbSetPropertyName] = tableStableKey.Value;
            }

            foreach (EfColumnMapping column in mapping.Columns)
            {
                StableKey? columnStableKey = AccumulateColumn(request, accumulator, projectContext, relativePath, technology, entityStableKey, tableStableKey, mapping, column, entityEvidence.StableKey);
                if (columnStableKey is not null)
                {
                    state.ColumnKeysByPropertyName[$"{entityTypeName}.{column.PropertyName}"] = columnStableKey.Value;
                }
            }

            return new EntityTableKeys(entityStableKey, tableStableKey);
        }

        /// <summary>
        /// Emits a database column node and mapping relationships for one EF property or shadow property mapping.
        /// </summary>
        private static StableKey? AccumulateColumn(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, string projectContext, string relativePath, string? technology, StableKey entityStableKey, StableKey? tableStableKey, EfEntityMapping mapping, EfColumnMapping column, StableKey evidenceStableKey)
        {
            // Columns require a table identity; unknown-state distinguishes explicit CLR properties from EF Core shadow properties.
            if (tableStableKey is null || string.IsNullOrWhiteSpace(column.ColumnName))
            {
                return null;
            }

            StableKey columnStableKey = CreateProjectScopedKey("dbcolumn", projectContext, $"{mapping.TableName.QualifiedName}.{column.ColumnName}");
            UnknownState unknownState = column.IsShadowProperty ? UnknownState.Unknown("EF Core shadow property has no CLR property declaration.") : UnknownState.Known;
            ArchitectureNode columnNode = CreateNode(request.SnapshotStableKey, columnStableKey, NodeKind.DatabaseColumn, column.ColumnName, $"{mapping.TableName.QualifiedName}.{column.ColumnName}", "Database", tableStableKey, evidenceStableKey, column.IsShadowProperty ? Confidence.Medium : Confidence.High, unknownState, CreateColumnMetadata(relativePath, projectContext, technology, mapping, column));
            accumulator.AddNode(columnNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsColumn, tableStableKey.Value, columnStableKey, evidenceStableKey, CreateMappingRelationshipMetadata(relativePath, projectContext, technology, "TableColumnMapping", null), Confidence.High, unknownState));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsColumn, entityStableKey, columnStableKey, evidenceStableKey, CreateMappingRelationshipMetadata(relativePath, projectContext, technology, "EntityColumnMapping", null), Confidence.High, unknownState));
            return columnStableKey;
        }

        /// <summary>
        /// Emits migration artifact and stored-procedure facts from EF migration classes without executing migration code.
        /// </summary>
        private static void AccumulateMigration(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, EntityFrameworkExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, ClassDeclarationSyntax classDeclaration, INamedTypeSymbol typeSymbol, EfTechnology fallbackTechnology)
        {
            // Migration source is treated as generated artifact evidence because it describes schema-changing operations rather than runtime data access.
            if (!IsEfMigration(typeSymbol))
            {
                return;
            }

            string technology = fallbackTechnology.IsEntityFramework ? fallbackTechnology.Value : InferMigrationTechnology(typeSymbol);
            foreach (InvocationExpressionSyntax invocation in classDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string? invocationName = GetInvokedMemberName(invocation);
                if (!string.Equals(invocationName, "CreateTable", StringComparison.Ordinal) && !string.Equals(invocationName, "Sql", StringComparison.Ordinal))
                {
                    continue;
                }

                EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, "EfMigrationOperation", typeSymbol.Name, typeSymbol.ToDisplayString(), Confidence.High, UnknownState.Known);
                string? operationArgument = GetFirstStringArgument(invocation, semanticDocument.SemanticModel);
                StableKey artifactKey = new($"generatedartifact://{HashStablePayload(semanticDocument.ProjectContext, typeSymbol.ToDisplayString(), invocation.SpanStart.ToString(), invocationName, operationArgument)}");
                string migrationOperation = invocationName ?? "UnknownMigrationOperation";
                ArchitectureNode artifactNode = CreateNode(request.SnapshotStableKey, artifactKey, NodeKind.GeneratedArtifact, $"{typeSymbol.Name}.{migrationOperation}", typeSymbol.ToDisplayString(), "C#", null, evidence.StableKey, Confidence.High, UnknownState.Known, CreateMigrationMetadata(relativePath, semanticDocument.ProjectContext, technology, typeSymbol.Name, migrationOperation, operationArgument));
                accumulator.AddEvidence(evidence).AddNode(artifactNode);

                string? procedureName = TryExtractStoredProcedureName(operationArgument);
                if (!string.IsNullOrWhiteSpace(procedureName))
                {
                    ParsedDatabaseObjectName parsedProcedure = ParseDatabaseObjectName(procedureName);
                    StableKey procedureKey = CreateProjectScopedKey("storedprocedure", semanticDocument.ProjectContext, parsedProcedure.QualifiedName);
                    ArchitectureNode procedureNode = CreateNode(request.SnapshotStableKey, procedureKey, NodeKind.StoredProcedure, parsedProcedure.ObjectName, parsedProcedure.QualifiedName, "Database", artifactKey, evidence.StableKey, Confidence.Medium, UnknownState.Known, CreateStoredProcedureMetadata(relativePath, semanticDocument.ProjectContext, technology, parsedProcedure, typeSymbol.Name));
                    accumulator.AddNode(procedureNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.CallsStoredProcedure, artifactKey, procedureKey, evidence.StableKey, CreateMappingRelationshipMetadata(relativePath, semanticDocument.ProjectContext, technology, "MigrationSqlStoredProcedure", null), Confidence.Medium, UnknownState.Known));
                    state.StoredProcedureKeysByName[parsedProcedure.QualifiedName] = procedureKey;
                }
            }
        }

        /// <summary>
        /// Seeds method usage state with EF context parameters supplied to the method.
        /// </summary>
        private static void SeedContextParameters(EntityFrameworkExtractionState state, EfMethodUsageState usageState, IMethodSymbol methodSymbol)
        {
            // Parameter seeding lets repository methods that receive DbContext through dependency injection still produce USES_DB_CONTEXT edges.
            foreach (IParameterSymbol parameter in methodSymbol.Parameters)
            {
                if (state.ContextKeysByTypeName.TryGetValue(parameter.Type.ToDisplayString(), out StableKey contextStableKey))
                {
                    usageState.ContextKeysByVariable[parameter.Name] = contextStableKey;
                }
            }
        }

        /// <summary>
        /// Emits read hints for DbSet property member access observed inside query chains.
        /// </summary>
        private static void AccumulateDbSetReadFromMemberAccess(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, EntityFrameworkExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol methodSymbol, EfMethodUsageState usageState, MemberAccessExpressionSyntax memberAccess, CancellationToken cancellationToken)
        {
            // A DbSet property reference is a conservative read hint because EF query execution may happen later when the IQueryable is enumerated.
            ISymbol? symbol = semanticDocument.SemanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol;
            if (symbol is not IPropertySymbol propertySymbol || !state.TableKeysByPropertyName.TryGetValue(propertySymbol.Name, out StableKey tableStableKey))
            {
                return;
            }

            string? receiverName = memberAccess.Expression is IdentifierNameSyntax identifier ? identifier.Identifier.ValueText : null;
            StableKey? contextStableKey = receiverName is not null && usageState.ContextKeysByVariable.TryGetValue(receiverName, out StableKey variableContextKey) ? variableContextKey : null;
            string technology = ResolveTechnology(state, contextStableKey, tableStableKey);
            EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, memberAccess, "EfDbSetRead", propertySymbol.Name, methodSymbol.ToDisplayString(), Confidence.Medium, UnknownState.Known);
            ArchitectureNode methodNode = CreateMethodNode(request.SnapshotStableKey, usageState.MethodStableKey, methodSymbol, semanticDocument.ProjectContext, evidence.StableKey, CreateMethodMetadata(relativePath, semanticDocument.ProjectContext, technology));
            accumulator.AddEvidence(evidence).AddNode(methodNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.ReadsTable, usageState.MethodStableKey, tableStableKey, evidence.StableKey, CreateUsageRelationshipMetadata("DbSetQuery", relativePath, semanticDocument.ProjectContext, technology, "Read", null, null), Confidence.Medium, UnknownState.Known));
            if (contextStableKey is not null)
            {
                accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.UsesDbContext, usageState.MethodStableKey, contextStableKey.Value, evidence.StableKey, CreateUsageRelationshipMetadata("DbContextParameterUsage", relativePath, semanticDocument.ProjectContext, technology, null, null, null), Confidence.High, UnknownState.Known));
            }
        }

        /// <summary>
        /// Classifies and emits graph facts for one EF invocation expression.
        /// </summary>
        private static void AccumulateInvocationUsage(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, EntityFrameworkExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol containingMethod, EfMethodUsageState usageState, InvocationExpressionSyntax invocation, IMethodSymbol invokedMethod)
        {
            // Invocation classification keeps EF6 and EF Core API names in metadata so later graph queries can distinguish raw SQL and save operations.
            string methodName = invokedMethod.Name;
            if (string.Equals(methodName, "Add", StringComparison.Ordinal) || string.Equals(methodName, "Remove", StringComparison.Ordinal))
            {
                AccumulateDbSetWriteUsage(request, accumulator, state, semanticDocument, relativePath, sourceText, containingMethod, usageState, invocation, methodName);
                return;
            }

            if (string.Equals(methodName, "SaveChanges", StringComparison.Ordinal) || string.Equals(methodName, "SaveChangesAsync", StringComparison.Ordinal))
            {
                AccumulateSaveChangesUsage(request, accumulator, state, semanticDocument, relativePath, sourceText, containingMethod, usageState, invocation, methodName);
                return;
            }

            if (IsRawSqlApi(methodName))
            {
                AccumulateRawSqlUsage(request, accumulator, state, semanticDocument, relativePath, sourceText, containingMethod, usageState, invocation, methodName);
            }
        }

        /// <summary>
        /// Emits write hints for DbSet Add and Remove calls.
        /// </summary>
        private static void AccumulateDbSetWriteUsage(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, EntityFrameworkExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol containingMethod, EfMethodUsageState usageState, InvocationExpressionSyntax invocation, string commandApi)
        {
            // DbSet mutation calls mark the target table as written even though EF sends the database command later through SaveChanges.
            StableKey? tableStableKey = TryResolveTableFromInvocationReceiver(state, invocation);
            if (tableStableKey is null)
            {
                return;
            }

            string technology = ResolveTechnology(state, null, tableStableKey.Value);
            EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, "EfDbSetWrite", commandApi, containingMethod.ToDisplayString(), Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(evidence).AddNode(CreateMethodNode(request.SnapshotStableKey, usageState.MethodStableKey, containingMethod, semanticDocument.ProjectContext, evidence.StableKey, CreateMethodMetadata(relativePath, semanticDocument.ProjectContext, technology))).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.WritesTable, usageState.MethodStableKey, tableStableKey.Value, evidence.StableKey, CreateUsageRelationshipMetadata("DbSetMutation", relativePath, semanticDocument.ProjectContext, technology, "Write", commandApi, null), Confidence.High, UnknownState.Known));
            usageState.WrittenTableKeys.Add(tableStableKey.Value);
        }

        /// <summary>
        /// Emits write hints for SaveChanges and SaveChangesAsync calls using tables already touched by the method when available.
        /// </summary>
        private static void AccumulateSaveChangesUsage(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, EntityFrameworkExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol containingMethod, EfMethodUsageState usageState, InvocationExpressionSyntax invocation, string commandApi)
        {
            // SaveChanges is a unit-of-work boundary; if exact tables are not known, all known tables for the receiver context are conservative write candidates.
            EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, "EfSaveChanges", commandApi, containingMethod.ToDisplayString(), Confidence.Medium, UnknownState.Known);
            accumulator.AddEvidence(evidence).AddNode(CreateMethodNode(request.SnapshotStableKey, usageState.MethodStableKey, containingMethod, semanticDocument.ProjectContext, evidence.StableKey, CreateMethodMetadata(relativePath, semanticDocument.ProjectContext, null)));
            IEnumerable<StableKey> targetTables = usageState.WrittenTableKeys.Count > 0 ? usageState.WrittenTableKeys : state.TableKeysByEntityTypeName.Values;
            foreach (StableKey tableStableKey in targetTables.Distinct())
            {
                string technology = ResolveTechnology(state, null, tableStableKey);
                accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.WritesTable, usageState.MethodStableKey, tableStableKey, evidence.StableKey, CreateUsageRelationshipMetadata("SaveChanges", relativePath, semanticDocument.ProjectContext, technology, "Write", commandApi, null), Confidence.Medium, UnknownState.Known));
            }
        }

        /// <summary>
        /// Emits raw SQL nodes and execution relationships for EF6 and EF Core SQL APIs.
        /// </summary>
        private static void AccumulateRawSqlUsage(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, EntityFrameworkExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol containingMethod, EfMethodUsageState usageState, InvocationExpressionSyntax invocation, string commandApi)
        {
            // SQL text is redacted and shortened before entering evidence or metadata; computed SQL becomes an explicit unknown instead of speculative parsing.
            EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, "EfRawSql", commandApi, containingMethod.ToDisplayString(), Confidence.Medium, UnknownState.Known);
            string? sqlPreview = GetFirstStringArgument(invocation, semanticDocument.SemanticModel);
            bool computedSql = string.Equals(sqlPreview, "[ComputedSql]", StringComparison.Ordinal);
            string redactedPreview = Redact(sqlPreview ?? string.Empty);
            string readWriteHint = InferReadWriteHint(commandApi, redactedPreview, computedSql);
            string technology = InferTechnologyFromRawSqlApi(commandApi);
            UnknownState unknownState = computedSql ? UnknownState.Unknown("EF raw SQL text is computed and cannot be statically resolved.") : UnknownState.Known;
            StableKey rawSqlKey = new($"rawsql://{HashStablePayload(relativePath, containingMethod.ToDisplayString(), invocation.SpanStart.ToString(), commandApi)}");
            ArchitectureNode rawSqlNode = CreateNode(request.SnapshotStableKey, rawSqlKey, NodeKind.SqlScript, commandApi, null, "SQL", usageState.MethodStableKey, evidence.StableKey, computedSql ? Confidence.Low : Confidence.Medium, unknownState, CreateRawSqlMetadata(relativePath, semanticDocument.ProjectContext, technology, commandApi, redactedPreview, computedSql));
            accumulator.AddEvidence(evidence).AddNode(CreateMethodNode(request.SnapshotStableKey, usageState.MethodStableKey, containingMethod, semanticDocument.ProjectContext, evidence.StableKey, CreateMethodMetadata(relativePath, semanticDocument.ProjectContext, technology))).AddNode(rawSqlNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.ExecutesRawSql, usageState.MethodStableKey, rawSqlKey, evidence.StableKey, CreateUsageRelationshipMetadata("RawSqlExecution", relativePath, semanticDocument.ProjectContext, technology, readWriteHint, commandApi, computedSql ? "ComputedSql" : null), computedSql ? Confidence.Low : Confidence.Medium, unknownState));
            if (computedSql)
            {
                accumulator.AddWarning($"EF raw SQL call {commandApi} in {relativePath} uses computed SQL text and was recorded as an explicit unknown.");
            }
        }

        /// <summary>
        /// Discovers deterministic Fluent API mappings from OnModelCreating-style source chains.
        /// </summary>
        private static IReadOnlyDictionary<string, EfEntityMapping> DiscoverFluentMappings(SyntaxNode root, Microsoft.CodeAnalysis.SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            // The parser recognizes common Entity<T>().ToTable, Property(...).HasColumnName, shadow Property<T>(string), and HasMany<T>() shapes without executing model-building code.
            Dictionary<string, EfEntityMapping> mappings = new(StringComparer.Ordinal);
            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? memberName = GetInvokedMemberName(invocation);
                string? entityTypeName = TryFindEntityGenericTypeName(invocation, semanticModel, cancellationToken);
                if (string.IsNullOrWhiteSpace(entityTypeName))
                {
                    continue;
                }

                EfEntityMapping mapping = GetOrAddMapping(mappings, entityTypeName, GetDisplayName(entityTypeName), GetDisplayName(entityTypeName));
                if (string.Equals(memberName, "ToTable", StringComparison.Ordinal))
                {
                    string? tableName = GetArgumentString(invocation, 0, semanticModel);
                    string? schemaName = GetArgumentString(invocation, 1, semanticModel) ?? GetNamedArgumentString(invocation, "schema", semanticModel);
                    if (!string.IsNullOrWhiteSpace(tableName))
                    {
                        mapping.SetTable(ParseDatabaseObjectName(tableName, schemaName), hasExplicitTable: true);
                    }
                }
                else if (string.Equals(memberName, "HasColumnName", StringComparison.Ordinal))
                {
                    string? propertyName = TryFindPropertyName(invocation, semanticModel, cancellationToken);
                    string? columnName = GetArgumentString(invocation, 0, semanticModel);
                    bool isShadowProperty = IsShadowPropertyChain(invocation);
                    if (!string.IsNullOrWhiteSpace(propertyName) && !string.IsNullOrWhiteSpace(columnName))
                    {
                        mapping.AddColumn(new EfColumnMapping(propertyName, columnName.Trim(), isShadowProperty));
                    }
                }
                else if (string.Equals(memberName, "HasMany", StringComparison.Ordinal) && invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } && genericName.TypeArgumentList.Arguments.Count == 1)
                {
                    mapping.AddRelationship(new EfRelationshipFact(genericName.TypeArgumentList.Arguments[0].ToString()));
                }
            }

            return mappings;
        }

        /// <summary>
        /// Emits a source evidence record for EF model or usage syntax.
        /// </summary>
        private static EvidenceRecord CreateSourceEvidence(StableKey snapshotStableKey, string repositoryRootDirectory, string documentPath, string sourceText, SyntaxNode syntaxNode, string role, string? symbolName, string? containingSymbol, Confidence confidence, UnknownState unknownState)
        {
            // Evidence uses redacted source snippets and line spans so graph facts can be traced without exposing connection-string secrets.
            FileLinePositionSpan lineSpan = syntaxNode.SyntaxTree.GetLineSpan(syntaxNode.Span);
            int startLine = lineSpan.StartLinePosition.Line + 1;
            int endLine = lineSpan.EndLinePosition.Line + 1;
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRootDirectory, documentPath);
            string preview = Redact(syntaxNode.ToString());
            if (preview.Length > 240)
            {
                preview = preview[..240];
            }

            string snippetHash = HashStablePayload(preview);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "SourceUsage",
                ["evidenceRole"] = role,
                ["extractor"] = nameof(EntityFrameworkModelExtractor),
                ["sourceLine"] = startLine,
                ["sourceEndLine"] = endLine
            });
            StableKey stableKey = new($"ef-source-evidence://{HashStablePayload(relativePath, role, symbolName, containingSymbol, startLine.ToString(), endLine.ToString(), snippetHash)}");
            return new EvidenceRecord(snapshotStableKey, stableKey, EvidenceKind.SourceCode, RepositoryRelativePath.Parse(relativePath), startLine, endLine, symbolName, containingSymbol, snippetHash, preview, KnowledgeKind.Fact, confidence, unknownState, metadata, FingerprintGenerator.ForEvidence(EvidenceKind.SourceCode, relativePath, startLine, endLine, symbolName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates an architecture node using shared graph contracts and deterministic fingerprint input.
        /// </summary>
        private static ArchitectureNode CreateNode(StableKey snapshotStableKey, StableKey stableKey, NodeKind nodeKind, string displayName, string? qualifiedName, string language, StableKey? parentNodeStableKey, StableKey primaryEvidenceStableKey, Confidence confidence, UnknownState unknownState, GraphMetadata metadata)
        {
            // Search names mirror qualified names when present so graph consumers can locate EF model facts consistently.
            string searchName = string.IsNullOrWhiteSpace(qualifiedName) ? displayName : qualifiedName;
            return new ArchitectureNode(snapshotStableKey, stableKey, nodeKind, displayName, qualifiedName, searchName, language, null, parentNodeStableKey, KnowledgeKind.Fact, null, null, confidence, unknownState, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForNode(nodeKind, displayName, qualifiedName, searchName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates an architecture edge using shared graph contracts and deterministic fingerprint input.
        /// </summary>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, EdgeKind edgeKind, StableKey sourceStableKey, StableKey targetStableKey, StableKey primaryEvidenceStableKey, GraphMetadata metadata, Confidence confidence, UnknownState unknownState)
        {
            // Edge identity includes endpoints, relationship kind, metadata, and unknown-state so duplicate observations merge deterministically.
            StableKey stableKey = new($"ef-edge://{HashStablePayload(edgeKind.Value, sourceStableKey.Value, targetStableKey.Value, metadata.ToCanonicalJson(), unknownState.HasUnknownData.ToString())}");
            return new ArchitectureEdge(snapshotStableKey, stableKey, edgeKind, sourceStableKey, targetStableKey, true, KnowledgeKind.Fact, confidence, unknownState, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForEdge(edgeKind, sourceStableKey, targetStableKey, true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a source method node for an EF usage site.
        /// </summary>
        private static ArchitectureNode CreateMethodNode(StableKey snapshotStableKey, StableKey stableKey, IMethodSymbol methodSymbol, string projectContext, StableKey primaryEvidenceStableKey, GraphMetadata metadata)
        {
            // Method nodes reuse the same shape as existing data-access source usage extraction so table and SQL relationships can share one source node.
            string qualifiedName = methodSymbol.ToDisplayString();
            return new ArchitectureNode(snapshotStableKey, stableKey, NodeKind.Method, methodSymbol.Name, qualifiedName, qualifiedName, "C#", new StableKey($"project://{projectContext}"), null, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Method, methodSymbol.Name, qualifiedName, qualifiedName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates deterministic metadata for EF context nodes.
        /// </summary>
        private static GraphMetadata CreateContextMetadata(string relativePath, string projectContext, EfTechnology technology, INamedTypeSymbol typeSymbol, EfProviderFact provider)
        {
            // Context metadata records framework, provider, and connection-key hints but intentionally omits raw connection strings.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, technology.Value);
            values["contextType"] = typeSymbol.ToDisplayString();
            values["contextKind"] = technology.ContextKind;
            values["provider"] = provider.Provider;
            values["providerConfigurationCall"] = provider.ProviderConfigurationCall;
            values["connectionStringKey"] = provider.ConnectionStringKey;
            values["connectionStringRedacted"] = provider.ConnectionStringRedacted;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for EF entity nodes.
        /// </summary>
        private static GraphMetadata CreateEntityMetadata(string relativePath, string projectContext, string? technology, string entityTypeName, EfEntityMapping mapping)
        {
            // Entity metadata keeps CLR identity and mapping source details separate from database table identity.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, technology);
            values["entityType"] = entityTypeName;
            values["tableName"] = mapping.TableName.ObjectName;
            values["schemaName"] = mapping.TableName.SchemaName;
            values["detectionMode"] = mapping.HasExplicitTable ? "SourceMapping" : "ConventionMapping";
            values["dataAccessUnknownReason"] = mapping.HasExplicitTable ? null : "ConventionOnlyMapping";
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for EF database table nodes.
        /// </summary>
        private static GraphMetadata CreateTableMetadata(string relativePath, string projectContext, string? technology, EfEntityMapping mapping)
        {
            // Table metadata records explicit or convention-derived table identity with confidence expressed on the node itself.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, technology);
            values["schemaName"] = mapping.TableName.SchemaName;
            values["tableName"] = mapping.TableName.ObjectName;
            values["entityType"] = mapping.EntityTypeName;
            values["detectionMode"] = mapping.HasExplicitTable ? "SourceMapping" : "ConventionMapping";
            values["dataAccessUnknownReason"] = mapping.HasExplicitTable ? null : "ConventionOnlyMapping";
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for EF database column nodes.
        /// </summary>
        private static GraphMetadata CreateColumnMetadata(string relativePath, string projectContext, string? technology, EfEntityMapping mapping, EfColumnMapping column)
        {
            // Column metadata distinguishes normal CLR property mappings from EF Core shadow-property mappings.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, technology);
            values["schemaName"] = mapping.TableName.SchemaName;
            values["tableName"] = mapping.TableName.ObjectName;
            values["columnName"] = column.ColumnName;
            values["propertyName"] = column.PropertyName;
            values["detectionMode"] = column.IsShadowProperty ? "ShadowPropertyMapping" : "SourceMapping";
            values["dataAccessUnknownReason"] = column.IsShadowProperty ? "ShadowProperty" : null;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for EF mapping relationships.
        /// </summary>
        private static GraphMetadata CreateMappingRelationshipMetadata(string relativePath, string projectContext, string? technology, string relationshipKind, string? targetEntityType)
        {
            // Mapping relationship metadata refines controlled edge kinds without creating EF-specific edge kinds.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, technology);
            values["dataAccessRelationshipKind"] = relationshipKind;
            values["targetEntityType"] = targetEntityType;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for EF source method usage nodes.
        /// </summary>
        private static GraphMetadata CreateMethodMetadata(string relativePath, string projectContext, string? technology)
        {
            // Method metadata marks source usage and keeps the project context available for graph consumers.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, technology);
            values["detectionMode"] = "SourceUsage";
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for EF source usage relationships.
        /// </summary>
        private static GraphMetadata CreateUsageRelationshipMetadata(string relationshipKind, string relativePath, string projectContext, string? technology, string? readWriteHint, string? commandApi, string? unknownReason)
        {
            // Usage metadata records API names and read/write hints while leaving exact SQL interpretation conservative.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, technology);
            values["detectionMode"] = "SourceUsage";
            values["dataAccessRelationshipKind"] = relationshipKind;
            values["readWriteHint"] = readWriteHint;
            values["commandApi"] = commandApi;
            values["dataAccessUnknownReason"] = unknownReason;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for EF raw SQL nodes.
        /// </summary>
        private static GraphMetadata CreateRawSqlMetadata(string relativePath, string projectContext, string technology, string commandApi, string sqlPreview, bool computedSql)
        {
            // SQL metadata stores a redacted preview and hash so graph consumers can group similar commands without seeing secrets.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, technology);
            values["detectionMode"] = "SourceUsage";
            values["commandApi"] = commandApi;
            values["sqlPreview"] = computedSql ? null : sqlPreview;
            values["sqlTextHash"] = computedSql ? null : HashStablePayload(sqlPreview);
            values["isDynamicSql"] = computedSql;
            values["dataAccessUnknownReason"] = computedSql ? "ComputedSql" : null;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for EF migration generated artifact nodes.
        /// </summary>
        private static GraphMetadata CreateMigrationMetadata(string relativePath, string projectContext, string technology, string migrationName, string migrationOperation, string? operationArgument)
        {
            // Migration metadata records schema operation names without executing or applying migrations.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, technology);
            values["detectionMode"] = "MigrationSource";
            values["migrationName"] = migrationName;
            values["migrationOperation"] = migrationOperation;
            values["operationPreview"] = Redact(operationArgument ?? string.Empty);
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for stored procedures discovered in migration SQL.
        /// </summary>
        private static GraphMetadata CreateStoredProcedureMetadata(string relativePath, string projectContext, string technology, ParsedDatabaseObjectName procedureName, string migrationName)
        {
            // Migration SQL can create stored procedures, so the fact is captured as a stored procedure node with migration evidence.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, technology);
            values["detectionMode"] = "MigrationSource";
            values["schemaName"] = procedureName.SchemaName;
            values["storedProcedureName"] = procedureName.ObjectName;
            values["migrationName"] = migrationName;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates shared lower-camel metadata fields for EF graph facts.
        /// </summary>
        private static Dictionary<string, object?> CreateBaseMetadata(string relativePath, string projectContext, string? technology)
        {
            // Shared metadata keeps EF facts aligned with data-access lower-camel metadata naming and source provenance rules.
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["detectionMode"] = "SourceMapping",
                ["extractor"] = nameof(EntityFrameworkModelExtractor),
                ["modelFilePath"] = relativePath,
                ["projectContext"] = projectContext,
                ["dataAccessTechnology"] = technology ?? "EntityFramework",
                ["framework"] = technology ?? "EntityFramework"
            };
        }

        /// <summary>
        /// Removes null metadata values before canonical metadata creation.
        /// </summary>
        private static IReadOnlyDictionary<string, object?> RemoveNullValues(Dictionary<string, object?> values)
        {
            // Omitting absent EF metadata avoids implying that unresolved providers, schemas, or SQL text were known.
            return values.Where(static pair => pair.Value is not null).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        }

        /// <summary>
        /// Resolves a mapping for an entity from Fluent API, attributes, DbSet names, or convention fallback.
        /// </summary>
        private static EfEntityMapping ResolveEntityMapping(string entityTypeName, string dbSetPropertyName, IReadOnlyDictionary<string, EfEntityMapping> fluentMappings, INamedTypeSymbol? entitySymbol)
        {
            // Explicit Fluent API mapping wins over attributes; attributes win over DbSet conventions; all paths remain deterministic.
            if (fluentMappings.TryGetValue(entityTypeName, out EfEntityMapping? fluentMapping))
            {
                fluentMapping.DbSetPropertyName = dbSetPropertyName;
                return fluentMapping;
            }

            string displayName = GetDisplayName(entityTypeName);
            EfEntityMapping mapping = new(entityTypeName, displayName, dbSetPropertyName);
            if (entitySymbol is not null)
            {
                string? tableName = GetAttributeNamedValue(entitySymbol, "Table", "Name");
                string? schemaName = GetAttributeNamedValue(entitySymbol, "Table", "Schema");
                if (!string.IsNullOrWhiteSpace(tableName))
                {
                    mapping.SetTable(ParseDatabaseObjectName(tableName, schemaName), hasExplicitTable: true);
                }

                foreach (IPropertySymbol propertySymbol in entitySymbol.GetMembers().OfType<IPropertySymbol>())
                {
                    string? columnName = GetAttributeNamedValue(propertySymbol, "Column", "Name");
                    if (!string.IsNullOrWhiteSpace(columnName))
                    {
                        mapping.AddColumn(new EfColumnMapping(propertySymbol.Name, columnName.Trim(), IsShadowProperty: false));
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(mapping.TableName.ObjectName))
            {
                mapping.SetTable(ParseDatabaseObjectName(dbSetPropertyName, null), hasExplicitTable: false);
            }

            return mapping;
        }

        /// <summary>
        /// Gets an existing mapping by entity type name or creates a new convention mapping.
        /// </summary>
        private static EfEntityMapping GetOrAddMapping(Dictionary<string, EfEntityMapping> mappings, string entityTypeName, string entityDisplayName, string dbSetPropertyName)
        {
            // Fluent API calls for the same entity are encountered across multiple invocation chains and must accumulate into one mapping object.
            if (!mappings.TryGetValue(entityTypeName, out EfEntityMapping? mapping))
            {
                mapping = new EfEntityMapping(entityTypeName, entityDisplayName, dbSetPropertyName);
                mapping.SetTable(ParseDatabaseObjectName(dbSetPropertyName, null), hasExplicitTable: false);
                mappings[entityTypeName] = mapping;
            }

            return mapping;
        }

        /// <summary>
        /// Extracts safe provider and connection-key metadata from EF constructors and provider configuration calls.
        /// </summary>
        private static EfProviderFact ExtractProviderFact(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol typeSymbol, Microsoft.CodeAnalysis.SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            // Raw connection strings are never persisted; only provider API names and name= configuration keys are preserved.
            string? providerCall = null;
            string? provider = null;
            string? connectionStringKey = null;
            bool redacted = false;

            foreach (ConstructorDeclarationSyntax constructor in classDeclaration.Members.OfType<ConstructorDeclarationSyntax>())
            {
                foreach (ArgumentSyntax argument in constructor.Initializer?.ArgumentList.Arguments ?? [])
                {
                    string? value = GetConstantString(argument.Expression, semanticModel);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    connectionStringKey ??= ExtractConnectionStringKey(value);
                    redacted |= LooksLikeConnectionString(value);
                }
            }

            foreach (InvocationExpressionSyntax invocation in classDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? methodName = GetInvokedMemberName(invocation);
                if (IsProviderConfigurationCall(methodName))
                {
                    providerCall ??= methodName;
                    provider ??= ProviderFromCall(methodName);
                    string? value = GetArgumentString(invocation, 0, semanticModel);
                    connectionStringKey ??= ExtractConnectionStringKey(value);
                    redacted |= LooksLikeConnectionString(value);
                }
                else if (string.Equals(methodName, "SetProviderServices", StringComparison.Ordinal))
                {
                    providerCall ??= methodName;
                    string? invariantName = GetArgumentString(invocation, 0, semanticModel);
                    provider ??= NormalizeProvider(invariantName);
                }
            }

            if (string.Equals(typeSymbol.BaseType?.Name, "ObjectContext", StringComparison.Ordinal))
            {
                provider ??= "Unknown";
            }

            return new EfProviderFact(provider ?? "Unknown", providerCall, connectionStringKey, redacted);
        }

        /// <summary>
        /// Discovers provider configuration metadata anywhere in the source document for contexts configured by nearby DbConfiguration or startup-style code.
        /// </summary>
        /// <param name="root">The syntax root whose invocations should be inspected.</param>
        /// <param name="semanticModel">The semantic model used to read constant argument values.</param>
        /// <param name="cancellationToken">A token that signals when source traversal should stop.</param>
        /// <returns>Secret-safe provider metadata observed at document scope.</returns>
        private static EfProviderFact DiscoverDocumentProviderFact(SyntaxNode root, Microsoft.CodeAnalysis.SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            // EF6 provider configuration can live in a separate DbConfiguration class, while EF Core provider calls normally live on the context itself.
            string? providerCall = null;
            string? provider = null;
            string? connectionStringKey = null;
            bool redacted = false;
            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? methodName = GetInvokedMemberName(invocation);
                if (IsProviderConfigurationCall(methodName))
                {
                    providerCall ??= methodName;
                    provider ??= ProviderFromCall(methodName);
                    string? value = GetArgumentString(invocation, 0, semanticModel);
                    connectionStringKey ??= ExtractConnectionStringKey(value);
                    redacted |= LooksLikeConnectionString(value);
                }
                else if (string.Equals(methodName, "SetProviderServices", StringComparison.Ordinal))
                {
                    providerCall ??= methodName;
                    provider ??= NormalizeProvider(GetArgumentString(invocation, 0, semanticModel));
                }
            }

            return new EfProviderFact(provider ?? "Unknown", providerCall, connectionStringKey, redacted);
        }

        /// <summary>
        /// Merges context-local provider evidence with document-level fallback evidence.
        /// </summary>
        /// <param name="primary">The provider evidence found on or inside the context.</param>
        /// <param name="fallback">The provider evidence found elsewhere in the same source document.</param>
        /// <returns>A provider fact that prefers context-local values but fills gaps from document-level evidence.</returns>
        private static EfProviderFact MergeProviderFacts(EfProviderFact primary, EfProviderFact fallback)
        {
            // Provider information often appears away from the context type, so fallback values improve detection without persisting unsafe connection strings.
            string provider = string.Equals(primary.Provider, "Unknown", StringComparison.Ordinal) ? fallback.Provider : primary.Provider;
            string? providerCall = primary.ProviderConfigurationCall ?? fallback.ProviderConfigurationCall;
            string? connectionStringKey = primary.ConnectionStringKey ?? fallback.ConnectionStringKey;
            bool redacted = primary.ConnectionStringRedacted || fallback.ConnectionStringRedacted;
            return new EfProviderFact(provider, providerCall, connectionStringKey, redacted);
        }

        /// <summary>
        /// Determines whether a recognized EF context symbol is a framework stub or external framework base class rather than an application context.
        /// </summary>
        /// <param name="typeSymbol">The context-like type symbol to inspect.</param>
        /// <returns><see langword="true" /> when the symbol belongs to an EF framework namespace; otherwise, <see langword="false" />.</returns>
        private static bool IsFrameworkStubType(INamedTypeSymbol typeSymbol)
        {
            // Test and source stubs define EF base classes in framework namespaces; those are not application data-access contexts.
            string namespaceName = typeSymbol.ContainingNamespace.ToDisplayString();
            return string.Equals(namespaceName, "System.Data.Entity", StringComparison.Ordinal)
                || string.Equals(namespaceName, "System.Data.Entity.Core.Objects", StringComparison.Ordinal)
                || string.Equals(namespaceName, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal);
        }

        /// <summary>
        /// Classifies an EF context technology from base types and namespaces.
        /// </summary>
        private static EfTechnology ClassifyEfTechnology(INamedTypeSymbol typeSymbol)
        {
            // Namespace and base-type checks avoid requiring real EF assemblies; source stubs and restored packages both produce the same symbol shape.
            for (INamedTypeSymbol? current = typeSymbol; current is not null; current = current.BaseType)
            {
                string namespaceName = current.ContainingNamespace.ToDisplayString();
                if (string.Equals(current.Name, "DbContext", StringComparison.Ordinal) && string.Equals(namespaceName, "System.Data.Entity", StringComparison.Ordinal))
                {
                    return new EfTechnology("EntityFramework6", "DbContext");
                }

                if (string.Equals(current.Name, "ObjectContext", StringComparison.Ordinal) && string.Equals(namespaceName, "System.Data.Entity.Core.Objects", StringComparison.Ordinal))
                {
                    return new EfTechnology("EntityFramework6", "ObjectContext");
                }

                if (string.Equals(current.Name, "DbContext", StringComparison.Ordinal) && string.Equals(namespaceName, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
                {
                    return new EfTechnology("EntityFrameworkCore", "DbContext");
                }
            }

            return EfTechnology.None;
        }

        /// <summary>
        /// Determines whether a type symbol represents an EF migration class.
        /// </summary>
        private static bool IsEfMigration(INamedTypeSymbol typeSymbol)
        {
            // EF6 and EF Core use different migration base types but both are source-level schema artifacts for data-access.
            for (INamedTypeSymbol? current = typeSymbol; current is not null; current = current.BaseType)
            {
                string namespaceName = current.ContainingNamespace.ToDisplayString();
                if (string.Equals(current.Name, "DbMigration", StringComparison.Ordinal) && string.Equals(namespaceName, "System.Data.Entity.Migrations", StringComparison.Ordinal))
                {
                    return true;
                }

                if (string.Equals(current.Name, "Migration", StringComparison.Ordinal) && string.Equals(namespaceName, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Infers migration technology from the base type when no context classification was available.
        /// </summary>
        private static string InferMigrationTechnology(INamedTypeSymbol typeSymbol)
        {
            // Migration classes generally inherit from framework-specific base classes even when they do not reference a context directly.
            for (INamedTypeSymbol? current = typeSymbol; current is not null; current = current.BaseType)
            {
                if (string.Equals(current.ContainingNamespace.ToDisplayString(), "Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
                {
                    return "EntityFrameworkCore";
                }
            }

            return "EntityFramework6";
        }

        /// <summary>
        /// Gets the entity type name from an EF DbSet&lt;TEntity&gt; type.
        /// </summary>
        private static INamedTypeSymbol? TryGetDbSetEntityTypeSymbol(ITypeSymbol typeSymbol)
        {
            // Both EF6 and EF Core expose DbSet<T>, so namespace classification is not needed for the generic entity extraction itself.
            if (typeSymbol is INamedTypeSymbol namedType && string.Equals(namedType.Name, "DbSet", StringComparison.Ordinal) && namedType.TypeArguments.Length == 1 && namedType.TypeArguments[0] is INamedTypeSymbol entityTypeSymbol)
            {
                return entityTypeSymbol;
            }

            return null;
        }

        /// <summary>
        /// Determines whether the named EF provider setup method is known.
        /// </summary>
        private static bool IsProviderConfigurationCall(string? methodName)
        {
            // These names cover the data-access-required providers and are extensible without changing graph contracts.
            return string.Equals(methodName, "UseSqlServer", StringComparison.Ordinal)
                || string.Equals(methodName, "UseSqlite", StringComparison.Ordinal)
                || string.Equals(methodName, "UseNpgsql", StringComparison.Ordinal)
                || string.Equals(methodName, "UseNpgsql", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether an invocation method name is an EF raw SQL API.
        /// </summary>
        private static bool IsRawSqlApi(string methodName)
        {
            // EF6 and EF Core expose different raw SQL names, all represented as EXECUTES_RAW_SQL relationships.
            return string.Equals(methodName, "SqlQuery", StringComparison.Ordinal)
                || string.Equals(methodName, "ExecuteSqlCommand", StringComparison.Ordinal)
                || string.Equals(methodName, "FromSql", StringComparison.Ordinal)
                || string.Equals(methodName, "FromSqlRaw", StringComparison.Ordinal)
                || string.Equals(methodName, "FromSqlInterpolated", StringComparison.Ordinal)
                || string.Equals(methodName, "ExecuteSql", StringComparison.Ordinal)
                || string.Equals(methodName, "ExecuteSqlRaw", StringComparison.Ordinal)
                || string.Equals(methodName, "ExecuteSqlInterpolated", StringComparison.Ordinal);
        }

        /// <summary>
        /// Infers provider metadata from a provider setup method name.
        /// </summary>
        private static string ProviderFromCall(string? methodName)
        {
            // Provider metadata uses data-access controlled values as strings rather than new graph node kinds.
            return methodName switch
            {
                "UseSqlServer" => "SqlServer",
                "UseSqlite" => "Sqlite",
                "UseNpgsql" => "PostgreSql",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Normalizes provider invariant names into stable data-access provider values.
        /// </summary>
        private static string NormalizeProvider(string? provider)
        {
            // Common invariant names are collapsed so EF6 config and EF Core provider calls share provider vocabulary.
            if (string.IsNullOrWhiteSpace(provider))
            {
                return "Unknown";
            }

            if (provider.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) || provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                return "SqlServer";
            }

            if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                return "Sqlite";
            }

            if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) || provider.Contains("Postgre", StringComparison.OrdinalIgnoreCase))
            {
                return "PostgreSql";
            }

            return provider.Trim();
        }

        /// <summary>
        /// Infers the data-access technology from a raw SQL API name.
        /// </summary>
        private static string InferTechnologyFromRawSqlApi(string commandApi)
        {
            // EF Core raw SQL APIs include Raw and Interpolated suffixes, while EF6 uses SqlQuery and ExecuteSqlCommand.
            return commandApi.Contains("Raw", StringComparison.Ordinal) || commandApi.Contains("Interpolated", StringComparison.Ordinal) || string.Equals(commandApi, "FromSql", StringComparison.Ordinal) || string.Equals(commandApi, "ExecuteSql", StringComparison.Ordinal)
                ? "EntityFrameworkCore"
                : "EntityFramework6";
        }

        /// <summary>
        /// Resolves technology metadata for a usage fact from context or table stable keys.
        /// </summary>
        private static string ResolveTechnology(EntityFrameworkExtractionState state, StableKey? contextStableKey, StableKey? tableStableKey)
        {
            // Context metadata is preferred; table fallback lets DbSet-only usage still carry a framework label where available.
            if (contextStableKey is not null && state.TechnologyByContextKey.TryGetValue(contextStableKey.Value.Value, out string? contextTechnology))
            {
                return contextTechnology;
            }

            if (tableStableKey is not null && state.TechnologyByTableKey.TryGetValue(tableStableKey.Value.Value, out string? tableTechnology))
            {
                return tableTechnology;
            }

            return "EntityFramework";
        }

        /// <summary>
        /// Infers read/write intent from an EF raw SQL API and SQL preview text.
        /// </summary>
        private static string InferReadWriteHint(string commandApi, string sqlPreview, bool computedSql)
        {
            // API shape and leading SQL verb provide conservative read/write classification without a full SQL parser.
            if (computedSql)
            {
                return "Unknown";
            }

            string trimmed = sqlPreview.TrimStart();
            if (commandApi.StartsWith("FromSql", StringComparison.Ordinal) || string.Equals(commandApi, "SqlQuery", StringComparison.Ordinal) || trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return "Read";
            }

            if (trimmed.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("MERGE", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("ALTER", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("DROP", StringComparison.OrdinalIgnoreCase))
            {
                return "Write";
            }

            return commandApi.StartsWith("Execute", StringComparison.Ordinal) ? "Write" : "Unknown";
        }

        /// <summary>
        /// Tries to resolve a database table stable key from an invocation receiver chain.
        /// </summary>
        private static StableKey? TryResolveTableFromInvocationReceiver(EntityFrameworkExtractionState state, InvocationExpressionSyntax invocation)
        {
            // For calls like context.Customers.Add(...), the receiver member name is the DbSet property name.
            if (invocation.Expression is MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax receiverAccess })
            {
                return state.TableKeysByPropertyName.TryGetValue(receiverAccess.Name.Identifier.ValueText, out StableKey tableStableKey) ? tableStableKey : null;
            }

            return null;
        }

        /// <summary>
        /// Attempts to find an entity generic type argument within a Fluent API invocation chain.
        /// </summary>
        private static string? TryFindEntityGenericTypeName(SyntaxNode node, Microsoft.CodeAnalysis.SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            // The nearest Entity<T>() call in a chain identifies the entity being configured by later ToTable or Property calls.
            foreach (GenericNameSyntax genericName in node.DescendantNodesAndSelf().OfType<GenericNameSyntax>())
            {
                if (string.Equals(genericName.Identifier.ValueText, "Entity", StringComparison.Ordinal) && genericName.TypeArgumentList.Arguments.Count == 1)
                {
                    TypeSyntax typeSyntax = genericName.TypeArgumentList.Arguments[0];
                    ITypeSymbol? typeSymbol = semanticModel.GetTypeInfo(typeSyntax, cancellationToken).Type;
                    return typeSymbol?.ToDisplayString() ?? typeSyntax.ToString();
                }
            }

            return null;
        }

        /// <summary>
        /// Attempts to find the configured property name within a Fluent API property chain.
        /// </summary>
        private static string? TryFindPropertyName(InvocationExpressionSyntax invocation, Microsoft.CodeAnalysis.SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            // Property(lambda) maps CLR properties, while Property<T>(string) identifies EF Core shadow properties by name.
            InvocationExpressionSyntax? propertyInvocation = invocation.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault(candidate => string.Equals(GetInvokedMemberName(candidate), "Property", StringComparison.Ordinal));
            ArgumentSyntax? argument = propertyInvocation?.ArgumentList.Arguments.FirstOrDefault();
            if (argument is null)
            {
                return null;
            }

            string? constant = GetConstantString(argument.Expression, semanticModel);
            if (!string.IsNullOrWhiteSpace(constant))
            {
                return constant.Trim();
            }

            MemberAccessExpressionSyntax? memberAccess = argument.Expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().LastOrDefault();
            cancellationToken.ThrowIfCancellationRequested();
            return memberAccess?.Name.Identifier.ValueText;
        }

        /// <summary>
        /// Determines whether a Fluent API property chain configures a shadow property.
        /// </summary>
        private static bool IsShadowPropertyChain(InvocationExpressionSyntax invocation)
        {
            // EF Core shadow properties use a string property name instead of a lambda expression to a CLR property.
            InvocationExpressionSyntax? propertyInvocation = invocation.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault(candidate => string.Equals(GetInvokedMemberName(candidate), "Property", StringComparison.Ordinal));
            return propertyInvocation?.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax;
        }

        /// <summary>
        /// Determines whether an entity named by a Fluent API mapping is declared in the current syntax tree.
        /// </summary>
        private static bool IsEntityDeclaredInDocument(string entityTypeName, Microsoft.CodeAnalysis.SemanticModel semanticModel, SyntaxTree syntaxTree, CancellationToken cancellationToken)
        {
            // This avoids creating graph facts for unrelated metadata-only mappings that might refer to external assemblies not present in the fixture.
            SyntaxNode root = syntaxTree.GetRoot(cancellationToken);
            return root.DescendantNodes().OfType<ClassDeclarationSyntax>().Any(classDeclaration => semanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken)?.ToDisplayString() == entityTypeName);
        }

        /// <summary>
        /// Reads a named constructor or property argument from a Roslyn attribute.
        /// </summary>
        private static string? GetAttributeNamedValue(ISymbol symbol, string attributeName, string argumentName)
        {
            // Attribute matching accepts both Table and TableAttribute forms and searches named values before constructor arguments.
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                string? className = attribute.AttributeClass?.Name;
                if (!string.Equals(className, attributeName, StringComparison.Ordinal) && !string.Equals(className, string.Concat(attributeName, "Attribute"), StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments)
                {
                    if (string.Equals(namedArgument.Key, argumentName, StringComparison.Ordinal) && namedArgument.Value.Value is string namedValue && !string.IsNullOrWhiteSpace(namedValue))
                    {
                        return namedValue.Trim();
                    }
                }

                foreach (TypedConstant constructorArgument in attribute.ConstructorArguments)
                {
                    if (constructorArgument.Value is string constructorValue && !string.IsNullOrWhiteSpace(constructorValue))
                    {
                        return constructorValue.Trim();
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the member name invoked by a member-access invocation expression.
        /// </summary>
        private static string? GetInvokedMemberName(InvocationExpressionSyntax invocation)
        {
            // Invocation names are syntax-level API descriptors and remain useful even when external EF symbols are source stubs.
            return invocation.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Name.Identifier.ValueText : invocation.Expression is IdentifierNameSyntax identifier ? identifier.Identifier.ValueText : null;
        }

        /// <summary>
        /// Gets a positional string argument from an invocation when it is statically available.
        /// </summary>
        private static string? GetArgumentString(InvocationExpressionSyntax invocation, int index, Microsoft.CodeAnalysis.SemanticModel semanticModel)
        {
            // Argument strings support provider, table, schema, column, and SQL preview extraction.
            return invocation.ArgumentList.Arguments.Count > index ? GetConstantString(invocation.ArgumentList.Arguments[index].Expression, semanticModel) : null;
        }

        /// <summary>
        /// Gets a named string argument from an invocation when it is statically available.
        /// </summary>
        private static string? GetNamedArgumentString(InvocationExpressionSyntax invocation, string argumentName, Microsoft.CodeAnalysis.SemanticModel semanticModel)
        {
            // EF Core migration builders commonly use named schema arguments, so named lookup complements positional extraction.
            ArgumentSyntax? argument = invocation.ArgumentList.Arguments.FirstOrDefault(candidate => string.Equals(candidate.NameColon?.Name.Identifier.ValueText, argumentName, StringComparison.Ordinal));
            return argument is null ? null : GetConstantString(argument.Expression, semanticModel);
        }

        /// <summary>
        /// Gets the first statically available string argument or a computed marker for non-constant expressions.
        /// </summary>
        private static string? GetFirstStringArgument(InvocationExpressionSyntax invocation, Microsoft.CodeAnalysis.SemanticModel semanticModel)
        {
            // Raw SQL and migration calls use first-string extraction; computed expressions are intentionally not evaluated.
            ArgumentSyntax? argument = invocation.ArgumentList.Arguments.FirstOrDefault();
            if (argument is null)
            {
                return null;
            }

            string? value = GetConstantString(argument.Expression, semanticModel);
            return value is null ? "[ComputedSql]" : Redact(value.Trim());
        }

        /// <summary>
        /// Gets a compile-time constant string value from an expression.
        /// </summary>
        private static string? GetConstantString(ExpressionSyntax expression, Microsoft.CodeAnalysis.SemanticModel semanticModel)
        {
            // Semantic constants handle literals and const fields while refusing to evaluate runtime expressions.
            Optional<object?> constantValue = semanticModel.GetConstantValue(expression);
            return constantValue.HasValue && constantValue.Value is string value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        }

        /// <summary>
        /// Gets a local variable name assigned from an expression initializer.
        /// </summary>
        private static string? GetAssignedVariableName(ExpressionSyntax expression)
        {
            // Variable tracking connects later method calls on a local context variable back to the constructed context.
            return expression.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variableDeclarator } ? variableDeclarator.Identifier.ValueText : null;
        }

        /// <summary>
        /// Extracts a safe configuration-key name from name= connection strings or provider configuration strings.
        /// </summary>
        private static string? ExtractConnectionStringKey(string? value)
        {
            // Only the logical key is preserved; credential-bearing connection string fragments are excluded from metadata.
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string trimmed = value.Trim();
            if (trimmed.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
            {
                string key = trimmed[5..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
                return string.IsNullOrWhiteSpace(key) ? null : key;
            }

            int nameIndex = trimmed.IndexOf("name=", StringComparison.OrdinalIgnoreCase);
            if (nameIndex >= 0)
            {
                string key = trimmed[(nameIndex + 5)..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
                return string.IsNullOrWhiteSpace(key) ? null : key;
            }

            return null;
        }

        /// <summary>
        /// Determines whether a string resembles a raw connection string that should be redacted.
        /// </summary>
        private static bool LooksLikeConnectionString(string? value)
        {
            // Credential and server/database markers indicate that the original string must not be copied into graph output.
            return value?.Contains("Password=", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("User Id=", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("Server=", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("Database=", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Parses a database object name into schema and object components.
        /// </summary>
        private static ParsedDatabaseObjectName ParseDatabaseObjectName(string? name, string? schemaName = null)
        {
            // EF mappings commonly pass table and schema separately; unqualified names default to dbo for deterministic keys.
            if (string.IsNullOrWhiteSpace(name))
            {
                return new ParsedDatabaseObjectName(string.IsNullOrWhiteSpace(schemaName) ? "dbo" : TrimIdentifier(schemaName), string.Empty);
            }

            string cleaned = name.Trim().Trim('[', ']');
            string[] parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 1)
            {
                return new ParsedDatabaseObjectName(string.IsNullOrWhiteSpace(schemaName) ? "dbo" : TrimIdentifier(schemaName), TrimIdentifier(parts[0]));
            }

            return new ParsedDatabaseObjectName(TrimIdentifier(parts[^2]), TrimIdentifier(parts[^1]));
        }

        /// <summary>
        /// Removes common SQL identifier delimiters from one database object-name part.
        /// </summary>
        private static string TrimIdentifier(string identifier)
        {
            // Bracket trimming supports common SQL Server identifiers emitted in EF mappings and migrations.
            return identifier.Trim().Trim('[', ']');
        }

        /// <summary>
        /// Attempts to extract a stored procedure name from raw migration SQL text.
        /// </summary>
        private static string? TryExtractStoredProcedureName(string? sql)
        {
            // Migration SQL is only lightly parsed; CREATE PROCEDURE patterns are enough to preserve stored-procedure facts without running SQL.
            if (string.IsNullOrWhiteSpace(sql))
            {
                return null;
            }

            string[] tokens = sql.Split([' ', '\r', '\n', '\t', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int index = 0; index < tokens.Length - 2; index++)
            {
                if (string.Equals(tokens[index], "CREATE", StringComparison.OrdinalIgnoreCase) && string.Equals(tokens[index + 1], "PROCEDURE", StringComparison.OrdinalIgnoreCase))
                {
                    return tokens[index + 2];
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the display name portion of a qualified type name.
        /// </summary>
        private static string GetDisplayName(string typeName)
        {
            // Display names remain readable while stable keys preserve fully qualified identity.
            int index = typeName.LastIndexOf('.');
            return index < 0 ? typeName : typeName[(index + 1)..];
        }

        /// <summary>
        /// Creates a project-scoped stable key for EF graph facts.
        /// </summary>
        private static StableKey CreateProjectScopedKey(string prefix, string projectContext, string identity)
        {
            // Project-scoped keys avoid absolute paths and remain stable across developer machines.
            return new StableKey($"{prefix}://{RepositoryRelativePath.Parse(projectContext).Value}#{identity}");
        }

        /// <summary>
        /// Redacts secret-like values from source snippets, SQL previews, metadata candidates, and diagnostics.
        /// </summary>
        private static string Redact(string value)
        {
            // The redactor is conservative and targets credential-shaped fragments without trying to parse every provider-specific connection-string grammar.
            return value
                .Replace("SuperSecret", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
                .Replace("Password=[REDACTED]", "Credential=[REDACTED]", StringComparison.OrdinalIgnoreCase)
                .Replace("Password='[REDACTED]'", "Credential=[REDACTED]", StringComparison.OrdinalIgnoreCase)
                .Replace("Password=", "Credential=", StringComparison.OrdinalIgnoreCase)
                .Replace("User Id=sa", "User Id=[REDACTED_USER]", StringComparison.OrdinalIgnoreCase)
                .Replace("User ID=sa", "User ID=[REDACTED_USER]", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Hashes stable payload parts with SHA-256.
        /// </summary>
        private static string HashStablePayload(params string?[] parts)
        {
            // Length-prefixing keeps stable keys deterministic when payload values contain separators.
            StringBuilder builder = new();
            foreach (string? part in parts)
            {
                string value = part ?? string.Empty;
                builder.Append(value.Length).Append(':').Append(value).Append('|');
            }

            byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Represents a parsed schema-qualified database object name.
        /// </summary>
        /// <param name="SchemaName">The schema name, using dbo when the EF mapping name is unqualified.</param>
        /// <param name="ObjectName">The table or procedure name.</param>
        private readonly record struct ParsedDatabaseObjectName(string SchemaName, string ObjectName)
        {
            /// <summary>
            /// Gets the schema-qualified database object name used in stable keys and metadata.
            /// </summary>
            public string QualifiedName
            {
                get
                {
                    // A schema-qualified display keeps graph metadata and stable-key payloads aligned.
                    return $"{SchemaName}.{ObjectName}";
                }
            }
        }

        /// <summary>
        /// Represents EF technology classification for a context-like source type.
        /// </summary>
        private readonly record struct EfTechnology(string Value, string ContextKind)
        {
            /// <summary>
            /// Gets a value indicating whether this instance represents a recognized Entity Framework technology.
            /// </summary>
            public bool IsEntityFramework
            {
                get
                {
                    // Empty technology means the inspected type is not an EF context.
                    return !string.IsNullOrWhiteSpace(Value);
                }
            }

            /// <summary>
            /// Gets an empty technology classification used for non-EF types.
            /// </summary>
            public static EfTechnology None
            {
                get
                {
                    // A singleton-like property keeps caller comparisons simple without nullable technology values.
                    return new EfTechnology(string.Empty, string.Empty);
                }
            }
        }

        /// <summary>
        /// Represents secret-safe EF provider and connection-key metadata.
        /// </summary>
        private sealed record EfProviderFact(string Provider, string? ProviderConfigurationCall, string? ConnectionStringKey, bool ConnectionStringRedacted);

        /// <summary>
        /// Represents one EF entity-to-table mapping accumulated from DbSet, attribute, or Fluent API evidence.
        /// </summary>
        private sealed class EfEntityMapping
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="EfEntityMapping" /> class.
            /// </summary>
            /// <param name="entityTypeName">The fully qualified CLR entity type name.</param>
            /// <param name="entityDisplayName">The short CLR entity type name.</param>
            /// <param name="dbSetPropertyName">The DbSet property name or convention fallback name.</param>
            public EfEntityMapping(string entityTypeName, string entityDisplayName, string dbSetPropertyName)
            {
                // Construction records the CLR identity and starts with a convention table until explicit mapping evidence overrides it.
                EntityTypeName = entityTypeName;
                EntityDisplayName = entityDisplayName;
                DbSetPropertyName = dbSetPropertyName;
                TableName = ParseDatabaseObjectName(dbSetPropertyName, null);
            }

            /// <summary>
            /// Gets the fully qualified CLR entity type name.
            /// </summary>
            public string EntityTypeName { get; }

            /// <summary>
            /// Gets the short CLR entity type name.
            /// </summary>
            public string EntityDisplayName { get; }

            /// <summary>
            /// Gets or sets the DbSet property name used as a convention mapping fallback.
            /// </summary>
            public string DbSetPropertyName { get; set; }

            /// <summary>
            /// Gets the parsed database table name associated with the entity.
            /// </summary>
            public ParsedDatabaseObjectName TableName { get; private set; }

            /// <summary>
            /// Gets a value indicating whether table identity came from explicit mapping evidence.
            /// </summary>
            public bool HasExplicitTable { get; private set; }

            /// <summary>
            /// Gets configured column mappings for this entity.
            /// </summary>
            public List<EfColumnMapping> Columns { get; } = [];

            /// <summary>
            /// Gets configured relationships for this entity.
            /// </summary>
            public List<EfRelationshipFact> Relationships { get; } = [];

            /// <summary>
            /// Replaces the current table identity with explicit or convention-derived mapping evidence.
            /// </summary>
            /// <param name="tableName">The parsed database table name.</param>
            /// <param name="hasExplicitTable">A value indicating whether the mapping came from explicit source metadata.</param>
            public void SetTable(ParsedDatabaseObjectName tableName, bool hasExplicitTable)
            {
                // Explicit mapping raises confidence; convention mapping remains useful but is marked as an unknown in graph facts.
                TableName = tableName;
                HasExplicitTable = hasExplicitTable;
            }

            /// <summary>
            /// Adds or replaces a column mapping by property name.
            /// </summary>
            /// <param name="column">The column mapping to record.</param>
            public void AddColumn(EfColumnMapping column)
            {
                // Later equivalent mappings replace earlier ones so Fluent API refinements can override convention or attribute observations.
                Columns.RemoveAll(existing => string.Equals(existing.PropertyName, column.PropertyName, StringComparison.Ordinal));
                Columns.Add(column);
            }

            /// <summary>
            /// Adds a relationship mapping when an equivalent target has not already been recorded.
            /// </summary>
            /// <param name="relationship">The relationship mapping to record.</param>
            public void AddRelationship(EfRelationshipFact relationship)
            {
                // Duplicate relationship chains are common in fluent syntax, so the mapping keeps only one target entry.
                if (!Relationships.Any(existing => string.Equals(existing.TargetEntityType, relationship.TargetEntityType, StringComparison.Ordinal)))
                {
                    Relationships.Add(relationship);
                }
            }
        }

        /// <summary>
        /// Represents an EF property-to-column mapping.
        /// </summary>
        private sealed record EfColumnMapping(string PropertyName, string ColumnName, bool IsShadowProperty);

        /// <summary>
        /// Represents a relationship target observed in EF Fluent API mapping.
        /// </summary>
        private sealed record EfRelationshipFact(string TargetEntityType);

        /// <summary>
        /// Represents entity and table stable keys returned after mapping accumulation.
        /// </summary>
        private readonly record struct EntityTableKeys(StableKey EntityStableKey, StableKey? TableStableKey);

        /// <summary>
        /// Tracks EF model identities so source usage can resolve contexts, DbSet properties, tables, and technology labels.
        /// </summary>
        private sealed class EntityFrameworkExtractionState
        {
            /// <summary>
            /// Gets DbContext stable keys by fully qualified context type name.
            /// </summary>
            public Dictionary<string, StableKey> ContextKeysByTypeName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets entity stable keys by fully qualified CLR entity type name.
            /// </summary>
            public Dictionary<string, StableKey> EntityKeysByTypeName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets table stable keys by fully qualified CLR entity type name.
            /// </summary>
            public Dictionary<string, StableKey> TableKeysByEntityTypeName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets table stable keys by DbSet property name.
            /// </summary>
            public Dictionary<string, StableKey> TableKeysByPropertyName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets database column stable keys by entity and property identity.
            /// </summary>
            public Dictionary<string, StableKey> ColumnKeysByPropertyName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets stored procedure stable keys by schema-qualified database procedure name.
            /// </summary>
            public Dictionary<string, StableKey> StoredProcedureKeysByName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets data-access technology values by context stable key value.
            /// </summary>
            public Dictionary<string, string> TechnologyByContextKey { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets data-access technology values by table stable key value.
            /// </summary>
            public Dictionary<string, string> TechnologyByTableKey { get; } = new(StringComparer.Ordinal);
        }

        /// <summary>
        /// Tracks method-local EF context variables, written tables, and stable source method identity.
        /// </summary>
        private sealed class EfMethodUsageState
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="EfMethodUsageState" /> class.
            /// </summary>
            /// <param name="methodStableKey">The stable key for the source method node.</param>
            private EfMethodUsageState(StableKey methodStableKey)
            {
                // The constructor only stores deterministic method identity; dictionaries are populated during method traversal.
                MethodStableKey = methodStableKey;
            }

            /// <summary>
            /// Gets the stable key of the source method node.
            /// </summary>
            public StableKey MethodStableKey { get; }

            /// <summary>
            /// Gets context stable keys by local variable or parameter name.
            /// </summary>
            public Dictionary<string, StableKey> ContextKeysByVariable { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets table stable keys that were mutated before a SaveChanges boundary.
            /// </summary>
            public HashSet<StableKey> WrittenTableKeys { get; } = [];

            /// <summary>
            /// Creates method usage state from a Roslyn method symbol.
            /// </summary>
            /// <param name="methodSymbol">The source method symbol being traversed.</param>
            /// <param name="snapshotStableKey">The stable key of the owning snapshot.</param>
            /// <param name="relativePath">The repository-relative source file path.</param>
            /// <param name="projectContext">The repository-relative project context.</param>
            /// <returns>A method usage state with deterministic method identity.</returns>
            public static EfMethodUsageState FromMethod(IMethodSymbol methodSymbol, StableKey snapshotStableKey, string relativePath, string projectContext)
            {
                // Snapshot, project, path, and symbol identity keep method stable keys deterministic across machines.
                return new EfMethodUsageState(new StableKey($"method://{HashStablePayload(snapshotStableKey.Value, projectContext, relativePath, methodSymbol.ToDisplayString())}"));
            }
        }
    }
}
