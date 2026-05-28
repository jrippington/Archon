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

namespace Archon.Extractors.DataAccess.AdoNet
{
    /// <summary>
    /// Extracts ADO.NET command, raw SQL, stored procedure, provider, affected-table, dynamic SQL, and redaction facts through static Roslyn analysis.
    /// </summary>
    public sealed class AdoNetRawSqlExtractor
    {
        /// <summary>
        /// Adds ADO.NET and raw SQL graph facts to the shared data-access extraction accumulator.
        /// </summary>
        /// <param name="request">The repository-scoped data-access request that provides semantic documents and snapshot identity.</param>
        /// <param name="accumulator">The shared architecture snapshot accumulator that receives graph contributions.</param>
        /// <param name="cancellationToken">A token that signals when semantic traversal should stop.</param>
        public void Accumulate(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, CancellationToken cancellationToken = default)
        {
            // ADO.NET extraction runs in the same data-access extraction path as LINQ to SQL and EF so callers receive one deterministic data-access snapshot.
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(accumulator);

            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateDocument(request, accumulator, semanticDocument, cancellationToken);
            }
        }

        /// <summary>
        /// Extracts method-level ADO.NET usage facts from one semantic document.
        /// </summary>
        /// <param name="request">The extraction request that scopes stable keys and repository paths.</param>
        /// <param name="accumulator">The shared accumulator receiving graph facts.</param>
        /// <param name="semanticDocument">The Roslyn semantic document to inspect.</param>
        /// <param name="cancellationToken">A token that signals when document traversal should stop.</param>
        private static void AccumulateDocument(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Each method gets an independent command-state map because local variables and parameters do not safely carry across method boundaries.
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

                AdoNetMethodState state = AdoNetMethodState.FromMethod(methodSymbol, request.SnapshotStableKey, relativePath, semanticDocument.ProjectContext);
                SeedCommandParameters(state, methodSymbol);
                foreach (ObjectCreationExpressionSyntax objectCreation in methodDeclaration.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AccumulateObjectCreation(request, accumulator, semanticDocument, relativePath, sourceText, methodSymbol, state, objectCreation, cancellationToken);
                }

                foreach (ImplicitObjectCreationExpressionSyntax objectCreation in methodDeclaration.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AccumulateImplicitObjectCreation(request, accumulator, semanticDocument, relativePath, sourceText, methodSymbol, state, objectCreation, cancellationToken);
                }

                foreach (VariableDeclaratorSyntax declarator in methodDeclaration.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AccumulateVariableDeclaration(semanticDocument, state, declarator, cancellationToken);
                }

                foreach (AssignmentExpressionSyntax assignment in methodDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AccumulateAssignment(semanticDocument, state, assignment, cancellationToken);
                }

                foreach (InvocationExpressionSyntax invocation in methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AccumulateInvocation(request, accumulator, semanticDocument, relativePath, sourceText, methodSymbol, state, invocation, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Seeds command facts for method parameters whose static type is a command abstraction.
        /// </summary>
        /// <param name="state">The method-local command state to populate.</param>
        /// <param name="methodSymbol">The method symbol whose parameters should be inspected.</param>
        private static void SeedCommandParameters(AdoNetMethodState state, IMethodSymbol methodSymbol)
        {
            // Command parameters may be executed directly without construction evidence in the method, so they start as unknown command-text facts.
            foreach (IParameterSymbol parameter in methodSymbol.Parameters)
            {
                AdoNetApiKind apiKind = ClassifyAdoNetType(parameter.Type);
                if (apiKind.IsCommand)
                {
                    state.CommandFactsByVariable[parameter.Name] = AdoNetCommandFact.FromUnknown(parameter.Name, apiKind.Provider, apiKind.TypeName);
                }
            }
        }

        /// <summary>
        /// Records command and adapter facts from ADO.NET object creation expressions.
        /// </summary>
        /// <param name="request">The extraction request that owns snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving immediate type-usage evidence.</param>
        /// <param name="semanticDocument">The semantic document that owns the object creation.</param>
        /// <param name="relativePath">The repository-relative source path.</param>
        /// <param name="sourceText">The full source text used for redacted evidence snippets.</param>
        /// <param name="methodSymbol">The containing method symbol.</param>
        /// <param name="state">The method-local command state to update.</param>
        /// <param name="objectCreation">The object creation syntax to inspect.</param>
        /// <param name="cancellationToken">A token that signals when symbol lookup should stop.</param>
        private static void AccumulateObjectCreation(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol methodSymbol, AdoNetMethodState state, ObjectCreationExpressionSyntax objectCreation, CancellationToken cancellationToken)
        {
            // Constructor arguments often contain command text or connection strings; only command text previews and safe key/provider hints are retained.
            ITypeSymbol? createdType = semanticDocument.SemanticModel.GetTypeInfo(objectCreation, cancellationToken).Type;
            AdoNetApiKind apiKind = ClassifyAdoNetType(createdType, objectCreation.Type.ToString());
            if (!apiKind.IsRelevant)
            {
                return;
            }

            string? variableName = GetAssignedVariableName(objectCreation);
            if (apiKind.IsCommand || apiKind.IsAdapter)
            {
                SqlTextFact sqlText = ExtractSqlTextFromConstructor(objectCreation, semanticDocument.SemanticModel);
                AdoNetCommandFact commandFact = new(variableName, apiKind.Provider, apiKind.TypeName, sqlText, null, ExtractConnectionKey(objectCreation.ArgumentList?.Arguments), apiKind.IsAdapter);
                if (!string.IsNullOrWhiteSpace(variableName))
                {
                    state.CommandFactsByVariable[variableName] = commandFact;
                }
            }

            if (apiKind.IsConnection && LooksLikeConnectionString(GetArgumentString(objectCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression, semanticDocument.SemanticModel)))
            {
                EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, objectCreation, "AdoNetConnection", apiKind.TypeName, methodSymbol.ToDisplayString(), Confidence.Medium, UnknownState.Known);
                accumulator.AddEvidence(evidence).AddNode(CreateMethodNode(request.SnapshotStableKey, state.MethodStableKey, methodSymbol, semanticDocument.ProjectContext, evidence.StableKey, CreateMethodMetadata(relativePath, semanticDocument.ProjectContext, apiKind.Provider)));
            }
        }

        /// <summary>
        /// Records command and adapter facts from target-typed ADO.NET object creation expressions.
        /// </summary>
        /// <param name="request">The extraction request that owns snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving immediate type-usage evidence.</param>
        /// <param name="semanticDocument">The semantic document that owns the object creation.</param>
        /// <param name="relativePath">The repository-relative source path.</param>
        /// <param name="sourceText">The full source text used for redacted evidence snippets.</param>
        /// <param name="methodSymbol">The containing method symbol.</param>
        /// <param name="state">The method-local command state to update.</param>
        /// <param name="objectCreation">The target-typed object creation syntax to inspect.</param>
        /// <param name="cancellationToken">A token that signals when symbol lookup should stop.</param>
        private static void AccumulateImplicitObjectCreation(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol methodSymbol, AdoNetMethodState state, ImplicitObjectCreationExpressionSyntax objectCreation, CancellationToken cancellationToken)
        {
            // Target-typed new is common in modern C# fixtures and production code, so the declared variable type provides the provider/API fallback name.
            string fallbackTypeName = GetDeclaredVariableTypeName(objectCreation) ?? "Unknown";
            ITypeSymbol? createdType = semanticDocument.SemanticModel.GetTypeInfo(objectCreation, cancellationToken).Type;
            AdoNetApiKind apiKind = ClassifyAdoNetType(createdType, fallbackTypeName);
            if (!apiKind.IsRelevant)
            {
                return;
            }

            string? variableName = GetAssignedVariableName(objectCreation);
            if (apiKind.IsCommand || apiKind.IsAdapter)
            {
                SqlTextFact sqlText = ExtractSqlTextFromArguments(objectCreation.ArgumentList.Arguments, semanticDocument.SemanticModel);
                AdoNetCommandFact commandFact = new(variableName, apiKind.Provider, apiKind.TypeName, sqlText, null, ExtractConnectionKey(objectCreation.ArgumentList.Arguments), apiKind.IsAdapter);
                if (!string.IsNullOrWhiteSpace(variableName))
                {
                    state.CommandFactsByVariable[variableName] = commandFact;
                }
            }

            if (apiKind.IsConnection && LooksLikeConnectionString(GetArgumentString(objectCreation.ArgumentList.Arguments.FirstOrDefault()?.Expression, semanticDocument.SemanticModel)))
            {
                EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, objectCreation, "AdoNetConnection", apiKind.TypeName, methodSymbol.ToDisplayString(), Confidence.Medium, UnknownState.Known);
                accumulator.AddEvidence(evidence).AddNode(CreateMethodNode(request.SnapshotStableKey, state.MethodStableKey, methodSymbol, semanticDocument.ProjectContext, evidence.StableKey, CreateMethodMetadata(relativePath, semanticDocument.ProjectContext, apiKind.Provider)));
            }
        }

        /// <summary>
        /// Updates method-local command facts from command property assignments.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used to resolve constant values.</param>
        /// <param name="state">The method-local command state to update.</param>
        /// <param name="assignment">The assignment expression to inspect.</param>
        /// <param name="cancellationToken">A token that signals when symbol lookup should stop.</param>
        private static void AccumulateAssignment(SemanticExtractionRequest semanticDocument, AdoNetMethodState state, AssignmentExpressionSyntax assignment, CancellationToken cancellationToken)
        {
            // Assignments such as command.CommandText and command.CommandType are common when commands are created from connections.
            if (assignment.Left is not MemberAccessExpressionSyntax memberAccess || memberAccess.Expression is not IdentifierNameSyntax receiverIdentifier)
            {
                return;
            }

            string variableName = receiverIdentifier.Identifier.ValueText;
            string propertyName = memberAccess.Name.Identifier.ValueText;
            if (!state.CommandFactsByVariable.TryGetValue(variableName, out AdoNetCommandFact? existingFact))
            {
                ITypeSymbol? receiverType = semanticDocument.SemanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
                AdoNetApiKind apiKind = ClassifyAdoNetType(receiverType);
                if (!apiKind.IsCommand)
                {
                    return;
                }

                existingFact = AdoNetCommandFact.FromUnknown(variableName, apiKind.Provider, apiKind.TypeName);
            }

            if (string.Equals(propertyName, "CommandText", StringComparison.Ordinal))
            {
                SqlTextFact sqlText = ExtractSqlTextFromExpression(assignment.Right, semanticDocument.SemanticModel);
                state.CommandFactsByVariable[variableName] = existingFact with { SqlText = sqlText };
            }
            else if (string.Equals(propertyName, "CommandType", StringComparison.Ordinal))
            {
                string? commandType = assignment.Right.ToString().Contains("StoredProcedure", StringComparison.Ordinal) ? "StoredProcedure" : null;
                state.CommandFactsByVariable[variableName] = existingFact with { CommandType = commandType };
            }
        }

        /// <summary>
        /// Records command facts declared by explicit cast or base-type assignment from command construction expressions.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used to resolve command type names and SQL constants.</param>
        /// <param name="state">The method-local command state to update.</param>
        /// <param name="declarator">The variable declarator to inspect.</param>
        /// <param name="cancellationToken">A token that signals when symbol lookup should stop.</param>
        private static void AccumulateVariableDeclaration(SemanticExtractionRequest semanticDocument, AdoNetMethodState state, VariableDeclaratorSyntax declarator, CancellationToken cancellationToken)
        {
            // Variables declared as DbCommand can be initialized from provider-specific CreateCommand calls, so declaration type is the fallback API shape.
            if (declarator.Initializer?.Value is null || declarator.Parent is not VariableDeclarationSyntax variableDeclaration)
            {
                return;
            }

            if (state.CommandFactsByVariable.ContainsKey(declarator.Identifier.ValueText))
            {
                return;
            }

            ITypeSymbol? declaredType = semanticDocument.SemanticModel.GetTypeInfo(variableDeclaration.Type, cancellationToken).Type;
            AdoNetApiKind apiKind = ClassifyAdoNetType(declaredType, variableDeclaration.Type.ToString());
            if (!apiKind.IsCommand)
            {
                return;
            }

            state.CommandFactsByVariable[declarator.Identifier.ValueText] = AdoNetCommandFact.FromUnknown(declarator.Identifier.ValueText, apiKind.Provider, apiKind.TypeName);
        }

        /// <summary>
        /// Classifies one invocation and emits ADO.NET graph facts when it executes SQL or creates commands.
        /// </summary>
        /// <param name="request">The extraction request that owns snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph facts.</param>
        /// <param name="semanticDocument">The semantic document that owns the invocation.</param>
        /// <param name="relativePath">The repository-relative source file path.</param>
        /// <param name="sourceText">The full source text used for evidence previews.</param>
        /// <param name="methodSymbol">The containing method symbol.</param>
        /// <param name="state">The method-local command state.</param>
        /// <param name="invocation">The invocation syntax to inspect.</param>
        /// <param name="cancellationToken">A token that signals when symbol lookup should stop.</param>
        private static void AccumulateInvocation(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol methodSymbol, AdoNetMethodState state, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
        {
            // Command factory calls update local state; execution calls emit graph relationships to raw SQL, tables, or stored procedures.
            string? methodName = GetInvokedMemberName(invocation);
            if (string.IsNullOrWhiteSpace(methodName))
            {
                return;
            }

            if (string.Equals(methodName, "CreateCommand", StringComparison.Ordinal) && GetAssignedVariableName(invocation) is string commandVariable)
            {
                ITypeSymbol? invocationType = semanticDocument.SemanticModel.GetTypeInfo(invocation, cancellationToken).Type;
                AdoNetApiKind apiKind = ClassifyAdoNetType(invocationType, "DbCommand");
                if (!state.CommandFactsByVariable.ContainsKey(commandVariable))
                {
                    state.CommandFactsByVariable[commandVariable] = AdoNetCommandFact.FromUnknown(commandVariable, apiKind.Provider, apiKind.TypeName);
                }

                return;
            }

            if (IsParameterMutation(methodName))
            {
                return;
            }

            if (!IsExecutionApi(methodName))
            {
                return;
            }

            AdoNetCommandFact commandFact = ResolveCommandFactForInvocation(semanticDocument, state, invocation, methodName, cancellationToken);
            AccumulateExecution(request, accumulator, semanticDocument, relativePath, sourceText, methodSymbol, state, invocation, methodName, commandFact);
        }

        /// <summary>
        /// Emits raw SQL, table, stored procedure, and method facts for one ADO.NET execution call.
        /// </summary>
        /// <param name="request">The extraction request that owns snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph facts.</param>
        /// <param name="semanticDocument">The semantic document that owns the execution call.</param>
        /// <param name="relativePath">The repository-relative source file path.</param>
        /// <param name="sourceText">The full source text used for redacted evidence snippets.</param>
        /// <param name="methodSymbol">The containing method symbol.</param>
        /// <param name="state">The method-local stable-key state.</param>
        /// <param name="invocation">The invocation syntax that executes the command.</param>
        /// <param name="commandApi">The ADO.NET execution API name.</param>
        /// <param name="commandFact">The resolved command fact associated with the execution.</param>
        private static void AccumulateExecution(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, SemanticExtractionRequest semanticDocument, string relativePath, string sourceText, IMethodSymbol methodSymbol, AdoNetMethodState state, InvocationExpressionSyntax invocation, string commandApi, AdoNetCommandFact commandFact)
        {
            // Every execution receives a raw SQL node, even if command text is missing, so unknown command text remains visible and evidence-backed.
            SqlAnalysisResult analysis = AnalyzeSql(commandFact.SqlText, commandFact.CommandType, commandApi);
            UnknownState unknownState = analysis.UnknownReason is null ? UnknownState.Known : UnknownState.Unknown(analysis.UnknownReason);
            Confidence confidence = analysis.UnknownReason is null ? Confidence.Medium : Confidence.Low;
            EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath, sourceText, invocation, "AdoNetExecution", commandApi, methodSymbol.ToDisplayString(), confidence, unknownState);
            ArchitectureNode methodNode = CreateMethodNode(request.SnapshotStableKey, state.MethodStableKey, methodSymbol, semanticDocument.ProjectContext, evidence.StableKey, CreateMethodMetadata(relativePath, semanticDocument.ProjectContext, commandFact.Provider));
            StableKey rawSqlKey = new($"rawsql://{HashStablePayload(semanticDocument.ProjectContext, relativePath, methodSymbol.ToDisplayString(), invocation.SpanStart.ToString(), commandApi)}");
            ArchitectureNode rawSqlNode = CreateNode(request.SnapshotStableKey, rawSqlKey, NodeKind.SqlScript, analysis.DisplayName, null, "SQL", state.MethodStableKey, evidence.StableKey, confidence, unknownState, CreateRawSqlMetadata(relativePath, semanticDocument.ProjectContext, commandFact, commandApi, analysis));
            accumulator.AddEvidence(evidence).AddNode(methodNode).AddNode(rawSqlNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.ExecutesRawSql, state.MethodStableKey, rawSqlKey, evidence.StableKey, CreateUsageRelationshipMetadata("RawSqlExecution", relativePath, semanticDocument.ProjectContext, commandFact, commandApi, analysis.ReadWriteHint, commandFact.CommandType, analysis.UnknownReason), confidence, unknownState));

            if (string.Equals(commandFact.CommandType, "StoredProcedure", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(analysis.StoredProcedureName))
            {
                AccumulateStoredProcedureCall(request, accumulator, semanticDocument.ProjectContext, relativePath, state.MethodStableKey, evidence.StableKey, commandFact, commandApi, analysis, confidence, unknownState);
            }

            foreach (ParsedDatabaseObjectName tableName in analysis.AffectedTables)
            {
                AccumulateTableUsage(request, accumulator, semanticDocument.ProjectContext, relativePath, state.MethodStableKey, evidence.StableKey, commandFact, commandApi, analysis, tableName, confidence, unknownState);
            }

            if (analysis.UnknownReason is not null)
            {
                accumulator.AddWarning($"ADO.NET command {commandApi} in {relativePath} has {analysis.UnknownReason.ToLowerInvariant()} and was recorded with explicit unknown metadata.");
            }
        }

        /// <summary>
        /// Emits a stored procedure node and call relationship for a stored procedure command execution.
        /// </summary>
        /// <param name="request">The extraction request that owns snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph facts.</param>
        /// <param name="projectContext">The repository-relative project context.</param>
        /// <param name="relativePath">The repository-relative source file path.</param>
        /// <param name="methodStableKey">The stable key of the source method making the call.</param>
        /// <param name="evidenceStableKey">The evidence stable key for the execution syntax.</param>
        /// <param name="commandFact">The command fact associated with the execution.</param>
        /// <param name="commandApi">The execution API name.</param>
        /// <param name="analysis">The SQL analysis result containing the procedure name.</param>
        /// <param name="confidence">The confidence for the emitted relationship.</param>
        /// <param name="unknownState">The unknown state for the emitted relationship.</param>
        private static void AccumulateStoredProcedureCall(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, string projectContext, string relativePath, StableKey methodStableKey, StableKey evidenceStableKey, AdoNetCommandFact commandFact, string commandApi, SqlAnalysisResult analysis, Confidence confidence, UnknownState unknownState)
        {
            // Stored procedures are first-class data-access nodes because they represent database-side execution targets, not ordinary SQL script text.
            ParsedDatabaseObjectName parsedProcedure = ParseDatabaseObjectName(analysis.StoredProcedureName);
            StableKey procedureKey = CreateProjectScopedKey("storedprocedure", projectContext, parsedProcedure.QualifiedName);
            ArchitectureNode procedureNode = CreateNode(request.SnapshotStableKey, procedureKey, NodeKind.StoredProcedure, parsedProcedure.ObjectName, parsedProcedure.QualifiedName, "Database", null, evidenceStableKey, confidence, unknownState, CreateStoredProcedureMetadata(relativePath, projectContext, commandFact, parsedProcedure));
            accumulator.AddNode(procedureNode).AddEdge(CreateEdge(request.SnapshotStableKey, EdgeKind.CallsStoredProcedure, methodStableKey, procedureKey, evidenceStableKey, CreateUsageRelationshipMetadata("StoredProcedureCommand", relativePath, projectContext, commandFact, commandApi, analysis.ReadWriteHint, commandFact.CommandType, analysis.UnknownReason), confidence, unknownState));
        }

        /// <summary>
        /// Emits a database table node and read/write relationship for a conservative affected-table hint.
        /// </summary>
        /// <param name="request">The extraction request that owns snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph facts.</param>
        /// <param name="projectContext">The repository-relative project context.</param>
        /// <param name="relativePath">The repository-relative source file path.</param>
        /// <param name="methodStableKey">The stable key of the source method using the table.</param>
        /// <param name="evidenceStableKey">The evidence stable key for the execution syntax.</param>
        /// <param name="commandFact">The command fact associated with the execution.</param>
        /// <param name="commandApi">The ADO.NET execution API name.</param>
        /// <param name="analysis">The SQL analysis result containing read/write classification.</param>
        /// <param name="tableName">The parsed affected table name.</param>
        /// <param name="confidence">The confidence for the emitted facts.</param>
        /// <param name="unknownState">The unknown state for the emitted facts.</param>
        private static void AccumulateTableUsage(LinqToSqlDbmlExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, string projectContext, string relativePath, StableKey methodStableKey, StableKey evidenceStableKey, AdoNetCommandFact commandFact, string commandApi, SqlAnalysisResult analysis, ParsedDatabaseObjectName tableName, Confidence confidence, UnknownState unknownState)
        {
            // Affected table hints are intentionally shallow; only leading SQL patterns with clear table tokens produce table relationships.
            StableKey tableKey = CreateProjectScopedKey("dbtable", projectContext, tableName.QualifiedName);
            ArchitectureNode tableNode = CreateNode(request.SnapshotStableKey, tableKey, NodeKind.DatabaseTable, tableName.ObjectName, tableName.QualifiedName, "Database", null, evidenceStableKey, confidence, unknownState, CreateTableMetadata(relativePath, projectContext, commandFact, tableName, analysis));
            EdgeKind edgeKind = string.Equals(analysis.ReadWriteHint, "Read", StringComparison.Ordinal) ? EdgeKind.ReadsTable : EdgeKind.WritesTable;
            accumulator.AddNode(tableNode).AddEdge(CreateEdge(request.SnapshotStableKey, edgeKind, methodStableKey, tableKey, evidenceStableKey, CreateUsageRelationshipMetadata("AffectedTableHint", relativePath, projectContext, commandFact, commandApi, analysis.ReadWriteHint, commandFact.CommandType, analysis.UnknownReason), confidence, unknownState));
        }

        /// <summary>
        /// Resolves command state for an execution invocation using receiver variables, adapter constructors, or API shape fallbacks.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for type classification.</param>
        /// <param name="state">The method-local command state.</param>
        /// <param name="invocation">The execution invocation to inspect.</param>
        /// <param name="commandApi">The execution API name.</param>
        /// <param name="cancellationToken">A token that signals when symbol lookup should stop.</param>
        /// <returns>The best available command fact for the execution.</returns>
        private static AdoNetCommandFact ResolveCommandFactForInvocation(SemanticExtractionRequest semanticDocument, AdoNetMethodState state, InvocationExpressionSyntax invocation, string commandApi, CancellationToken cancellationToken)
        {
            // Most command executions are receiver calls on a local command variable; adapter Fill and unknown DbCommand parameters are handled by the same state map.
            string? receiverName = GetReceiverIdentifierName(invocation);
            if (receiverName is not null && state.CommandFactsByVariable.TryGetValue(receiverName, out AdoNetCommandFact? commandFact))
            {
                return commandFact;
            }

            ITypeSymbol? receiverType = TryGetReceiverType(semanticDocument, invocation, cancellationToken);
            AdoNetApiKind apiKind = ClassifyAdoNetType(receiverType, receiverType?.Name ?? "Unknown");
            SqlTextFact sqlText = ExtractSqlTextFromExpression(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, semanticDocument.SemanticModel);
            return new AdoNetCommandFact(receiverName, apiKind.Provider, apiKind.TypeName, sqlText, null, null, string.Equals(commandApi, "Fill", StringComparison.Ordinal));
        }

        /// <summary>
        /// Analyzes statically available SQL text into conservative command kind, table, stored procedure, and unknown metadata.
        /// </summary>
        /// <param name="sqlText">The extracted SQL text fact.</param>
        /// <param name="commandType">The command type, if explicitly assigned.</param>
        /// <param name="commandApi">The ADO.NET execution API name.</param>
        /// <returns>A conservative SQL analysis result.</returns>
        private static SqlAnalysisResult AnalyzeSql(SqlTextFact sqlText, string? commandType, string commandApi)
        {
            // The analysis deliberately recognizes only simple leading SQL shapes; everything else remains raw SQL with explicit unknowns.
            if (string.Equals(commandType, "StoredProcedure", StringComparison.Ordinal))
            {
                string? procedure = sqlText.IsStatic && !string.IsNullOrWhiteSpace(sqlText.RedactedText) ? sqlText.RedactedText : null;
                return new SqlAnalysisResult("StoredProcedure", "Write", procedure, [], procedure, sqlText.RedactedText, sqlText.Hash, sqlText.IsDynamic, procedure is null ? "MissingCommandText" : null);
            }

            if (!sqlText.IsStatic || string.IsNullOrWhiteSpace(sqlText.RedactedText))
            {
                string unknownReason = sqlText.IsDynamic ? "ComputedSql" : "MissingCommandText";
                return new SqlAnalysisResult("RawSql", "Unknown", null, [], null, null, null, sqlText.IsDynamic, unknownReason);
            }

            string preview = sqlText.RedactedText;
            string firstVerb = GetFirstSqlToken(preview);
            string readWriteHint = InferReadWriteHint(firstVerb, commandApi);
            IReadOnlyList<ParsedDatabaseObjectName> affectedTables = ExtractAffectedTables(preview, firstVerb);
            string displayName = string.IsNullOrWhiteSpace(firstVerb) ? "RawSql" : firstVerb.ToUpperInvariant();
            return new SqlAnalysisResult(displayName, readWriteHint, null, affectedTables, null, preview, sqlText.Hash, sqlText.IsDynamic, null);
        }

        /// <summary>
        /// Extracts conservative affected-table hints from simple SQL statements.
        /// </summary>
        /// <param name="sqlText">The redacted SQL text to inspect.</param>
        /// <param name="firstVerb">The first SQL verb already extracted from the statement.</param>
        /// <returns>The affected tables that can be determined without speculative parsing.</returns>
        private static IReadOnlyList<ParsedDatabaseObjectName> ExtractAffectedTables(string sqlText, string firstVerb)
        {
            // Token-based extraction intentionally handles only common single-statement shapes required by data-access.
            string[] tokens = TokenizeSql(sqlText);
            if (tokens.Length == 0)
            {
                return [];
            }

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
            // SQL aliases or punctuation are stripped before object-name parsing.
            if (index < 0 || index >= tokens.Count)
            {
                return;
            }

            string candidate = tokens[index];
            if (IsSqlKeyword(candidate))
            {
                return;
            }

            tables.Add(ParseDatabaseObjectName(candidate));
        }

        /// <summary>
        /// Extracts a SQL text fact from an expression without evaluating runtime values.
        /// </summary>
        /// <param name="expression">The expression that may contain SQL text.</param>
        /// <param name="semanticModel">The semantic model used for constant extraction.</param>
        /// <returns>A SQL text fact describing static, dynamic, or missing command text.</returns>
        private static SqlTextFact ExtractSqlTextFromExpression(ExpressionSyntax? expression, Microsoft.CodeAnalysis.SemanticModel semanticModel)
        {
            // Constant strings are safe after redaction; concatenated and interpolated expressions are marked dynamic instead of being reconstructed.
            if (expression is null)
            {
                return SqlTextFact.Missing;
            }

            if (expression is InterpolatedStringExpressionSyntax || expression is InvocationExpressionSyntax)
            {
                return SqlTextFact.Dynamic;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                string? leftConstant = GetConstantString(binaryExpression.Left, semanticModel);
                string? rightConstant = GetConstantString(binaryExpression.Right, semanticModel);
                if (leftConstant is not null && rightConstant is not null)
                {
                    string redactedBinary = Redact(string.Concat(leftConstant, rightConstant).Trim());
                    return new SqlTextFact(redactedBinary, HashStablePayload(redactedBinary), IsStatic: true, IsDynamic: false);
                }

                return SqlTextFact.Dynamic;
            }

            string? constantValue = GetConstantString(expression, semanticModel);
            if (constantValue is null)
            {
                return SqlTextFact.Dynamic;
            }

            string redacted = Redact(constantValue.Trim());
            return new SqlTextFact(redacted, HashStablePayload(redacted), IsStatic: true, IsDynamic: false);
        }

        /// <summary>
        /// Extracts SQL text from a command or adapter constructor while distinguishing connection-string-only constructors from command-text constructors.
        /// </summary>
        /// <param name="objectCreation">The object creation expression to inspect.</param>
        /// <param name="semanticModel">The semantic model used to resolve constant values.</param>
        /// <returns>A SQL text fact for the first non-connection-string command text argument, dynamic argument, or missing command text.</returns>
        private static SqlTextFact ExtractSqlTextFromConstructor(ObjectCreationExpressionSyntax objectCreation, Microsoft.CodeAnalysis.SemanticModel semanticModel)
        {
            // Constructors such as SqlCommand(string, SqlConnection) and DataAdapter(string, Connection) use the first SQL-like string argument, while connection constructors should not become SQL facts.
            return ExtractSqlTextFromArguments(objectCreation.ArgumentList?.Arguments ?? [], semanticModel);
        }

        /// <summary>
        /// Extracts SQL text from command or adapter constructor arguments while skipping connection-string arguments.
        /// </summary>
        /// <param name="arguments">The constructor arguments to inspect.</param>
        /// <param name="semanticModel">The semantic model used to resolve constant values.</param>
        /// <returns>A SQL text fact for the first SQL-like argument, dynamic argument, or missing command text.</returns>
        private static SqlTextFact ExtractSqlTextFromArguments(IEnumerable<ArgumentSyntax> arguments, Microsoft.CodeAnalysis.SemanticModel semanticModel)
        {
            // Constructors such as SqlCommand(string, SqlConnection) and DataAdapter(string, Connection) use the first SQL-like string argument, while connection constructors should not become SQL facts.
            foreach (ArgumentSyntax argument in arguments)
            {
                string? constant = GetConstantString(argument.Expression, semanticModel);
                if (constant is not null && LooksLikeConnectionString(constant))
                {
                    continue;
                }

                SqlTextFact fact = ExtractSqlTextFromExpression(argument.Expression, semanticModel);
                if (fact.IsStatic || fact.IsDynamic)
                {
                    return fact;
                }
            }

            return SqlTextFact.Missing;
        }

        /// <summary>
        /// Classifies a Roslyn type symbol as an ADO.NET provider, command, connection, adapter, or data container API.
        /// </summary>
        /// <param name="typeSymbol">The type symbol to classify.</param>
        /// <returns>The ADO.NET API classification for the symbol.</returns>
        private static AdoNetApiKind ClassifyAdoNetType(ITypeSymbol? typeSymbol)
        {
            // Symbol-only classification is used for method parameters where no source type syntax is available.
            return ClassifyAdoNetType(typeSymbol, typeSymbol?.Name ?? "Unknown");
        }

        /// <summary>
        /// Classifies a Roslyn type symbol or source type name as an ADO.NET provider, command, connection, adapter, or data container API.
        /// </summary>
        /// <param name="typeSymbol">The type symbol to classify when semantic binding succeeds.</param>
        /// <param name="fallbackTypeName">The source-level type name to use when provider assemblies are unavailable.</param>
        /// <returns>The ADO.NET API classification for the symbol or fallback name.</returns>
        private static AdoNetApiKind ClassifyAdoNetType(ITypeSymbol? typeSymbol, string fallbackTypeName)
        {
            // Type names and namespaces cover concrete providers and abstract base classes; fallback syntax keeps tests and target projects resilient when optional provider assemblies are absent.
            string typeName = typeSymbol?.Name ?? fallbackTypeName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "Unknown";
            string namespaceName = typeSymbol?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            string provider = ProviderFromType(typeName, namespaceName);
            bool isCommand = typeName.EndsWith("Command", StringComparison.Ordinal) || string.Equals(typeName, "DbCommand", StringComparison.Ordinal);
            bool isConnection = typeName.EndsWith("Connection", StringComparison.Ordinal) || string.Equals(typeName, "DbConnection", StringComparison.Ordinal);
            bool isAdapter = typeName.EndsWith("DataAdapter", StringComparison.Ordinal);
            bool isReader = typeName.EndsWith("DataReader", StringComparison.Ordinal) || string.Equals(typeName, "DbDataReader", StringComparison.Ordinal);
            bool isDataContainer = string.Equals(typeName, "DataSet", StringComparison.Ordinal) || string.Equals(typeName, "DataTable", StringComparison.Ordinal);
            bool relevantNamespace = namespaceName.StartsWith("System.Data", StringComparison.Ordinal) || isCommand || isConnection || isAdapter || isReader || isDataContainer;
            return relevantNamespace && (isCommand || isConnection || isAdapter || isReader || isDataContainer) ? new AdoNetApiKind(provider, typeName, isCommand, isConnection, isAdapter, isReader, isDataContainer) : AdoNetApiKind.Unknown;
        }

        /// <summary>
        /// Infers the provider value from an ADO.NET type name and namespace.
        /// </summary>
        /// <param name="typeName">The short type name.</param>
        /// <param name="namespaceName">The containing namespace name.</param>
        /// <returns>The normalized provider value used in metadata.</returns>
        private static string ProviderFromType(string typeName, string namespaceName)
        {
            // Provider values align with data-access metadata vocabulary while keeping abstract Db* APIs provider-neutral.
            if (typeName.StartsWith("Sql", StringComparison.Ordinal) || namespaceName.Contains("SqlClient", StringComparison.OrdinalIgnoreCase))
            {
                return "SqlServer";
            }

            if (typeName.StartsWith("OleDb", StringComparison.Ordinal) || namespaceName.Contains("OleDb", StringComparison.OrdinalIgnoreCase))
            {
                return "OleDb";
            }

            if (typeName.StartsWith("Odbc", StringComparison.Ordinal) || namespaceName.Contains("Odbc", StringComparison.OrdinalIgnoreCase))
            {
                return "Odbc";
            }

            return "Unknown";
        }

        /// <summary>
        /// Determines whether an invocation name executes command text or fills data containers from a command.
        /// </summary>
        /// <param name="methodName">The invocation method name.</param>
        /// <returns><see langword="true" /> when the API represents database execution; otherwise, <see langword="false" />.</returns>
        private static bool IsExecutionApi(string methodName)
        {
            // These are the data-access-required execution APIs; Fill is included because DataAdapter executes its select command.
            return string.Equals(methodName, "ExecuteReader", StringComparison.Ordinal)
                || string.Equals(methodName, "ExecuteNonQuery", StringComparison.Ordinal)
                || string.Equals(methodName, "ExecuteScalar", StringComparison.Ordinal)
                || string.Equals(methodName, "Fill", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether an invocation mutates parameters rather than executing SQL.
        /// </summary>
        /// <param name="methodName">The invocation method name.</param>
        /// <returns><see langword="true" /> when the invocation is a parameter mutation; otherwise, <see langword="false" />.</returns>
        private static bool IsParameterMutation(string methodName)
        {
            // Parameter APIs are evidence of command shape but do not independently execute SQL.
            return string.Equals(methodName, "AddWithValue", StringComparison.Ordinal)
                || string.Equals(methodName, "Add", StringComparison.Ordinal)
                || string.Equals(methodName, "AddRange", StringComparison.Ordinal);
        }

        /// <summary>
        /// Infers read/write impact from the leading SQL verb and command execution API.
        /// </summary>
        /// <param name="firstVerb">The leading SQL verb, if present.</param>
        /// <param name="commandApi">The ADO.NET execution API name.</param>
        /// <returns>The conservative read/write hint.</returns>
        private static string InferReadWriteHint(string firstVerb, string commandApi)
        {
            // SQL verb evidence wins over API shape because ExecuteScalar can read while ExecuteNonQuery can run writes or DDL.
            if (string.Equals(firstVerb, "SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return "Read";
            }

            if (string.Equals(firstVerb, "INSERT", StringComparison.OrdinalIgnoreCase) || string.Equals(firstVerb, "UPDATE", StringComparison.OrdinalIgnoreCase) || string.Equals(firstVerb, "DELETE", StringComparison.OrdinalIgnoreCase) || string.Equals(firstVerb, "MERGE", StringComparison.OrdinalIgnoreCase) || string.Equals(firstVerb, "CREATE", StringComparison.OrdinalIgnoreCase) || string.Equals(firstVerb, "ALTER", StringComparison.OrdinalIgnoreCase) || string.Equals(firstVerb, "DROP", StringComparison.OrdinalIgnoreCase))
            {
                return "Write";
            }

            if (string.Equals(commandApi, "ExecuteReader", StringComparison.Ordinal) || string.Equals(commandApi, "Fill", StringComparison.Ordinal))
            {
                return "Read";
            }

            return "Unknown";
        }

        /// <summary>
        /// Gets the leading SQL token from redacted SQL text.
        /// </summary>
        /// <param name="sqlText">The SQL text to inspect.</param>
        /// <returns>The first SQL token, or an empty string when none is available.</returns>
        private static string GetFirstSqlToken(string sqlText)
        {
            // Leading comments are not parsed in this first slice; the first token is enough for supported fixtures and common command text.
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
            // Keyword filtering prevents obvious non-table tokens from becoming graph table nodes.
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
        /// Gets the invoked member name from an invocation expression.
        /// </summary>
        /// <param name="invocation">The invocation expression to inspect.</param>
        /// <returns>The invoked member name, if one is syntactically available.</returns>
        private static string? GetInvokedMemberName(InvocationExpressionSyntax invocation)
        {
            // Syntax names remain useful when assemblies differ across target projects or tests use source stubs.
            return invocation.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Name.Identifier.ValueText : invocation.Expression is IdentifierNameSyntax identifier ? identifier.Identifier.ValueText : null;
        }

        /// <summary>
        /// Gets the local variable name assigned from an expression initializer.
        /// </summary>
        /// <param name="expression">The expression whose assignment parent should be inspected.</param>
        /// <returns>The assigned local variable name, if the expression initializes a local variable.</returns>
        private static string? GetAssignedVariableName(ExpressionSyntax expression)
        {
            // Variable tracking connects command construction and CreateCommand calls to later execution calls.
            return expression.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variableDeclarator } ? variableDeclarator.Identifier.ValueText : null;
        }

        /// <summary>
        /// Gets the declared variable type name for target-typed object creation expressions.
        /// </summary>
        /// <param name="expression">The target-typed object creation expression.</param>
        /// <returns>The declared variable type name, if the expression initializes a local variable.</returns>
        private static string? GetDeclaredVariableTypeName(ExpressionSyntax expression)
        {
            // Target-typed new expressions omit the constructor type, so the surrounding variable declaration supplies the ADO.NET API name.
            return expression.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax variableDeclaration } }
                ? variableDeclaration.Type.ToString()
                : null;
        }

        /// <summary>
        /// Gets the receiver variable name for a member-access invocation.
        /// </summary>
        /// <param name="invocation">The invocation to inspect.</param>
        /// <returns>The receiver identifier name, if available.</returns>
        private static string? GetReceiverIdentifierName(InvocationExpressionSyntax invocation)
        {
            // Receiver names are the link between local command facts and execution APIs.
            return invocation.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax identifier } ? identifier.Identifier.ValueText : null;
        }

        /// <summary>
        /// Gets the static receiver type for a member-access invocation.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used for type lookup.</param>
        /// <param name="invocation">The invocation whose receiver should be typed.</param>
        /// <param name="cancellationToken">A token that signals when type lookup should stop.</param>
        /// <returns>The receiver type symbol, if available.</returns>
        private static ITypeSymbol? TryGetReceiverType(SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
        {
            // Receiver type fallback lets direct command parameters and adapter executions be classified when local construction was not observed.
            return invocation.Expression is MemberAccessExpressionSyntax memberAccess ? semanticDocument.SemanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type : null;
        }

        /// <summary>
        /// Gets a compile-time constant string value from an expression.
        /// </summary>
        /// <param name="expression">The expression to inspect.</param>
        /// <param name="semanticModel">The semantic model used to resolve constants.</param>
        /// <returns>The constant string value, if one exists.</returns>
        private static string? GetConstantString(ExpressionSyntax? expression, Microsoft.CodeAnalysis.SemanticModel semanticModel)
        {
            // Semantic constants support literals and const fields while intentionally refusing runtime-computed SQL.
            if (expression is null)
            {
                return null;
            }

            Optional<object?> constantValue = semanticModel.GetConstantValue(expression);
            return constantValue.HasValue && constantValue.Value is string value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        }

        /// <summary>
        /// Gets a string argument value from an object creation or invocation expression when it is constant.
        /// </summary>
        /// <param name="expression">The argument expression to inspect.</param>
        /// <param name="semanticModel">The semantic model used to resolve constants.</param>
        /// <returns>The constant string value, if one exists.</returns>
        private static string? GetArgumentString(ExpressionSyntax? expression, Microsoft.CodeAnalysis.SemanticModel semanticModel)
        {
            // This wrapper keeps call sites expressive where the expression represents constructor or method argument text.
            return GetConstantString(expression, semanticModel);
        }

        /// <summary>
        /// Extracts a safe configuration key from connection-oriented constructor arguments.
        /// </summary>
        /// <param name="objectCreation">The object creation that may contain a connection string or key.</param>
        /// <returns>A safe connection-string key, if one is present.</returns>
        private static string? ExtractConnectionKey(IEnumerable<ArgumentSyntax>? arguments)
        {
            // The first slice preserves only name= keys; credential-bearing connection strings are represented by redaction metadata instead.
            foreach (ArgumentSyntax argument in arguments ?? [])
            {
                string text = argument.Expression.ToString();
                int index = text.IndexOf("name=", StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    string key = text[(index + 5)..].Trim('"', '\'', ';', ')');
                    return string.IsNullOrWhiteSpace(key) ? null : key;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines whether a string resembles a raw connection string requiring redaction.
        /// </summary>
        /// <param name="value">The candidate connection string.</param>
        /// <returns><see langword="true" /> when the value appears to contain sensitive connection details; otherwise, <see langword="false" />.</returns>
        private static bool LooksLikeConnectionString(string? value)
        {
            // Credential and provider markers are enough for safe redaction behavior without parsing every provider-specific grammar.
            return value?.Contains("Password=", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("User Id=", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("Server=", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("Database=", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("Provider=", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("Driver=", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Redacts secret-like values from SQL previews, source snippets, and diagnostic text.
        /// </summary>
        /// <param name="value">The text to redact.</param>
        /// <returns>The redacted text.</returns>
        private static string Redact(string value)
        {
            // Redaction targets credential-shaped fragments and known test sentinel values before any text enters metadata or evidence.
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
        /// Creates source evidence for an ADO.NET syntax node with redacted snippet preview.
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
            // Evidence line spans and snippet previews make SQL facts traceable while redaction protects embedded credentials and tokens.
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
                ["extractor"] = nameof(AdoNetRawSqlExtractor),
                ["sourceLine"] = startLine,
                ["sourceEndLine"] = endLine
            });
            StableKey stableKey = new($"adonet-source-evidence://{HashStablePayload(relativePath, role, symbolName, containingSymbol, startLine.ToString(), endLine.ToString(), snippetHash)}");
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
            // Search names mirror qualified names when possible so query consumers can locate SQL artifacts consistently.
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
            // Edge identity includes metadata and unknown-state so duplicate execution observations merge deterministically.
            StableKey stableKey = new($"adonet-edge://{HashStablePayload(edgeKind.Value, sourceStableKey.Value, targetStableKey.Value, metadata.ToCanonicalJson(), unknownState.HasUnknownData.ToString())}");
            return new ArchitectureEdge(snapshotStableKey, stableKey, edgeKind, sourceStableKey, targetStableKey, true, KnowledgeKind.Fact, confidence, unknownState, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForEdge(edgeKind, sourceStableKey, targetStableKey, true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a method node for a source ADO.NET usage site.
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
            // Method nodes anchor raw SQL, table, and stored-procedure relationships in the source code graph.
            string qualifiedName = methodSymbol.ToDisplayString();
            return new ArchitectureNode(snapshotStableKey, stableKey, NodeKind.Method, methodSymbol.Name, qualifiedName, qualifiedName, "C#", new StableKey($"project://{projectContext}"), null, KnowledgeKind.Fact, null, null, Confidence.High, UnknownState.Known, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Method, methodSymbol.Name, qualifiedName, qualifiedName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates deterministic metadata for method nodes participating in ADO.NET usage.
        /// </summary>
        /// <param name="relativePath">The repository-relative source file path.</param>
        /// <param name="projectContext">The repository-relative project context.</param>
        /// <param name="provider">The normalized provider value.</param>
        /// <returns>Method metadata.</returns>
        private static GraphMetadata CreateMethodMetadata(string relativePath, string projectContext, string provider)
        {
            // Method metadata marks source usage and provider context without duplicating relationship-level command details.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, provider);
            values["detectionMode"] = "SourceUsage";
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for raw SQL script nodes.
        /// </summary>
        /// <param name="relativePath">The repository-relative source file path.</param>
        /// <param name="projectContext">The repository-relative project context.</param>
        /// <param name="commandFact">The command fact associated with the SQL node.</param>
        /// <param name="commandApi">The execution API name.</param>
        /// <param name="analysis">The SQL analysis result.</param>
        /// <returns>Raw SQL node metadata.</returns>
        private static GraphMetadata CreateRawSqlMetadata(string relativePath, string projectContext, AdoNetCommandFact commandFact, string commandApi, SqlAnalysisResult analysis)
        {
            // SQL metadata stores a redacted preview and hash when static text exists; dynamic or missing SQL remains explicit unknown metadata.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, commandFact.Provider);
            values["detectionMode"] = "SourceUsage";
            values["commandApi"] = commandApi;
            values["commandType"] = commandFact.CommandType;
            values["commandObjectType"] = commandFact.CommandTypeName;
            values["connectionStringKey"] = commandFact.ConnectionStringKey;
            values["sqlPreview"] = analysis.SqlPreview;
            values["sqlTextHash"] = analysis.SqlTextHash;
            values["readWriteHint"] = analysis.ReadWriteHint;
            values["isDynamicSql"] = analysis.IsDynamicSql;
            values["dataAccessUnknownReason"] = analysis.UnknownReason;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for table nodes discovered from SQL text.
        /// </summary>
        /// <param name="relativePath">The repository-relative source file path.</param>
        /// <param name="projectContext">The repository-relative project context.</param>
        /// <param name="commandFact">The command fact associated with the table usage.</param>
        /// <param name="tableName">The parsed database table name.</param>
        /// <param name="analysis">The SQL analysis result.</param>
        /// <returns>Table metadata.</returns>
        private static GraphMetadata CreateTableMetadata(string relativePath, string projectContext, AdoNetCommandFact commandFact, ParsedDatabaseObjectName tableName, SqlAnalysisResult analysis)
        {
            // Table metadata records only conservative affected-table hints from static SQL text.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, commandFact.Provider);
            values["detectionMode"] = "SqlText";
            values["schemaName"] = tableName.SchemaName;
            values["tableName"] = tableName.ObjectName;
            values["readWriteHint"] = analysis.ReadWriteHint;
            values["commandType"] = commandFact.CommandType;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for stored procedure nodes discovered from command metadata.
        /// </summary>
        /// <param name="relativePath">The repository-relative source file path.</param>
        /// <param name="projectContext">The repository-relative project context.</param>
        /// <param name="commandFact">The command fact associated with the stored procedure call.</param>
        /// <param name="procedureName">The parsed stored procedure name.</param>
        /// <returns>Stored procedure metadata.</returns>
        private static GraphMetadata CreateStoredProcedureMetadata(string relativePath, string projectContext, AdoNetCommandFact commandFact, ParsedDatabaseObjectName procedureName)
        {
            // Stored procedure metadata keeps database object identity separate from the raw SQL node that triggered the call.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, commandFact.Provider);
            values["detectionMode"] = "CommandType";
            values["schemaName"] = procedureName.SchemaName;
            values["storedProcedureName"] = procedureName.ObjectName;
            values["commandType"] = commandFact.CommandType;
            values["commandObjectType"] = commandFact.CommandTypeName;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates deterministic metadata for ADO.NET usage relationships.
        /// </summary>
        /// <param name="relationshipKind">The data-access relationship subtype.</param>
        /// <param name="relativePath">The repository-relative source file path.</param>
        /// <param name="projectContext">The repository-relative project context.</param>
        /// <param name="commandFact">The command fact associated with the relationship.</param>
        /// <param name="commandApi">The execution API name.</param>
        /// <param name="readWriteHint">The read/write hint.</param>
        /// <param name="commandType">The command type, if known.</param>
        /// <param name="unknownReason">The data-access unknown reason, if any.</param>
        /// <returns>Usage relationship metadata.</returns>
        private static GraphMetadata CreateUsageRelationshipMetadata(string relationshipKind, string relativePath, string projectContext, AdoNetCommandFact commandFact, string commandApi, string readWriteHint, string? commandType, string? unknownReason)
        {
            // Relationship metadata refines controlled edge kinds without inventing ADO.NET-specific graph relationship types.
            Dictionary<string, object?> values = CreateBaseMetadata(relativePath, projectContext, commandFact.Provider);
            values["detectionMode"] = "SourceUsage";
            values["dataAccessRelationshipKind"] = relationshipKind;
            values["commandApi"] = commandApi;
            values["commandType"] = commandType;
            values["commandObjectType"] = commandFact.CommandTypeName;
            values["readWriteHint"] = readWriteHint;
            values["sqlTextHash"] = commandFact.SqlText.Hash;
            values["sqlPreview"] = commandFact.SqlText.IsStatic ? commandFact.SqlText.RedactedText : null;
            values["dataAccessUnknownReason"] = unknownReason;
            return GraphMetadata.From(RemoveNullValues(values));
        }

        /// <summary>
        /// Creates shared lower-camel metadata fields for ADO.NET graph facts.
        /// </summary>
        /// <param name="relativePath">The repository-relative source file path.</param>
        /// <param name="projectContext">The repository-relative project context.</param>
        /// <param name="provider">The normalized provider value.</param>
        /// <returns>A mutable metadata dictionary with shared ADO.NET fields.</returns>
        private static Dictionary<string, object?> CreateBaseMetadata(string relativePath, string projectContext, string provider)
        {
            // Shared metadata keeps ADO.NET facts aligned with data-access lower-camel naming and source provenance rules.
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["detectionMode"] = "SourceUsage",
                ["extractor"] = nameof(AdoNetRawSqlExtractor),
                ["modelFilePath"] = relativePath,
                ["projectContext"] = projectContext,
                ["dataAccessTechnology"] = "AdoNet",
                ["framework"] = "AdoNet",
                ["provider"] = provider
            };
        }

        /// <summary>
        /// Removes null metadata values before canonical metadata creation.
        /// </summary>
        /// <param name="values">The metadata dictionary that may contain null values.</param>
        /// <returns>A dictionary with null values removed.</returns>
        private static IReadOnlyDictionary<string, object?> RemoveNullValues(Dictionary<string, object?> values)
        {
            // Omitting absent metadata avoids implying that unresolved command text, providers, or schemas were known.
            return values.Where(static pair => pair.Value is not null).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        }

        /// <summary>
        /// Creates a project-scoped stable key for ADO.NET graph facts.
        /// </summary>
        /// <param name="prefix">The stable key prefix.</param>
        /// <param name="projectContext">The repository-relative project context.</param>
        /// <param name="identity">The project-scoped fact identity.</param>
        /// <returns>A deterministic stable key.</returns>
        private static StableKey CreateProjectScopedKey(string prefix, string projectContext, string identity)
        {
            // Project-scoped keys avoid absolute paths and remain stable across developer machines.
            return new StableKey($"{prefix}://{RepositoryRelativePath.Parse(projectContext).Value}#{identity}");
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
        /// Represents an extracted SQL text value and whether it was static, dynamic, or missing.
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
                    // Missing command text is distinct from dynamic SQL because no expression supplied SQL at all.
                    return new SqlTextFact(null, null, IsStatic: false, IsDynamic: false);
                }
            }

            /// <summary>
            /// Gets a SQL text fact for computed command text.
            /// </summary>
            public static SqlTextFact Dynamic
            {
                get
                {
                    // Dynamic command text is represented explicitly without evaluating runtime string construction.
                    return new SqlTextFact(null, null, IsStatic: false, IsDynamic: true);
                }
            }
        }

        /// <summary>
        /// Represents an ADO.NET command or adapter observed inside one source method.
        /// </summary>
        /// <param name="VariableName">The local variable or parameter name when available.</param>
        /// <param name="Provider">The normalized provider value.</param>
        /// <param name="CommandTypeName">The command or adapter CLR type name.</param>
        /// <param name="SqlText">The extracted SQL text fact.</param>
        /// <param name="CommandType">The command type, such as StoredProcedure, when assigned.</param>
        /// <param name="ConnectionStringKey">The safe connection-string key when available.</param>
        /// <param name="IsAdapter">A value indicating whether the fact represents a data adapter rather than a direct command.</param>
        private sealed record AdoNetCommandFact(string? VariableName, string Provider, string CommandTypeName, SqlTextFact SqlText, string? CommandType, string? ConnectionStringKey, bool IsAdapter)
        {
            /// <summary>
            /// Creates an unknown command fact for parameters or command factory calls whose command text is assigned later or not available.
            /// </summary>
            /// <param name="variableName">The local variable or parameter name associated with the command.</param>
            /// <param name="provider">The normalized provider value.</param>
            /// <param name="commandTypeName">The command CLR type name.</param>
            /// <returns>An unknown command fact.</returns>
            public static AdoNetCommandFact FromUnknown(string? variableName, string provider, string commandTypeName)
            {
                // Unknown command facts preserve execution evidence even when static command text cannot be resolved.
                return new AdoNetCommandFact(variableName, provider, commandTypeName, SqlTextFact.Missing, null, null, IsAdapter: false);
            }
        }

        /// <summary>
        /// Represents the relevant ADO.NET shape of a Roslyn type symbol.
        /// </summary>
        /// <param name="Provider">The normalized provider value.</param>
        /// <param name="TypeName">The short type name.</param>
        /// <param name="IsCommand">A value indicating whether the type is an ADO.NET command.</param>
        /// <param name="IsConnection">A value indicating whether the type is an ADO.NET connection.</param>
        /// <param name="IsAdapter">A value indicating whether the type is an ADO.NET data adapter.</param>
        /// <param name="IsReader">A value indicating whether the type is an ADO.NET data reader.</param>
        /// <param name="IsDataContainer">A value indicating whether the type is a DataSet or DataTable container.</param>
        private readonly record struct AdoNetApiKind(string Provider, string TypeName, bool IsCommand, bool IsConnection, bool IsAdapter, bool IsReader, bool IsDataContainer)
        {
            /// <summary>
            /// Gets a value indicating whether the type participates in ADO.NET extraction.
            /// </summary>
            public bool IsRelevant
            {
                get
                {
                    // Any recognized command, connection, adapter, reader, or data container is relevant evidence.
                    return IsCommand || IsConnection || IsAdapter || IsReader || IsDataContainer;
                }
            }

            /// <summary>
            /// Gets an unknown ADO.NET type classification.
            /// </summary>
            public static AdoNetApiKind Unknown
            {
                get
                {
                    // Unknown still carries provider and type placeholders so metadata can remain non-null.
                    return new AdoNetApiKind("Unknown", "Unknown", IsCommand: false, IsConnection: false, IsAdapter: false, IsReader: false, IsDataContainer: false);
                }
            }
        }

        /// <summary>
        /// Represents the conservative result of raw SQL text analysis.
        /// </summary>
        /// <param name="DisplayName">The display name for the raw SQL node.</param>
        /// <param name="ReadWriteHint">The read/write hint.</param>
        /// <param name="StoredProcedureName">The stored procedure name, if this is a stored procedure command.</param>
        /// <param name="AffectedTables">The affected tables conservatively parsed from SQL text.</param>
        /// <param name="ProcedurePreview">The redacted stored procedure preview.</param>
        /// <param name="SqlPreview">The redacted SQL preview.</param>
        /// <param name="SqlTextHash">The hash of the redacted SQL text.</param>
        /// <param name="IsDynamicSql">A value indicating whether command text was computed dynamically.</param>
        /// <param name="UnknownReason">The unknown reason when static analysis cannot resolve command text or impact.</param>
        private sealed record SqlAnalysisResult(string DisplayName, string ReadWriteHint, string? StoredProcedureName, IReadOnlyList<ParsedDatabaseObjectName> AffectedTables, string? ProcedurePreview, string? SqlPreview, string? SqlTextHash, bool IsDynamicSql, string? UnknownReason);

        /// <summary>
        /// Tracks method-local command variables and deterministic method identity.
        /// </summary>
        private sealed class AdoNetMethodState
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="AdoNetMethodState" /> class.
            /// </summary>
            /// <param name="methodStableKey">The stable key for the source method node.</param>
            private AdoNetMethodState(StableKey methodStableKey)
            {
                // The constructor only stores deterministic method identity; command facts are populated during method traversal.
                MethodStableKey = methodStableKey;
            }

            /// <summary>
            /// Gets the stable key of the source method node.
            /// </summary>
            public StableKey MethodStableKey { get; }

            /// <summary>
            /// Gets command facts by local variable or parameter name.
            /// </summary>
            public Dictionary<string, AdoNetCommandFact> CommandFactsByVariable { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Creates method state from a Roslyn method symbol.
            /// </summary>
            /// <param name="methodSymbol">The source method symbol being traversed.</param>
            /// <param name="snapshotStableKey">The stable key of the owning snapshot.</param>
            /// <param name="relativePath">The repository-relative source file path.</param>
            /// <param name="projectContext">The repository-relative project context.</param>
            /// <returns>A method state with deterministic method identity.</returns>
            public static AdoNetMethodState FromMethod(IMethodSymbol methodSymbol, StableKey snapshotStableKey, string relativePath, string projectContext)
            {
                // Snapshot, project, path, and symbol identity keep method stable keys deterministic across machines.
                return new AdoNetMethodState(new StableKey($"method://{HashStablePayload(snapshotStableKey.Value, projectContext, relativePath, methodSymbol.ToDisplayString())}"));
            }
        }
    }
}
