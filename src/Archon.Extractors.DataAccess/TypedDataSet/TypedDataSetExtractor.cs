using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Extractors.DataAccess.LinqToSql;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Archon.Extractors.DataAccess.TypedDataSet
{
    /// <summary>
    /// Extracts typed DataSet XSD, generated source, TableAdapter, query, stored procedure, and usage facts without executing target code or connecting to databases.
    /// </summary>
    public sealed class TypedDataSetExtractor
    {
        /// <summary>
        /// Adds typed DataSet graph facts to the shared data-access extraction accumulator.
        /// </summary>
        /// <param name="request">The repository-scoped data-access request that provides artifact and semantic source context.</param>
        /// <param name="accumulator">The shared architecture snapshot accumulator that receives graph contributions.</param>
        /// <param name="cancellationToken">A token that signals when file and semantic traversal should stop.</param>
        public void Accumulate(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, CancellationToken cancellationToken = default)
        {
            // Typed DataSet extraction is deliberately part of the existing WP009 entry path so callers receive a single accumulated data-access snapshot.
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(accumulator);

            TypedDataSetExtractionState state = new();
            foreach (string xsdFilePath in DiscoverXsdFiles(request.RepositoryRootDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParseXsdFile(request, accumulator, state, xsdFilePath, cancellationToken);
            }

            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateGeneratedSource(request, accumulator, state, semanticDocument, cancellationToken);
            }

            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateUsage(request, accumulator, state, semanticDocument, cancellationToken);
            }
        }

        /// <summary>
        /// Discovers typed DataSet XSD files below the repository root using deterministic ordering.
        /// </summary>
        /// <param name="repositoryRootDirectory">The repository root to search.</param>
        /// <returns>Repository-contained XSD file paths ordered deterministically.</returns>
        private static IReadOnlyList<string> DiscoverXsdFiles(string repositoryRootDirectory)
        {
            // XSD discovery intentionally excludes obj artifacts so generated build output does not duplicate checked-in model facts.
            if (!Directory.Exists(repositoryRootDirectory))
            {
                return [];
            }

            return Directory.EnumerateFiles(repositoryRootDirectory, "*.xsd", SearchOption.AllDirectories)
                .Where(path => !path.Contains(string.Concat(Path.DirectorySeparatorChar, "obj", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Parses a single typed DataSet XSD file and emits graph facts, warnings, and unknowns.
        /// </summary>
        /// <param name="request">The extraction request that scopes stable keys and repository paths.</param>
        /// <param name="accumulator">The shared accumulator receiving graph facts.</param>
        /// <param name="state">The typed DataSet state used for generated-source and usage correlation.</param>
        /// <param name="xsdFilePath">The absolute XSD file path to parse.</param>
        /// <param name="cancellationToken">A token that signals when XML traversal should stop.</param>
        private static void ParseXsdFile(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, TypedDataSetExtractionState state, string xsdFilePath, CancellationToken cancellationToken)
        {
            // Malformed XML degrades to a warning so one broken typed DataSet artifact cannot block the rest of extraction.
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(request.RepositoryRootDirectory, xsdFilePath);
            string content = File.ReadAllText(xsdFilePath);
            string redactedContent = Redact(content);
            try
            {
                XDocument document = XDocument.Parse(content, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
                XElement? dataSetElement = document.Descendants().FirstOrDefault(IsTypedDataSetElement);
                if (dataSetElement is null)
                {
                    return;
                }

                AccumulateTypedDataSetModel(request, accumulator, state, relativePath, redactedContent, document, dataSetElement, cancellationToken);
            }
            catch (XmlException exception)
            {
                accumulator.AddWarning($"Malformed typed DataSet XSD {relativePath}: {Redact(exception.Message)}");
            }
        }

        /// <summary>
        /// Emits graph facts for a parsed typed DataSet XSD model.
        /// </summary>
        /// <param name="request">The extraction request that scopes stable keys and snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph facts.</param>
        /// <param name="state">The typed DataSet state used for later source correlation.</param>
        /// <param name="relativePath">The repository-relative XSD path.</param>
        /// <param name="redactedContent">The redacted XSD content used for evidence previews and hashes.</param>
        /// <param name="document">The parsed XSD document.</param>
        /// <param name="dataSetElement">The XSD element that declares the typed DataSet.</param>
        /// <param name="cancellationToken">A token that signals when traversal should stop.</param>
        private static void AccumulateTypedDataSetModel(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, TypedDataSetExtractionState state, string relativePath, string redactedContent, XDocument document, XElement dataSetElement, CancellationToken cancellationToken)
        {
            // The typed DataSet itself is represented as an Entity node because WP009 uses subtyped metadata values rather than introducing additional graph node kinds.
            string dataSetName = FirstNonEmpty(GetAttribute(dataSetElement, "DataSetName"), GetAttribute(dataSetElement, "name"), Path.GetFileNameWithoutExtension(relativePath));
            bool hasPartialUnknown = false;
            EvidenceRecord dataSetEvidence = CreateXmlEvidence(request.SnapshotStableKey, relativePath, dataSetElement, redactedContent, "TypedDataSet", dataSetName, Confidence.High, UnknownState.Known);
            StableKey dataSetKey = CreateScopedKey("entity", relativePath, dataSetName);
            TypedDataSetModel model = new(dataSetName, relativePath, dataSetKey);

            foreach (XElement tableElement in dataSetElement.Descendants().Where(IsTableElement))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryAccumulateTable(request, accumulator, model, relativePath, redactedContent, tableElement, out TypedDataSetTableFact? tableFact) && tableFact is not null)
                {
                    model.TablesByTableName[tableFact.TableName] = tableFact;
                    model.TablesByClassName[tableFact.DataTableClassName] = tableFact;
                }
                else
                {
                    hasPartialUnknown = true;
                }
            }

            foreach (XElement adapterElement in document.Descendants().Where(IsTableAdapterElement))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateTableAdapter(request, accumulator, model, relativePath, redactedContent, adapterElement);
            }

            UnknownState dataSetUnknownState = hasPartialUnknown ? UnknownState.Unknown("PartialTypedDataSetModel") : UnknownState.Known;
            Confidence dataSetConfidence = hasPartialUnknown ? Confidence.Medium : Confidence.High;
            ArchitectureNode dataSetNode = CreateNode(request.SnapshotStableKey, dataSetKey, NodeKind.Entity, dataSetName, dataSetName, "XSD", null, dataSetEvidence.StableKey, dataSetConfidence, dataSetUnknownState, CreateDataSetMetadata(relativePath, dataSetName, hasPartialUnknown));
            accumulator.AddEvidence(dataSetEvidence).AddNode(dataSetNode);
            if (hasPartialUnknown)
            {
                accumulator.AddWarning($"Typed DataSet XSD {relativePath} contains partial table metadata and was recorded with explicit unknown data.");
            }

            state.AddModel(model);
        }

        /// <summary>
        /// Emits table, DataTable entity, column, and mapping facts for one typed DataSet table declaration.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph facts.</param>
        /// <param name="model">The current typed DataSet model state.</param>
        /// <param name="relativePath">The repository-relative XSD path.</param>
        /// <param name="redactedContent">The redacted XSD content used for evidence previews and hashes.</param>
        /// <param name="tableElement">The XSD element that declares a typed DataTable.</param>
        /// <param name="tableFact">The table fact emitted when deterministic table identity exists.</param>
        /// <returns><see langword="true" /> when a deterministic table fact was emitted; otherwise, <see langword="false" />.</returns>
        private static bool TryAccumulateTable(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, TypedDataSetModel model, string relativePath, string redactedContent, XElement tableElement, out TypedDataSetTableFact? tableFact)
        {
            // Typed DataSet tables usually carry an element name plus optional generator metadata that points to the database table and generated DataTable class.
            string? logicalName = GetAttribute(tableElement, "name");
            string? databaseTableName = FirstNonEmptyOrNull(GetAttribute(tableElement, "DbTableName"), logicalName);
            if (string.IsNullOrWhiteSpace(logicalName) || tableElement.Elements().All(element => !IsNamed(element, "complexType")) || string.IsNullOrWhiteSpace(databaseTableName))
            {
                tableFact = null;
                return false;
            }

            ParsedDatabaseObjectName parsedTableName = ParseDatabaseObjectName(databaseTableName);
            string dataTableClassName = FirstNonEmpty(GetAttribute(tableElement, "Generator_TableClassName"), $"{logicalName}DataTable");
            EvidenceRecord evidence = CreateXmlEvidence(request.SnapshotStableKey, relativePath, tableElement, redactedContent, "TypedDataTable", logicalName, Confidence.High, UnknownState.Known);
            StableKey dataTableKey = CreateScopedKey("entity", relativePath, $"{model.DataSetName}.{dataTableClassName}");
            StableKey tableKey = CreateScopedKey("dbtable", relativePath, parsedTableName.QualifiedName);

            ArchitectureNode dataTableNode = CreateNode(request.SnapshotStableKey, dataTableKey, NodeKind.Entity, dataTableClassName, $"{model.DataSetName}.{dataTableClassName}", "XSD", model.DataSetStableKey, evidence.StableKey, Confidence.High, UnknownState.Known, CreateDataTableMetadata(relativePath, model.DataSetName, logicalName, dataTableClassName, parsedTableName));
            ArchitectureNode tableNode = CreateNode(request.SnapshotStableKey, tableKey, NodeKind.DatabaseTable, parsedTableName.ObjectName, parsedTableName.QualifiedName, "Database", null, evidence.StableKey, Confidence.High, UnknownState.Known, CreateTableMetadata(relativePath, model.DataSetName, null, parsedTableName));
            accumulator.AddEvidence(evidence).AddNode(dataTableNode).AddNode(tableNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsEntity, model.DataSetStableKey, dataTableKey, evidence.StableKey, CreateRelationshipMetadata("TypedDataSetTable", relativePath, model.DataSetName, null, null, null, null), Confidence.High, UnknownState.Known));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsTable, dataTableKey, tableKey, evidence.StableKey, CreateRelationshipMetadata("TypedDataTableDatabaseTable", relativePath, model.DataSetName, null, null, "ReadWrite", null), Confidence.High, UnknownState.Known));

            foreach (XElement columnElement in tableElement.Descendants().Where(IsColumnElement))
            {
                AccumulateColumn(request, accumulator, model, relativePath, redactedContent, tableKey, dataTableKey, parsedTableName, columnElement);
            }

            tableFact = new TypedDataSetTableFact(logicalName, dataTableClassName, parsedTableName, tableKey, dataTableKey);
            return true;
        }

        /// <summary>
        /// Emits database-column facts for one typed DataSet table column.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph facts.</param>
        /// <param name="model">The current typed DataSet model state.</param>
        /// <param name="relativePath">The repository-relative XSD path.</param>
        /// <param name="redactedContent">The redacted XSD content used for evidence previews and hashes.</param>
        /// <param name="tableKey">The database table stable key.</param>
        /// <param name="dataTableKey">The DataTable entity stable key.</param>
        /// <param name="tableName">The parsed database table name.</param>
        /// <param name="columnElement">The XSD element that declares a column.</param>
        private static void AccumulateColumn(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, TypedDataSetModel model, string relativePath, string redactedContent, StableKey tableKey, StableKey dataTableKey, ParsedDatabaseObjectName tableName, XElement columnElement)
        {
            // Column extraction preserves the typed DataSet property name and the database-column identity when XSD metadata is deterministic.
            string? columnName = GetAttribute(columnElement, "name");
            if (string.IsNullOrWhiteSpace(columnName))
            {
                return;
            }

            EvidenceRecord evidence = CreateXmlEvidence(request.SnapshotStableKey, relativePath, columnElement, redactedContent, "TypedDataSetColumn", columnName, Confidence.High, UnknownState.Known);
            StableKey columnKey = CreateScopedKey("dbcolumn", relativePath, $"{tableName.QualifiedName}.{columnName}");
            ArchitectureNode columnNode = CreateNode(request.SnapshotStableKey, columnKey, NodeKind.DatabaseColumn, columnName, $"{tableName.QualifiedName}.{columnName}", "Database", tableKey, evidence.StableKey, Confidence.High, UnknownState.Known, CreateColumnMetadata(relativePath, model.DataSetName, tableName, columnName));
            accumulator.AddEvidence(evidence).AddNode(columnNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsColumn, tableKey, columnKey, evidence.StableKey, CreateRelationshipMetadata("TypedDataTableColumn", relativePath, model.DataSetName, null, null, null, null), Confidence.High, UnknownState.Known));
            accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.MapsColumn, dataTableKey, columnKey, evidence.StableKey, CreateRelationshipMetadata("TypedDataTableColumnProperty", relativePath, model.DataSetName, null, null, null, null), Confidence.High, UnknownState.Known));
        }

        /// <summary>
        /// Emits TableAdapter query, stored-procedure, raw SQL, and table relationship facts.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph facts.</param>
        /// <param name="model">The typed DataSet model being populated.</param>
        /// <param name="relativePath">The repository-relative XSD path.</param>
        /// <param name="redactedContent">The redacted XSD content used for evidence previews and hashes.</param>
        /// <param name="adapterElement">The XML element declaring a TableAdapter.</param>
        private static void AccumulateTableAdapter(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, TypedDataSetModel model, string relativePath, string redactedContent, XElement adapterElement)
        {
            // TableAdapter metadata ties model tables to generated adapter methods and the command definitions that those methods wrap.
            string adapterName = FirstNonEmpty(GetAttribute(adapterElement, "Name"), GetAttribute(adapterElement, "GeneratorDataComponentClassName"), "UnknownTableAdapter");
            string? logicalTableName = FirstNonEmptyOrNull(GetAttribute(adapterElement, "DataTableName"), InferTableNameFromAdapter(adapterName));
            TypedDataSetTableFact? tableFact = logicalTableName is null ? null : model.FindTable(logicalTableName);
            Dictionary<string, TypedDataSetQueryFact> queryFacts = new(StringComparer.Ordinal);

            foreach (XElement sourceElement in adapterElement.Descendants().Where(IsDbSourceElement))
            {
                string queryName = FirstNonEmpty(GetAttribute(sourceElement, "Name"), IsMainSource(sourceElement) ? "GetData" : "UnknownQuery");
                string commandType = FirstNonEmpty(GetAttribute(sourceElement, "CommandType"), "Text");
                string? commandText = FirstNonEmptyOrNull(GetAttribute(sourceElement, "CommandText"), GetAttribute(sourceElement, "Command"));
                SqlTextFact sqlText = CreateSqlTextFact(commandText);
                SqlAnalysisResult analysis = AnalyzeSql(sqlText, commandType, queryName);
                UnknownState unknownState = analysis.UnknownReason is null ? UnknownState.Known : UnknownState.Unknown(analysis.UnknownReason);
                Confidence confidence = analysis.UnknownReason is null ? Confidence.High : Confidence.Low;
                EvidenceRecord evidence = CreateXmlEvidence(request.SnapshotStableKey, relativePath, sourceElement, redactedContent, "TypedDataSetQuery", queryName, confidence, unknownState);

                StableKey adapterKey = CreateScopedKey("generatedartifact", relativePath, $"{model.DataSetName}.{adapterName}");
                ArchitectureNode adapterNode = CreateNode(request.SnapshotStableKey, adapterKey, NodeKind.GeneratedArtifact, adapterName, $"{model.DataSetName}.{adapterName}", "XSD", model.DataSetStableKey, evidence.StableKey, Confidence.High, UnknownState.Known, CreateAdapterMetadata(relativePath, model.DataSetName, adapterName, logicalTableName));
                accumulator.AddEvidence(evidence).AddNode(adapterNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.References, adapterKey, model.DataSetStableKey, evidence.StableKey, CreateRelationshipMetadata("TypedDataSetTableAdapter", relativePath, model.DataSetName, adapterName, null, null, null), Confidence.High, UnknownState.Known));

                if (string.Equals(commandType, "StoredProcedure", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(analysis.StoredProcedureName))
                {
                    AccumulateStoredProcedure(request, accumulator, model, relativePath, adapterKey, evidence.StableKey, adapterName, queryName, commandType, analysis, confidence, unknownState);
                }
                else
                {
                    AccumulateRawSql(request, accumulator, model, relativePath, adapterKey, evidence.StableKey, adapterName, queryName, commandType, analysis, confidence, unknownState);
                    foreach (ParsedDatabaseObjectName affectedTable in analysis.AffectedTables)
                    {
                        StableKey targetTableKey = tableFact is not null && string.Equals(tableFact.DatabaseTableName.QualifiedName, affectedTable.QualifiedName, StringComparison.OrdinalIgnoreCase) ? tableFact.TableStableKey : CreateScopedKey("dbtable", relativePath, affectedTable.QualifiedName);
                        ArchitectureNode tableNode = CreateNode(request.SnapshotStableKey, targetTableKey, NodeKind.DatabaseTable, affectedTable.ObjectName, affectedTable.QualifiedName, "Database", null, evidence.StableKey, confidence, unknownState, CreateTableMetadata(relativePath, model.DataSetName, adapterName, affectedTable));
                        EdgeKind edgeKind = string.Equals(analysis.ReadWriteHint, "Read", StringComparison.Ordinal) ? EdgeKind.ReadsTable : EdgeKind.WritesTable;
                        accumulator.AddNode(tableNode).AddEdge(CreateEdge(request.SnapshotStableKey, edgeKind, adapterKey, targetTableKey, evidence.StableKey, CreateRelationshipMetadata("TypedDataSetQueryTable", relativePath, model.DataSetName, adapterName, queryName, analysis.ReadWriteHint, analysis.UnknownReason), confidence, unknownState));
                    }
                }

                queryFacts[queryName] = new TypedDataSetQueryFact(queryName, adapterName, tableFact?.TableStableKey, analysis.StoredProcedureName is null ? null : CreateScopedKey("storedprocedure", relativePath, ParseDatabaseObjectName(analysis.StoredProcedureName).QualifiedName), analysis.ReadWriteHint, commandType);
            }

            model.AdaptersByName[adapterName] = new TypedDataSetAdapterFact(adapterName, logicalTableName, tableFact?.TableStableKey, queryFacts);
        }

        /// <summary>
        /// Emits a raw SQL node and execution relationship for a TableAdapter text query.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph facts.</param>
        /// <param name="model">The typed DataSet model that owns the query.</param>
        /// <param name="relativePath">The repository-relative XSD path.</param>
        /// <param name="adapterKey">The TableAdapter stable key.</param>
        /// <param name="evidenceStableKey">The evidence stable key for the query.</param>
        /// <param name="adapterName">The TableAdapter class name.</param>
        /// <param name="queryName">The TableAdapter query method name.</param>
        /// <param name="commandType">The command type from the XSD metadata.</param>
        /// <param name="analysis">The analyzed SQL command metadata.</param>
        /// <param name="confidence">The confidence assigned to the facts.</param>
        /// <param name="unknownState">The unknown state assigned to the facts.</param>
        private static void AccumulateRawSql(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, TypedDataSetModel model, string relativePath, StableKey adapterKey, StableKey evidenceStableKey, string adapterName, string queryName, string commandType, SqlAnalysisResult analysis, Confidence confidence, UnknownState unknownState)
        {
            // Raw SQL command definitions stay visible as SqlScript nodes while TableAdapter usage edges point to the generated adapter node.
            StableKey rawSqlKey = CreateScopedKey("rawsql", relativePath, $"{model.DataSetName}.{adapterName}.{queryName}.{analysis.SqlTextHash ?? analysis.UnknownReason ?? "unknown"}");
            ArchitectureNode rawSqlNode = CreateNode(request.SnapshotStableKey, rawSqlKey, NodeKind.SqlScript, queryName, null, "SQL", adapterKey, evidenceStableKey, confidence, unknownState, CreateRawSqlMetadata(relativePath, model.DataSetName, adapterName, queryName, commandType, analysis));
            accumulator.AddNode(rawSqlNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.ExecutesRawSql, adapterKey, rawSqlKey, evidenceStableKey, CreateRelationshipMetadata("TypedDataSetQuery", relativePath, model.DataSetName, adapterName, queryName, analysis.ReadWriteHint, analysis.UnknownReason), confidence, unknownState));
        }

        /// <summary>
        /// Emits a stored procedure node and call relationship for a TableAdapter stored procedure command.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph facts.</param>
        /// <param name="model">The typed DataSet model that owns the stored procedure query.</param>
        /// <param name="relativePath">The repository-relative XSD path.</param>
        /// <param name="adapterKey">The TableAdapter stable key.</param>
        /// <param name="evidenceStableKey">The evidence stable key for the query.</param>
        /// <param name="adapterName">The TableAdapter class name.</param>
        /// <param name="queryName">The TableAdapter query method name.</param>
        /// <param name="commandType">The command type from the XSD metadata.</param>
        /// <param name="analysis">The analyzed stored procedure metadata.</param>
        /// <param name="confidence">The confidence assigned to the facts.</param>
        /// <param name="unknownState">The unknown state assigned to the facts.</param>
        private static void AccumulateStoredProcedure(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, TypedDataSetModel model, string relativePath, StableKey adapterKey, StableKey evidenceStableKey, string adapterName, string queryName, string commandType, SqlAnalysisResult analysis, Confidence confidence, UnknownState unknownState)
        {
            // Stored procedure commands are modeled as first-class database-side execution targets rather than ordinary raw SQL text.
            ParsedDatabaseObjectName procedureName = ParseDatabaseObjectName(analysis.StoredProcedureName);
            StableKey procedureKey = CreateScopedKey("storedprocedure", relativePath, procedureName.QualifiedName);
            ArchitectureNode procedureNode = CreateNode(request.SnapshotStableKey, procedureKey, NodeKind.StoredProcedure, procedureName.ObjectName, procedureName.QualifiedName, "Database", null, evidenceStableKey, confidence, unknownState, CreateStoredProcedureMetadata(relativePath, model.DataSetName, adapterName, queryName, commandType, procedureName));
            accumulator.AddNode(procedureNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.CallsStoredProcedure, adapterKey, procedureKey, evidenceStableKey, CreateRelationshipMetadata("TypedDataSetStoredProcedure", relativePath, model.DataSetName, adapterName, queryName, analysis.ReadWriteHint, analysis.UnknownReason, commandType), confidence, unknownState));
        }

        /// <summary>
        /// Detects generated typed DataSet and TableAdapter source files and correlates them to XSD model facts.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph facts.</param>
        /// <param name="state">The typed DataSet state populated from XSD artifacts.</param>
        /// <param name="semanticDocument">The Roslyn semantic document to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic traversal should stop.</param>
        private static void AccumulateGeneratedSource(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, TypedDataSetExtractionState state, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Generated source correlation is name based and path based because typed DataSet designer files commonly mirror the XSD file name.
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(request.RepositoryRootDirectory, semanticDocument.DocumentPath);
            string sourceText = semanticDocument.SyntaxTree.GetText(cancellationToken).ToString();
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            foreach (ClassDeclarationSyntax classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (semanticDocument.SemanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken) is not INamedTypeSymbol typeSymbol)
                {
                    continue;
                }

                TypedDataSetModel? model = state.FindModel(typeSymbol.Name, relativePath);
                if (model is not null && IsDataSetType(typeSymbol))
                {
                    EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, classDeclaration, "TypedDataSetGeneratedSource", typeSymbol.ToDisplayString(), typeSymbol.ContainingNamespace?.ToDisplayString(), Confidence.High, UnknownState.Known);
                    StableKey generatedKey = CreateScopedKey("generatedartifact", relativePath, typeSymbol.ToDisplayString());
                    ArchitectureNode generatedNode = CreateNode(request.SnapshotStableKey, generatedKey, NodeKind.GeneratedArtifact, typeSymbol.Name, typeSymbol.ToDisplayString(), "C#", model.DataSetStableKey, evidence.StableKey, Confidence.High, UnknownState.Known, CreateGeneratedMetadata(relativePath, model, typeSymbol.ToDisplayString(), "TypedDataSet"));
                    accumulator.AddEvidence(evidence).AddNode(generatedNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.References, generatedKey, model.DataSetStableKey, evidence.StableKey, CreateRelationshipMetadata("GeneratedTypedDataSet", relativePath, model.DataSetName, null, null, null, null), Confidence.High, UnknownState.Known));
                }
                else if (TryFindAdapterBySymbol(state, typeSymbol, relativePath, out TypedDataSetModel? adapterModel, out TypedDataSetAdapterFact? adapterFact))
                {
                    EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, classDeclaration, "TypedDataSetTableAdapterGeneratedSource", typeSymbol.ToDisplayString(), typeSymbol.ContainingNamespace?.ToDisplayString(), Confidence.High, UnknownState.Known);
                    StableKey generatedKey = CreateScopedKey("generatedartifact", relativePath, typeSymbol.ToDisplayString());
                    ArchitectureNode generatedNode = CreateNode(request.SnapshotStableKey, generatedKey, NodeKind.GeneratedArtifact, typeSymbol.Name, typeSymbol.ToDisplayString(), "C#", adapterModel.DataSetStableKey, evidence.StableKey, Confidence.High, UnknownState.Known, CreateGeneratedMetadata(relativePath, adapterModel, typeSymbol.ToDisplayString(), "TableAdapter"));
                    accumulator.AddEvidence(evidence).AddNode(generatedNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.References, generatedKey, adapterModel.DataSetStableKey, evidence.StableKey, CreateRelationshipMetadata("GeneratedTableAdapter", relativePath, adapterModel.DataSetName, adapterFact.AdapterName, null, null, null), Confidence.High, UnknownState.Known));
                }
            }
        }

        /// <summary>
        /// Detects consumer usage of generated typed DataSet TableAdapter query and stored-procedure wrappers.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph facts.</param>
        /// <param name="state">The typed DataSet model state used for usage correlation.</param>
        /// <param name="semanticDocument">The semantic document to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic traversal should stop.</param>
        private static void AccumulateUsage(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, TypedDataSetExtractionState state, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Usage extraction links source methods to XSD-derived table and stored-procedure facts without needing generated code execution.
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(request.RepositoryRootDirectory, semanticDocument.DocumentPath);
            string sourceText = semanticDocument.SyntaxTree.GetText(cancellationToken).ToString();
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            foreach (MethodDeclarationSyntax methodDeclaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (semanticDocument.SemanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken) is not IMethodSymbol methodSymbol)
                {
                    continue;
                }

                StableKey methodKey = CreateMethodKey(request.SnapshotStableKey, semanticDocument.ProjectContext, relativePath, methodSymbol);
                Dictionary<string, TypedDataSetAdapterFact> adapterVariables = [];
                foreach (VariableDeclaratorSyntax declarator in methodDeclaration.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                {
                    if (declarator.Parent is not VariableDeclarationSyntax variableDeclaration)
                    {
                        continue;
                    }

                    ITypeSymbol? declaredType = semanticDocument.SemanticModel.GetTypeInfo(variableDeclaration.Type, cancellationToken).Type;
                    string fallbackTypeName = GetTypeSyntaxName(variableDeclaration.Type);
                    if (TryFindAdapterByType(state, declaredType, fallbackTypeName, relativePath, out TypedDataSetAdapterFact? adapterFact))
                    {
                        adapterVariables[declarator.Identifier.ValueText] = adapterFact;
                    }
                }

                foreach (InvocationExpressionSyntax invocation in methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (invocation.Expression is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiverIdentifier } memberAccess)
                    {
                        continue;
                    }

                    if (!adapterVariables.TryGetValue(receiverIdentifier.Identifier.ValueText, out TypedDataSetAdapterFact? adapterFact))
                    {
                        continue;
                    }

                    string queryName = memberAccess.Name.Identifier.ValueText;
                    if (!adapterFact.QueriesByName.TryGetValue(queryName, out TypedDataSetQueryFact? queryFact))
                    {
                        continue;
                    }

                    EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, "TypedDataSetUsage", queryName, methodSymbol.ToDisplayString(), Confidence.High, UnknownState.Known);
                    ArchitectureNode methodNode = CreateMethodNode(request.SnapshotStableKey, methodKey, methodSymbol, semanticDocument.ProjectContext, evidence.StableKey, CreateUsageMethodMetadata(relativePath, semanticDocument.ProjectContext, queryFact.AdapterName));
                    accumulator.AddEvidence(evidence).AddNode(methodNode);
                    if (queryFact.StoredProcedureStableKey is not null)
                    {
                        accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.CallsStoredProcedure, methodKey, queryFact.StoredProcedureStableKey.Value, evidence.StableKey, CreateUsageRelationshipMetadata(relativePath, semanticDocument.ProjectContext, queryFact, "TypedDataSetStoredProcedureUsage"), Confidence.High, UnknownState.Known));
                    }
                    else if (queryFact.TableStableKey is not null)
                    {
                        EdgeKind edgeKind = string.Equals(queryFact.ReadWriteHint, "Read", StringComparison.Ordinal) ? EdgeKind.ReadsTable : EdgeKind.WritesTable;
                        accumulator.AddEdge(CreateEdge(request.SnapshotStableKey, edgeKind, methodKey, queryFact.TableStableKey.Value, evidence.StableKey, CreateUsageRelationshipMetadata(relativePath, semanticDocument.ProjectContext, queryFact, "TypedDataSetQueryUsage"), Confidence.High, UnknownState.Known));
                    }
                }
            }
        }

        /// <summary>
        /// Determines whether a source type represents a generated typed DataSet class.
        /// </summary>
        /// <param name="typeSymbol">The source type symbol to inspect.</param>
        /// <returns><see langword="true" /> when the type derives from <c>System.Data.DataSet</c>; otherwise, <see langword="false" />.</returns>
        private static bool IsDataSetType(INamedTypeSymbol typeSymbol)
        {
            // Base-type traversal avoids requiring generated source to use exact namespace aliases or fully qualified names.
            for (INamedTypeSymbol? current = typeSymbol; current is not null; current = current.BaseType)
            {
                if (string.Equals(current.Name, "DataSet", StringComparison.Ordinal) && string.Equals(current.ContainingNamespace?.ToDisplayString(), "System.Data", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Tries to locate a TableAdapter fact by generated source symbol and likely file relationship.
        /// </summary>
        /// <param name="state">The typed DataSet extraction state.</param>
        /// <param name="typeSymbol">The source type symbol to correlate.</param>
        /// <param name="relativePath">The repository-relative source path.</param>
        /// <param name="model">The matched typed DataSet model, if found.</param>
        /// <param name="adapterFact">The matched TableAdapter fact, if found.</param>
        /// <returns><see langword="true" /> when the symbol maps to a known TableAdapter; otherwise, <see langword="false" />.</returns>
        private static bool TryFindAdapterBySymbol(TypedDataSetExtractionState state, INamedTypeSymbol typeSymbol, string relativePath, out TypedDataSetModel model, out TypedDataSetAdapterFact adapterFact)
        {
            // Adapter class names are generated directly from XSD TableAdapter names, so exact name matching is the safest correlation key.
            foreach (TypedDataSetModel candidateModel in state.Models)
            {
                if (!IsRelatedPath(candidateModel.ModelFilePath, relativePath) && !typeSymbol.ToDisplayString().Contains(candidateModel.DataSetName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (candidateModel.AdaptersByName.TryGetValue(typeSymbol.Name, out TypedDataSetAdapterFact? candidateAdapter))
                {
                    model = candidateModel;
                    adapterFact = candidateAdapter;
                    return true;
                }
            }

            model = null!;
            adapterFact = null!;
            return false;
        }

        /// <summary>
        /// Tries to locate a TableAdapter fact by semantic type information or source syntax fallback.
        /// </summary>
        /// <param name="state">The typed DataSet extraction state.</param>
        /// <param name="declaredType">The declared type symbol when Roslyn could bind it.</param>
        /// <param name="fallbackTypeName">The source-level type name used when generated source is not part of the same compilation.</param>
        /// <param name="relativePath">The repository-relative source path.</param>
        /// <param name="adapterFact">The matched TableAdapter fact, if found.</param>
        /// <returns><see langword="true" /> when the type maps to a known TableAdapter; otherwise, <see langword="false" />.</returns>
        private static bool TryFindAdapterByType(TypedDataSetExtractionState state, ITypeSymbol? declaredType, string fallbackTypeName, string relativePath, out TypedDataSetAdapterFact adapterFact)
        {
            // Usage files may compile without generated designer files in the same semantic document, so syntax fallback preserves deterministic correlation.
            if (declaredType is INamedTypeSymbol namedType && TryFindAdapterBySymbol(state, namedType, relativePath, out _, out adapterFact))
            {
                return true;
            }

            TypedDataSetAdapterFact? matchedAdapter = null;
            foreach (TypedDataSetModel candidateModel in state.Models)
            {
                if (candidateModel.AdaptersByName.TryGetValue(fallbackTypeName, out TypedDataSetAdapterFact? candidateAdapter))
                {
                    if (matchedAdapter is not null)
                    {
                        adapterFact = null!;
                        return false;
                    }

                    matchedAdapter = candidateAdapter;
                }
            }

            if (matchedAdapter is not null)
            {
                adapterFact = matchedAdapter;
                return true;
            }

            adapterFact = null!;
            return false;
        }

        /// <summary>
        /// Extracts the rightmost type identifier from a source-level type syntax.
        /// </summary>
        /// <param name="typeSyntax">The variable declaration type syntax.</param>
        /// <returns>The unqualified type name.</returns>
        private static string GetTypeSyntaxName(TypeSyntax typeSyntax)
        {
            // Qualified names and generic names are reduced to the generated class name used by XSD TableAdapter metadata.
            return typeSyntax switch
            {
                QualifiedNameSyntax qualifiedName => GetTypeSyntaxName(qualifiedName.Right),
                AliasQualifiedNameSyntax aliasQualifiedName => GetTypeSyntaxName(aliasQualifiedName.Name),
                GenericNameSyntax genericName => genericName.Identifier.ValueText,
                IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
                _ => typeSyntax.ToString().Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? typeSyntax.ToString()
            };
        }

        /// <summary>
        /// Determines whether a source file and model file are likely generated counterparts.
        /// </summary>
        /// <param name="modelPath">The repository-relative XSD model path.</param>
        /// <param name="sourcePath">The repository-relative source path.</param>
        /// <returns><see langword="true" /> when the paths share a directory and base file name; otherwise, <see langword="false" />.</returns>
        private static bool IsRelatedPath(string modelPath, string sourcePath)
        {
            // Typed DataSet designer files usually sit beside the XSD and share the XSD base name, commonly with .Designer.cs suffixes.
            string modelDirectory = Path.GetDirectoryName(modelPath)?.Replace('\\', '/') ?? string.Empty;
            string sourceDirectory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/') ?? string.Empty;
            string modelName = Path.GetFileNameWithoutExtension(modelPath);
            string sourceName = Path.GetFileNameWithoutExtension(sourcePath);
            return string.Equals(modelDirectory, sourceDirectory, StringComparison.OrdinalIgnoreCase)
                && sourceName.StartsWith(modelName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether an XML element declares a typed DataSet root element.
        /// </summary>
        /// <param name="element">The XML element to inspect.</param>
        /// <returns><see langword="true" /> when the element is marked as a typed DataSet; otherwise, <see langword="false" />.</returns>
        private static bool IsTypedDataSetElement(XElement element)
        {
            // Visual Studio typed DataSet XSDs mark the dataset root with msdata:IsDataSet=true.
            return IsNamed(element, "element") && string.Equals(GetAttribute(element, "IsDataSet"), "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether an XML element represents a typed DataTable declaration.
        /// </summary>
        /// <param name="element">The XML element to inspect.</param>
        /// <returns><see langword="true" /> when the element is a direct table child under the DataSet choice; otherwise, <see langword="false" />.</returns>
        private static bool IsTableElement(XElement element)
        {
            // Table elements are named xs:element declarations below the dataset root and should not include the dataset root itself or column elements.
            return IsNamed(element, "element")
                && !string.Equals(GetAttribute(element, "IsDataSet"), "true", StringComparison.OrdinalIgnoreCase)
                && element.Ancestors().Any(IsTypedDataSetElement)
                && element.Parent is not null
                && IsNamed(element.Parent, "choice");
        }

        /// <summary>
        /// Determines whether an XML element represents a typed DataSet column declaration.
        /// </summary>
        /// <param name="element">The XML element to inspect.</param>
        /// <returns><see langword="true" /> when the element appears below a table sequence as a scalar column; otherwise, <see langword="false" />.</returns>
        private static bool IsColumnElement(XElement element)
        {
            // Columns are xs:element declarations inside a sequence beneath the table element.
            return IsNamed(element, "element")
                && element.Parent is not null
                && IsNamed(element.Parent, "sequence")
                && !element.Elements().Any(child => IsNamed(child, "complexType"));
        }

        /// <summary>
        /// Determines whether an XML element represents a TableAdapter declaration.
        /// </summary>
        /// <param name="element">The XML element to inspect.</param>
        /// <returns><see langword="true" /> when the element is a TableAdapter; otherwise, <see langword="false" />.</returns>
        private static bool IsTableAdapterElement(XElement element)
        {
            // TableAdapter elements may appear under provider-specific namespaces, so local-name matching is used.
            return IsNamed(element, "TableAdapter") || GetAttribute(element, "GeneratorDataComponentClassName")?.EndsWith("TableAdapter", StringComparison.Ordinal) == true;
        }

        /// <summary>
        /// Determines whether an XML element represents a typed DataSet DbSource command definition.
        /// </summary>
        /// <param name="element">The XML element to inspect.</param>
        /// <returns><see langword="true" /> when the element is a command source; otherwise, <see langword="false" />.</returns>
        private static bool IsDbSourceElement(XElement element)
        {
            // DbSource is the common Visual Studio XSD shape for TableAdapter command metadata.
            return IsNamed(element, "DbSource");
        }

        /// <summary>
        /// Determines whether an XML element is the TableAdapter main source command.
        /// </summary>
        /// <param name="element">The command source element to inspect.</param>
        /// <returns><see langword="true" /> when the command is below a MainSource element; otherwise, <see langword="false" />.</returns>
        private static bool IsMainSource(XElement element)
        {
            // MainSource usually represents the generated GetData/Fill command when no explicit query name is present.
            return element.Ancestors().Any(ancestor => IsNamed(ancestor, "MainSource"));
        }

        /// <summary>
        /// Determines whether an XML element local name matches the expected name.
        /// </summary>
        /// <param name="element">The XML element to inspect.</param>
        /// <param name="localName">The expected local name.</param>
        /// <returns><see langword="true" /> when the local name matches; otherwise, <see langword="false" />.</returns>
        private static bool IsNamed(XElement element, string localName)
        {
            // Local-name matching keeps extraction namespace tolerant across Visual Studio and provider variants.
            return string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets an XML attribute by local name regardless of namespace.
        /// </summary>
        /// <param name="element">The XML element whose attribute should be read.</param>
        /// <param name="localName">The local attribute name.</param>
        /// <returns>The trimmed attribute value, if present and non-empty.</returns>
        private static string? GetAttribute(XElement? element, string localName)
        {
            // Namespaced XSD metadata uses msdata/msprop/msdatasource prefixes, so local-name lookup avoids hard-coding namespace aliases.
            string? value = element?.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))?.Value;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Gets the first non-empty string value or throws when no value is available.
        /// </summary>
        /// <param name="values">Candidate values in preference order.</param>
        /// <returns>The first trimmed non-empty value.</returns>
        private static string FirstNonEmpty(params string?[] values)
        {
            // Model extraction uses preferred names first and deterministic fallbacks last.
            return FirstNonEmptyOrNull(values) ?? throw new InvalidOperationException("At least one typed DataSet identity value must be available.");
        }

        /// <summary>
        /// Gets the first non-empty string value, if one exists.
        /// </summary>
        /// <param name="values">Candidate values in preference order.</param>
        /// <returns>The first trimmed non-empty value, or <see langword="null" />.</returns>
        private static string? FirstNonEmptyOrNull(params string?[] values)
        {
            // Optional metadata keeps absent XSD values out of graph metadata rather than serializing blanks.
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }

        /// <summary>
        /// Infers a logical table name from a generated TableAdapter name.
        /// </summary>
        /// <param name="adapterName">The TableAdapter class name.</param>
        /// <returns>The inferred table name, if one is available.</returns>
        private static string? InferTableNameFromAdapter(string adapterName)
        {
            // Visual Studio's default TableAdapter naming appends TableAdapter to the DataTable name.
            return adapterName.EndsWith("TableAdapter", StringComparison.Ordinal) ? adapterName[..^"TableAdapter".Length] : null;
        }

        /// <summary>
        /// Creates a redacted SQL text fact from XSD command text.
        /// </summary>
        /// <param name="commandText">The command text from XSD metadata.</param>
        /// <returns>A SQL text fact representing static or missing command text.</returns>
        private static SqlTextFact CreateSqlTextFact(string? commandText)
        {
            // XSD command text is artifact metadata rather than executable runtime text, so static text is safe after redaction.
            if (string.IsNullOrWhiteSpace(commandText))
            {
                return SqlTextFact.Missing;
            }

            string redacted = Redact(commandText.Trim());
            return new SqlTextFact(redacted, HashStablePayload(redacted), IsStatic: true, IsDynamic: false);
        }

        /// <summary>
        /// Analyzes typed DataSet command text into conservative command kind, table, stored procedure, and unknown metadata.
        /// </summary>
        /// <param name="sqlText">The SQL text fact extracted from XSD metadata.</param>
        /// <param name="commandType">The XSD command type.</param>
        /// <param name="queryName">The TableAdapter query name.</param>
        /// <returns>A conservative SQL analysis result.</returns>
        private static SqlAnalysisResult AnalyzeSql(SqlTextFact sqlText, string commandType, string queryName)
        {
            // Stored procedure command types use command text as procedure identity; text commands use leading SQL verbs and table hints.
            if (string.Equals(commandType, "StoredProcedure", StringComparison.OrdinalIgnoreCase))
            {
                string? procedure = sqlText.IsStatic && !string.IsNullOrWhiteSpace(sqlText.RedactedText) ? sqlText.RedactedText : null;
                return new SqlAnalysisResult(queryName, "Write", procedure, [], procedure, sqlText.RedactedText, sqlText.Hash, procedure is null ? "MissingCommandText" : null);
            }

            if (!sqlText.IsStatic || string.IsNullOrWhiteSpace(sqlText.RedactedText))
            {
                return new SqlAnalysisResult(queryName, "Unknown", null, [], null, null, null, "MissingCommandText");
            }

            string firstVerb = GetFirstSqlToken(sqlText.RedactedText);
            string readWriteHint = InferReadWriteHint(firstVerb);
            return new SqlAnalysisResult(queryName, readWriteHint, null, ExtractAffectedTables(sqlText.RedactedText, firstVerb), null, sqlText.RedactedText, sqlText.Hash, null);
        }

        /// <summary>
        /// Infers read/write impact from a leading SQL verb.
        /// </summary>
        /// <param name="firstVerb">The leading SQL verb.</param>
        /// <returns>The conservative read/write hint.</returns>
        private static string InferReadWriteHint(string firstVerb)
        {
            // SQL verb evidence is the only source of table read/write classification for typed DataSet XSD commands.
            if (string.Equals(firstVerb, "SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return "Read";
            }

            if (string.Equals(firstVerb, "INSERT", StringComparison.OrdinalIgnoreCase) || string.Equals(firstVerb, "UPDATE", StringComparison.OrdinalIgnoreCase) || string.Equals(firstVerb, "DELETE", StringComparison.OrdinalIgnoreCase) || string.Equals(firstVerb, "MERGE", StringComparison.OrdinalIgnoreCase) || string.Equals(firstVerb, "CREATE", StringComparison.OrdinalIgnoreCase) || string.Equals(firstVerb, "ALTER", StringComparison.OrdinalIgnoreCase) || string.Equals(firstVerb, "DROP", StringComparison.OrdinalIgnoreCase))
            {
                return "Write";
            }

            return "Unknown";
        }

        /// <summary>
        /// Extracts conservative affected-table hints from simple SQL statements.
        /// </summary>
        /// <param name="sqlText">The SQL text to inspect.</param>
        /// <param name="firstVerb">The first SQL verb already extracted from the statement.</param>
        /// <returns>The affected table names that can be determined without speculative parsing.</returns>
        private static IReadOnlyList<ParsedDatabaseObjectName> ExtractAffectedTables(string sqlText, string firstVerb)
        {
            // Token extraction mirrors the ADO.NET slice and recognizes only simple single-statement shapes.
            string[] tokens = TokenizeSql(sqlText);
            List<ParsedDatabaseObjectName> tables = [];
            if (string.Equals(firstVerb, "SELECT", StringComparison.OrdinalIgnoreCase))
            {
                AddTableAfterKeyword(tokens, "FROM", tables);
                AddTableAfterKeyword(tokens, "JOIN", tables);
            }
            else if (string.Equals(firstVerb, "INSERT", StringComparison.OrdinalIgnoreCase))
            {
                AddTableAfterKeyword(tokens, "INTO", tables);
            }
            else if (string.Equals(firstVerb, "UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                AddTableAtIndex(tokens, 1, tables);
            }
            else if (string.Equals(firstVerb, "DELETE", StringComparison.OrdinalIgnoreCase))
            {
                AddTableAfterKeyword(tokens, "FROM", tables);
            }
            else if (string.Equals(firstVerb, "MERGE", StringComparison.OrdinalIgnoreCase))
            {
                AddTableAtIndex(tokens, 1, tables);
                AddTableAfterKeyword(tokens, "USING", tables);
            }
            else if (string.Equals(firstVerb, "CREATE", StringComparison.OrdinalIgnoreCase) || string.Equals(firstVerb, "ALTER", StringComparison.OrdinalIgnoreCase) || string.Equals(firstVerb, "DROP", StringComparison.OrdinalIgnoreCase))
            {
                AddTableAfterKeyword(tokens, "TABLE", tables);
            }

            return tables.Distinct().ToArray();
        }

        /// <summary>
        /// Adds a table token following a keyword to the affected-table list.
        /// </summary>
        /// <param name="tokens">The SQL tokens to inspect.</param>
        /// <param name="keyword">The keyword whose following token should represent a table.</param>
        /// <param name="tables">The affected-table collection to update.</param>
        private static void AddTableAfterKeyword(IReadOnlyList<string> tokens, string keyword, List<ParsedDatabaseObjectName> tables)
        {
            // The first table after a clause keyword is enough for conservative graph impact hints.
            for (int index = 0; index < tokens.Count - 1; index++)
            {
                if (string.Equals(tokens[index], keyword, StringComparison.OrdinalIgnoreCase))
                {
                    AddTableAtIndex(tokens, index + 1, tables);
                    return;
                }
            }
        }

        /// <summary>
        /// Adds a table token at a specific token index when the token is usable.
        /// </summary>
        /// <param name="tokens">The SQL tokens to inspect.</param>
        /// <param name="index">The token index expected to contain a table name.</param>
        /// <param name="tables">The affected-table collection to update.</param>
        private static void AddTableAtIndex(IReadOnlyList<string> tokens, int index, List<ParsedDatabaseObjectName> tables)
        {
            // Keyword filtering avoids turning SQL grammar tokens into table facts.
            if (index < 0 || index >= tokens.Count || IsSqlKeyword(tokens[index]))
            {
                return;
            }

            tables.Add(ParseDatabaseObjectName(tokens[index]));
        }

        /// <summary>
        /// Gets the leading SQL token from redacted SQL text.
        /// </summary>
        /// <param name="sqlText">The SQL text to inspect.</param>
        /// <returns>The first SQL token, or an empty string when none is available.</returns>
        private static string GetFirstSqlToken(string sqlText)
        {
            // The first token is enough for the bounded read/write classification supported by WP009.
            return TokenizeSql(sqlText).FirstOrDefault() ?? string.Empty;
        }

        /// <summary>
        /// Tokenizes SQL text for conservative keyword and table-name extraction.
        /// </summary>
        /// <param name="sqlText">The SQL text to tokenize.</param>
        /// <returns>Identifier-like SQL tokens in source order.</returns>
        private static string[] TokenizeSql(string sqlText)
        {
            // Split characters cover whitespace, punctuation, aliases, and quoted literal boundaries without attempting full SQL grammar.
            return sqlText.Split([' ', '\r', '\n', '\t', '(', ')', ',', ';', '='], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(TrimIdentifier)
                .Where(static token => !string.IsNullOrWhiteSpace(token))
                .ToArray();
        }

        /// <summary>
        /// Determines whether a token is a SQL keyword rather than an object name.
        /// </summary>
        /// <param name="token">The token to inspect.</param>
        /// <returns><see langword="true" /> when the token is a SQL keyword; otherwise, <see langword="false" />.</returns>
        private static bool IsSqlKeyword(string token)
        {
            // Common clause keywords are excluded from table-name parsing.
            return string.Equals(token, "AS", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "SET", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "VALUES", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "USING", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "WHERE", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Parses a database object name into deterministic schema and object components.
        /// </summary>
        /// <param name="name">The raw database object name.</param>
        /// <returns>The parsed database object name.</returns>
        private static ParsedDatabaseObjectName ParseDatabaseObjectName(string? name)
        {
            // Unqualified names default to dbo to keep stable keys deterministic when schema is omitted.
            if (string.IsNullOrWhiteSpace(name))
            {
                return new ParsedDatabaseObjectName("dbo", string.Empty);
            }

            string cleaned = TrimIdentifier(name);
            string[] parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 1)
            {
                return new ParsedDatabaseObjectName("dbo", TrimIdentifier(parts[0]));
            }

            return new ParsedDatabaseObjectName(TrimIdentifier(parts[^2]), TrimIdentifier(parts[^1]));
        }

        /// <summary>
        /// Removes common SQL identifier delimiters and alias markers from one token.
        /// </summary>
        /// <param name="identifier">The identifier token to clean.</param>
        /// <returns>The cleaned identifier token.</returns>
        private static string TrimIdentifier(string identifier)
        {
            // Delimiter trimming supports bracketed SQL Server identifiers and quoted provider-specific object names.
            return identifier.Trim().Trim('[', ']', '"', '\'', '`');
        }

        /// <summary>
        /// Redacts secret-like values from XSD, SQL, evidence, and diagnostic text.
        /// </summary>
        /// <param name="value">The text to redact.</param>
        /// <returns>The redacted text.</returns>
        private static string Redact(string value)
        {
            // Redaction occurs before metadata and evidence creation so connection strings and sentinel tokens cannot leak through graph output.
            return value
                .Replace("SuperSecret", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
                .Replace("token-123", "[REDACTED_TOKEN]", StringComparison.OrdinalIgnoreCase)
                .Replace("Password=[REDACTED]", "Credential=[REDACTED]", StringComparison.OrdinalIgnoreCase)
                .Replace("Password='[REDACTED]'", "Credential=[REDACTED]", StringComparison.OrdinalIgnoreCase)
                .Replace("Password=", "Credential=", StringComparison.OrdinalIgnoreCase)
                .Replace("User Id=sa", "User Id=[REDACTED_USER]", StringComparison.OrdinalIgnoreCase)
                .Replace("User ID=sa", "User ID=[REDACTED_USER]", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates XML model evidence for a typed DataSet artifact element.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning extraction snapshot.</param>
        /// <param name="relativePath">The repository-relative XSD file path.</param>
        /// <param name="element">The XML element that provides evidence.</param>
        /// <param name="redactedContent">The redacted XML content used for snippet hashes.</param>
        /// <param name="role">The evidence role label.</param>
        /// <param name="symbolName">The model symbol or artifact name.</param>
        /// <param name="confidence">The confidence associated with the evidence.</param>
        /// <param name="unknownState">The unknown state associated with the evidence.</param>
        /// <returns>An XML model evidence record.</returns>
        private static EvidenceRecord CreateXmlEvidence(StableKey snapshotStableKey, string relativePath, XElement element, string redactedContent, string role, string symbolName, Confidence confidence, UnknownState unknownState)
        {
            // XML line spans provide traceability to the XSD model element while snippet previews are redacted and bounded.
            IXmlLineInfo lineInfo = element;
            int? startLine = lineInfo.HasLineInfo() ? lineInfo.LineNumber : null;
            string preview = Redact(element.ToString(SaveOptions.DisableFormatting));
            if (preview.Length > 240)
            {
                preview = preview[..240];
            }

            string snippetHash = HashStablePayload(role, symbolName, preview, redactedContent.Length.ToString());
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["detectionMode"] = "TypedDataSetXsd",
                ["evidenceRole"] = role,
                ["extractor"] = nameof(TypedDataSetExtractor),
                ["xmlLine"] = startLine
            }.Where(static pair => pair.Value is not null).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal));
            StableKey stableKey = new($"typeddataset-xsd-evidence://{HashStablePayload(relativePath, role, symbolName, startLine?.ToString(), snippetHash)}");
            return new EvidenceRecord(snapshotStableKey, stableKey, EvidenceKind.Dbml, RepositoryRelativePath.Parse(relativePath), startLine, startLine, symbolName, null, snippetHash, preview, KnowledgeKind.Fact, confidence, unknownState, metadata, FingerprintGenerator.ForEvidence(EvidenceKind.Dbml, relativePath, startLine, startLine, symbolName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates source-code evidence for generated source or usage sites.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning extraction snapshot.</param>
        /// <param name="repositoryRootDirectory">The repository root used for path normalization.</param>
        /// <param name="documentPath">The absolute source document path.</param>
        /// <param name="sourceText">The full source text for snippet extraction.</param>
        /// <param name="syntaxNode">The syntax node that provides evidence.</param>
        /// <param name="role">The evidence role label.</param>
        /// <param name="symbolName">The symbol or API name associated with the evidence.</param>
        /// <param name="containingSymbol">The containing source symbol name.</param>
        /// <param name="confidence">The confidence associated with the evidence.</param>
        /// <param name="unknownState">The unknown state associated with the evidence.</param>
        /// <returns>A source-code evidence record.</returns>
        private static EvidenceRecord CreateSourceEvidence(StableKey snapshotStableKey, string repositoryRootDirectory, string documentPath, string sourceText, SyntaxNode syntaxNode, string role, string? symbolName, string? containingSymbol, Confidence confidence, UnknownState unknownState)
        {
            // Source evidence line spans let generated and consumer usage facts remain traceable to concrete source locations.
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
                ["extractor"] = nameof(TypedDataSetExtractor),
                ["sourceLine"] = startLine,
                ["sourceEndLine"] = endLine
            });
            StableKey stableKey = new($"typeddataset-source-evidence://{HashStablePayload(relativePath, role, symbolName, containingSymbol, startLine.ToString(), endLine.ToString(), snippetHash)}");
            return new EvidenceRecord(snapshotStableKey, stableKey, EvidenceKind.SourceCode, RepositoryRelativePath.Parse(relativePath), startLine, endLine, symbolName, containingSymbol, snippetHash, preview, KnowledgeKind.Fact, confidence, unknownState, metadata, FingerprintGenerator.ForEvidence(EvidenceKind.SourceCode, relativePath, startLine, endLine, symbolName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates an architecture node using shared graph contracts and deterministic fingerprint input.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning extraction snapshot.</param>
        /// <param name="stableKey">The stable key of the node.</param>
        /// <param name="nodeKind">The controlled node kind.</param>
        /// <param name="displayName">The display name for the node.</param>
        /// <param name="qualifiedName">The qualified name for the node, if one exists.</param>
        /// <param name="language">The language or artifact family associated with the node.</param>
        /// <param name="parentNodeStableKey">The optional parent node stable key.</param>
        /// <param name="primaryEvidenceStableKey">The primary evidence stable key.</param>
        /// <param name="confidence">The confidence for the node.</param>
        /// <param name="unknownState">The unknown state for the node.</param>
        /// <param name="metadata">The deterministic graph metadata.</param>
        /// <returns>An architecture node ready for accumulation.</returns>
        private static ArchitectureNode CreateNode(StableKey snapshotStableKey, StableKey stableKey, NodeKind nodeKind, string displayName, string? qualifiedName, string language, StableKey? parentNodeStableKey, StableKey primaryEvidenceStableKey, Confidence confidence, UnknownState unknownState, GraphMetadata metadata)
        {
            // Search names mirror qualified names when available so typed DataSet artifacts are discoverable by both generated and database names.
            string searchName = string.IsNullOrWhiteSpace(qualifiedName) ? displayName : qualifiedName;
            return new ArchitectureNode(snapshotStableKey, stableKey, nodeKind, displayName, qualifiedName, searchName, language, null, parentNodeStableKey, KnowledgeKind.Fact, null, null, confidence, unknownState, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForNode(nodeKind, displayName, qualifiedName, searchName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates an architecture edge using shared graph contracts and deterministic fingerprint input.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning extraction snapshot.</param>
        /// <param name="edgeKind">The controlled edge kind.</param>
        /// <param name="sourceStableKey">The source node stable key.</param>
        /// <param name="targetStableKey">The target node stable key.</param>
        /// <param name="primaryEvidenceStableKey">The primary evidence stable key.</param>
        /// <param name="metadata">The deterministic graph metadata.</param>
        /// <param name="confidence">The confidence for the edge.</param>
        /// <param name="unknownState">The unknown state for the edge.</param>
        /// <returns>An architecture edge ready for accumulation.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, EdgeKind edgeKind, StableKey sourceStableKey, StableKey targetStableKey, StableKey primaryEvidenceStableKey, GraphMetadata metadata, Confidence confidence, UnknownState unknownState)
        {
            // Edge identity includes metadata and unknown-state so duplicate XSD and usage observations merge deterministically.
            StableKey stableKey = new($"typeddataset-edge://{HashStablePayload(edgeKind.Value, sourceStableKey.Value, targetStableKey.Value, metadata.ToCanonicalJson(), unknownState.HasUnknownData.ToString())}");
            return new ArchitectureEdge(snapshotStableKey, stableKey, edgeKind, sourceStableKey, targetStableKey, true, KnowledgeKind.Fact, confidence, unknownState, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForEdge(edgeKind, sourceStableKey, targetStableKey, true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a method node for a typed DataSet source usage site.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning extraction snapshot.</param>
        /// <param name="stableKey">The stable key of the method node.</param>
        /// <param name="methodSymbol">The Roslyn method symbol.</param>
        /// <param name="projectContext">The repository-relative project context.</param>
        /// <param name="primaryEvidenceStableKey">The primary evidence stable key.</param>
        /// <param name="metadata">The deterministic method metadata.</param>
        /// <returns>An architecture method node.</returns>
        private static ArchitectureNode CreateMethodNode(StableKey snapshotStableKey, StableKey stableKey, IMethodSymbol methodSymbol, string projectContext, StableKey primaryEvidenceStableKey, GraphMetadata metadata)
        {
            // Method nodes anchor typed DataSet usage relationships in the source code graph.
            string qualifiedName = methodSymbol.ToDisplayString();
            return new ArchitectureNode(snapshotStableKey, stableKey, NodeKind.Method, methodSymbol.Name, qualifiedName, qualifiedName, "C#", new StableKey($"project://{projectContext}"), null, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Method, methodSymbol.Name, qualifiedName, qualifiedName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a deterministic stable key for a source method.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning snapshot.</param>
        /// <param name="projectContext">The repository-relative project context.</param>
        /// <param name="relativePath">The repository-relative source path.</param>
        /// <param name="methodSymbol">The method symbol to identify.</param>
        /// <returns>A deterministic method stable key.</returns>
        private static StableKey CreateMethodKey(StableKey snapshotStableKey, string projectContext, string relativePath, IMethodSymbol methodSymbol)
        {
            // Snapshot, project, path, and symbol identity keep method stable keys deterministic across machines.
            return new StableKey($"method://{HashStablePayload(snapshotStableKey.Value, projectContext, relativePath, methodSymbol.ToDisplayString())}");
        }

        /// <summary>
        /// Creates deterministic metadata for typed DataSet nodes.
        /// </summary>
        /// <param name="relativePath">The repository-relative XSD path.</param>
        /// <param name="dataSetName">The typed DataSet name.</param>
        /// <param name="hasPartialUnknown">A value indicating whether partial model data was observed.</param>
        /// <returns>Typed DataSet node metadata.</returns>
        private static GraphMetadata CreateDataSetMetadata(string relativePath, string dataSetName, bool hasPartialUnknown)
        {
            // Dataset metadata establishes the model root and explicit unknown reason when table metadata is incomplete.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, dataSetName);
            values["contextType"] = "TypedDataSet";
            values["entityType"] = dataSetName;
            values["dataAccessUnknownReason"] = hasPartialUnknown ? "PartialTypedDataSetModel" : null;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for typed DataTable entity nodes.
        /// </summary>
        /// <param name="relativePath">The repository-relative XSD path.</param>
        /// <param name="dataSetName">The owning typed DataSet name.</param>
        /// <param name="logicalTableName">The logical DataTable name from XSD.</param>
        /// <param name="dataTableClassName">The generated DataTable class name.</param>
        /// <param name="databaseTableName">The parsed database table name.</param>
        /// <returns>Typed DataTable metadata.</returns>
        private static GraphMetadata CreateDataTableMetadata(string relativePath, string dataSetName, string logicalTableName, string dataTableClassName, ParsedDatabaseObjectName databaseTableName)
        {
            // DataTable metadata bridges generated class names to database table identity.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, dataSetName);
            values["entityType"] = dataTableClassName;
            values["tableName"] = databaseTableName.ObjectName;
            values["schemaName"] = databaseTableName.SchemaName;
            values["typedDataTableName"] = logicalTableName;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for database table nodes discovered from typed DataSet artifacts.
        /// </summary>
        /// <param name="relativePath">The repository-relative XSD path.</param>
        /// <param name="dataSetName">The owning typed DataSet name.</param>
        /// <param name="adapterName">The TableAdapter name when table evidence comes from a query.</param>
        /// <param name="tableName">The parsed database table name.</param>
        /// <returns>Database table metadata.</returns>
        private static GraphMetadata CreateTableMetadata(string relativePath, string dataSetName, string? adapterName, ParsedDatabaseObjectName tableName)
        {
            // Table metadata keeps database identity and optional TableAdapter provenance together.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, dataSetName);
            values["detectionMode"] = "TypedDataSetXsd";
            values["schemaName"] = tableName.SchemaName;
            values["tableName"] = tableName.ObjectName;
            values["tableAdapterName"] = adapterName;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for database column nodes discovered from typed DataSet artifacts.
        /// </summary>
        /// <param name="relativePath">The repository-relative XSD path.</param>
        /// <param name="dataSetName">The owning typed DataSet name.</param>
        /// <param name="tableName">The parsed database table name.</param>
        /// <param name="columnName">The database column name.</param>
        /// <returns>Database column metadata.</returns>
        private static GraphMetadata CreateColumnMetadata(string relativePath, string dataSetName, ParsedDatabaseObjectName tableName, string columnName)
        {
            // Column metadata uses the same lower-camel database field names as the other WP009 slices.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, dataSetName);
            values["schemaName"] = tableName.SchemaName;
            values["tableName"] = tableName.ObjectName;
            values["columnName"] = columnName;
            values["propertyName"] = columnName;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for generated TableAdapter artifact nodes.
        /// </summary>
        /// <param name="relativePath">The repository-relative XSD path.</param>
        /// <param name="dataSetName">The owning typed DataSet name.</param>
        /// <param name="adapterName">The TableAdapter class name.</param>
        /// <param name="logicalTableName">The logical DataTable name, if known.</param>
        /// <returns>TableAdapter metadata.</returns>
        private static GraphMetadata CreateAdapterMetadata(string relativePath, string dataSetName, string adapterName, string? logicalTableName)
        {
            // Adapter metadata preserves generated class and logical table context without creating custom node kinds.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, dataSetName);
            values["tableAdapterName"] = adapterName;
            values["typedDataTableName"] = logicalTableName;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for raw SQL nodes from typed DataSet query definitions.
        /// </summary>
        /// <param name="relativePath">The repository-relative XSD path.</param>
        /// <param name="dataSetName">The owning typed DataSet name.</param>
        /// <param name="adapterName">The TableAdapter class name.</param>
        /// <param name="queryName">The query method name.</param>
        /// <param name="commandType">The XSD command type.</param>
        /// <param name="analysis">The SQL analysis result.</param>
        /// <returns>Raw SQL metadata.</returns>
        private static GraphMetadata CreateRawSqlMetadata(string relativePath, string dataSetName, string adapterName, string queryName, string commandType, SqlAnalysisResult analysis)
        {
            // Raw SQL metadata stores only redacted previews and hashes for static command text.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, dataSetName);
            values["commandType"] = commandType;
            values["commandApi"] = "TableAdapter";
            values["tableAdapterName"] = adapterName;
            values["queryName"] = queryName;
            values["sqlPreview"] = analysis.SqlPreview;
            values["sqlTextHash"] = analysis.SqlTextHash;
            values["readWriteHint"] = analysis.ReadWriteHint;
            values["dataAccessUnknownReason"] = analysis.UnknownReason;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for stored procedure nodes discovered from typed DataSet query definitions.
        /// </summary>
        /// <param name="relativePath">The repository-relative XSD path.</param>
        /// <param name="dataSetName">The owning typed DataSet name.</param>
        /// <param name="adapterName">The TableAdapter class name.</param>
        /// <param name="queryName">The query method name.</param>
        /// <param name="commandType">The XSD command type.</param>
        /// <param name="procedureName">The parsed stored procedure name.</param>
        /// <returns>Stored procedure metadata.</returns>
        private static GraphMetadata CreateStoredProcedureMetadata(string relativePath, string dataSetName, string adapterName, string queryName, string commandType, ParsedDatabaseObjectName procedureName)
        {
            // Stored procedure metadata ties generated wrapper methods to database-side procedure identity.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, dataSetName);
            values["commandType"] = commandType;
            values["tableAdapterName"] = adapterName;
            values["queryName"] = queryName;
            values["schemaName"] = procedureName.SchemaName;
            values["storedProcedureName"] = procedureName.ObjectName;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for generated source artifact nodes.
        /// </summary>
        /// <param name="relativePath">The repository-relative generated source path.</param>
        /// <param name="model">The correlated typed DataSet model.</param>
        /// <param name="qualifiedName">The generated source type name.</param>
        /// <param name="generatedArtifactKind">The generated artifact subtype.</param>
        /// <returns>Generated source metadata.</returns>
        private static GraphMetadata CreateGeneratedMetadata(string relativePath, TypedDataSetModel model, string qualifiedName, string generatedArtifactKind)
        {
            // Generated metadata records both the source file and model file so deterministic correlation remains explainable.
            Dictionary<string, object?> values = CreateBaseMetadata(model.ModelFilePath, model.DataSetName);
            values["detectionMode"] = "GeneratedSource";
            values["generatedFilePath"] = relativePath;
            values["generatedArtifactKind"] = generatedArtifactKind;
            values["entityType"] = qualifiedName;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for method nodes that use typed DataSet artifacts.
        /// </summary>
        /// <param name="relativePath">The repository-relative source path.</param>
        /// <param name="projectContext">The repository-relative project context.</param>
        /// <param name="adapterName">The TableAdapter class name.</param>
        /// <returns>Method metadata.</returns>
        private static GraphMetadata CreateUsageMethodMetadata(string relativePath, string projectContext, string adapterName)
        {
            // Usage method metadata identifies the source context without duplicating edge-level query details.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext);
            values["detectionMode"] = "SourceUsage";
            values["modelFilePath"] = relativePath;
            values["tableAdapterName"] = adapterName;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for model and usage relationships.
        /// </summary>
        /// <param name="relationshipKind">The data-access relationship subtype.</param>
        /// <param name="relativePath">The repository-relative evidence path.</param>
        /// <param name="dataSetName">The typed DataSet name.</param>
        /// <param name="adapterName">The TableAdapter class name, if available.</param>
        /// <param name="queryName">The query method name, if available.</param>
        /// <param name="readWriteHint">The read/write hint, if available.</param>
        /// <param name="unknownReason">The unknown reason, if available.</param>
        /// <returns>Relationship metadata.</returns>
        private static GraphMetadata CreateRelationshipMetadata(string relationshipKind, string relativePath, string dataSetName, string? adapterName, string? queryName, string? readWriteHint, string? unknownReason, string? commandType = null)
        {
            // Relationship metadata refines controlled edge kinds without creating typed DataSet-specific edge kinds.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, dataSetName);
            values["dataAccessRelationshipKind"] = relationshipKind;
            values["tableAdapterName"] = adapterName;
            values["queryName"] = queryName;
            values["commandType"] = commandType;
            values["readWriteHint"] = readWriteHint;
            values["dataAccessUnknownReason"] = unknownReason;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for source usage relationships.
        /// </summary>
        /// <param name="relativePath">The repository-relative source path.</param>
        /// <param name="projectContext">The repository-relative project context.</param>
        /// <param name="queryFact">The query fact used by the source method.</param>
        /// <param name="relationshipKind">The data-access relationship subtype.</param>
        /// <returns>Source usage relationship metadata.</returns>
        private static GraphMetadata CreateUsageRelationshipMetadata(string relativePath, string projectContext, TypedDataSetQueryFact queryFact, string relationshipKind)
        {
            // Usage edge metadata explains which generated adapter/query wrapper caused the table or stored-procedure relationship.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext);
            values["detectionMode"] = "SourceUsage";
            values["modelFilePath"] = relativePath;
            values["dataAccessRelationshipKind"] = relationshipKind;
            values["tableAdapterName"] = queryFact.AdapterName;
            values["queryName"] = queryFact.QueryName;
            values["commandType"] = queryFact.CommandType;
            values["readWriteHint"] = queryFact.ReadWriteHint;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates shared lower-camel metadata fields for typed DataSet graph facts.
        /// </summary>
        /// <param name="modelFilePath">The repository-relative model or source path that supplied evidence.</param>
        /// <param name="dataSetName">The typed DataSet name or source project context, depending on fact kind.</param>
        /// <returns>A mutable metadata dictionary with shared typed DataSet fields.</returns>
        private static Dictionary<string, object?> CreateBaseMetadata(string modelFilePath, string dataSetName)
        {
            // Shared metadata keeps typed DataSet facts aligned with WP009 lower-camel naming and source provenance rules.
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["detectionMode"] = "TypedDataSetXsd",
                ["extractor"] = nameof(TypedDataSetExtractor),
                ["modelFilePath"] = modelFilePath,
                ["dataAccessTechnology"] = "TypedDataSet",
                ["framework"] = "TypedDataSet",
                ["contextType"] = dataSetName
            };
        }

        /// <summary>
        /// Removes null metadata values before canonical metadata creation.
        /// </summary>
        /// <param name="values">The metadata dictionary that may contain null values.</param>
        /// <returns>A dictionary with null values removed.</returns>
        private static IReadOnlyDictionary<string, object?> RemoveNullValues(Dictionary<string, object?> values)
        {
            // Omitting absent metadata avoids implying that unresolved generated source or database targets were known.
            return values.Where(static pair => pair.Value is not null).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        }

        /// <summary>
        /// Creates a repository-scoped stable key from a prefix, artifact path, and identity.
        /// </summary>
        /// <param name="prefix">The stable key prefix.</param>
        /// <param name="relativePath">The repository-relative artifact path.</param>
        /// <param name="identity">The artifact-scoped identity.</param>
        /// <returns>A deterministic stable key.</returns>
        private static StableKey CreateScopedKey(string prefix, string relativePath, string identity)
        {
            // Repository-relative paths keep typed DataSet keys deterministic across developer machines.
            return new StableKey($"{prefix}://{RepositoryRelativePath.Parse(relativePath).Value}#{identity}");
        }

        /// <summary>
        /// Hashes stable payload parts with SHA-256.
        /// </summary>
        /// <param name="parts">The stable payload parts to hash.</param>
        /// <returns>A lower-case hexadecimal SHA-256 hash.</returns>
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
        /// <param name="SchemaName">The schema name, using dbo when SQL text is unqualified.</param>
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
        /// Represents an extracted SQL text value from a typed DataSet command definition.
        /// </summary>
        /// <param name="RedactedText">The redacted SQL text when statically available.</param>
        /// <param name="Hash">The stable hash of the redacted SQL text when available.</param>
        /// <param name="IsStatic">A value indicating whether the SQL text was statically available.</param>
        /// <param name="IsDynamic">A value indicating whether the SQL text was computed dynamically.</param>
        private sealed record SqlTextFact(string? RedactedText, string? Hash, bool IsStatic, bool IsDynamic)
        {
            /// <summary>
            /// Gets a SQL text fact for missing command text.
            /// </summary>
            public static SqlTextFact Missing
            {
                get
                {
                    // Missing command text is represented separately from dynamic SQL because XSD command metadata simply omitted it.
                    return new SqlTextFact(null, null, IsStatic: false, IsDynamic: false);
                }
            }
        }

        /// <summary>
        /// Represents the conservative result of typed DataSet command text analysis.
        /// </summary>
        /// <param name="DisplayName">The display name for raw SQL or stored-procedure facts.</param>
        /// <param name="ReadWriteHint">The read/write hint.</param>
        /// <param name="StoredProcedureName">The stored procedure name, if this is a stored procedure command.</param>
        /// <param name="AffectedTables">The affected tables conservatively parsed from SQL text.</param>
        /// <param name="ProcedurePreview">The redacted stored procedure preview.</param>
        /// <param name="SqlPreview">The redacted SQL preview.</param>
        /// <param name="SqlTextHash">The hash of the redacted SQL text.</param>
        /// <param name="UnknownReason">The unknown reason when static analysis cannot resolve command text or impact.</param>
        private sealed record SqlAnalysisResult(string DisplayName, string ReadWriteHint, string? StoredProcedureName, IReadOnlyList<ParsedDatabaseObjectName> AffectedTables, string? ProcedurePreview, string? SqlPreview, string? SqlTextHash, string? UnknownReason);

        /// <summary>
        /// Represents one typed DataSet model and its correlation indexes.
        /// </summary>
        /// <param name="DataSetName">The typed DataSet name.</param>
        /// <param name="ModelFilePath">The repository-relative XSD model path.</param>
        /// <param name="DataSetStableKey">The typed DataSet stable key.</param>
        private sealed class TypedDataSetModel(string DataSetName, string ModelFilePath, StableKey DataSetStableKey)
        {
            /// <summary>
            /// Gets the typed DataSet name.
            /// </summary>
            public string DataSetName { get; } = DataSetName;

            /// <summary>
            /// Gets the repository-relative XSD model path.
            /// </summary>
            public string ModelFilePath { get; } = ModelFilePath;

            /// <summary>
            /// Gets the typed DataSet stable key.
            /// </summary>
            public StableKey DataSetStableKey { get; } = DataSetStableKey;

            /// <summary>
            /// Gets table facts by logical table name.
            /// </summary>
            public Dictionary<string, TypedDataSetTableFact> TablesByTableName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets table facts by generated DataTable class name.
            /// </summary>
            public Dictionary<string, TypedDataSetTableFact> TablesByClassName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets adapter facts by generated TableAdapter class name.
            /// </summary>
            public Dictionary<string, TypedDataSetAdapterFact> AdaptersByName { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Finds a table by logical or generated table name.
            /// </summary>
            /// <param name="tableName">The logical or generated table name to resolve.</param>
            /// <returns>The matching table fact, if one is available.</returns>
            public TypedDataSetTableFact? FindTable(string tableName)
            {
                // Both logical XSD table names and generated class names are used in source and designer artifacts.
                if (TablesByTableName.TryGetValue(tableName, out TypedDataSetTableFact? tableFact))
                {
                    return tableFact;
                }

                return TablesByClassName.TryGetValue(tableName, out tableFact) ? tableFact : null;
            }
        }

        /// <summary>
        /// Represents a typed DataTable and its mapped database table.
        /// </summary>
        /// <param name="TableName">The logical DataTable name from XSD.</param>
        /// <param name="DataTableClassName">The generated DataTable class name.</param>
        /// <param name="DatabaseTableName">The parsed database table name.</param>
        /// <param name="TableStableKey">The database table stable key.</param>
        /// <param name="DataTableStableKey">The generated DataTable entity stable key.</param>
        private sealed record TypedDataSetTableFact(string TableName, string DataTableClassName, ParsedDatabaseObjectName DatabaseTableName, StableKey TableStableKey, StableKey DataTableStableKey);

        /// <summary>
        /// Represents a generated TableAdapter and its XSD query definitions.
        /// </summary>
        /// <param name="AdapterName">The generated TableAdapter class name.</param>
        /// <param name="LogicalTableName">The logical table name, if available.</param>
        /// <param name="TableStableKey">The mapped database table stable key, if available.</param>
        /// <param name="QueriesByName">The query facts by generated method name.</param>
        private sealed record TypedDataSetAdapterFact(string AdapterName, string? LogicalTableName, StableKey? TableStableKey, IReadOnlyDictionary<string, TypedDataSetQueryFact> QueriesByName);

        /// <summary>
        /// Represents a TableAdapter query or stored-procedure wrapper.
        /// </summary>
        /// <param name="QueryName">The generated query method name.</param>
        /// <param name="AdapterName">The generated TableAdapter class name.</param>
        /// <param name="TableStableKey">The mapped database table stable key, if available.</param>
        /// <param name="StoredProcedureStableKey">The stored procedure stable key, if this query wraps a procedure.</param>
        /// <param name="ReadWriteHint">The conservative read/write hint.</param>
        /// <param name="CommandType">The XSD command type.</param>
        private sealed record TypedDataSetQueryFact(string QueryName, string AdapterName, StableKey? TableStableKey, StableKey? StoredProcedureStableKey, string ReadWriteHint, string CommandType);

        /// <summary>
        /// Stores typed DataSet models collected from XSD artifacts for later generated-source and usage correlation.
        /// </summary>
        private sealed class TypedDataSetExtractionState
        {
            /// <summary>
            /// Gets the typed DataSet models in deterministic discovery order.
            /// </summary>
            public List<TypedDataSetModel> Models { get; } = [];

            /// <summary>
            /// Adds one model to the correlation state.
            /// </summary>
            /// <param name="model">The typed DataSet model to add.</param>
            public void AddModel(TypedDataSetModel model)
            {
                // Models are kept in discovery order so ambiguous name-only correlation remains deterministic.
                Models.Add(model);
            }

            /// <summary>
            /// Finds a typed DataSet model by generated type name and likely file relationship.
            /// </summary>
            /// <param name="typeName">The generated type name.</param>
            /// <param name="relativePath">The repository-relative source path.</param>
            /// <returns>The matching model, if one is available.</returns>
            public TypedDataSetModel? FindModel(string typeName, string relativePath)
            {
                // Prefer exact dataset name and related path, then fall back to exact name when only one matching model exists.
                TypedDataSetModel? related = Models.FirstOrDefault(model => string.Equals(model.DataSetName, typeName, StringComparison.Ordinal) && IsRelatedPath(model.ModelFilePath, relativePath));
                if (related is not null)
                {
                    return related;
                }

                return Models.Where(model => string.Equals(model.DataSetName, typeName, StringComparison.Ordinal)).Take(2).Count() == 1
                    ? Models.First(model => string.Equals(model.DataSetName, typeName, StringComparison.Ordinal))
                    : null;
            }
        }
    }
}
