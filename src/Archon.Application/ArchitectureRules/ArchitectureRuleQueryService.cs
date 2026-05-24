using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Application.Rules;

namespace Archon.Application.ArchitectureRules
{
    /// <summary>
    /// Implements controlled architecture-rule result query behavior over persisted architecture snapshots.
    /// </summary>
    public sealed class ArchitectureRuleQueryService : IArchitectureRuleQueryService
    {
        /// <summary>
        /// Reads snapshots from the registered architecture snapshot writer when in-memory diagnostics are available.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Evaluates architecture-rule results from snapshot facts.
        /// </summary>
        private readonly ArchitectureRuleEvaluator _evaluator;

        /// <summary>
        /// Stores configurable policy-like options for built-in architecture-rule checks.
        /// </summary>
        private readonly ArchitectureRuleEvaluationOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArchitectureRuleQueryService"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        public ArchitectureRuleQueryService(IArchitectureSnapshotWriter snapshotWriter)
            : this(snapshotWriter, ArchitectureRuleEvaluationOptions.Default)
        {
            // Default composition uses documented built-in behavior while preserving an explicit seam for future configured options.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArchitectureRuleQueryService"/> class with explicit evaluation options.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        /// <param name="options">The policy-like options used by built-in checks.</param>
        public ArchitectureRuleQueryService(IArchitectureSnapshotWriter snapshotWriter, ArchitectureRuleEvaluationOptions options)
        {
            // The query service mirrors hotspot and cycle query patterns so hosts expose controlled APIs without raw graph access.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _evaluator = new ArchitectureRuleEvaluator();
        }

        /// <summary>
        /// Lists architecture-rule results using controlled filters and deterministic ordering.
        /// </summary>
        /// <param name="query">The controlled architecture-rule query contract.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before evaluation starts.</param>
        /// <returns>A bounded page of stable architecture-rule result DTOs.</returns>
        public Task<PagedQueryResult<ArchitectureRuleItemDto>> ListArchitectureRulesAsync(ArchitectureRuleQuery query, CancellationToken cancellationToken)
        {
            // Results are evaluated from persisted snapshot facts and then filtered; callers never provide arbitrary rule expressions.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            ExtractedArchitectureSnapshot? snapshot = _snapshotWriter is InMemoryArchitectureSnapshotWriter writer
                ? writer.GetSnapshotsSnapshotForDiagnostics().FirstOrDefault(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.StableKey.Value, query.SnapshotStableKey))
                : null;
            if (snapshot is null)
            {
                PagedQueryResult<ArchitectureRuleItemDto> empty = new([], totalCount: 0, query.Skip, query.Take);
                return Task.FromResult(empty);
            }

            ArchitectureRuleResult[] matches = _evaluator.Evaluate(snapshot, _options)
                .Where(result => query.Category is null || StringComparer.Ordinal.Equals(result.Category, query.Category))
                .Where(result => query.Status is null || StringComparer.Ordinal.Equals(result.Status, query.Status))
                .Where(result => query.TargetStableKey is null || StringComparer.Ordinal.Equals(result.TargetStableKey.Value, query.TargetStableKey))
                .OrderBy(static result => result.Category, StringComparer.Ordinal)
                .ThenBy(static result => result.RuleCode, StringComparer.Ordinal)
                .ThenBy(static result => result.Status, StringComparer.Ordinal)
                .ThenBy(static result => result.TargetStableKey.Value, StringComparer.Ordinal)
                .ThenBy(static result => result.StableKey.Value, StringComparer.Ordinal)
                .ToArray();
            ArchitectureRuleItemDto[] items = matches
                .Skip(query.Skip)
                .Take(query.Take)
                .Select(ToArchitectureRuleItem)
                .ToArray();
            PagedQueryResult<ArchitectureRuleItemDto> result = new(items, matches.Length, query.Skip, query.Take);
            return Task.FromResult(result);
        }

        /// <summary>
        /// Maps an architecture-rule result to a public DTO.
        /// </summary>
        /// <param name="result">The evaluated architecture-rule result.</param>
        /// <returns>The stable public architecture-rule result DTO.</returns>
        private static ArchitectureRuleItemDto ToArchitectureRuleItem(ArchitectureRuleResult result)
        {
            // Public responses expose stable contribution references and sanitized metadata but never persistence-local identifiers.
            return new ArchitectureRuleItemDto(
                result.SnapshotStableKey.Value,
                result.StableKey.Value,
                result.RuleCode,
                result.RuleName,
                result.Category,
                result.Status,
                result.TargetStableKey.Value,
                result.TargetKind,
                result.DisplayName,
                result.Description,
                result.ContributingMetricStableKeys.Select(static stableKey => stableKey.Value).ToArray(),
                result.ContributingEdgeStableKeys.Select(static stableKey => stableKey.Value).ToArray(),
                result.ContributingFindingStableKeys.Select(static stableKey => stableKey.Value).ToArray(),
                result.EvidenceStableKeys.Select(static stableKey => stableKey.Value).ToArray(),
                result.Confidence.Value,
                result.UnknownState.HasUnknownData,
                result.UnknownState.UnknownReason,
                PublicMetadataSanitizer.Sanitize(result.Metadata),
                result.Fingerprint.Value);
        }
    }
}
