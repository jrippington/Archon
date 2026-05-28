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
    /// Verifies the typed DataSet extraction slice for XSD models, generated source, TableAdapters, queries, stored procedures, usage sites, evidence, confidence, unknowns, redaction, and deduplication.
    /// </summary>
    public sealed class TypedDataSetExtractorTests
    {
        /// <summary>
        /// Confirms typed DataSet XSD artifacts emit deterministic DataSet, DataTable, TableAdapter, query, stored-procedure, table, raw SQL, and evidence facts.
        /// </summary>
        [Fact]
        public void TypedDataSetExtractionParsesXsdTablesAdaptersQueriesStoredProceduresAndRedactsSecrets()
        {
            // The XSD fixture is intentionally self-contained so extraction proves that model artifacts are parsed without generated code or database access.
            LinqToSqlDbmlExtractionResult result = ExtractFixture("src/Sample.TypedDataSet/SalesDataSet.xsd", CompleteTypedDataSetXsd, []);

            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Entity && node.StableKey.Value == "entity://src/Sample.TypedDataSet/SalesDataSet.xsd#SalesDataSet" && ContainsMetadata(node, "\"dataAccessTechnology\":\"TypedDataSet\"") && ContainsMetadata(node, "\"contextType\":\"TypedDataSet\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseTable && node.StableKey.Value == "dbtable://src/Sample.TypedDataSet/SalesDataSet.xsd#sales.Customers" && ContainsMetadata(node, "\"tableAdapterName\":\"CustomersTableAdapter\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseColumn && node.StableKey.Value == "dbcolumn://src/Sample.TypedDataSet/SalesDataSet.xsd#sales.Customers.CustomerID" && ContainsMetadata(node, "\"columnName\":\"CustomerID\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.StoredProcedure && node.StableKey.Value == "storedprocedure://src/Sample.TypedDataSet/SalesDataSet.xsd#dbo.GetCustomer" && ContainsMetadata(node, "\"storedProcedureName\":\"GetCustomer\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.SqlScript && ContainsMetadata(node, "\"queryName\":\"FindByName\"") && ContainsMetadata(node, "\"sqlTextHash\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.MapsTable && edge.SourceNodeStableKey.Value == "entity://src/Sample.TypedDataSet/SalesDataSet.xsd#SalesDataSet.CustomersDataTable" && edge.TargetNodeStableKey.Value == "dbtable://src/Sample.TypedDataSet/SalesDataSet.xsd#sales.Customers");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.CallsStoredProcedure && edge.TargetNodeStableKey.Value == "storedprocedure://src/Sample.TypedDataSet/SalesDataSet.xsd#dbo.GetCustomer" && ContainsMetadata(edge, "\"commandType\":\"StoredProcedure\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ExecutesRawSql && ContainsMetadata(edge, "\"queryName\":\"FindByName\"") && ContainsMetadata(edge, "\"readWriteHint\":\"Read\""));
            Assert.Single(result.Snapshot.Nodes, node => node.StableKey.Value == "dbtable://src/Sample.TypedDataSet/SalesDataSet.xsd#sales.Customers");
            Assert.Contains(result.Snapshot.Evidence, evidence => evidence.EvidenceKind == EvidenceKind.Dbml && evidence.FilePath.Value == "src/Sample.TypedDataSet/SalesDataSet.xsd" && evidence.SnippetPreview?.Contains("DataSetName=\"SalesDataSet\"", StringComparison.Ordinal) == true);
            AssertDoesNotLeakSecrets(result);
        }

        /// <summary>
        /// Confirms generated typed DataSet source and application usage are correlated with XSD model facts through deterministic names and file relationships.
        /// </summary>
        [Fact]
        public void TypedDataSetExtractionCorrelatesGeneratedSourceAndUsageSites()
        {
            // The source fixture mimics generated typed DataSet and TableAdapter classes plus consumer code that uses a query and stored-procedure wrapper.
            LinqToSqlDbmlExtractionResult result = ExtractFixture(
                "src/Sample.TypedDataSet/SalesDataSet.xsd",
                CompleteTypedDataSetXsd,
                [CreateSourceFixture("src/Sample.TypedDataSet/SalesDataSet.Designer.cs", GeneratedTypedDataSetSource), CreateSourceFixture("src/Sample.TypedDataSet/CustomerService.cs", TypedDataSetUsageSource)]);

            Assert.Empty(result.Errors);
            ArchitectureNode usageMethod = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "Load");
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.GeneratedArtifact && node.StableKey.Value == "generatedartifact://src/Sample.TypedDataSet/SalesDataSet.Designer.cs#Sample.TypedDataSet.SalesDataSet" && ContainsMetadata(node, "\"generatedFilePath\":\"src/Sample.TypedDataSet/SalesDataSet.Designer.cs\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.References && edge.SourceNodeStableKey.Value == "generatedartifact://src/Sample.TypedDataSet/SalesDataSet.Designer.cs#Sample.TypedDataSet.SalesDataSet" && edge.TargetNodeStableKey.Value == "entity://src/Sample.TypedDataSet/SalesDataSet.xsd#SalesDataSet");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ReadsTable && edge.SourceNodeStableKey == usageMethod.StableKey && edge.TargetNodeStableKey.Value == "dbtable://src/Sample.TypedDataSet/SalesDataSet.xsd#sales.Customers" && ContainsMetadata(edge, "\"tableAdapterName\":\"CustomersTableAdapter\"") && ContainsMetadata(edge, "\"queryName\":\"FindByName\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.CallsStoredProcedure && edge.SourceNodeStableKey == usageMethod.StableKey && edge.TargetNodeStableKey.Value == "storedprocedure://src/Sample.TypedDataSet/SalesDataSet.xsd#dbo.GetCustomer" && ContainsMetadata(edge, "\"queryName\":\"GetCustomer\""));
            Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseTable && node.StableKey.Value == "dbtable://src/Sample.TypedDataSet/SalesDataSet.xsd#sales.Customers");
        }

        /// <summary>
        /// Confirms malformed and partial typed DataSet XSD artifacts produce warnings and explicit unknowns while preserving available model evidence.
        /// </summary>
        [Fact]
        public void TypedDataSetExtractionReportsMalformedAndPartialXsdArtifactsAsWarningsAndUnknowns()
        {
            // Partial model XML should contribute known DataSet identity while incomplete table metadata remains an explicit unknown rather than a guessed database table.
            LinqToSqlDbmlExtractionResult partialResult = ExtractFixture("src/Sample.TypedDataSet/PartialDataSet.xsd", PartialTypedDataSetXsd, []);

            ArchitectureNode dataSetNode = Assert.Single(partialResult.Snapshot.Nodes, node => node.NodeKind == NodeKind.Entity && node.DisplayName == "PartialDataSet");
            Assert.True(dataSetNode.UnknownState.HasUnknownData);
            Assert.Contains(partialResult.Warnings, warning => warning.Contains("typed DataSet", StringComparison.OrdinalIgnoreCase) && warning.Contains("partial", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(partialResult.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseTable);

            // Malformed XML cannot supply trustworthy model facts, so it must degrade to warnings without throwing or blocking the extraction result.
            LinqToSqlDbmlExtractionResult malformedResult = ExtractFixture("src/Sample.TypedDataSet/BrokenDataSet.xsd", "<xs:schema><xs:element name=\"Broken\"></xs:schema>", []);

            Assert.Empty(malformedResult.Errors);
            Assert.Empty(malformedResult.Snapshot.Nodes);
            Assert.Contains(malformedResult.Warnings, warning => warning.Contains("Malformed typed DataSet XSD", StringComparison.Ordinal));
        }

        /// <summary>
        /// Determines whether a node metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="node">The architecture node whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the node metadata.</param>
        /// <returns><see langword="true" /> when the metadata contains the expected fragment; otherwise, <see langword="false" />.</returns>
        private static bool ContainsMetadata(ArchitectureNode node, string expectedFragment)
        {
            // Canonical JSON assertions keep typed DataSet tests focused on emitted metadata values rather than dictionary ordering.
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
            // Edge metadata assertions cover TableAdapter/query names, command types, and read/write hints.
            return edge.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Runs the production data-access extractor against one XSD fixture and optional semantic source fixtures.
        /// </summary>
        /// <param name="xsdRelativePath">The repository-relative XSD artifact path.</param>
        /// <param name="xsdContent">The typed DataSet XSD content.</param>
        /// <param name="sourceFixtures">The semantic source fixtures to include in extraction.</param>
        /// <returns>The extraction result containing graph-ready typed DataSet facts.</returns>
        private static LinqToSqlDbmlExtractionResult ExtractFixture(string xsdRelativePath, string xsdContent, IReadOnlyList<SourceFixture> sourceFixtures)
        {
            // Fixtures use isolated temporary repositories so repository-relative XSD and source correlation is covered without relying on checkout paths.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-typed-dataset-fixture", Guid.NewGuid().ToString("N"));
            string absoluteXsdPath = Path.Combine(repositoryRoot, xsdRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteXsdPath)!);
            File.WriteAllText(absoluteXsdPath, xsdContent);

            List<SemanticExtractionRequest> semanticDocuments = [];
            foreach (SourceFixture sourceFixture in sourceFixtures)
            {
                string absoluteSourcePath = Path.Combine(repositoryRoot, sourceFixture.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(absoluteSourcePath)!);
                File.WriteAllText(absoluteSourcePath, sourceFixture.Source);
                semanticDocuments.Add(CreateSemanticRequest(repositoryRoot, "src/Sample.TypedDataSet/Sample.TypedDataSet.csproj", absoluteSourcePath, sourceFixture.Source));
            }

            LinqToSqlDbmlModelExtractor extractor = new();
            LinqToSqlDbmlExtractionRequest request = new(StableKeyGenerator.ForRepository("Sample.Repository"), repositoryRoot, semanticDocuments);
            return extractor.Extract(request, CancellationToken.None);
        }

        /// <summary>
        /// Creates a semantic extraction request for a source fixture using Roslyn references sufficient for typed DataSet usage analysis.
        /// </summary>
        /// <param name="repositoryRoot">The absolute repository root for evidence normalization.</param>
        /// <param name="projectContext">The repository-relative project context used by stable keys.</param>
        /// <param name="sourcePath">The absolute source path associated with the syntax tree.</param>
        /// <param name="source">The C# source text to parse and compile.</param>
        /// <returns>A semantic extraction request for the supplied source document.</returns>
        private static SemanticExtractionRequest CreateSemanticRequest(string repositoryRoot, string projectContext, string sourcePath, string source)
        {
            // Test compilations use framework references required by typed DataSet base types and partial-class usage analysis.
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(sourcePath),
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Data.DataSet).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Data.DataTable).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.ComponentModel.Component).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            return new SemanticExtractionRequest(repositoryRoot, projectContext, sourcePath, syntaxTree, semanticModel);
        }

        /// <summary>
        /// Creates a source fixture value object.
        /// </summary>
        /// <param name="relativePath">The repository-relative source path.</param>
        /// <param name="source">The source text to compile.</param>
        /// <returns>A source fixture value.</returns>
        private static SourceFixture CreateSourceFixture(string relativePath, string source)
        {
            // A small helper keeps fixture calls readable while retaining explicit path and source data.
            return new SourceFixture(relativePath, source);
        }

        /// <summary>
        /// Verifies that extraction output does not expose fixture secret literals in metadata, evidence, warnings, or errors.
        /// </summary>
        /// <param name="result">The extraction result to inspect for secret leakage.</param>
        private static void AssertDoesNotLeakSecrets(LinqToSqlDbmlExtractionResult result)
        {
            // Redaction guards XSD connection strings and SQL command literals before facts reach graph contracts.
            Assert.DoesNotContain(result.Snapshot.Nodes, node => ContainsSensitiveText(node.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Snapshot.Edges, edge => ContainsSensitiveText(edge.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Snapshot.Evidence, evidence => ContainsSensitiveText(evidence.SnippetPreview) || ContainsSensitiveText(evidence.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Warnings, ContainsSensitiveText);
            Assert.DoesNotContain(result.Errors, ContainsSensitiveText);
        }

        /// <summary>
        /// Determines whether a value contains any known secret literal from the typed DataSet fixtures.
        /// </summary>
        /// <param name="value">The value to inspect for fixture secrets.</param>
        /// <returns><see langword="true" /> when a sensitive fixture literal appears; otherwise, <see langword="false" />.</returns>
        private static bool ContainsSensitiveText(string? value)
        {
            // The checks use exact fixture literals and common credential labels as leak sentinels.
            return value?.Contains("SuperSecret", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("Password=", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("User Id=sa", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("token-123", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Carries a repository-relative source path and source text for semantic fixture creation.
        /// </summary>
        /// <param name="RelativePath">The repository-relative source path.</param>
        /// <param name="Source">The source text to compile.</param>
        private sealed record SourceFixture(string RelativePath, string Source);

        /// <summary>
        /// Gets a representative typed DataSet XSD fixture containing DataSet, DataTable, column, TableAdapter, query, stored procedure, connection, and secret-like command evidence.
        /// </summary>
        private const string CompleteTypedDataSetXsd = """
            <?xml version="1.0" encoding="utf-8"?>
            <xs:schema id="SalesDataSet"
                       targetNamespace="http://tempuri.org/SalesDataSet.xsd"
                       xmlns="http://tempuri.org/SalesDataSet.xsd"
                       xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       xmlns:msdata="urn:schemas-microsoft-com:xml-msdata"
                       xmlns:msprop="urn:schemas-microsoft-com:xml-msprop"
                       xmlns:msdatasource="urn:schemas-microsoft-com:xml-msdatasource">
              <xs:element name="SalesDataSet" msdata:IsDataSet="true" msdata:DataSetName="SalesDataSet">
                <xs:complexType>
                  <xs:choice maxOccurs="unbounded">
                    <xs:element name="Customers" msprop:Generator_TableClassName="CustomersDataTable" msprop:DbTableName="sales.Customers">
                      <xs:complexType>
                        <xs:sequence>
                          <xs:element name="CustomerID" type="xs:int" minOccurs="0" />
                          <xs:element name="Name" type="xs:string" minOccurs="0" />
                        </xs:sequence>
                      </xs:complexType>
                    </xs:element>
                  </xs:choice>
                </xs:complexType>
              </xs:element>
              <msdatasource:DataSource>
                <msdatasource:Connection ConnectionStringObject="Settings" ConnectionStringProperty="SalesConnection" ConnectionString="Server=.;Database=Sales;User Id=sa;Password=SuperSecret" />
                <msdatasource:TableAdapter Name="CustomersTableAdapter" DataTableName="Customers" GeneratorDataComponentClassName="CustomersTableAdapter">
                  <msdatasource:MainSource>
                    <msdatasource:DbSource CommandType="Text" CommandText="SELECT CustomerID, Name FROM sales.Customers WHERE Token = 'token-123'" />
                  </msdatasource:MainSource>
                  <msdatasource:DbSource Name="FindByName" CommandType="Text" CommandText="SELECT CustomerID, Name FROM sales.Customers WHERE Name = @name" />
                  <msdatasource:DbSource Name="GetCustomer" CommandType="StoredProcedure" CommandText="dbo.GetCustomer" />
                </msdatasource:TableAdapter>
              </msdatasource:DataSource>
            </xs:schema>
            """;

        /// <summary>
        /// Gets a partial typed DataSet XSD fixture that has DataSet identity but lacks deterministic table identity.
        /// </summary>
        private const string PartialTypedDataSetXsd = """
            <?xml version="1.0" encoding="utf-8"?>
            <xs:schema id="PartialDataSet" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
              <xs:element name="PartialDataSet" msdata:IsDataSet="true" msdata:DataSetName="PartialDataSet">
                <xs:complexType>
                  <xs:choice maxOccurs="unbounded">
                    <xs:element name="UnknownRows" />
                  </xs:choice>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """;

        /// <summary>
        /// Gets generated typed DataSet source that should correlate with the XSD model artifact.
        /// </summary>
        private const string GeneratedTypedDataSetSource = """
            namespace Sample.TypedDataSet
            {
                public partial class SalesDataSet : System.Data.DataSet
                {
                    public CustomersDataTable Customers { get; }

                    public partial class CustomersDataTable : System.Data.DataTable
                    {
                    }
                }
            }

            namespace Sample.TypedDataSet.SalesDataSetTableAdapters
            {
                public partial class CustomersTableAdapter : System.ComponentModel.Component
                {
                    public virtual Sample.TypedDataSet.SalesDataSet.CustomersDataTable GetData()
                    {
                        return new Sample.TypedDataSet.SalesDataSet.CustomersDataTable();
                    }

                    public virtual Sample.TypedDataSet.SalesDataSet.CustomersDataTable FindByName(string name)
                    {
                        return new Sample.TypedDataSet.SalesDataSet.CustomersDataTable();
                    }

                    public virtual int GetCustomer(int customerId)
                    {
                        return 0;
                    }
                }
            }
            """;

        /// <summary>
        /// Gets application usage source that consumes a generated TableAdapter and typed DataSet table.
        /// </summary>
        private const string TypedDataSetUsageSource = """
            namespace Sample.TypedDataSet
            {
                using Sample.TypedDataSet.SalesDataSetTableAdapters;

                public sealed class CustomerService
                {
                    public void Load()
                    {
                        SalesDataSet dataSet = new();
                        CustomersTableAdapter adapter = new();
                        dataSet.Customers.Merge(adapter.FindByName("Alice"));
                        adapter.GetCustomer(42);
                    }
                }
            }
            """;
    }
}
