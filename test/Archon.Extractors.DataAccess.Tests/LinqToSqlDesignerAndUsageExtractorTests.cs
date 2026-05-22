using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.DataAccess.LinqToSql;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Archon.Extractors.DataAccess.Tests
{
    /// <summary>
    /// Verifies the WP009 LINQ to SQL designer and source-usage extraction slice for generated model code, method-level usage, raw SQL, stored procedure wrappers, and unknowns.
    /// </summary>
    public sealed class LinqToSqlDesignerAndUsageExtractorTests
    {
        /// <summary>
        /// Confirms generated LINQ to SQL designer classes produce DataContext, entity, table, column, association, and stored-procedure wrapper facts that deduplicate with DBML model facts.
        /// </summary>
        [Fact]
        public void ExtractDetectsGeneratedDesignerMappingsAndDeduplicatesWithDbmlFacts()
        {
            // The fixture combines DBML and generated designer source so the extractor must correlate two evidence families into one graph identity per model fact.
            LinqToSqlDbmlExtractionResult result = ExtractFixture(includeUsageSource: false);

            Assert.Empty(result.Errors);
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.LinqToSqlDataContext && node.StableKey.Value == "linqtosql://src/Sample.Data/Northwind.dbml#NorthwindDataContext" && ContainsMetadata(node, "\"generatedFilePath\":\"src/Sample.Data/Northwind.designer.cs\""));
            Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.LinqToSqlDataContext && node.DisplayName == "NorthwindDataContext");
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Entity && node.StableKey.Value == "entity://src/Sample.Data/Northwind.dbml#Customer" && ContainsMetadata(node, "\"detectionMode\":\"DbmlAndDesignerSource\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseTable && node.StableKey.Value == "dbtable://src/Sample.Data/Northwind.dbml#dbo.Customers");
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseColumn && node.StableKey.Value == "dbcolumn://src/Sample.Data/Northwind.dbml#dbo.Customers.CustomerID" && ContainsMetadata(node, "\"generatedFilePath\":\"src/Sample.Data/Northwind.designer.cs\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.StoredProcedure && node.StableKey.Value == "storedprocedure://src/Sample.Data/Northwind.dbml#dbo.GetCustomerOrders" && ContainsMetadata(node, "\"methodName\":\"GetCustomerOrders\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.MapsEntity && edge.SourceNodeStableKey.Value == "linqtosql://src/Sample.Data/Northwind.dbml#NorthwindDataContext" && edge.TargetNodeStableKey.Value == "entity://src/Sample.Data/Northwind.dbml#Customer");
            Assert.Contains(result.Snapshot.Evidence, evidence => evidence.EvidenceKind == EvidenceKind.DesignerGeneratedCode && evidence.FilePath.Value == "src/Sample.Data/Northwind.designer.cs" && evidence.SymbolName == "NorthwindDataContext");
        }

        /// <summary>
        /// Confirms source-code usage of generated LINQ to SQL contexts emits method-level context, read/write, stored-procedure, raw SQL, and unknown facts.
        /// </summary>
        [Fact]
        public void ExtractDetectsSourceUsageRelationshipsRawSqlAndUnknowns()
        {
            // Usage source exercises DataContext construction, Table<T> queries, GetTable<T>(), SubmitChanges, InsertOnSubmit, DeleteOnSubmit, ExecuteQuery, ExecuteCommand, and stored procedure wrappers.
            LinqToSqlDbmlExtractionResult result = ExtractFixture(includeUsageSource: true);

            ArchitectureNode methodNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "Run");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesLinqToSqlContext && edge.SourceNodeStableKey == methodNode.StableKey && edge.TargetNodeStableKey.Value == "linqtosql://src/Sample.Data/Northwind.dbml#NorthwindDataContext");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ReadsTable && edge.SourceNodeStableKey == methodNode.StableKey && edge.TargetNodeStableKey.Value == "dbtable://src/Sample.Data/Northwind.dbml#dbo.Customers" && ContainsMetadata(edge, "\"readWriteHint\":\"Read\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.WritesTable && edge.SourceNodeStableKey == methodNode.StableKey && edge.TargetNodeStableKey.Value == "dbtable://src/Sample.Data/Northwind.dbml#dbo.Customers" && ContainsMetadata(edge, "\"readWriteHint\":\"Write\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.CallsStoredProcedure && edge.SourceNodeStableKey == methodNode.StableKey && edge.TargetNodeStableKey.Value == "storedprocedure://src/Sample.Data/Northwind.dbml#dbo.GetCustomerOrders");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ExecutesRawSql && edge.SourceNodeStableKey == methodNode.StableKey && ContainsMetadata(edge, "\"commandApi\":\"ExecuteQuery\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ExecutesRawSql && edge.SourceNodeStableKey == methodNode.StableKey && ContainsMetadata(edge, "\"commandApi\":\"ExecuteCommand\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ReadsTable && edge.SourceNodeStableKey == methodNode.StableKey && edge.UnknownState.HasUnknownData && edge.TargetNodeStableKey.Value.Contains("Unknown", StringComparison.Ordinal));
            Assert.Contains(result.Warnings, warning => warning.Contains("GetTable", StringComparison.Ordinal));
            Assert.Contains(result.Snapshot.Evidence, evidence => evidence.EvidenceKind == EvidenceKind.SourceCode && evidence.FilePath.Value == "src/Sample.App/Repository.cs" && evidence.SnippetPreview?.Contains("ExecuteQuery<Customer>", StringComparison.Ordinal) == true);
        }

        /// <summary>
        /// Determines whether a node metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="node">The architecture node whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the node metadata.</param>
        /// <returns><see langword="true" /> when the metadata contains the expected fragment; otherwise, <see langword="false" />.</returns>
        private static bool ContainsMetadata(ArchitectureNode node, string expectedFragment)
        {
            // Canonical metadata assertions verify extractor-specific details without depending on dictionary construction order.
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
            // Edge metadata assertions verify usage classification, raw SQL APIs, and read/write hints while stable-key assertions cover identity.
            return edge.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Builds a temporary repository fixture, creates Roslyn semantic documents, and invokes the production LINQ to SQL extractor.
        /// </summary>
        /// <param name="includeUsageSource">A value indicating whether the usage source file should be included in semantic extraction.</param>
        /// <returns>The LINQ to SQL extraction result for the fixture repository and semantic documents.</returns>
        private static LinqToSqlDbmlExtractionResult ExtractFixture(bool includeUsageSource)
        {
            // The fixture uses real syntax trees and semantic models so pattern detection can rely on compiler symbols where they are available.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-linqtosql-usage-fixture", Guid.NewGuid().ToString("N"));
            string dataDirectory = Path.Combine(repositoryRoot, "src", "Sample.Data");
            string appDirectory = Path.Combine(repositoryRoot, "src", "Sample.App");
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(appDirectory);
            File.WriteAllText(Path.Combine(dataDirectory, "Northwind.dbml"), CompleteDbml);

            string designerPath = Path.Combine(dataDirectory, "Northwind.designer.cs");
            File.WriteAllText(designerPath, DesignerSource);
            List<SemanticExtractionRequest> semanticDocuments = [CreateSemanticRequest(repositoryRoot, "src/Sample.Data/Sample.Data.csproj", designerPath, DesignerSource)];

            if (includeUsageSource)
            {
                string usagePath = Path.Combine(appDirectory, "Repository.cs");
                File.WriteAllText(usagePath, UsageSource);
                semanticDocuments.Add(CreateSemanticRequest(repositoryRoot, "src/Sample.App/Sample.App.csproj", usagePath, UsageSource));
            }

            LinqToSqlDbmlModelExtractor extractor = new();
            LinqToSqlDbmlExtractionRequest request = new(StableKeyGenerator.ForRepository("Sample.Repository"), repositoryRoot, semanticDocuments);
            return extractor.Extract(request, CancellationToken.None);
        }

        /// <summary>
        /// Creates a semantic extraction request for one C# source document in the fixture repository.
        /// </summary>
        /// <param name="repositoryRoot">The absolute repository root for evidence normalization.</param>
        /// <param name="projectContext">The repository-relative project context for stable source identity.</param>
        /// <param name="sourcePath">The absolute source path associated with the syntax tree.</param>
        /// <param name="source">The C# source text to parse and compile.</param>
        /// <returns>A semantic extraction request for the supplied source document.</returns>
        private static SemanticExtractionRequest CreateSemanticRequest(string repositoryRoot, string projectContext, string sourcePath, string source)
        {
            // Tests compile each file independently with local LINQ to SQL stubs so the extractor can resolve generated source symbols without external packages.
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(sourcePath),
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            return new SemanticExtractionRequest(repositoryRoot, projectContext, sourcePath, syntaxTree, semanticModel);
        }

        /// <summary>
        /// Gets a DBML fixture that should deduplicate with generated designer source facts.
        /// </summary>
        private const string CompleteDbml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Database Name="Northwind" Class="NorthwindDataContext" xmlns="http://schemas.microsoft.com/linqtosql/dbml/2007">
              <Connection Mode="AppSettings" SettingsPropertyName="NorthwindConnectionString" Provider="System.Data.SqlClient" />
              <Table Name="dbo.Customers" Member="Customers">
                <Type Name="Customer">
                  <Column Name="CustomerID" Member="CustomerID" Type="System.String" DbType="NChar(5) NOT NULL" IsPrimaryKey="true" CanBeNull="false" />
                  <Association Name="FK_Orders_Customers" Member="Orders" ThisKey="CustomerID" OtherKey="CustomerID" Type="Order" />
                </Type>
              </Table>
              <Function Name="dbo.GetCustomerOrders" Method="GetCustomerOrders">
                <Parameter Name="customerId" Parameter="customerId" Type="System.String" DbType="NChar(5)" />
              </Function>
            </Database>
            """;

        /// <summary>
        /// Gets generated designer source containing LINQ to SQL mapping attributes, Table&lt;T&gt; properties, and stored-procedure wrapper metadata.
        /// </summary>
        private const string DesignerSource = """
            namespace System.Data.Linq
            {
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Linq;
                public class DataContext { public DataContext(string connection) { } public Table<TEntity> GetTable<TEntity>() where TEntity : class => new(); public int ExecuteCommand(string command, params object[] parameters) => 0; public IEnumerable<TResult> ExecuteQuery<TResult>(string query, params object[] parameters) => Enumerable.Empty<TResult>(); public void SubmitChanges() { } }
                public class Table<TEntity> : List<TEntity> where TEntity : class { public void InsertOnSubmit(TEntity entity) { } public void DeleteOnSubmit(TEntity entity) { } public void Attach(TEntity entity) { } }
            }

            namespace System.Data.Linq.Mapping
            {
                using System;
                [AttributeUsage(AttributeTargets.Class)] public sealed class DatabaseAttribute : Attribute { public string? Name { get; set; } }
                [AttributeUsage(AttributeTargets.Class)] public sealed class TableAttribute : Attribute { public string? Name { get; set; } }
                [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)] public sealed class ColumnAttribute : Attribute { public string? Name { get; set; } public string? DbType { get; set; } public bool IsPrimaryKey { get; set; } public bool CanBeNull { get; set; } }
                [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)] public sealed class AssociationAttribute : Attribute { public string? Name { get; set; } public string? ThisKey { get; set; } public string? OtherKey { get; set; } }
                [AttributeUsage(AttributeTargets.Method)] public sealed class FunctionAttribute : Attribute { public string? Name { get; set; } }
                [AttributeUsage(AttributeTargets.Parameter)] public sealed class ParameterAttribute : Attribute { public string? Name { get; set; } public string? DbType { get; set; } }
            }

            namespace Sample.Data
            {
                using System.Data.Linq;
                using System.Data.Linq.Mapping;

                [Database(Name = "Northwind")]
                public partial class NorthwindDataContext : DataContext
                {
                    public NorthwindDataContext(string connection) : base(connection) { }
                    public Table<Customer> Customers => GetTable<Customer>();
                    [Function(Name = "dbo.GetCustomerOrders")]
                    public void GetCustomerOrders([Parameter(Name = "customerId", DbType = "NChar(5)")] string customerId) { }
                }

                [Table(Name = "dbo.Customers")]
                public partial class Customer
                {
                    [Column(Name = "CustomerID", DbType = "NChar(5) NOT NULL", IsPrimaryKey = true, CanBeNull = false)]
                    public string? CustomerID { get; set; }
                    [Association(Name = "FK_Orders_Customers", ThisKey = "CustomerID", OtherKey = "CustomerID")]
                    public object? Orders { get; set; }
                }
            }
            """;

        /// <summary>
        /// Gets source usage containing direct context construction, table reads/writes, raw SQL execution, stored-procedure wrappers, and an unresolved generic table target.
        /// </summary>
        private const string UsageSource = DesignerSource + """

            namespace Sample.App
            {
                using System.Linq;
                using Sample.Data;

                public sealed class Repository
                {
                    public void Run()
                    {
                        var context = new NorthwindDataContext("name=NorthwindConnectionString");
                        var query = context.Customers.Where(customer => customer.CustomerID != null).ToList();
                        var table = context.GetTable<Customer>();
                        table.InsertOnSubmit(new Customer());
                        table.DeleteOnSubmit(new Customer());
                        table.Attach(new Customer());
                        context.SubmitChanges();
                        context.GetCustomerOrders("ALFKI");
                        context.ExecuteQuery<Customer>("SELECT * FROM dbo.Customers WHERE CustomerID = {0}", "ALFKI");
                        context.ExecuteCommand("DELETE FROM dbo.Customers WHERE CustomerID = {0}", "ALFKI");
                        var unknown = context.GetTable<UnmappedCustomer>();
                    }
                }

                public sealed class UnmappedCustomer { }
            }
            """;
    }
}
