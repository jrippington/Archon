using Archon.Domain.Graph.Identity;
using Xunit;

namespace Archon.Domain.Tests.Graph.Identity
{
    /// <summary>
    /// Verifies stable-key value objects and generation rules for WP002 graph identity.
    /// </summary>
    public sealed class StableKeyTests
    {
        /// <summary>
        /// Supplies every required WP002 stable-key prefix together with a generated key using that prefix.
        /// </summary>
        public static TheoryData<string, StableKey> RequiredGeneratedPrefixes => new()
        {
            { "repository://", StableKeyGenerator.ForRepository("main") },
            { "solution://", StableKeyGenerator.ForSolution("src\\Product.sln") },
            { "project://", StableKeyGenerator.ForProject("src\\Customer.Api\\Customer.Api.csproj") },
            { "package://", StableKeyGenerator.ForPackage("Newtonsoft.Json") },
            { "namespace://", StableKeyGenerator.ForNamespace("Customer.Application") },
            { "type://", StableKeyGenerator.ForType("Customer.Application.CustomerService") },
            { "method://", StableKeyGenerator.ForMethod("Customer.Application.CustomerService.GetCustomerAsync(System.Int32)") },
            { "property://", StableKeyGenerator.ForProperty("Customer.Application.Customer.Name") },
            { "field://", StableKeyGenerator.ForField("Customer.Application.Customer._name") },
            { "endpoint://", StableKeyGenerator.ForEndpoint("GET", "/api/customers/{id}") },
            { "controller://", StableKeyGenerator.ForController("Customer.Api.Controllers.CustomerController") },
            { "hostedservice://", StableKeyGenerator.ForHostedService("Customer.Worker.CustomerSyncService") },
            { "config://", StableKeyGenerator.ForConfigurationKey("ConnectionStrings:CustomerDatabase") },
            { "dbcontext://", StableKeyGenerator.ForDbContext("Customer.Data.CustomerDbContext") },
            { "linqtosql://", StableKeyGenerator.ForLinqToSqlDataContext("Legacy.Data.CustomerDataContext") },
            { "entity://", StableKeyGenerator.ForEntity("Customer.Data.Customer") },
            { "dbtable://", StableKeyGenerator.ForDatabaseTable("dbo", "Customer") },
            { "dbcolumn://", StableKeyGenerator.ForDatabaseColumn("dbo", "Customer", "CustomerId") },
            { "storedprocedure://", StableKeyGenerator.ForStoredProcedure("dbo", "GetCustomer") },
            { "externalservice://", StableKeyGenerator.ForExternalService("CustomerCreditService") },
            { "queue://", StableKeyGenerator.ForQueue("customer-updates") },
            { "topic://", StableKeyGenerator.ForTopic("customer-events") },
            { "file://", StableKeyGenerator.ForFile("database\\schema\\customer.sql") },
            { "pipeline://", StableKeyGenerator.ForPipeline(".github\\workflows\\build.yml") },
            { "rule://", StableKeyGenerator.ForRule("ARCHON001", "1.0.0") },
            { "finding://", StableKeyGenerator.ForFinding("snapshot://2026-05-20", "ARCHON001", "src/Customer.Api/Customer.Api.csproj") },
            { "metric://", StableKeyGenerator.ForMetric("snapshot://2026-05-20", "ProjectCount", "Graph") },
            { "summary://", StableKeyGenerator.ForSummary("snapshot://2026-05-20", "Graph", "ArchitectureOverview") }
        };

        /// <summary>
        /// Verifies a stable key preserves a valid non-empty external value.
        /// </summary>
        [Fact]
        public void StableKeyStoresStableExternalValue()
        {
            // StableKey is a value object around the durable external key string, not a database identifier.
            StableKey key = new("project://src/Customer.Api/Customer.Api.csproj");

            Assert.Equal("project://src/Customer.Api/Customer.Api.csproj", key.Value);
            Assert.Equal("project://src/Customer.Api/Customer.Api.csproj", key.ToString());
        }

        /// <summary>
        /// Verifies stable-key construction rejects invalid identity strings.
        /// </summary>
        /// <param name="value">The invalid stable-key string to test.</param>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void StableKeyRejectsNullEmptyOrWhitespace(string? value)
        {
            // Empty stable keys would make graph facts impossible to compare safely across snapshots.
            Assert.Throws<ArgumentException>(() => new StableKey(value));
        }

