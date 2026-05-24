using Archon.Application.Rules;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Model;
using Archon.Domain.Graph.Metadata;
using Xunit;

namespace Archon.Application.Tests.Rules
{
    /// <summary>
    /// Verifies that rule evaluation matches become deterministic finding records with stable identity, evidence, confidence, history, and suppression semantics.
    /// </summary>
    public sealed class FindingConstructionServiceTests
    {
        /// <summary>
        /// Confirms one satisfied rule match is converted into a finding whose stable key, fingerprint, node links, evidence links, metadata, and confidence are deterministic.
        /// </summary>
        [Fact]
        public void CreateFindings_WhenRuleEvaluationMatches_ShouldCreateDeterministicFindingRecords()
        {
            // The service under test is intentionally application-layer only so finding identity can be proven without Neo4j or API hosts.
            FindingConstructionService service = new();
            RuleCatalogEntry rule = CreateRule("ARCHON-FINDING-DETERMINISTIC", "1.0.0", FindingSeverity.High, RuleFindingStatus.OutOfSupport);
            RuleEvaluationMatch match = new(
                rule,
                "project://Legacy/Legacy.csproj",
                ["project://Legacy/Legacy.csproj", "package://Microsoft.AspNet.Mvc"],
                [new RuleMatchedEvidenceReference("target-framework-membership", "target-framework-membership:net48")],
                ["evidence://project/legacy"],
                new RuleEvaluationConfidenceInputs(0.90m, 0.80m, 1));
            RuleEvaluationUnknownState unknownState = new(rule.RuleCode, match.PrimaryNodeStableKey, "Target framework facts were partially degraded.");
            FindingConstructionRequest request = new("snapshot://wp012/current", [rule], [match], [unknownState]);

            FindingConstructionResult result = service.CreateFindings(request);

            FindingRecord finding = Assert.Single(result.Findings);
            Assert.Equal("finding://snapshot://wp012/current/ARCHON-FINDING-DETERMINISTIC/sha256:", finding.StableKey.Value[.."finding://snapshot://wp012/current/ARCHON-FINDING-DETERMINISTIC/sha256:".Length]);
            Assert.Equal("ARCHON-FINDING-DETERMINISTIC", finding.RuleCode);
            Assert.Equal("1.0.0", finding.RuleVersion);
            Assert.Equal(FindingSeverity.High, finding.Severity);
            Assert.Equal(FindingStatus.Open, finding.Status);
            Assert.Equal(KnowledgeKind.Inference, finding.KnowledgeKind);
            Assert.Equal(0.68m, finding.Confidence.Value);
            Assert.Equal("project://Legacy/Legacy.csproj", finding.PrimaryNodeStableKey!.Value.Value);
            Assert.Equal("evidence://project/legacy", finding.PrimaryEvidenceStableKey!.Value.Value);
            Assert.Equal("snapshot://wp012/current", finding.FirstSeenSnapshotStableKey!.Value.Value);
            Assert.Equal("snapshot://wp012/current", finding.LatestSeenSnapshotStableKey!.Value.Value);
            Assert.True(finding.UnknownState.HasUnknownData);
            Assert.Contains("partially degraded", finding.UnknownState.UnknownReason, StringComparison.Ordinal);
            Assert.Equal("sha256:", finding.Fingerprint.Value[.."sha256:".Length]);
            Assert.Equal(["package://Microsoft.AspNet.Mvc", "project://Legacy/Legacy.csproj"], finding.AffectedNodeStableKeys.Select(static key => key.Value).ToArray());
            Assert.Equal("evidence://project/legacy", Assert.Single(finding.EvidenceStableKeys).Value);
            Assert.Contains("target-framework-membership:net48", finding.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Empty(result.Warnings);
        }

        /// <summary>
        /// Confirms equivalent matches in different snapshots keep the same logical history key while receiving snapshot-scoped stable keys.
        /// </summary>
        [Fact]
        public void CreateFindings_WhenEquivalentMatchAppearsInLaterSnapshot_ShouldPreserveHistoryIdentityAndUpdateLatestSeen()
        {
            // Stable history identity excludes snapshot scope, while persisted finding stable keys include snapshot scope for per-snapshot records.
            FindingConstructionService service = new();
            RuleCatalogEntry rule = CreateRule("ARCHON-FINDING-HISTORY", "1.0.0", FindingSeverity.Medium, RuleFindingStatus.Legacy);
            RuleEvaluationMatch firstMatch = CreateMatch(rule, "project://Legacy/Legacy.csproj", "evidence://first");
            FindingConstructionResult firstResult = service.CreateFindings(new FindingConstructionRequest("snapshot://wp012/first", [rule], [firstMatch], []));
            FindingHistorySeed historySeed = FindingHistorySeed.FromFinding(Assert.Single(firstResult.Findings));
            RuleEvaluationMatch secondMatch = CreateMatch(rule, "project://Legacy/Legacy.csproj", "evidence://second");

            FindingConstructionResult secondResult = service.CreateFindings(new FindingConstructionRequest("snapshot://wp012/second", [rule], [secondMatch], [], [historySeed]));

            FindingRecord secondFinding = Assert.Single(secondResult.Findings);
            Assert.NotEqual(Assert.Single(firstResult.Findings).StableKey.Value, secondFinding.StableKey.Value);
            Assert.Equal(historySeed.HistoryKey, secondFinding.HistoryKey);
            Assert.Equal("snapshot://wp012/first", secondFinding.FirstSeenSnapshotStableKey!.Value.Value);
            Assert.Equal("snapshot://wp012/second", secondFinding.LatestSeenSnapshotStableKey!.Value.Value);
        }

        /// <summary>
        /// Confirms duplicate rule matches for the same snapshot and logical target create only one finding and report a deterministic warning.
        /// </summary>
        [Fact]
        public void CreateFindings_WhenEquivalentMatchAlreadyExistsInSnapshot_ShouldDeduplicateFindings()
        {
            // Deduplication protects persistence from writing duplicate findings when an evaluator returns equivalent affected-node context twice.
            FindingConstructionService service = new();
            RuleCatalogEntry rule = CreateRule("ARCHON-FINDING-DEDUP", "1.0.0", FindingSeverity.Low, RuleFindingStatus.Discouraged);
            RuleEvaluationMatch firstMatch = CreateMatch(rule, "project://Legacy/Legacy.csproj", "evidence://first");
            RuleEvaluationMatch duplicateMatch = CreateMatch(rule, "project://Legacy/Legacy.csproj", "evidence://second");

            FindingConstructionResult result = service.CreateFindings(new FindingConstructionRequest("snapshot://wp012/current", [rule], [firstMatch, duplicateMatch], []));

            Assert.Single(result.Findings);
            Assert.Contains(result.Warnings, warning => warning.Code == FindingConstructionWarningCodes.DuplicateFindingInSnapshot);
        }

        /// <summary>
        /// Confirms validated suppression requests mark matching findings as suppressed without deleting the finding record or changing its stable identity.
        /// </summary>
        [Fact]
        public void ApplySuppression_WhenMatchingSuppressionExists_ShouldMarkFindingSuppressedAndPreserveIdentity()
        {
            // Suppression is a durable lifecycle overlay: it records why a finding is intentionally hidden while preserving the underlying finding.
            FindingConstructionService service = new();
            RuleCatalogEntry rule = CreateRule("ARCHON-FINDING-SUPPRESS", "1.0.0", FindingSeverity.High, RuleFindingStatus.SecuritySensitive);
            FindingRecord finding = Assert.Single(service.CreateFindings(new FindingConstructionRequest("snapshot://wp012/current", [rule], [CreateMatch(rule, "project://Legacy/Legacy.csproj", "evidence://legacy")], [])).Findings);
            SuppressFindingRequest suppression = new(
                finding.HistoryKey,
                finding.RuleCode,
                finding.RuleVersion,
                finding.PrimaryNodeStableKey!.Value.Value,
                "Accepted migration waiver until Q4.",
                "architect@example.invalid",
                GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["ticket"] = "ARCH-123"
                }));

