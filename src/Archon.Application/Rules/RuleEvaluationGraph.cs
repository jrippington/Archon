using Archon.Domain.Graph.ControlledValues;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents the application-layer graph-fact read model used by the WP012 rule evaluator.
    /// </summary>
    /// <remarks>
    /// The model is intentionally smaller than the full persisted graph. It captures the normalized fact collections needed by the current boolean DSL so tests and application code can evaluate rules without starting Neo4j, the Aspire AppHost, Roslyn workspaces, or extractor implementations.
    /// </remarks>
    public sealed class RuleEvaluationGraph
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleEvaluationGraph"/> class.
        /// </summary>
        /// <param name="nodes">The candidate graph nodes that rule evaluation can inspect.</param>
        public RuleEvaluationGraph(IEnumerable<RuleEvaluationNode> nodes)
        {
            // Nodes are sorted by stable key once so all downstream evaluation results are deterministic regardless of fixture insertion order.
            ArgumentNullException.ThrowIfNull(nodes);
            Nodes = nodes.OrderBy(static node => node.StableKey, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Gets the candidate graph nodes that rule evaluation can inspect, sorted by stable key.
        /// </summary>
        public IReadOnlyList<RuleEvaluationNode> Nodes { get; }
    }

    /// <summary>
    /// Represents one graph node plus condition-specific facts that can be inspected by data-only WP012 rules.
    /// </summary>
    public sealed class RuleEvaluationNode
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleEvaluationNode"/> class.
        /// </summary>
        /// <param name="stableKey">The deterministic graph stable key for the node.</param>
        /// <param name="nodeKind">The controlled graph node kind used for candidate filtering.</param>
        /// <param name="displayName">The developer-facing node display name.</param>
        /// <param name="targetFrameworks">The target framework monikers associated with the node.</param>
        /// <param name="namespaces">The namespace facts associated with the node.</param>
        /// <param name="symbols">The symbol facts associated with the node.</param>
        /// <param name="packages">The package facts associated with the node.</param>
        /// <param name="filePaths">The repository-relative file path facts associated with the node.</param>
        /// <param name="methodCalls">The method-call facts associated with the node.</param>
        /// <param name="attributes">The attribute usage facts associated with the node.</param>
        /// <param name="metrics">The numeric metric facts associated with the node, keyed by metric name.</param>
        /// <param name="evidenceStableKeys">The evidence stable keys that explain the node or its facts.</param>
        /// <param name="confidence">The confidence assigned to the node's fixture facts.</param>
        /// <param name="unknownReasons">The explicit unknown-state reasons associated with the node.</param>
        public RuleEvaluationNode(
            string stableKey,
            NodeKind nodeKind,
            string displayName,
            IEnumerable<string> targetFrameworks,
            IEnumerable<string> namespaces,
            IEnumerable<string> symbols,
            IEnumerable<string> packages,
            IEnumerable<string> filePaths,
            IEnumerable<string> methodCalls,
            IEnumerable<string> attributes,
            IReadOnlyDictionary<string, decimal> metrics,
            IEnumerable<string> evidenceStableKeys,
            decimal confidence,
            IEnumerable<string> unknownReasons)
        {
            // Constructor normalization keeps the evaluator independent from caller collection mutability and whitespace noise.
            StableKey = RequireText(stableKey, nameof(stableKey));
            NodeKind = nodeKind ?? throw new ArgumentNullException(nameof(nodeKind));
            DisplayName = RequireText(displayName, nameof(displayName));
            TargetFrameworks = NormalizeValues(targetFrameworks);
            Namespaces = NormalizeValues(namespaces);
            Symbols = NormalizeValues(symbols);
            Packages = NormalizeValues(packages);
            FilePaths = NormalizeValues(filePaths);
            MethodCalls = NormalizeValues(methodCalls);
            Attributes = NormalizeValues(attributes);
            Metrics = NormalizeMetrics(metrics);
            EvidenceStableKeys = NormalizeValues(evidenceStableKeys);
            Confidence = confidence < 0m || confidence > 1m ? throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1.") : confidence;
            UnknownReasons = NormalizeValues(unknownReasons);
        }

        /// <summary>
        /// Gets the deterministic graph stable key for the node.
        /// </summary>
        public string StableKey { get; }

        /// <summary>
        /// Gets the controlled graph node kind used for candidate filtering.
        /// </summary>
        public NodeKind NodeKind { get; }

        /// <summary>
        /// Gets the developer-facing node display name.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets the target framework monikers associated with the node.
        /// </summary>
        public IReadOnlyList<string> TargetFrameworks { get; }

        /// <summary>
        /// Gets the namespace facts associated with the node.
        /// </summary>
        public IReadOnlyList<string> Namespaces { get; }

        /// <summary>
        /// Gets the symbol facts associated with the node.
        /// </summary>
        public IReadOnlyList<string> Symbols { get; }

        /// <summary>
        /// Gets the package facts associated with the node.
        /// </summary>
        public IReadOnlyList<string> Packages { get; }

        /// <summary>
        /// Gets the repository-relative file path facts associated with the node.
        /// </summary>
        public IReadOnlyList<string> FilePaths { get; }

        /// <summary>
        /// Gets the method-call facts associated with the node.
        /// </summary>
        public IReadOnlyList<string> MethodCalls { get; }

        /// <summary>
        /// Gets the attribute usage facts associated with the node.
        /// </summary>
        public IReadOnlyList<string> Attributes { get; }

        /// <summary>
        /// Gets the numeric metric facts associated with the node, keyed by metric name.
        /// </summary>
        public IReadOnlyDictionary<string, decimal> Metrics { get; }

        /// <summary>
        /// Gets the evidence stable keys that explain the node or its facts.
        /// </summary>
        public IReadOnlyList<string> EvidenceStableKeys { get; }

        /// <summary>
        /// Gets the confidence assigned to the node's fixture facts.
        /// </summary>
        public decimal Confidence { get; }

        /// <summary>
        /// Gets the explicit unknown-state reasons associated with the node.
        /// </summary>
        public IReadOnlyList<string> UnknownReasons { get; }

        /// <summary>
        /// Requires a non-empty text value and returns its trimmed form.
        /// </summary>
        /// <param name="value">The candidate value.</param>
        /// <param name="parameterName">The parameter name used for invalid input exceptions.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Stable keys and display names must be meaningful because evaluator diagnostics and results expose them directly.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }

        /// <summary>
        /// Normalizes a text sequence into a deterministic immutable list.
        /// </summary>
        /// <param name="values">The source values to normalize.</param>
        /// <returns>A sorted list of non-empty trimmed values.</returns>
        private static IReadOnlyList<string> NormalizeValues(IEnumerable<string> values)
        {
            // Ordinal ordering keeps evidence and condition comparisons stable across platforms and cultures.
            ArgumentNullException.ThrowIfNull(values);
            return values.Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Normalizes metric facts into a deterministic dictionary keyed by ordinal metric name.
        /// </summary>
        /// <param name="metrics">The source metrics to normalize.</param>
        /// <returns>A read-only dictionary of metric names and numeric values.</returns>
        private static IReadOnlyDictionary<string, decimal> NormalizeMetrics(IReadOnlyDictionary<string, decimal> metrics)
        {
            // Metrics use explicit names from extraction or fixtures; blank names would make threshold conditions ambiguous.
            ArgumentNullException.ThrowIfNull(metrics);
            SortedDictionary<string, decimal> normalizedMetrics = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, decimal> metric in metrics)
            {
                string metricName = RequireText(metric.Key, nameof(metrics));
                normalizedMetrics[metricName] = metric.Value;
            }

            return normalizedMetrics;
        }
    }
}
