using Archon.Application.Rules;
using Archon.Domain.Graph.ControlledValues;
using Xunit;

namespace Archon.Application.Tests.Rules
{
    /// <summary>
    /// Verifies the WP012 first-cut built-in rule catalog validates and produces representative evaluator matches.
    /// </summary>
    public sealed class BuiltInRuleCatalogTests
    {
        /// <summary>
        /// Defines secret-like text that must never appear in built-in rule content or safe evidence fixtures.
        /// </summary>
        private const string SecretLikeValue = "Server=prod;Password=DoNotStore123!;User Id=admin";

        /// <summary>
        /// Verifies every copied-output built-in rule file validates through the shared runtime loader.
        /// </summary>
        [Fact]
        public async Task BuiltInRules_WhenLoadedFromCopiedOutput_ShouldValidateEveryRule()
        {
            // The default options read AppContext.BaseDirectory/rules, which is populated by the test project's copy-to-output item.
            RuleCatalogLoadResult result = await LoadBuiltInRulesAsync();

            Assert.True(result.IsValid, FormatDiagnostics(result.Diagnostics));
            Assert.Empty(result.Diagnostics);
            Assert.Contains(result.Rules, rule => rule.RuleCode == "ARCHON-LIFECYCLE-NETFRAMEWORK-UNSUPPORTED");
            Assert.True(result.Rules.Count >= BuiltInRuleInventory.RequiredRuleCodes.Count);
            Assert.All(result.Rules, rule => Assert.True(rule.IsBuiltIn, $"Rule {rule.RuleCode} must identify itself as built-in."));
            Assert.All(result.Rules, rule => Assert.True(rule.Enabled, $"Rule {rule.RuleCode} must declare its enabled state as true for the first-cut catalog."));
            Assert.All(result.Rules, rule => Assert.NotEmpty(rule.Impact));
            Assert.All(result.Rules, rule => Assert.NotEmpty(rule.EvidenceRequirements));
            Assert.All(result.Rules, rule => Assert.NotEmpty(rule.RecommendedActions));
            Assert.All(result.Rules, rule => Assert.NotEmpty(rule.Tags));
        }

        /// <summary>
        /// Verifies the copied-output catalog includes concise traceability for every required first-cut built-in rule family.
        /// </summary>
        [Fact]
        public async Task BuiltInRules_WhenComparedWithInventory_ShouldCoverRequiredFamiliesAndScenarios()
        {
            // Inventory assertions keep traceability in executable tests rather than a standalone implementation-notes artifact.
            RuleCatalogLoadResult result = await LoadBuiltInRulesAsync();
            IReadOnlyDictionary<string, RuleCatalogEntry> rulesByCode = result.Rules.ToDictionary(static rule => rule.RuleCode, StringComparer.Ordinal);

            foreach (BuiltInRuleInventoryEntry inventoryEntry in BuiltInRuleInventory.RequiredRuleCodes)
            {
                RuleCatalogEntry rule = Assert.Contains(inventoryEntry.RuleCode, rulesByCode);
                Assert.Equal(inventoryEntry.Category, rule.Category.Value);
                Assert.Equal(inventoryEntry.Severity, rule.Severity.Value);
                Assert.Equal(inventoryEntry.Status, rule.DefaultStatus.Value);
                Assert.NotEmpty(inventoryEntry.RepresentativeFixture);
                Assert.Contains(rule.Tags, tag => StringComparer.Ordinal.Equals(tag, inventoryEntry.RuleFamily));
                foreach (string expectedCoverage in inventoryEntry.ExpectedCoverage)
                {
                    Assert.Contains(expectedCoverage, rule.DefinitionJson, StringComparison.Ordinal);
                }
            }
        }