            SuppressFindingResult result = service.ApplySuppression(finding, [suppression]);

            Assert.True(result.Suppressed);
            Assert.Empty(result.ValidationErrors);
            Assert.Equal(finding.StableKey.Value, result.Finding.StableKey.Value);
            Assert.Equal(FindingStatus.Suppressed, result.Finding.Status);
            Assert.Equal("Accepted migration waiver until Q4.", result.Finding.SuppressionReason);
            Assert.Equal("architect@example.invalid", result.Finding.SuppressedBy);
            Assert.Contains("ARCH-123", result.Finding.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
        }

        /// <summary>
        /// Confirms invalid suppression requests return validation errors instead of modifying the finding lifecycle state.
        /// </summary>
        [Fact]
        public void ApplySuppression_WhenRequestIsInvalid_ShouldReturnValidationErrorsAndLeaveFindingOpen()
        {
            // Validation prevents untraceable suppressions because reason and actor fields are required audit data.
            FindingConstructionService service = new();
            RuleCatalogEntry rule = CreateRule("ARCHON-FINDING-SUPPRESS-INVALID", "1.0.0", FindingSeverity.Info, RuleFindingStatus.Unknown);
            FindingRecord finding = Assert.Single(service.CreateFindings(new FindingConstructionRequest("snapshot://wp012/current", [rule], [CreateMatch(rule, "project://Legacy/Legacy.csproj", "evidence://legacy")], [])).Findings);
            SuppressFindingRequest invalidSuppression = new(finding.HistoryKey, finding.RuleCode, finding.RuleVersion, finding.PrimaryNodeStableKey!.Value.Value, " ", " ", GraphMetadata.Empty);

            SuppressFindingResult result = service.ApplySuppression(finding, [invalidSuppression]);

            Assert.False(result.Suppressed);
            Assert.Equal(FindingStatus.Unknown, result.Finding.Status);
            Assert.Contains(result.ValidationErrors, error => error.Code == SuppressFindingValidationCodes.MissingReason);
            Assert.Contains(result.ValidationErrors, error => error.Code == SuppressFindingValidationCodes.MissingSuppressedBy);
        }

