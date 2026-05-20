using Archon.Application.Extraction.Contracts;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Extraction.Accumulation
{
    /// <summary>
    /// Accumulates graph fact contributions from one or more future extractor slices into a deterministic in-memory snapshot.
    /// </summary>
    /// <remarks>
    /// The accumulator performs no I/O, persistence, Roslyn workspace loading, host wiring, or Neo4j interaction. It is an application-layer collection and merge component that prepares an `ExtractedArchitectureSnapshot` from domain graph contracts.
    /// </remarks>
    public sealed class ArchitectureSnapshotAccumulator
    {
        /// <summary>
        /// Stores repository facts by stable key using ordinal string comparison for deterministic duplicate replacement.
        /// </summary>
        private readonly Dictionary<string, RepositoryModel> _repositories = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores solution facts by stable key using ordinal string comparison for deterministic duplicate replacement.
        /// </summary>
        private readonly Dictionary<string, SolutionModel> _solutions = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores architecture node facts by stable key using ordinal string comparison for deterministic duplicate replacement.
        /// </summary>
        private readonly Dictionary<string, ArchitectureNode> _nodes = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores architecture edge facts by stable key using ordinal string comparison for deterministic duplicate replacement.
        /// </summary>
        private readonly Dictionary<string, ArchitectureEdge> _edges = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores evidence records by stable key using ordinal string comparison for deterministic duplicate replacement.
        /// </summary>
        private readonly Dictionary<string, EvidenceRecord> _evidence = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores rule definitions by rule code and version using ordinal string comparison for deterministic duplicate replacement.
        /// </summary>
        private readonly Dictionary<string, RuleDefinition> _rules = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores finding records by stable key using ordinal string comparison for deterministic duplicate replacement.
        /// </summary>
        private readonly Dictionary<string, FindingRecord> _findings = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores metric records by stable key using ordinal string comparison for deterministic duplicate replacement.
        /// </summary>
        private readonly Dictionary<string, MetricRecord> _metrics = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores generated summaries by stable key using ordinal string comparison for deterministic duplicate replacement.
        /// </summary>
        private readonly Dictionary<string, GeneratedSummary> _generatedSummaries = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores warning diagnostics as an insertion-ordered stream.
        /// </summary>
        private readonly List<string> _warnings = [];

        /// <summary>
        /// Stores error diagnostics as an insertion-ordered stream.
        /// </summary>
        private readonly List<string> _errors = [];

        /// <summary>
        /// Stores the currently selected snapshot header for the final assembled snapshot.
        /// </summary>
        private SnapshotHeader? _snapshotHeader;

        /// <summary>
        /// Sets or replaces the snapshot header that scopes the final assembled snapshot.
        /// </summary>
        /// <param name="snapshotHeader">The snapshot header to use for the assembled snapshot.</param>
        /// <returns>The current accumulator for fluent contribution flows.</returns>
        public ArchitectureSnapshotAccumulator SetSnapshotHeader(SnapshotHeader snapshotHeader)
        {
            // Snapshot header is singular; the latest explicit header replaces any earlier header deterministically.
            ArgumentNullException.ThrowIfNull(snapshotHeader);
            _snapshotHeader = snapshotHeader;
            return this;
        }

        /// <summary>
        /// Adds or replaces a repository fact by stable key.
        /// </summary>
        /// <param name="repository">The repository fact to accumulate.</param>
        /// <returns>The current accumulator for fluent contribution flows.</returns>
        public ArchitectureSnapshotAccumulator AddRepository(RepositoryModel repository)
        {
            // Stable-keyed sections use latest-wins replacement so future extractors can refine an existing fact.
            ArgumentNullException.ThrowIfNull(repository);
            AddByStableKey(_repositories, repository.StableKey, repository);
            return this;
        }

        /// <summary>
        /// Adds or replaces a solution fact by stable key.
        /// </summary>
        /// <param name="solution">The solution fact to accumulate.</param>
        /// <returns>The current accumulator for fluent contribution flows.</returns>
        public ArchitectureSnapshotAccumulator AddSolution(SolutionModel solution)
        {
            // Stable-key replacement keeps the final solution collection unambiguous.
            ArgumentNullException.ThrowIfNull(solution);
            AddByStableKey(_solutions, solution.StableKey, solution);
            return this;
        }

        /// <summary>
        /// Adds or replaces an architecture node fact by stable key.
        /// </summary>
        /// <param name="node">The architecture node fact to accumulate.</param>
        /// <returns>The current accumulator for fluent contribution flows.</returns>
        public ArchitectureSnapshotAccumulator AddNode(ArchitectureNode node)
        {
            // Nodes are stable-keyed architecture concepts and therefore de-duplicate by stable identity.
            ArgumentNullException.ThrowIfNull(node);
            AddByStableKey(_nodes, node.StableKey, node);
            return this;
        }

        /// <summary>
        /// Adds or replaces an architecture edge fact by stable key.
        /// </summary>
        /// <param name="edge">The architecture edge fact to accumulate.</param>
        /// <returns>The current accumulator for fluent contribution flows.</returns>
        public ArchitectureSnapshotAccumulator AddEdge(ArchitectureEdge edge)
        {
            // Edges are stable-keyed relationships and therefore de-duplicate by stable identity.
            ArgumentNullException.ThrowIfNull(edge);
            AddByStableKey(_edges, edge.StableKey, edge);
            return this;
        }

        /// <summary>
        /// Adds or replaces an evidence record by stable key.
        /// </summary>
        /// <param name="evidence">The evidence record to accumulate.</param>
        /// <returns>The current accumulator for fluent contribution flows.</returns>
        public ArchitectureSnapshotAccumulator AddEvidence(EvidenceRecord evidence)
        {
            // Evidence is stable-keyed because the same source explanation can support multiple facts.
            ArgumentNullException.ThrowIfNull(evidence);
            AddByStableKey(_evidence, evidence.StableKey, evidence);
            return this;
        }

        /// <summary>
        /// Adds or replaces a rule definition by rule code and version.
        /// </summary>
        /// <param name="rule">The rule definition to accumulate.</param>
        /// <returns>The current accumulator for fluent contribution flows.</returns>
        public ArchitectureSnapshotAccumulator AddRule(RuleDefinition rule)
        {
            // Rule definitions are global catalog entries, so code plus version is the deterministic replacement identity.
            ArgumentNullException.ThrowIfNull(rule);
            _rules[BuildRuleVersionKey(rule.RuleCode, rule.Version)] = rule;
            return this;
        }

        /// <summary>
        /// Adds or replaces a finding record by stable key.
        /// </summary>
        /// <param name="finding">The finding record to accumulate.</param>
        /// <returns>The current accumulator for fluent contribution flows.</returns>
        public ArchitectureSnapshotAccumulator AddFinding(FindingRecord finding)
        {
            // Findings are stable-keyed by snapshot, rule, and target identity.
            ArgumentNullException.ThrowIfNull(finding);
            AddByStableKey(_findings, finding.StableKey, finding);
            return this;
        }

        /// <summary>
        /// Adds or replaces a metric record by stable key.
        /// </summary>
        /// <param name="metric">The metric record to accumulate.</param>
        /// <returns>The current accumulator for fluent contribution flows.</returns>
        public ArchitectureSnapshotAccumulator AddMetric(MetricRecord metric)
        {
            // Metrics are stable-keyed by snapshot, metric name, and scope identity.
            ArgumentNullException.ThrowIfNull(metric);
            AddByStableKey(_metrics, metric.StableKey, metric);
            return this;
        }

        /// <summary>
        /// Adds or replaces a generated summary by stable key.
        /// </summary>
        /// <param name="generatedSummary">The generated summary to accumulate.</param>
        /// <returns>The current accumulator for fluent contribution flows.</returns>
        public ArchitectureSnapshotAccumulator AddGeneratedSummary(GeneratedSummary generatedSummary)
        {
            // Generated summaries are stable-keyed by snapshot, summary kind, and target identity.
            ArgumentNullException.ThrowIfNull(generatedSummary);
            AddByStableKey(_generatedSummaries, generatedSummary.StableKey, generatedSummary);
            return this;
        }

        /// <summary>
        /// Adds a warning diagnostic to the insertion-ordered diagnostic stream.
        /// </summary>
        /// <param name="warning">The warning diagnostic text to preserve.</param>
        /// <returns>The current accumulator for fluent contribution flows.</returns>
        public ArchitectureSnapshotAccumulator AddWarning(string? warning)
        {
            // Diagnostics are not stable-keyed facts; repeated messages can carry useful extractor context and are preserved.
            AddDiagnostic(_warnings, warning);
            return this;
        }

        /// <summary>
        /// Adds an error diagnostic to the insertion-ordered diagnostic stream.
        /// </summary>
        /// <param name="error">The error diagnostic text to preserve.</param>
        /// <returns>The current accumulator for fluent contribution flows.</returns>
        public ArchitectureSnapshotAccumulator AddError(string? error)
        {
            // Errors mirror warnings but are kept in a separate stream so callers can decide how to report severity.
            AddDiagnostic(_errors, error);
            return this;
        }

        /// <summary>
        /// Merges an existing extracted architecture snapshot into this accumulator.
        /// </summary>
        /// <param name="snapshot">The existing snapshot whose sections should be accumulated.</param>
        /// <returns>The current accumulator for fluent contribution flows.</returns>
        public ArchitectureSnapshotAccumulator Merge(ExtractedArchitectureSnapshot snapshot)
        {
            // Merge reuses the same add paths so duplicate handling and diagnostic normalization remain consistent.
            ArgumentNullException.ThrowIfNull(snapshot);

            if (snapshot.SnapshotHeader is not null)
            {
                SetSnapshotHeader(snapshot.SnapshotHeader);
            }

            foreach (RepositoryModel repository in snapshot.Repositories)
            {
                AddRepository(repository);
            }

            foreach (SolutionModel solution in snapshot.Solutions)
            {
                AddSolution(solution);
            }

            foreach (ArchitectureNode node in snapshot.Nodes)
            {
                AddNode(node);
            }

            foreach (ArchitectureEdge edge in snapshot.Edges)
            {
                AddEdge(edge);
            }

            foreach (EvidenceRecord evidence in snapshot.Evidence)
            {
                AddEvidence(evidence);
            }

            foreach (RuleDefinition rule in snapshot.Rules)
            {
                AddRule(rule);
            }

            foreach (FindingRecord finding in snapshot.Findings)
            {
                AddFinding(finding);
            }

            foreach (MetricRecord metric in snapshot.Metrics)
            {
                AddMetric(metric);
            }

            foreach (GeneratedSummary generatedSummary in snapshot.GeneratedSummaries)
            {
                AddGeneratedSummary(generatedSummary);
            }

            foreach (string warning in snapshot.Warnings)
            {
                AddWarning(warning);
            }

            foreach (string error in snapshot.Errors)
            {
                AddError(error);
            }

            return this;
        }

        /// <summary>
        /// Creates an immutable extracted architecture snapshot from the accumulated facts and diagnostics.
        /// </summary>
        /// <returns>An immutable snapshot with stable-keyed sections ordered by stable key.</returns>
        public ExtractedArchitectureSnapshot ToSnapshot()
        {
            // Sorting by stable key makes output deterministic regardless of extractor contribution order.
            return new ExtractedArchitectureSnapshot(
                _snapshotHeader,
                OrderByStableKey(_repositories),
                OrderByStableKey(_solutions),
                OrderByStableKey(_nodes),
                OrderByStableKey(_edges),
                OrderByStableKey(_evidence),
                OrderByRuleIdentity(_rules),
                OrderByStableKey(_findings),
                OrderByStableKey(_metrics),
                OrderByStableKey(_generatedSummaries),
                _warnings,
                _errors);
        }

        /// <summary>
        /// Adds or replaces a stable-keyed item in a dictionary.
        /// </summary>
        /// <typeparam name="TItem">The graph fact item type.</typeparam>
        /// <param name="items">The dictionary that stores the graph fact items.</param>
        /// <param name="stableKey">The stable key associated with the item.</param>
        /// <param name="item">The graph fact item to store.</param>
        private static void AddByStableKey<TItem>(Dictionary<string, TItem> items, StableKey stableKey, TItem item)
        {
            // The dictionary indexer implements the documented latest-wins duplicate policy deterministically.
            items[RequireStableKeyValue(stableKey)] = item;
        }

        /// <summary>
        /// Builds a deterministic in-memory identity for a versioned rule definition.
        /// </summary>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="ruleVersion">The stable rule version.</param>
        /// <returns>A composite identity used only by the accumulator.</returns>
        private static string BuildRuleVersionKey(string ruleCode, string ruleVersion)
        {
            // The separator is private to the accumulator; persistence still stores rule code and version as separate fields.
            return string.Concat(ruleCode, "\u001F", ruleVersion);
        }

        /// <summary>
        /// Orders stable-keyed dictionary values by their stable-key string.
        /// </summary>
        /// <typeparam name="TItem">The graph fact item type.</typeparam>
        /// <param name="items">The dictionary that stores keyed graph fact items.</param>
        /// <returns>The graph fact values ordered by ordinal stable-key string.</returns>
        private static IReadOnlyList<TItem> OrderByStableKey<TItem>(Dictionary<string, TItem> items)
        {
            // Ordinal ordering avoids culture-specific output changes between developer machines and CI agents.
            return items.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray();
        }

        /// <summary>
        /// Orders accumulated rule definitions by their rule code and version identity.
        /// </summary>
        /// <param name="items">The accumulated rule dictionary to order.</param>
        /// <returns>A deterministic read-only rule definition list.</returns>
        private static IReadOnlyList<RuleDefinition> OrderByRuleIdentity(Dictionary<string, RuleDefinition> items)
        {
            // Rule definitions do not carry StableKey values, so the accumulator orders them by its composite code/version dictionary key.
            return items.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray();
        }

        /// <summary>
        /// Adds a non-empty trimmed diagnostic message to a diagnostic stream.
        /// </summary>
        /// <param name="diagnostics">The diagnostic stream to append to.</param>
        /// <param name="message">The diagnostic message to normalize and append.</param>
        private static void AddDiagnostic(List<string> diagnostics, string? message)
        {
            // Blank diagnostics cannot explain extraction behavior and are ignored at accumulation time.
            if (!string.IsNullOrWhiteSpace(message))
            {
                diagnostics.Add(message.Trim());
            }
        }

        /// <summary>
        /// Reads and validates the string value from a stable-key value object.
        /// </summary>
        /// <param name="stableKey">The stable key to validate.</param>
        /// <returns>The non-empty stable-key string value.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="stableKey"/> is a default value object without a valid string.</exception>
        private static string RequireStableKeyValue(StableKey stableKey)
        {
            // Default value-object instances can bypass their constructor, so accumulator keys are revalidated before storage.
            if (string.IsNullOrWhiteSpace(stableKey.Value))
            {
                throw new ArgumentException("Stable-keyed snapshot contributions require a non-empty stable key.", nameof(stableKey));
            }

            return stableKey.Value;
        }
    }
}
