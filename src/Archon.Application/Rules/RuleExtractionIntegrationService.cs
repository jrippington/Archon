using System.Text.Json;
using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Contracts;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Coordinates WP012 copied-output rule loading, catalog persistence, snapshot fact projection, and data-only evaluation for extraction.
    /// </summary>
    public sealed class RuleExtractionIntegrationService
    {
        /// <summary>
        /// Loads and validates copied-output rule catalog files.
        /// </summary>
        private readonly RuleCatalogLoader _catalogLoader;

        /// <summary>
        /// Persists versioned rule catalog records behind an application-layer port.
        /// </summary>
        private readonly IRuleCatalogStore _catalogStore;

        /// <summary>
        /// Evaluates enabled rules against projected extraction snapshot facts.
        /// </summary>
        private readonly RuleEvaluator _ruleEvaluator;

        /// <summary>
        /// Logs credential-safe rule integration diagnostics.
        /// </summary>
        private readonly ILogger<RuleExtractionIntegrationService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleExtractionIntegrationService"/> class.
        /// </summary>
        /// <param name="catalogLoader">The copied-output rule catalog loader.</param>
        /// <param name="catalogStore">The catalog persistence port used to upsert validated rules.</param>
        /// <param name="ruleEvaluator">The data-only rule evaluator.</param>
        public RuleExtractionIntegrationService(RuleCatalogLoader catalogLoader, IRuleCatalogStore catalogStore, RuleEvaluator ruleEvaluator)
            : this(catalogLoader, catalogStore, ruleEvaluator, NullLogger<RuleExtractionIntegrationService>.Instance)
        {
            // This overload keeps tests concise while production composition can provide structured logging.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleExtractionIntegrationService"/> class.
        /// </summary>
        /// <param name="catalogLoader">The copied-output rule catalog loader.</param>
        /// <param name="catalogStore">The catalog persistence port used to upsert validated rules.</param>
        /// <param name="ruleEvaluator">The data-only rule evaluator.</param>
        /// <param name="logger">The logger used for credential-safe rule integration diagnostics.</param>
        public RuleExtractionIntegrationService(
            RuleCatalogLoader catalogLoader,
            IRuleCatalogStore catalogStore,
            RuleEvaluator ruleEvaluator,
            ILogger<RuleExtractionIntegrationService> logger)
        {
            // Constructor injection keeps rule orchestration application-owned and independent from API host composition.
            _catalogLoader = catalogLoader ?? throw new ArgumentNullException(nameof(catalogLoader));
            _catalogStore = catalogStore ?? throw new ArgumentNullException(nameof(catalogStore));
            _ruleEvaluator = ruleEvaluator ?? throw new ArgumentNullException(nameof(ruleEvaluator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Loads validated rules, persists the catalog, contributes rule definitions to the snapshot, and evaluates enabled rules.
        /// </summary>
        /// <param name="accumulation">The extraction accumulation model containing facts from earlier pipeline stages.</param>
        /// <param name="cancellationToken">The cancellation token flowing through loading, persistence, projection, and evaluation.</param>
        /// <returns>The rule integration result with deterministic counts and warnings.</returns>
        public async Task<RuleExtractionIntegrationResult> LoadPersistAndEvaluateAsync(ArchitectureSnapshotAccumulator accumulation, CancellationToken cancellationToken)
        {
            // This method is the application-layer entry point used by extraction so hosts do not place evaluator logic in composition code.
            ArgumentNullException.ThrowIfNull(accumulation);
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<RuleCatalogEntry> rules = await _catalogLoader.EnsureValidCatalogAsync(cancellationToken).ConfigureAwait(false);
            foreach (RuleCatalogEntry rule in rules)
            {
                accumulation.AddRule(RuleCatalogEntryMapper.ToRuleDefinition(rule));
            }

            RuleCatalogUpsertResult upsertResult = await _catalogStore.UpsertRulesAsync(rules, cancellationToken).ConfigureAwait(false);
            if (!upsertResult.Succeeded)
            {
                foreach (string warning in upsertResult.Warnings)
                {
                    accumulation.AddWarning($"Rule catalog persistence warning: {warning}");
                }

                foreach (string error in upsertResult.Errors)
                {
                    accumulation.AddError($"Rule catalog persistence failed: {error}");
                }

                _logger.LogWarning("Rule catalog persistence failed after loading {RuleCount} rules.", rules.Count);
                return new RuleExtractionIntegrationResult(rules.Count, upsertResult.UpsertedRuleCount, 0, 0, upsertResult.Warnings);
            }

            ExtractedArchitectureSnapshot snapshot = accumulation.ToSnapshot();
            RuleEvaluationGraph graph = ProjectEvaluationGraph(snapshot, accumulation);
            IReadOnlyList<RuleCatalogEntry> enabledRules = rules.Where(static rule => rule.AvailableForEvaluation).ToArray();
            RuleEvaluationResult evaluation = await _ruleEvaluator.EvaluateAsync(enabledRules, graph, cancellationToken).ConfigureAwait(false);

            foreach (string warning in upsertResult.Warnings)
            {
                accumulation.AddWarning($"Rule catalog persistence warning: {warning}");
            }

            foreach (RuleEvaluationWarning warning in evaluation.Warnings)
            {
                accumulation.AddWarning($"Rule evaluation warning {warning.Code} for rule {warning.RuleCode}: {warning.Message}");
            }

            foreach (RuleEvaluationUnknownState unknownState in evaluation.UnknownStates)
            {
                accumulation.AddWarning($"Rule evaluation unknown state for rule {unknownState.RuleCode} on node {unknownState.NodeStableKey}: {unknownState.Reason}");
            }

            _logger.LogInformation(
                "Rule extraction integration loaded {LoadedRuleCount} rules, upserted {UpsertedRuleCount} rules, evaluated {EvaluatedRuleCount} enabled rules, and found {MatchCount} matches.",
                rules.Count,
                upsertResult.UpsertedRuleCount,
                enabledRules.Count,
                evaluation.Matches.Count);

            return new RuleExtractionIntegrationResult(
                rules.Count,
                upsertResult.UpsertedRuleCount,
                enabledRules.Count,
                evaluation.Matches.Count,
                upsertResult.Warnings.Concat(evaluation.Warnings.Select(static warning => warning.Message)));
        }

        /// <summary>
        /// Projects the generalized accumulated snapshot into the smaller read model consumed by the rule evaluator.
        /// </summary>
        /// <param name="snapshot">The accumulated snapshot after rule definitions have been contributed.</param>
        /// <param name="accumulation">The original accumulator that receives projection diagnostics.</param>
        /// <returns>A rule evaluation graph containing candidate nodes and fact collections.</returns>
        private static RuleEvaluationGraph ProjectEvaluationGraph(ExtractedArchitectureSnapshot snapshot, ArchitectureSnapshotAccumulator accumulation)
        {
            // Projection intentionally consumes already-extracted graph facts rather than scanning source code again.
            IReadOnlyDictionary<string, List<ArchitectureEdge>> outgoingEdges = snapshot.Edges
                .GroupBy(static edge => edge.SourceNodeStableKey.Value, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.ToList(), StringComparer.Ordinal);
            Dictionary<string, List<MetricRecord>> metricsByNode = new(StringComparer.Ordinal);
            foreach (MetricRecord metric in snapshot.Metrics.Where(static metric => metric.NodeStableKey is not null))
            {
                string nodeStableKey = metric.NodeStableKey.GetValueOrDefault().Value;
                if (!metricsByNode.TryGetValue(nodeStableKey, out List<MetricRecord>? nodeMetrics))
                {
                    nodeMetrics = [];
                    metricsByNode.Add(nodeStableKey, nodeMetrics);
                }

                nodeMetrics.Add(metric);
            }

            List<RuleEvaluationNode> nodes = [];
            foreach (ArchitectureNode node in snapshot.Nodes)
            {
                JsonElement metadata = ParseMetadata(node.Metadata.ToCanonicalJson(), node.StableKey.Value, accumulation);
                IReadOnlyList<string> targetFrameworks = ReadStringFacts(metadata, "project.targetFramework")
                    .Concat(ReadStringFacts(metadata, "project.targetFrameworks"))
                    .Concat(ReadStringFacts(metadata, "project.legacyTargetFramework"))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                IReadOnlyList<string> packages = ReadPackageFacts(node.StableKey.Value, outgoingEdges, snapshot.Nodes);
                IReadOnlyList<string> filePaths = ReadStringFacts(metadata, "project.relativePath")
                    .Concat(node.PrimaryEvidenceStableKey is null ? [] : snapshot.Evidence.Where(evidence => evidence.StableKey == node.PrimaryEvidenceStableKey).Select(static evidence => evidence.FilePath.Value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                Dictionary<string, decimal> metrics = ReadMetrics(node.StableKey.Value, metricsByNode);
                List<string> unknownReasons = [];
                if (node.UnknownState.HasUnknownData && !string.IsNullOrWhiteSpace(node.UnknownState.UnknownReason))
                {
                    unknownReasons.Add(node.UnknownState.UnknownReason);
                }

                if (TryReadBoolean(metadata, "project.targetFrameworkUnknown"))
                {
                    unknownReasons.Add("Project target framework facts were unavailable during extraction.");
                }

                nodes.Add(new RuleEvaluationNode(
                    node.StableKey.Value,
                    node.NodeKind,
                    node.DisplayName,
                    targetFrameworks,
                    ReadStringFacts(metadata, "project.rootNamespace").Concat(ReadStringFacts(metadata, "semantic.namespace")),
                    ReadStringFacts(metadata, "semantic.symbolName").Concat(CreateNodeSymbolFacts(node)),
                    packages,
                    filePaths,
                    ReadStringFacts(metadata, "semantic.methodCalls"),
                    ReadStringFacts(metadata, "semantic.attributes"),
                    metrics,
                    CreateEvidenceStableKeys(node),
                    node.Confidence.Value,
                    unknownReasons));
            }

            return new RuleEvaluationGraph(nodes);
        }

        /// <summary>
        /// Parses canonical graph metadata for fact projection while preserving a warning when metadata is unexpectedly invalid.
        /// </summary>
        /// <param name="metadataJson">The canonical metadata JSON string to parse.</param>
        /// <param name="nodeStableKey">The node stable key used in projection diagnostics.</param>
        /// <param name="accumulation">The accumulator receiving projection diagnostics.</param>
        /// <returns>A JSON element representing the metadata object.</returns>
        private static JsonElement ParseMetadata(string metadataJson, string nodeStableKey, ArchitectureSnapshotAccumulator accumulation)
        {
            // GraphMetadata should always contain valid canonical JSON, but projection keeps failures rule-specific and non-fatal.
            try
            {
                using JsonDocument document = JsonDocument.Parse(metadataJson);
                return document.RootElement.Clone();
            }
            catch (JsonException exception)
            {
                accumulation.AddWarning($"Rule evaluation could not parse metadata for node {nodeStableKey}: {exception.Message}");
                using JsonDocument empty = JsonDocument.Parse("{}");
                return empty.RootElement.Clone();
            }
        }

        /// <summary>
        /// Reads string or string-array metadata facts from a JSON object property.
        /// </summary>
        /// <param name="metadata">The metadata object to inspect.</param>
        /// <param name="propertyName">The exact metadata property name.</param>
        /// <returns>The normalized metadata fact values.</returns>
        private static IReadOnlyList<string> ReadStringFacts(JsonElement metadata, string propertyName)
        {
            // Metadata facts are optional; missing or unsupported shapes simply produce an empty fact collection for partial evaluation warnings.
            if (metadata.ValueKind != JsonValueKind.Object || !metadata.TryGetProperty(propertyName, out JsonElement property))
            {
                return [];
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                string? value = property.GetString();
                return string.IsNullOrWhiteSpace(value) ? [] : [value.Trim()];
            }

            if (property.ValueKind == JsonValueKind.Array)
            {
                return property.EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    .Select(static item => item.GetString()!.Trim())
                    .ToArray();
            }

            return [];
        }

        /// <summary>
        /// Reads a boolean metadata property by exact name.
        /// </summary>
        /// <param name="metadata">The metadata object to inspect.</param>
        /// <param name="propertyName">The exact metadata property name.</param>
        /// <returns><see langword="true"/> when the property exists and is true; otherwise, <see langword="false"/>.</returns>
        private static bool TryReadBoolean(JsonElement metadata, string propertyName)
        {
            // Boolean flags help projection carry explicit unknown state from earlier extractor slices into evaluator warnings.
            return metadata.ValueKind == JsonValueKind.Object
                && metadata.TryGetProperty(propertyName, out JsonElement property)
                && property.ValueKind == JsonValueKind.True;
        }

        /// <summary>
        /// Creates the evaluator evidence stable-key sequence for one architecture node.
        /// </summary>
        /// <param name="node">The architecture node whose primary evidence should be exposed to evaluation.</param>
        /// <returns>A sequence containing the primary evidence stable key, or an empty sequence when none exists.</returns>
        private static IEnumerable<string> CreateEvidenceStableKeys(ArchitectureNode node)
        {
            // The evaluator accepts strings, while the domain node stores a stable-key value object.
            return node.PrimaryEvidenceStableKey is null ? Array.Empty<string>() : new[] { node.PrimaryEvidenceStableKey.Value.Value };
        }

        /// <summary>
        /// Creates symbol-like facts from the normalized node name fields.
        /// </summary>
        /// <param name="node">The architecture node whose name fields should be exposed as symbol facts.</param>
        /// <returns>A sequence of non-empty symbol fact strings.</returns>
        private static IEnumerable<string> CreateNodeSymbolFacts(ArchitectureNode node)
        {
            // Qualified names and display names give symbol rules a useful fallback even when semantic metadata is sparse.
            if (!string.IsNullOrWhiteSpace(node.QualifiedName))
            {
                yield return node.QualifiedName;
            }

            yield return node.DisplayName;
        }

        /// <summary>
        /// Reads package facts from outgoing dependency edges to package nodes.
        /// </summary>
        /// <param name="nodeStableKey">The source node stable key whose outgoing package edges should be inspected.</param>
        /// <param name="outgoingEdges">The outgoing edge lookup for the snapshot.</param>
        /// <param name="nodes">The snapshot nodes used to resolve package target names.</param>
        /// <returns>The package fact values associated with the node.</returns>
        private static IReadOnlyList<string> ReadPackageFacts(string nodeStableKey, IReadOnlyDictionary<string, List<ArchitectureEdge>> outgoingEdges, IReadOnlyList<ArchitectureNode> nodes)
        {
            // Package conditions use package node display or qualified names when project extraction has already emitted dependency facts.
            if (!outgoingEdges.TryGetValue(nodeStableKey, out List<ArchitectureEdge>? edges))
            {
                return [];
            }

            Dictionary<string, ArchitectureNode> nodesByStableKey = nodes.ToDictionary(static node => node.StableKey.Value, StringComparer.Ordinal);
            return edges
                .Select(edge => nodesByStableKey.TryGetValue(edge.TargetNodeStableKey.Value, out ArchitectureNode? target) && target.NodeKind == NodeKind.Package ? target : null)
                .Where(static target => target is not null)
                .SelectMany(static target => new[] { target!.QualifiedName, target.DisplayName })
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Reads numeric metric facts for one architecture node.
        /// </summary>
        /// <param name="nodeStableKey">The node stable key whose metrics should be read.</param>
        /// <param name="metricsByNode">The snapshot metrics grouped by node stable key.</param>
        /// <returns>A deterministic dictionary of metric names and numeric values.</returns>
        private static Dictionary<string, decimal> ReadMetrics(string nodeStableKey, IReadOnlyDictionary<string, List<MetricRecord>> metricsByNode)
        {
            // Metric threshold rules consume only numeric metrics; text-only metrics remain unavailable for this evaluator slice.
            Dictionary<string, decimal> metrics = new(StringComparer.Ordinal);
            if (!metricsByNode.TryGetValue(nodeStableKey, out List<MetricRecord>? nodeMetrics))
            {
                return metrics;
            }

            foreach (MetricRecord metric in nodeMetrics.Where(static metric => metric.NumericValue.HasValue).OrderBy(static metric => metric.Name, StringComparer.Ordinal))
            {
                metrics[metric.Name] = metric.NumericValue!.Value;
            }

            return metrics;
        }
    }
}
