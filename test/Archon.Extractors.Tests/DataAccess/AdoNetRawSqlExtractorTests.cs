using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.DataAccess.LinqToSql;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Archon.Extractors.Tests.DataAccess
{
    /// <summary>
    /// Verifies the ADO.NET and raw SQL extraction slice for provider APIs, commands, stored procedures, read/write hints, dynamic SQL, affected tables, evidence, confidence, unknowns, and redaction.
    /// </summary>
    public sealed class AdoNetRawSqlExtractorTests
    {
        /// <summary>
        /// Confirms concrete and abstract ADO.NET commands emit table, stored procedure, raw SQL, read/write, dynamic SQL, and redaction facts.
        /// </summary>
        [Fact]
        public void AdoNetExtractionDetectsCommandsStoredProceduresSqlHintsDynamicSqlAndRedaction()
        {
            // The fixture covers the primary ADO.NET APIs without opening a connection or executing target code.
            LinqToSqlDbmlExtractionResult result = ExtractFixture("src/Sample.AdoNet/Sample.AdoNet.csproj", "src/Sample.AdoNet/CustomerRepository.cs", AdoNetSource);

            Assert.Empty(result.Errors);
            ArchitectureNode methodNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "Run");
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseTable && node.StableKey.Value == "dbtable://src/Sample.AdoNet/Sample.AdoNet.csproj#sales.Customers" && ContainsMetadata(node, "\"dataAccessTechnology\":\"AdoNet\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseTable && node.StableKey.Value == "dbtable://src/Sample.AdoNet/Sample.AdoNet.csproj#sales.AuditLog");
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.StoredProcedure && node.StableKey.Value == "storedprocedure://src/Sample.AdoNet/Sample.AdoNet.csproj#dbo.GetCustomer" && ContainsMetadata(node, "\"storedProcedureName\":\"GetCustomer\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ReadsTable && edge.SourceNodeStableKey == methodNode.StableKey && edge.TargetNodeStableKey.Value == "dbtable://src/Sample.AdoNet/Sample.AdoNet.csproj#sales.Customers" && ContainsMetadata(edge, "\"commandApi\":\"ExecuteReader\"") && ContainsMetadata(edge, "\"readWriteHint\":\"Read\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.WritesTable && edge.SourceNodeStableKey == methodNode.StableKey && edge.TargetNodeStableKey.Value == "dbtable://src/Sample.AdoNet/Sample.AdoNet.csproj#sales.Customers" && ContainsMetadata(edge, "\"commandApi\":\"ExecuteNonQuery\"") && ContainsMetadata(edge, "\"readWriteHint\":\"Write\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.WritesTable && edge.SourceNodeStableKey == methodNode.StableKey && edge.TargetNodeStableKey.Value == "dbtable://src/Sample.AdoNet/Sample.AdoNet.csproj#sales.AuditLog");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.CallsStoredProcedure && edge.SourceNodeStableKey == methodNode.StableKey && edge.TargetNodeStableKey.Value == "storedprocedure://src/Sample.AdoNet/Sample.AdoNet.csproj#dbo.GetCustomer" && ContainsMetadata(edge, "\"commandType\":\"StoredProcedure\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ExecutesRawSql && edge.SourceNodeStableKey == methodNode.StableKey && ContainsMetadata(edge, "\"sqlTextHash\"") && ContainsMetadata(edge, "\"sqlPreview\"") && ContainsMetadata(edge, "\"commandApi\":\"ExecuteScalar\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ExecutesRawSql && edge.SourceNodeStableKey == methodNode.StableKey && edge.UnknownState.HasUnknownData && ContainsMetadata(edge, "\"dataAccessUnknownReason\":\"ComputedSql\""));
            Assert.Contains(result.Warnings, warning => warning.Contains("computed", StringComparison.OrdinalIgnoreCase));
            AssertDoesNotLeakSecrets(result);
        }

        /// <summary>
        /// Confirms provider-specific ADO.NET API shapes and SQL statement classes are represented with conservative table hints.
        /// </summary>
        [Fact]
        public void RawSqlExtractionClassifiesProviderApisAndStatementKindsConservatively()
        {
            // This fixture broadens coverage beyond SqlClient to DbCommand, OleDb, Odbc, DataAdapter, DataSet, DataTable, MERGE, DDL, and unknown command text.
            LinqToSqlDbmlExtractionResult result = ExtractFixture("src/Sample.MixedSql/Sample.MixedSql.csproj", "src/Sample.MixedSql/MixedRepository.cs", MixedProviderSource);

            Assert.Empty(result.Errors);
            ArchitectureNode methodNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "Execute");
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseTable && node.StableKey.Value == "dbtable://src/Sample.MixedSql/Sample.MixedSql.csproj#dbo.CustomerArchive");
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseTable && node.StableKey.Value == "dbtable://src/Sample.MixedSql/Sample.MixedSql.csproj#dbo.LegacyOrders");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.WritesTable && edge.SourceNodeStableKey == methodNode.StableKey && edge.TargetNodeStableKey.Value == "dbtable://src/Sample.MixedSql/Sample.MixedSql.csproj#dbo.CustomerArchive" && ContainsMetadata(edge, "\"readWriteHint\":\"Write\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.WritesTable && edge.SourceNodeStableKey == methodNode.StableKey && edge.TargetNodeStableKey.Value == "dbtable://src/Sample.MixedSql/Sample.MixedSql.csproj#dbo.LegacyOrders" && ContainsMetadata(edge, "\"commandApi\":\"ExecuteNonQuery\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ExecutesRawSql && edge.SourceNodeStableKey == methodNode.StableKey && ContainsMetadata(edge, "\"commandApi\":\"Fill\"") && ContainsMetadata(edge, "\"readWriteHint\":\"Read\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ExecutesRawSql && edge.SourceNodeStableKey == methodNode.StableKey && edge.UnknownState.HasUnknownData && ContainsMetadata(edge, "\"commandApi\":\"ExecuteScalar\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.SqlScript && node.UnknownState.HasUnknownData && ContainsMetadata(node, "\"dataAccessUnknownReason\":\"MissingCommandText\""));
        }

        /// <summary>
        /// Determines whether a node metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="node">The architecture node whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the node metadata.</param>
        /// <returns><see langword="true" /> when the metadata contains the expected fragment; otherwise, <see langword="false" />.</returns>
        private static bool ContainsMetadata(ArchitectureNode node, string expectedFragment)
        {
            // Canonical JSON assertions verify ADO.NET-specific metadata while avoiding test coupling to dictionary construction order.
            return node.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether an edge metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="edge">The architecture edge whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the edge metadata.</param>
        /// <returns><see langword="true" /> when the metadata contains the expected fragment; otherwise, <see langword="false" />.</returns>
        private static bool ContainsMetadata(ArchitectureEdge edge, string expectedFragment)
        {
            // Edge metadata assertions cover API names, command types, read/write hints, SQL hashes, and unknown reasons.
            return edge.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Runs the production data-access extractor against one semantic source fixture.
        /// </summary>
        /// <param name="projectContext">The repository-relative project context used by stable keys.</param>
        /// <param name="documentPath">The repository-relative source document path.</param>
        /// <param name="source">The C# source fixture to compile and inspect.</param>
        /// <returns>The extraction result containing graph-ready ADO.NET and raw SQL facts.</returns>
        private static LinqToSqlDbmlExtractionResult ExtractFixture(string projectContext, string documentPath, string source)
        {
            // Fixtures use isolated temporary repositories so repository-relative path normalization is covered without depending on the checkout location.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-adonet-fixture", Guid.NewGuid().ToString("N"));
            string absoluteDocumentPath = Path.Combine(repositoryRoot, documentPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteDocumentPath)!);
            File.WriteAllText(absoluteDocumentPath, source);
            SemanticExtractionRequest semanticDocument = CreateSemanticRequest(repositoryRoot, projectContext, absoluteDocumentPath, source);
            LinqToSqlDbmlModelExtractor extractor = new();
            LinqToSqlDbmlExtractionRequest request = new(StableKeyGenerator.ForRepository("Sample.Repository"), repositoryRoot, [semanticDocument]);
            return extractor.Extract(request, CancellationToken.None);
        }

        /// <summary>
        /// Creates a semantic extraction request for a source fixture using Roslyn metadata references that are sufficient for static symbol analysis.
        /// </summary>
        /// <param name="repositoryRoot">The absolute repository root for evidence normalization.</param>
        /// <param name="projectContext">The repository-relative project context used by stable keys.</param>
        /// <param name="sourcePath">The absolute source path associated with the syntax tree.</param>
        /// <param name="source">The C# source text to parse and compile.</param>
        /// <returns>A semantic extraction request for the supplied source document.</returns>
        private static SemanticExtractionRequest CreateSemanticRequest(string repositoryRoot, string projectContext, string sourcePath, string source)
        {
            // The test compilation uses framework assemblies for ADO.NET symbols and local stubs only where provider-specific types are unavailable on all platforms.
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(sourcePath),
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Data.CommandType).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Data.Common.DbCommand).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Data.DataSet).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            return new SemanticExtractionRequest(repositoryRoot, projectContext, sourcePath, syntaxTree, semanticModel);
        }

        /// <summary>
        /// Verifies that extraction output does not expose fixture secret literals in metadata, evidence, warnings, or errors.
        /// </summary>
        /// <param name="result">The extraction result to inspect for secret leakage.</param>
        private static void AssertDoesNotLeakSecrets(LinqToSqlDbmlExtractionResult result)
        {
            // Redaction is a data-access safety requirement because source fixtures may contain SQL and connection-string literals that look like real credentials.
            Assert.DoesNotContain(result.Snapshot.Nodes, node => ContainsSensitiveText(node.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Snapshot.Edges, edge => ContainsSensitiveText(edge.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Snapshot.Evidence, evidence => ContainsSensitiveText(evidence.SnippetPreview) || ContainsSensitiveText(evidence.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Warnings, ContainsSensitiveText);
            Assert.DoesNotContain(result.Errors, ContainsSensitiveText);
        }

        /// <summary>
        /// Determines whether a value contains any known secret literal from the ADO.NET fixtures.
        /// </summary>
        /// <param name="value">The value to inspect for fixture secrets.</param>
        /// <returns><see langword="true" /> when a sensitive fixture literal appears; otherwise, <see langword="false" />.</returns>
        private static bool ContainsSensitiveText(string? value)
        {
            // The checks use exact fixture literals and common connection-string credential labels as leak sentinels.
            return value?.Contains("SuperSecret", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("Password=", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("User Id=sa", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("token-123", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Gets an ADO.NET fixture covering SqlClient command creation, command text assignments, parameters, stored procedures, static SQL, and dynamic SQL.
        /// </summary>
        private const string AdoNetSource = """
            namespace Sample.AdoNet
            {
                using System.Data;
                using System.Data.Common;
                using System.Data.SqlClient;

                public sealed class CustomerRepository
                {
                    public void Run(string suffix)
                    {
                        using SqlConnection connection = new("Server=.;Database=Sales;User Id=sa;Password=SuperSecret");
                        using SqlCommand readCommand = new("SELECT Id, Name FROM sales.Customers WHERE Token = 'token-123'", connection);
                        readCommand.Parameters.AddWithValue("@id", 42);
                        using SqlDataReader reader = readCommand.ExecuteReader();

                        using SqlCommand writeCommand = connection.CreateCommand();
                        writeCommand.CommandText = "UPDATE sales.Customers SET Name = 'Updated' WHERE Id = @id";
                        writeCommand.ExecuteNonQuery();

                        using SqlCommand storedProcedureCommand = new("dbo.GetCustomer", connection);
                        storedProcedureCommand.CommandType = CommandType.StoredProcedure;
                        storedProcedureCommand.ExecuteScalar();

                        DbCommand insertCommand = connection.CreateCommand();
                        insertCommand.CommandText = "INSERT INTO sales.AuditLog(CustomerId) VALUES(@id)";
                        insertCommand.ExecuteNonQuery();

                        using SqlCommand dynamicCommand = new("DELETE FROM sales.Customers WHERE Name = '" + suffix + "'", connection);
                        dynamicCommand.ExecuteNonQuery();
                    }
                }
            }
            """;

        /// <summary>
        /// Gets an ADO.NET fixture covering provider variants, data adapter fill, DataSet/DataTable usage, MERGE, DDL, and missing command text unknowns.
        /// </summary>
        private const string MixedProviderSource = """
            namespace Sample.MixedSql
            {
                using System.Data;
                using System.Data.Common;
                using System.Data.Odbc;
                using System.Data.OleDb;

                public sealed class MixedRepository
                {
                    public void Execute(DbCommand unknownCommand)
                    {
                        DataSet dataSet = new();
                        DataTable table = new();

                        using OleDbCommand archiveCommand = new("MERGE dbo.CustomerArchive AS target USING dbo.Customers AS source ON target.Id = source.Id WHEN MATCHED THEN UPDATE SET Name = source.Name", new OleDbConnection("Provider=SQLOLEDB;Password=SuperSecret"));
                        archiveCommand.ExecuteNonQuery();

                        using OdbcCommand ddlCommand = new("CREATE TABLE dbo.LegacyOrders (Id int)", new OdbcConnection("Driver={SQL Server};Password=SuperSecret"));
                        ddlCommand.ExecuteNonQuery();

                        using OleDbDataAdapter adapter = new("SELECT * FROM dbo.CustomerArchive", new OleDbConnection("Provider=SQLOLEDB;Password=SuperSecret"));
                        adapter.Fill(dataSet);
                        table.TableName = "LocalOnly";

                        unknownCommand.ExecuteScalar();
                    }
                }
            }
            """;
    }
}