        /// <summary>
        /// Verifies representative graph facts match every required built-in rule family through the shared evaluator.
        /// </summary>
        [Fact]
        public async Task BuiltInRules_WhenEvaluatedAgainstRepresentativeFixtures_ShouldMatchRequiredRuleFamilies()
        {
            // The graph combines one deterministic fixture node per family so the test proves authored JSON works with the current evaluator read model.
            RuleCatalogLoadResult catalog = await LoadBuiltInRulesAsync();
            RuleEvaluationGraph graph = new(CreateRepresentativeNodes());
            RuleEvaluator evaluator = new();

            RuleEvaluationResult result = await evaluator.EvaluateAsync(catalog.Rules, graph, CancellationToken.None);

            foreach (BuiltInRuleInventoryEntry inventoryEntry in BuiltInRuleInventory.RequiredRuleCodes)
            {
                Assert.Contains(result.Matches, match => match.RuleCode == inventoryEntry.RuleCode && match.PrimaryNodeStableKey == inventoryEntry.RepresentativeFixture);
            }

            Assert.DoesNotContain(result.Matches.SelectMany(static match => match.MatchedEvidenceReferences), evidence => evidence.Reference.Contains(SecretLikeValue, StringComparison.Ordinal));
            Assert.DoesNotContain(result.Matches.SelectMany(static match => match.EvidenceStableKeys), evidenceStableKey => evidenceStableKey.Contains(SecretLikeValue, StringComparison.Ordinal));
        }

        /// <summary>
        /// Verifies security-sensitive rules describe secret existence and location without storing a secret value in rule content or match evidence.
        /// </summary>
        [Fact]
        public async Task SecuritySensitiveRules_WhenSecretLocationsAreEvaluated_ShouldAvoidPersistingSecretValues()
        {
            // The security fixture exposes a location-like symbol and evidence key; the raw secret is deliberately kept out of graph facts and rule JSON.
            RuleCatalogLoadResult catalog = await LoadBuiltInRulesAsync();
            RuleCatalogEntry securityRule = Assert.Single(catalog.Rules, rule => rule.RuleCode == "ARCHON-SECURITY-SENSITIVE-LEGACY");
            RuleEvaluationGraph graph = new(
                [
                    CreateNode(
                        "node://configuration/security-location",
                        NodeKind.ConfigurationKey,
                        "Connection string location",
                        symbols: ["ConnectionStringLocation"],
                        filePaths: ["app.config"],
                        evidence: ["evidence://configuration/app-config/connection-string-location"])
                ]);
            RuleEvaluator evaluator = new();

            RuleEvaluationResult result = await evaluator.EvaluateAsync([securityRule], graph, CancellationToken.None);

            RuleEvaluationMatch match = Assert.Single(result.Matches);
            Assert.Equal("ARCHON-SECURITY-SENSITIVE-LEGACY", match.RuleCode);
            Assert.DoesNotContain(SecretLikeValue, securityRule.DefinitionJson, StringComparison.Ordinal);
            Assert.DoesNotContain(match.MatchedEvidenceReferences, evidence => evidence.Reference.Contains(SecretLikeValue, StringComparison.Ordinal));
            Assert.DoesNotContain(match.EvidenceStableKeys, evidenceStableKey => evidenceStableKey.Contains(SecretLikeValue, StringComparison.Ordinal));
            Assert.Contains("storesSecretValues", securityRule.DefinitionJson, StringComparison.Ordinal);
            Assert.Contains("false", securityRule.DefinitionJson, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Verifies metric-backed architecture rules surface partial-evaluation warnings when upstream metrics are unavailable.
        /// </summary>
        [Fact]
        public async Task ArchitectureRules_WhenMetricsAreMissing_ShouldReportPartialEvaluationWarnings()
        {
            // This scenario proves architecture-smell rules do not invent metrics: missing metric facts produce warnings while a symbol fact can still match.
            RuleCatalogLoadResult catalog = await LoadBuiltInRulesAsync();
            RuleCatalogEntry architectureRule = Assert.Single(catalog.Rules, rule => rule.RuleCode == "ARCHON-ARCHITECTURE-SMELL-FIRST-CUT");
            RuleEvaluationGraph graph = new(
                [
                    CreateNode(
                        "node://type/service-locator",
                        NodeKind.Type,
                        "ServiceLocator",
                        symbols: ["ServiceLocator.Current"],
                        evidence: ["evidence://type/service-locator"],
                        unknownReasons: ["Architecture metrics were unavailable for this snapshot."])
                ]);
            RuleEvaluator evaluator = new();

            RuleEvaluationResult result = await evaluator.EvaluateAsync([architectureRule], graph, CancellationToken.None);

            Assert.Single(result.Matches);
            Assert.Contains(result.Warnings, warning => warning.Code == RuleEvaluationWarningCodes.ConditionFactsUnavailable && warning.RuleCode == architectureRule.RuleCode);
            RuleEvaluationUnknownState unknownState = Assert.Single(result.UnknownStates);
            Assert.Equal("node://type/service-locator", unknownState.NodeStableKey);
        }

        /// <summary>
        /// Loads the copied-output built-in catalog and asserts the shared loader accepted it.
        /// </summary>
        /// <returns>The validated built-in catalog load result.</returns>
        private static async Task<RuleCatalogLoadResult> LoadBuiltInRulesAsync()
        {
            // Tests intentionally use the default runtime folder so they exercise the same copied-output path as application startup.
            RuleCatalogLoadResult result = await new RuleCatalogLoader(new RuleCatalogOptions()).LoadAsync(CancellationToken.None);
            Assert.True(result.IsValid, FormatDiagnostics(result.Diagnostics));
            return result;
        }

        /// <summary>
        /// Formats loader diagnostics for readable assertion failures.
        /// </summary>
        /// <param name="diagnostics">The diagnostics returned by the rule catalog loader.</param>
        /// <returns>A multi-line diagnostic string.</returns>
        private static string FormatDiagnostics(IEnumerable<RuleCatalogDiagnostic> diagnostics)
        {
            // Each line includes code, path, and message so JSON authoring failures point directly at the rule problem.
            return string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => $"{diagnostic.Code} {diagnostic.FilePath} {diagnostic.Path}: {diagnostic.Message}"));
        }

