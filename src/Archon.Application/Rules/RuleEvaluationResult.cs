namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents the complete deterministic output from evaluating a rule catalog against graph facts.
    /// </summary>
    public sealed class RuleEvaluationResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleEvaluationResult"/> class.
        /// </summary>
        /// <param name="matches">The matched rule contexts produced by evaluation.</param>
        /// <param name="warnings">The warnings produced when evaluation was partial or rule-specific data issues were encountered.</param>
        /// <param name="unknownStates">The explicit unknown-state contexts observed while evaluating matching or partially matching candidates.</param>
        public RuleEvaluationResult(
            IEnumerable<RuleEvaluationMatch> matches,
            IEnumerable<RuleEvaluationWarning> warnings,
            IEnumerable<RuleEvaluationUnknownState> unknownStates)
        {
            // Output collections are sorted during construction so callers can compare results without knowing evaluator traversal details.
            ArgumentNullException.ThrowIfNull(matches);
            ArgumentNullException.ThrowIfNull(warnings);
            ArgumentNullException.ThrowIfNull(unknownStates);
            Matches = matches.OrderBy(static match => match.RuleCode, StringComparer.Ordinal).ThenBy(static match => match.PrimaryNodeStableKey, StringComparer.Ordinal).ToArray();
            Warnings = warnings.OrderBy(static warning => warning.RuleCode, StringComparer.Ordinal).ThenBy(static warning => warning.NodeStableKey, StringComparer.Ordinal).ThenBy(static warning => warning.Message, StringComparer.Ordinal).ToArray();
            UnknownStates = unknownStates.OrderBy(static unknown => unknown.RuleCode, StringComparer.Ordinal).ThenBy(static unknown => unknown.NodeStableKey, StringComparer.Ordinal).ThenBy(static unknown => unknown.Reason, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Gets the matched rule contexts produced by evaluation.
        /// </summary>
        public IReadOnlyList<RuleEvaluationMatch> Matches { get; }

        /// <summary>
        /// Gets the warnings produced when evaluation was partial or rule-specific data issues were encountered.
        /// </summary>
        public IReadOnlyList<RuleEvaluationWarning> Warnings { get; }

        /// <summary>
        /// Gets the explicit unknown-state contexts observed while evaluating matching or partially matching candidates.
        /// </summary>
        public IReadOnlyList<RuleEvaluationUnknownState> UnknownStates { get; }
    }

    /// <summary>
    /// Represents one satisfied rule predicate for one affected graph node.
    /// </summary>
    public sealed class RuleEvaluationMatch
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleEvaluationMatch"/> class.
        /// </summary>
        /// <param name="rule">The catalog rule that matched.</param>
        /// <param name="primaryNodeStableKey">The primary affected graph node stable key.</param>
        /// <param name="affectedNodeStableKeys">The affected node stable keys for the match.</param>
        /// <param name="matchedEvidenceReferences">The condition-level evidence references that explain the match.</param>
        /// <param name="evidenceStableKeys">The graph evidence stable keys associated with the affected node.</param>
        /// <param name="confidenceInputs">The confidence input data captured for later finding confidence calculation.</param>
        public RuleEvaluationMatch(
            RuleCatalogEntry rule,
            string primaryNodeStableKey,
            IEnumerable<string> affectedNodeStableKeys,
            IEnumerable<RuleMatchedEvidenceReference> matchedEvidenceReferences,
            IEnumerable<string> evidenceStableKeys,
            RuleEvaluationConfidenceInputs confidenceInputs)
        {
            // Matches preserve rule identity and node/evidence context so later finding slices can create stable findings without re-evaluating predicates.
            ArgumentNullException.ThrowIfNull(rule);
            RuleCode = rule.RuleCode;
            RuleVersion = rule.Version;
            RuleName = rule.Name;
            PrimaryNodeStableKey = RequireText(primaryNodeStableKey, nameof(primaryNodeStableKey));
            AffectedNodeStableKeys = NormalizeText(affectedNodeStableKeys);
            MatchedEvidenceReferences = (matchedEvidenceReferences ?? throw new ArgumentNullException(nameof(matchedEvidenceReferences))).OrderBy(static evidence => evidence.Reference, StringComparer.Ordinal).ToArray();
            EvidenceStableKeys = NormalizeText(evidenceStableKeys);
            ConfidenceInputs = confidenceInputs ?? throw new ArgumentNullException(nameof(confidenceInputs));
        }

        /// <summary>
        /// Gets the stable rule code that matched.
        /// </summary>
        public string RuleCode { get; }

        /// <summary>
        /// Gets the exact rule version that matched.
        /// </summary>
        public string RuleVersion { get; }

        /// <summary>
        /// Gets the human-readable rule name that matched.
        /// </summary>
        public string RuleName { get; }

        /// <summary>
        /// Gets the primary affected graph node stable key.
        /// </summary>
        public string PrimaryNodeStableKey { get; }

        /// <summary>
        /// Gets the affected node stable keys for the match.
        /// </summary>
        public IReadOnlyList<string> AffectedNodeStableKeys { get; }

        /// <summary>
        /// Gets the condition-level evidence references that explain the match.
        /// </summary>
        public IReadOnlyList<RuleMatchedEvidenceReference> MatchedEvidenceReferences { get; }

        /// <summary>
        /// Gets the graph evidence stable keys associated with the affected node.
        /// </summary>
        public IReadOnlyList<string> EvidenceStableKeys { get; }

        /// <summary>
        /// Gets the confidence input data captured for later finding confidence calculation.
        /// </summary>
        public RuleEvaluationConfidenceInputs ConfidenceInputs { get; }

        /// <summary>
        /// Requires a non-empty text value and returns its trimmed form.
        /// </summary>
        /// <param name="value">The candidate value.</param>
        /// <param name="parameterName">The parameter name used for invalid input exceptions.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Blank identifiers would prevent deterministic finding identity in later WP012 slices.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }

        /// <summary>
        /// Normalizes text output values into a deterministic immutable list.
        /// </summary>
        /// <param name="values">The values to normalize.</param>
        /// <returns>A sorted list of non-empty text values.</returns>
        private static IReadOnlyList<string> NormalizeText(IEnumerable<string> values)
        {
            // Stable output ordering makes test results and future API DTO projection deterministic.
            ArgumentNullException.ThrowIfNull(values);
            return values.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value.Trim()).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>
    /// Represents one matched condition value that can be displayed as evidence before persisted finding evidence exists.
    /// </summary>
    public sealed class RuleMatchedEvidenceReference
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleMatchedEvidenceReference"/> class.
        /// </summary>
        /// <param name="conditionKind">The condition kind that produced the evidence reference.</param>
        /// <param name="reference">The deterministic evidence reference string.</param>
        public RuleMatchedEvidenceReference(string conditionKind, string reference)
        {
            // Evidence references are lightweight strings in this slice; persisted evidence links are added by later finding work.
            ConditionKind = RequireText(conditionKind, nameof(conditionKind));
            Reference = RequireText(reference, nameof(reference));
        }

        /// <summary>
        /// Gets the condition kind that produced the evidence reference.
        /// </summary>
        public string ConditionKind { get; }

        /// <summary>
        /// Gets the deterministic evidence reference string.
        /// </summary>
        public string Reference { get; }

        /// <summary>
        /// Requires a non-empty text value and returns its trimmed form.
        /// </summary>
        /// <param name="value">The candidate value.</param>
        /// <param name="parameterName">The parameter name used for invalid input exceptions.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Evidence references must remain human-readable and deterministic.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }

    /// <summary>
    /// Represents the confidence inputs captured during rule evaluation before finding confidence is calculated.
    /// </summary>
    public sealed class RuleEvaluationConfidenceInputs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleEvaluationConfidenceInputs"/> class.
        /// </summary>
        /// <param name="ruleConfidence">The confidence contributed by the rule definition.</param>
        /// <param name="factConfidence">The confidence contributed by matched graph facts.</param>
        /// <param name="unknownCount">The number of unknown contexts associated with the matched node.</param>
        public RuleEvaluationConfidenceInputs(decimal ruleConfidence, decimal factConfidence, int unknownCount)
        {
            // Confidence inputs are captured separately so later finding creation can evolve the final formula without changing predicate semantics.
            RuleConfidence = ValidateConfidence(ruleConfidence, nameof(ruleConfidence));
            FactConfidence = ValidateConfidence(factConfidence, nameof(factConfidence));
            UnknownCount = unknownCount < 0 ? throw new ArgumentOutOfRangeException(nameof(unknownCount), "Unknown count cannot be negative.") : unknownCount;
        }

        /// <summary>
        /// Gets the confidence contributed by the rule definition.
        /// </summary>
        public decimal RuleConfidence { get; }

        /// <summary>
        /// Gets the confidence contributed by matched graph facts.
        /// </summary>
        public decimal FactConfidence { get; }

        /// <summary>
        /// Gets the number of unknown contexts associated with the matched node.
        /// </summary>
        public int UnknownCount { get; }

        /// <summary>
        /// Validates a normalized confidence value.
        /// </summary>
        /// <param name="value">The candidate confidence value.</param>
        /// <param name="parameterName">The parameter name used for invalid input exceptions.</param>
        /// <returns>The validated confidence value.</returns>
        private static decimal ValidateConfidence(decimal value, string parameterName)
        {
            // The graph domain uses normalized confidence, so evaluator output follows the same range.
            return value < 0m || value > 1m ? throw new ArgumentOutOfRangeException(parameterName, "Confidence must be between 0 and 1.") : value;
        }
    }

    /// <summary>
    /// Represents an evaluator warning for partial evaluation, unavailable facts, or isolated rule-specific failures.
    /// </summary>
    public sealed class RuleEvaluationWarning
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleEvaluationWarning"/> class.
        /// </summary>
        /// <param name="code">The stable warning code.</param>
        /// <param name="ruleCode">The rule code associated with the warning.</param>
        /// <param name="nodeStableKey">The node stable key associated with the warning, when available.</param>
        /// <param name="message">The human-readable warning message.</param>
        public RuleEvaluationWarning(string code, string ruleCode, string? nodeStableKey, string message)
        {
            // Warnings stay separate from matches so partial evaluation remains visible even when a rule still matches through another branch.
            Code = RequireText(code, nameof(code));
            RuleCode = RequireText(ruleCode, nameof(ruleCode));
            NodeStableKey = string.IsNullOrWhiteSpace(nodeStableKey) ? string.Empty : nodeStableKey.Trim();
            Message = RequireText(message, nameof(message));
        }

        /// <summary>
        /// Gets the stable warning code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the rule code associated with the warning.
        /// </summary>
        public string RuleCode { get; }

        /// <summary>
        /// Gets the node stable key associated with the warning, or an empty string for rule-level warnings.
        /// </summary>
        public string NodeStableKey { get; }

        /// <summary>
        /// Gets the human-readable warning message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Requires a non-empty text value and returns its trimmed form.
        /// </summary>
        /// <param name="value">The candidate value.</param>
        /// <param name="parameterName">The parameter name used for invalid input exceptions.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Warnings are developer-facing diagnostics and must be meaningful.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }

    /// <summary>
    /// Represents explicit unknown-state context observed during rule evaluation.
    /// </summary>
    public sealed class RuleEvaluationUnknownState
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleEvaluationUnknownState"/> class.
        /// </summary>
        /// <param name="ruleCode">The rule code associated with the unknown context.</param>
        /// <param name="nodeStableKey">The node stable key associated with the unknown context.</param>
        /// <param name="reason">The reason graph data is unknown or partial.</param>
        public RuleEvaluationUnknownState(string ruleCode, string nodeStableKey, string reason)
        {
            // Unknown context is explicit so later findings can explain uncertainty instead of inventing facts.
            RuleCode = RequireText(ruleCode, nameof(ruleCode));
            NodeStableKey = RequireText(nodeStableKey, nameof(nodeStableKey));
            Reason = RequireText(reason, nameof(reason));
        }

        /// <summary>
        /// Gets the rule code associated with the unknown context.
        /// </summary>
        public string RuleCode { get; }

        /// <summary>
        /// Gets the node stable key associated with the unknown context.
        /// </summary>
        public string NodeStableKey { get; }

        /// <summary>
        /// Gets the reason graph data is unknown or partial.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Requires a non-empty text value and returns its trimmed form.
        /// </summary>
        /// <param name="value">The candidate value.</param>
        /// <param name="parameterName">The parameter name used for invalid input exceptions.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Unknown records without a clear reason do not meet Archon's explicit-unknown contract.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }

    /// <summary>
    /// Provides stable warning codes emitted by the WP012 rule evaluator.
    /// </summary>
    public static class RuleEvaluationWarningCodes
    {
        /// <summary>
        /// Indicates a condition could not inspect its expected graph fact collection for a candidate node.
        /// </summary>
        public const string ConditionFactsUnavailable = "RULE-EVAL-CONDITION-FACTS-UNAVAILABLE";

        /// <summary>
        /// Indicates a rule-specific evaluation failure was isolated and reported without aborting independent rule evaluation.
        /// </summary>
        public const string RuleEvaluationFailed = "RULE-EVAL-RULE-FAILED";
    }
}
