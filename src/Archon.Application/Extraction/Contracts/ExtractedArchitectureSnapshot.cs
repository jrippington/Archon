using Archon.Domain.Graph.Model;

namespace Archon.Application.Extraction.Contracts
{
    /// <summary>
    /// Represents the authoritative in-memory architecture snapshot assembled by application-layer extraction contributors.
    /// </summary>
    /// <remarks>
    /// This contract is intentionally independent of Neo4j, Roslyn workspace loading, API hosts, MCP hosts, and filesystem-specific implementation details. It is the handoff shape that later extraction, persistence, API, MCP, markdown, diff, and hotlist work packages can consume without inventing separate top-level persistence models.
    /// </remarks>
    public sealed class ExtractedArchitectureSnapshot
    {
        /// <summary>
        /// Initializes a new immutable extracted architecture snapshot.
        /// </summary>
        /// <param name="snapshotHeader">The optional snapshot header that scopes the assembled facts.</param>
        /// <param name="repositories">The repositories contributed to the snapshot.</param>
        /// <param name="solutions">The solutions contributed to the snapshot.</param>
        /// <param name="nodes">The architecture nodes contributed to the snapshot.</param>
        /// <param name="edges">The architecture edges contributed to the snapshot.</param>
        /// <param name="evidence">The evidence records contributed to the snapshot.</param>
        /// <param name="rules">The versioned rule catalog entries contributed with the snapshot.</param>
        /// <param name="findings">The findings contributed to the snapshot.</param>
        /// <param name="metrics">The metrics contributed to the snapshot.</param>
        /// <param name="generatedSummaries">The generated summaries contributed to the snapshot.</param>
        /// <param name="warnings">The warning diagnostics emitted by extraction contributors.</param>
        /// <param name="errors">The error diagnostics emitted by extraction contributors.</param>
        public ExtractedArchitectureSnapshot(
            SnapshotHeader? snapshotHeader,
            IEnumerable<RepositoryModel>? repositories,
            IEnumerable<SolutionModel>? solutions,
            IEnumerable<ArchitectureNode>? nodes,
            IEnumerable<ArchitectureEdge>? edges,
            IEnumerable<EvidenceRecord>? evidence,
            IEnumerable<RuleDefinition>? rules,
            IEnumerable<FindingRecord>? findings,
            IEnumerable<MetricRecord>? metrics,
            IEnumerable<GeneratedSummary>? generatedSummaries,
            IEnumerable<string>? warnings,
            IEnumerable<string>? errors)
        {
            // Constructor copies every incoming sequence so callers cannot mutate the snapshot after creation.
            SnapshotHeader = snapshotHeader;
            Repositories = CopySection(repositories);
            Solutions = CopySection(solutions);
            Nodes = CopySection(nodes);
            Edges = CopySection(edges);
            Evidence = CopySection(evidence);
            Rules = CopySection(rules);
            Findings = CopySection(findings);
            Metrics = CopySection(metrics);
            GeneratedSummaries = CopySection(generatedSummaries);
            Warnings = CopyDiagnostics(warnings);
            Errors = CopyDiagnostics(errors);
        }

        /// <summary>
        /// Gets the optional snapshot header that scopes the assembled facts.
        /// </summary>
        public SnapshotHeader? SnapshotHeader { get; }

        /// <summary>
        /// Gets the repositories contributed to the snapshot.
        /// </summary>
        public IReadOnlyList<RepositoryModel> Repositories { get; }

        /// <summary>
        /// Gets the solutions contributed to the snapshot.
        /// </summary>
        public IReadOnlyList<SolutionModel> Solutions { get; }

        /// <summary>
        /// Gets the architecture nodes contributed to the snapshot.
        /// </summary>
        public IReadOnlyList<ArchitectureNode> Nodes { get; }

        /// <summary>
        /// Gets the architecture edges contributed to the snapshot.
        /// </summary>
        public IReadOnlyList<ArchitectureEdge> Edges { get; }

        /// <summary>
        /// Gets the evidence records contributed to the snapshot.
        /// </summary>
        public IReadOnlyList<EvidenceRecord> Evidence { get; }

        /// <summary>
        /// Gets the versioned rule catalog entries contributed with the snapshot.
        /// </summary>
        public IReadOnlyList<RuleDefinition> Rules { get; }

        /// <summary>
        /// Gets the findings contributed to the snapshot.
        /// </summary>
        public IReadOnlyList<FindingRecord> Findings { get; }

        /// <summary>
        /// Gets the metrics contributed to the snapshot.
        /// </summary>
        public IReadOnlyList<MetricRecord> Metrics { get; }

        /// <summary>
        /// Gets the generated summaries contributed to the snapshot.
        /// </summary>
        public IReadOnlyList<GeneratedSummary> GeneratedSummaries { get; }

        /// <summary>
        /// Gets the warning diagnostics emitted by extraction contributors.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Gets the error diagnostics emitted by extraction contributors.
        /// </summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>
        /// Copies a nullable graph fact sequence into an immutable read-only list.
        /// </summary>
        /// <typeparam name="TSection">The graph fact section item type.</typeparam>
        /// <param name="items">The nullable source sequence to copy.</param>
        /// <returns>A read-only list containing the source items, or an empty list when no source sequence is supplied.</returns>
        private static IReadOnlyList<TSection> CopySection<TSection>(IEnumerable<TSection>? items)
        {
            // Arrays provide a compact immutable snapshot as long as the property exposes only IReadOnlyList.
            return items is null ? [] : items.ToArray();
        }

        /// <summary>
        /// Copies and normalizes diagnostic messages into an immutable read-only list.
        /// </summary>
        /// <param name="diagnostics">The nullable source diagnostic sequence to copy.</param>
        /// <returns>A read-only list of trimmed non-empty diagnostic messages.</returns>
        private static IReadOnlyList<string> CopyDiagnostics(IEnumerable<string>? diagnostics)
        {
            // Blank diagnostics cannot explain extraction behavior and are omitted from authoritative snapshots.
            return diagnostics is null
                ? []
                : diagnostics.Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic)).Select(diagnostic => diagnostic.Trim()).ToArray();
        }
    }
}