        /// <summary>
        /// Confirms the in-memory finding store upserts findings, exposes history, and applies suppressions across later equivalent snapshots.
        /// </summary>
        /// <returns>A task that completes after in-memory persistence behavior has been asserted.</returns>
        [Fact]
        public async Task InMemoryFindingStore_WhenSuppressionExists_ShouldApplySuppressionAcrossLaterSnapshots()
        {
            // The in-memory store gives extraction and tests the same application persistence seam that Neo4j implements later in the slice.
            FindingConstructionService service = new();
            InMemoryFindingStore store = new();
            RuleCatalogEntry rule = CreateRule("ARCHON-FINDING-STORE", "1.0.0", FindingSeverity.High, RuleFindingStatus.MigrationBlocker);
            FindingRecord firstFinding = Assert.Single(service.CreateFindings(new FindingConstructionRequest("snapshot://wp012/first", [rule], [CreateMatch(rule, "project://Legacy/Legacy.csproj", "evidence://first")], [])).Findings);
            await store.UpsertFindingsAsync([firstFinding], CancellationToken.None);
            SuppressFindingRequest suppression = new(firstFinding.HistoryKey, firstFinding.RuleCode, firstFinding.RuleVersion, firstFinding.PrimaryNodeStableKey!.Value.Value, "Accepted risk.", "architect@example.invalid", GraphMetadata.Empty);

            SuppressionPersistenceResult suppressionResult = await store.SuppressFindingsAsync([suppression], CancellationToken.None);
            IReadOnlyList<FindingHistorySeed> history = await store.GetHistoryAsync([firstFinding.HistoryKey], CancellationToken.None);
            FindingHistorySeed historySeed = Assert.Single(history);
            FindingRecord secondFinding = Assert.Single(service.CreateFindings(new FindingConstructionRequest("snapshot://wp012/second", [rule], [CreateMatch(rule, "project://Legacy/Legacy.csproj", "evidence://second")], [], [historySeed])).Findings);
            await store.UpsertFindingsAsync([secondFinding], CancellationToken.None);

            Assert.True(suppressionResult.Succeeded);
            Assert.Equal(1, suppressionResult.SuppressedFindingCount);
            FindingRecord? persistedFirst = await store.GetFindingAsync(firstFinding.SnapshotStableKey.Value, firstFinding.StableKey.Value, CancellationToken.None);
            FindingRecord? persistedSecond = await store.GetFindingAsync(secondFinding.SnapshotStableKey.Value, secondFinding.StableKey.Value, CancellationToken.None);
            Assert.Equal(FindingStatus.Suppressed, persistedFirst!.Status);
            Assert.Equal(FindingStatus.Suppressed, persistedSecond!.Status);
            Assert.Equal("snapshot://wp012/first", persistedSecond.FirstSeenSnapshotStableKey!.Value.Value);
            Assert.Equal("snapshot://wp012/second", persistedSecond.LatestSeenSnapshotStableKey!.Value.Value);
        }

