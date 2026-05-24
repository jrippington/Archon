using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Evaluates enabled WP012 rule catalog entries against deterministic application-layer graph facts.
    /// </summary>
    public sealed class RuleEvaluator
    {
        /// <summary>
        /// Defines a conservative maximum wildcard pattern length for bounded pattern matching.
        /// </summary>
        private const int MaximumPatternLength = 256;

        /// <summary>
        /// Defines a conservative maximum nested detection depth to guard accidental runaway authored group recursion.
        /// </summary>
        private const int MaximumGroupDepth = 32;

        /// <summary>
        /// Evaluates enabled rules against the supplied graph facts.
        /// </summary>
        /// <param name="rules">The validated rule catalog entries to evaluate.</param>
        /// <param name="graph">The graph-fact read model to inspect.</param>
        /// <param name="cancellationToken">The cancellation token that can stop evaluation between rules and candidate nodes.</param>
        /// <returns>The deterministic rule evaluation result.</returns>
        public Task<RuleEvaluationResult> EvaluateAsync(IEnumerable<RuleCatalogEntry> rules, RuleEvaluationGraph graph, CancellationToken cancellationToken)
        {
            // Evaluation is CPU-only and data-only in this slice, so the async contract completes synchronously while preserving a future-friendly application seam.
            ArgumentNullException.ThrowIfNull(rules);
            ArgumentNullException.ThrowIfNull(graph);

            List<RuleEvaluationMatch> matches = [];
            List<RuleEvaluationWarning> warnings = [];
            List<RuleEvaluationUnknownState> unknownStates = [];
            foreach (RuleCatalogEntry rule in rules.Where(static rule => rule.AvailableForEvaluation).OrderBy(static rule => rule.RuleCode, StringComparer.Ordinal).ThenBy(static rule => rule.Version, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EvaluateRule(rule, graph, matches, warnings, unknownStates, cancellationToken);
            }

            return Task.FromResult(new RuleEvaluationResult(matches, warnings, unknownStates));
        }

        /// <summary>
        /// Evaluates one enabled rule against all candidate nodes in stable order.
        /// </summary>
        /// <param name="rule">The enabled catalog rule to evaluate.</param>
        /// <param name="graph">The graph-fact read model to inspect.</param>
        /// <param name="matches">The match collection receiving satisfied predicates.</param>
        /// <param name="warnings">The warning collection receiving partial-evaluation diagnostics.</param>
        /// <param name="unknownStates">The unknown-state collection receiving explicit unknown context.</param>
        /// <param name="cancellationToken">The cancellation token that can stop evaluation between candidates.</param>
        private static void EvaluateRule(
            RuleCatalogEntry rule,
            RuleEvaluationGraph graph,
            List<RuleEvaluationMatch> matches,
            List<RuleEvaluationWarning> warnings,
            List<RuleEvaluationUnknownState> unknownStates,
            CancellationToken cancellationToken)
        {
            // Rule-level failures are isolated to the rule being evaluated so independent rules can still produce results.
            try
            {
                foreach (RuleEvaluationNode node in SelectCandidateNodes(rule.Detection, graph.Nodes))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    GroupEvaluationState state = new();
                    bool isMatch = EvaluateGroup(rule, rule.Detection, node, state, warnings, depth: 0);
                    if (!isMatch)
                    {
                        continue;
                    }

                    foreach (string unknownReason in node.UnknownReasons)
                    {
                        state.UnknownReasons.Add(unknownReason);
                        unknownStates.Add(new RuleEvaluationUnknownState(rule.RuleCode, node.StableKey, unknownReason));
                    }

                    RuleEvaluationConfidenceInputs confidenceInputs = new(1.0m, node.Confidence, state.UnknownReasons.Count);
                    matches.Add(new RuleEvaluationMatch(rule, node.StableKey, [node.StableKey], state.EvidenceReferences, node.EvidenceStableKeys, confidenceInputs));
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or JsonException or RegexParseException or ArgumentException)
            {
                warnings.Add(new RuleEvaluationWarning(
                    RuleEvaluationWarningCodes.RuleEvaluationFailed,
                    rule.RuleCode,
                    null,
                    $"Rule '{rule.RuleCode}' version '{rule.Version}' could not be evaluated: {exception.Message}"));
            }
        }

        /// <summary>
        /// Selects candidate nodes for a detection group by applying the root node-kind restriction before any condition evaluation.
        /// </summary>
        /// <param name="group">The root detection group.</param>
        /// <param name="nodes">The available graph nodes.</param>
        /// <returns>The nodes that are eligible for condition evaluation.</returns>
        private static IEnumerable<RuleEvaluationNode> SelectCandidateNodes(RuleDetectionGroup group, IReadOnlyList<RuleEvaluationNode> nodes)
        {
            // Candidate restriction is applied before condition work so rules cannot inspect unrelated node kinds accidentally.
            if (group.NodeKinds.Count == 0)
            {
                return nodes;
            }

            HashSet<string> allowedKinds = group.NodeKinds.Select(static nodeKind => nodeKind.Value).ToHashSet(StringComparer.Ordinal);
            return nodes.Where(node => allowedKinds.Contains(node.NodeKind.Value));
        }

        /// <summary>
        /// Evaluates a detection group recursively for one candidate node.
        /// </summary>
        /// <param name="rule">The rule that owns the group.</param>
        /// <param name="group">The detection group to evaluate.</param>
        /// <param name="node">The candidate node being inspected.</param>
        /// <param name="state">The mutable per-node evaluation state receiving evidence and unknown context.</param>
        /// <param name="warnings">The warning collection receiving partial-evaluation diagnostics.</param>
        /// <param name="depth">The current nested group depth.</param>
        /// <returns><see langword="true"/> when the group predicate is satisfied; otherwise, <see langword="false"/>.</returns>
        private static bool EvaluateGroup(RuleCatalogEntry rule, RuleDetectionGroup group, RuleEvaluationNode node, GroupEvaluationState state, List<RuleEvaluationWarning> warnings, int depth)
        {
            // The evaluator treats conditions and nested groups as operands of the same match mode, as required by the DSL contract.
            if (depth > MaximumGroupDepth)
            {
                throw new InvalidOperationException($"Detection group nesting exceeded the supported depth of {MaximumGroupDepth}.");
            }

            if (group.NodeKinds.Count > 0 && !group.NodeKinds.Any(nodeKind => StringComparer.Ordinal.Equals(nodeKind.Value, node.NodeKind.Value)))
            {
                return false;
            }

            List<bool> operands = [];
            foreach (RuleCondition condition in group.Conditions)
            {
                ConditionEvaluationResult conditionResult = EvaluateCondition(rule, condition, node, warnings);
                operands.Add(conditionResult.IsMatch);
                if (conditionResult.IsMatch)
                {
                    state.EvidenceReferences.AddRange(conditionResult.EvidenceReferences);
                }
            }

            foreach (RuleDetectionGroup childGroup in group.Groups)
            {
                operands.Add(EvaluateGroup(rule, childGroup, node, state, warnings, depth + 1));
            }

            if (group.Match == RuleDetectionMatch.MatchAll)
            {
                return operands.All(static operand => operand);
            }

            if (group.Match == RuleDetectionMatch.MatchAny)
            {
                return operands.Any(static operand => operand);
            }

            if (group.Match == RuleDetectionMatch.MatchNone)
            {
                return operands.All(static operand => !operand);
            }

            throw new InvalidOperationException($"Unsupported detection match value '{group.Match.Value}'.");
        }

        /// <summary>
        /// Evaluates one condition against the fact collection selected by its kind.
        /// </summary>
        /// <param name="rule">The rule that owns the condition.</param>
        /// <param name="condition">The condition to evaluate.</param>
        /// <param name="node">The candidate node being inspected.</param>
        /// <param name="warnings">The warning collection receiving partial-evaluation diagnostics.</param>
        /// <returns>The condition evaluation result.</returns>
        private static ConditionEvaluationResult EvaluateCondition(RuleCatalogEntry rule, RuleCondition condition, RuleEvaluationNode node, List<RuleEvaluationWarning> warnings)
        {
            // Kind dispatch is explicit so rule JSON cannot select arbitrary code paths or invoke application behavior.
            if (condition.Kind == RuleConditionKind.MetricThreshold)
            {
                return EvaluateMetricCondition(rule, condition, node, warnings);
            }

            IReadOnlyList<string> facts = GetStringFacts(condition.Kind, node);
            if (facts.Count == 0)
            {
                AddUnavailableFactsWarning(rule, condition, node, warnings);
                return ConditionEvaluationResult.NoMatch;
            }

            List<RuleMatchedEvidenceReference> evidenceReferences = [];
            IReadOnlyList<string> expectedValues = ReadExpectedValues(condition.Payload, condition.Operator);
            foreach (string fact in facts)
            {
                if (EvaluateStringOperator(fact, expectedValues, condition.Operator))
                {
                    evidenceReferences.Add(new RuleMatchedEvidenceReference(condition.Kind.Value, $"{condition.Kind.Value}:{fact}"));
                }
            }

            return evidenceReferences.Count == 0 ? ConditionEvaluationResult.NoMatch : new ConditionEvaluationResult(true, evidenceReferences);
        }

        /// <summary>
        /// Evaluates a metric-threshold condition against a node metric value.
        /// </summary>
        /// <param name="rule">The rule that owns the condition.</param>
        /// <param name="condition">The metric condition to evaluate.</param>
        /// <param name="node">The candidate node being inspected.</param>
        /// <param name="warnings">The warning collection receiving partial-evaluation diagnostics.</param>
        /// <returns>The condition evaluation result.</returns>
        private static ConditionEvaluationResult EvaluateMetricCondition(RuleCatalogEntry rule, RuleCondition condition, RuleEvaluationNode node, List<RuleEvaluationWarning> warnings)
        {
            // Metric thresholds compare decimal values only; string pattern operators cannot reach this path because validation rejects them.
            string metricName = ReadRequiredPayloadString(condition.Payload, "metric");
            decimal threshold = ReadRequiredPayloadDecimal(condition.Payload, "value");
            if (!node.Metrics.TryGetValue(metricName, out decimal actualValue))
            {
                AddUnavailableFactsWarning(rule, condition, node, warnings, metricName);
                return ConditionEvaluationResult.NoMatch;
            }

            bool isMatch = EvaluateNumericOperator(actualValue, threshold, condition.Operator);
            return isMatch
                ? new ConditionEvaluationResult(true, [new RuleMatchedEvidenceReference(condition.Kind.Value, $"metric:{metricName}={actualValue.ToString(CultureInfo.InvariantCulture)}")])
                : ConditionEvaluationResult.NoMatch;
        }

        /// <summary>
        /// Selects the string fact collection for a condition kind.
        /// </summary>
        /// <param name="kind">The condition kind to map.</param>
        /// <param name="node">The candidate node that owns fact collections.</param>
        /// <returns>The fact collection for the condition kind.</returns>
        private static IReadOnlyList<string> GetStringFacts(RuleConditionKind kind, RuleEvaluationNode node)
        {
            // The fixture read model exposes one collection per required WP012 condition kind.
            if (kind == RuleConditionKind.TargetFrameworkMembership)
            {
                return node.TargetFrameworks;
            }

            if (kind == RuleConditionKind.Namespace)
            {
                return node.Namespaces;
            }

            if (kind == RuleConditionKind.Symbol)
            {
                return node.Symbols;
            }

            if (kind == RuleConditionKind.Package)
            {
                return node.Packages;
            }

            if (kind == RuleConditionKind.FilePattern)
            {
                return node.FilePaths;
            }

            if (kind == RuleConditionKind.MethodCall)
            {
                return node.MethodCalls;
            }

            if (kind == RuleConditionKind.Attribute)
            {
                return node.Attributes;
            }

            throw new InvalidOperationException($"Unsupported condition kind '{kind.Value}'.");
        }

        /// <summary>
        /// Evaluates a string operator using ordinal comparison semantics.
        /// </summary>
        /// <param name="actualValue">The graph fact value.</param>
        /// <param name="expectedValues">The rule-authored expected value or value set.</param>
        /// <param name="conditionOperator">The condition operator to apply.</param>
        /// <returns><see langword="true"/> when the operator is satisfied; otherwise, <see langword="false"/>.</returns>
        private static bool EvaluateStringOperator(string actualValue, IReadOnlyList<string> expectedValues, RuleConditionOperator conditionOperator)
        {
            // String comparison is intentionally ordinal and case-sensitive to avoid culture drift and hidden normalization surprises.
            if (conditionOperator == RuleConditionOperator.Equal)
            {
                return expectedValues.Any(expected => StringComparer.Ordinal.Equals(actualValue, expected));
            }

            if (conditionOperator == RuleConditionOperator.NotEqual)
            {
                return expectedValues.All(expected => !StringComparer.Ordinal.Equals(actualValue, expected));
            }

            if (conditionOperator == RuleConditionOperator.In)
            {
                return expectedValues.Contains(actualValue, StringComparer.Ordinal);
            }

            if (conditionOperator == RuleConditionOperator.NotIn)
            {
                return !expectedValues.Contains(actualValue, StringComparer.Ordinal);
            }

            if (conditionOperator == RuleConditionOperator.Contains)
            {
                return expectedValues.Any(expected => actualValue.Contains(expected, StringComparison.Ordinal));
            }

            if (conditionOperator == RuleConditionOperator.StartsWith)
            {
                return expectedValues.Any(expected => actualValue.StartsWith(expected, StringComparison.Ordinal));
            }

            if (conditionOperator == RuleConditionOperator.EndsWith)
            {
                return expectedValues.Any(expected => actualValue.EndsWith(expected, StringComparison.Ordinal));
            }

            if (conditionOperator == RuleConditionOperator.MatchesPattern)
            {
                return expectedValues.Any(expected => IsWildcardPatternMatch(actualValue, expected));
            }

            throw new InvalidOperationException($"Operator '{conditionOperator.Value}' is not supported for string facts.");
        }

        /// <summary>
        /// Evaluates a numeric operator against decimal metric values.
        /// </summary>
        /// <param name="actualValue">The actual graph metric value.</param>
        /// <param name="expectedValue">The rule-authored threshold value.</param>
        /// <param name="conditionOperator">The numeric condition operator.</param>
        /// <returns><see langword="true"/> when the numeric comparison is satisfied; otherwise, <see langword="false"/>.</returns>
        private static bool EvaluateNumericOperator(decimal actualValue, decimal expectedValue, RuleConditionOperator conditionOperator)
        {
            // Decimal comparison keeps metric thresholds deterministic and avoids binary floating point surprises.
            if (conditionOperator == RuleConditionOperator.GreaterThan)
            {
                return actualValue > expectedValue;
            }

            if (conditionOperator == RuleConditionOperator.GreaterThanOrEqual)
            {
                return actualValue >= expectedValue;
            }

            if (conditionOperator == RuleConditionOperator.LessThan)
            {
                return actualValue < expectedValue;
            }

            if (conditionOperator == RuleConditionOperator.LessThanOrEqual)
            {
                return actualValue <= expectedValue;
            }

            throw new InvalidOperationException($"Operator '{conditionOperator.Value}' is not supported for metric thresholds.");
        }

        /// <summary>
        /// Evaluates a bounded wildcard pattern against an actual string value.
        /// </summary>
        /// <param name="actualValue">The graph fact value to inspect.</param>
        /// <param name="pattern">The rule-authored wildcard pattern.</param>
        /// <returns><see langword="true"/> when the pattern matches; otherwise, <see langword="false"/>.</returns>
        private static bool IsWildcardPatternMatch(string actualValue, string pattern)
        {
            // MatchesPattern is intentionally wildcard based rather than raw regular expression execution, preventing authored rules from supplying expensive arbitrary regexes.
            if (pattern.Length > MaximumPatternLength)
            {
                throw new InvalidOperationException($"Pattern length cannot exceed {MaximumPatternLength} characters.");
            }

            string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(actualValue, regexPattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }

        /// <summary>
        /// Reads expected string values from a condition payload.
        /// </summary>
        /// <param name="payload">The cloned condition payload.</param>
        /// <param name="conditionOperator">The operator determining whether value or values is preferred.</param>
        /// <returns>The expected string values.</returns>
        private static IReadOnlyList<string> ReadExpectedValues(JsonElement payload, RuleConditionOperator conditionOperator)
        {
            // In and NotIn use values arrays; other string operators accept value and tolerate values for shared validation compatibility.
            if ((conditionOperator == RuleConditionOperator.In || conditionOperator == RuleConditionOperator.NotIn) && payload.TryGetProperty("values", out JsonElement valuesElement))
            {
                return ReadStringArray(valuesElement);
            }

            if (payload.TryGetProperty("value", out JsonElement valueElement) && valueElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(valueElement.GetString()))
            {
                return [valueElement.GetString()!.Trim()];
            }

            if (payload.TryGetProperty("values", out JsonElement fallbackValuesElement))
            {
                return ReadStringArray(fallbackValuesElement);
            }

            throw new InvalidOperationException("Condition payload did not contain an evaluable value or values property.");
        }

        /// <summary>
        /// Reads string values from a JSON array payload.
        /// </summary>
        /// <param name="valuesElement">The JSON array to read.</param>
        /// <returns>The deterministic non-empty string values.</returns>
        private static IReadOnlyList<string> ReadStringArray(JsonElement valuesElement)
        {
            // Validation has already checked the payload shape; this reader still filters blanks defensively for evaluator robustness.
            if (valuesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Expected a JSON array of string values.");
            }

            return valuesElement.EnumerateArray()
                .Where(static element => element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString()))
                .Select(static element => element.GetString()!.Trim())
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Reads a required string property from a condition payload.
        /// </summary>
        /// <param name="payload">The condition payload to inspect.</param>
        /// <param name="propertyName">The property name to read.</param>
        /// <returns>The trimmed string value.</returns>
        private static string ReadRequiredPayloadString(JsonElement payload, string propertyName)
        {
            // Required payload fields were validated during loading, but evaluator failures are still isolated and surfaced as warnings.
            if (!payload.TryGetProperty(propertyName, out JsonElement valueElement) || valueElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(valueElement.GetString()))
            {
                throw new InvalidOperationException($"Condition payload is missing required string property '{propertyName}'.");
            }

            return valueElement.GetString()!.Trim();
        }

        /// <summary>
        /// Reads a required decimal property from a condition payload.
        /// </summary>
        /// <param name="payload">The condition payload to inspect.</param>
        /// <param name="propertyName">The property name to read.</param>
        /// <returns>The decimal value.</returns>
        private static decimal ReadRequiredPayloadDecimal(JsonElement payload, string propertyName)
        {
            // Decimal parsing follows System.Text.Json numeric handling and uses invariant conversion for deterministic thresholds.
            if (!payload.TryGetProperty(propertyName, out JsonElement valueElement) || valueElement.ValueKind != JsonValueKind.Number || !valueElement.TryGetDecimal(out decimal value))
            {
                throw new InvalidOperationException($"Condition payload is missing required numeric property '{propertyName}'.");
            }

            return value;
        }

        /// <summary>
        /// Adds a deterministic warning when expected graph facts are unavailable for a condition.
        /// </summary>
        /// <param name="rule">The rule being evaluated.</param>
        /// <param name="condition">The condition whose facts are unavailable.</param>
        /// <param name="node">The node being inspected.</param>
        /// <param name="warnings">The warning collection receiving the diagnostic.</param>
        /// <param name="detail">Optional detail identifying the missing metric or fact subset.</param>
        private static void AddUnavailableFactsWarning(RuleCatalogEntry rule, RuleCondition condition, RuleEvaluationNode node, List<RuleEvaluationWarning> warnings, string? detail = null)
        {
            // Missing fact collections are warnings rather than matches because the evaluator must not invent facts to satisfy a predicate.
            string detailSuffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" '{detail}'";
            warnings.Add(new RuleEvaluationWarning(
                RuleEvaluationWarningCodes.ConditionFactsUnavailable,
                rule.RuleCode,
                node.StableKey,
                $"Condition '{condition.Kind.Value}' could not inspect expected graph facts{detailSuffix} for node '{node.StableKey}'."));
        }

        /// <summary>
        /// Tracks mutable per-node evaluation state while recursive group evaluation executes.
        /// </summary>
        private sealed class GroupEvaluationState
        {
            /// <summary>
            /// Gets the matched condition evidence references accumulated for a candidate node.
            /// </summary>
            public List<RuleMatchedEvidenceReference> EvidenceReferences { get; } = [];

            /// <summary>
            /// Gets the unknown reasons accumulated for a candidate node.
            /// </summary>
            public List<string> UnknownReasons { get; } = [];
        }

        /// <summary>
        /// Represents the result of evaluating a single condition operand.
        /// </summary>
        private sealed class ConditionEvaluationResult
        {
            /// <summary>
            /// Represents a reusable non-match result without evidence.
            /// </summary>
            public static readonly ConditionEvaluationResult NoMatch = new(false, []);

            /// <summary>
            /// Initializes a new instance of the <see cref="ConditionEvaluationResult"/> class.
            /// </summary>
            /// <param name="isMatch">Indicates whether the condition matched.</param>
            /// <param name="evidenceReferences">The condition evidence references for matched values.</param>
            public ConditionEvaluationResult(bool isMatch, IEnumerable<RuleMatchedEvidenceReference> evidenceReferences)
            {
                // Condition results remain small immutable snapshots so recursive group evaluation can compose them safely.
                IsMatch = isMatch;
                EvidenceReferences = (evidenceReferences ?? throw new ArgumentNullException(nameof(evidenceReferences))).ToArray();
            }

            /// <summary>
            /// Gets a value indicating whether the condition matched.
            /// </summary>
            public bool IsMatch { get; }

            /// <summary>
            /// Gets the condition evidence references for matched values.
            /// </summary>
            public IReadOnlyList<RuleMatchedEvidenceReference> EvidenceReferences { get; }
        }
    }
}
