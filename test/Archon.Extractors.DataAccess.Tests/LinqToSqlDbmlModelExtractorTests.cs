using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.DataAccess.LinqToSql;
using Xunit;

namespace Archon.Extractors.DataAccess.Tests
{
    /// <summary>
    /// Verifies the WP009 LINQ to SQL DBML model extraction slice for model facts, relationships, evidence, warnings, unknowns, and redaction.
    /// </summary>
    public sealed class LinqToSqlDbmlModelExtractorTests
    {
        /// <summary>
        /// Confirms a complete DBML model emits deterministic data-context, entity, table, column, stored-procedure, relationship, and evidence facts.
        /// </summary>
        [Fact]
        public void ExtractParsesCompleteDbmlModelFactsAndEvidence()
        {
            // The fixture contains one representative DBML model so the test can verify the end-to-end graph contribution shape without source-code or database execution.
            string repositoryRoot = CreateRepositoryWithDbml("src/Sample.Data/Northwind.dbml", CompleteDbml);
            LinqToSqlDbmlExtractionResult result = Extract(repositoryRoot);

            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.LinqToSqlDataContext && node.StableKey.Value == "linqtosql://src/Sample.Data/Northwind.dbml#NorthwindDataContext" && ContainsMetadata(node, "\"databaseName\":\"Northwind\"") && ContainsMetadata(node, "\"connectionStringKey\":\"NorthwindConnectionString\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Entity && node.StableKey.Value == "entity://src/Sample.Data/Northwind.dbml#Customer" && ContainsMetadata(node, "\"entityType\":\"Customer\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseTable && node.StableKey.Value == "dbtable://src/Sample.Data/Northwind.dbml#dbo.Customers" && ContainsMetadata(node, "\"schemaName\":\"dbo\"") && ContainsMetadata(node, "\"tableName\":\"Customers\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseColumn && node.StableKey.Value == "dbcolumn://src/Sample.Data/Northwind.dbml#dbo.Customers.CustomerID" && ContainsMetadata(node, "\"propertyName\":\"CustomerID\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.StoredProcedure && node.StableKey.Value == "storedprocedure://src/Sample.Data/Northwind.dbml#dbo.GetCustomerOrders" && ContainsMetadata(node, "\"storedProcedureName\":\"GetCustomerOrders\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.MapsEntity && edge.SourceNodeStableKey.Value == "linqtosql://src/Sample.Data/Northwind.dbml#NorthwindDataContext" && edge.TargetNodeStableKey.Value == "entity://src/Sample.Data/Northwind.dbml#Customer");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.MapsTable && edge.SourceNodeStableKey.Value == "entity://src/Sample.Data/Northwind.dbml#Customer" && edge.TargetNodeStableKey.Value == "dbtable://src/Sample.Data/Northwind.dbml#dbo.Customers");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.MapsColumn && edge.SourceNodeStableKey.Value == "dbtable://src/Sample.Data/Northwind.dbml#dbo.Customers" && edge.TargetNodeStableKey.Value == "dbcolumn://src/Sample.Data/Northwind.dbml#dbo.Customers.CustomerID");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.CallsStoredProcedure && edge.SourceNodeStableKey.Value == "linqtosql://src/Sample.Data/Northwind.dbml#NorthwindDataContext" && edge.TargetNodeStableKey.Value == "storedprocedure://src/Sample.Data/Northwind.dbml#dbo.GetCustomerOrders");
            Assert.Contains(result.Snapshot.Evidence, evidence => evidence.EvidenceKind == EvidenceKind.Dbml && evidence.FilePath.Value == "src/Sample.Data/Northwind.dbml" && evidence.StartLine > 0 && !string.IsNullOrWhiteSpace(evidence.SnippetHash) && evidence.SnippetPreview?.Contains("<Database", StringComparison.Ordinal) == true && ContainsMetadata(evidence, "\"detectionMode\":\"DbmlXmlModel\""));
        }

        /// <summary>
        /// Confirms partial DBML metadata is preserved where deterministic and missing DataContext identity is represented as explicit unknown data.
        /// </summary>
        [Fact]
        public void ExtractModelsPartialDbmlAsWarningsAndUnknowns()
        {
            // Partial XML is valid enough to preserve known facts, but the missing DataContext class must not be silently treated as certain.
            string repositoryRoot = CreateRepositoryWithDbml("src/Sample.Data/Partial.dbml", PartialDbml);
            LinqToSqlDbmlExtractionResult result = Extract(repositoryRoot);

            ArchitectureNode contextNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.LinqToSqlDataContext);
            Assert.True(contextNode.UnknownState.HasUnknownData);
            Assert.Equal(Confidence.Medium, contextNode.Confidence);
            Assert.Contains(result.Warnings, warning => warning.Contains("DataContext class", StringComparison.Ordinal));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Entity && node.DisplayName == "PartialEntity");
            Assert.DoesNotContain(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseTable);
        }