        /// <summary>
        /// Creates a deterministic rule catalog fixture for finding construction scenarios.
        /// </summary>
        /// <param name="ruleCode">The stable rule code to assign to the fixture.</param>
        /// <param name="version">The rule version to assign to the fixture.</param>
        /// <param name="severity">The default finding severity to assign to the fixture.</param>
        /// <param name="status">The rule default status to assign to the fixture.</param>
        /// <returns>A validated rule catalog entry fixture.</returns>
        private static RuleCatalogEntry CreateRule(string ruleCode, string version, FindingSeverity severity, RuleFindingStatus status)
        {
            // The detection group is valid but not evaluated in these tests; construction consumes the already-satisfied match output.
            return new RuleCatalogEntry(
                ruleCode,
                "Legacy modernization finding",
                RuleCategory.Lifecycle,
                severity,
                status,
                enabled: true,
                version,
                "Flags a legacy modernization concern.",
                "{\"ruleCode\":\"" + ruleCode + "\"}",
                ["https://example.invalid/rules/" + ruleCode],
                isBuiltIn: true,
                ownerScope: "Archon",
                impact: ["Legacy usage increases modernization risk."],
                evidenceRequirements: ["Project facts must be available."],
                recommendedActions: ["Plan a migration."],
                tags: ["wp012"],
                GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["ruleFamily"] = "findingTests"
                }),
                new RuleDetectionGroup([NodeKind.Project], RuleDetectionMatch.MatchAll, [], []),
                "rules/" + ruleCode + ".json");
        }

        /// <summary>
        /// Creates a deterministic rule evaluation match fixture for finding construction scenarios.
        /// </summary>
        /// <param name="rule">The rule that should appear as the matched rule.</param>
        /// <param name="nodeStableKey">The primary affected node stable key.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key.</param>
        /// <returns>A rule evaluation match with normalized affected-node and evidence context.</returns>
        private static RuleEvaluationMatch CreateMatch(RuleCatalogEntry rule, string nodeStableKey, string evidenceStableKey)
        {
            // The confidence inputs intentionally use full confidence and no unknowns so history tests focus on identity, not confidence math.
            return new RuleEvaluationMatch(
                rule,
                nodeStableKey,
                [nodeStableKey],
                [new RuleMatchedEvidenceReference("target-framework-membership", "target-framework-membership:net48")],
                [evidenceStableKey],
                new RuleEvaluationConfidenceInputs(1m, 1m, 0));
        }
    }
}
