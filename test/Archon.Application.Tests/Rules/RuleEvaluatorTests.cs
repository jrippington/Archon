using Archon.Application.Rules;
using Archon.Domain.Graph.ControlledValues;
using Xunit;

namespace Archon.Application.Tests.Rules
{
    /// <summary>
    /// Verifies the WP012 boolean rule evaluator against deterministic fixture graph facts.
    /// </summary>
    public sealed class RuleEvaluatorTests : IDisposable
    {
        /// <summary>
        /// Stores temporary rule folders created by tests so filesystem-backed catalog fixtures are removed after each scenario.
        /// </summary>
        private readonly List<string> _temporaryDirectories = [];

        /// <summary>
        /// Deletes temporary rules folders after each test scenario so copied-output style fixtures do not leak between tests.
        /// </summary>
        public void Dispose()
        {
            // Evaluator tests load rule JSON through the same disk loader as production, so temporary folders are cleaned deterministically.
            foreach (string temporaryDirectory in _temporaryDirectories)
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies enabled rules are selected, disabled rules are skipped, project candidates are filtered by node kind, and matched evidence is returned.
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenRulesAreLoadedFromCopiedOutput_ShouldEvaluateEnabledRulesAndSkipDisabledRules()
        {
            // The rule catalog is loaded from a runtime rules folder to prove evaluator tests exercise the same data-only path as Work Item 1.
            string rulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(rulesDirectory, "enabled.json", CreateRuleJson("ARCHON-EVAL-ENABLED", "Enabled target framework", "Project", "all", """
                { "kind": "target-framework-membership", "operator": "In", "values": ["net48", "net472"] }
                """));
            await WriteRuleAsync(rulesDirectory, "disabled.json", CreateRuleJson("ARCHON-EVAL-DISABLED", "Disabled target framework", "Project", "all", """
                { "kind": "target-framework-membership", "operator": "In", "values": ["net48"] }
                """, enabled: false));
            RuleCatalogLoadResult catalog = await new RuleCatalogLoader(new RuleCatalogOptions(rulesDirectory)).LoadAsync(CancellationToken.None);
            RuleEvaluationGraph graph = new(
                [
                    CreateNode("node://method/legacy", NodeKind.Method, "LegacyMethod"),
                    CreateNode("node://project/modern", NodeKind.Project, "ModernProject", targetFrameworks: ["net10.0"]),
                    CreateNode("node://project/legacy", NodeKind.Project, "LegacyProject", targetFrameworks: ["net48"], evidence: ["evidence://project/legacy"])
                ]);
            RuleEvaluator evaluator = new();

            RuleEvaluationResult result = await evaluator.EvaluateAsync(catalog.Rules, graph, CancellationToken.None);

            RuleEvaluationMatch match = Assert.Single(result.Matches);
            Assert.Equal("ARCHON-EVAL-ENABLED", match.RuleCode);
            Assert.Equal("node://project/legacy", Assert.Single(match.AffectedNodeStableKeys));
            Assert.Equal("evidence://project/legacy", Assert.Single(match.EvidenceStableKeys));
            Assert.Empty(result.Warnings);
            Assert.Empty(result.UnknownStates);
        }

        /// <summary>
        /// Verifies all, any, none, combined direct conditions and nested groups, and recursive nested group evaluation semantics.
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenBooleanGroupsAreNested_ShouldApplyAllAnyAndNoneSemanticsRecursively()
        {
            // This fixture requires a project to target net48, have either a legacy namespace or legacy package, and have no forbidden symbol.
            IReadOnlyList<RuleCatalogEntry> rules = await LoadRulesAsync(CreateRuleJson("ARCHON-EVAL-BOOLEAN", "Nested boolean rule", "Project", "all", """
                { "kind": "target-framework-membership", "operator": "Equal", "value": "net48" }
                """, groupsJson: """
                [
                  {
                    "match": "any",
                    "conditions": [
                      { "kind": "namespace", "operator": "StartsWith", "value": "System.Web" },
                      { "kind": "package", "operator": "Equal", "value": "Microsoft.AspNet.Mvc" }
                    ]
                  },
                  {
                    "match": "none",
                    "conditions": [
                      { "kind": "symbol", "operator": "Equal", "value": "AllowedOnlyMarker" }
                    ]
                  }
                ]
                """));
            RuleEvaluationGraph graph = new(
                [
                    CreateNode("node://project/match", NodeKind.Project, "MatchingProject", targetFrameworks: ["net48"], namespaces: ["System.Web.Mvc"], packages: ["Newtonsoft.Json"]),
                    CreateNode("node://project/blocked", NodeKind.Project, "BlockedProject", targetFrameworks: ["net48"], packages: ["Microsoft.AspNet.Mvc"], symbols: ["AllowedOnlyMarker"]),
                    CreateNode("node://project/miss", NodeKind.Project, "MissingProject", targetFrameworks: ["net48"], packages: ["Other.Package"])
                ]);
            RuleEvaluator evaluator = new();

            RuleEvaluationResult result = await evaluator.EvaluateAsync(rules, graph, CancellationToken.None);

            RuleEvaluationMatch match = Assert.Single(result.Matches);
            Assert.Equal("node://project/match", Assert.Single(match.AffectedNodeStableKeys));
            Assert.Contains(match.MatchedEvidenceReferences, evidence => evidence.Reference == "target-framework-membership:net48");
            Assert.Contains(match.MatchedEvidenceReferences, evidence => evidence.Reference == "namespace:System.Web.Mvc");
        }

        /// <summary>
        /// Verifies every supported condition kind can match deterministic graph-fact fixture fields.
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenAllConditionKindsArePresent_ShouldMatchSupportedFixtureFacts()
        {
            // The rule uses every required condition kind so one result proves the evaluator maps each DSL kind to the fixture read model.
            IReadOnlyList<RuleCatalogEntry> rules = await LoadRulesAsync(CreateRuleJson("ARCHON-EVAL-CONDITIONS", "Condition coverage", "Project", "all", """
                { "kind": "target-framework-membership", "operator": "Equal", "value": "net48" },
                { "kind": "namespace", "operator": "Contains", "value": "Legacy" },
                { "kind": "symbol", "operator": "EndsWith", "value": "Controller" },
                { "kind": "package", "operator": "In", "values": ["Microsoft.AspNet.Mvc", "System.Web.Mvc"] },
                { "kind": "file-pattern", "operator": "MatchesPattern", "value": "*.csproj" },
                { "kind": "method-call", "operator": "Equal", "value": "System.Web.HttpContext.Current" },
                { "kind": "attribute", "operator": "Equal", "value": "AuthorizeAttribute" },
                { "kind": "metric-threshold", "metric": "fanOut", "operator": "GreaterThanOrEqual", "value": 12 }
                """));
            RuleEvaluationGraph graph = new(
                [
                    CreateNode(
                        "node://project/legacy",
                        NodeKind.Project,
                        "LegacyProject",
                        targetFrameworks: ["net48"],
                        namespaces: ["Contoso.Legacy.Web"],
                        symbols: ["HomeController"],
                        packages: ["Microsoft.AspNet.Mvc"],
                        filePaths: ["src/Legacy/Legacy.csproj"],
                        methodCalls: ["System.Web.HttpContext.Current"],
                        attributes: ["AuthorizeAttribute"],
                        metrics: new Dictionary<string, decimal>(StringComparer.Ordinal) { ["fanOut"] = 12 })
                ]);
            RuleEvaluator evaluator = new();

            RuleEvaluationResult result = await evaluator.EvaluateAsync(rules, graph, CancellationToken.None);

            RuleEvaluationMatch match = Assert.Single(result.Matches);
            Assert.Equal(8, match.MatchedEvidenceReferences.Count);
            Assert.Equal(1.0m, match.ConfidenceInputs.RuleConfidence);
            Assert.Equal(1.0m, match.ConfidenceInputs.FactConfidence);
            Assert.Equal(0, match.ConfidenceInputs.UnknownCount);
        }

        /// <summary>
        /// Verifies each supported operator has deterministic ordinal or numeric behavior.
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenOperatorsAreUsed_ShouldApplyDocumentedComparisonBehavior()
        {
            // The fixture deliberately keeps casing exact because string operators use ordinal comparison rather than culture-sensitive matching.
            IReadOnlyList<RuleCatalogEntry> rules = await LoadRulesAsync(CreateRuleJson("ARCHON-EVAL-OPERATORS", "Operator coverage", "Project", "all", """
                { "kind": "namespace", "operator": "Equal", "value": "Contoso.Legacy" },
                { "kind": "namespace", "operator": "NotEqual", "value": "contoso.legacy" },
                { "kind": "package", "operator": "In", "values": ["Microsoft.AspNet.Mvc"] },
                { "kind": "package", "operator": "NotIn", "values": ["Modern.Package"] },
                { "kind": "symbol", "operator": "Contains", "value": "Legacy" },
                { "kind": "symbol", "operator": "StartsWith", "value": "Legacy" },
                { "kind": "symbol", "operator": "EndsWith", "value": "Controller" },
                { "kind": "file-pattern", "operator": "MatchesPattern", "value": "src/*/Legacy*.cs" },
                { "kind": "metric-threshold", "metric": "fanIn", "operator": "GreaterThan", "value": 9 },
                { "kind": "metric-threshold", "metric": "fanOut", "operator": "GreaterThanOrEqual", "value": 12 },
                { "kind": "metric-threshold", "metric": "instability", "operator": "LessThan", "value": 1 },
                { "kind": "metric-threshold", "metric": "abstractness", "operator": "LessThanOrEqual", "value": 0 }
                """));
            RuleEvaluationGraph graph = new(
                [
                    CreateNode(
                        "node://project/operator",
                        NodeKind.Project,
                        "OperatorProject",
                        namespaces: ["Contoso.Legacy"],
                        symbols: ["LegacyController"],
                        packages: ["Microsoft.AspNet.Mvc"],
                        filePaths: ["src/Web/LegacyController.cs"],
                        metrics: new Dictionary<string, decimal>(StringComparer.Ordinal)
                        {
                            ["fanIn"] = 10,
                            ["fanOut"] = 12,
                            ["instability"] = 0.5m,
                            ["abstractness"] = 0
                        })
                ]);
            RuleEvaluator evaluator = new();

            RuleEvaluationResult result = await evaluator.EvaluateAsync(rules, graph, CancellationToken.None);

            Assert.Single(result.Matches);
            Assert.Empty(result.Warnings);
        }

        /// <summary>
        /// Verifies deterministic result ordering, warning output, and explicit unknown-state preservation for partial evaluation.
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenFactsAreUnknownOrIncomplete_ShouldReturnDeterministicWarningsAndUnknownContext()
        {
            // Missing metric data creates a warning while unknown project data preserves explicit unknown context without inventing a match.
            IReadOnlyList<RuleCatalogEntry> rules = await LoadRulesAsync(CreateRuleJson("ARCHON-EVAL-UNKNOWN", "Unknown context", "Project", "any", """
                { "kind": "metric-threshold", "metric": "fanOut", "operator": "GreaterThan", "value": 10 },
                { "kind": "target-framework-membership", "operator": "Equal", "value": "net48" }
                """));
            RuleEvaluationGraph graph = new(
                [
                    CreateNode("node://project/b", NodeKind.Project, "ProjectB", targetFrameworks: ["net48"], unknownReasons: ["Target framework graph facts were partially degraded."]),
                    CreateNode("node://project/a", NodeKind.Project, "ProjectA", targetFrameworks: ["net48"])
                ]);
            RuleEvaluator evaluator = new();

            RuleEvaluationResult result = await evaluator.EvaluateAsync(rules, graph, CancellationToken.None);

            Assert.Equal(["node://project/a", "node://project/b"], result.Matches.Select(match => match.PrimaryNodeStableKey).ToArray());
            Assert.Contains(result.Warnings, warning => warning.Code == RuleEvaluationWarningCodes.ConditionFactsUnavailable && warning.NodeStableKey == "node://project/a");
            Assert.Contains(result.Warnings, warning => warning.Code == RuleEvaluationWarningCodes.ConditionFactsUnavailable && warning.NodeStableKey == "node://project/b");
            RuleEvaluationUnknownState unknownState = Assert.Single(result.UnknownStates);
            Assert.Equal("node://project/b", unknownState.NodeStableKey);
            Assert.Contains("partially degraded", unknownState.Reason, StringComparison.Ordinal);
            Assert.Equal(1, result.Matches.Single(match => match.PrimaryNodeStableKey == "node://project/b").ConfidenceInputs.UnknownCount);
        }

        /// <summary>
        /// Verifies data-only security boundaries by proving executable-looking rule values are treated only as literal comparison data.
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenRuleContainsExecutableLookingValues_ShouldTreatThemAsDataOnly()
        {
            // The evaluator must never interpret condition values as shell commands, SQL, Cypher, network calls, filesystem mutations, or application code.
            string executableLookingValue = "powershell -NoProfile -Command Remove-Item ./important; MATCH (n) RETURN n; https://example.invalid";
            IReadOnlyList<RuleCatalogEntry> rules = await LoadRulesAsync(CreateRuleJson("ARCHON-EVAL-DATA-ONLY", "Data only", "Project", "all", $$"""
                { "kind": "symbol", "operator": "Equal", "value": "{{executableLookingValue}}" }
                """));
            RuleEvaluationGraph graph = new(
                [
                    CreateNode("node://project/data-only", NodeKind.Project, "DataOnlyProject", symbols: [executableLookingValue], evidence: ["evidence://data-only"])
                ]);
            RuleEvaluator evaluator = new();

            RuleEvaluationResult result = await evaluator.EvaluateAsync(rules, graph, CancellationToken.None);

            RuleEvaluationMatch match = Assert.Single(result.Matches);
            Assert.Equal("evidence://data-only", Assert.Single(match.EvidenceStableKeys));
            Assert.DoesNotContain(RuleEvaluationWarningCodes.RuleEvaluationFailed, result.Warnings.Select(static warning => warning.Code));
        }

        /// <summary>
        /// Loads one authored JSON rule through the disk catalog loader for evaluator scenarios.
        /// </summary>
        /// <param name="ruleJson">The complete JSON rule document to load.</param>
        /// <returns>The loaded catalog entries.</returns>
        private async Task<IReadOnlyList<RuleCatalogEntry>> LoadRulesAsync(string ruleJson)
        {
            // Loading through RuleCatalogLoader keeps evaluator tests aligned with validated Work Item 1 rule contracts.
            string rulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(rulesDirectory, "rule.json", ruleJson);
            RuleCatalogLoadResult result = await new RuleCatalogLoader(new RuleCatalogOptions(rulesDirectory)).LoadAsync(CancellationToken.None);
            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
            return result.Rules;
        }

        /// <summary>
        /// Creates an isolated runtime rules directory for a filesystem-backed evaluator scenario.
        /// </summary>
        /// <returns>The absolute path of the created rules directory.</returns>
        private string CreateRulesDirectory()
        {
            // Each scenario receives a fresh rules folder so file ordering and duplicate behavior stay deterministic.
            string rulesDirectory = Path.Combine(Path.GetTempPath(), "archon-rule-evaluator-" + Guid.NewGuid().ToString("N"), "rules");
            Directory.CreateDirectory(rulesDirectory);
            _temporaryDirectories.Add(Path.GetDirectoryName(rulesDirectory)!);
            return rulesDirectory;
        }

        /// <summary>
        /// Writes a rule JSON fixture into a runtime rules directory.
        /// </summary>
        /// <param name="rulesDirectory">The directory that receives the rule file.</param>
        /// <param name="fileName">The deterministic fixture file name.</param>
        /// <param name="json">The JSON rule content to write.</param>
        /// <returns>A task that completes when the fixture is written.</returns>
        private static Task WriteRuleAsync(string rulesDirectory, string fileName, string json)
        {
            // Async writing mirrors the production loader's async file-read path.
            return File.WriteAllTextAsync(Path.Combine(rulesDirectory, fileName), json);
        }

        /// <summary>
        /// Creates a fixture graph node with condition-specific fact collections.
        /// </summary>
        /// <param name="stableKey">The deterministic node stable key.</param>
        /// <param name="nodeKind">The controlled node kind.</param>
        /// <param name="displayName">The node display name used for diagnostics.</param>
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
            // The fixture node captures just the WP012 condition read model and avoids depending on Neo4j or extractor implementation details.
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
        /// Creates a complete rule JSON document with a configurable detection group.
        /// </summary>
        /// <param name="ruleCode">The stable rule code to write.</param>
        /// <param name="name">The human-readable rule name to write.</param>
        /// <param name="nodeKind">The root candidate node kind.</param>
        /// <param name="match">The root boolean match mode.</param>
        /// <param name="conditionsJson">The JSON entries for the root conditions array.</param>
        /// <param name="groupsJson">The optional JSON array for nested groups.</param>
        /// <param name="enabled">Indicates whether the rule is available for evaluation.</param>
        /// <returns>A complete JSON rule document.</returns>
        private static string CreateRuleJson(string ruleCode, string name, string nodeKind, string match, string conditionsJson, string? groupsJson = null, bool enabled = true)
        {
            // The fixture follows the authored rule contract while letting each test vary only the predicate shape it needs.
            string groupsProperty = groupsJson is null ? string.Empty : $",\n      \"groups\": {groupsJson}";
            return $$"""
                {
                  "ruleCode": "{{ruleCode}}",
                  "name": "{{name}}",
                  "category": "Lifecycle",
                  "severity": "High",
                  "defaultStatus": "OutOfSupport",
                  "enabled": {{enabled.ToString().ToLowerInvariant()}},
                  "version": "1.0.0",
                  "description": "Evaluator test rule.",
                  "builtIn": true,
                  "impact": ["Evaluator test impact."],
                  "evidenceRequirements": ["Fixture graph facts must be available."],
                  "detection": {
                    "nodeKinds": ["{{nodeKind}}"],
                    "match": "{{match}}",
                    "conditions": [
                      {{conditionsJson}}
                    ]{{groupsProperty}}
                  }
                }
                """;
        }
    }
}