        /// <summary>
        /// Verifies equality is based on the stable key string rather than object identity.
        /// </summary>
        [Fact]
        public void StableKeyEqualityUsesStableExternalValue()
        {
            // Equivalent keys from different construction paths should compare equal because they identify the same logical graph fact.
            StableKey left = new("package://Newtonsoft.Json");
            StableKey right = StableKeyGenerator.ForPackage("Newtonsoft.Json");

            Assert.Equal(left, right);
            Assert.True(left == right);
            Assert.False(left != right);
        }

        /// <summary>
        /// Verifies repository-relative paths are normalized to machine-independent forward-slash form.
        /// </summary>
        [Fact]
        public void RepositoryRelativePathNormalizesSeparatorsAndRelativePrefix()
        {
            // The normalized value should not preserve Windows-only separators, repeated separators, or redundant leading relative prefixes.
            RepositoryRelativePath path = RepositoryRelativePath.Parse(".\\src\\Customer.Api//Customer.Api.csproj");

            Assert.Equal("src/Customer.Api/Customer.Api.csproj", path.Value);
            Assert.Equal("src/Customer.Api/Customer.Api.csproj", path.ToString());
        }

        /// <summary>
        /// Verifies repository-relative paths reject machine-specific absolute paths.
        /// </summary>
        /// <param name="path">The absolute or rooted path that must be rejected.</param>
        [Theory]
        [InlineData("D:\\Dev\\Archon\\src\\Customer.Api\\Customer.Api.csproj")]
        [InlineData("/home/user/archon/src/Customer.Api/Customer.Api.csproj")]
        [InlineData("\\\\server\\share\\Customer.Api.csproj")]
        public void RepositoryRelativePathRejectsAbsolutePaths(string path)
        {
            // Absolute paths would make generated keys differ across developer machines and CI agents.
            Assert.Throws<ArgumentException>(() => RepositoryRelativePath.Parse(path));
        }

        /// <summary>
        /// Verifies every required stable-key prefix is emitted by the shared generator.
        /// </summary>
        /// <param name="prefix">The required stable-key prefix from WP002.</param>
        /// <param name="key">A generated stable key that should use the prefix.</param>
        [Theory]
        [MemberData(nameof(RequiredGeneratedPrefixes))]
        public void StableKeyGeneratorEmitsEveryRequiredPrefix(string prefix, StableKey key)
        {
            // The prefix list prevents later extraction slices from inventing divergent key formats.
            Assert.StartsWith(prefix, key.Value, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies project key generation normalizes Windows and forward-slash path inputs identically.
        /// </summary>
        [Fact]
        public void StableKeyGeneratorNormalizesRepositoryRelativePathsDeterministically()
        {
            // Equivalent logical project paths should produce one stable key regardless of the developer's path separator style.
            StableKey windowsPathKey = StableKeyGenerator.ForProject("src\\Customer.Api\\Customer.Api.csproj");
            StableKey forwardSlashPathKey = StableKeyGenerator.ForProject("./src/Customer.Api/Customer.Api.csproj");

            Assert.Equal(windowsPathKey, forwardSlashPathKey);
            Assert.Equal("project://src/Customer.Api/Customer.Api.csproj", windowsPathKey.Value);
        }

        /// <summary>
        /// Verifies repeated equivalent input produces identical stable-key values.
        /// </summary>
        [Fact]
        public void StableKeyGeneratorIsDeterministicForEquivalentInput()
        {
            // Determinism allows later snapshots to compare graph facts without depending on database IDs.
            StableKey first = StableKeyGenerator.ForEndpoint("get", "api/customers/{id}");
            StableKey second = StableKeyGenerator.ForEndpoint(" GET ", "/api/customers/{id}");

            Assert.Equal(first, second);
            Assert.Equal("endpoint://GET:/api/customers/{id}", first.Value);
        }

        /// <summary>
        /// Verifies generator methods reject invalid input instead of producing ambiguous keys.
        /// </summary>
        [Fact]
        public void StableKeyGeneratorRejectsInvalidInput()
        {
            // A key missing a logical identity component would be ambiguous and unsafe to persist or compare.
            Assert.Throws<ArgumentException>(() => StableKeyGenerator.ForPackage("   "));
            Assert.Throws<ArgumentException>(() => StableKeyGenerator.ForDatabaseTable("dbo", "   "));
            Assert.Throws<ArgumentException>(() => StableKeyGenerator.ForProject("D:\\Dev\\Archon\\src\\Customer.Api\\Customer.Api.csproj"));
        }
    }
}
