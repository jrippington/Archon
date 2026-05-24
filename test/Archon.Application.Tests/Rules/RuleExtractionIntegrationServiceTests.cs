using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Rules;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Xunit;

namespace Archon.Application.Tests.Rules
{
    /// <summary>
    /// Verifies the WP012 extraction integration service loads, persists, contributes, and evaluates rule catalog entries.
    /// </summary>
    public sealed class RuleExtractionIntegrationServiceTests : IDisposable
    {
        /// <summary>
        /// Stores temporary rule folders created by tests so copied-output fixtures are deleted after each scenario.
        /// </summary>
        private readonly List<string> _temporaryDirectories = [];

        /// <summary>
        /// Deletes temporary rules folders created by test scenarios.
        /// </summary>
        public void Dispose()
        {
            // Integration tests load rules from disk to exercise the copied-output rule loader contract.
            foreach (string temporaryDirectory in _temporaryDirectories)
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies the integration service loads copied-output rules, upserts by code/version, contributes snapshot rule definitions, and evaluates enabled rules.
        /// </summary>
        /// <returns>A task that completes after the load-upsert-evaluate sequence has been asserted.</returns>
        [Fact]
        public async Task LoadPersistAndEvaluateAsync_WhenSnapshotContainsRequiredFacts_ShouldUpsertRulesAndEvaluateEnabledRules()
        {
            // The fixture mirrors extraction flow: project facts exist before WP012 runs, and the rule stage adds catalog records and evaluator diagnostics.
            string rulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(rulesDirectory, "enabled.json", CreateRuleJson("ARCHON-WP012-ENABLED", enabled: true, version: "1.0.0"));
            await WriteRuleAsync(rulesDirectory, "disabled.json", CreateRuleJson("ARCHON-WP012-DISABLED", enabled: false, version: "1.0.0"));
            InMemoryRuleCatalogStore store = new();
            RuleExtractionIntegrationService service = new(new RuleCatalogLoader(new RuleCatalogOptions(rulesDirectory)), store, new RuleEvaluator());
            ArchitectureSnapshotAccumulator accumulation = CreateAccumulationWithLegacyProjectFacts();

            RuleExtractionIntegrationResult result = await service.LoadPersistAndEvaluateAsync(accumulation, CancellationToken.None);

            Assert.Equal(2, result.LoadedRuleCount);
            Assert.Equal(2, result.UpsertedRuleCount);
            Assert.Equal(1, result.EvaluatedRuleCount);
            Assert.Equal(1, result.MatchCount);
            IReadOnlyList<RuleCatalogEntry> persistedRules = await store.GetRulesAsync(CancellationToken.None);
            Assert.Equal(["ARCHON-WP012-DISABLED", "ARCHON-WP012-ENABLED"], persistedRules.Select(static rule => rule.RuleCode).ToArray());
            ExtractedArchitectureSnapshot snapshot = accumulation.ToSnapshot();
            Assert.Equal(2, snapshot.Rules.Count);
            Assert.Contains(snapshot.Rules, rule => rule.RuleCode == "ARCHON-WP012-ENABLED" && rule.Version == "1.0.0");
            Assert.Empty(snapshot.Errors);
        }

        /// <summary>
        /// Verifies catalog upserts are idempotent for unchanged code/version entries while preserving new historical versions.
        /// </summary>
        /// <returns>A task that completes after persisted rule identity has been asserted.</returns>
        [Fact]
        public async Task LoadPersistAndEvaluateAsync_WhenRuleVersionChanges_ShouldPreserveHistoricalVersions()
        {
            // Rule code plus version is the catalog identity, so a new version must coexist with the earlier version after a later load.
            InMemoryRuleCatalogStore store = new();
            string firstRulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(firstRulesDirectory, "rule.json", CreateRuleJson("ARCHON-WP012-VERSIONED", enabled: true, version: "1.0.0"));
            RuleExtractionIntegrationService firstService = new(new RuleCatalogLoader(new RuleCatalogOptions(firstRulesDirectory)), store, new RuleEvaluator());
            await firstService.LoadPersistAndEvaluateAsync(CreateAccumulationWithLegacyProjectFacts(), CancellationToken.None);

            string secondRulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(secondRulesDirectory, "rule.json", CreateRuleJson("ARCHON-WP012-VERSIONED", enabled: false, version: "1.1.0"));
            RuleExtractionIntegrationService secondService = new(new RuleCatalogLoader(new RuleCatalogOptions(secondRulesDirectory)), store, new RuleEvaluator());
            await secondService.LoadPersistAndEvaluateAsync(CreateAccumulationWithLegacyProjectFacts(), CancellationToken.None);

            IReadOnlyList<RuleCatalogEntry> persistedRules = await store.GetRulesAsync(CancellationToken.None);
            Assert.Equal(["1.0.0", "1.1.0"], persistedRules.Select(static rule => rule.Version).ToArray());
            Assert.Contains(persistedRules, rule => rule.RuleCode == "ARCHON-WP012-VERSIONED" && rule.Version == "1.0.0" && rule.Enabled);
            Assert.Contains(persistedRules, rule => rule.RuleCode == "ARCHON-WP012-VERSIONED" && rule.Version == "1.1.0" && !rule.Enabled);
        }

        /// <summary>
        /// Verifies removed-on-disk rules are not destructively deleted from the persisted catalog during later loads.
        /// </summary>
        /// <returns>A task that completes after non-destructive persistence behavior has been asserted.</returns>
        [Fact]
        public async Task LoadPersistAndEvaluateAsync_WhenRuleIsRemovedFromDisk_ShouldNotDeletePersistedCatalogHistory()
        {
            // A later rule folder that omits a previously persisted rule must not delete historical catalog data or future finding references.
            InMemoryRuleCatalogStore store = new();
            string initialRulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(initialRulesDirectory, "first.json", CreateRuleJson("ARCHON-WP012-KEPT", enabled: true, version: "1.0.0"));
            await WriteRuleAsync(initialRulesDirectory, "removed.json", CreateRuleJson("ARCHON-WP012-REMOVED", enabled: true, version: "1.0.0"));
            await new RuleExtractionIntegrationService(new RuleCatalogLoader(new RuleCatalogOptions(initialRulesDirectory)), store, new RuleEvaluator())
                .LoadPersistAndEvaluateAsync(CreateAccumulationWithLegacyProjectFacts(), CancellationToken.None);

            string laterRulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(laterRulesDirectory, "first.json", CreateRuleJson("ARCHON-WP012-KEPT", enabled: true, version: "1.0.0"));
            await new RuleExtractionIntegrationService(new RuleCatalogLoader(new RuleCatalogOptions(laterRulesDirectory)), store, new RuleEvaluator())
                .LoadPersistAndEvaluateAsync(CreateAccumulationWithLegacyProjectFacts(), CancellationToken.None);

            IReadOnlyList<RuleCatalogEntry> persistedRules = await store.GetRulesAsync(CancellationToken.None);
            Assert.Contains(persistedRules, rule => rule.RuleCode == "ARCHON-WP012-REMOVED");
            Assert.Contains(persistedRules, rule => rule.RuleCode == "ARCHON-WP012-KEPT");
        }

        /// <summary>
        /// Verifies the integrated WP012 application path can load copied-output rules, evaluate extracted facts, persist findings, query hotlist output, retrieve history, apply suppression, and keep secret-like data out of public DTOs.
        /// </summary>
        /// <returns>A task that completes after the end-to-end application seams have been asserted.</returns>
        [Fact]
        public async Task WP012EndToEndPath_WhenRepresentativeFactsExist_ShouldPersistQueryAndSuppressFindingsSafely()
        {
            // This scenario deliberately composes application-layer seams instead of launching the Aspire AppHost, which keeps validation targeted and non-blocking.
            const string secretLikeValue = "Server=prod;Password=DoNotStore123!;User Id=admin";
            string rulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(rulesDirectory, "lifecycle.json", CreateRuleJson("ARCHON-WP012-E2E-LIFECYCLE", enabled: true, version: "1.0.0"));
            await WriteRuleAsync(rulesDirectory, "security.json", CreateSecurityLocationRuleJson());
            InMemoryRuleCatalogStore catalogStore = new();
            InMemoryFindingStore findingStore = new();
            RuleCatalogLoader loader = new(new RuleCatalogOptions(rulesDirectory));
            RuleEvaluator evaluator = new();
            RuleExtractionIntegrationService integrationService = new(loader, catalogStore, evaluator);
            ArchitectureSnapshotAccumulator accumulation = CreateAccumulationWithEndToEndFacts(secretLikeValue);

            RuleExtractionIntegrationResult integrationResult = await integrationService.LoadPersistAndEvaluateAsync(accumulation, CancellationToken.None);
            RuleCatalogLoadResult loadedCatalog = await loader.LoadAsync(CancellationToken.None);
            RuleEvaluationResult evaluation = await evaluator.EvaluateAsync(loadedCatalog.Rules, CreateEndToEndEvaluationGraph(), CancellationToken.None);
            FindingConstructionService findingConstructionService = new();
            FindingConstructionResult constructionResult = findingConstructionService.CreateFindings(new FindingConstructionRequest("snapshot://wp012/e2e", loadedCatalog.Rules, evaluation.Matches, evaluation.UnknownStates));
            await findingStore.UpsertFindingsAsync(constructionResult.Findings, CancellationToken.None);
            HotlistQueryService queryService = new(new InMemoryHotlistQueryStore(catalogStore, findingStore), findingStore);

            PagedQueryResult<RuleCatalogItemDto> catalogPage = await queryService.ListRulesAsync(new RuleCatalogQuery(null, null, null, null, enabled: true, builtIn: true, null, 0, 10), CancellationToken.None);
            PagedQueryResult<HotlistItemDto> hotlistPage = await queryService.ListHotlistAsync(new HotlistQuery("snapshot://wp012/e2e", null, null, null, null, null, 0, 10), CancellationToken.None);
            HotlistItemDto securityHotlistItem = Assert.Single(hotlistPage.Items, static item => item.RuleCode == "ARCHON-WP012-E2E-SECURITY");
            FindingDetailDto? detail = await queryService.GetFindingAsync(securityHotlistItem.SnapshotStableKey, securityHotlistItem.StableKey, CancellationToken.None);
            FindingHistoryDto? historyBeforeSuppression = await queryService.GetFindingHistoryAsync(securityHotlistItem.HistoryKey, CancellationToken.None);
            SuppressionCommandResult suppressionResult = await queryService.SuppressFindingAsync(new SuppressFindingCommand(securityHotlistItem.HistoryKey, securityHotlistItem.RuleCode, securityHotlistItem.RuleVersion, Assert.Single(securityHotlistItem.AffectedNodes).StableKey, "Accepted risk until the migration window closes.", "architect@example.invalid", GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["ticket"] = "ARCH-012"
            })), CancellationToken.None);
            FindingDetailDto? suppressedDetail = await queryService.GetFindingAsync(securityHotlistItem.SnapshotStableKey, securityHotlistItem.StableKey, CancellationToken.None);

