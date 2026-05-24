using Archon.Application.Rules;
using Archon.Domain.Graph.ControlledValues;
using Xunit;

namespace Archon.Application.Tests.Rules
{
    /// <summary>
    /// Verifies the WP012 disk-backed rule catalog loading and validation slice.
    /// </summary>
    public sealed class RuleCatalogLoaderTests : IDisposable
    {
        /// <summary>
        /// Stores temporary rule folders created by tests so filesystem state is cleaned after each scenario.
        /// </summary>
        private readonly List<string> _temporaryDirectories = [];

        /// <summary>
        /// Deletes temporary rule folders after each test scenario completes.
        /// </summary>
        public void Dispose()
        {
            // The loader is intentionally tested against real files because copied-output loading and JSON parse diagnostics are filesystem behaviors.
            foreach (string temporaryDirectory in _temporaryDirectories)
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies a valid built-in lifecycle rule loads from the runtime rules folder and preserves its contract fields.
        /// </summary>
        [Fact]
        public async Task LoadAsync_WhenValidRuleExists_ShouldReturnValidatedCatalog()
        {
            // A copied-output folder is represented by a temporary rules directory that is not the repository-root source folder.
            string rulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(rulesDirectory, "archon.lifecycle.net-framework-unsupported.json", CreateValidRuleJson());
            RuleCatalogLoader loader = CreateLoader(rulesDirectory);

            RuleCatalogLoadResult result = await loader.LoadAsync(CancellationToken.None);

            Assert.True(result.IsValid);
            RuleCatalogEntry rule = Assert.Single(result.Rules);
            Assert.Equal("ARCHON-LIFECYCLE-NETFRAMEWORK-UNSUPPORTED", rule.RuleCode);
            Assert.Equal("1.0.0", rule.Version);
            Assert.Equal(RuleCategory.Lifecycle, rule.Category);
            Assert.Equal(FindingSeverity.High, rule.Severity);
            Assert.Equal(RuleFindingStatus.OutOfSupport, rule.DefaultStatus);
            Assert.True(rule.Enabled);
            Assert.True(rule.AvailableForEvaluation);
            Assert.True(rule.IsBuiltIn);
            Assert.Contains("target framework", rule.Description, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("all", rule.Detection.Match.Value);
            Assert.Contains(NodeKind.Project, rule.Detection.NodeKinds);
            Assert.Empty(result.Diagnostics);
        }

        /// <summary>
        /// Verifies disabled rules still load and validate while being marked unavailable for evaluation.
        /// </summary>
        [Fact]
        public async Task LoadAsync_WhenRuleIsDisabled_ShouldLoadButMarkUnavailableForEvaluation()
        {
            // Disabled catalog entries must remain visible for catalog and historical explainability even though the evaluator skips them later.
            string rulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(rulesDirectory, "disabled.json", CreateValidRuleJson(enabled: false));
            RuleCatalogLoader loader = CreateLoader(rulesDirectory);

            RuleCatalogLoadResult result = await loader.LoadAsync(CancellationToken.None);

            Assert.True(result.IsValid);
            RuleCatalogEntry rule = Assert.Single(result.Rules);
            Assert.False(rule.Enabled);
            Assert.False(rule.AvailableForEvaluation);
        }

        /// <summary>
        /// Verifies missing runtime rule folders are surfaced as deterministic loader diagnostics.
        /// </summary>
        [Fact]
        public async Task LoadAsync_WhenFolderIsMissing_ShouldReturnMissingFolderDiagnostic()
        {
            // The startup path must fail visibly when copied rule content is absent rather than silently running without rules.
            string missingDirectory = Path.Combine(Path.GetTempPath(), "archon-rules-missing-" + Guid.NewGuid().ToString("N"));
            RuleCatalogLoader loader = CreateLoader(missingDirectory);

            RuleCatalogLoadResult result = await loader.LoadAsync(CancellationToken.None);

            Assert.False(result.IsValid);
            RuleCatalogDiagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(RuleCatalogDiagnosticCodes.RuleFolderMissing, diagnostic.Code);
            Assert.Contains(missingDirectory, diagnostic.Message, StringComparison.Ordinal);
            Assert.Empty(result.Rules);
        }

        /// <summary>
        /// Verifies invalid JSON is reported with file and parse-location context where the JSON reader exposes it.
        /// </summary>
        [Fact]
        public async Task LoadAsync_WhenJsonIsInvalid_ShouldReturnParseDiagnostic()
        {
            // Invalid JSON cannot be schema-validated, so the loader reports a parse diagnostic and continues with other files where possible.
            string rulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(rulesDirectory, "invalid-json.json", "{ \"ruleCode\": ");
            RuleCatalogLoader loader = CreateLoader(rulesDirectory);

            RuleCatalogLoadResult result = await loader.LoadAsync(CancellationToken.None);

            Assert.False(result.IsValid);
            RuleCatalogDiagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(RuleCatalogDiagnosticCodes.JsonParseFailed, diagnostic.Code);
            Assert.EndsWith("invalid-json.json", diagnostic.FilePath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("line", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Verifies schema-equivalent validation returns multiple deterministic diagnostics for a malformed rule file.
        /// </summary>
        [Fact]
        public async Task LoadAsync_WhenRuleHasSchemaAndSemanticErrors_ShouldAggregateDiagnostics()
        {
            // This fixture intentionally combines independent validation failures so the loader proves it does not stop at the first simple error.
            string rulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(
                rulesDirectory,
                "invalid-rule.json",
                """
                {
                  "ruleCode": " ",
                  "name": "Invalid rule",
                  "category": "NotACategory",
                  "severity": "Severe",
                  "defaultStatus": "Open",
                  "enabled": true,
                  "version": "one",
                  "description": "Invalid rule for validation coverage.",
                  "builtIn": true,
                  "detection": {
                    "nodeKinds": ["NotANode"],
                    "match": "sometimes",
                    "conditions": [
                      { "kind": "unsupported-kind", "operator": "Equal", "value": "x" },
                      { "kind": "metric-threshold", "metric": "cyclomaticComplexity", "operator": "Contains", "value": 10 }
                    ]
                  }
                }
                """);
            RuleCatalogLoader loader = CreateLoader(rulesDirectory);

            RuleCatalogLoadResult result = await loader.LoadAsync(CancellationToken.None);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleCatalogDiagnosticCodes.RequiredFieldMissing && diagnostic.Path == "ruleCode");
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleCatalogDiagnosticCodes.InvalidCategory);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleCatalogDiagnosticCodes.InvalidSeverity);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleCatalogDiagnosticCodes.InvalidStatus);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleCatalogDiagnosticCodes.InvalidVersion);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleCatalogDiagnosticCodes.InvalidNodeKind);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleCatalogDiagnosticCodes.InvalidMatch);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleCatalogDiagnosticCodes.UnsupportedConditionKind);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleCatalogDiagnosticCodes.OperatorIncompatibleWithCondition);
            Assert.Empty(result.Rules);
        }

        /// <summary>
        /// Verifies empty detection groups are invalid because later evaluation would otherwise have no deterministic predicate operands.
        /// </summary>
        [Fact]
        public async Task LoadAsync_WhenDetectionGroupIsEmpty_ShouldReturnEmptyGroupDiagnostic()
        {
            // A group without conditions or child groups is ambiguous for all, any, and none semantics.
            string rulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(rulesDirectory, "empty-group.json", CreateValidRuleJson(detectionOverride: """
                {
                  "nodeKinds": ["Project"],
                  "match": "all",
                  "conditions": []
                }
                """));
            RuleCatalogLoader loader = CreateLoader(rulesDirectory);

            RuleCatalogLoadResult result = await loader.LoadAsync(CancellationToken.None);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleCatalogDiagnosticCodes.EmptyDetectionGroup);
        }

        /// <summary>
        /// Verifies duplicate rule code and version combinations are rejected deterministically across separate files.
        /// </summary>
        [Fact]
        public async Task LoadAsync_WhenDuplicateRuleIdentityExists_ShouldReturnDuplicateDiagnostic()
        {
            // Rule identity is rule code plus version, so two files cannot contribute the same catalog identity.
            string rulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(rulesDirectory, "first.json", CreateValidRuleJson());
            await WriteRuleAsync(rulesDirectory, "second.json", CreateValidRuleJson());
            RuleCatalogLoader loader = CreateLoader(rulesDirectory);

            RuleCatalogLoadResult result = await loader.LoadAsync(CancellationToken.None);

            Assert.False(result.IsValid);
            RuleCatalogDiagnostic diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == RuleCatalogDiagnosticCodes.DuplicateRuleIdentity);
            Assert.Contains("ARCHON-LIFECYCLE-NETFRAMEWORK-UNSUPPORTED", diagnostic.Message, StringComparison.Ordinal);
            Assert.Contains("1.0.0", diagnostic.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies startup validation throws visibly when built-in rule content is invalid.
        /// </summary>
        [Fact]
        public async Task EnsureValidCatalogAsync_WhenBuiltInRuleIsInvalid_ShouldThrowVisibleException()
        {
            // Hosts and extraction initialization can call the fail-fast helper to avoid silently ignoring broken built-in rules.
            string rulesDirectory = CreateRulesDirectory();
            await WriteRuleAsync(rulesDirectory, "invalid-built-in.json", CreateValidRuleJson(version: "invalid"));
            RuleCatalogLoader loader = CreateLoader(rulesDirectory);

            RuleCatalogValidationException exception = await Assert.ThrowsAsync<RuleCatalogValidationException>(() => loader.EnsureValidCatalogAsync(CancellationToken.None));

            Assert.Contains(RuleCatalogDiagnosticCodes.InvalidVersion, exception.Message, StringComparison.Ordinal);
            Assert.Contains("invalid-built-in.json", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Verifies the project copies repository-root rules into the test runtime output so the default loader path works without source-root assumptions.
        /// </summary>
        [Fact]
        public async Task LoadAsync_WhenUsingDefaultRuntimeOptions_ShouldReadCopiedOutputRules()
        {
            // The test project copies ../../rules into its output; the default options read AppContext.BaseDirectory/rules rather than repository paths.
            RuleCatalogLoader loader = new(new RuleCatalogOptions());

            RuleCatalogLoadResult result = await loader.LoadAsync(CancellationToken.None);

            Assert.True(result.IsValid);
            Assert.Contains(result.Rules, rule => rule.RuleCode == "ARCHON-LIFECYCLE-NETFRAMEWORK-UNSUPPORTED" && rule.IsBuiltIn);
        }

        /// <summary>
        /// Creates a loader instance for a scenario-specific runtime rules folder.
        /// </summary>
        /// <param name="rulesDirectory">The rules directory that the loader should read.</param>
        /// <returns>A configured rule catalog loader.</returns>
        private static RuleCatalogLoader CreateLoader(string rulesDirectory)
        {
            // Passing an explicit options value keeps tests focused on loader behavior rather than dependency injection.
            return new RuleCatalogLoader(new RuleCatalogOptions(rulesDirectory));
        }

        /// <summary>
        /// Creates an isolated temporary rules directory for a test scenario.
        /// </summary>
        /// <returns>The absolute path of the created directory.</returns>
        private string CreateRulesDirectory()
        {
            // Each scenario gets a fresh folder so duplicate detection and missing-folder behavior remain deterministic.
            string rulesDirectory = Path.Combine(Path.GetTempPath(), "archon-rules-" + Guid.NewGuid().ToString("N"), "rules");
            Directory.CreateDirectory(rulesDirectory);
            _temporaryDirectories.Add(Path.GetDirectoryName(rulesDirectory)!);
            return rulesDirectory;
        }

        /// <summary>
        /// Writes a rule JSON fixture into a rules directory.
        /// </summary>
        /// <param name="rulesDirectory">The directory that receives the fixture.</param>
        /// <param name="fileName">The fixture file name.</param>
        /// <param name="json">The JSON content to write.</param>
        /// <returns>A task that completes when the file has been written.</returns>
        private static Task WriteRuleAsync(string rulesDirectory, string fileName, string json)
        {
            // Async file writing mirrors the production loader path and keeps cancellation-compatible APIs natural.
            return File.WriteAllTextAsync(Path.Combine(rulesDirectory, fileName), json);
        }

        /// <summary>
        /// Creates the common valid lifecycle rule JSON used by tests.
        /// </summary>
        /// <param name="enabled">Indicates whether the rule should be available for evaluation.</param>
        /// <param name="version">The semantic version to write into the rule.</param>
        /// <param name="detectionOverride">An optional detection JSON object that replaces the default valid detection block.</param>
        /// <returns>A complete JSON rule definition.</returns>
        private static string CreateValidRuleJson(bool enabled = true, string version = "1.0.0", string? detectionOverride = null)
        {
            // The fixture follows the public rule authoring contract and intentionally includes optional fields used by catalog consumers.
            string detection = detectionOverride ?? """
                {
                  "nodeKinds": ["Project"],
                  "match": "all",
                  "conditions": [
                    {
                      "kind": "target-framework-membership",
                      "operator": "In",
                      "values": ["net48", "net472", "net471", "net47", "net462", "net461", "net46", "net452", "net451", "net45", "net40"]
                    }
                  ]
                }
                """;

            return $$"""
                {
                  "ruleCode": "ARCHON-LIFECYCLE-NETFRAMEWORK-UNSUPPORTED",
                  "name": "Unsupported .NET Framework target framework",
                  "category": "Lifecycle",
                  "severity": "High",
                  "defaultStatus": "OutOfSupport",
                  "enabled": {{enabled.ToString().ToLowerInvariant()}},
                  "version": "{{version}}",
                  "description": "Flags projects that target unsupported .NET Framework target framework monikers.",
                  "builtIn": true,
                  "ownerScope": "Archon",
                  "sourceUrls": ["https://learn.microsoft.com/lifecycle/products/microsoft-net-framework"],
                  "impact": ["Unsupported target frameworks increase operational and security risk."],
                  "evidenceRequirements": ["Project target framework metadata must be available."],
                  "recommendedActions": ["Plan migration to a supported .NET target framework."],
                  "tags": ["lifecycle", "target-framework"],
                  "metadata": {
                    "ruleFamily": "lifecycle",
                    "appliesTo": "project"
                  },
                  "detection": {{detection}}
                }
                """;
        }
    }
}