        /// <summary>
        /// Creates the representative graph-fact nodes used to prove the first-cut built-in catalog can match expected families.
        /// </summary>
        /// <returns>The deterministic graph nodes for evaluator input.</returns>
        private static IReadOnlyList<RuleEvaluationNode> CreateRepresentativeNodes()
        {
            // The fixture uses location-like evidence and graph facts only; it does not execute application code or include raw secret values.
            return
            [
                CreateNode("node://project/net-framework", NodeKind.Project, "FrameworkProject", targetFrameworks: ["net48"], evidence: ["evidence://project/net-framework"]),
                CreateNode("node://project/net-core", NodeKind.Project, "NetCoreProject", targetFrameworks: ["netcoreapp3.1"], evidence: ["evidence://project/net-core"]),
                CreateNode("node://project/net7", NodeKind.Project, "Net7Project", targetFrameworks: ["net7.0"], evidence: ["evidence://project/net7"]),
                CreateNode("node://project/netstandard", NodeKind.Project, "NetStandardProject", targetFrameworks: ["netstandard2.0"], evidence: ["evidence://project/netstandard"]),
                CreateNode("node://project/classic-aspnet", NodeKind.Project, "ClassicAspNetProject", namespaces: ["System.Web.Mvc"], packages: ["Microsoft.AspNet.Mvc"], filePaths: ["Global.asax"], symbols: ["System.Web.Mvc.Controller"], evidence: ["evidence://project/classic-aspnet"]),
                CreateNode("node://project/framework-runtime", NodeKind.Project, "FrameworkRuntimeProject", namespaces: ["System.ServiceModel"], packages: ["Topshelf"], symbols: ["ServiceContractAttribute"], attributes: ["ServiceContractAttribute"], evidence: ["evidence://project/framework-runtime"]),
                CreateNode("node://project/data-access", NodeKind.Project, "DataAccessProject", packages: ["EntityFramework"], namespaces: ["System.Data.SqlClient"], symbols: ["SqlCommand"], methodCalls: ["ExecuteReader"], filePaths: ["Northwind.dbml"], evidence: ["evidence://project/data-access"]),
                CreateNode("node://method/obsolete", NodeKind.Method, "ObsoleteCall", symbols: ["SYSLIB0011"], attributes: ["ObsoleteAttribute"], evidence: ["evidence://method/obsolete"]),
                CreateNode("node://configuration/security-location", NodeKind.ConfigurationKey, "SecurityLocation", namespaces: ["System.Security.Cryptography"], symbols: ["ConnectionStringLocation"], filePaths: ["app.config"], evidence: ["evidence://configuration/security-location"]),
                CreateNode("node://project/configuration", NodeKind.Project, "ConfigurationProject", namespaces: ["System.Configuration"], symbols: ["ConfigurationManager"], filePaths: ["web.config"], evidence: ["evidence://project/configuration"]),
                CreateNode("node://package/dependency", NodeKind.Package, "LegacyDependency", packages: ["Castle.Windsor"], evidence: ["evidence://package/dependency"]),
                CreateNode("node://type/architecture", NodeKind.Type, "GodService", symbols: ["ServiceLocator.Current"], metrics: new Dictionary<string, decimal>(StringComparer.Ordinal) { ["fanIn"] = 30 }, evidence: ["evidence://type/architecture"])
            ];
        }