        /// <summary>
        /// Confirms malformed DBML files degrade to warnings without throwing, blocking extraction, or emitting unsupported partial facts from invalid XML.
        /// </summary>
        [Fact]
        public void ExtractReportsMalformedDbmlAsWarningWithoutErrors()
        {
            // Malformed XML cannot provide trustworthy model element locations, so the extractor records the issue and continues without graph facts.
            string repositoryRoot = CreateRepositoryWithDbml("src/Sample.Data/Broken.dbml", "<Database><Table Name=\"dbo.Broken\"></Database>");
            LinqToSqlDbmlExtractionResult result = Extract(repositoryRoot);

            Assert.Empty(result.Errors);
            Assert.Empty(result.Snapshot.Nodes);
            Assert.Contains(result.Warnings, warning => warning.Contains("Malformed DBML", StringComparison.Ordinal));
        }

        /// <summary>
        /// Confirms secret-like connection values are redacted from every externally visible extraction surface while preserving configuration-key evidence.
        /// </summary>
        [Fact]
        public void ExtractRedactsSecretConnectionValues()
        {
            // The DBML connection string intentionally contains credential-shaped text so the test can guard graph metadata, evidence, and diagnostics.
            string repositoryRoot = CreateRepositoryWithDbml("src/Sample.Data/Secrets.dbml", SecretDbml);
            LinqToSqlDbmlExtractionResult result = Extract(repositoryRoot);

            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.LinqToSqlDataContext && ContainsMetadata(node, "\"connectionStringKey\":\"SecretConnection\""));
            Assert.DoesNotContain(result.Snapshot.Nodes, node => ContainsSensitiveText(node.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Snapshot.Evidence, evidence => ContainsSensitiveText(evidence.SnippetPreview) || ContainsSensitiveText(evidence.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Warnings, ContainsSensitiveText);
            Assert.DoesNotContain(result.Errors, ContainsSensitiveText);
        }

        /// <summary>
        /// Determines whether a node metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="node">The architecture node whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the node metadata.</param>
        /// <returns><see langword="true" /> when the metadata contains the expected fragment; otherwise, <see langword="false" />.</returns>
        private static bool ContainsMetadata(ArchitectureNode node, string expectedFragment)
        {
            // Canonical JSON comparisons keep assertions deterministic without binding tests to dictionary construction order.
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
            // Edge metadata assertions verify relationship details while keeping stable-key assertions focused on graph identity.
            return edge.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether an evidence metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="evidence">The evidence record whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the evidence metadata.</param>
        /// <returns><see langword="true" /> when the metadata contains the expected fragment; otherwise, <see langword="false" />.</returns>
        private static bool ContainsMetadata(EvidenceRecord evidence, string expectedFragment)
        {
            // Evidence metadata assertions validate XML-specific location details that do not have normalized evidence fields.
            return evidence.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a value contains any known secret literal from the DBML fixtures.
        /// </summary>
        /// <param name="value">The value to inspect for fixture secrets.</param>
        /// <returns><see langword="true" /> when a sensitive fixture literal appears; otherwise, <see langword="false" />.</returns>
        private static bool ContainsSensitiveText(string? value)
        {
            // The redaction tests use exact fixture secrets and common connection-string credential labels as leak sentinels.
            return value?.Contains("SuperSecret", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("Password=", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("User Id=sa", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Creates a temporary repository containing one DBML file at the requested repository-relative path.
        /// </summary>
        /// <param name="relativePath">The repository-relative DBML file path to create.</param>
        /// <param name="dbmlContent">The DBML XML content to write.</param>
        /// <returns>The absolute temporary repository root directory.</returns>
        private static string CreateRepositoryWithDbml(string relativePath, string dbmlContent)
        {
            // Each fixture uses an isolated root so repository-relative path handling is validated without depending on checkout location.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-dbml-fixture", Guid.NewGuid().ToString("N"));
            string dbmlPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dbmlPath)!);
            File.WriteAllText(dbmlPath, dbmlContent);
            return repositoryRoot;
        }

        /// <summary>
        /// Runs the production DBML extractor for a fixture repository.
        /// </summary>
        /// <param name="repositoryRoot">The absolute repository root containing DBML fixture files.</param>
        /// <returns>The graph-ready DBML extraction result.</returns>
        private static LinqToSqlDbmlExtractionResult Extract(string repositoryRoot)
        {
            // Tests call the public extractor API so they cover request validation, file discovery, XML parsing, and snapshot accumulation together.
            LinqToSqlDbmlModelExtractor extractor = new();
            LinqToSqlDbmlExtractionRequest request = new(StableKeyGenerator.ForRepository("Sample.Repository"), repositoryRoot);
            return extractor.Extract(request, CancellationToken.None);
        }

        /// <summary>
        /// Gets a complete LINQ to SQL DBML model fixture with DataContext, database, table, columns, association, and stored-procedure metadata.
        /// </summary>
        private const string CompleteDbml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Database Name="Northwind" Class="NorthwindDataContext" xmlns="http://schemas.microsoft.com/linqtosql/dbml/2007">
              <Connection Mode="AppSettings" SettingsObjectName="Sample.Properties.Settings" SettingsPropertyName="NorthwindConnectionString" Provider="System.Data.SqlClient" />
              <Table Name="dbo.Customers" Member="Customers">
                <Type Name="Customer">
                  <Column Name="CustomerID" Member="CustomerID" Type="System.String" DbType="NChar(5) NOT NULL" IsPrimaryKey="true" CanBeNull="false" />
                  <Column Name="CompanyName" Member="CompanyName" Type="System.String" DbType="NVarChar(40) NOT NULL" CanBeNull="false" />
                  <Association Name="FK_Orders_Customers" Member="Orders" ThisKey="CustomerID" OtherKey="CustomerID" Type="Order" />
                </Type>
              </Table>
              <Table Name="dbo.Orders" Member="Orders">
                <Type Name="Order">
                  <Column Name="OrderID" Member="OrderID" Type="System.Int32" DbType="Int NOT NULL" IsPrimaryKey="true" CanBeNull="false" />
                  <Column Name="CustomerID" Member="CustomerID" Type="System.String" DbType="NChar(5)" CanBeNull="true" />
                </Type>
              </Table>
              <Function Name="dbo.GetCustomerOrders" Method="GetCustomerOrders">
                <Parameter Name="customerId" Parameter="customerId" Type="System.String" DbType="NChar(5)" />
              </Function>
            </Database>
            """;

        /// <summary>
        /// Gets a valid but partial DBML fixture that lacks DataContext and table identity metadata.
        /// </summary>
        private const string PartialDbml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Database Name="PartialDatabase" xmlns="http://schemas.microsoft.com/linqtosql/dbml/2007">
              <Table Member="PartialEntities">
                <Type Name="PartialEntity" />
              </Table>
            </Database>
            """;

        /// <summary>
        /// Gets a DBML fixture with a secret-like connection string and safe app-settings key metadata.
        /// </summary>
        private const string SecretDbml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Database Name="SecretDatabase" Class="SecretDataContext" xmlns="http://schemas.microsoft.com/linqtosql/dbml/2007">
              <Connection Mode="AppSettings" ConnectionString="Server=.;Database=Secret;User Id=sa;Password=SuperSecret;" SettingsPropertyName="SecretConnection" Provider="System.Data.SqlClient" />
            </Database>
            """;
    }
}
