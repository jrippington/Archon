using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Application.Rules;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Diff
{
    /// <summary>
    /// Implements deterministic snapshot diff comparison over persisted snapshot facts.
    /// </summary>
    public sealed class SnapshotDiffService : ISnapshotDiffService
    {
        /// <summary>
        /// Reads persisted snapshots from the registered architecture snapshot writer when in-memory diagnostics are available.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotDiffService"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        public SnapshotDiffService(IArchitectureSnapshotWriter snapshotWriter)
        {
            // The service mirrors other WP013 query services by using the application snapshot contract instead of persistence-local IDs.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
        }

        /// <summary>
        /// Compares two snapshots using stable keys and normalized fingerprints across nodes, edges, findings, and metrics.
        /// </summary>
        /// <param name="query">The controlled snapshot diff request.</param>
        /// <param name="cancellationToken">The token that can cancel comparison before snapshot data is read.</param>
        /// <returns>A snapshot diff result containing summaries, bounded details, truncation metadata, or validation errors.</returns>
        public Task<SnapshotDiffResult> CompareSnapshotsAsync(SnapshotDiffQuery query, CancellationToken cancellationToken)
        {
            // Validation happens before comparison so missing snapshots and unsupported filters return deterministic client-correctable errors.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ExtractedArchitectureSnapshot> snapshots = _snapshotWriter is InMemoryArchitectureSnapshotWriter writer
                ? writer.GetSnapshotsSnapshotForDiagnostics()
                : [];
            ExtractedArchitectureSnapshot? current = snapshots.FirstOrDefault(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.StableKey.Value, query.CurrentSnapshotStableKey));
            ExtractedArchitectureSnapshot? previous = snapshots.FirstOrDefault(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.StableKey.Value, query.PreviousSnapshotStableKey));
            List<SnapshotDiffValidationError> validationErrors = Validate(query, current, previous);
            if (validationErrors.Count > 0)
            {
                return Task.FromResult(new SnapshotDiffResult(query.CurrentSnapshotStableKey, query.PreviousSnapshotStableKey, validationErrors));
            }

            // After validation, both snapshots must be non-null; this defensive guard keeps nullable flow explicit for the compiler.
            if (current is not ExtractedArchitectureSnapshot currentSnapshot || previous is not ExtractedArchitectureSnapshot previousSnapshot)
            {
                SnapshotDiffValidationError defensiveError = new(SnapshotDiffValidationCodes.CurrentSnapshotNotFound, "Snapshot diff could not locate both requested snapshots.");
                return Task.FromResult(new SnapshotDiffResult(query.CurrentSnapshotStableKey, query.PreviousSnapshotStableKey, [defensiveError]));
            }
            HashSet<string> includedDomains = query.Domains.Count == 0
                ? new HashSet<string>(SnapshotDiffDomains.All, StringComparer.Ordinal)
                : new HashSet<string>(query.Domains, StringComparer.Ordinal);
            HashSet<string> includedChangeKinds = query.ChangeKinds.Count == 0
                ? new HashSet<string>(SnapshotDiffChangeKind.All, StringComparer.Ordinal)
                : new HashSet<string>(query.ChangeKinds, StringComparer.Ordinal);
            List<SnapshotDiffSummaryDto> summaries = [];
            List<SnapshotDiffItemDto> allItems = [];

            AddDomainComparison(summaries, allItems, SnapshotDiffDomains.Nodes, previousSnapshot.Nodes.Select(ToComparableNode), currentSnapshot.Nodes.Select(ToComparableNode), query.IncludeUnchangedDetails);
            AddDomainComparison(summaries, allItems, SnapshotDiffDomains.Edges, previousSnapshot.Edges.Select(ToComparableEdge), currentSnapshot.Edges.Select(ToComparableEdge), query.IncludeUnchangedDetails);
            AddDomainComparison(summaries, allItems, SnapshotDiffDomains.Findings, previousSnapshot.Findings.Select(ToComparableFinding), currentSnapshot.Findings.Select(ToComparableFinding), query.IncludeUnchangedDetails);
            AddDomainComparison(summaries, allItems, SnapshotDiffDomains.Metrics, previousSnapshot.Metrics.Select(ToComparableMetric), currentSnapshot.Metrics.Select(ToComparableMetric), query.IncludeUnchangedDetails);

            SnapshotDiffSummaryDto[] filteredSummaries = summaries
                .Where(summary => includedDomains.Contains(summary.Domain))
                .OrderBy(summary => DomainOrder(summary.Domain))
                .ToArray();
            SnapshotDiffItemDto[] filteredItems = allItems
                .Where(item => includedDomains.Contains(item.Domain))
                .Where(item => includedChangeKinds.Contains(item.ChangeKind))
                .OrderBy(item => DomainOrder(item.Domain))
                .ThenBy(item => ChangeKindOrder(item.ChangeKind))
                .ThenBy(item => item.StableKey, StringComparer.Ordinal)
                .ToArray();
            SnapshotDiffItemDto[] page = filteredItems.Skip(query.Skip).Take(query.Take).ToArray();
            SnapshotDiffTruncationDto truncation = new(
                query.Skip > 0 || query.Skip + page.Length < filteredItems.Length,
                filteredItems.Length,
                page.Length,
                query.Skip,
                query.Take);
            string comparisonScope = currentSnapshot.SnapshotHeader!.RepositoryStableKey.Value;
            SnapshotDiffResult result = new(query.CurrentSnapshotStableKey, query.PreviousSnapshotStableKey, comparisonScope, filteredSummaries, page, truncation);
            return Task.FromResult(result);
        }

        /// <summary>
        /// Validates the request and located snapshots before comparison starts.
        /// </summary>
        /// <param name="query">The controlled snapshot diff request.</param>
        /// <param name="current">The located current snapshot, if any.</param>
        /// <param name="previous">The located previous snapshot, if any.</param>
        /// <returns>A deterministic list of validation errors.</returns>
        private static List<SnapshotDiffValidationError> Validate(SnapshotDiffQuery query, ExtractedArchitectureSnapshot? current, ExtractedArchitectureSnapshot? previous)
        {
            // Validation accumulates all request problems that can be known without performing the diff.
            List<SnapshotDiffValidationError> errors = [];
            if (string.IsNullOrWhiteSpace(query.CurrentSnapshotStableKey))
            {
                errors.Add(new SnapshotDiffValidationError(SnapshotDiffValidationCodes.CurrentSnapshotRequired, "A current snapshot stable key is required for snapshot diff."));
            }
            else if (current is null)
            {
                errors.Add(new SnapshotDiffValidationError(SnapshotDiffValidationCodes.CurrentSnapshotNotFound, "The current snapshot was not found."));
            }

            if (string.IsNullOrWhiteSpace(query.PreviousSnapshotStableKey))
            {
                errors.Add(new SnapshotDiffValidationError(SnapshotDiffValidationCodes.PreviousSnapshotRequired, "A previous snapshot stable key is required for snapshot diff."));
            }
            else if (previous is null)
            {
                errors.Add(new SnapshotDiffValidationError(SnapshotDiffValidationCodes.PreviousSnapshotNotFound, "The previous snapshot was not found."));
            }

            foreach (string domain in query.Domains)
            {
                if (!SnapshotDiffDomains.All.Contains(domain, StringComparer.Ordinal))
                {
                    errors.Add(new SnapshotDiffValidationError(SnapshotDiffValidationCodes.UnsupportedDomain, $"Unsupported snapshot diff domain '{domain}'."));
                }
            }

            foreach (string changeKind in query.ChangeKinds)
            {
                if (!SnapshotDiffChangeKind.All.Contains(changeKind, StringComparer.Ordinal))
                {
                    errors.Add(new SnapshotDiffValidationError(SnapshotDiffValidationCodes.UnsupportedChangeKind, $"Unsupported snapshot diff change kind '{changeKind}'."));
                }
            }

            if (query.Skip < 0)
            {
                errors.Add(new SnapshotDiffValidationError(SnapshotDiffValidationCodes.SkipInvalid, "Skip must be greater than or equal to zero."));
            }

            if (query.Take < 1 || query.Take > QueryPagingOptions.MaximumPageSize)
            {
                errors.Add(new SnapshotDiffValidationError(SnapshotDiffValidationCodes.TakeInvalid, "Take must be between 1 and 500."));
            }

            if (current?.SnapshotHeader is not null && previous?.SnapshotHeader is not null && !StringComparer.Ordinal.Equals(current.SnapshotHeader.RepositoryStableKey.Value, previous.SnapshotHeader.RepositoryStableKey.Value))
            {
                errors.Add(new SnapshotDiffValidationError(SnapshotDiffValidationCodes.IncompatibleRepository, "Snapshot diff requires snapshots from the same repository or an explicitly compatible comparison scope."));
            }

            return errors;
        }

        /// <summary>
        /// Compares one domain and appends summary and detail rows to the aggregate result buffers.
        /// </summary>
        /// <param name="summaries">The mutable summary buffer.</param>
        /// <param name="items">The mutable item buffer.</param>
        /// <param name="domain">The controlled domain being compared.</param>
        /// <param name="previousRecords">The previous comparable records.</param>
        /// <param name="currentRecords">The current comparable records.</param>
        /// <param name="includeUnchangedDetails">Indicates whether unchanged detail rows should be emitted.</param>
        private static void AddDomainComparison(List<SnapshotDiffSummaryDto> summaries, List<SnapshotDiffItemDto> items, string domain, IEnumerable<ComparableRecord> previousRecords, IEnumerable<ComparableRecord> currentRecords, bool includeUnchangedDetails)
        {
            // Stable-key dictionaries are the heart of diff comparison: fingerprints classify content change only after identity matching succeeds.
            Dictionary<string, ComparableRecord> previousByStableKey = ToStableKeyDictionary(previousRecords);
            Dictionary<string, ComparableRecord> currentByStableKey = ToStableKeyDictionary(currentRecords);
            SortedSet<string> stableKeys = new(previousByStableKey.Keys.Concat(currentByStableKey.Keys), StringComparer.Ordinal);
            int added = 0;
            int removed = 0;
            int changed = 0;
            int unchanged = 0;
            foreach (string stableKey in stableKeys)
            {
                bool hasPrevious = previousByStableKey.TryGetValue(stableKey, out ComparableRecord? previous);
                bool hasCurrent = currentByStableKey.TryGetValue(stableKey, out ComparableRecord? current);
                if (!hasPrevious && hasCurrent)
                {
                    added++;
                    items.Add(ToItem(domain, SnapshotDiffChangeKind.Added, null, current!));
                    continue;
                }

                if (hasPrevious && !hasCurrent)
                {
                    removed++;
                    items.Add(ToItem(domain, SnapshotDiffChangeKind.Removed, previous!, null));
                    continue;
                }

                if (previous!.Fingerprint == current!.Fingerprint)
                {
                    unchanged++;
                    if (includeUnchangedDetails)
                    {
                        items.Add(ToItem(domain, SnapshotDiffChangeKind.Unchanged, previous, current));
                    }

                    continue;
                }

                changed++;
                items.Add(ToItem(domain, SnapshotDiffChangeKind.Changed, previous, current));
            }

            summaries.Add(new SnapshotDiffSummaryDto(domain, added, removed, changed, unchanged));
        }

        /// <summary>
        /// Converts comparable records into a deterministic stable-key dictionary.
        /// </summary>
        /// <param name="records">The comparable records to index.</param>
        /// <returns>A stable-key dictionary using the last deterministic record for duplicate keys.</returns>
        private static Dictionary<string, ComparableRecord> ToStableKeyDictionary(IEnumerable<ComparableRecord> records)
        {
            // Duplicate stable keys should not happen in a valid snapshot; ordering still makes the selected record deterministic if bad input appears.
            return records
                .OrderBy(static record => record.StableKey, StringComparer.Ordinal)
                .GroupBy(static record => record.StableKey, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        }

        /// <summary>
        /// Maps matched comparable records into one public diff item.
        /// </summary>
        /// <param name="domain">The controlled domain being compared.</param>
        /// <param name="changeKind">The classified change kind.</param>
        /// <param name="previous">The previous record when present.</param>
        /// <param name="current">The current record when present.</param>
        /// <returns>A public snapshot diff item.</returns>
        private static SnapshotDiffItemDto ToItem(string domain, string changeKind, ComparableRecord? previous, ComparableRecord? current)
        {
            // The current record provides display/evidence context for added and changed rows; removed rows fall back to previous context.
            ComparableRecord representative = current ?? previous ?? throw new ArgumentException("A diff item requires at least one comparable record.", nameof(current));
            string? previousFingerprint = previous?.Fingerprint;
            string? currentFingerprint = current?.Fingerprint;
            IReadOnlyList<string> changedFields = changeKind == SnapshotDiffChangeKind.Changed
                ? GetChangedFields(previous!, current!)
                : [];
            string[] evidenceStableKeys = (current?.EvidenceStableKeys ?? previous?.EvidenceStableKeys ?? [])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static stableKey => stableKey, StringComparer.Ordinal)
                .ToArray();
            UnknownSnapshotState unknownState = current?.UnknownState.HasUnknownData == true || previous?.UnknownState.HasUnknownData != true
                ? current?.UnknownState ?? UnknownSnapshotState.Known
                : previous.UnknownState;
            return new SnapshotDiffItemDto(
                domain,
                changeKind,
                representative.StableKey,
                representative.DisplayName,
                representative.Kind,
                previousFingerprint,
                currentFingerprint,
                changedFields,
                evidenceStableKeys,
                unknownState.HasUnknownData,
                unknownState.UnknownReason);
        }

        /// <summary>
        /// Builds a deterministic changed-field summary for two matched records.
        /// </summary>
        /// <param name="previous">The previous comparable record.</param>
        /// <param name="current">The current comparable record.</param>
        /// <returns>The changed field names known to differ.</returns>
        private static IReadOnlyList<string> GetChangedFields(ComparableRecord previous, ComparableRecord current)
        {
            // Fingerprint is always included for changed rows; known public fields add more explanation when practical.
            List<string> fields = ["fingerprint"];
            AddIfDifferent(fields, "displayName", previous.DisplayName, current.DisplayName);
            AddIfDifferent(fields, "kind", previous.Kind, current.Kind);
            AddIfDifferent(fields, "evidenceStableKeys", string.Join("|", previous.EvidenceStableKeys), string.Join("|", current.EvidenceStableKeys));
            AddIfDifferent(fields, "unknownState", previous.UnknownState.ToComparisonString(), current.UnknownState.ToComparisonString());
            return fields.Distinct(StringComparer.Ordinal).OrderBy(static field => field, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Adds a field name when two normalized values differ.
        /// </summary>
        /// <param name="fields">The mutable field-name list.</param>
        /// <param name="fieldName">The public field name.</param>
        /// <param name="previous">The previous normalized value.</param>
        /// <param name="current">The current normalized value.</param>
        private static void AddIfDifferent(List<string> fields, string fieldName, string? previous, string? current)
        {
            // Ordinal comparison keeps changed-field summaries deterministic and culture-independent.
            if (!StringComparer.Ordinal.Equals(previous ?? string.Empty, current ?? string.Empty))
            {
                fields.Add(fieldName);
            }
        }

        /// <summary>
        /// Converts a domain architecture node into a comparable diff record.
        /// </summary>
        /// <param name="node">The architecture node to convert.</param>
        /// <returns>A comparable node record.</returns>
        private static ComparableRecord ToComparableNode(ArchitectureNode node)
        {
            // Node diff identity is the node stable key; content change is represented by the node fingerprint.
            return new ComparableRecord(
                node.StableKey.Value,
                node.DisplayName,
                node.NodeKind.Value,
                node.Fingerprint.Value,
                ToEvidenceKeys(node.PrimaryEvidenceStableKey),
                ToUnknownSnapshotState(node.UnknownState));
        }

        /// <summary>
        /// Converts a domain architecture edge into a comparable diff record.
        /// </summary>
        /// <param name="edge">The architecture edge to convert.</param>
        /// <returns>A comparable edge record.</returns>
        private static ComparableRecord ToComparableEdge(ArchitectureEdge edge)
        {
            // Edge display text includes source and target stable keys so reviewers can recognize relationship direction in diff output.
            string displayName = edge.SourceNodeStableKey.Value + " -> " + edge.TargetNodeStableKey.Value;
            return new ComparableRecord(
                edge.StableKey.Value,
                displayName,
                edge.EdgeKind.Value,
                edge.Fingerprint.Value,
                ToEvidenceKeys(edge.PrimaryEvidenceStableKey),
                ToUnknownSnapshotState(edge.UnknownState));
        }

        /// <summary>
        /// Converts a domain finding into a comparable diff record.
        /// </summary>
        /// <param name="finding">The finding to convert.</param>
        /// <returns>A comparable finding record.</returns>
        private static ComparableRecord ToComparableFinding(FindingRecord finding)
        {
            // Findings use their stable finding identity for this slice; history keys remain available on finding APIs for cross-snapshot grouping.
            return new ComparableRecord(
                finding.StableKey.Value,
                finding.Title,
                finding.RuleCode,
                finding.Fingerprint.Value,
                finding.EvidenceStableKeys.Select(static stableKey => stableKey.Value).ToArray(),
                ToUnknownSnapshotState(finding.UnknownState));
        }

        /// <summary>
        /// Converts a domain metric into a comparable diff record.
        /// </summary>
        /// <param name="metric">The metric to convert.</param>
        /// <returns>A comparable metric record.</returns>
        private static ComparableRecord ToComparableMetric(MetricRecord metric)
        {
            // Metrics expose kind as the domain-specific kind and retain primary evidence when the metric has a directly supporting fact.
            return new ComparableRecord(
                metric.StableKey.Value,
                metric.Name,
                metric.MetricKind,
                metric.Fingerprint.Value,
                ToEvidenceKeys(metric.PrimaryEvidenceStableKey),
                ToUnknownSnapshotState(metric.UnknownState));
        }

        /// <summary>
        /// Converts an optional stable key into deterministic evidence-key output.
        /// </summary>
        /// <param name="stableKey">The optional evidence stable key.</param>
        /// <returns>A stable-key list containing the evidence key when present.</returns>
        private static IReadOnlyList<string> ToEvidenceKeys(StableKey? stableKey)
        {
            // Null evidence is represented as an empty list so API consumers never see placeholder IDs.
            return stableKey.HasValue ? [stableKey.Value.Value] : [];
        }

        /// <summary>
        /// Converts domain unknown state into the internal comparable unknown-state shape.
        /// </summary>
        /// <param name="unknownState">The domain unknown-state value.</param>
        /// <returns>The comparable unknown-state value.</returns>
        private static UnknownSnapshotState ToUnknownSnapshotState(UnknownState unknownState)
        {
            // Diff output preserves explicit unknown-state semantics from the representative compared record.
            return new UnknownSnapshotState(unknownState.HasUnknownData, unknownState.UnknownReason);
        }

        /// <summary>
        /// Gets deterministic ordering for supported diff domains.
        /// </summary>
        /// <param name="domain">The domain to order.</param>
        /// <returns>The deterministic order index.</returns>
        private static int DomainOrder(string domain)
        {
            // Explicit ordering prevents incidental alphabetical changes from reshaping API responses.
            return domain switch
            {
                SnapshotDiffDomains.Nodes => 0,
                SnapshotDiffDomains.Edges => 1,
                SnapshotDiffDomains.Findings => 2,
                SnapshotDiffDomains.Metrics => 3,
                _ => 99
            };
        }

        /// <summary>
        /// Gets deterministic ordering for supported change kinds.
        /// </summary>
        /// <param name="changeKind">The change kind to order.</param>
        /// <returns>The deterministic order index.</returns>
        private static int ChangeKindOrder(string changeKind)
        {
            // Added and removed rows are listed before changed and unchanged rows because they are usually the most actionable drift.
            return changeKind switch
            {
                SnapshotDiffChangeKind.Added => 0,
                SnapshotDiffChangeKind.Removed => 1,
                SnapshotDiffChangeKind.Changed => 2,
                SnapshotDiffChangeKind.Unchanged => 3,
                _ => 99
            };
        }

        /// <summary>
        /// Represents a normalized record shape shared by all diff domains.
        /// </summary>
        /// <param name="StableKey">The stable public identity used for cross-snapshot matching.</param>
        /// <param name="DisplayName">The optional human-readable display text.</param>
        /// <param name="Kind">The domain-specific kind.</param>
        /// <param name="Fingerprint">The normalized fingerprint used for content comparison.</param>
        /// <param name="EvidenceStableKeys">Stable evidence references for contributor navigation.</param>
        /// <param name="UnknownState">The explicit unknown-state context carried by the source record.</param>
        private sealed record ComparableRecord(
            string StableKey,
            string? DisplayName,
            string Kind,
            string Fingerprint,
            IReadOnlyList<string> EvidenceStableKeys,
            UnknownSnapshotState UnknownState);

        /// <summary>
        /// Represents unknown-state fields in the comparable diff record shape.
        /// </summary>
        /// <param name="HasUnknownData">Indicates whether the compared record has explicit unknown data.</param>
        /// <param name="UnknownReason">The optional unknown-state reason.</param>
        private sealed record UnknownSnapshotState(bool HasUnknownData, string? UnknownReason)
        {
            /// <summary>
            /// Gets the known-state value used when a record carries no unknown context.
            /// </summary>
            public static UnknownSnapshotState Known { get; } = new(false, null);

            /// <summary>
            /// Creates a deterministic comparison string for changed-field summaries.
            /// </summary>
            /// <returns>A normalized unknown-state comparison string.</returns>
            public string ToComparisonString()
            {
                // Combining the flag and reason lets changed-field summaries detect both presence and explanation changes.
                return HasUnknownData.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (UnknownReason ?? string.Empty);
            }
        }
    }
}