        /// <summary>
        /// Creates a graph-fact node for built-in rule evaluator fixtures.
        /// </summary>
        /// <param name="stableKey">The deterministic node stable key.</param>
        /// <param name="nodeKind">The controlled node kind used for candidate filtering.</param>
        /// <param name="displayName">The developer-facing node display name.</param>
        /// <param name="targetFrameworks">The target framework facts associated with the node.</param>
        /// <param name="namespaces">The namespace facts associated with the node.</param>
        /// <param name="symbols">The symbol facts associated with the node.</param>
        /// <param name="packages">The package facts associated with the node.</param>
        /// <param name="filePaths">The file path facts associated with the node.</param>
        /// <param name="methodCalls">The method-call facts associated with the node.</param>
        /// <param name="attributes">The attribute facts associated with the node.</param>
        /// <param name="metrics">The numeric metric facts associated with the node.</param>
        /// <param name="evidence">The evidence stable keys associated with the node.</param>
        /// <param name="unknownReasons">The explicit unknown-state reasons associated with the node.</param>
        /// <returns>A graph-fact node for evaluator input.</returns>
        private static RuleEvaluationNode CreateNode(
            string stableKey,
            NodeKind nodeKind,
            string displayName,
            IEnumerable<string>? targetFrameworks = null,
            IEnumerable<string>? namespaces = null,
            IEnumerable<string>? symbols = null,
            IEnumerable<string>? packages = null,
            IEnumerable<string>? filePaths = null,
            IEnumerable<string>? methodCalls = null,
            IEnumerable<string>? attributes = null,
            IReadOnlyDictionary<string, decimal>? metrics = null,
            IEnumerable<string>? evidence = null,
            IEnumerable<string>? unknownReasons = null)
        {
            // The fixture node mirrors the evaluator's condition collections and keeps test setup independent of persistence or extractor projects.
            return new RuleEvaluationNode(
                stableKey,
                nodeKind,
                displayName,
                targetFrameworks ?? [],
                namespaces ?? [],
                symbols ?? [],
                packages ?? [],
                filePaths ?? [],
                methodCalls ?? [],
                attributes ?? [],
                metrics ?? new Dictionary<string, decimal>(StringComparer.Ordinal),
                evidence ?? [],
                1.0m,
                unknownReasons ?? []);
        }

