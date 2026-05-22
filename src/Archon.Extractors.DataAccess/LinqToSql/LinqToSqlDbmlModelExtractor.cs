using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Extractors.DataAccess.AdoNet;
using Archon.Extractors.DataAccess.EntityFramework;
using Archon.Extractors.DataAccess.TypedDataSet;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Archon.Extractors.DataAccess.LinqToSql
{
    /// <summary>
    /// Extracts LINQ to SQL DBML model artifacts into graph-ready data-access facts without executing target code or connecting to databases.
    /// </summary>
    public sealed class LinqToSqlDbmlModelExtractor
    {
        /// <summary>
        /// Stores graph node kinds that participate in final WP009 cross-slice correlation.
        /// </summary>
        private static readonly HashSet<string> s_dataAccessNodeKinds = new(StringComparer.Ordinal)
        {
            NodeKind.DbContext.Value,
            NodeKind.LinqToSqlDataContext.Value,
            NodeKind.Entity.Value,
            NodeKind.DatabaseTable.Value,
            NodeKind.DatabaseColumn.Value,
            NodeKind.StoredProcedure.Value,
            NodeKind.SqlScript.Value
        };

        /// <summary>
        /// Stores graph edge kinds that prove a method has downstream data-access behavior.
        /// </summary>
        private static readonly HashSet<string> s_dataAccessUsageEdgeKinds = new(StringComparer.Ordinal)
        {
            EdgeKind.UsesDbContext.Value,
            EdgeKind.UsesLinqToSqlContext.Value,
            EdgeKind.ReadsTable.Value,
            EdgeKind.WritesTable.Value,
            EdgeKind.CallsStoredProcedure.Value,
            EdgeKind.ExecutesRawSql.Value
        };

        /// <summary>
        /// Extracts DBML DataContext, entity, table, column, stored-procedure, evidence, warning, and unknown facts from the supplied repository request.
        /// </summary>
        /// <param name="request">The repository-scoped DBML extraction request.</param>
        /// <param name="cancellationToken">A token that signals when extraction should stop before or between file operations.</param>
        /// <returns>The DBML extraction result containing shared architecture snapshot contributions.</returns>
        public LinqToSqlDbmlExtractionResult Extract(LinqToSqlDbmlExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // Extraction is fully static: DBML files are discovered as repository artifacts and parsed as XML model metadata only.
            ArgumentNullException.ThrowIfNull(request);
            ArchitectureSnapshotAccumulator accumulator = new();
            foreach (string dbmlFilePath in DiscoverDbmlFiles(request.RepositoryRootDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParseDbmlFile(request, accumulator, dbmlFilePath, cancellationToken);
            }

            LinqToSqlSemanticExtractionState semanticState = LinqToSqlSemanticExtractionState.FromSnapshot(accumulator.ToSnapshot());
            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateDesignerModel(request, accumulator, semanticState, semanticDocument, cancellationToken);
            }

            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateSourceUsage(request, accumulator, semanticState, semanticDocument, cancellationToken);
            }

            EntityFrameworkModelExtractor entityFrameworkExtractor = new();
            entityFrameworkExtractor.Accumulate(request, accumulator, cancellationToken);

            AdoNetRawSqlExtractor adoNetExtractor = new();
            adoNetExtractor.Accumulate(request, accumulator, cancellationToken);

            TypedDataSetExtractor typedDataSetExtractor = new();
            typedDataSetExtractor.Accumulate(request, accumulator, cancellationToken);

            AccumulateCrossSliceCorrelations(request, accumulator, cancellationToken);

            return new LinqToSqlDbmlExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Discovers DBML files below the repository root using deterministic ordering.
        /// </summary>
        /// <param name="repositoryRootDirectory">The absolute repository root to search.</param>
        /// <returns>Repository-contained DBML file paths ordered deterministically.</returns>
        private static IReadOnlyList<string> DiscoverDbmlFiles(string repositoryRootDirectory)
        {
            // Missing roots simply contribute no facts; request validation already ensured the path text itself was explicit.
            if (!Directory.Exists(repositoryRootDirectory))
            {
                return [];
            }

            return Directory.EnumerateFiles(repositoryRootDirectory, "*.dbml", SearchOption.AllDirectories)
                .Where(path => !path.Contains(string.Concat(Path.DirectorySeparatorChar, "obj", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Adds deterministic correlations between WP009 data-access facts and graph facts emitted by earlier configuration, dependency-injection, and runtime stages.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity and semantic source documents.</param>
        /// <param name="accumulator">The shared accumulator containing all current WP009 and precursor API-stage facts.</param>
        /// <param name="cancellationToken">A token that signals when semantic correlation should stop.</param>
        public void AccumulateCrossSliceCorrelations(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, CancellationToken cancellationToken)
        {
            // Correlation runs after all data-access slices so it can use the accumulator's stable-key de-duplication instead of inventing a parallel result model.
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(accumulator);

            Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot snapshot = accumulator.ToSnapshot();
            CrossSliceCorrelationState state = CrossSliceCorrelationState.FromSnapshot(snapshot);
            AccumulateConfigurationCorrelations(request, accumulator, state);
            AccumulateDependencyInjectionCorrelations(request, accumulator, state, cancellationToken);

            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateRuntimeMethodCorrelations(request, accumulator, state, semanticDocument, cancellationToken);
            }
        }

        /// <summary>
        /// Links data-access nodes that carry connection-string-key metadata to matching configuration-key nodes from earlier extraction stages.
        /// </summary>
        /// <param name="request">The extraction request that owns the snapshot stable key.</param>
        /// <param name="accumulator">The shared accumulator receiving correlation edges.</param>
        /// <param name="state">The precomputed cross-slice lookup state.</param>
        private static void AccumulateConfigurationCorrelations(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, CrossSliceCorrelationState state)
        {
            // Only safe key names are used; raw configuration values and connection strings are never copied into data-access metadata.
            foreach (ArchitectureNode dataAccessNode in state.DataAccessNodes)
            {
                string? connectionStringKey = ExtractMetadataString(dataAccessNode.Metadata, "connectionStringKey");
                if (string.IsNullOrWhiteSpace(connectionStringKey))
                {
                    continue;
                }

                foreach (ArchitectureNode configurationNode in state.FindConfigurationNodes(connectionStringKey))
                {
                    GraphMetadata metadata = CreateCorrelationMetadata("DataAccessConnectionStringKey", null, ExtractMetadataString(dataAccessNode.Metadata, "dataAccessTechnology"), null, connectionStringKey, null, null);
                    accumulator.AddEdge(CreateCorrelationEdge(request.SnapshotStableKey, EdgeKind.UsesConfig, dataAccessNode.StableKey, configurationNode.StableKey, dataAccessNode.PrimaryEvidenceStableKey, metadata, Confidence.High));
                }
            }
        }

        /// <summary>
        /// Links dependency-injection registrations for known context implementation types to the corresponding data-access context nodes.
        /// </summary>
        /// <param name="request">The extraction request that owns the snapshot stable key.</param>
        /// <param name="accumulator">The shared accumulator receiving correlation edges.</param>
        /// <param name="state">The precomputed cross-slice lookup state.</param>
        private static void AccumulateDependencyInjectionCorrelations(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, CrossSliceCorrelationState state, CancellationToken cancellationToken)
        {
            // Registration edges are emitted by WP007 before WP009 runs; matching by type identity avoids depending on container implementation details.
            foreach (ArchitectureNode contextNode in state.ContextNodes)
            {
                string? contextType = ExtractMetadataString(contextNode.Metadata, "contextType") ?? contextNode.QualifiedName;
                if (string.IsNullOrWhiteSpace(contextType))
                {
                    continue;
                }

                StableKey typeStableKey = StableKeyGenerator.ForType(contextType);
                if (!state.RegisteredImplementationTypeKeys.Contains(typeStableKey.Value))
                {
                    continue;
                }

                GraphMetadata metadata = CreateCorrelationMetadata("DependencyInjectionDbContextRegistration", contextType, ExtractMetadataString(contextNode.Metadata, "dataAccessTechnology"), null, null, null, null);
                accumulator.AddEdge(CreateCorrelationEdge(request.SnapshotStableKey, contextNode.NodeKind == NodeKind.LinqToSqlDataContext ? EdgeKind.UsesLinqToSqlContext : EdgeKind.UsesDbContext, typeStableKey, contextNode.StableKey, contextNode.PrimaryEvidenceStableKey, metadata, Confidence.High));
            }

            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateDbContextRegistrationInvocations(request, accumulator, state, semanticDocument, cancellationToken);
            }
        }

        /// <summary>
        /// Links AddDbContext-style registration calls to known data-access context nodes when WP007 has not emitted a generic registration edge for the extension method.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving correlation edges.</param>
        /// <param name="state">The precomputed cross-slice lookup state.</param>
        /// <param name="semanticDocument">The Roslyn semantic document to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic traversal should stop.</param>
        private static void AccumulateDbContextRegistrationInvocations(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, CrossSliceCorrelationState state, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Entity Framework registrations are provider setup calls as well as DI registrations, so WP009 records the context correlation even if the general DI extractor treats the API as framework-specific.
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(request.RepositoryRootDirectory, semanticDocument.DocumentPath);
            string sourceText = semanticDocument.SyntaxTree.GetText(cancellationToken).ToString();
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(GetInvokedMemberName(invocation), "AddDbContext", StringComparison.Ordinal))
                {
                    continue;
                }

                string? contextType = TryGetFirstGenericTypeArgumentName(semanticDocument, invocation, cancellationToken);
                if (string.IsNullOrWhiteSpace(contextType))
                {
                    continue;
                }

                ArchitectureNode? contextNode = state.ContextNodes.FirstOrDefault(node => string.Equals(ExtractMetadataString(node.Metadata, "contextType") ?? node.QualifiedName, contextType, StringComparison.Ordinal));
                if (contextNode is null)
                {
                    continue;
                }

                EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, EvidenceKind.SourceCode, "DbContextRegistration", contextType, null, Confidence.High, UnknownState.Known);
                StableKey typeStableKey = StableKeyGenerator.ForType(contextType);
                GraphMetadata metadata = CreateCorrelationMetadata("DependencyInjectionDbContextRegistration", contextType, ExtractMetadataString(contextNode.Metadata, "dataAccessTechnology"), relativePath, null, null, null);
                accumulator.AddEvidence(evidence).AddEdge(CreateCorrelationEdge(request.SnapshotStableKey, EdgeKind.UsesDbContext, typeStableKey, contextNode.StableKey, evidence.StableKey, metadata, Confidence.High));
            }
        }

        /// <summary>
        /// Links runtime methods from previous stages to method-level data-access usage facts when source contains a deterministic method call.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving correlation edges.</param>
        /// <param name="state">The precomputed cross-slice lookup state.</param>
        /// <param name="semanticDocument">The Roslyn semantic document to inspect for method call evidence.</param>
        /// <param name="cancellationToken">A token that signals when semantic traversal should stop.</param>
        private static void AccumulateRuntimeMethodCorrelations(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, CrossSliceCorrelationState state, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Runtime-to-data-access links are conservative: a known runtime method must directly invoke a known method that already anchors data-access usage edges.
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(request.RepositoryRootDirectory, semanticDocument.DocumentPath);
            string sourceText = semanticDocument.SyntaxTree.GetText(cancellationToken).ToString();
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            foreach (MethodDeclarationSyntax methodDeclaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (semanticDocument.SemanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken) is not IMethodSymbol callerSymbol)
                {
                    continue;
                }

                string callerDisplay = callerSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                if (!state.RuntimeMethodNodesByQualifiedName.TryGetValue(callerDisplay, out IReadOnlyList<ArchitectureNode>? runtimeMethods))
                {
                    continue;
                }

                foreach (InvocationExpressionSyntax invocation in methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    IMethodSymbol? invokedMethod = semanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                    if (invokedMethod is null)
                    {
                        continue;
                    }

                    string invokedDisplay = invokedMethod.ToDisplayString();
                    if (!state.DataAccessMethodNodesByQualifiedName.TryGetValue(invokedDisplay, out IReadOnlyList<ArchitectureNode>? dataAccessMethods))
                    {
                        continue;
                    }

                    EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, EvidenceKind.SourceCode, "RuntimeDataAccessMethodCall", invokedMethod.Name, callerDisplay, Confidence.Medium, UnknownState.Known);
                    accumulator.AddEvidence(evidence);
                    foreach (ArchitectureNode runtimeMethod in runtimeMethods)
                    {
                        foreach (ArchitectureNode dataAccessMethod in dataAccessMethods)
                        {
                            GraphMetadata metadata = CreateCorrelationMetadata("RuntimeDataAccessMethod", invokedDisplay, ExtractMetadataString(dataAccessMethod.Metadata, "dataAccessTechnology"), relativePath, null, runtimeMethod.QualifiedName, dataAccessMethod.QualifiedName);
                            accumulator.AddEdge(CreateCorrelationEdge(request.SnapshotStableKey, EdgeKind.DependsOn, runtimeMethod.StableKey, dataAccessMethod.StableKey, evidence.StableKey, metadata, Confidence.Medium));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Parses a single DBML file and emits graph facts or non-fatal diagnostics into the shared accumulator.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity and repository paths.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="dbmlFilePath">The absolute DBML file path to parse.</param>
        /// <param name="cancellationToken">A token that signals when parsing should stop.</param>
        private static void ParseDbmlFile(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, string dbmlFilePath, CancellationToken cancellationToken)
        {
            // Malformed XML degrades to a warning so one broken model cannot block extraction of other data-access artifacts.
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(request.RepositoryRootDirectory, dbmlFilePath);
            string content = File.ReadAllText(dbmlFilePath);
            string redactedContent = DbmlRedactor.Redact(content);
            try
            {
                XDocument document = XDocument.Parse(content, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
                if (document.Root is null || !IsNamed(document.Root, "Database"))
                {
                    accumulator.AddWarning($"DBML file {relativePath} does not contain a Database root element.");
                    return;
                }

                AccumulateDatabaseModel(request, accumulator, relativePath, redactedContent, document.Root, cancellationToken);
            }
            catch (XmlException exception)
            {
                accumulator.AddWarning($"Malformed DBML file {relativePath}: {DbmlRedactor.Redact(exception.Message)}");
            }
        }

        /// <summary>
        /// Emits graph facts for the parsed DBML database model root.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="relativePath">The repository-relative DBML file path.</param>
        /// <param name="redactedContent">The redacted DBML content used for snippet previews and hashes.</param>
        /// <param name="databaseElement">The parsed DBML Database element.</param>
        /// <param name="cancellationToken">A token that signals when model traversal should stop.</param>
        private static void AccumulateDatabaseModel(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, string relativePath, string redactedContent, XElement databaseElement, CancellationToken cancellationToken)
        {
            // The DataContext node anchors all DBML model facts even when the model is partial and the class name must be synthesized.
            DbmlLocation databaseLocation = GetLocation(databaseElement);
            string? databaseName = GetAttribute(databaseElement, "Name");
            string? contextClass = GetAttribute(databaseElement, "Class");
            string contextIdentity = string.IsNullOrWhiteSpace(contextClass) ? $"UnknownDataContext:{HashStablePayload(relativePath, databaseName)}" : contextClass.Trim();
            UnknownState contextUnknownState = string.IsNullOrWhiteSpace(contextClass) ? UnknownState.Unknown("DBML Database element does not declare a DataContext class name.") : UnknownState.Known;
            Confidence contextConfidence = contextUnknownState.HasUnknownData ? Confidence.Medium : Confidence.Certain;
            if (contextUnknownState.HasUnknownData)
            {
                accumulator.AddWarning($"DBML file {relativePath} does not declare a DataContext class; emitted an explicit unknown DataContext identity.");
            }

            DbmlConnectionFact connection = ExtractConnection(databaseElement);
            EvidenceRecord contextEvidence = CreateEvidence(request.SnapshotStableKey, relativePath, databaseElement, redactedContent, "DataContext", contextIdentity, databaseLocation, contextConfidence, contextUnknownState);
            StableKey contextStableKey = CreateScopedKey("linqtosql", relativePath, contextIdentity);
            ArchitectureNode contextNode = CreateNode(request.SnapshotStableKey, contextStableKey, NodeKind.LinqToSqlDataContext, contextIdentity, contextIdentity, "DBML", null, contextEvidence.StableKey, contextConfidence, contextUnknownState, CreateContextMetadata(databaseName, contextClass, relativePath, connection));
            accumulator.AddEvidence(contextEvidence).AddNode(contextNode);

            Dictionary<string, StableKey> tableKeysByMemberOrName = new(StringComparer.Ordinal);
            foreach (XElement tableElement in databaseElement.Elements().Where(static element => IsNamed(element, "Table")))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateTableModel(request, accumulator, relativePath, redactedContent, contextNode, tableElement, tableKeysByMemberOrName);
            }

            foreach (XElement functionElement in databaseElement.Elements().Where(static element => IsNamed(element, "Function")))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateFunctionModel(request, accumulator, relativePath, redactedContent, contextNode, functionElement);
            }
        }

        /// <summary>
        /// Emits graph facts for one DBML table, its entity type, columns, and associations.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="relativePath">The repository-relative DBML file path.</param>
        /// <param name="redactedContent">The redacted DBML content used for snippet previews and hashes.</param>
        /// <param name="contextNode">The DataContext node that owns the table mapping.</param>
        /// <param name="tableElement">The DBML Table element to extract.</param>
        /// <param name="tableKeysByMemberOrName">The table lookup used by association metadata and deterministic correlation.</param>
        private static void AccumulateTableModel(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, string relativePath, string redactedContent, ArchitectureNode contextNode, XElement tableElement, Dictionary<string, StableKey> tableKeysByMemberOrName)
        {
            // DBML maps a table element to an entity type child, so the extractor preserves both graph concepts when enough metadata exists.
            XElement? typeElement = tableElement.Elements().FirstOrDefault(static element => IsNamed(element, "Type"));
            string? entityName = GetAttribute(typeElement, "Name");
            string? tableName = GetAttribute(tableElement, "Name");
            string? memberName = GetAttribute(tableElement, "Member");
            string entityIdentity = FirstNonEmpty(entityName, memberName, tableName, $"UnknownEntity:{HashStablePayload(relativePath, tableElement.ToString(SaveOptions.DisableFormatting))}");
            DbmlLocation entityLocation = GetLocation(typeElement ?? tableElement);
            EvidenceRecord entityEvidence = CreateEvidence(request.SnapshotStableKey, relativePath, typeElement ?? tableElement, redactedContent, "Entity", entityIdentity, entityLocation, Confidence.Certain, UnknownState.Known);
            StableKey entityStableKey = CreateScopedKey("entity", relativePath, entityIdentity);
            ArchitectureNode entityNode = CreateNode(request.SnapshotStableKey, entityStableKey, NodeKind.Entity, entityIdentity, entityIdentity, "DBML", contextNode.StableKey, entityEvidence.StableKey, Confidence.Certain, UnknownState.Known, CreateEntityMetadata(entityName, memberName, tableName, relativePath));
            accumulator.AddEvidence(entityEvidence).AddNode(entityNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsEntity, contextNode.StableKey, entityStableKey, entityEvidence.StableKey, CreateRelationshipMetadata("DataContextEntityMapping", relativePath)));

            StableKey? tableStableKey = null;
            ParsedDatabaseObjectName parsedTableName = ParseDatabaseObjectName(tableName);
            if (!string.IsNullOrWhiteSpace(parsedTableName.ObjectName))
            {
                EvidenceRecord tableEvidence = CreateEvidence(request.SnapshotStableKey, relativePath, tableElement, redactedContent, "DatabaseTable", parsedTableName.ObjectName, GetLocation(tableElement), Confidence.Certain, UnknownState.Known);
                tableStableKey = CreateScopedKey("dbtable", relativePath, $"{parsedTableName.SchemaName}.{parsedTableName.ObjectName}");
                ArchitectureNode tableNode = CreateNode(request.SnapshotStableKey, tableStableKey.Value, NodeKind.DatabaseTable, parsedTableName.ObjectName, parsedTableName.QualifiedName, "Database", null, tableEvidence.StableKey, Confidence.Certain, UnknownState.Known, CreateTableMetadata(parsedTableName, memberName, relativePath));
                accumulator.AddEvidence(tableEvidence).AddNode(tableNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsTable, entityStableKey, tableStableKey.Value, tableEvidence.StableKey, CreateRelationshipMetadata("EntityTableMapping", relativePath)));
                AddLookup(tableKeysByMemberOrName, memberName, tableStableKey.Value);
                AddLookup(tableKeysByMemberOrName, parsedTableName.QualifiedName, tableStableKey.Value);
            }

            foreach (XElement columnElement in (typeElement ?? tableElement).Elements().Where(static element => IsNamed(element, "Column")))
            {
                AccumulateColumnModel(request, accumulator, relativePath, redactedContent, tableStableKey, entityStableKey, parsedTableName, columnElement);
            }

            foreach (XElement associationElement in (typeElement ?? tableElement).Elements().Where(static element => IsNamed(element, "Association")))
            {
                AccumulateAssociationRelationship(request, accumulator, relativePath, redactedContent, entityStableKey, associationElement);
            }
        }

        /// <summary>
        /// Emits graph facts for one DBML column when a deterministic table identity is available.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="relativePath">The repository-relative DBML file path.</param>
        /// <param name="redactedContent">The redacted DBML content used for snippet previews and hashes.</param>
        /// <param name="tableStableKey">The table stable key when the DBML table name was deterministic.</param>
        /// <param name="entityStableKey">The entity stable key used as a fallback relationship source.</param>
        /// <param name="tableName">The parsed database table name associated with the column.</param>
        /// <param name="columnElement">The DBML Column element to extract.</param>
        private static void AccumulateColumnModel(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, string relativePath, string redactedContent, StableKey? tableStableKey, StableKey entityStableKey, ParsedDatabaseObjectName tableName, XElement columnElement)
        {
            // Columns require a known table node for canonical DatabaseColumn identity; otherwise column details remain part of entity metadata only in later slices.
            if (tableStableKey is null)
            {
                return;
            }

            string? columnName = GetAttribute(columnElement, "Name");
            if (string.IsNullOrWhiteSpace(columnName))
            {
                return;
            }

            string columnDisplayName = columnName.Trim();
            string columnIdentity = $"{tableName.SchemaName}.{tableName.ObjectName}.{columnDisplayName}";
            EvidenceRecord columnEvidence = CreateEvidence(request.SnapshotStableKey, relativePath, columnElement, redactedContent, "DatabaseColumn", columnDisplayName, GetLocation(columnElement), Confidence.Certain, UnknownState.Known);
            StableKey columnStableKey = CreateScopedKey("dbcolumn", relativePath, columnIdentity);
            ArchitectureNode columnNode = CreateNode(request.SnapshotStableKey, columnStableKey, NodeKind.DatabaseColumn, columnDisplayName, columnIdentity, "Database", tableStableKey, columnEvidence.StableKey, Confidence.Certain, UnknownState.Known, CreateColumnMetadata(columnElement, tableName, relativePath));
            accumulator.AddEvidence(columnEvidence).AddNode(columnNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsColumn, tableStableKey.Value, columnStableKey, columnEvidence.StableKey, CreateRelationshipMetadata("TableColumnMapping", relativePath)));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsColumn, entityStableKey, columnStableKey, columnEvidence.StableKey, CreateRelationshipMetadata("EntityColumnMapping", relativePath)));
        }

        /// <summary>
        /// Emits an association relationship as metadata-backed mapping evidence when DBML association metadata exists.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="relativePath">The repository-relative DBML file path.</param>
        /// <param name="redactedContent">The redacted DBML content used for snippet previews and hashes.</param>
        /// <param name="entityStableKey">The entity stable key that owns the association.</param>
        /// <param name="associationElement">The DBML Association element to extract.</param>
        private static void AccumulateAssociationRelationship(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, string relativePath, string redactedContent, StableKey entityStableKey, XElement associationElement)
        {
            // Work Item 1 preserves association metadata as a self-contained mapping edge because cross-entity correlation is expanded in later LINQ to SQL slices.
            string associationName = FirstNonEmpty(GetAttribute(associationElement, "Name"), GetAttribute(associationElement, "Member"), "UnknownAssociation");
            EvidenceRecord associationEvidence = CreateEvidence(request.SnapshotStableKey, relativePath, associationElement, redactedContent, "Association", associationName, GetLocation(associationElement), Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(associationEvidence).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsEntity, entityStableKey, entityStableKey, associationEvidence.StableKey, CreateAssociationMetadata(associationElement, relativePath)));
        }

        /// <summary>
        /// Emits graph facts for one DBML function or stored procedure declaration.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="relativePath">The repository-relative DBML file path.</param>
        /// <param name="redactedContent">The redacted DBML content used for snippet previews and hashes.</param>
        /// <param name="contextNode">The DataContext node that owns the function wrapper.</param>
        /// <param name="functionElement">The DBML Function element to extract.</param>
        private static void AccumulateFunctionModel(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, string relativePath, string redactedContent, ArchitectureNode contextNode, XElement functionElement)
        {
            // DBML functions generally represent stored procedure wrappers; Work Item 1 records them with CALLS_STORED_PROCEDURE from the DataContext.
            string? functionName = GetAttribute(functionElement, "Name");
            ParsedDatabaseObjectName parsedName = ParseDatabaseObjectName(functionName);
            if (string.IsNullOrWhiteSpace(parsedName.ObjectName))
            {
                return;
            }

            EvidenceRecord procedureEvidence = CreateEvidence(request.SnapshotStableKey, relativePath, functionElement, redactedContent, "StoredProcedure", parsedName.ObjectName, GetLocation(functionElement), Confidence.Certain, UnknownState.Known);
            StableKey procedureStableKey = CreateScopedKey("storedprocedure", relativePath, parsedName.QualifiedName);
            ArchitectureNode procedureNode = CreateNode(request.SnapshotStableKey, procedureStableKey, NodeKind.StoredProcedure, parsedName.ObjectName, parsedName.QualifiedName, "Database", contextNode.StableKey, procedureEvidence.StableKey, Confidence.Certain, UnknownState.Known, CreateStoredProcedureMetadata(functionElement, parsedName, relativePath));
            accumulator.AddEvidence(procedureEvidence).AddNode(procedureNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.CallsStoredProcedure, contextNode.StableKey, procedureStableKey, procedureEvidence.StableKey, CreateRelationshipMetadata("DataContextStoredProcedureWrapper", relativePath)));
        }

        /// <summary>
        /// Emits graph facts for generated LINQ to SQL designer source files and correlates them with DBML model identities when possible.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="state">The semantic extraction state used to deduplicate designer facts with DBML facts.</param>
        /// <param name="semanticDocument">The Roslyn semantic document to inspect.</param>
        /// <param name="cancellationToken">A token that signals when source traversal should stop.</param>
        private static void AccumulateDesignerModel(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, LinqToSqlSemanticExtractionState state, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Generated designer source carries the same model facts as DBML in attribute form; stable-key correlation keeps the graph deduplicated.
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(request.RepositoryRootDirectory, semanticDocument.DocumentPath);
            string sourceText = semanticDocument.SyntaxTree.GetText(cancellationToken).ToString();
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            Dictionary<string, StableKey> tableKeysByEntityName = new(StringComparer.Ordinal);
            Dictionary<string, StableKey> entityKeysByEntityName = new(StringComparer.Ordinal);
            foreach (ClassDeclarationSyntax classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                INamedTypeSymbol? typeSymbol = (INamedTypeSymbol?)semanticDocument.SemanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken);
                if (typeSymbol is null)
                {
                    continue;
                }

                if (IsLinqToSqlDataContext(typeSymbol))
                {
                    AccumulateDesignerDataContext(request, accumulator, state, semanticDocument, relativePath, sourceText, classDeclaration, typeSymbol);
                }

                DesignerTableFact? tableFact = TryCreateDesignerTableFact(typeSymbol);
                if (tableFact is not null)
                {
                    AccumulateDesignerEntity(request, accumulator, state, semanticDocument, relativePath, sourceText, classDeclaration, typeSymbol, tableFact, entityKeysByEntityName, tableKeysByEntityName);
                }
            }

            foreach (ClassDeclarationSyntax classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                INamedTypeSymbol? typeSymbol = (INamedTypeSymbol?)semanticDocument.SemanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken);
                if (typeSymbol is not null && IsLinqToSqlDataContext(typeSymbol))
                {
                    AccumulateDesignerContextMembers(request, accumulator, state, semanticDocument, relativePath, sourceText, classDeclaration, typeSymbol, entityKeysByEntityName, tableKeysByEntityName);
                }
            }
        }

        /// <summary>
        /// Emits or refines a LINQ to SQL DataContext node from generated source metadata.
        /// </summary>
        private static void AccumulateDesignerDataContext(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, LinqToSqlSemanticExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, ClassDeclarationSyntax classDeclaration, INamedTypeSymbol typeSymbol)
        {
            EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, classDeclaration, EvidenceKind.DesignerGeneratedCode, "DesignerDataContext", typeSymbol.Name, typeSymbol.ContainingNamespace.ToDisplayString(), Confidence.Certain, UnknownState.Known);
            StableKey contextStableKey = state.ContextKeysByTypeName.TryGetValue(typeSymbol.Name, out StableKey existingKey) ? existingKey : CreateScopedKey("linqtosql", relativePath, typeSymbol.Name);
            string? databaseName = GetAttributeNamedValue(typeSymbol, "Database", "Name");
            ArchitectureNode contextNode = CreateNode(request.SnapshotStableKey, contextStableKey, NodeKind.LinqToSqlDataContext, typeSymbol.Name, typeSymbol.ToDisplayString(), "C#", null, evidence.StableKey, Confidence.Certain, UnknownState.Known, CreateDesignerContextMetadata(databaseName, typeSymbol.Name, state.ModelPathForContext(typeSymbol.Name, relativePath), relativePath));
            accumulator.AddEvidence(evidence).AddNode(contextNode);
            state.ContextKeysByTypeName[typeSymbol.Name] = contextStableKey;
        }

        /// <summary>
        /// Emits or refines entity, table, column, and association facts from one generated entity class.
        /// </summary>
        private static void AccumulateDesignerEntity(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, LinqToSqlSemanticExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, ClassDeclarationSyntax classDeclaration, INamedTypeSymbol typeSymbol, DesignerTableFact tableFact, Dictionary<string, StableKey> entityKeysByEntityName, Dictionary<string, StableKey> tableKeysByEntityName)
        {
            EvidenceRecord entityEvidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, classDeclaration, EvidenceKind.DesignerGeneratedCode, "DesignerEntity", typeSymbol.Name, typeSymbol.ContainingNamespace.ToDisplayString(), Confidence.Certain, UnknownState.Known);
            string modelPath = state.ModelPathForEntity(typeSymbol.Name, relativePath);
            StableKey entityStableKey = state.EntityKeysByTypeName.TryGetValue(typeSymbol.Name, out StableKey existingEntityKey) ? existingEntityKey : CreateScopedKey("entity", modelPath, typeSymbol.Name);
            ArchitectureNode entityNode = CreateNode(request.SnapshotStableKey, entityStableKey, NodeKind.Entity, typeSymbol.Name, typeSymbol.ToDisplayString(), "C#", null, entityEvidence.StableKey, Confidence.Certain, UnknownState.Known, CreateDesignerEntityMetadata(typeSymbol.Name, tableFact.TableName, modelPath, relativePath));
            accumulator.AddEvidence(entityEvidence).AddNode(entityNode);
            entityKeysByEntityName[typeSymbol.Name] = entityStableKey;
            state.EntityKeysByTypeName[typeSymbol.Name] = entityStableKey;

            StableKey tableStableKey = state.TableKeysByQualifiedName.TryGetValue(tableFact.QualifiedName, out StableKey existingTableKey) ? existingTableKey : CreateScopedKey("dbtable", modelPath, tableFact.QualifiedName);
            EvidenceRecord tableEvidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, classDeclaration, EvidenceKind.DesignerGeneratedCode, "DesignerDatabaseTable", tableFact.ObjectName, typeSymbol.ToDisplayString(), Confidence.Certain, UnknownState.Known);
            ArchitectureNode tableNode = CreateNode(request.SnapshotStableKey, tableStableKey, NodeKind.DatabaseTable, tableFact.ObjectName, tableFact.QualifiedName, "Database", null, tableEvidence.StableKey, Confidence.Certain, UnknownState.Known, CreateDesignerTableMetadata(tableFact, modelPath, relativePath));
            accumulator.AddEvidence(tableEvidence).AddNode(tableNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsTable, entityStableKey, tableStableKey, tableEvidence.StableKey, CreateDesignerRelationshipMetadata("EntityTableMapping", modelPath, relativePath)));
            tableKeysByEntityName[typeSymbol.Name] = tableStableKey;
            state.TableKeysByQualifiedName[tableFact.QualifiedName] = tableStableKey;
            state.TableKeysByEntityTypeName[typeSymbol.Name] = tableStableKey;

            foreach (IPropertySymbol propertySymbol in typeSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                DesignerColumnFact? columnFact = TryCreateDesignerColumnFact(propertySymbol, tableFact);
                if (columnFact is not null)
                {
                    AccumulateDesignerColumn(request, accumulator, semanticDocument, relativePath, sourceText, propertySymbol, tableStableKey, entityStableKey, modelPath, columnFact);
                    continue;
                }

                DesignerAssociationFact? associationFact = TryCreateDesignerAssociationFact(propertySymbol);
                if (associationFact is not null)
                {
                    EvidenceRecord associationEvidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, propertySymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() ?? classDeclaration, EvidenceKind.DesignerGeneratedCode, "DesignerAssociation", associationFact.AssociationName, typeSymbol.ToDisplayString(), Confidence.High, UnknownState.Known);
                    accumulator.AddEvidence(associationEvidence).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsEntity, entityStableKey, entityStableKey, associationEvidence.StableKey, CreateDesignerAssociationMetadata(associationFact, modelPath, relativePath)));
                }
            }
        }

        /// <summary>
        /// Emits generated column metadata for one mapped entity property.
        /// </summary>
        private static void AccumulateDesignerColumn(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IPropertySymbol propertySymbol, StableKey tableStableKey, StableKey entityStableKey, string modelPath, DesignerColumnFact columnFact)
        {
            SyntaxNode propertySyntax = propertySymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() ?? semanticDocument.SyntaxTree.GetRoot();
            EvidenceRecord columnEvidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, propertySyntax, EvidenceKind.DesignerGeneratedCode, "DesignerDatabaseColumn", columnFact.ColumnName, propertySymbol.ContainingType.ToDisplayString(), Confidence.Certain, UnknownState.Known);
            StableKey columnStableKey = CreateScopedKey("dbcolumn", modelPath, columnFact.QualifiedName);
            ArchitectureNode columnNode = CreateNode(request.SnapshotStableKey, columnStableKey, NodeKind.DatabaseColumn, columnFact.ColumnName, columnFact.QualifiedName, "Database", tableStableKey, columnEvidence.StableKey, Confidence.Certain, UnknownState.Known, CreateDesignerColumnMetadata(columnFact, propertySymbol.Name, modelPath, relativePath));
            accumulator.AddEvidence(columnEvidence).AddNode(columnNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsColumn, tableStableKey, columnStableKey, columnEvidence.StableKey, CreateDesignerRelationshipMetadata("TableColumnMapping", modelPath, relativePath)));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsColumn, entityStableKey, columnStableKey, columnEvidence.StableKey, CreateDesignerRelationshipMetadata("EntityColumnMapping", modelPath, relativePath)));
        }

        /// <summary>
        /// Emits generated DataContext table-property and stored-procedure wrapper relationships.
        /// </summary>
        private static void AccumulateDesignerContextMembers(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, LinqToSqlSemanticExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, ClassDeclarationSyntax classDeclaration, INamedTypeSymbol typeSymbol, Dictionary<string, StableKey> entityKeysByEntityName, Dictionary<string, StableKey> tableKeysByEntityName)
        {
            StableKey contextStableKey = state.ContextKeysByTypeName.TryGetValue(typeSymbol.Name, out StableKey existingContextKey) ? existingContextKey : CreateScopedKey("linqtosql", relativePath, typeSymbol.Name);
            string modelPath = state.ModelPathForContext(typeSymbol.Name, relativePath);
            foreach (IPropertySymbol propertySymbol in typeSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                string? entityTypeName = TryGetTableEntityTypeName(propertySymbol.Type);
                if (entityTypeName is null || !entityKeysByEntityName.TryGetValue(entityTypeName, out StableKey entityStableKey))
                {
                    continue;
                }

                SyntaxNode propertySyntax = propertySymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() ?? classDeclaration;
                EvidenceRecord tablePropertyEvidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, propertySyntax, EvidenceKind.DesignerGeneratedCode, "DesignerTableProperty", propertySymbol.Name, typeSymbol.ToDisplayString(), Confidence.Certain, UnknownState.Known);
                accumulator.AddEvidence(tablePropertyEvidence).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsEntity, contextStableKey, entityStableKey, tablePropertyEvidence.StableKey, CreateDesignerRelationshipMetadata("DataContextEntityMapping", modelPath, relativePath)));
                if (tableKeysByEntityName.TryGetValue(entityTypeName, out StableKey tableStableKey))
                {
                    state.TableKeysByPropertyName[propertySymbol.Name] = tableStableKey;
                    accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsTable, contextStableKey, tableStableKey, tablePropertyEvidence.StableKey, CreateDesignerRelationshipMetadata("DataContextTableProperty", modelPath, relativePath)));
                }
            }

            foreach (IMethodSymbol methodSymbol in typeSymbol.GetMembers().OfType<IMethodSymbol>().Where(static method => method.MethodKind == MethodKind.Ordinary))
            {
                string? procedureName = GetAttributeNamedValue(methodSymbol, "Function", "Name");
                if (string.IsNullOrWhiteSpace(procedureName))
                {
                    continue;
                }

                ParsedDatabaseObjectName parsedName = ParseDatabaseObjectName(procedureName);
                StableKey procedureStableKey = state.StoredProcedureKeysByQualifiedName.TryGetValue(parsedName.QualifiedName, out StableKey existingProcedureKey) ? existingProcedureKey : CreateScopedKey("storedprocedure", modelPath, parsedName.QualifiedName);
                SyntaxNode methodSyntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() ?? classDeclaration;
                EvidenceRecord procedureEvidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, methodSyntax, EvidenceKind.DesignerGeneratedCode, "DesignerStoredProcedure", parsedName.ObjectName, typeSymbol.ToDisplayString(), Confidence.Certain, UnknownState.Known);
                ArchitectureNode procedureNode = CreateNode(request.SnapshotStableKey, procedureStableKey, NodeKind.StoredProcedure, parsedName.ObjectName, parsedName.QualifiedName, "Database", contextStableKey, procedureEvidence.StableKey, Confidence.Certain, UnknownState.Known, CreateDesignerStoredProcedureMetadata(methodSymbol, parsedName, modelPath, relativePath));
                accumulator.AddEvidence(procedureEvidence).AddNode(procedureNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.CallsStoredProcedure, contextStableKey, procedureStableKey, procedureEvidence.StableKey, CreateDesignerRelationshipMetadata("DataContextStoredProcedureWrapper", modelPath, relativePath)));
                state.StoredProcedureKeysByQualifiedName[parsedName.QualifiedName] = procedureStableKey;
                state.StoredProcedureKeysByMethodName[methodSymbol.Name] = procedureStableKey;
            }
        }

        /// <summary>
        /// Emits graph relationships for source-code LINQ to SQL usage within method bodies.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="state">The semantic extraction state used to resolve model targets.</param>
        /// <param name="semanticDocument">The Roslyn semantic document to inspect.</param>
        /// <param name="cancellationToken">A token that signals when source traversal should stop.</param>
        private static void AccumulateSourceUsage(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, LinqToSqlSemanticExtractionState state, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Source usage extraction is intentionally pattern-based and side-effect free; it records observed APIs without executing user code or connecting to databases.
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(request.RepositoryRootDirectory, semanticDocument.DocumentPath);
            string sourceText = semanticDocument.SyntaxTree.GetText(cancellationToken).ToString();
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            foreach (MethodDeclarationSyntax methodDeclaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ISymbol? declaredSymbol = semanticDocument.SemanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken);
                if (declaredSymbol is not IMethodSymbol methodSymbol || methodDeclaration.Body is null && methodDeclaration.ExpressionBody is null)
                {
                    continue;
                }

                MethodUsageState usageState = MethodUsageState.FromMethod(methodSymbol, request.SnapshotStableKey, relativePath, semanticDocument.ProjectContext);
                foreach (ObjectCreationExpressionSyntax objectCreation in methodDeclaration.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                {
                    INamedTypeSymbol? createdType = semanticDocument.SemanticModel.GetTypeInfo(objectCreation, cancellationToken).Type as INamedTypeSymbol;
                    if (createdType is not null && IsLinqToSqlDataContext(createdType) && state.ContextKeysByTypeName.TryGetValue(createdType.Name, out StableKey contextStableKey))
                    {
                        EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, objectCreation, EvidenceKind.SourceCode, "LinqToSqlDataContextConstruction", createdType.Name, methodSymbol.ToDisplayString(), Confidence.High, UnknownState.Known);
                        ArchitectureNode methodNode = CreateMethodNode(request.SnapshotStableKey, usageState.MethodStableKey, methodSymbol, semanticDocument.ProjectContext, evidence.StableKey, CreateUsageMethodMetadata(relativePath, semanticDocument.ProjectContext));
                        accumulator.AddEvidence(evidence).AddNode(methodNode).AddEdge(CreateUsageEdge(request.SnapshotStableKey, EdgeKind.UsesLinqToSqlContext, usageState.MethodStableKey, contextStableKey, evidence.StableKey, CreateUsageRelationshipMetadata("DataContextConstruction", relativePath, semanticDocument.ProjectContext, null, null), Confidence.High, UnknownState.Known));
                        string? contextVariableName = GetAssignedVariableName(objectCreation);
                        if (!string.IsNullOrWhiteSpace(contextVariableName))
                        {
                            usageState.ContextKeysByVariable[contextVariableName] = contextStableKey;
                        }
                    }
                }

                foreach (InvocationExpressionSyntax invocation in methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AccumulateSyntaxTablePropertyReadUsage(request, accumulator, state, semanticDocument, relativePath, sourceText, methodSymbol, usageState, invocation);
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
        /// Classifies and emits one LINQ to SQL invocation usage relationship.
        /// </summary>
        private static void AccumulateInvocationUsage(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, LinqToSqlSemanticExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol containingMethod, MethodUsageState usageState, InvocationExpressionSyntax invocation, IMethodSymbol invokedMethod)
        {
            string methodName = invokedMethod.Name;
            if (string.Equals(methodName, "GetTable", StringComparison.Ordinal) && invokedMethod.TypeArguments.Length == 1)
            {
                AccumulateGetTableUsage(request, accumulator, state, semanticDocument, relativePath, sourceText, containingMethod, usageState, invocation, invokedMethod.TypeArguments[0]);
                return;
            }

            if (string.Equals(methodName, "InsertOnSubmit", StringComparison.Ordinal) || string.Equals(methodName, "DeleteOnSubmit", StringComparison.Ordinal) || string.Equals(methodName, "Attach", StringComparison.Ordinal))
            {
                AccumulateTableWriteUsage(request, accumulator, state, semanticDocument, relativePath, sourceText, containingMethod, usageState, invocation, invokedMethod);
                return;
            }

            if (string.Equals(methodName, "SubmitChanges", StringComparison.Ordinal))
            {
                AccumulateSubmitChangesUsage(request, accumulator, state, semanticDocument, relativePath, sourceText, containingMethod, usageState, invocation);
                return;
            }

            if (string.Equals(methodName, "ExecuteQuery", StringComparison.Ordinal) || string.Equals(methodName, "ExecuteCommand", StringComparison.Ordinal))
            {
                AccumulateRawSqlUsage(request, accumulator, state, semanticDocument, relativePath, sourceText, containingMethod, usageState, invocation, invokedMethod);
                return;
            }

            if (state.StoredProcedureKeysByMethodName.TryGetValue(methodName, out StableKey procedureStableKey))
            {
                EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, EvidenceKind.SourceCode, "StoredProcedureWrapperCall", methodName, containingMethod.ToDisplayString(), Confidence.High, UnknownState.Known);
                accumulator.AddEvidence(evidence).AddNode(CreateMethodNode(request.SnapshotStableKey, usageState.MethodStableKey, containingMethod, semanticDocument.ProjectContext, evidence.StableKey, CreateUsageMethodMetadata(relativePath, semanticDocument.ProjectContext))).AddEdge(CreateUsageEdge(request.SnapshotStableKey, EdgeKind.CallsStoredProcedure, usageState.MethodStableKey, procedureStableKey, evidence.StableKey, CreateUsageRelationshipMetadata("StoredProcedureWrapperCall", relativePath, semanticDocument.ProjectContext, null, null), Confidence.High, UnknownState.Known));
            }
            else
            {
                AccumulateTablePropertyReadUsage(request, accumulator, state, semanticDocument, relativePath, sourceText, containingMethod, usageState, invocation, invokedMethod);
            }
        }

        /// <summary>
        /// Emits a read relationship for GetTable&lt;TEntity&gt; calls and records unknowns for unresolved entity targets.
        /// </summary>
        private static void AccumulateGetTableUsage(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, LinqToSqlSemanticExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol containingMethod, MethodUsageState usageState, InvocationExpressionSyntax invocation, ITypeSymbol entityType)
        {
            EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, EvidenceKind.SourceCode, "GetTableUsage", entityType.Name, containingMethod.ToDisplayString(), Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(evidence).AddNode(CreateMethodNode(request.SnapshotStableKey, usageState.MethodStableKey, containingMethod, semanticDocument.ProjectContext, evidence.StableKey, CreateUsageMethodMetadata(relativePath, semanticDocument.ProjectContext)));
            if (state.TableKeysByEntityTypeName.TryGetValue(entityType.Name, out StableKey tableStableKey))
            {
                accumulator.AddEdge(CreateUsageEdge(request.SnapshotStableKey, EdgeKind.ReadsTable, usageState.MethodStableKey, tableStableKey, evidence.StableKey, CreateUsageRelationshipMetadata("GetTable", relativePath, semanticDocument.ProjectContext, "Read", null), Confidence.High, UnknownState.Known));
                string? tableVariableName = GetAssignedVariableName(invocation);
                if (!string.IsNullOrWhiteSpace(tableVariableName))
                {
                    usageState.TableKeysByVariable[tableVariableName] = tableStableKey;
                }
                return;
            }

            UnknownState unknownState = UnknownState.Unknown($"LINQ to SQL GetTable target {entityType.Name} could not be resolved to a mapped table.");
            StableKey unknownTableKey = CreateScopedKey("dbtable", relativePath, $"Unknown:{entityType.Name}:{HashStablePayload(semanticDocument.ProjectContext, containingMethod.ToDisplayString())}");
            ArchitectureNode unknownTableNode = CreateNode(request.SnapshotStableKey, unknownTableKey, NodeKind.DatabaseTable, $"UnknownTable:{entityType.Name}", null, "Database", null, evidence.StableKey, Confidence.Low, unknownState, CreateUnknownUsageMetadata(relativePath, semanticDocument.ProjectContext, entityType.Name, "GetTable"));
            accumulator.AddNode(unknownTableNode).AddEdge(CreateUsageEdge(request.SnapshotStableKey, EdgeKind.ReadsTable, usageState.MethodStableKey, unknownTableKey, evidence.StableKey, CreateUsageRelationshipMetadata("GetTable", relativePath, semanticDocument.ProjectContext, "Read", "UnresolvedTarget"), Confidence.Low, unknownState));
            accumulator.AddWarning($"LINQ to SQL GetTable target {entityType.Name} in {relativePath} could not be resolved to a mapped table.");
        }

        /// <summary>
        /// Emits write relationships for Table&lt;TEntity&gt; mutation methods.
        /// </summary>
        private static void AccumulateTableWriteUsage(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, LinqToSqlSemanticExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol containingMethod, MethodUsageState usageState, InvocationExpressionSyntax invocation, IMethodSymbol invokedMethod)
        {
            string? tableVariable = GetReceiverName(invocation);
            StableKey? tableStableKey = tableVariable is not null && usageState.TableKeysByVariable.TryGetValue(tableVariable, out StableKey variableTableKey) ? variableTableKey : TryResolveTableFromTableReceiver(state, invokedMethod.ReceiverType);
            if (tableStableKey is null)
            {
                return;
            }

            EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, EvidenceKind.SourceCode, "TableWriteUsage", invokedMethod.Name, containingMethod.ToDisplayString(), Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(evidence).AddNode(CreateMethodNode(request.SnapshotStableKey, usageState.MethodStableKey, containingMethod, semanticDocument.ProjectContext, evidence.StableKey, CreateUsageMethodMetadata(relativePath, semanticDocument.ProjectContext))).AddEdge(CreateUsageEdge(request.SnapshotStableKey, EdgeKind.WritesTable, usageState.MethodStableKey, tableStableKey.Value, evidence.StableKey, CreateUsageRelationshipMetadata(invokedMethod.Name, relativePath, semanticDocument.ProjectContext, "Write", null), Confidence.High, UnknownState.Known));
        }

        /// <summary>
        /// Emits write hints for SubmitChanges when a method already observed table mutations.
        /// </summary>
        private static void AccumulateSubmitChangesUsage(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, LinqToSqlSemanticExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol containingMethod, MethodUsageState usageState, InvocationExpressionSyntax invocation)
        {
            EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, EvidenceKind.SourceCode, "SubmitChangesUsage", "SubmitChanges", containingMethod.ToDisplayString(), Confidence.Medium, UnknownState.Known);
            accumulator.AddEvidence(evidence).AddNode(CreateMethodNode(request.SnapshotStableKey, usageState.MethodStableKey, containingMethod, semanticDocument.ProjectContext, evidence.StableKey, CreateUsageMethodMetadata(relativePath, semanticDocument.ProjectContext)));
            foreach (StableKey tableStableKey in usageState.TableKeysByVariable.Values.Distinct())
            {
                accumulator.AddEdge(CreateUsageEdge(request.SnapshotStableKey, EdgeKind.WritesTable, usageState.MethodStableKey, tableStableKey, evidence.StableKey, CreateUsageRelationshipMetadata("SubmitChanges", relativePath, semanticDocument.ProjectContext, "Write", null), Confidence.Medium, UnknownState.Known));
            }
        }

        /// <summary>
        /// Emits raw SQL execution relationships and any table hints inferred from ExecuteQuery generic type arguments.
        /// </summary>
        private static void AccumulateRawSqlUsage(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, LinqToSqlSemanticExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol containingMethod, MethodUsageState usageState, InvocationExpressionSyntax invocation, IMethodSymbol invokedMethod)
        {
            bool isQuery = string.Equals(invokedMethod.Name, "ExecuteQuery", StringComparison.Ordinal);
            EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, EvidenceKind.SourceCode, "RawSqlUsage", invokedMethod.Name, containingMethod.ToDisplayString(), Confidence.Medium, UnknownState.Known);
            StableKey rawSqlKey = new($"rawsql://{HashStablePayload(relativePath, containingMethod.ToDisplayString(), invocation.SpanStart.ToString(), invokedMethod.Name)}");
            string? sqlPreview = GetFirstArgumentLiteral(invocation, semanticDocument.SemanticModel);
            bool computedSql = string.Equals(sqlPreview, "[ComputedSql]", StringComparison.Ordinal);
            UnknownState unknownState = computedSql ? UnknownState.Unknown("LINQ to SQL raw SQL text is computed and cannot be statically resolved.") : UnknownState.Known;
            ArchitectureNode rawSqlNode = CreateNode(request.SnapshotStableKey, rawSqlKey, NodeKind.SqlScript, invokedMethod.Name, null, "SQL", usageState.MethodStableKey, evidence.StableKey, computedSql ? Confidence.Low : Confidence.Medium, unknownState, CreateRawSqlMetadata(relativePath, semanticDocument.ProjectContext, invokedMethod.Name, sqlPreview));
            accumulator.AddEvidence(evidence).AddNode(CreateMethodNode(request.SnapshotStableKey, usageState.MethodStableKey, containingMethod, semanticDocument.ProjectContext, evidence.StableKey, CreateUsageMethodMetadata(relativePath, semanticDocument.ProjectContext))).AddNode(rawSqlNode).AddEdge(CreateUsageEdge(request.SnapshotStableKey, EdgeKind.ExecutesRawSql, usageState.MethodStableKey, rawSqlKey, evidence.StableKey, computedSql ? CreateComputedRawSqlRelationshipMetadata(relativePath, semanticDocument.ProjectContext, invokedMethod.Name) : CreateUsageRelationshipMetadata("RawSqlExecution", relativePath, semanticDocument.ProjectContext, isQuery ? "Read" : "Write", invokedMethod.Name), computedSql ? Confidence.Low : Confidence.Medium, unknownState));

            if (isQuery && invokedMethod.TypeArguments.Length == 1 && state.TableKeysByEntityTypeName.TryGetValue(invokedMethod.TypeArguments[0].Name, out StableKey tableStableKey))
            {
                accumulator.AddEdge(CreateUsageEdge(request.SnapshotStableKey, EdgeKind.ReadsTable, usageState.MethodStableKey, tableStableKey, evidence.StableKey, CreateUsageRelationshipMetadata("ExecuteQuery", relativePath, semanticDocument.ProjectContext, "Read", invokedMethod.Name), Confidence.Medium, UnknownState.Known));
            }
        }

        /// <summary>
        /// Emits read relationships for generated DataContext Table&lt;TEntity&gt; property calls seen as property getter invocations.
        /// </summary>
        private static void AccumulateTablePropertyReadUsage(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, LinqToSqlSemanticExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol containingMethod, MethodUsageState usageState, InvocationExpressionSyntax invocation, IMethodSymbol invokedMethod)
        {
            if (!state.TableKeysByPropertyName.TryGetValue(invokedMethod.AssociatedSymbol?.Name ?? invokedMethod.Name.TrimStart("get_".ToCharArray()), out StableKey tableStableKey))
            {
                return;
            }

            EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, EvidenceKind.SourceCode, "TablePropertyRead", invokedMethod.Name, containingMethod.ToDisplayString(), Confidence.High, UnknownState.Known);
            accumulator.AddEvidence(evidence).AddNode(CreateMethodNode(request.SnapshotStableKey, usageState.MethodStableKey, containingMethod, semanticDocument.ProjectContext, evidence.StableKey, CreateUsageMethodMetadata(relativePath, semanticDocument.ProjectContext))).AddEdge(CreateUsageEdge(request.SnapshotStableKey, EdgeKind.ReadsTable, usageState.MethodStableKey, tableStableKey, evidence.StableKey, CreateUsageRelationshipMetadata("TablePropertyRead", relativePath, semanticDocument.ProjectContext, "Read", null), Confidence.High, UnknownState.Known));
        }

        /// <summary>
        /// Emits read relationships for source syntax that references generated table properties inside LINQ extension call chains.
        /// </summary>
        private static void AccumulateSyntaxTablePropertyReadUsage(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, LinqToSqlSemanticExtractionState state, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol containingMethod, MethodUsageState usageState, InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return;
            }

            foreach (KeyValuePair<string, StableKey> tableProperty in state.TableKeysByPropertyName)
            {
                if (!memberAccess.ToString().Contains($".{tableProperty.Key}", StringComparison.Ordinal))
                {
                    continue;
                }

                EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, EvidenceKind.SourceCode, "TablePropertyRead", tableProperty.Key, containingMethod.ToDisplayString(), Confidence.High, UnknownState.Known);
                accumulator.AddEvidence(evidence).AddNode(CreateMethodNode(request.SnapshotStableKey, usageState.MethodStableKey, containingMethod, semanticDocument.ProjectContext, evidence.StableKey, CreateUsageMethodMetadata(relativePath, semanticDocument.ProjectContext))).AddEdge(CreateUsageEdge(request.SnapshotStableKey, EdgeKind.ReadsTable, usageState.MethodStableKey, tableProperty.Value, evidence.StableKey, CreateUsageRelationshipMetadata("TablePropertyRead", relativePath, semanticDocument.ProjectContext, "Read", null), Confidence.High, UnknownState.Known));
            }
        }

        /// <summary>
        /// Creates a DBML evidence record for an XML model element.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning snapshot.</param>
        /// <param name="relativePath">The repository-relative DBML file path.</param>
        /// <param name="element">The XML element that supports the graph fact.</param>
        /// <param name="redactedContent">The redacted DBML content used for fallback snippets.</param>
        /// <param name="role">The evidence role within DBML extraction.</param>
        /// <param name="symbolName">The optional model symbol associated with the evidence.</param>
        /// <param name="location">The XML artifact location for the evidence.</param>
        /// <param name="confidence">The confidence assigned to the evidence.</param>
        /// <param name="unknownState">The unknown-state assigned to the evidence.</param>
        /// <returns>A deterministic DBML evidence record.</returns>
        private static EvidenceRecord CreateEvidence(StableKey snapshotStableKey, string relativePath, XElement element, string redactedContent, string role, string? symbolName, DbmlLocation location, Confidence confidence, UnknownState unknownState)
        {
            // Evidence snippets are redacted before hashing so secret-like DBML connection values cannot be recovered from graph output.
            string preview = CreatePreview(element, redactedContent);
            string snippetHash = HashStablePayload(preview);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "DbmlXmlModel",
                ["evidenceRole"] = role,
                ["extractor"] = nameof(LinqToSqlDbmlModelExtractor),
                ["xmlElement"] = element.Name.LocalName,
                ["xmlLine"] = location.LineNumber,
                ["xmlColumn"] = location.LinePosition
            });
            StableKey stableKey = new($"dbml-evidence://{HashStablePayload(relativePath, role, symbolName, location.LineNumber.ToString(), location.LinePosition.ToString(), snippetHash)}");
            return new EvidenceRecord(snapshotStableKey, stableKey, EvidenceKind.Dbml, RepositoryRelativePath.Parse(relativePath), location.LineNumber, location.LineNumber, symbolName, null, snippetHash, preview, KnowledgeKind.Fact, confidence, unknownState, metadata, FingerprintGenerator.ForEvidence(EvidenceKind.Dbml, relativePath, location.LineNumber, location.LineNumber, symbolName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates an architecture node using shared graph contracts and deterministic fingerprint input.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning snapshot.</param>
        /// <param name="stableKey">The deterministic node stable key.</param>
        /// <param name="nodeKind">The graph node kind to emit.</param>
        /// <param name="displayName">The developer-facing node display name.</param>
        /// <param name="qualifiedName">The optional qualified name for the node.</param>
        /// <param name="language">The artifact or model language associated with the node.</param>
        /// <param name="parentNodeStableKey">The optional parent graph node stable key.</param>
        /// <param name="primaryEvidenceStableKey">The evidence stable key explaining the node.</param>
        /// <param name="confidence">The confidence assigned to the node.</param>
        /// <param name="unknownState">The unknown-state assigned to the node.</param>
        /// <param name="metadata">The deterministic metadata payload for the node.</param>
        /// <returns>A graph-ready architecture node.</returns>
        private static ArchitectureNode CreateNode(StableKey snapshotStableKey, StableKey stableKey, NodeKind nodeKind, string displayName, string? qualifiedName, string language, StableKey? parentNodeStableKey, StableKey primaryEvidenceStableKey, Confidence confidence, UnknownState unknownState, GraphMetadata metadata)
        {
            // Search names mirror qualified names when present so graph consumers can find database and model artifacts consistently.
            string searchName = string.IsNullOrWhiteSpace(qualifiedName) ? displayName : qualifiedName;
            return new ArchitectureNode(snapshotStableKey, stableKey, nodeKind, displayName, qualifiedName, searchName, language, null, parentNodeStableKey, KnowledgeKind.Fact, null, null, confidence, unknownState, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForNode(nodeKind, displayName, qualifiedName, searchName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates an architecture edge using shared graph contracts and deterministic fingerprint input.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning snapshot.</param>
        /// <param name="edgeKind">The graph edge kind to emit.</param>
        /// <param name="sourceStableKey">The stable key of the source graph node.</param>
        /// <param name="targetStableKey">The stable key of the target graph node.</param>
        /// <param name="primaryEvidenceStableKey">The evidence stable key explaining the relationship.</param>
        /// <param name="metadata">The deterministic metadata payload for the edge.</param>
        /// <returns>A graph-ready architecture edge.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, EdgeKind edgeKind, StableKey sourceStableKey, StableKey targetStableKey, StableKey primaryEvidenceStableKey, GraphMetadata metadata)
        {
            // Edge identity includes kind and endpoints so duplicate model observations merge deterministically in the accumulator.
            StableKey stableKey = new($"dbml-edge://{HashStablePayload(edgeKind.Value, sourceStableKey.Value, targetStableKey.Value, metadata.ToCanonicalJson())}");
            return new ArchitectureEdge(snapshotStableKey, stableKey, edgeKind, sourceStableKey, targetStableKey, true, KnowledgeKind.Fact, Confidence.Certain, UnknownState.Known, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForEdge(edgeKind, sourceStableKey, targetStableKey, true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a source usage relationship using explicit confidence and unknown-state values.
        /// </summary>
        private static ArchitectureEdge CreateUsageEdge(StableKey snapshotStableKey, EdgeKind edgeKind, StableKey sourceStableKey, StableKey targetStableKey, StableKey primaryEvidenceStableKey, GraphMetadata metadata, Confidence confidence, UnknownState unknownState)
        {
            StableKey stableKey = new($"linqtosql-usage-edge://{HashStablePayload(edgeKind.Value, sourceStableKey.Value, targetStableKey.Value, metadata.ToCanonicalJson(), unknownState.HasUnknownData.ToString())}");
            return new ArchitectureEdge(snapshotStableKey, stableKey, edgeKind, sourceStableKey, targetStableKey, true, KnowledgeKind.Fact, confidence, unknownState, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForEdge(edgeKind, sourceStableKey, targetStableKey, true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a method node for a source method that contains data-access usage.
        /// </summary>
        private static ArchitectureNode CreateMethodNode(StableKey snapshotStableKey, StableKey stableKey, IMethodSymbol methodSymbol, string projectContext, StableKey primaryEvidenceStableKey, GraphMetadata metadata)
        {
            string qualifiedName = methodSymbol.ToDisplayString();
            return new ArchitectureNode(snapshotStableKey, stableKey, NodeKind.Method, methodSymbol.Name, qualifiedName, qualifiedName, "C#", new StableKey($"project://{projectContext}"), null, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Method, methodSymbol.Name, qualifiedName, qualifiedName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Extracts safe connection metadata from a DBML Database element.
        /// </summary>
        /// <param name="databaseElement">The DBML Database element that may contain a Connection child.</param>
        /// <returns>A connection fact containing only non-secret metadata.</returns>
        private static DbmlConnectionFact ExtractConnection(XElement databaseElement)
        {
            // DBML may embed raw connection strings; this method intentionally keeps only provider and configuration-key identifiers.
            XElement? connectionElement = databaseElement.Elements().FirstOrDefault(static element => IsNamed(element, "Connection"));
            if (connectionElement is null)
            {
                return new DbmlConnectionFact(null, null, null, false);
            }

            string? settingsPropertyName = GetAttribute(connectionElement, "SettingsPropertyName");
            string? provider = GetAttribute(connectionElement, "Provider");
            string? mode = GetAttribute(connectionElement, "Mode");
            bool hadSecretLikeValue = !string.IsNullOrWhiteSpace(GetAttribute(connectionElement, "ConnectionString"));
            return new DbmlConnectionFact(settingsPropertyName, provider, mode, hadSecretLikeValue);
        }

        /// <summary>
        /// Creates metadata for a LINQ to SQL DataContext node.
        /// </summary>
        /// <param name="databaseName">The DBML database name when present.</param>
        /// <param name="contextClass">The DBML DataContext class name when present.</param>
        /// <param name="relativePath">The repository-relative DBML model file path.</param>
        /// <param name="connection">The safe connection metadata extracted from the model.</param>
        /// <returns>A deterministic metadata value for the DataContext node.</returns>
        private static GraphMetadata CreateContextMetadata(string? databaseName, string? contextClass, string relativePath, DbmlConnectionFact connection)
        {
            // Metadata follows WP009 lower-camel field names and excludes any raw connection string value.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath);
            values["dataAccessTechnology"] = "LinqToSql";
            values["contextType"] = contextClass;
            values["databaseName"] = databaseName;
            values["connectionStringKey"] = connection.SettingsPropertyName;
            values["provider"] = NormalizeProvider(connection.Provider);
            values["connectionMode"] = connection.Mode;
            values["connectionStringRedacted"] = connection.HadSecretLikeConnectionString;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates metadata for a LINQ to SQL entity node.
        /// </summary>
        /// <param name="entityName">The DBML entity type name when present.</param>
        /// <param name="memberName">The DBML table member name when present.</param>
        /// <param name="tableName">The DBML table name when present.</param>
        /// <param name="relativePath">The repository-relative DBML model file path.</param>
        /// <returns>A deterministic metadata value for the entity node.</returns>
        private static GraphMetadata CreateEntityMetadata(string? entityName, string? memberName, string? tableName, string relativePath)
        {
            // Entity metadata preserves DBML names without inventing a compiled CLR type that has not yet been analyzed.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath);
            values["dataAccessTechnology"] = "LinqToSql";
            values["entityType"] = entityName;
            values["tableMember"] = memberName;
            values["tableName"] = tableName;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates metadata for a database table node.
        /// </summary>
        /// <param name="tableName">The parsed database object name.</param>
        /// <param name="memberName">The DBML table member name when present.</param>
        /// <param name="relativePath">The repository-relative DBML model file path.</param>
        /// <returns>A deterministic metadata value for the table node.</returns>
        private static GraphMetadata CreateTableMetadata(ParsedDatabaseObjectName tableName, string? memberName, string relativePath)
        {
            // Table metadata records both database identity parts and the model member that exposes the table in the DataContext.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath);
            values["dataAccessTechnology"] = "LinqToSql";
            values["schemaName"] = tableName.SchemaName;
            values["tableName"] = tableName.ObjectName;
            values["tableMember"] = memberName;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates metadata for a database column node.
        /// </summary>
        /// <param name="columnElement">The DBML Column element.</param>
        /// <param name="tableName">The parsed table name that owns the column.</param>
        /// <param name="relativePath">The repository-relative DBML model file path.</param>
        /// <returns>A deterministic metadata value for the column node.</returns>
        private static GraphMetadata CreateColumnMetadata(XElement columnElement, ParsedDatabaseObjectName tableName, string relativePath)
        {
            // Column metadata keeps DBML column attributes as model facts while avoiding runtime database inspection.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath);
            values["dataAccessTechnology"] = "LinqToSql";
            values["schemaName"] = tableName.SchemaName;
            values["tableName"] = tableName.ObjectName;
            values["columnName"] = GetAttribute(columnElement, "Name");
            values["propertyName"] = GetAttribute(columnElement, "Member");
            values["columnType"] = GetAttribute(columnElement, "Type");
            values["dbType"] = GetAttribute(columnElement, "DbType");
            values["isPrimaryKey"] = GetAttribute(columnElement, "IsPrimaryKey");
            values["canBeNull"] = GetAttribute(columnElement, "CanBeNull");
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates metadata for a stored procedure node.
        /// </summary>
        /// <param name="functionElement">The DBML Function element.</param>
        /// <param name="procedureName">The parsed stored procedure name.</param>
        /// <param name="relativePath">The repository-relative DBML model file path.</param>
        /// <returns>A deterministic metadata value for the stored procedure node.</returns>
        private static GraphMetadata CreateStoredProcedureMetadata(XElement functionElement, ParsedDatabaseObjectName procedureName, string relativePath)
        {
            // Function metadata records wrapper method identity and parameter names while treating the database call as a stored procedure fact.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath);
            values["dataAccessTechnology"] = "LinqToSql";
            values["schemaName"] = procedureName.SchemaName;
            values["storedProcedureName"] = procedureName.ObjectName;
            values["methodName"] = GetAttribute(functionElement, "Method");
            values["parameterNames"] = functionElement.Elements().Where(static element => IsNamed(element, "Parameter")).Select(element => FirstNonEmpty(GetAttribute(element, "Name"), GetAttribute(element, "Parameter"))).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates metadata for a DBML association mapping relationship.
        /// </summary>
        /// <param name="associationElement">The DBML Association element.</param>
        /// <param name="relativePath">The repository-relative DBML model file path.</param>
        /// <returns>A deterministic metadata value for the association edge.</returns>
        private static GraphMetadata CreateAssociationMetadata(XElement associationElement, string relativePath)
        {
            // Association metadata preserves relationship attributes even before later slices correlate designer or usage symbols.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath);
            values["dataAccessRelationshipKind"] = "LinqToSqlAssociation";
            values["associationName"] = GetAttribute(associationElement, "Name");
            values["memberName"] = GetAttribute(associationElement, "Member");
            values["thisKey"] = GetAttribute(associationElement, "ThisKey");
            values["otherKey"] = GetAttribute(associationElement, "OtherKey");
            values["targetEntityType"] = GetAttribute(associationElement, "Type");
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates metadata for a generated LINQ to SQL DataContext node.
        /// </summary>
        private static GraphMetadata CreateDesignerContextMetadata(string? databaseName, string contextClass, string modelPath, string generatedPath)
        {
            Dictionary<string, object?> values = CreateBaseMetadata(modelPath);
            values["detectionMode"] = "DbmlAndDesignerSource";
            values["dataAccessTechnology"] = "LinqToSql";
            values["contextType"] = contextClass;
            values["databaseName"] = databaseName;
            values["generatedFilePath"] = generatedPath;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates metadata for a generated LINQ to SQL entity node.
        /// </summary>
        private static GraphMetadata CreateDesignerEntityMetadata(string entityName, string tableName, string modelPath, string generatedPath)
        {
            Dictionary<string, object?> values = CreateBaseMetadata(modelPath);
            values["detectionMode"] = "DbmlAndDesignerSource";
            values["dataAccessTechnology"] = "LinqToSql";
            values["entityType"] = entityName;
            values["tableName"] = tableName;
            values["generatedFilePath"] = generatedPath;
            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Creates metadata for a generated LINQ to SQL database table node.
        /// </summary>
        private static GraphMetadata CreateDesignerTableMetadata(DesignerTableFact tableFact, string modelPath, string generatedPath)
        {
            Dictionary<string, object?> values = CreateBaseMetadata(modelPath);
            values["detectionMode"] = "DbmlAndDesignerSource";
            values["dataAccessTechnology"] = "LinqToSql";
            values["schemaName"] = tableFact.SchemaName;
            values["tableName"] = tableFact.ObjectName;
            values["generatedFilePath"] = generatedPath;
            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Creates metadata for a generated LINQ to SQL database column node.
        /// </summary>
        private static GraphMetadata CreateDesignerColumnMetadata(DesignerColumnFact columnFact, string propertyName, string modelPath, string generatedPath)
        {
            Dictionary<string, object?> values = CreateBaseMetadata(modelPath);
            values["detectionMode"] = "DbmlAndDesignerSource";
            values["dataAccessTechnology"] = "LinqToSql";
            values["schemaName"] = columnFact.SchemaName;
            values["tableName"] = columnFact.TableName;
            values["columnName"] = columnFact.ColumnName;
            values["propertyName"] = propertyName;
            values["dbType"] = columnFact.DbType;
            values["isPrimaryKey"] = columnFact.IsPrimaryKey;
            values["canBeNull"] = columnFact.CanBeNull;
            values["generatedFilePath"] = generatedPath;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates metadata for a generated LINQ to SQL stored-procedure wrapper node.
        /// </summary>
        private static GraphMetadata CreateDesignerStoredProcedureMetadata(IMethodSymbol methodSymbol, ParsedDatabaseObjectName procedureName, string modelPath, string generatedPath)
        {
            Dictionary<string, object?> values = CreateBaseMetadata(modelPath);
            values["detectionMode"] = "DbmlAndDesignerSource";
            values["dataAccessTechnology"] = "LinqToSql";
            values["schemaName"] = procedureName.SchemaName;
            values["storedProcedureName"] = procedureName.ObjectName;
            values["methodName"] = methodSymbol.Name;
            values["parameterNames"] = methodSymbol.Parameters.Select(parameter => parameter.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
            values["generatedFilePath"] = generatedPath;
            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Creates metadata for a generated LINQ to SQL association relationship.
        /// </summary>
        private static GraphMetadata CreateDesignerAssociationMetadata(DesignerAssociationFact associationFact, string modelPath, string generatedPath)
        {
            Dictionary<string, object?> values = CreateBaseMetadata(modelPath);
            values["detectionMode"] = "DbmlAndDesignerSource";
            values["dataAccessRelationshipKind"] = "LinqToSqlAssociation";
            values["associationName"] = associationFact.AssociationName;
            values["memberName"] = associationFact.MemberName;
            values["thisKey"] = associationFact.ThisKey;
            values["otherKey"] = associationFact.OtherKey;
            values["generatedFilePath"] = generatedPath;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates metadata for a generated-source mapping relationship.
        /// </summary>
        private static GraphMetadata CreateDesignerRelationshipMetadata(string relationshipKind, string modelPath, string generatedPath)
        {
            Dictionary<string, object?> values = CreateBaseMetadata(modelPath);
            values["detectionMode"] = "DbmlAndDesignerSource";
            values["dataAccessRelationshipKind"] = relationshipKind;
            values["generatedFilePath"] = generatedPath;
            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Creates metadata for a method node observed during LINQ to SQL source usage extraction.
        /// </summary>
        private static GraphMetadata CreateUsageMethodMetadata(string relativePath, string projectContext)
        {
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath);
            values["detectionMode"] = "SourceUsage";
            values["dataAccessTechnology"] = "LinqToSql";
            values["projectContext"] = projectContext;
            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Creates metadata for source usage relationships.
        /// </summary>
        private static GraphMetadata CreateUsageRelationshipMetadata(string relationshipKind, string relativePath, string projectContext, string? readWriteHint, string? commandApi)
        {
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath);
            values["detectionMode"] = "SourceUsage";
            values["dataAccessTechnology"] = "LinqToSql";
            values["dataAccessRelationshipKind"] = relationshipKind;
            values["projectContext"] = projectContext;
            values["readWriteHint"] = readWriteHint;
            values["commandApi"] = commandApi;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates metadata for unresolved source usage targets.
        /// </summary>
        private static GraphMetadata CreateUnknownUsageMetadata(string relativePath, string projectContext, string unresolvedTarget, string usageApi)
        {
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath);
            values["detectionMode"] = "SourceUsage";
            values["dataAccessTechnology"] = "LinqToSql";
            values["projectContext"] = projectContext;
            values["unresolvedTarget"] = unresolvedTarget;
            values["usageApi"] = usageApi;
            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Creates metadata for raw SQL command nodes.
        /// </summary>
        private static GraphMetadata CreateRawSqlMetadata(string relativePath, string projectContext, string commandApi, string? sqlPreview)
        {
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath);
            values["detectionMode"] = "SourceUsage";
            values["dataAccessTechnology"] = "LinqToSql";
            values["projectContext"] = projectContext;
            values["commandApi"] = commandApi;
            values["sqlPreview"] = sqlPreview;
            values["sqlPreviewHash"] = string.IsNullOrWhiteSpace(sqlPreview) ? null : HashStablePayload(sqlPreview);
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates metadata for unknown or computed raw SQL usage.
        /// </summary>
        private static GraphMetadata CreateComputedRawSqlRelationshipMetadata(string relativePath, string projectContext, string commandApi)
        {
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath);
            values["detectionMode"] = "SourceUsage";
            values["dataAccessTechnology"] = "LinqToSql";
            values["dataAccessRelationshipKind"] = "RawSqlExecution";
            values["projectContext"] = projectContext;
            values["commandApi"] = commandApi;
            values["unknownReason"] = "ComputedSql";
            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Creates deterministic metadata for a WP009 final cross-slice correlation edge.
        /// </summary>
        /// <param name="correlationKind">The specific correlation rule that produced the edge.</param>
        /// <param name="targetType">The optional CLR type or method identity that was matched.</param>
        /// <param name="technology">The optional data-access technology associated with the target fact.</param>
        /// <param name="relativePath">The optional repository-relative evidence path.</param>
        /// <param name="connectionStringKey">The optional safe connection-string key used for configuration correlation.</param>
        /// <param name="sourceMethod">The optional runtime method identity used for call correlation.</param>
        /// <param name="targetMethod">The optional data-access method identity used for call correlation.</param>
        /// <returns>Canonical graph metadata for the correlation edge.</returns>
        private static GraphMetadata CreateCorrelationMetadata(string correlationKind, string? targetType, string? technology, string? relativePath, string? connectionStringKey, string? sourceMethod, string? targetMethod)
        {
            // Correlation metadata explains why a cross-slice edge exists while avoiding raw configuration values or SQL text.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["correlationKind"] = correlationKind,
                ["dataAccessTechnology"] = technology,
                ["detectionMode"] = "CrossSliceCorrelation",
                ["extractor"] = nameof(LinqToSqlDbmlModelExtractor),
                ["modelFilePath"] = relativePath,
                ["connectionStringKey"] = connectionStringKey,
                ["targetType"] = targetType,
                ["sourceMethod"] = sourceMethod,
                ["targetMethod"] = targetMethod
            };
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates a deterministic architecture edge for final WP009 cross-slice correlation.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning extraction snapshot.</param>
        /// <param name="edgeKind">The controlled edge kind.</param>
        /// <param name="sourceStableKey">The stable key of the source node.</param>
        /// <param name="targetStableKey">The stable key of the target node.</param>
        /// <param name="primaryEvidenceStableKey">The optional evidence stable key explaining the correlation.</param>
        /// <param name="metadata">The deterministic correlation metadata.</param>
        /// <param name="confidence">The confidence assigned to the correlation.</param>
        /// <returns>An architecture edge ready for accumulation.</returns>
        private static ArchitectureEdge CreateCorrelationEdge(StableKey snapshotStableKey, EdgeKind edgeKind, StableKey sourceStableKey, StableKey targetStableKey, StableKey? primaryEvidenceStableKey, GraphMetadata metadata, Confidence confidence)
        {
            // Edge identity uses only stable endpoints and canonical metadata so repeated stage execution collapses duplicates deterministically.
            StableKey stableKey = new($"wp009-correlation-edge://{HashStablePayload(edgeKind.Value, sourceStableKey.Value, targetStableKey.Value, metadata.ToCanonicalJson())}");
            return new ArchitectureEdge(snapshotStableKey, stableKey, edgeKind, sourceStableKey, targetStableKey, true, KnowledgeKind.Fact, confidence, UnknownState.Known, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForEdge(edgeKind, sourceStableKey, targetStableKey, true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Reads a simple string metadata property from canonical graph metadata.
        /// </summary>
        /// <param name="metadata">The metadata value to inspect.</param>
        /// <param name="propertyName">The lower-camel metadata property to read.</param>
        /// <returns>The property value when it exists as a JSON string; otherwise, <see langword="null" />.</returns>
        private static string? ExtractMetadataString(GraphMetadata metadata, string propertyName)
        {
            // Metadata has no typed accessor today, so correlation reads only string properties from its canonical JSON representation.
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(metadata.ToCanonicalJson());
            return document.RootElement.TryGetProperty(propertyName, out System.Text.Json.JsonElement property) && property.ValueKind == System.Text.Json.JsonValueKind.String
                ? property.GetString()
                : null;
        }

        /// <summary>
        /// Determines whether a candidate configuration key node can satisfy a safe connection-string-key reference.
        /// </summary>
        /// <param name="node">The configuration node to inspect.</param>
        /// <param name="connectionStringKey">The safe connection-string key observed on a data-access fact.</param>
        /// <returns><see langword="true" /> when the configuration node represents that connection key; otherwise, <see langword="false" />.</returns>
        private static bool IsConfigurationNodeForConnectionKey(ArchitectureNode node, string connectionStringKey)
        {
            // WP007 modern and legacy extractors use different stable-key namespaces, so matching accepts exact keys and terminal path segments only.
            string stableKey = node.StableKey.Value;
            return stableKey.EndsWith($":{connectionStringKey}", StringComparison.Ordinal)
                || stableKey.EndsWith($"/{connectionStringKey}", StringComparison.Ordinal)
                || stableKey.EndsWith($"#{connectionStringKey}", StringComparison.Ordinal)
                || string.Equals(ExtractMetadataString(node.Metadata, "configurationKey"), connectionStringKey, StringComparison.Ordinal)
                || string.Equals(ExtractMetadataString(node.Metadata, "name"), connectionStringKey, StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets the first generic type argument name from an invocation when semantic binding or source syntax can identify it.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for symbol lookup.</param>
        /// <param name="invocation">The invocation syntax to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic lookup should stop.</param>
        /// <returns>The fully qualified type name of the first generic argument when available; otherwise, <see langword="null" />.</returns>
        private static string? TryGetFirstGenericTypeArgumentName(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
        {
            // Semantic type arguments are preferred; source syntax is a fallback for self-contained fixtures with lightweight extension stubs.
            if (semanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol methodSymbol && methodSymbol.TypeArguments.Length > 0)
            {
                return methodSymbol.TypeArguments[0].ToDisplayString();
            }

            GenericNameSyntax? genericName = invocation.Expression.DescendantNodesAndSelf().OfType<GenericNameSyntax>().LastOrDefault();
            if (genericName?.TypeArgumentList.Arguments.FirstOrDefault() is TypeSyntax typeSyntax)
            {
                return semanticDocument.SemanticModel.GetTypeInfo(typeSyntax, cancellationToken).Type?.ToDisplayString() ?? typeSyntax.ToString();
            }

            return null;
        }

        /// <summary>
        /// Creates metadata for a DBML mapping relationship.
        /// </summary>
        /// <param name="relationshipKind">The relationship subtype to store in metadata.</param>
        /// <param name="relativePath">The repository-relative DBML model file path.</param>
        /// <returns>A deterministic metadata value for the relationship.</returns>
        private static GraphMetadata CreateRelationshipMetadata(string relationshipKind, string relativePath)
        {
            // Relationship metadata refines the controlled edge kind without creating additional graph relationship kinds.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath);
            values["dataAccessRelationshipKind"] = relationshipKind;
            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Creates common metadata fields used by all DBML graph facts.
        /// </summary>
        /// <param name="relativePath">The repository-relative DBML model file path.</param>
        /// <returns>A mutable metadata dictionary populated with shared DBML extraction fields.</returns>
        private static Dictionary<string, object?> CreateBaseMetadata(string relativePath)
        {
            // Shared metadata keeps fact provenance consistent across DBML nodes, relationships, and evidence records.
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["detectionMode"] = "DbmlXmlModel",
                ["extractor"] = nameof(LinqToSqlDbmlModelExtractor),
                ["modelFilePath"] = relativePath
            };
        }

        /// <summary>
        /// Removes null metadata values before canonical metadata creation.
        /// </summary>
        /// <param name="values">The candidate metadata values.</param>
        /// <returns>Metadata values excluding null entries.</returns>
        private static IReadOnlyDictionary<string, object?> RemoveNullValues(Dictionary<string, object?> values)
        {
            // GraphMetadata accepts null, but omitting absent DBML attributes keeps metadata concise and avoids false claims about missing model fields.
            return values.Where(static pair => pair.Value is not null).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        }

        /// <summary>
        /// Parses a schema-qualified database object name using dbo as the deterministic default schema.
        /// </summary>
        /// <param name="name">The raw DBML object name.</param>
        /// <returns>A parsed database object name.</returns>
        private static ParsedDatabaseObjectName ParseDatabaseObjectName(string? name)
        {
            // DBML commonly stores dbo.Table names; unqualified names are treated as dbo to produce deterministic keys.
            if (string.IsNullOrWhiteSpace(name))
            {
                return new ParsedDatabaseObjectName("dbo", string.Empty);
            }

            string cleaned = name.Trim().Trim('[', ']');
            string[] parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length switch
            {
                1 => new ParsedDatabaseObjectName("dbo", TrimIdentifier(parts[0])),
                _ => new ParsedDatabaseObjectName(TrimIdentifier(parts[^2]), TrimIdentifier(parts[^1]))
            };
        }

        /// <summary>
        /// Removes common SQL identifier delimiters from one object-name part.
        /// </summary>
        /// <param name="identifier">The identifier part to normalize.</param>
        /// <returns>The identifier without surrounding square brackets.</returns>
        private static string TrimIdentifier(string identifier)
        {
            // Square-bracket trimming supports common DBML names such as [dbo].[Customers].
            return identifier.Trim().Trim('[', ']');
        }

        /// <summary>
        /// Normalizes a provider string into a small stable WP009 provider vocabulary where possible.
        /// </summary>
        /// <param name="provider">The provider value from DBML connection metadata.</param>
        /// <returns>A stable provider metadata value.</returns>
        private static string NormalizeProvider(string? provider)
        {
            // Provider values are metadata, not graph kinds; recognized names are collapsed for query consistency.
            if (string.IsNullOrWhiteSpace(provider))
            {
                return "Unknown";
            }

            return provider.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) ? "SqlServer" : provider.Trim();
        }

        /// <summary>
        /// Creates a scoped stable key for DBML model facts using repository-relative model identity.
        /// </summary>
        /// <param name="prefix">The stable-key prefix without the delimiter suffix.</param>
        /// <param name="relativePath">The repository-relative DBML file path.</param>
        /// <param name="identity">The DBML model identity within the file.</param>
        /// <returns>A deterministic scoped stable key.</returns>
        private static StableKey CreateScopedKey(string prefix, string relativePath, string identity)
        {
            // The current StableKeyGenerator data-access helpers are not project/model scoped, so DBML keys include the model path explicitly.
            return new StableKey($"{prefix}://{RepositoryRelativePath.Parse(relativePath).Value}#{identity}");
        }

        /// <summary>
        /// Adds a non-empty lookup key for later DBML relationship correlation.
        /// </summary>
        /// <param name="lookup">The lookup dictionary to update.</param>
        /// <param name="key">The candidate lookup key.</param>
        /// <param name="stableKey">The stable key to store.</param>
        private static void AddLookup(Dictionary<string, StableKey> lookup, string? key, StableKey stableKey)
        {
            // Duplicate lookup entries keep the first observed value because DBML table order is deterministic and duplicate aliases are unusual.
            if (!string.IsNullOrWhiteSpace(key))
            {
                lookup.TryAdd(key.Trim(), stableKey);
            }
        }

        /// <summary>
        /// Reads a DBML attribute by local name while ignoring XML namespace prefixes.
        /// </summary>
        /// <param name="element">The XML element whose attribute should be read.</param>
        /// <param name="attributeName">The local attribute name to read.</param>
        /// <returns>The trimmed attribute value when present; otherwise, <see langword="null" />.</returns>
        private static string? GetAttribute(XElement? element, string attributeName)
        {
            // DBML attributes are unqualified in normal files, but local-name matching keeps the parser tolerant of prefixes.
            string? value = element?.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, attributeName, StringComparison.Ordinal))?.Value;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Reads a named constructor or property argument from a Roslyn attribute by attribute class suffix and argument name.
        /// </summary>
        private static string? GetAttributeNamedValue(ISymbol symbol, string attributeName, string argumentName)
        {
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
        /// Gets the variable name assigned from an expression when the expression is the initializer of a local declaration.
        /// </summary>
        private static string? GetAssignedVariableName(ExpressionSyntax expression)
        {
            return expression.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variableDeclarator } ? variableDeclarator.Identifier.ValueText : null;
        }

        /// <summary>
        /// Gets the simple receiver identifier for an invocation expression.
        /// </summary>
        private static string? GetReceiverName(InvocationExpressionSyntax invocation)
        {
            return invocation.Expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Expression is IdentifierNameSyntax identifier ? identifier.Identifier.ValueText : null;
        }

        /// <summary>
        /// Gets the member name invoked by a member-access invocation expression.
        /// </summary>
        private static string? GetInvokedMemberName(InvocationExpressionSyntax invocation)
        {
            return invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => null
            };
        }

        /// <summary>
        /// Resolves a table stable key from a Table&lt;TEntity&gt; receiver type.
        /// </summary>
        private static StableKey? TryResolveTableFromTableReceiver(LinqToSqlSemanticExtractionState state, ITypeSymbol? receiverType)
        {
            string? entityName = receiverType is null ? null : TryGetTableEntityTypeName(receiverType);
            return entityName is not null && state.TableKeysByEntityTypeName.TryGetValue(entityName, out StableKey tableStableKey) ? tableStableKey : null;
        }

        /// <summary>
        /// Reads the first constant string argument from an invocation as a redacted preview.
        /// </summary>
        private static string? GetFirstArgumentLiteral(InvocationExpressionSyntax invocation, Microsoft.CodeAnalysis.SemanticModel semanticModel)
        {
            ArgumentSyntax? argument = invocation.ArgumentList.Arguments.FirstOrDefault();
            if (argument is null)
            {
                return null;
            }

            Optional<object?> constantValue = semanticModel.GetConstantValue(argument.Expression);
            if (!constantValue.HasValue || constantValue.Value is not string sql || string.IsNullOrWhiteSpace(sql))
            {
                return "[ComputedSql]";
            }

            string preview = DbmlRedactor.Redact(sql.Trim());
            return preview.Length > 160 ? preview[..160] : preview;
        }

        /// <summary>
        /// Reads a named boolean argument from a Roslyn mapping attribute.
        /// </summary>
        private static bool? GetAttributeNamedBooleanValue(ISymbol symbol, string attributeName, string argumentName)
        {
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                string? className = attribute.AttributeClass?.Name;
                if (!string.Equals(className, attributeName, StringComparison.Ordinal) && !string.Equals(className, string.Concat(attributeName, "Attribute"), StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments)
                {
                    if (string.Equals(namedArgument.Key, argumentName, StringComparison.Ordinal) && namedArgument.Value.Value is bool namedValue)
                    {
                        return namedValue;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Determines whether a type symbol represents or inherits from System.Data.Linq.DataContext.
        /// </summary>
        private static bool IsLinqToSqlDataContext(INamedTypeSymbol typeSymbol)
        {
            for (INamedTypeSymbol? current = typeSymbol; current is not null; current = current.BaseType)
            {
                if (string.Equals(current.Name, "DataContext", StringComparison.Ordinal) && string.Equals(current.ContainingNamespace.ToDisplayString(), "System.Data.Linq", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates a generated table fact from a class decorated with TableAttribute.
        /// </summary>
        private static DesignerTableFact? TryCreateDesignerTableFact(INamedTypeSymbol typeSymbol)
        {
            string? tableName = GetAttributeNamedValue(typeSymbol, "Table", "Name");
            if (string.IsNullOrWhiteSpace(tableName))
            {
                return null;
            }

            ParsedDatabaseObjectName parsedName = ParseDatabaseObjectName(tableName);
            return string.IsNullOrWhiteSpace(parsedName.ObjectName) ? null : new DesignerTableFact(parsedName.SchemaName, parsedName.ObjectName, parsedName.QualifiedName, tableName.Trim());
        }

        /// <summary>
        /// Creates a generated column fact from a property decorated with ColumnAttribute.
        /// </summary>
        private static DesignerColumnFact? TryCreateDesignerColumnFact(IPropertySymbol propertySymbol, DesignerTableFact tableFact)
        {
            string? columnName = GetAttributeNamedValue(propertySymbol, "Column", "Name");
            if (string.IsNullOrWhiteSpace(columnName))
            {
                return null;
            }

            string columnDisplayName = columnName.Trim();
            return new DesignerColumnFact(tableFact.SchemaName, tableFact.ObjectName, columnDisplayName, $"{tableFact.SchemaName}.{tableFact.ObjectName}.{columnDisplayName}", GetAttributeNamedValue(propertySymbol, "Column", "DbType"), GetAttributeNamedBooleanValue(propertySymbol, "Column", "IsPrimaryKey"), GetAttributeNamedBooleanValue(propertySymbol, "Column", "CanBeNull"));
        }

        /// <summary>
        /// Creates a generated association fact from a property decorated with AssociationAttribute.
        /// </summary>
        private static DesignerAssociationFact? TryCreateDesignerAssociationFact(IPropertySymbol propertySymbol)
        {
            string? associationName = GetAttributeNamedValue(propertySymbol, "Association", "Name");
            if (string.IsNullOrWhiteSpace(associationName))
            {
                return null;
            }

            return new DesignerAssociationFact(associationName.Trim(), propertySymbol.Name, GetAttributeNamedValue(propertySymbol, "Association", "ThisKey"), GetAttributeNamedValue(propertySymbol, "Association", "OtherKey"));
        }

        /// <summary>
        /// Gets the entity type name from a System.Data.Linq.Table&lt;TEntity&gt; type.
        /// </summary>
        private static string? TryGetTableEntityTypeName(ITypeSymbol typeSymbol)
        {
            if (typeSymbol is INamedTypeSymbol namedType && string.Equals(namedType.Name, "Table", StringComparison.Ordinal) && string.Equals(namedType.ContainingNamespace.ToDisplayString(), "System.Data.Linq", StringComparison.Ordinal) && namedType.TypeArguments.Length == 1)
            {
                return namedType.TypeArguments[0].Name;
            }

            return null;
        }

        /// <summary>
        /// Determines whether an XML element has the expected local name.
        /// </summary>
        /// <param name="element">The XML element to inspect.</param>
        /// <param name="localName">The expected local name.</param>
        /// <returns><see langword="true" /> when the element local name matches; otherwise, <see langword="false" />.</returns>
        private static bool IsNamed(XElement element, string localName)
        {
            // DBML files may carry a default namespace, so element checks must use local names rather than full names.
            return string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets XML line information for an element when available.
        /// </summary>
        /// <param name="element">The XML element to locate.</param>
        /// <returns>A DBML location with one-based line and column numbers.</returns>
        private static DbmlLocation GetLocation(XElement element)
        {
            // XDocument LoadOptions.SetLineInfo provides artifact locations without a separate XML parser.
            IXmlLineInfo lineInfo = element;
            int lineNumber = lineInfo.HasLineInfo() && lineInfo.LineNumber > 0 ? lineInfo.LineNumber : 1;
            int linePosition = lineInfo.HasLineInfo() && lineInfo.LinePosition > 0 ? lineInfo.LinePosition : 1;
            return new DbmlLocation(lineNumber, linePosition);
        }

        /// <summary>
        /// Creates a compact redacted XML preview for one DBML element.
        /// </summary>
        /// <param name="element">The DBML element to preview.</param>
        /// <param name="redactedContent">The redacted DBML content used as a fallback source.</param>
        /// <returns>A redacted snippet preview.</returns>
        private static string CreatePreview(XElement element, string redactedContent)
        {
            // Element text is redacted and compacted; the fallback ensures an empty element still receives useful evidence.
            string preview = DbmlRedactor.Redact(element.ToString(SaveOptions.DisableFormatting));
            if (string.IsNullOrWhiteSpace(preview))
            {
                preview = redactedContent.Length > 240 ? redactedContent[..240] : redactedContent;
            }

            return preview.Length > 240 ? preview[..240] : preview;
        }

        /// <summary>
        /// Creates a source-backed evidence record for generated designer or usage code.
        /// </summary>
        private static EvidenceRecord CreateSourceEvidence(StableKey snapshotStableKey, string repositoryRootDirectory, string documentPath, string sourceText, SyntaxNode syntaxNode, EvidenceKind evidenceKind, string role, string? symbolName, string? containingSymbol, Confidence confidence, UnknownState unknownState)
        {
            FileLinePositionSpan lineSpan = syntaxNode.SyntaxTree.GetLineSpan(syntaxNode.Span);
            int startLine = lineSpan.StartLinePosition.Line + 1;
            int endLine = lineSpan.EndLinePosition.Line + 1;
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRootDirectory, documentPath);
            string preview = DbmlRedactor.Redact(syntaxNode.ToString());
            if (preview.Length > 240)
            {
                preview = preview[..240];
            }

            string snippetHash = HashStablePayload(preview);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = evidenceKind == EvidenceKind.DesignerGeneratedCode ? "DesignerGeneratedCode" : "SourceUsage",
                ["evidenceRole"] = role,
                ["extractor"] = nameof(LinqToSqlDbmlModelExtractor),
                ["sourceLine"] = startLine,
                ["sourceEndLine"] = endLine
            });
            StableKey stableKey = new($"source-evidence://{HashStablePayload(relativePath, evidenceKind.Value, role, symbolName, containingSymbol, startLine.ToString(), endLine.ToString(), snippetHash)}");
            return new EvidenceRecord(snapshotStableKey, stableKey, evidenceKind, RepositoryRelativePath.Parse(relativePath), startLine, endLine, symbolName, containingSymbol, snippetHash, preview, KnowledgeKind.Fact, confidence, unknownState, metadata, FingerprintGenerator.ForEvidence(evidenceKind, relativePath, startLine, endLine, symbolName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Returns the first non-empty value from a candidate sequence.
        /// </summary>
        /// <param name="values">The candidate values in priority order.</param>
        /// <returns>The first non-empty trimmed value, or an empty string when no candidates are present.</returns>
        private static string FirstNonEmpty(params string?[] values)
        {
            // DBML often offers parallel names such as Name, Member, and Method; this helper keeps fallback ordering explicit.
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Hashes stable payload parts with SHA-256.
        /// </summary>
        /// <param name="parts">The logical values that form the stable payload.</param>
        /// <returns>A lowercase hexadecimal SHA-256 hash.</returns>
        private static string HashStablePayload(params string?[] parts)
        {
            // Length-prefixing keeps stable keys deterministic even when values contain separators.
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
        /// <param name="SchemaName">The schema name, using dbo when the DBML name is unqualified.</param>
        /// <param name="ObjectName">The table, function, or procedure name.</param>
        private readonly record struct ParsedDatabaseObjectName(string SchemaName, string ObjectName)
        {
            /// <summary>
            /// Gets the schema-qualified database object name.
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
        /// Represents DBML XML artifact location information.
        /// </summary>
        /// <param name="LineNumber">The one-based XML line number.</param>
        /// <param name="LinePosition">The one-based XML column position.</param>
        private readonly record struct DbmlLocation(int LineNumber, int LinePosition);

        /// <summary>
        /// Represents generated designer table metadata.
        /// </summary>
        private sealed record DesignerTableFact(string SchemaName, string ObjectName, string QualifiedName, string TableName);

        /// <summary>
        /// Represents generated designer column metadata.
        /// </summary>
        private sealed record DesignerColumnFact(string SchemaName, string TableName, string ColumnName, string QualifiedName, string? DbType, bool? IsPrimaryKey, bool? CanBeNull);

        /// <summary>
        /// Represents generated designer association metadata.
        /// </summary>
        private sealed record DesignerAssociationFact(string AssociationName, string MemberName, string? ThisKey, string? OtherKey);

        /// <summary>
        /// Tracks source method identity and local variables observed while classifying LINQ to SQL usage.
        /// </summary>
        private sealed class MethodUsageState
        {
            /// <summary>
            /// Initializes method-local usage state.
            /// </summary>
            private MethodUsageState(StableKey methodStableKey)
            {
                MethodStableKey = methodStableKey;
            }

            /// <summary>
            /// Gets the stable key of the source method node.
            /// </summary>
            public StableKey MethodStableKey { get; }

            /// <summary>
            /// Gets DataContext stable keys by local variable name.
            /// </summary>
            public Dictionary<string, StableKey> ContextKeysByVariable { get; } = new Dictionary<string, StableKey>(StringComparer.Ordinal);

            /// <summary>
            /// Gets table stable keys by local variable name.
            /// </summary>
            public Dictionary<string, StableKey> TableKeysByVariable { get; } = new Dictionary<string, StableKey>(StringComparer.Ordinal);

            /// <summary>
            /// Creates method usage state from a Roslyn method symbol.
            /// </summary>
            public static MethodUsageState FromMethod(IMethodSymbol methodSymbol, StableKey snapshotStableKey, string relativePath, string projectContext)
            {
                return new MethodUsageState(new StableKey($"method://{HashStablePayload(snapshotStableKey.Value, projectContext, relativePath, methodSymbol.ToDisplayString())}"));
            }
        }

        /// <summary>
        /// Tracks DBML model identities so generated source facts can refine rather than duplicate graph nodes.
        /// </summary>
        private sealed class LinqToSqlSemanticExtractionState
        {
            /// <summary>
            /// Initializes an empty semantic extraction state.
            /// </summary>
            private LinqToSqlSemanticExtractionState()
            {
                // Dictionaries are ordinal because stable keys and symbol names are case-sensitive graph identity inputs.
            }

            /// <summary>
            /// Gets DataContext stable keys by context type name.
            /// </summary>
            public Dictionary<string, StableKey> ContextKeysByTypeName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets entity stable keys by entity type name.
            /// </summary>
            public Dictionary<string, StableKey> EntityKeysByTypeName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets table stable keys by schema-qualified table name.
            /// </summary>
            public Dictionary<string, StableKey> TableKeysByQualifiedName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets stored procedure stable keys by schema-qualified procedure name.
            /// </summary>
            public Dictionary<string, StableKey> StoredProcedureKeysByQualifiedName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets stored procedure stable keys by generated wrapper method name.
            /// </summary>
            public Dictionary<string, StableKey> StoredProcedureKeysByMethodName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets table stable keys by generated entity type name.
            /// </summary>
            public Dictionary<string, StableKey> TableKeysByEntityTypeName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets table stable keys by generated DataContext property name.
            /// </summary>
            public Dictionary<string, StableKey> TableKeysByPropertyName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Creates semantic extraction state from DBML graph facts that have already been accumulated.
            /// </summary>
            public static LinqToSqlSemanticExtractionState FromSnapshot(Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot snapshot)
            {
                LinqToSqlSemanticExtractionState state = new();
                foreach (ArchitectureNode node in snapshot.Nodes)
                {
                    switch (node.NodeKind.Value)
                    {
                        case "LinqToSqlDataContext":
                            state.ContextKeysByTypeName.TryAdd(node.DisplayName, node.StableKey);
                            break;
                        case "Entity":
                            state.EntityKeysByTypeName.TryAdd(node.DisplayName, node.StableKey);
                            break;
                        case "DatabaseTable":
                            if (!string.IsNullOrWhiteSpace(node.QualifiedName))
                            {
                                state.TableKeysByQualifiedName.TryAdd(node.QualifiedName, node.StableKey);
                            }
                            break;
                        case "StoredProcedure":
                            if (!string.IsNullOrWhiteSpace(node.QualifiedName))
                            {
                                state.StoredProcedureKeysByQualifiedName.TryAdd(node.QualifiedName, node.StableKey);
                            }
                            break;
                    }
                }

                return state;
            }

            /// <summary>
            /// Gets the repository-relative model path embedded in a known context stable key or falls back to the generated source path.
            /// </summary>
            public string ModelPathForContext(string contextName, string fallbackPath)
            {
                return ContextKeysByTypeName.TryGetValue(contextName, out StableKey stableKey) ? ExtractModelPath(stableKey, fallbackPath) : fallbackPath;
            }

            /// <summary>
            /// Gets the repository-relative model path embedded in a known entity stable key or falls back to the generated source path.
            /// </summary>
            public string ModelPathForEntity(string entityName, string fallbackPath)
            {
                return EntityKeysByTypeName.TryGetValue(entityName, out StableKey stableKey) ? ExtractModelPath(stableKey, fallbackPath) : fallbackPath;
            }

            /// <summary>
            /// Extracts the DBML model path portion from scoped stable keys like prefix://path#identity.
            /// </summary>
            private static string ExtractModelPath(StableKey stableKey, string fallbackPath)
            {
                string value = stableKey.Value;
                int schemeIndex = value.IndexOf("://", StringComparison.Ordinal);
                int identityIndex = value.IndexOf('#', StringComparison.Ordinal);
                if (schemeIndex < 0 || identityIndex <= schemeIndex + 3)
                {
                    return fallbackPath;
                }

                return value[(schemeIndex + 3)..identityIndex];
            }
        }

        /// <summary>
        /// Holds stable lookup tables used by final WP009 cross-slice correlation after all extractor slices have contributed graph facts.
        /// </summary>
        private sealed class CrossSliceCorrelationState
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="CrossSliceCorrelationState" /> class.
            /// </summary>
            /// <param name="nodesByStableKey">Architecture nodes keyed by stable key.</param>
            /// <param name="configurationNodes">Configuration-key nodes emitted by previous stages.</param>
            /// <param name="dataAccessNodes">Data-access nodes emitted by WP009 slices.</param>
            /// <param name="contextNodes">Data-access context nodes emitted by WP009 slices.</param>
            /// <param name="registeredImplementationTypeKeys">Type stable keys that appear as DI registration implementation sources.</param>
            /// <param name="dataAccessMethodNodesByQualifiedName">Method nodes with data-access relationships keyed by qualified method identity.</param>
            /// <param name="runtimeMethodNodesByQualifiedName">Runtime method nodes keyed by qualified method identity.</param>
            private CrossSliceCorrelationState(
                IReadOnlyDictionary<string, ArchitectureNode> nodesByStableKey,
                IReadOnlyList<ArchitectureNode> configurationNodes,
                IReadOnlyList<ArchitectureNode> dataAccessNodes,
                IReadOnlyList<ArchitectureNode> contextNodes,
                IReadOnlySet<string> registeredImplementationTypeKeys,
                IReadOnlyDictionary<string, IReadOnlyList<ArchitectureNode>> dataAccessMethodNodesByQualifiedName,
                IReadOnlyDictionary<string, IReadOnlyList<ArchitectureNode>> runtimeMethodNodesByQualifiedName)
            {
                // The state captures a point-in-time snapshot so correlation loops do not repeatedly parse and filter the accumulator.
                NodesByStableKey = nodesByStableKey;
                ConfigurationNodes = configurationNodes;
                DataAccessNodes = dataAccessNodes;
                ContextNodes = contextNodes;
                RegisteredImplementationTypeKeys = registeredImplementationTypeKeys;
                DataAccessMethodNodesByQualifiedName = dataAccessMethodNodesByQualifiedName;
                RuntimeMethodNodesByQualifiedName = runtimeMethodNodesByQualifiedName;
            }

            /// <summary>
            /// Gets architecture nodes keyed by stable key.
            /// </summary>
            public IReadOnlyDictionary<string, ArchitectureNode> NodesByStableKey { get; }

            /// <summary>
            /// Gets configuration-key nodes emitted by previous stages.
            /// </summary>
            public IReadOnlyList<ArchitectureNode> ConfigurationNodes { get; }

            /// <summary>
            /// Gets data-access nodes emitted by WP009 slices.
            /// </summary>
            public IReadOnlyList<ArchitectureNode> DataAccessNodes { get; }

            /// <summary>
            /// Gets EF and LINQ to SQL context nodes emitted by WP009 slices.
            /// </summary>
            public IReadOnlyList<ArchitectureNode> ContextNodes { get; }

            /// <summary>
            /// Gets type stable keys that appear as DI registration implementation sources.
            /// </summary>
            public IReadOnlySet<string> RegisteredImplementationTypeKeys { get; }

            /// <summary>
            /// Gets method nodes that have downstream data-access relationships, keyed by qualified method identity.
            /// </summary>
            public IReadOnlyDictionary<string, IReadOnlyList<ArchitectureNode>> DataAccessMethodNodesByQualifiedName { get; }

            /// <summary>
            /// Gets runtime method nodes emitted by WP008, keyed by qualified method identity.
            /// </summary>
            public IReadOnlyDictionary<string, IReadOnlyList<ArchitectureNode>> RuntimeMethodNodesByQualifiedName { get; }

            /// <summary>
            /// Creates cross-slice lookup state from an accumulated snapshot.
            /// </summary>
            /// <param name="snapshot">The current accumulated extraction snapshot.</param>
            /// <returns>Lookup state ready for deterministic correlation.</returns>
            public static CrossSliceCorrelationState FromSnapshot(Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot snapshot)
            {
                // Stable-key maps let correlation distinguish graph identities even when display names repeat across slices.
                Dictionary<string, ArchitectureNode> nodesByStableKey = snapshot.Nodes.ToDictionary(node => node.StableKey.Value, node => node, StringComparer.Ordinal);
                ArchitectureNode[] configurationNodes = snapshot.Nodes.Where(static node => node.NodeKind == NodeKind.ConfigurationKey).ToArray();
                ArchitectureNode[] dataAccessNodes = snapshot.Nodes.Where(static node => s_dataAccessNodeKinds.Contains(node.NodeKind.Value)).ToArray();
                ArchitectureNode[] contextNodes = dataAccessNodes.Where(static node => node.NodeKind == NodeKind.DbContext || node.NodeKind == NodeKind.LinqToSqlDataContext).ToArray();
                HashSet<string> registeredImplementationTypeKeys = snapshot.Edges
                    .Where(static edge => edge.EdgeKind == EdgeKind.RegisteredAsService)
                    .Select(static edge => edge.SourceNodeStableKey.Value)
                    .Where(static stableKey => stableKey.StartsWith("type://", StringComparison.Ordinal))
                    .ToHashSet(StringComparer.Ordinal);
                HashSet<string> dataAccessMethodStableKeys = snapshot.Edges
                    .Where(static edge => s_dataAccessUsageEdgeKinds.Contains(edge.EdgeKind.Value))
                    .Select(static edge => edge.SourceNodeStableKey.Value)
                    .ToHashSet(StringComparer.Ordinal);
                Dictionary<string, IReadOnlyList<ArchitectureNode>> dataAccessMethodNodesByQualifiedName = GroupMethodNodesByQualifiedName(nodesByStableKey.Values.Where(node => node.NodeKind == NodeKind.Method && dataAccessMethodStableKeys.Contains(node.StableKey.Value)));
                Dictionary<string, IReadOnlyList<ArchitectureNode>> runtimeMethodNodesByQualifiedName = GroupMethodNodesByQualifiedName(nodesByStableKey.Values.Where(static node => node.NodeKind == NodeKind.Method && IsRuntimeMethodNode(node)));
                return new CrossSliceCorrelationState(nodesByStableKey, configurationNodes, dataAccessNodes, contextNodes, registeredImplementationTypeKeys, dataAccessMethodNodesByQualifiedName, runtimeMethodNodesByQualifiedName);
            }

            /// <summary>
            /// Finds configuration nodes that match a safe connection-string key.
            /// </summary>
            /// <param name="connectionStringKey">The safe connection-string key from a data-access fact.</param>
            /// <returns>Matching configuration-key nodes.</returns>
            public IReadOnlyList<ArchitectureNode> FindConfigurationNodes(string connectionStringKey)
            {
                // The matching rule intentionally ignores values and only compares safe logical key names.
                return ConfigurationNodes.Where(node => IsConfigurationNodeForConnectionKey(node, connectionStringKey)).ToArray();
            }

            /// <summary>
            /// Groups method nodes by qualified method identity.
            /// </summary>
            /// <param name="methodNodes">The method nodes to group.</param>
            /// <returns>A dictionary from qualified method identity to one or more graph nodes.</returns>
            private static Dictionary<string, IReadOnlyList<ArchitectureNode>> GroupMethodNodesByQualifiedName(IEnumerable<ArchitectureNode> methodNodes)
            {
                // Different stages can produce distinct stable keys for the same method identity; grouping keeps correlation explicit rather than replacing either fact.
                return methodNodes
                    .Where(static node => !string.IsNullOrWhiteSpace(node.QualifiedName))
                    .GroupBy(static node => node.QualifiedName!, StringComparer.Ordinal)
                    .ToDictionary(static group => group.Key, static group => (IReadOnlyList<ArchitectureNode>)group.ToArray(), StringComparer.Ordinal);
            }

            /// <summary>
            /// Determines whether a method node belongs to a runtime slice rather than a data-access-only slice.
            /// </summary>
            /// <param name="node">The method node to inspect.</param>
            /// <returns><see langword="true" /> when the node metadata contains a runtime-kind marker; otherwise, <see langword="false" />.</returns>
            private static bool IsRuntimeMethodNode(ArchitectureNode node)
            {
                // WP008 runtime method nodes carry runtimeKind metadata, which avoids hard-coding individual runtime stable-key formats.
                return node.Metadata.ToCanonicalJson().Contains("\"runtimeKind\"", StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Represents secret-safe connection metadata extracted from a DBML Connection element.
        /// </summary>
        /// <param name="SettingsPropertyName">The app-settings property name that stores the connection string when present.</param>
        /// <param name="Provider">The provider identifier declared by DBML when present.</param>
        /// <param name="Mode">The DBML connection mode when present.</param>
        /// <param name="HadSecretLikeConnectionString">A value indicating whether a raw connection string was present and intentionally omitted.</param>
        private sealed record DbmlConnectionFact(string? SettingsPropertyName, string? Provider, string? Mode, bool HadSecretLikeConnectionString);

        /// <summary>
        /// Provides deterministic redaction for DBML snippets, metadata candidates, and diagnostics.
        /// </summary>
        private static class DbmlRedactor
        {
            /// <summary>
            /// Redacts secret-like values from a text payload before it can enter graph output.
            /// </summary>
            /// <param name="value">The text value to redact.</param>
            /// <returns>The redacted value.</returns>
            public static string Redact(string value)
            {
                // This conservative redactor handles fixture secrets and common inline connection-string credential assignments.
                return value
                    .Replace("SuperSecret", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("Password=SuperSecret", "Password=[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("Password=[REDACTED]", "Credential=[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("User Id=sa", "User Id=[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("User ID=sa", "User ID=[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("User Id=[REDACTED]", "User Id=[REDACTED_USER]", StringComparison.OrdinalIgnoreCase)
                    .Replace("User ID=[REDACTED]", "User ID=[REDACTED_USER]", StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
