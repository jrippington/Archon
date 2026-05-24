using Archon.Domain.Graph.Model;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Stores WP012 findings, history, and suppression overlays in memory for tests and default composition without an external database.
    /// </summary>
    public sealed class InMemoryFindingStore : IFindingStore
    {
        /// <summary>
        /// Stores snapshot-owned finding records by snapshot stable key and finding stable key.
        /// </summary>
        private readonly Dictionary<string, FindingRecord> _findings = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores suppression requests by finding history key so later snapshots can inherit suppressions.
        /// </summary>
        private readonly Dictionary<string, SuppressFindingRequest> _suppressions = new(StringComparer.Ordinal);

        /// <summary>
        /// Applies suppression overlays to findings when persisted or when explicit suppression requests are saved.
        /// </summary>
        private readonly FindingConstructionService _findingConstructionService = new();

        /// <summary>
        /// Protects in-memory dictionaries from concurrent extraction or query access.
        /// </summary>
        private readonly object _syncRoot = new();

        /// <summary>
        /// Upserts snapshot-owned findings and applies any matching durable suppressions.
        /// </summary>
        /// <param name="findings">The findings to persist or update.</param>
        /// <param name="cancellationToken">The cancellation token that can stop persistence before the in-memory store is updated.</param>
        /// <returns>A successful result with the number of finding records offered to the store.</returns>
        public Task<FindingUpsertResult> UpsertFindingsAsync(IEnumerable<FindingRecord> findings, CancellationToken cancellationToken)
        {
            // The in-memory store mirrors Neo4j identity: snapshot stable key plus finding stable key replaces equivalent records without deleting others.
            ArgumentNullException.ThrowIfNull(findings);
            cancellationToken.ThrowIfCancellationRequested();
            FindingRecord[] entries = findings.ToArray();
            lock (_syncRoot)
            {
                foreach (FindingRecord finding in entries)
                {
                    FindingRecord storedFinding = _findingConstructionService.ApplySuppression(finding, _suppressions.Values).Finding;
                    _findings[BuildSnapshotFindingKey(storedFinding.SnapshotStableKey.Value, storedFinding.StableKey.Value)] = storedFinding;
                }
            }

            return Task.FromResult(FindingUpsertResult.Success(entries.Length));
        }

        /// <summary>
        /// Retrieves persisted findings for one snapshot in deterministic stable-key order.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key whose findings should be retrieved.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before the in-memory snapshot is copied.</param>
        /// <returns>The persisted findings for the requested snapshot.</returns>
        public Task<IReadOnlyList<FindingRecord>> GetFindingsBySnapshotAsync(string snapshotStableKey, CancellationToken cancellationToken)
        {
            // Retrieval returns a copied sorted snapshot so callers cannot observe dictionary mutation during concurrent writes.
            string normalizedSnapshotKey = RequireText(snapshotStableKey, nameof(snapshotStableKey));
            cancellationToken.ThrowIfCancellationRequested();
            lock (_syncRoot)
            {
                IReadOnlyList<FindingRecord> result = _findings.Values
                    .Where(finding => StringComparer.Ordinal.Equals(finding.SnapshotStableKey.Value, normalizedSnapshotKey))
                    .OrderBy(static finding => finding.StableKey.Value, StringComparer.Ordinal)
                    .ToArray();
                return Task.FromResult(result);
            }
        }

        /// <summary>
        /// Retrieves a deterministic all-snapshot finding snapshot for diagnostics and in-memory query API tests.
        /// </summary>
        /// <returns>A copied in-memory snapshot sorted by snapshot stable key and finding stable key.</returns>
        internal IReadOnlyList<FindingRecord> GetFindingsSnapshotForDiagnostics()
        {
            // The diagnostic snapshot is internal so production API contracts still flow through controlled query services.
            lock (_syncRoot)
            {
                return _findings.Values
                    .OrderBy(static finding => finding.SnapshotStableKey.Value, StringComparer.Ordinal)
                    .ThenBy(static finding => finding.StableKey.Value, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        /// <summary>
        /// Retrieves a persisted finding by snapshot stable key and finding stable key.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the finding.</param>
        /// <param name="findingStableKey">The finding stable key to retrieve.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before the in-memory lookup starts.</param>
        /// <returns>The matching finding, or <see langword="null"/> when no finding exists.</returns>
        public Task<FindingRecord?> GetFindingAsync(string snapshotStableKey, string findingStableKey, CancellationToken cancellationToken)
        {
            // The lookup key matches persistence identity and does not depend on dictionary enumeration order.
            string key = BuildSnapshotFindingKey(RequireText(snapshotStableKey, nameof(snapshotStableKey)), RequireText(findingStableKey, nameof(findingStableKey)));
            cancellationToken.ThrowIfCancellationRequested();
            lock (_syncRoot)
            {
                _findings.TryGetValue(key, out FindingRecord? finding);
                return Task.FromResult(finding);
            }
        }

        /// <summary>
        /// Retrieves cross-snapshot history seeds for the requested finding history keys.
        /// </summary>
        /// <param name="historyKeys">The deterministic finding history keys to resolve.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before the in-memory lookup starts.</param>
        /// <returns>The history seeds known to the store.</returns>
        public Task<IReadOnlyList<FindingHistorySeed>> GetHistoryAsync(IEnumerable<string> historyKeys, CancellationToken cancellationToken)
        {
            // History is derived from persisted findings so first/latest seen data stays aligned with stored records.
            ArgumentNullException.ThrowIfNull(historyKeys);
            cancellationToken.ThrowIfCancellationRequested();
            HashSet<string> requestedKeys = historyKeys.Where(static key => !string.IsNullOrWhiteSpace(key)).Select(static key => key.Trim()).ToHashSet(StringComparer.Ordinal);
            lock (_syncRoot)
            {
                IReadOnlyList<FindingHistorySeed> seeds = _findings.Values
                    .Where(finding => requestedKeys.Contains(finding.HistoryKey))
                    .GroupBy(static finding => finding.HistoryKey, StringComparer.Ordinal)
                    .Select(static group => new FindingHistorySeed(
                        group.Key,
                        group.Select(static finding => finding.FirstSeenSnapshotStableKey?.Value ?? finding.SnapshotStableKey.Value).OrderBy(static key => key, StringComparer.Ordinal).First(),
                        group.Select(static finding => finding.LatestSeenSnapshotStableKey?.Value ?? finding.SnapshotStableKey.Value).OrderByDescending(static key => key, StringComparer.Ordinal).First()))
                    .OrderBy(static seed => seed.HistoryKey, StringComparer.Ordinal)
                    .ToArray();
                return Task.FromResult(seeds);
            }
        }

        /// <summary>
        /// Persists suppression requests and applies matching suppressions to already stored findings.
        /// </summary>
        /// <param name="suppressionRequests">The suppression requests to persist and apply.</param>
        /// <param name="cancellationToken">The cancellation token that can stop suppression before the in-memory store is updated.</param>
        /// <returns>A result describing the suppression outcome.</returns>
        public Task<SuppressionPersistenceResult> SuppressFindingsAsync(IEnumerable<SuppressFindingRequest> suppressionRequests, CancellationToken cancellationToken)
        {
            // Suppression records are retained by history key so later equivalent findings inherit the same suppression overlay on upsert.
            ArgumentNullException.ThrowIfNull(suppressionRequests);
            cancellationToken.ThrowIfCancellationRequested();
            SuppressFindingRequest[] requests = suppressionRequests.ToArray();
            List<SuppressFindingValidationError> validationErrors = [];
            int suppressedCount = 0;
            lock (_syncRoot)
            {
                foreach (SuppressFindingRequest request in requests)
                {
                    FindingRecord? firstMatchingFinding = _findings.Values
                        .OrderBy(static finding => finding.StableKey.Value, StringComparer.Ordinal)
                        .FirstOrDefault(finding => SuppressionTargetsFinding(finding, request));
                    IReadOnlyList<SuppressFindingValidationError> requestValidationErrors = firstMatchingFinding is null
                        ? _findingConstructionService.ValidateSuppressionRequest(request)
                        : _findingConstructionService.ApplySuppression(firstMatchingFinding, [request]).ValidationErrors;
                    if (requestValidationErrors.Count > 0)
                    {
                        validationErrors.AddRange(requestValidationErrors);
                        continue;
                    }

                    if (firstMatchingFinding is null)
                    {
                        _suppressions[request.FindingHistoryKey] = request;
                        continue;
                    }

                    _suppressions[request.FindingHistoryKey] = request;
                    foreach (FindingRecord finding in _findings.Values.Where(finding => SuppressionTargetsFinding(finding, request)).ToArray())
                    {
                        FindingRecord suppressedFinding = _findingConstructionService.ApplySuppression(finding, [request]).Finding;
                        _findings[BuildSnapshotFindingKey(suppressedFinding.SnapshotStableKey.Value, suppressedFinding.StableKey.Value)] = suppressedFinding;
                        suppressedCount++;
                    }
                }
            }

            if (validationErrors.Count > 0)
            {
                return Task.FromResult(SuppressionPersistenceResult.ValidationFailure(validationErrors));
            }

            return Task.FromResult(SuppressionPersistenceResult.Success(suppressedCount));
        }

        /// <summary>
        /// Determines whether a suppression request targets a stored finding.
        /// </summary>
        /// <param name="finding">The stored finding to inspect.</param>
        /// <param name="suppression">The suppression request being matched.</param>
        /// <returns><see langword="true"/> when the suppression targets the finding; otherwise, <see langword="false"/>.</returns>
        private static bool SuppressionTargetsFinding(FindingRecord finding, SuppressFindingRequest suppression)
        {
            // Matching mirrors FindingConstructionService so persisted and construction-time suppression behavior remain aligned.
            return StringComparer.Ordinal.Equals(finding.HistoryKey, suppression.FindingHistoryKey)
                && StringComparer.Ordinal.Equals(finding.RuleCode, suppression.RuleCode)
                && StringComparer.Ordinal.Equals(finding.RuleVersion, suppression.RuleVersion)
                && StringComparer.Ordinal.Equals(finding.PrimaryNodeStableKey?.Value, suppression.PrimaryNodeStableKey);
        }

        /// <summary>
        /// Builds the private composite identity used by the in-memory finding dictionary.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the finding.</param>
        /// <param name="findingStableKey">The finding stable key.</param>
        /// <returns>A deterministic composite key for in-memory replacement.</returns>
        private static string BuildSnapshotFindingKey(string snapshotStableKey, string findingStableKey)
        {
            // The separator is private to the store; external contracts continue to expose snapshot and finding stable keys separately.
            return string.Concat(snapshotStableKey, "\u001F", findingStableKey);
        }

        /// <summary>
        /// Requires a non-empty text value and returns its trimmed form.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <param name="parameterName">The parameter name used for validation failures.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Store lookups require explicit stable identities to avoid ambiguous results.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }
}