        /// <summary>
        /// Provides executable traceability between required WP012 built-in rule scenarios and authored rule codes.
        /// </summary>
        private static class BuiltInRuleInventory
        {
            /// <summary>
            /// Gets the required built-in rule code inventory with family, classification, and representative fixture mapping.
            /// </summary>
            public static IReadOnlyList<BuiltInRuleInventoryEntry> RequiredRuleCodes { get; } =
            [
                new("ARCHON-LIFECYCLE-NETFRAMEWORK-UNSUPPORTED", "lifecycle", "Lifecycle", "High", "OutOfSupport", "node://project/net-framework", ["net48", "net462"]),
                new("ARCHON-LIFECYCLE-NETCORE-RETIRED", "lifecycle", "Lifecycle", "High", "OutOfSupport", "node://project/net-core", ["dotnetCore31"]),
                new("ARCHON-LIFECYCLE-DOTNET-5-7-RETIRED", "lifecycle", "Lifecycle", "High", "OutOfSupport", "node://project/net7", ["dotnet7"]),
                new("ARCHON-MODERNIZATION-NETSTANDARD-ONLY", "modernization-blocker", "ModernizationBlocker", "Medium", "MigrationBlocker", "node://project/netstandard", ["netStandardOnly"]),
                new("ARCHON-LEGACY-ASP.NET-CLASSIC", "legacy-technology", "LegacyTechnology", "High", "Legacy", "node://project/classic-aspnet", ["aspNetWebForms", "webApi2", "systemWeb"]),
                new("ARCHON-LEGACY-FRAMEWORK-RUNTIME", "legacy-technology", "LegacyTechnology", "High", "FrameworkOnly", "node://project/framework-runtime", ["wcfServer", "asmx", "topshelf"]),
                new("ARCHON-DATAACCESS-LEGACY-RELATIONAL", "data-access", "DataAccess", "Medium", "Discouraged", "node://project/data-access", ["linqToSql", "adoNetSqlCommand", "ef6", "storedProcedureHeavyAccess"]),
                new("ARCHON-OBSOLETE-API-SYSLIB-EXTOBS", "obsolete-api", "ObsoleteApi", "Medium", "Obsolete", "node://method/obsolete", ["syslib", "extobs"]),
                new("ARCHON-SECURITY-SENSITIVE-LEGACY", "security", "SecuritySensitive", "Critical", "SecuritySensitive", "node://configuration/security-location", ["binaryFormatter", "connectionStringLocation", "storesSecretValues"]),
                new("ARCHON-CONFIGURATION-HOSTING-BLOCKERS", "configuration", "Configuration", "Medium", "MigrationBlocker", "node://project/configuration", ["webConfigHeavy", "configurationManager", "packagesConfig"]),
                new("ARCHON-DEPENDENCY-RISK-LEGACY-PACKAGES", "dependency-risk", "DependencyRisk", "Medium", "Discouraged", "node://package/dependency", ["castleWindsor", "oldNewtonsoftJson", "topshelf"]),
                new("ARCHON-ARCHITECTURE-SMELL-FIRST-CUT", "architecture", "ArchitectureLayering", "Medium", "Unknown", "node://type/architecture", ["highFanIn", "staticServiceLocator", "dynamicInvocation"])
            ];
        }

        /// <summary>
        /// Captures the concise expected classification and fixture mapping for one built-in rule.
        /// </summary>
        /// <param name="RuleCode">The stable built-in rule code.</param>
        /// <param name="RuleFamily">The expected authored tag for the rule family.</param>
        /// <param name="Category">The expected controlled category value.</param>
        /// <param name="Severity">The expected controlled severity value.</param>
        /// <param name="Status">The expected default finding status value.</param>
        /// <param name="RepresentativeFixture">The node stable key expected to match this rule in representative evaluation.</param>
        /// <param name="ExpectedCoverage">Coverage strings expected in the rule definition metadata.</param>
        private sealed record BuiltInRuleInventoryEntry(
            string RuleCode,
            string RuleFamily,
            string Category,
            string Severity,
            string Status,
            string RepresentativeFixture,
            IReadOnlyList<string> ExpectedCoverage);
    }
}
