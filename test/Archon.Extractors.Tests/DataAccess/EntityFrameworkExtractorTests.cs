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
    /// Verifies the Entity Framework extraction slice for EF6, EF Core, mapping facts, migrations, providers, source usage, raw SQL, warnings, unknowns, and redaction.
    /// </summary>
    public sealed class EntityFrameworkExtractorTests
    {
        /// <summary>
        /// Confirms EF6 source artifacts emit context, ObjectContext, entity, table, column, migration, provider, raw SQL, save-call, and unknown convention facts.
        /// </summary>
        [Fact]
        public void ExtractDetectsEf6ContextsMappingsMigrationsProvidersUsageAndUnknowns()
        {
            // The EF6 fixture uses source-level framework stubs so the extractor can recognize legacy namespaces and symbols without requiring external EF packages during tests.
            LinqToSqlDbmlExtractionResult result = ExtractFixture("src/Sample.Ef6/Sample.Ef6.csproj", "src/Sample.Ef6/LegacyModel.cs", Ef6Source);

            Assert.Empty(result.Errors);
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DbContext && node.StableKey.Value == "dbcontext://src/Sample.Ef6/Sample.Ef6.csproj#Sample.Ef6.LegacyContext" && ContainsMetadata(node, "\"dataAccessTechnology\":\"EntityFramework6\"") && ContainsMetadata(node, "\"connectionStringKey\":\"LegacyConnection\"") && ContainsMetadata(node, "\"provider\":\"SqlServer\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DbContext && node.StableKey.Value == "dbcontext://src/Sample.Ef6/Sample.Ef6.csproj#Sample.Ef6.LegacyObjectContext" && ContainsMetadata(node, "\"contextKind\":\"ObjectContext\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Entity && node.StableKey.Value == "entity://src/Sample.Ef6/Sample.Ef6.csproj#Sample.Ef6.LegacyCustomer" && ContainsMetadata(node, "\"entityType\":\"Sample.Ef6.LegacyCustomer\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseTable && node.StableKey.Value == "dbtable://src/Sample.Ef6/Sample.Ef6.csproj#sales.LegacyCustomers" && ContainsMetadata(node, "\"schemaName\":\"sales\"") && ContainsMetadata(node, "\"tableName\":\"LegacyCustomers\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseColumn && node.StableKey.Value == "dbcolumn://src/Sample.Ef6/Sample.Ef6.csproj#sales.LegacyCustomers.Name" && ContainsMetadata(node, "\"propertyName\":\"Name\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.GeneratedArtifact && ContainsMetadata(node, "\"migrationName\":\"AddLegacyCustomer\"") && ContainsMetadata(node, "\"migrationOperation\":\"CreateTable\""));
            ArchitectureNode methodNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "Run");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesDbContext && edge.SourceNodeStableKey == methodNode.StableKey && edge.TargetNodeStableKey.Value == "dbcontext://src/Sample.Ef6/Sample.Ef6.csproj#Sample.Ef6.LegacyContext");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ReadsTable && edge.SourceNodeStableKey == methodNode.StableKey && edge.TargetNodeStableKey.Value == "dbtable://src/Sample.Ef6/Sample.Ef6.csproj#sales.LegacyCustomers");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.WritesTable && edge.SourceNodeStableKey == methodNode.StableKey && ContainsMetadata(edge, "\"commandApi\":\"SaveChanges\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ExecutesRawSql && edge.SourceNodeStableKey == methodNode.StableKey && ContainsMetadata(edge, "\"commandApi\":\"SqlQuery\"") && ContainsMetadata(edge, "\"readWriteHint\":\"Read\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ExecutesRawSql && edge.SourceNodeStableKey == methodNode.StableKey && ContainsMetadata(edge, "\"commandApi\":\"ExecuteSqlCommand\"") && edge.UnknownState.HasUnknownData);
            Assert.Contains(result.Warnings, warning => warning.Contains("computed", StringComparison.OrdinalIgnoreCase));
            AssertDoesNotLeakSecrets(result);
        }

        /// <summary>
        /// Confirms EF Core source artifacts emit context, entity, Fluent API mapping, migration, provider, raw SQL, async save-call, relationship, and shadow-property unknown facts.
        /// </summary>
        [Fact]
        public void ExtractDetectsEfCoreContextsMappingsMigrationsProvidersUsageAndUnknowns()
        {
            // The EF Core fixture exercises the modern provider setup and raw SQL API names documented by Microsoft while remaining entirely static and package-free.
            LinqToSqlDbmlExtractionResult result = ExtractFixture("src/Sample.EfCore/Sample.EfCore.csproj", "src/Sample.EfCore/ModernModel.cs", EfCoreSource);

            Assert.Empty(result.Errors);
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DbContext && node.StableKey.Value == "dbcontext://src/Sample.EfCore/Sample.EfCore.csproj#Sample.EfCore.ModernContext" && ContainsMetadata(node, "\"dataAccessTechnology\":\"EntityFrameworkCore\"") && ContainsMetadata(node, "\"providerConfigurationCall\":\"UseSqlServer\"") && ContainsMetadata(node, "\"connectionStringKey\":\"ModernConnection\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Entity && node.StableKey.Value == "entity://src/Sample.EfCore/Sample.EfCore.csproj#Sample.EfCore.ModernCustomer");
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseTable && node.StableKey.Value == "dbtable://src/Sample.EfCore/Sample.EfCore.csproj#crm.ModernCustomers");
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseColumn && node.StableKey.Value == "dbcolumn://src/Sample.EfCore/Sample.EfCore.csproj#crm.ModernCustomers.DisplayName");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.MapsEntity && edge.SourceNodeStableKey.Value == "dbcontext://src/Sample.EfCore/Sample.EfCore.csproj#Sample.EfCore.ModernContext" && edge.TargetNodeStableKey.Value == "entity://src/Sample.EfCore/Sample.EfCore.csproj#Sample.EfCore.ModernCustomer");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.MapsEntity && ContainsMetadata(edge, "\"dataAccessRelationshipKind\":\"FluentRelationship\"") && ContainsMetadata(edge, "\"targetEntityType\":\"ModernOrder\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.GeneratedArtifact && ContainsMetadata(node, "\"migrationName\":\"AddModernCustomer\"") && ContainsMetadata(node, "\"migrationOperation\":\"CreateTable\""));
            ArchitectureNode methodNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "RunAsync");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesDbContext && edge.SourceNodeStableKey == methodNode.StableKey && edge.TargetNodeStableKey.Value == "dbcontext://src/Sample.EfCore/Sample.EfCore.csproj#Sample.EfCore.ModernContext");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ReadsTable && edge.SourceNodeStableKey == methodNode.StableKey && edge.TargetNodeStableKey.Value == "dbtable://src/Sample.EfCore/Sample.EfCore.csproj#crm.ModernCustomers");
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.WritesTable && edge.SourceNodeStableKey == methodNode.StableKey && ContainsMetadata(edge, "\"commandApi\":\"SaveChangesAsync\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ExecutesRawSql && edge.SourceNodeStableKey == methodNode.StableKey && ContainsMetadata(edge, "\"commandApi\":\"FromSqlRaw\"") && ContainsMetadata(edge, "\"readWriteHint\":\"Read\""));
            Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.ExecutesRawSql && edge.SourceNodeStableKey == methodNode.StableKey && ContainsMetadata(edge, "\"commandApi\":\"ExecuteSqlRaw\"") && ContainsMetadata(edge, "\"readWriteHint\":\"Write\""));
            Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.DatabaseColumn && node.UnknownState.HasUnknownData && ContainsMetadata(node, "\"dataAccessUnknownReason\":\"ShadowProperty\""));
            AssertDoesNotLeakSecrets(result);
        }

        /// <summary>
        /// Determines whether a node metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="node">The architecture node whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the node metadata.</param>
        /// <returns><see langword="true" /> when the metadata contains the expected fragment; otherwise, <see langword="false" />.</returns>
        private static bool ContainsMetadata(ArchitectureNode node, string expectedFragment)
        {
            // Canonical JSON assertions verify EF-specific metadata while avoiding test coupling to dictionary construction order.
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
            // Edge metadata assertions cover relationship subtype, command API, and read/write hint classification.
            return edge.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Runs the production data-access extractor against one semantic source fixture.
        /// </summary>
        /// <param name="projectContext">The repository-relative project context used by stable keys.</param>
        /// <param name="documentPath">The repository-relative source document path.</param>
        /// <param name="source">The C# source fixture to compile and inspect.</param>
        /// <returns>The extraction result containing graph-ready EF facts.</returns>
        private static LinqToSqlDbmlExtractionResult ExtractFixture(string projectContext, string documentPath, string source)
        {
            // Fixtures use isolated temporary repositories so repository-relative path normalization is covered without depending on the checkout location.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-ef-fixture", Guid.NewGuid().ToString("N"));
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
            // The test compilation deliberately relies on in-source EF stubs, which keeps detection static and avoids NuGet restore variability.
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(sourcePath),
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location), MetadataReference.CreateFromFile(typeof(Task).Assembly.Location)],
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
            // Redaction is a data-access safety requirement because source fixtures may contain connection-string literals that look like real credentials.
            Assert.DoesNotContain(result.Snapshot.Nodes, node => ContainsSensitiveText(node.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Snapshot.Edges, edge => ContainsSensitiveText(edge.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Snapshot.Evidence, evidence => ContainsSensitiveText(evidence.SnippetPreview) || ContainsSensitiveText(evidence.Metadata.ToCanonicalJson()));
            Assert.DoesNotContain(result.Warnings, ContainsSensitiveText);
            Assert.DoesNotContain(result.Errors, ContainsSensitiveText);
        }

        /// <summary>
        /// Determines whether a value contains any known secret literal from the EF fixtures.
        /// </summary>
        /// <param name="value">The value to inspect for fixture secrets.</param>
        /// <returns><see langword="true" /> when a sensitive fixture literal appears; otherwise, <see langword="false" />.</returns>
        private static bool ContainsSensitiveText(string? value)
        {
            // The checks use exact fixture literals and common connection-string credential labels as leak sentinels.
            return value?.Contains("SuperSecret", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("Password=", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("User Id=sa", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Gets a static EF6 fixture covering legacy context APIs, mapping attributes, migrations, providers, raw SQL, and save operations.
        /// </summary>
        private const string Ef6Source = """
            namespace System.ComponentModel.DataAnnotations.Schema
            {
                using System;
                [AttributeUsage(AttributeTargets.Class)] public sealed class TableAttribute : Attribute { public TableAttribute(string name) { Name = name; } public string Name { get; } public string? Schema { get; set; } }
                [AttributeUsage(AttributeTargets.Property)] public sealed class ColumnAttribute : Attribute { public ColumnAttribute(string name) { Name = name; } public string Name { get; } }
            }

            namespace System.Data.Entity
            {
                using System.Collections.Generic;
                using System.Threading.Tasks;
                public class DbContext { public DbContext() { } public DbContext(string nameOrConnectionString) { } public Database Database { get; } = new(); public int SaveChanges() => 0; public Task<int> SaveChangesAsync() => Task.FromResult(0); }
                public class DbSet<TEntity> : List<TEntity> where TEntity : class { public IEnumerable<TEntity> SqlQuery(string sql, params object[] parameters) => this; public void Add(TEntity entity) { } public void Remove(TEntity entity) { } }
                public class Database { public int ExecuteSqlCommand(string sql, params object[] parameters) => 0; }
                public class DbModelBuilder { public EntityTypeConfiguration<TEntity> Entity<TEntity>() where TEntity : class => new(); }
                public class EntityTypeConfiguration<TEntity> where TEntity : class { public EntityTypeConfiguration<TEntity> ToTable(string tableName, string schemaName) => this; public EntityTypeConfiguration<TEntity> Property<TProperty>(System.Linq.Expressions.Expression<System.Func<TEntity, TProperty>> propertyExpression) => this; public EntityTypeConfiguration<TEntity> HasColumnName(string columnName) => this; public EntityTypeConfiguration<TEntity> HasMany<TTarget>(System.Linq.Expressions.Expression<System.Func<TEntity, object>> navigationExpression) where TTarget : class => this; }
                public class DbConfiguration { protected void SetProviderServices(string invariantName, object providerServices) { } }
            }

            namespace System.Data.Entity.Core.Objects
            {
                public class ObjectContext { public ObjectContext(string connectionString) { } }
            }

            namespace System.Data.Entity.Migrations
            {
                using System;
                public abstract class DbMigration { protected void CreateTable(string name, Func<ColumnBuilder, object> columns) { } protected void Sql(string sql) { } }
                public sealed class ColumnBuilder { public object Int(bool nullable = true) => new(); public object String(int maxLength = 0, bool nullable = true) => new(); }
            }

            namespace Sample.Ef6
            {
                using System.ComponentModel.DataAnnotations.Schema;
                using System.Data.Entity;
                using System.Data.Entity.Core.Objects;
                using System.Data.Entity.Migrations;
                using System.Linq;

                public sealed class LegacyContext : DbContext
                {
                    public LegacyContext() : base("name=LegacyConnection") { }
                    public DbSet<LegacyCustomer> Customers { get; set; } = new();
                    protected void OnModelCreating(DbModelBuilder modelBuilder)
                    {
                        modelBuilder.Entity<LegacyCustomer>().ToTable("LegacyCustomers", "sales");
                        modelBuilder.Entity<LegacyCustomer>().Property(customer => customer.Name).HasColumnName("Name");
                    }
                }

                public sealed class LegacyObjectContext : ObjectContext
                {
                    public LegacyObjectContext() : base("name=LegacyObjectConnection") { }
                }

                public sealed class LegacyConfiguration : DbConfiguration
                {
                    public LegacyConfiguration()
                    {
                        SetProviderServices("System.Data.SqlClient", new object());
                    }
                }

                [Table("LegacyCustomers", Schema = "sales")]
                public sealed class LegacyCustomer
                {
                    [Column("Name")]
                    public string? Name { get; set; }
                    public int Id { get; set; }
                }

                public sealed class AddLegacyCustomer : DbMigration
                {
                    public void Up()
                    {
                        CreateTable("sales.LegacyCustomers", c => new { Id = c.Int(nullable: false), Name = c.String(maxLength: 200) });
                        Sql("CREATE PROCEDURE sales.GetLegacyCustomers AS SELECT * FROM sales.LegacyCustomers");
                    }
                }

                public sealed class LegacyRepository
                {
                    public void Run(string sql)
                    {
                        var context = new LegacyContext();
                        var customers = context.Customers.Where(customer => customer.Name != null).ToList();
                        context.Customers.Add(new LegacyCustomer());
                        context.SaveChanges();
                        context.Customers.SqlQuery("SELECT * FROM sales.LegacyCustomers WHERE Password='SuperSecret'").ToList();
                        context.Database.ExecuteSqlCommand(sql);
                    }
                }
            }
            """;

        /// <summary>
        /// Gets a static EF Core fixture covering modern context APIs, Fluent API mapping, migrations, providers, raw SQL, async saves, relationships, and shadow properties.
        /// </summary>
        private const string EfCoreSource = """
            namespace Microsoft.EntityFrameworkCore
            {
                using System.Collections.Generic;
                using System.Threading.Tasks;
                public class DbContext { public DbContext() { } public DbContext(DbContextOptions options) { } public DatabaseFacade Database { get; } = new(); public int SaveChanges() => 0; public Task<int> SaveChangesAsync() => Task.FromResult(0); protected virtual void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { } protected virtual void OnModelCreating(ModelBuilder modelBuilder) { } }
                public class DbContextOptions { }
                public class DbContextOptions<TContext> : DbContextOptions where TContext : DbContext { }
                public class DbContextOptionsBuilder { public DbContextOptionsBuilder UseSqlServer(string connectionString) => this; public DbContextOptionsBuilder UseSqlite(string connectionString) => this; public DbContextOptionsBuilder UseNpgsql(string connectionString) => this; }
                public class DbSet<TEntity> : List<TEntity> where TEntity : class { public DbSet<TEntity> FromSqlRaw(string sql, params object[] parameters) => this; public void Add(TEntity entity) { } public void Remove(TEntity entity) { } }
                public class DatabaseFacade { public int ExecuteSqlRaw(string sql, params object[] parameters) => 0; }
                public class ModelBuilder { public EntityTypeBuilder<TEntity> Entity<TEntity>() where TEntity : class => new(); }
                public class EntityTypeBuilder<TEntity> where TEntity : class { public EntityTypeBuilder<TEntity> ToTable(string tableName, string schemaName) => this; public PropertyBuilder<TProperty> Property<TProperty>(System.Linq.Expressions.Expression<System.Func<TEntity, TProperty>> propertyExpression) => new(); public PropertyBuilder<TProperty> Property<TProperty>(string propertyName) => new(); public ReferenceCollectionBuilder<TEntity, TTarget> HasMany<TTarget>(System.Linq.Expressions.Expression<System.Func<TEntity, object>> navigationExpression) where TTarget : class => new(); }
                public class PropertyBuilder<TProperty> { public PropertyBuilder<TProperty> HasColumnName(string columnName) => this; }
                public class ReferenceCollectionBuilder<TEntity, TTarget> where TEntity : class where TTarget : class { public ReferenceCollectionBuilder<TEntity, TTarget> WithOne(System.Linq.Expressions.Expression<System.Func<TTarget, object>> navigationExpression) => this; }
                public abstract class Migration { protected abstract void Up(MigrationBuilder migrationBuilder); }
                public sealed class MigrationBuilder { public void CreateTable(string name, System.Action<TableBuilder> columns, string? schema = null) { } public void Sql(string sql) { } }
                public sealed class TableBuilder { public ColumnBuilder Column<TColumn>(string name) => new(); }
                public sealed class ColumnBuilder { }
            }

            namespace Sample.EfCore
            {
                using Microsoft.EntityFrameworkCore;
                using System.Linq;
                using System.Threading.Tasks;

                public sealed class ModernContext : DbContext
                {
                    public ModernContext(DbContextOptions<ModernContext> options) : base(options) { }
                    public DbSet<ModernCustomer> Customers { get; set; } = new();
                    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                    {
                        optionsBuilder.UseSqlServer("name=ModernConnection;User Id=sa;Password=SuperSecret");
                    }
                    protected override void OnModelCreating(ModelBuilder modelBuilder)
                    {
                        modelBuilder.Entity<ModernCustomer>().ToTable("ModernCustomers", "crm");
                        modelBuilder.Entity<ModernCustomer>().Property(customer => customer.DisplayName).HasColumnName("DisplayName");
                        modelBuilder.Entity<ModernCustomer>().Property<string>("ShadowCode").HasColumnName("ShadowCode");
                        modelBuilder.Entity<ModernCustomer>().HasMany<ModernOrder>(customer => customer.Orders).WithOne(order => order.Customer);
                    }
                }

                public sealed class ModernCustomer
                {
                    public int Id { get; set; }
                    public string? DisplayName { get; set; }
                    public System.Collections.Generic.List<ModernOrder> Orders { get; set; } = new();
                }

                public sealed class ModernOrder
                {
                    public int Id { get; set; }
                    public ModernCustomer? Customer { get; set; }
                }

                public sealed class AddModernCustomer : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.CreateTable("ModernCustomers", table => { }, schema: "crm");
                        migrationBuilder.Sql("CREATE PROCEDURE crm.GetModernCustomers AS SELECT * FROM crm.ModernCustomers");
                    }
                }

                public sealed class ModernRepository
                {
                    public async Task RunAsync(ModernContext context)
                    {
                        var customers = context.Customers.Where(customer => customer.DisplayName != null).ToList();
                        context.Customers.Add(new ModernCustomer());
                        await context.SaveChangesAsync();
                        var bySql = context.Customers.FromSqlRaw("SELECT * FROM crm.ModernCustomers WHERE Password='SuperSecret'").ToList();
                        context.Database.ExecuteSqlRaw("DELETE FROM crm.ModernCustomers WHERE Id = {0}", 5);
                    }
                }
            }
            """;
    }
}