            Assert.Equal(2, integrationResult.LoadedRuleCount);
            Assert.Equal(2, integrationResult.UpsertedRuleCount);
            Assert.Equal(2, integrationResult.EvaluatedRuleCount);
            Assert.Equal(2, integrationResult.MatchCount);
            Assert.Equal(["ARCHON-WP012-E2E-LIFECYCLE", "ARCHON-WP012-E2E-SECURITY"], catalogPage.Items.Select(static item => item.RuleCode).ToArray());
            Assert.Equal(["ARCHON-WP012-E2E-LIFECYCLE", "ARCHON-WP012-E2E-SECURITY"], hotlistPage.Items.Select(static item => item.RuleCode).ToArray());
            Assert.Equal("Critical", securityHotlistItem.Severity);
            Assert.Equal("Open", securityHotlistItem.Status);
            Assert.True(securityHotlistItem.HasUnknownData);
            Assert.Contains("Secret-like value was redacted", securityHotlistItem.UnknownReason, StringComparison.Ordinal);
            Assert.InRange(securityHotlistItem.Confidence, 0.1m, 1.0m);
            Assert.NotNull(detail);
            Assert.Equal("snapshot://wp012/e2e", detail.FirstSeenSnapshotStableKey);
            Assert.Equal("snapshot://wp012/e2e", detail.LatestSeenSnapshotStableKey);
            Assert.Equal("evidence://configuration/app-config/connection-string-location", detail.PrimaryEvidenceStableKey);
            Assert.NotNull(historyBeforeSuppression);
            Assert.Single(historyBeforeSuppression.Records);
            Assert.True(suppressionResult.Succeeded);
            Assert.Equal("Suppressed", suppressedDetail!.Item.Status);
            Assert.Equal("Accepted risk until the migration window closes.", suppressedDetail.SuppressionReason);
            Assert.DoesNotContain(secretLikeValue, detail.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.DoesNotContain(secretLikeValue, suppressedDetail.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.DoesNotContain(secretLikeValue, string.Join("|", accumulation.ToSnapshot().Warnings), StringComparison.Ordinal);
            Assert.DoesNotContain(secretLikeValue, string.Join("|", hotlistPage.Items.Select(static item => item.Summary)), StringComparison.Ordinal);
        }

        /// <summary>
        /// Creates an accumulator containing project and package facts that satisfy the test lifecycle rule.
        /// </summary>
        /// <returns>An accumulator populated as if earlier extraction stages had run.</returns>
        private static ArchitectureSnapshotAccumulator CreateAccumulationWithLegacyProjectFacts()
        {
            // The project node metadata uses the existing project extractor keys so projection validates real extraction fact shapes.
            ArchitectureSnapshotAccumulator accumulation = new();
            StableKey repositoryStableKey = new("repository://wp012");
            StableKey solutionStableKey = new("solution://wp012");
            StableKey snapshotStableKey = new("snapshot://wp012");
            StableKey projectStableKey = new("project://Legacy.csproj");
            StableKey packageStableKey = new("package://Microsoft.AspNet.Mvc");
            StableKey evidenceStableKey = new("evidence://wp012/project");
            accumulation.SetSnapshotHeader(new SnapshotHeader(
                snapshotStableKey,
                repositoryStableKey,
                "main",
                "abcdef",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "test",
                "Completed",
                [],
                [],
                GraphMetadata.Empty));
            accumulation.AddRepository(new RepositoryModel(repositoryStableKey, "WP012", "D:/Dev/WP012", null, "main", GraphMetadata.Empty));
            accumulation.AddSolution(new SolutionModel(repositoryStableKey, solutionStableKey, "WP012", RepositoryRelativePath.Parse("WP012.sln"), GraphMetadata.Empty));
            accumulation.AddEvidence(new EvidenceRecord(
                snapshotStableKey,
                evidenceStableKey,
                EvidenceKind.ProjectFile,
                RepositoryRelativePath.Parse("Legacy.csproj"),
                1,
                10,
                null,
                null,
                "hash",
                "<TargetFramework>net48</TargetFramework>",
                KnowledgeKind.Fact,
                Confidence.High,
                UnknownState.Known,
                GraphMetadata.Empty,
                FingerprintGenerator.ForEvidence(EvidenceKind.ProjectFile, "Legacy.csproj", 1, 10, null, KnowledgeKind.Fact, GraphMetadata.Empty)));
            accumulation.AddNode(new ArchitectureNode(
                snapshotStableKey,
                projectStableKey,
                NodeKind.Project,
                "Legacy",
                "Legacy",
                "legacy",
                "C#",
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.High,
                UnknownState.Known,
                evidenceStableKey,
                GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["project.relativePath"] = "Legacy.csproj",
                    ["project.rootNamespace"] = "Legacy.Web",
                    ["project.targetFramework"] = "net48"
                }),
                FingerprintGenerator.ForNode(NodeKind.Project, "Legacy", "Legacy", "legacy", KnowledgeKind.Fact, GraphMetadata.Empty)));
            accumulation.AddNode(new ArchitectureNode(
                snapshotStableKey,
                packageStableKey,
                NodeKind.Package,
                "Microsoft.AspNet.Mvc",
                "Microsoft.AspNet.Mvc",
                "microsoft.aspnet.mvc",
                null,
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.High,
                UnknownState.Known,
                evidenceStableKey,
                GraphMetadata.Empty,
                FingerprintGenerator.ForNode(NodeKind.Package, "Microsoft.AspNet.Mvc", "Microsoft.AspNet.Mvc", "microsoft.aspnet.mvc", KnowledgeKind.Fact, GraphMetadata.Empty)));
            accumulation.AddEdge(new ArchitectureEdge(
                snapshotStableKey,
                StableKeyGenerator.ForMetric("snapshot://wp012", "depends-on", "mvc"),
                EdgeKind.DependsOn,
                projectStableKey,
                packageStableKey,
                isDirect: true,
                KnowledgeKind.Fact,
                Confidence.High,
                UnknownState.Known,
                evidenceStableKey,
                GraphMetadata.Empty,
                FingerprintGenerator.ForEdge(EdgeKind.DependsOn, projectStableKey, packageStableKey, true, KnowledgeKind.Fact, GraphMetadata.Empty)));
            return accumulation;
        }

