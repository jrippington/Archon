using System.Security.Cryptography;
using System.Text;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Converts deterministic rule evaluation matches into snapshot-owned finding records and applies suppression overlays.
    /// </summary>
    public sealed class FindingConstructionService
    {
        /// <summary>
        /// Creates deterministic finding records from satisfied rule evaluation matches.
        /// </summary>
        /// <param name="request">The finding construction request containing snapshot, rule, match, unknown, and history context.</param>
        /// <returns>The constructed findings and non-blocking construction warnings.</returns>
        public FindingConstructionResult CreateFindings(FindingConstructionRequest request)
        {
            // Construction is a pure application-layer transformation so persistence adapters can store findings without re-running rule evaluation.
            ArgumentNullException.ThrowIfNull(request);
            Dictionary<string, RuleCatalogEntry> rulesByIdentity = request.Rules.ToDictionary(static rule => BuildRuleIdentity(rule.RuleCode, rule.Version), StringComparer.Ordinal);
            Dictionary<string, FindingHistorySeed> historyByKey = request.HistorySeeds.ToDictionary(static seed => seed.HistoryKey, StringComparer.Ordinal);
            HashSet<string> seenHistoryKeys = new(StringComparer.Ordinal);
            List<FindingRecord> findings = [];
            List<FindingConstructionWarning> warnings = [];

            foreach (RuleEvaluationMatch match in request.Matches)
            {
                if (!rulesByIdentity.TryGetValue(BuildRuleIdentity(match.RuleCode, match.RuleVersion), out RuleCatalogEntry? rule))
                {
                    warnings.Add(new FindingConstructionWarning(FindingConstructionWarningCodes.MissingRuleForMatch, $"No rule catalog entry was supplied for match {match.RuleCode} version {match.RuleVersion}."));
                    continue;
                }

                string historyKey = BuildHistoryKey(match);
                if (!seenHistoryKeys.Add(historyKey))
                {
                    warnings.Add(new FindingConstructionWarning(FindingConstructionWarningCodes.DuplicateFindingInSnapshot, $"A finding for history key {historyKey} was already constructed for snapshot {request.SnapshotStableKey}."));
                    continue;
                }

                historyByKey.TryGetValue(historyKey, out FindingHistorySeed? historySeed);
                findings.Add(CreateFinding(request.SnapshotStableKey, rule, match, request.UnknownStates, historyKey, historySeed));
            }

            return new FindingConstructionResult(findings, warnings);
        }

        /// <summary>
        /// Applies the first matching valid suppression request to a finding while preserving the underlying finding identity.
        /// </summary>
        /// <param name="finding">The finding that may receive a suppression overlay.</param>
        /// <param name="suppressionRequests">The candidate suppression requests to evaluate.</param>
        /// <returns>The suppression result containing either the updated finding or validation errors.</returns>
        public SuppressFindingResult ApplySuppression(FindingRecord finding, IEnumerable<SuppressFindingRequest> suppressionRequests)
        {
            // Suppression is matched by history, rule, version, and primary node so later snapshots inherit the waiver for equivalent findings.
            ArgumentNullException.ThrowIfNull(finding);
            ArgumentNullException.ThrowIfNull(suppressionRequests);
            foreach (SuppressFindingRequest suppression in suppressionRequests.OrderBy(static request => request.FindingHistoryKey, StringComparer.Ordinal))
            {
                if (!SuppressionTargetsFinding(finding, suppression))
                {
                    continue;
                }

                IReadOnlyList<SuppressFindingValidationError> validationErrors = ValidateSuppression(suppression);
                if (validationErrors.Count > 0)
                {
                    return new SuppressFindingResult(finding, suppressed: false, validationErrors);
                }

                FindingRecord suppressedFinding = CloneWithSuppression(finding, suppression);
                return new SuppressFindingResult(suppressedFinding, suppressed: true, []);
            }

            return new SuppressFindingResult(finding, suppressed: false, []);
        }

        /// <summary>
        /// Validates one suppression request independently from any matching finding record.
        /// </summary>
        /// <param name="suppression">The suppression request to validate.</param>
        /// <returns>The deterministic validation errors for missing required fields.</returns>
        public IReadOnlyList<SuppressFindingValidationError> ValidateSuppressionRequest(SuppressFindingRequest suppression)
        {
            // Store implementations use this when a suppression targets future findings and no current finding exists for validation through ApplySuppression.
            ArgumentNullException.ThrowIfNull(suppression);
            return ValidateSuppression(suppression);
        }

        /// <summary>
        /// Creates one finding record from one matched rule context.
        /// </summary>
        /// <param name="snapshotStableKey">The current snapshot stable key.</param>
        /// <param name="rule">The catalog rule that produced the match.</param>
        /// <param name="match">The evaluator match being converted.</param>
        /// <param name="unknownStates">The evaluator unknown states available for this construction pass.</param>
        /// <param name="historyKey">The deterministic cross-snapshot history key for the finding.</param>
        /// <param name="historySeed">The optional prior history seed for first-seen resolution.</param>
        /// <returns>A finding record ready for accumulation or persistence.</returns>
        private static FindingRecord CreateFinding(string snapshotStableKey, RuleCatalogEntry rule, RuleEvaluationMatch match, IReadOnlyList<RuleEvaluationUnknownState> unknownStates, string historyKey, FindingHistorySeed? historySeed)
        {
            // The stable key is snapshot-scoped, while the history key intentionally excludes snapshot scope for cross-snapshot continuity.
            StableKey snapshotKey = new(snapshotStableKey);
            string targetIdentity = BuildTargetIdentity(match);
            StableKey stableKey = StableKeyGenerator.ForFinding(snapshotStableKey, rule.RuleCode, targetIdentity);
            IReadOnlyList<string> unknownReasons = FindUnknownReasons(rule.RuleCode, match.PrimaryNodeStableKey, unknownStates);
            UnknownState unknownState = unknownReasons.Count == 0 ? UnknownState.Known : UnknownState.Unknown(string.Join(" | ", unknownReasons));
            decimal confidence = DeriveConfidence(match.ConfidenceInputs, unknownReasons.Count);
            GraphMetadata metadata = BuildFindingMetadata(rule, match, historyKey, unknownReasons);
            Fingerprint fingerprint = FingerprintGenerator.ForFinding(rule.RuleCode, rule.Version, rule.Severity, MapStatus(rule.DefaultStatus), rule.Name, KnowledgeKind.Inference, metadata);
            StableKey? primaryEvidenceStableKey = match.EvidenceStableKeys.Count == 0 ? null : new StableKey(match.EvidenceStableKeys[0]);
            string firstSeen = historySeed?.FirstSeenSnapshotStableKey ?? snapshotStableKey;

            return new FindingRecord(
                snapshotKey,
                stableKey,
                rule.RuleCode,
                rule.Version,
                rule.Severity,
                MapStatus(rule.DefaultStatus),
                rule.Name,
                rule.Description,
                KnowledgeKind.Inference,
                new Confidence(confidence),
                unknownState,
                new StableKey(match.PrimaryNodeStableKey),
                primaryEvidenceStableKey,
                new StableKey(firstSeen),
                snapshotKey,
                suppressionReason: null,
                suppressedBy: null,
                match.AffectedNodeStableKeys.Select(static stableKeyValue => new StableKey(stableKeyValue)),
                match.EvidenceStableKeys.Select(static stableKeyValue => new StableKey(stableKeyValue)),
                historyKey,
                metadata,
                fingerprint);
        }

        /// <summary>
        /// Builds the cross-snapshot history key for one matched rule and target context.
        /// </summary>
        /// <param name="match">The evaluator match whose history identity should be built.</param>
        /// <returns>A deterministic history key independent of snapshot scope.</returns>
        private static string BuildHistoryKey(RuleEvaluationMatch match)
        {
            // History identity uses rule version, affected nodes, and matched condition evidence so rule changes and target changes remain distinct.
            string input = string.Join(
                "\u001F",
                match.RuleCode,
                match.RuleVersion,
                match.PrimaryNodeStableKey,
                string.Join("\u001E", match.AffectedNodeStableKeys),
                string.Join("\u001E", match.MatchedEvidenceReferences.Select(static evidence => evidence.ConditionKind + ":" + evidence.Reference)));
            return "history://finding/" + ComputeSha256(input);
        }

        /// <summary>
        /// Builds the snapshot-scoped finding target discriminator used by stable-key generation.
        /// </summary>
        /// <param name="match">The evaluator match whose target discriminator should be built.</param>
        /// <returns>A deterministic target discriminator.</returns>
        private static string BuildTargetIdentity(RuleEvaluationMatch match)
        {
            // The domain stable-key generator accepts a readable discriminator, so a hash keeps special characters and path separators from becoming ambiguous.
            string input = string.Join("\u001F", match.RuleVersion, match.PrimaryNodeStableKey, string.Join("\u001E", match.AffectedNodeStableKeys), string.Join("\u001E", match.MatchedEvidenceReferences.Select(static evidence => evidence.Reference)));
            return ComputeSha256(input);
        }

        /// <summary>
        /// Builds deterministic metadata that preserves rule, evidence, confidence-input, and unknown-context details not modeled as first-class finding properties.
        /// </summary>
        /// <param name="rule">The rule that produced the finding.</param>
        /// <param name="match">The evaluator match being converted.</param>
        /// <param name="historyKey">The cross-snapshot history key.</param>
        /// <param name="unknownReasons">The unknown reasons associated with the finding.</param>
        /// <returns>Canonical finding metadata.</returns>
        private static GraphMetadata BuildFindingMetadata(RuleCatalogEntry rule, RuleEvaluationMatch match, string historyKey, IReadOnlyList<string> unknownReasons)
        {
            // Metadata uses lower camel case names so later API DTOs can expose stable extension fields without reparsing rule output.
            return GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["affectedNodeStableKeys"] = match.AffectedNodeStableKeys.ToArray(),
                ["evidenceStableKeys"] = match.EvidenceStableKeys.ToArray(),
                ["findingHistoryKey"] = historyKey,
                ["matchedEvidenceReferences"] = match.MatchedEvidenceReferences.Select(static evidence => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["conditionKind"] = evidence.ConditionKind,
                    ["reference"] = evidence.Reference
                }).ToArray(),
                ["ruleCategory"] = rule.Category.Value,
                ["ruleDefaultStatus"] = rule.DefaultStatus.Value,
                ["ruleSourceUrls"] = rule.SourceUrls.ToArray(),
                ["ruleTags"] = rule.Tags.ToArray(),
                ["confidenceInputs"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["ruleConfidence"] = match.ConfidenceInputs.RuleConfidence,
                    ["factConfidence"] = match.ConfidenceInputs.FactConfidence,
                    ["unknownCount"] = match.ConfidenceInputs.UnknownCount
                },
                ["unknownReasons"] = unknownReasons.ToArray()
            });
        }

        /// <summary>
        /// Derives final finding confidence from rule confidence, fact confidence, and unknown-state context.
        /// </summary>
        /// <param name="inputs">The evaluator confidence inputs.</param>
        /// <param name="unknownReasonCount">The number of unknown reasons preserved on the finding.</param>
        /// <returns>A normalized confidence value rounded to two decimal places.</returns>
        private static decimal DeriveConfidence(RuleEvaluationConfidenceInputs inputs, int unknownReasonCount)
        {
            // Unknown data reduces confidence deterministically while preserving a lower bound for evidence-backed matches.
            ArgumentNullException.ThrowIfNull(inputs);
            int totalUnknowns = Math.Max(inputs.UnknownCount, unknownReasonCount);
            decimal unknownPenalty = Math.Min(0.5m, totalUnknowns * 0.05m);
            decimal confidence = inputs.RuleConfidence * inputs.FactConfidence * (1m - unknownPenalty);
            return decimal.Round(Math.Clamp(confidence, 0m, 1m), 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Finds unknown-state reasons that apply to the supplied rule and primary node.
        /// </summary>
        /// <param name="ruleCode">The rule code to match.</param>
        /// <param name="nodeStableKey">The primary node stable key to match.</param>
        /// <param name="unknownStates">The unknown-state contexts emitted by evaluation.</param>
        /// <returns>A deterministic list of unknown reasons.</returns>
        private static IReadOnlyList<string> FindUnknownReasons(string ruleCode, string nodeStableKey, IReadOnlyList<RuleEvaluationUnknownState> unknownStates)
        {
            // Unknown context is attached to the finding only when it matches both the rule and affected primary node.
            return unknownStates
                .Where(unknown => StringComparer.Ordinal.Equals(unknown.RuleCode, ruleCode) && StringComparer.Ordinal.Equals(unknown.NodeStableKey, nodeStableKey))
                .Select(static unknown => unknown.Reason)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static reason => reason, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Maps WP012 rule-authored status values onto the current persisted finding lifecycle vocabulary.
        /// </summary>
        /// <param name="status">The WP012 rule status.</param>
        /// <returns>The persisted finding lifecycle status.</returns>
        private static FindingStatus MapStatus(RuleFindingStatus status)
        {
            // Specific modernization statuses remain in metadata while the current graph lifecycle status uses Open/Unknown/Suppressed.
            ArgumentNullException.ThrowIfNull(status);
            return status == RuleFindingStatus.Unknown ? FindingStatus.Unknown : FindingStatus.Open;
        }

        /// <summary>
        /// Determines whether a suppression request targets the supplied finding.
        /// </summary>
        /// <param name="finding">The finding that may be suppressed.</param>
        /// <param name="suppression">The suppression request being inspected.</param>
        /// <returns><see langword="true"/> when the request targets the finding; otherwise, <see langword="false"/>.</returns>
        private static bool SuppressionTargetsFinding(FindingRecord finding, SuppressFindingRequest suppression)
        {
            // Matching all identity fields avoids applying a waiver to a different rule version or affected node by history-key collision alone.
            return StringComparer.Ordinal.Equals(finding.HistoryKey, suppression.FindingHistoryKey)
                && StringComparer.Ordinal.Equals(finding.RuleCode, suppression.RuleCode)
                && StringComparer.Ordinal.Equals(finding.RuleVersion, suppression.RuleVersion)
                && StringComparer.Ordinal.Equals(finding.PrimaryNodeStableKey?.Value, suppression.PrimaryNodeStableKey);
        }

        /// <summary>
        /// Validates a suppression request and returns all missing required fields.
        /// </summary>
        /// <param name="suppression">The suppression request to validate.</param>
        /// <returns>The deterministic validation errors.</returns>
        private static IReadOnlyList<SuppressFindingValidationError> ValidateSuppression(SuppressFindingRequest suppression)
        {
            // Aggregating validation failures lets callers correct all required audit fields in one request cycle.
            List<SuppressFindingValidationError> errors = [];
            AddRequiredError(errors, suppression.FindingHistoryKey, SuppressFindingValidationCodes.MissingFindingHistoryKey, "Suppression requires a finding history key.");
            AddRequiredError(errors, suppression.RuleCode, SuppressFindingValidationCodes.MissingRuleCode, "Suppression requires a rule code.");
            AddRequiredError(errors, suppression.RuleVersion, SuppressFindingValidationCodes.MissingRuleVersion, "Suppression requires a rule version.");
            AddRequiredError(errors, suppression.PrimaryNodeStableKey, SuppressFindingValidationCodes.MissingPrimaryNodeStableKey, "Suppression requires a primary node stable key.");
            AddRequiredError(errors, suppression.Reason, SuppressFindingValidationCodes.MissingReason, "Suppression requires a reason.");
            AddRequiredError(errors, suppression.SuppressedBy, SuppressFindingValidationCodes.MissingSuppressedBy, "Suppression requires a suppressed-by identity.");
            return errors;
        }

        /// <summary>
        /// Adds one required-field validation error when a value is missing.
        /// </summary>
        /// <param name="errors">The validation error list being accumulated.</param>
        /// <param name="value">The required value to inspect.</param>
        /// <param name="code">The stable validation code to add when missing.</param>
        /// <param name="message">The validation message to add when missing.</param>
        private static void AddRequiredError(List<SuppressFindingValidationError> errors, string value, string code, string message)
        {
            // A helper keeps validation field checks consistent and easy to extend as the suppression contract grows.
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new SuppressFindingValidationError(code, message));
            }
        }

        /// <summary>
        /// Creates a copy of a finding with suppression fields and lifecycle status applied.
        /// </summary>
        /// <param name="finding">The original finding to copy.</param>
        /// <param name="suppression">The valid suppression request to apply.</param>
        /// <returns>A suppressed finding that preserves stable identity and links.</returns>
        private static FindingRecord CloneWithSuppression(FindingRecord finding, SuppressFindingRequest suppression)
        {
            // Suppression metadata is merged into the finding metadata so query slices can expose audit context without a separate record shape.
            GraphMetadata metadata = MergeSuppressionMetadata(finding, suppression);
            Fingerprint fingerprint = FingerprintGenerator.ForFinding(finding.RuleCode, finding.RuleVersion, finding.Severity, FindingStatus.Suppressed, finding.Title, finding.KnowledgeKind, metadata);
            return new FindingRecord(
                finding.SnapshotStableKey,
                finding.StableKey,
                finding.RuleCode,
                finding.RuleVersion,
                finding.Severity,
                FindingStatus.Suppressed,
                finding.Title,
                finding.Description,
                finding.KnowledgeKind,
                finding.Confidence,
                finding.UnknownState,
                finding.PrimaryNodeStableKey,
                finding.PrimaryEvidenceStableKey,
                finding.FirstSeenSnapshotStableKey,
                finding.LatestSeenSnapshotStableKey,
                suppression.Reason,
                suppression.SuppressedBy,
                finding.AffectedNodeStableKeys,
                finding.EvidenceStableKeys,
                finding.HistoryKey,
                metadata,
                fingerprint);
        }

        /// <summary>
        /// Builds metadata for a suppressed finding by nesting prior finding metadata and suppression metadata.
        /// </summary>
        /// <param name="finding">The finding being suppressed.</param>
        /// <param name="suppression">The valid suppression request being applied.</param>
        /// <returns>Canonical metadata that includes suppression audit context.</returns>
        private static GraphMetadata MergeSuppressionMetadata(FindingRecord finding, SuppressFindingRequest suppression)
        {
            // Nesting the original canonical metadata preserves construction context without needing to parse and merge arbitrary JSON properties.
            return GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["findingMetadataJson"] = finding.Metadata.ToCanonicalJson(),
                ["suppression"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["reason"] = suppression.Reason,
                    ["suppressedBy"] = suppression.SuppressedBy,
                    ["metadataJson"] = suppression.Metadata.ToCanonicalJson()
                }
            });
        }

        /// <summary>
        /// Builds a private composite key for rule lookup.
        /// </summary>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="ruleVersion">The exact rule version.</param>
        /// <returns>A deterministic rule identity key.</returns>
        private static string BuildRuleIdentity(string ruleCode, string ruleVersion)
        {
            // The separator stays private to this service because external contracts keep rule code and version separate.
            return string.Concat(ruleCode, "\u001F", ruleVersion);
        }

        /// <summary>
        /// Computes a lower-case SHA-256 digest for stable identity components.
        /// </summary>
        /// <param name="input">The canonical identity input.</param>
        /// <returns>A sha256-prefixed digest string.</returns>
        private static string ComputeSha256(string input)
        {
            // SHA-256 keeps stable keys and history keys independent from absolute paths, Neo4j IDs, and process-local identifiers.
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            byte[] hash = SHA256.HashData(bytes);
            return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
