using System.Text.Json;
using Archon.Application.Extraction.Contracts;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Domain.Graph.Metrics;

namespace Archon.Application.ArchitectureRules
{
    /// <summary>
    /// Evaluates built-in WP013 architecture-rule checks from already-extracted snapshot facts.
    /// </summary>
    public sealed class ArchitectureRuleEvaluator
    {
        /// <summary>
        /// Evaluates graph, metric, finding, and semantic facts for one snapshot using the supplied configurable options.
        /// </summary>
        /// <param name="snapshot">The extracted architecture snapshot containing graph facts, metrics, findings, and configured rule definitions.</param>
        /// <param name="options">The policy-like options that keep organization-specific allowances outside the built-in rules.</param>
        /// <returns>A deterministic list of architecture-rule results.</returns>
        public IReadOnlyList<ArchitectureRuleResult> Evaluate(ExtractedArchitectureSnapshot snapshot, ArchitectureRuleEvaluationOptions options)
        {
            // Evaluation is pure and bounded to the snapshot: it never rescans source code, queries Neo4j, or applies organization-specific policy hidden in code.
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(options);
            StableKey snapshotStableKey = snapshot.SnapshotHeader?.StableKey ?? throw new ArgumentException("Architecture-rule evaluation requires a snapshot header stable key.", nameof(snapshot));
            EvaluationContext context = new(snapshot, options);
            List<ArchitectureRuleResult> results = [];

            EvaluateLayeringEdges(snapshotStableKey, context, results);
            EvaluateDataAccessFacts(snapshotStableKey, context, results);
            EvaluateWorkerMessagingFacts(snapshotStableKey, context, results);
            EvaluateSharedLibraryMetrics(snapshotStableKey, context, results);

            return results
                .OrderBy(static result => result.Category, StringComparer.Ordinal)
                .ThenBy(static result => result.RuleCode, StringComparer.Ordinal)
                .ThenBy(static result => result.Status, StringComparer.Ordinal)
                .ThenBy(static result => result.TargetStableKey.Value, StringComparer.Ordinal)
                .ThenBy(static result => result.StableKey.Value, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Evaluates generic dependency-direction checks for project layering boundaries.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot being evaluated.</param>
        /// <param name="context">The normalized evaluation context.</param>
        /// <param name="results">The mutable result collection receiving violations.</param>
        private static void EvaluateLayeringEdges(StableKey snapshotStableKey, EvaluationContext context, List<ArchitectureRuleResult> results)
        {
            // The three layering checks inspect dependency-like edges between project nodes and classify layers from generic metadata/name signals.
            foreach (ArchitectureEdge edge in context.DependencyEdges)
            {
                if (!context.NodesByStableKey.TryGetValue(edge.SourceNodeStableKey.Value, out ArchitectureNode? source) || !context.NodesByStableKey.TryGetValue(edge.TargetNodeStableKey.Value, out ArchitectureNode? target))
                {
                    continue;
                }

                string sourceLayer = context.GetLayer(source);
                string targetLayer = context.GetLayer(target);
                if (IsRuleEnabled(context, ArchitectureRuleChecks.DomainReferencesInfrastructure) && IsDomainLayer(sourceLayer) && IsInfrastructureLayer(targetLayer))
                {
                    results.Add(CreateResult(snapshotStableKey, ArchitectureRuleChecks.DomainReferencesInfrastructure, ArchitectureRuleResultStatus.Violation, source, ArchitectureRuleChecks.Find(ArchitectureRuleChecks.DomainReferencesInfrastructure)!.Description, [], [edge.StableKey], [], EdgeEvidence(edge), ConfidenceFrom(source, target, edge), UnknownState.Known, CreateMetadata("dependencyDirection", sourceLayer, targetLayer, null)));
                }

                if (IsRuleEnabled(context, ArchitectureRuleChecks.DomainReferencesWeb) && IsDomainLayer(sourceLayer) && IsWebLayer(targetLayer))
                {
                    results.Add(CreateResult(snapshotStableKey, ArchitectureRuleChecks.DomainReferencesWeb, ArchitectureRuleResultStatus.Violation, source, ArchitectureRuleChecks.Find(ArchitectureRuleChecks.DomainReferencesWeb)!.Description, [], [edge.StableKey], [], EdgeEvidence(edge), ConfidenceFrom(source, target, edge), UnknownState.Known, CreateMetadata("dependencyDirection", sourceLayer, targetLayer, null)));
                }

                if (IsRuleEnabled(context, ArchitectureRuleChecks.WebReferencedByNonWeb) && !IsWebLayer(sourceLayer) && IsWebLayer(targetLayer))
                {
                    results.Add(CreateResult(snapshotStableKey, ArchitectureRuleChecks.WebReferencedByNonWeb, ArchitectureRuleResultStatus.Violation, target, ArchitectureRuleChecks.Find(ArchitectureRuleChecks.WebReferencedByNonWeb)!.Description, [], [edge.StableKey], [], EdgeEvidence(edge), ConfidenceFrom(source, target, edge), UnknownState.Known, CreateMetadata("incomingWebReference", sourceLayer, targetLayer, source.StableKey.Value)));
                }
            }
        }

        /// <summary>
        /// Evaluates configurable direct data-access checks from semantic metadata and data-access relationship facts.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot being evaluated.</param>
        /// <param name="context">The normalized evaluation context.</param>
        /// <param name="results">The mutable result collection receiving violations.</param>
        private static void EvaluateDataAccessFacts(StableKey snapshotStableKey, EvaluationContext context, List<ArchitectureRuleResult> results)
        {
            // Direct data-access checks are generic but policy-like, so explicit options or disabled catalog entries can suppress them without code edits.
            foreach (ArchitectureNode node in context.Nodes)
            {
                string layer = context.GetLayer(node);
                NodeFacts facts = context.GetFacts(node);
                if (!context.Options.AllowApplicationLinqToSqlDirectUse && IsRuleEnabled(context, ArchitectureRuleChecks.ApplicationUsesLinqToSqlDirectly) && IsApplicationLayer(layer) && HasLinqToSqlUsage(context, node, facts))
                {
                    results.Add(CreateResult(snapshotStableKey, ArchitectureRuleChecks.ApplicationUsesLinqToSqlDirectly, ArchitectureRuleResultStatus.Violation, node, ArchitectureRuleChecks.Find(ArchitectureRuleChecks.ApplicationUsesLinqToSqlDirectly)!.Description, [], LinqEdges(context, node), [], NodeEvidence(node), node.Confidence, UnknownState.Known, CreateMetadata("semanticDataAccess", layer, "LinqToSql", null)));
                }

                if (!context.Options.AllowControllerDataContextDirectUse && IsRuleEnabled(context, ArchitectureRuleChecks.ControllerUsesDataContextDirectly) && IsControllerNode(node) && HasDataContextUsage(context, node, facts))
                {
                    results.Add(CreateResult(snapshotStableKey, ArchitectureRuleChecks.ControllerUsesDataContextDirectly, ArchitectureRuleResultStatus.Violation, node, ArchitectureRuleChecks.Find(ArchitectureRuleChecks.ControllerUsesDataContextDirectly)!.Description, [], DataContextEdges(context, node), [], NodeEvidence(node), node.Confidence, UnknownState.Known, CreateMetadata("semanticDataAccess", layer, "DataContext", null)));
                }
            }
        }

        /// <summary>
        /// Evaluates worker runtime evidence for missing queue or topic dependency visibility.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot being evaluated.</param>
        /// <param name="context">The normalized evaluation context.</param>
        /// <param name="results">The mutable result collection receiving unknown-state results.</param>
        private static void EvaluateWorkerMessagingFacts(StableKey snapshotStableKey, EvaluationContext context, List<ArchitectureRuleResult> results)
        {
            // This check returns Unknown rather than Violation because missing queue/topic edges can mean incomplete extraction rather than a proven design defect.
            if (!IsRuleEnabled(context, ArchitectureRuleChecks.WorkerMissingQueueOrTopicDependency))
            {
                return;
            }

            foreach (ArchitectureNode node in context.Nodes.Where(IsWorkerNode))
            {
                NodeFacts facts = context.GetFacts(node);
                if (!facts.MessagingExpected || HasQueueOrTopicDependency(context, node))
                {
                    continue;
                }

                UnknownState unknownState = UnknownState.Unknown("Worker runtime evidence indicates queue or topic messaging should exist, but no queue or topic dependency edge was observed in the extracted graph.");
                results.Add(CreateResult(snapshotStableKey, ArchitectureRuleChecks.WorkerMissingQueueOrTopicDependency, ArchitectureRuleResultStatus.Unknown, node, ArchitectureRuleChecks.Find(ArchitectureRuleChecks.WorkerMissingQueueOrTopicDependency)!.Description, [], [], [], NodeEvidence(node), Confidence.Medium, unknownState, CreateMetadata("runtimeMessaging", context.GetLayer(node), "QueueOrTopic", null)));
            }
        }

        /// <summary>
        /// Evaluates high fan-in shared library metrics and carries metric and finding contributions into review results.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot being evaluated.</param>
        /// <param name="context">The normalized evaluation context.</param>
        /// <param name="results">The mutable result collection receiving review results.</param>
        private static void EvaluateSharedLibraryMetrics(StableKey snapshotStableKey, EvaluationContext context, List<ArchitectureRuleResult> results)
        {
            // Shared-library review uses GraphFanIn metrics so the threshold can be configured while stable metric identities explain the score source.
            if (!IsRuleEnabled(context, ArchitectureRuleChecks.SharedLibraryHighFanInReview))
            {
                return;
            }

            foreach (MetricRecord metric in context.Metrics.Where(metric => StringComparer.Ordinal.Equals(metric.MetricKind, MetricDefinitions.GraphFanIn.Kind) && metric.NodeStableKey is not null && metric.NumericValue >= context.Options.SharedLibraryHighFanInThreshold).OrderBy(metric => metric.NodeStableKey!.Value.Value, StringComparer.Ordinal))
            {
                ArchitectureNode? node = context.NodesByStableKey.TryGetValue(metric.NodeStableKey!.Value.Value, out ArchitectureNode? matchedNode) ? matchedNode : null;
                if (node is null || !IsSharedLayer(context.GetLayer(node), node))
                {
                    continue;
                }

                FindingRecord[] contributingFindings = context.FindingsByNode.TryGetValue(node.StableKey.Value, out List<FindingRecord>? findings) ? findings.ToArray() : [];
                UnknownState unknownState = ComposeUnknownState(metric.UnknownState, contributingFindings.Select(static finding => finding.UnknownState));
                Confidence confidence = ComposeConfidence([metric.Confidence, .. contributingFindings.Select(static finding => finding.Confidence)]);
                results.Add(CreateResult(snapshotStableKey, ArchitectureRuleChecks.SharedLibraryHighFanInReview, ArchitectureRuleResultStatus.ReviewRequired, node, ArchitectureRuleChecks.Find(ArchitectureRuleChecks.SharedLibraryHighFanInReview)!.Description, [metric.StableKey], [], contributingFindings.Select(static finding => finding.StableKey), metric.PrimaryEvidenceStableKey is null ? [] : [metric.PrimaryEvidenceStableKey.Value], confidence, unknownState, CreateMetadata("metricThreshold", context.GetLayer(node), MetricDefinitions.GraphFanIn.Kind, context.Options.SharedLibraryHighFanInThreshold)));
            }
        }

        /// <summary>
        /// Creates one deterministic architecture-rule result from normalized contribution fields.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot being evaluated.</param>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="status">The stable result status.</param>
        /// <param name="target">The target node for the result.</param>
        /// <param name="description">The result description.</param>
        /// <param name="metricStableKeys">The metric contribution stable keys.</param>
        /// <param name="edgeStableKeys">The edge contribution stable keys.</param>
        /// <param name="findingStableKeys">The finding contribution stable keys.</param>
        /// <param name="evidenceStableKeys">The evidence contribution stable keys.</param>
        /// <param name="confidence">The normalized result confidence.</param>
        /// <param name="unknownState">The explicit unknown-state context.</param>
        /// <param name="metadata">The deterministic metadata explaining the check inputs.</param>
        /// <returns>A deterministic architecture-rule result.</returns>
        private static ArchitectureRuleResult CreateResult(StableKey snapshotStableKey, string ruleCode, string status, ArchitectureNode target, string description, IEnumerable<StableKey> metricStableKeys, IEnumerable<StableKey> edgeStableKeys, IEnumerable<StableKey> findingStableKeys, IEnumerable<StableKey> evidenceStableKeys, Confidence confidence, UnknownState unknownState, GraphMetadata metadata)
        {
            // Stable keys exclude rank and database IDs so the same logical rule result keeps identity across repeated extraction runs.
            ArchitectureRuleCheckDefinition definition = ArchitectureRuleChecks.Find(ruleCode) ?? throw new InvalidOperationException($"Unknown architecture rule code '{ruleCode}'.");
            StableKey stableKey = new($"architecture-rule://{snapshotStableKey.Value}/{ruleCode}/{target.StableKey.Value}");
            FingerprintInput input = FingerprintInput.Create("ArchitectureRuleResult")
                .AddField("snapshotStableKey", snapshotStableKey)
                .AddField("ruleCode", ruleCode)
                .AddField("category", definition.Category.Value)
                .AddField("status", status)
                .AddField("targetStableKey", target.StableKey)
                .AddField("targetKind", target.NodeKind.Value)
                .AddField("confidence", confidence.Value)
                .AddField("hasUnknownData", unknownState.HasUnknownData)
                .AddField("unknownReason", unknownState.UnknownReason)
                .AddField("metrics", string.Join("|", metricStableKeys.Select(static stableKey => stableKey.Value).OrderBy(static value => value, StringComparer.Ordinal)))
                .AddField("edges", string.Join("|", edgeStableKeys.Select(static stableKey => stableKey.Value).OrderBy(static value => value, StringComparer.Ordinal)))
                .AddField("findings", string.Join("|", findingStableKeys.Select(static stableKey => stableKey.Value).OrderBy(static value => value, StringComparer.Ordinal)))
                .AddField("evidence", string.Join("|", evidenceStableKeys.Select(static stableKey => stableKey.Value).OrderBy(static value => value, StringComparer.Ordinal)))
                .AddMetadata(metadata);
            return new ArchitectureRuleResult(snapshotStableKey, stableKey, ruleCode, definition.Name, definition.Category.Value, status, target.StableKey, target.NodeKind.Value, target.DisplayName, description, metricStableKeys, edgeStableKeys, findingStableKeys, evidenceStableKeys, confidence, unknownState, metadata, FingerprintGenerator.FromInput(input));
        }

        /// <summary>
        /// Determines whether a built-in rule is enabled by configured rule definitions in the snapshot.
        /// </summary>
        /// <param name="context">The normalized evaluation context.</param>
        /// <param name="ruleCode">The built-in rule code.</param>
        /// <returns><see langword="true"/> when the check should run; otherwise, <see langword="false"/>.</returns>
        private static bool IsRuleEnabled(EvaluationContext context, string ruleCode)
        {
            // A matching persisted rule definition controls enabled state; absent definitions leave built-in checks available by default.
            return !context.RuleEnabledByCode.TryGetValue(ruleCode, out bool enabled) || enabled;
        }

        /// <summary>
        /// Creates rule-result metadata with safe, lower camel case property names.
        /// </summary>
        /// <param name="source">The rule source or calculation source label.</param>
        /// <param name="sourceLayer">The source or target layer classification.</param>
        /// <param name="targetLayerOrSignal">The target layer, semantic signal, or metric kind.</param>
        /// <param name="threshold">The optional numeric threshold used by the rule.</param>
        /// <returns>Deterministic graph metadata for the result.</returns>
        private static GraphMetadata CreateMetadata(string source, string sourceLayer, string targetLayerOrSignal, object? threshold)
        {
            // Metadata explains generic rule behavior without placing normalized identity or status fields in metadata.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["ruleSource"] = source,
                ["sourceLayer"] = sourceLayer,
                ["targetLayerOrSignal"] = targetLayerOrSignal,
                ["policyScope"] = "GenericBuiltIn"
            };
            if (threshold is not null)
            {
                values["threshold"] = threshold;
            }

            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Gets the edge evidence stable key when one exists.
        /// </summary>
        /// <param name="edge">The edge whose evidence should be exposed.</param>
        /// <returns>A one-item evidence list or an empty list.</returns>
        private static IReadOnlyList<StableKey> EdgeEvidence(ArchitectureEdge edge)
        {
            // Edge evidence is optional, so contribution lists remain empty when extraction did not produce source evidence.
            return edge.PrimaryEvidenceStableKey is null ? [] : [edge.PrimaryEvidenceStableKey.Value];
        }

        /// <summary>
        /// Gets the node evidence stable key when one exists.
        /// </summary>
        /// <param name="node">The node whose evidence should be exposed.</param>
        /// <returns>A one-item evidence list or an empty list.</returns>
        private static IReadOnlyList<StableKey> NodeEvidence(ArchitectureNode node)
        {
            // Node evidence is optional, especially for fixture and inferred nodes.
            return node.PrimaryEvidenceStableKey is null ? [] : [node.PrimaryEvidenceStableKey.Value];
        }

        /// <summary>
        /// Composes confidence by taking the minimum confidence from the source, target, and edge facts.
        /// </summary>
        /// <param name="source">The source node fact.</param>
        /// <param name="target">The target node fact.</param>
        /// <param name="edge">The edge fact.</param>
        /// <returns>The conservative combined confidence.</returns>
        private static Confidence ConfidenceFrom(ArchitectureNode source, ArchitectureNode target, ArchitectureEdge edge)
        {
            // Conservative confidence avoids overstating a rule result when any contributing graph fact is less certain.
            return ComposeConfidence([source.Confidence, target.Confidence, edge.Confidence]);
        }

        /// <summary>
        /// Composes confidence values by choosing the lowest non-empty value.
        /// </summary>
        /// <param name="confidences">The confidence values to compose.</param>
        /// <returns>The conservative combined confidence.</returns>
        private static Confidence ComposeConfidence(IEnumerable<Confidence> confidences)
        {
            // Minimum confidence is deterministic and aligns with hotspot confidence composition.
            decimal minimum = confidences.Select(static confidence => confidence.Value).DefaultIfEmpty(1m).Min();
            return new Confidence(minimum);
        }

        /// <summary>
        /// Composes unknown-state values from one metric and zero or more findings.
        /// </summary>
        /// <param name="metricUnknownState">The unknown state from the metric source.</param>
        /// <param name="findingUnknownStates">The unknown states from contributing findings.</param>
        /// <returns>A known state when all inputs are known, otherwise a combined unknown state.</returns>
        private static UnknownState ComposeUnknownState(UnknownState metricUnknownState, IEnumerable<UnknownState> findingUnknownStates)
        {
            // Unknown reasons are joined in stable order so contributors can see which input made the review result incomplete.
            string[] reasons = findingUnknownStates.Append(metricUnknownState)
                .Where(static state => state.HasUnknownData && !string.IsNullOrWhiteSpace(state.UnknownReason))
                .Select(static state => state.UnknownReason!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static reason => reason, StringComparer.Ordinal)
                .ToArray();
            return reasons.Length == 0 ? UnknownState.Known : UnknownState.Unknown(string.Join("; ", reasons));
        }

        /// <summary>
        /// Determines whether an edge kind participates in dependency-direction checks.
        /// </summary>
        /// <param name="edge">The edge to inspect.</param>
        /// <returns><see langword="true"/> when the edge is dependency-like; otherwise, <see langword="false"/>.</returns>
        private static bool IsDependencyEdge(ArchitectureEdge edge)
        {
            // Dependency-direction checks intentionally ignore containment and support relationships.
            return edge.EdgeKind == EdgeKind.References || edge.EdgeKind == EdgeKind.DependsOn || edge.EdgeKind == EdgeKind.Calls || edge.EdgeKind == EdgeKind.CallsApi || edge.EdgeKind == EdgeKind.Injects;
        }

        /// <summary>
        /// Determines whether a layer string represents a domain layer.
        /// </summary>
        /// <param name="layer">The normalized layer string.</param>
        /// <returns><see langword="true"/> when the layer is domain-like; otherwise, <see langword="false"/>.</returns>
        private static bool IsDomainLayer(string layer)
        {
            // Layer matching uses broad generic terms rather than organization-specific project names.
            return layer.Contains("Domain", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a layer string represents an infrastructure layer.
        /// </summary>
        /// <param name="layer">The normalized layer string.</param>
        /// <returns><see langword="true"/> when the layer is infrastructure-like; otherwise, <see langword="false"/>.</returns>
        private static bool IsInfrastructureLayer(string layer)
        {
            // Infrastructure terms are generic source-brief architecture vocabulary.
            return layer.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a layer string represents a web or host composition layer.
        /// </summary>
        /// <param name="layer">The normalized layer string.</param>
        /// <returns><see langword="true"/> when the layer is web-like; otherwise, <see langword="false"/>.</returns>
        private static bool IsWebLayer(string layer)
        {
            // Web, API, host, and UI terms all represent outward composition/runtime surfaces for this generic check.
            return layer.Contains("Web", StringComparison.OrdinalIgnoreCase) || layer.Contains("Api", StringComparison.OrdinalIgnoreCase) || layer.Contains("Host", StringComparison.OrdinalIgnoreCase) || layer.Contains("Ui", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a layer string represents an application/services layer.
        /// </summary>
        /// <param name="layer">The normalized layer string.</param>
        /// <returns><see langword="true"/> when the layer is application-like; otherwise, <see langword="false"/>.</returns>
        private static bool IsApplicationLayer(string layer)
        {
            // The source brief uses application projects generically; services is accepted because this repository uses Services as an onion layer.
            return layer.Contains("Application", StringComparison.OrdinalIgnoreCase) || layer.Contains("Services", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a node represents a controller.
        /// </summary>
        /// <param name="node">The node to inspect.</param>
        /// <returns><see langword="true"/> when the node is controller-like; otherwise, <see langword="false"/>.</returns>
        private static bool IsControllerNode(ArchitectureNode node)
        {
            // Controller detection uses the controlled kind first and falls back to naming for fixtures and partial graph facts.
            return node.NodeKind == NodeKind.Controller || node.DisplayName.Contains("Controller", StringComparison.OrdinalIgnoreCase) || (node.QualifiedName?.Contains("Controller", StringComparison.OrdinalIgnoreCase) ?? false);
        }

        /// <summary>
        /// Determines whether a node represents a worker project or hosted service.
        /// </summary>
        /// <param name="node">The node to inspect.</param>
        /// <returns><see langword="true"/> when the node is worker-like; otherwise, <see langword="false"/>.</returns>
        private static bool IsWorkerNode(ArchitectureNode node)
        {
            // Worker checks apply to project-level worker names and hosted-service nodes because either can represent background processing.
            return node.NodeKind == NodeKind.HostedService || node.DisplayName.Contains("Worker", StringComparison.OrdinalIgnoreCase) || (node.QualifiedName?.Contains("Worker", StringComparison.OrdinalIgnoreCase) ?? false);
        }

        /// <summary>
        /// Determines whether a node represents a shared library target.
        /// </summary>
        /// <param name="layer">The normalized layer string.</param>
        /// <param name="node">The node to inspect.</param>
        /// <returns><see langword="true"/> when the node is shared-library-like; otherwise, <see langword="false"/>.</returns>
        private static bool IsSharedLayer(string layer, ArchitectureNode node)
        {
            // The shared-library review check is intentionally generic and relies on common shared/library vocabulary plus project node kind.
            return node.NodeKind == NodeKind.Project && (layer.Contains("Shared", StringComparison.OrdinalIgnoreCase) || node.DisplayName.Contains("Shared", StringComparison.OrdinalIgnoreCase) || node.DisplayName.Contains("Common", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Determines whether node semantic facts indicate LINQ to SQL usage.
        /// </summary>
        /// <param name="context">The normalized evaluation context.</param>
        /// <param name="node">The node to inspect.</param>
        /// <param name="facts">The node semantic facts.</param>
        /// <returns><see langword="true"/> when LINQ to SQL usage is present; otherwise, <see langword="false"/>.</returns>
        private static bool HasLinqToSqlUsage(EvaluationContext context, ArchitectureNode node, NodeFacts facts)
        {
            // LINQ to SQL can appear as namespace/method metadata or as explicit graph relationships to context nodes.
            return facts.Namespaces.Concat(facts.MethodCalls).Concat(facts.Symbols).Any(static value => value.Contains("System.Data.Linq", StringComparison.OrdinalIgnoreCase) || value.Contains("LinqToSql", StringComparison.OrdinalIgnoreCase))
                || context.OutgoingEdgesBySource.TryGetValue(node.StableKey.Value, out List<ArchitectureEdge>? edges) && edges.Any(static edge => edge.EdgeKind == EdgeKind.UsesLinqToSqlContext);
        }

        /// <summary>
        /// Determines whether node semantic facts indicate direct DataContext usage.
        /// </summary>
        /// <param name="context">The normalized evaluation context.</param>
        /// <param name="node">The node to inspect.</param>
        /// <param name="facts">The node semantic facts.</param>
        /// <returns><see langword="true"/> when DataContext usage is present; otherwise, <see langword="false"/>.</returns>
        private static bool HasDataContextUsage(EvaluationContext context, ArchitectureNode node, NodeFacts facts)
        {
            // Direct DataContext usage can be semantic text or explicit DbContext/LINQ-to-SQL graph relationships.
            return facts.MethodCalls.Concat(facts.Symbols).Concat(facts.Namespaces).Any(static value => value.Contains("DataContext", StringComparison.OrdinalIgnoreCase) || value.Contains("DbContext", StringComparison.OrdinalIgnoreCase))
                || context.OutgoingEdgesBySource.TryGetValue(node.StableKey.Value, out List<ArchitectureEdge>? edges) && edges.Any(static edge => edge.EdgeKind == EdgeKind.UsesDbContext || edge.EdgeKind == EdgeKind.UsesLinqToSqlContext);
        }

        /// <summary>
        /// Gets LINQ-to-SQL data-access edge contributions for a node.
        /// </summary>
        /// <param name="context">The normalized evaluation context.</param>
        /// <param name="node">The source node.</param>
        /// <returns>The matching edge stable keys.</returns>
        private static IReadOnlyList<StableKey> LinqEdges(EvaluationContext context, ArchitectureNode node)
        {
            // Edge contributions are included when extraction produced normalized data-access relationships.
            return context.OutgoingEdgesBySource.TryGetValue(node.StableKey.Value, out List<ArchitectureEdge>? edges)
                ? edges.Where(static edge => edge.EdgeKind == EdgeKind.UsesLinqToSqlContext).Select(static edge => edge.StableKey).ToArray()
                : [];
        }

        /// <summary>
        /// Gets direct data-context edge contributions for a node.
        /// </summary>
        /// <param name="context">The normalized evaluation context.</param>
        /// <param name="node">The source node.</param>
        /// <returns>The matching edge stable keys.</returns>
        private static IReadOnlyList<StableKey> DataContextEdges(EvaluationContext context, ArchitectureNode node)
        {
            // Controller direct-access results preserve normalized graph evidence when it is available.
            return context.OutgoingEdgesBySource.TryGetValue(node.StableKey.Value, out List<ArchitectureEdge>? edges)
                ? edges.Where(static edge => edge.EdgeKind == EdgeKind.UsesDbContext || edge.EdgeKind == EdgeKind.UsesLinqToSqlContext).Select(static edge => edge.StableKey).ToArray()
                : [];
        }

        /// <summary>
        /// Determines whether a worker has an observed queue or topic dependency edge.
        /// </summary>
        /// <param name="context">The normalized evaluation context.</param>
        /// <param name="node">The worker node to inspect.</param>
        /// <returns><see langword="true"/> when a queue or topic dependency exists; otherwise, <see langword="false"/>.</returns>
        private static bool HasQueueOrTopicDependency(EvaluationContext context, ArchitectureNode node)
        {
            // Queue and topic targets are checked by controlled node kind so naming variations do not matter once extraction has produced nodes.
            return context.OutgoingEdgesBySource.TryGetValue(node.StableKey.Value, out List<ArchitectureEdge>? edges)
                && edges.Any(edge => context.NodesByStableKey.TryGetValue(edge.TargetNodeStableKey.Value, out ArchitectureNode? target) && (target.NodeKind == NodeKind.Queue || target.NodeKind == NodeKind.Topic));
        }

        /// <summary>
        /// Represents normalized node semantic facts read from metadata.
        /// </summary>
        /// <param name="Namespaces">The namespace facts associated with the node.</param>
        /// <param name="Symbols">The symbol facts associated with the node.</param>
        /// <param name="MethodCalls">The method-call facts associated with the node.</param>
        /// <param name="MessagingExpected">A value indicating whether metadata says messaging dependencies are expected for this node.</param>
        private sealed record NodeFacts(IReadOnlyList<string> Namespaces, IReadOnlyList<string> Symbols, IReadOnlyList<string> MethodCalls, bool MessagingExpected);

        /// <summary>
        /// Provides a normalized read model over snapshot facts for architecture-rule evaluation.
        /// </summary>
        private sealed class EvaluationContext
        {
            /// <summary>
            /// Stores parsed node facts by node stable key.
            /// </summary>
            private readonly Dictionary<string, NodeFacts> _factsByNode = new(StringComparer.Ordinal);

            /// <summary>
            /// Stores layer names by node stable key.
            /// </summary>
            private readonly Dictionary<string, string> _layersByNode = new(StringComparer.Ordinal);

            /// <summary>
            /// Initializes a new instance of the <see cref="EvaluationContext"/> class.
            /// </summary>
            /// <param name="snapshot">The extracted architecture snapshot to normalize.</param>
            /// <param name="options">The configurable architecture-rule evaluation options.</param>
            public EvaluationContext(ExtractedArchitectureSnapshot snapshot, ArchitectureRuleEvaluationOptions options)
            {
                // The context builds indexes once so individual checks can stay simple, deterministic, and side-effect free.
                Options = options;
                Nodes = snapshot.Nodes.OrderBy(static node => node.StableKey.Value, StringComparer.Ordinal).ToArray();
                Metrics = snapshot.Metrics.OrderBy(static metric => metric.StableKey.Value, StringComparer.Ordinal).ToArray();
                NodesByStableKey = Nodes.ToDictionary(static node => node.StableKey.Value, StringComparer.Ordinal);
                DependencyEdges = snapshot.Edges.Where(IsDependencyEdge).OrderBy(static edge => edge.StableKey.Value, StringComparer.Ordinal).ToArray();
                OutgoingEdgesBySource = snapshot.Edges.GroupBy(static edge => edge.SourceNodeStableKey.Value, StringComparer.Ordinal).ToDictionary(static group => group.Key, static group => group.OrderBy(edge => edge.StableKey.Value, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
                RuleEnabledByCode = snapshot.Rules.GroupBy(static rule => rule.RuleCode, StringComparer.Ordinal).ToDictionary(static group => group.Key, static group => group.OrderByDescending(rule => rule.Version, StringComparer.Ordinal).First().Enabled, StringComparer.Ordinal);
                FindingsByNode = BuildFindingsByNode(snapshot.Findings);
                foreach (ArchitectureNode node in Nodes)
                {
                    using JsonDocument metadata = JsonDocument.Parse(node.Metadata.ToCanonicalJson());
                    _factsByNode[node.StableKey.Value] = new NodeFacts(ReadStringFacts(metadata.RootElement, "semantic.namespaces").Concat(ReadStringFacts(metadata.RootElement, "semantic.namespace")).ToArray(), ReadStringFacts(metadata.RootElement, "semantic.symbols").Concat(ReadStringFacts(metadata.RootElement, "semantic.symbolName")).ToArray(), ReadStringFacts(metadata.RootElement, "semantic.methodCalls").ToArray(), ReadBoolean(metadata.RootElement, "runtime.messagingExpected"));
                    _layersByNode[node.StableKey.Value] = ReadStringFacts(metadata.RootElement, "architecture.layer").FirstOrDefault() ?? InferLayer(node);
                }
            }

            /// <summary>
            /// Gets the configurable architecture-rule evaluation options.
            /// </summary>
            public ArchitectureRuleEvaluationOptions Options { get; }

            /// <summary>
            /// Gets nodes sorted by stable key.
            /// </summary>
            public IReadOnlyList<ArchitectureNode> Nodes { get; }

            /// <summary>
            /// Gets metrics sorted by stable key.
            /// </summary>
            public IReadOnlyList<MetricRecord> Metrics { get; }

            /// <summary>
            /// Gets nodes keyed by stable key.
            /// </summary>
            public IReadOnlyDictionary<string, ArchitectureNode> NodesByStableKey { get; }

            /// <summary>
            /// Gets dependency-like edges sorted by stable key.
            /// </summary>
            public IReadOnlyList<ArchitectureEdge> DependencyEdges { get; }

            /// <summary>
            /// Gets outgoing edges grouped by source node stable key.
            /// </summary>
            public IReadOnlyDictionary<string, List<ArchitectureEdge>> OutgoingEdgesBySource { get; }

            /// <summary>
            /// Gets configured rule enabled states keyed by rule code.
            /// </summary>
            public IReadOnlyDictionary<string, bool> RuleEnabledByCode { get; }

            /// <summary>
            /// Gets findings grouped by affected node stable key.
            /// </summary>
            public IReadOnlyDictionary<string, List<FindingRecord>> FindingsByNode { get; }

            /// <summary>
            /// Gets normalized semantic facts for one node.
            /// </summary>
            /// <param name="node">The node whose facts should be read.</param>
            /// <returns>The normalized node facts.</returns>
            public NodeFacts GetFacts(ArchitectureNode node)
            {
                // Every node receives a facts entry during construction, but a safe empty fallback protects future partial contexts.
                return _factsByNode.TryGetValue(node.StableKey.Value, out NodeFacts? facts) ? facts : new NodeFacts([], [], [], false);
            }

            /// <summary>
            /// Gets the normalized architecture layer classification for one node.
            /// </summary>
            /// <param name="node">The node whose layer should be read.</param>
            /// <returns>The normalized layer string.</returns>
            public string GetLayer(ArchitectureNode node)
            {
                // Layer names are generic labels inferred from metadata or common project naming vocabulary.
                return _layersByNode.TryGetValue(node.StableKey.Value, out string? layer) ? layer : InferLayer(node);
            }

            /// <summary>
            /// Builds a deterministic finding lookup by affected node.
            /// </summary>
            /// <param name="findings">The findings to group.</param>
            /// <returns>A dictionary of findings keyed by affected node stable key.</returns>
            private static IReadOnlyDictionary<string, List<FindingRecord>> BuildFindingsByNode(IEnumerable<FindingRecord> findings)
            {
                // Findings may affect multiple nodes, so each affected node receives the same finding contribution reference.
                Dictionary<string, List<FindingRecord>> result = new(StringComparer.Ordinal);
                foreach (FindingRecord finding in findings.OrderBy(static finding => finding.StableKey.Value, StringComparer.Ordinal))
                {
                    foreach (StableKey nodeStableKey in finding.AffectedNodeStableKeys)
                    {
                        if (!result.TryGetValue(nodeStableKey.Value, out List<FindingRecord>? nodeFindings))
                        {
                            nodeFindings = [];
                            result.Add(nodeStableKey.Value, nodeFindings);
                        }

                        nodeFindings.Add(finding);
                    }
                }

                return result;
            }

            /// <summary>
            /// Reads text facts from a JSON metadata property.
            /// </summary>
            /// <param name="metadata">The metadata JSON object.</param>
            /// <param name="propertyName">The property name to read.</param>
            /// <returns>A normalized text fact list.</returns>
            private static IReadOnlyList<string> ReadStringFacts(JsonElement metadata, string propertyName)
            {
                // Metadata can store a single string or an array of strings; unsupported shapes are treated as absent facts.
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
                    return property.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())).Select(static item => item.GetString()!.Trim()).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
                }

                return [];
            }

            /// <summary>
            /// Reads one boolean metadata property.
            /// </summary>
            /// <param name="metadata">The metadata JSON object.</param>
            /// <param name="propertyName">The property name to read.</param>
            /// <returns><see langword="true"/> when the property exists and is true; otherwise, <see langword="false"/>.</returns>
            private static bool ReadBoolean(JsonElement metadata, string propertyName)
            {
                // Boolean metadata flags are optional and default to false when absent.
                return metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.True;
            }

            /// <summary>
            /// Infers a generic layer label from node kind and display name when metadata does not supply one.
            /// </summary>
            /// <param name="node">The node to classify.</param>
            /// <returns>A generic layer or node-kind label.</returns>
            private static string InferLayer(ArchitectureNode node)
            {
                // Inference is deliberately simple and generic; organization-specific conventions belong in configured metadata or catalog rules.
                string text = string.Join(" ", node.DisplayName, node.QualifiedName, node.StableKey.Value);
                if (text.Contains("Domain", StringComparison.OrdinalIgnoreCase))
                {
                    return "Domain";
                }

                if (text.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase))
                {
                    return "Infrastructure";
                }

                if (text.Contains("Web", StringComparison.OrdinalIgnoreCase) || text.Contains("Api", StringComparison.OrdinalIgnoreCase) || text.Contains("Host", StringComparison.OrdinalIgnoreCase))
                {
                    return "Web";
                }

                if (text.Contains("Application", StringComparison.OrdinalIgnoreCase) || text.Contains("Services", StringComparison.OrdinalIgnoreCase))
                {
                    return "Application";
                }

                if (text.Contains("Shared", StringComparison.OrdinalIgnoreCase) || text.Contains("Common", StringComparison.OrdinalIgnoreCase))
                {
                    return "Shared";
                }

                if (text.Contains("Worker", StringComparison.OrdinalIgnoreCase))
                {
                    return "Worker";
                }

                return node.NodeKind.Value;
            }
        }
    }
}