        /// <summary>
        /// Creates an accumulator containing representative project, package, and configuration-location facts for the end-to-end WP012 validation path.
        /// </summary>
        /// <param name="secretLikeValue">A deliberately secret-like value used to prove only redacted placeholders enter public output.</param>
        /// <returns>An accumulator populated as if previous extractor stages had emitted all facts needed by the selected rules.</returns>
        private static ArchitectureSnapshotAccumulator CreateAccumulationWithEndToEndFacts(string secretLikeValue)
        {
            // The raw value is intentionally not stored in evidence or metadata; only a redacted location marker is emitted as an extracted fact.
            ArchitectureSnapshotAccumulator accumulation = CreateAccumulationWithLegacyProjectFacts();
            StableKey snapshotStableKey = new("snapshot://wp012/e2e");
            StableKey configurationStableKey = new("configuration://app.config/connection-string-location");
            StableKey configurationEvidenceStableKey = new("evidence://configuration/app-config/connection-string-location");
            accumulation.SetSnapshotHeader(new SnapshotHeader(
                snapshotStableKey,
                new StableKey("repository://wp012"),
                "main",
                "abcdef",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "test",
                "Completed",
                [],
                [],
                GraphMetadata.Empty));
            accumulation.AddEvidence(new EvidenceRecord(
                snapshotStableKey,
                configurationEvidenceStableKey,
                EvidenceKind.Configuration,
                RepositoryRelativePath.Parse("app.config"),
                10,
                12,
                null,
                null,
                "hash-redacted",
                secretLikeValue.Replace(secretLikeValue, "<redacted connection string location>", StringComparison.Ordinal),
                KnowledgeKind.Fact,
                Confidence.High,
                UnknownState.Unknown("Secret-like value was redacted; only the configuration location is available."),
                GraphMetadata.Empty,
                FingerprintGenerator.ForEvidence(EvidenceKind.Configuration, "app.config", 10, 12, null, KnowledgeKind.Fact, GraphMetadata.Empty)));
            accumulation.AddNode(new ArchitectureNode(
                snapshotStableKey,
                configurationStableKey,
                NodeKind.ConfigurationKey,
                "Connection string location",
                "ConnectionStringLocation",
                "connectionStringLocation",
                null,
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Medium,
                UnknownState.Unknown("Secret-like value was redacted; only the configuration location is available."),
                configurationEvidenceStableKey,
                GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["semantic.symbolName"] = "ConnectionStringLocation",
                    ["project.relativePath"] = "app.config",
                    ["redactionState"] = "redacted"
                }),
                FingerprintGenerator.ForNode(NodeKind.ConfigurationKey, "Connection string location", "ConnectionStringLocation", "connectionStringLocation", KnowledgeKind.Fact, GraphMetadata.Empty)));
            return accumulation;
        }

        /// <summary>
        /// Creates the evaluator graph used to construct persisted findings for the end-to-end WP012 validation path.
        /// </summary>
        /// <returns>A graph with deterministic lifecycle and security-sensitive matches.</returns>
        private static RuleEvaluationGraph CreateEndToEndEvaluationGraph()
        {
            // The graph mirrors the projection produced from the accumulator while keeping finding construction focused on evaluator output.
            return new RuleEvaluationGraph(
                [
                    new RuleEvaluationNode(
                        "project://Legacy.csproj",
                        NodeKind.Project,
                        "Legacy",
                        ["net48"],
                        ["Legacy.Web"],
                        ["Legacy", "Legacy.Web"],
                        ["Microsoft.AspNet.Mvc"],
                        ["Legacy.csproj"],
                        [],
                        [],
                        new Dictionary<string, decimal>(StringComparer.Ordinal),
                        ["evidence://wp012/project"],
                        1.0m,
                        []),
                    new RuleEvaluationNode(
                        "configuration://app.config/connection-string-location",
                        NodeKind.ConfigurationKey,
                        "Connection string location",
                        [],
                        [],
                        ["ConnectionStringLocation"],
                        [],
                        ["app.config"],
                        [],
                        [],
                        new Dictionary<string, decimal>(StringComparer.Ordinal),
                        ["evidence://configuration/app-config/connection-string-location"],
                        0.8m,
                        ["Secret-like value was redacted; only the configuration location is available."])
                ]);
        }

        /// <summary>
        /// Creates an isolated temporary rules directory for a test scenario.
        /// </summary>
        /// <returns>The absolute path of the created rules directory.</returns>
        private string CreateRulesDirectory()
        {
            // Each scenario gets a fresh folder so removed-on-disk behavior can be tested explicitly.
            string rulesDirectory = Path.Combine(Path.GetTempPath(), "archon-wp012-rules-" + Guid.NewGuid().ToString("N"), "rules");
            Directory.CreateDirectory(rulesDirectory);
            _temporaryDirectories.Add(Path.GetDirectoryName(rulesDirectory)!);
            return rulesDirectory;
        }

        /// <summary>
        /// Writes a rule JSON fixture into a copied-output style rules directory.
        /// </summary>
        /// <param name="rulesDirectory">The rules directory that receives the fixture.</param>
        /// <param name="fileName">The fixture file name.</param>
        /// <param name="json">The JSON content to write.</param>
        /// <returns>A task that completes after the rule file has been written.</returns>
        private static Task WriteRuleAsync(string rulesDirectory, string fileName, string json)
        {
            // Async writing mirrors the production loader path and keeps tests compatible with cancellation-aware APIs.
            return File.WriteAllTextAsync(Path.Combine(rulesDirectory, fileName), json);
        }

        /// <summary>
        /// Creates a lifecycle rule JSON fixture for extraction integration tests.
        /// </summary>
        /// <param name="ruleCode">The stable rule code to write.</param>
        /// <param name="enabled">A value indicating whether the rule should be evaluated.</param>
        /// <param name="version">The semantic rule version to write.</param>
        /// <returns>A complete JSON rule definition.</returns>
        private static string CreateRuleJson(string ruleCode, bool enabled, string version)
        {
            // The rule checks both target framework and package facts so the integration service must project extraction facts before evaluation.
            return $$"""
                {
                  "ruleCode": "{{ruleCode}}",
                  "name": "Unsupported framework and legacy MVC",
                  "category": "Lifecycle",
                  "severity": "High",
                  "defaultStatus": "OutOfSupport",
                  "enabled": {{enabled.ToString().ToLowerInvariant()}},
                  "version": "{{version}}",
                  "description": "Flags legacy framework projects that reference ASP.NET MVC.",
                  "builtIn": true,
                  "ownerScope": "Archon",
                  "sourceUrls": ["https://example.invalid/wp012"],
                  "impact": ["Legacy framework usage increases modernization risk."],
                  "evidenceRequirements": ["Project target framework and package references must be available."],
                  "recommendedActions": ["Plan migration to supported ASP.NET Core."],
                  "tags": ["lifecycle"],
                  "metadata": {
                    "ruleFamily": "lifecycle"
                  },
                  "detection": {
                    "nodeKinds": ["Project"],
                    "match": "all",
                    "conditions": [
                      { "kind": "target-framework-membership", "operator": "Equal", "value": "net48" },
                      { "kind": "package", "operator": "Equal", "value": "Microsoft.AspNet.Mvc" }
                    ]
                  }
                }
                """;
        }

        /// <summary>
        /// Creates a security-sensitive rule JSON fixture that matches a redacted configuration location rather than any raw secret value.
        /// </summary>
        /// <returns>A complete JSON rule definition for the end-to-end security-sensitive path.</returns>
        private static string CreateSecurityLocationRuleJson()
        {
            // The authored rule models existence and location evidence only, keeping raw credentials outside rule content and finding output.
            return """
                {
                  "ruleCode": "ARCHON-WP012-E2E-SECURITY",
                  "name": "Security-sensitive configuration location",
                  "category": "SecuritySensitive",
                  "severity": "Critical",
                  "defaultStatus": "SecuritySensitive",
                  "enabled": true,
                  "version": "1.0.0",
                  "description": "Flags configuration locations that may contain sensitive connection material without storing secret values.",
                  "builtIn": true,
                  "ownerScope": "Archon",
                  "sourceUrls": ["https://example.invalid/wp012/security"],
                  "impact": ["Sensitive configuration locations require review before modernization."],
                  "evidenceRequirements": ["Configuration location evidence must be available and redacted."],
                  "recommendedActions": ["Move secrets to a managed secret store during migration."],
                  "tags": ["security", "wp012"],
                  "metadata": {
                    "ruleFamily": "security",
                    "storesSecretValues": false
                  },
                  "detection": {
                    "nodeKinds": ["ConfigurationKey"],
                    "match": "all",
                    "conditions": [
                      { "kind": "symbol", "operator": "Equal", "value": "ConnectionStringLocation" },
                      { "kind": "file-pattern", "operator": "MatchesPattern", "value": "*.config" }
                    ]
                  }
                }
                """;
        }
    }
}
