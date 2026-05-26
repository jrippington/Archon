using Archon.Application.Diff;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Application.Hotspots;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Model;
using System.Text.Json;

namespace Archon.Application.Dashboard
{
    /// <summary>
    /// Implements deterministic dashboard summary query behavior over persisted architecture snapshots.
    /// </summary>
    public sealed class DashboardSummaryQueryService : IDashboardSummaryQueryService
    {
        /// <summary>
        /// Defines the maximum number of top hotspot rows included in the summary envelope.
        /// </summary>
        private const int TopHotspotLimit = 5;

        /// <summary>
        /// Reads snapshots from the registered architecture snapshot writer when in-memory diagnostics are available.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Detects deterministic architecture hotspots from selected snapshot facts.
        /// </summary>
        private readonly HotspotDetector _hotspotDetector;

        /// <summary>
        /// Stores the threshold policy used for dashboard hotspot summaries.
        /// </summary>
        private readonly HotspotThresholds _hotspotThresholds;

        /// <summary>
        /// Initializes a new instance of the <see cref="DashboardSummaryQueryService"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        public DashboardSummaryQueryService(IArchitectureSnapshotWriter snapshotWriter)
            : this(snapshotWriter, HotspotThresholds.Default)
        {
            // The default constructor uses the same hotspot thresholds as the dedicated hotspot query endpoint.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DashboardSummaryQueryService"/> class with explicit hotspot thresholds.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        /// <param name="hotspotThresholds">The threshold policy used when producing top-hotspot summary rows.</param>
        public DashboardSummaryQueryService(IArchitectureSnapshotWriter snapshotWriter, HotspotThresholds hotspotThresholds)
        {
            // Dashboard summaries read already persisted application snapshot contracts and never expose Neo4j implementation details.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
            _hotspotThresholds = hotspotThresholds ?? throw new ArgumentNullException(nameof(hotspotThresholds));
            _hotspotDetector = new HotspotDetector();
        }

        /// <summary>
        /// Retrieves a deterministic dashboard summary for the selected repository, solution, and snapshot scope.
        /// </summary>
        /// <param name="selector">The caller-supplied dashboard snapshot selector.</param>
        /// <param name="cancellationToken">The token that can cancel query work before snapshot facts are read.</param>
        /// <returns>A successful dashboard summary or deterministic validation errors.</returns>
        public Task<DashboardSummaryResult> GetDashboardSummaryAsync(DashboardSnapshotSelector selector, CancellationToken cancellationToken)
        {
            // Validation is performed before summary construction so API callers receive safe, client-correctable problem details.
            ArgumentNullException.ThrowIfNull(selector);
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ExtractedArchitectureSnapshot> snapshots = GetSnapshots();
            List<DashboardSummaryValidationError> validationErrors = ValidateSelector(selector);
            if (validationErrors.Count > 0)
            {
                return Task.FromResult(new DashboardSummaryResult(validationErrors));
            }

            ExtractedArchitectureSnapshot[] repositorySnapshots = snapshots
                .Where(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.RepositoryStableKey.Value, selector.RepositoryStableKey))
                .ToArray();
            if (repositorySnapshots.Length == 0)
            {
                DashboardSummaryValidationError error = new(DashboardSummaryValidationCodes.RepositoryNotFound, "The requested repository scope was not found.");
                return Task.FromResult(new DashboardSummaryResult([error]));
            }

            ExtractedArchitectureSnapshot[] scopedSnapshots = ApplySolutionScope(repositorySnapshots, selector);
            if (selector.SolutionStableKey is not null && scopedSnapshots.Length == 0)
            {
                DashboardSummaryValidationError error = new(DashboardSummaryValidationCodes.SolutionNotFound, "The requested solution scope was not found for the repository.");
                return Task.FromResult(new DashboardSummaryResult([error]));
            }

            ExtractedArchitectureSnapshot? selectedSnapshot = ResolveSnapshot(scopedSnapshots, selector);
            if (selectedSnapshot?.SnapshotHeader is null)
            {
                DashboardSummaryValidationError error = new(DashboardSummaryValidationCodes.SnapshotNotFound, "The requested snapshot scope was not found.");
                return Task.FromResult(new DashboardSummaryResult([error]));
            }

            DashboardSummaryDto summary = BuildSummary(selector, selectedSnapshot, scopedSnapshots);
            return Task.FromResult(new DashboardSummaryResult(summary));
        }

        /// <summary>
        /// Reads snapshots from the in-memory fallback writer when that diagnostic path is available.
        /// </summary>
        /// <returns>The snapshots available to application-layer query services.</returns>
        private IReadOnlyList<ExtractedArchitectureSnapshot> GetSnapshots()
        {
            // Infrastructure-backed stores can replace this service later; the first WP014 slice uses the same testable in-memory seam as WP013.
            return _snapshotWriter is InMemoryArchitectureSnapshotWriter writer
                ? writer.GetSnapshotsSnapshotForDiagnostics()
                : [];
        }

        /// <summary>
        /// Validates selector syntax before any snapshot matching occurs.
        /// </summary>
        /// <param name="selector">The caller-supplied dashboard snapshot selector.</param>
        /// <returns>A deterministic list of syntax validation errors.</returns>
        private static List<DashboardSummaryValidationError> ValidateSelector(DashboardSnapshotSelector selector)
        {
            // The repository scope is required because latest snapshot resolution must be bounded to one repository.
            List<DashboardSummaryValidationError> errors = [];
            if (selector.RepositoryStableKey is null)
            {
                errors.Add(new DashboardSummaryValidationError(DashboardSummaryValidationCodes.RepositoryStableKeyRequired, "A repository stable key is required for dashboard summary."));
            }

            if (selector.SnapshotStableKey is not null
                && !selector.RequestsLatestSnapshot
                && !selector.SnapshotStableKey.StartsWith("snapshot://", StringComparison.Ordinal))
            {
                errors.Add(new DashboardSummaryValidationError(DashboardSummaryValidationCodes.SnapshotSelectorInvalid, "Snapshot selector must be 'latest' or a snapshot:// stable key."));
            }

            return errors;
        }

        /// <summary>
        /// Applies the optional solution scope to repository snapshots.
        /// </summary>
        /// <param name="repositorySnapshots">The snapshots already matched to the requested repository.</param>
        /// <param name="selector">The caller-supplied dashboard snapshot selector.</param>
        /// <returns>The snapshots matching the optional solution scope.</returns>
        private static ExtractedArchitectureSnapshot[] ApplySolutionScope(IEnumerable<ExtractedArchitectureSnapshot> repositorySnapshots, DashboardSnapshotSelector selector)
        {
            // Solution scope is matched through stable solution identity, not solution display name or Neo4j IDs.
            return selector.SolutionStableKey is null
                ? repositorySnapshots.ToArray()
                : repositorySnapshots
                    .Where(snapshot => snapshot.Solutions.Any(solution => StringComparer.Ordinal.Equals(solution.StableKey.Value, selector.SolutionStableKey)))
                    .ToArray();
        }

        /// <summary>
        /// Resolves the selected snapshot from an already scoped snapshot set.
        /// </summary>
        /// <param name="scopedSnapshots">The repository and solution scoped snapshots.</param>
        /// <param name="selector">The caller-supplied dashboard snapshot selector.</param>
        /// <returns>The selected snapshot, or null when none matches.</returns>
        private static ExtractedArchitectureSnapshot? ResolveSnapshot(IEnumerable<ExtractedArchitectureSnapshot> scopedSnapshots, DashboardSnapshotSelector selector)
        {
            // Latest resolution uses completed time, started time, then stable key so repeated calls remain deterministic.
            return selector.RequestsLatestSnapshot
                ? scopedSnapshots
                    .Where(static snapshot => snapshot.SnapshotHeader is not null)
                    .OrderByDescending(static snapshot => snapshot.SnapshotHeader!.CompletedUtc ?? snapshot.SnapshotHeader.StartedUtc)
                    .ThenByDescending(static snapshot => snapshot.SnapshotHeader!.StartedUtc)
                    .ThenByDescending(static snapshot => snapshot.SnapshotHeader!.StableKey.Value, StringComparer.Ordinal)
                    .FirstOrDefault()
                : scopedSnapshots.FirstOrDefault(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.StableKey.Value, selector.SnapshotStableKey));
        }

        /// <summary>
        /// Builds the successful dashboard summary DTO from the resolved snapshot.
        /// </summary>
        /// <param name="selector">The caller-supplied dashboard snapshot selector.</param>
        /// <param name="selectedSnapshot">The resolved snapshot used for counts and hotspot summaries.</param>
        /// <param name="scopedSnapshots">All snapshots in the selected repository and solution scope.</param>
        /// <returns>The successful dashboard summary DTO.</returns>
        private DashboardSummaryDto BuildSummary(DashboardSnapshotSelector selector, ExtractedArchitectureSnapshot selectedSnapshot, IReadOnlyList<ExtractedArchitectureSnapshot> scopedSnapshots)
        {
            // Summary construction keeps each section explicit so missing optional sections can produce warnings and unknowns.
            RepositoryModel? repository = selectedSnapshot.Repositories.FirstOrDefault(repository => StringComparer.Ordinal.Equals(repository.StableKey.Value, selector.RepositoryStableKey));
            SolutionModel? solution = selector.SolutionStableKey is null
                ? selectedSnapshot.Solutions.OrderBy(static candidate => candidate.StableKey.Value, StringComparer.Ordinal).FirstOrDefault()
                : selectedSnapshot.Solutions.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.StableKey.Value, selector.SolutionStableKey));
            ArchitectureNode[] scopedNodes = GetScopedNodes(selectedSnapshot, solution?.StableKey.Value);
            DashboardCountSummaryDto counts = BuildCounts(scopedNodes, selectedSnapshot);
            List<DashboardWarningDto> warnings = [];
            List<DashboardUnknownDto> unknowns = [];
            DashboardHotspotSummaryDto[] topHotspots = BuildTopHotspots(selectedSnapshot, scopedNodes, warnings, unknowns);
            DashboardLatestChangeSummaryDto[] latestChanges = BuildLatestChanges(selectedSnapshot, scopedSnapshots, warnings, unknowns);
            DashboardSnapshotMetadataDto snapshotMetadata = new(
                selectedSnapshot.SnapshotHeader!.StableKey.Value,
                selector.SnapshotStableKey ?? DashboardSnapshotSelector.LatestSnapshotSelector,
                selector.RequestsLatestSnapshot,
                selectedSnapshot.SnapshotHeader.CommitSha,
                selectedSnapshot.SnapshotHeader.StartedUtc,
                selectedSnapshot.SnapshotHeader.CompletedUtc,
                selectedSnapshot.SnapshotHeader.Status);
            DashboardScopeDto scope = new(selector.RepositoryStableKey, repository?.Name, solution?.StableKey.Value, solution?.Name);
            AddSnapshotDiagnostics(selectedSnapshot, warnings);
            return new DashboardSummaryDto(scope, snapshotMetadata, counts, topHotspots, latestChanges, warnings, unknowns);
        }

        /// <summary>
        /// Selects nodes that belong to the optional solution scope.
        /// </summary>
        /// <param name="snapshot">The selected snapshot.</param>
        /// <param name="solutionStableKey">The optional solution stable key.</param>
        /// <returns>The scoped architecture nodes used by dashboard counts.</returns>
        private static ArchitectureNode[] GetScopedNodes(ExtractedArchitectureSnapshot snapshot, string? solutionStableKey)
        {
            // Current project nodes may not carry direct solution membership metadata, so the first slice treats a matched solution as a snapshot-level boundary.
            _ = solutionStableKey;
            return snapshot.Nodes.ToArray();
        }

        /// <summary>
        /// Builds deterministic dashboard count summaries from selected snapshot facts.
        /// </summary>
        /// <param name="scopedNodes">The architecture nodes included in the selected scope.</param>
        /// <param name="snapshot">The selected snapshot.</param>
        /// <returns>The deterministic dashboard count summary.</returns>
        private static DashboardCountSummaryDto BuildCounts(IReadOnlyList<ArchitectureNode> scopedNodes, ExtractedArchitectureSnapshot snapshot)
        {
            // Counts use normalized node kinds and safe metadata rather than persistence-local labels or database IDs.
            int projectCount = scopedNodes.Count(node => node.NodeKind == NodeKind.Project);
            int cSharpProjectCount = scopedNodes.Count(node => node.NodeKind == NodeKind.Project && IsLanguage(node, "C#"));
            int visualBasicProjectCount = scopedNodes.Count(node => node.NodeKind == NodeKind.Project && (IsLanguage(node, "VB") || IsLanguage(node, "Visual Basic") || IsLanguage(node, "VB.NET")));
            int apiCount = scopedNodes.Count(node => node.NodeKind == NodeKind.Project && HasMetadataValue(node, "application.type", "Api"));
            int workerCount = scopedNodes.Count(node => node.NodeKind == NodeKind.Project && (HasMetadataValue(node, "application.type", "Worker") || HasMetadataValue(node, "runtime.hostedService", true)));
            int endpointCount = scopedNodes.Count(node => node.NodeKind == NodeKind.Endpoint);
            int dataContextCount = scopedNodes.Count(node => node.NodeKind == NodeKind.DbContext || node.NodeKind == NodeKind.LinqToSqlDataContext);
            int hotlistFindingCount = snapshot.Findings.Count;
            return new DashboardCountSummaryDto(projectCount, cSharpProjectCount, visualBasicProjectCount, apiCount, workerCount, endpointCount, dataContextCount, hotlistFindingCount);
        }

        /// <summary>
        /// Determines whether a node language matches a requested dashboard language category.
        /// </summary>
        /// <param name="node">The architecture node to inspect.</param>
        /// <param name="language">The language text to match.</param>
        /// <returns><see langword="true"/> when the node language matches the requested value; otherwise, <see langword="false"/>.</returns>
        private static bool IsLanguage(ArchitectureNode node, string language)
        {
            // Language comparisons are ordinal-ignore-case because extraction stages may use display casing such as C# or csharp.
            return string.Equals(node.Language, language, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a node metadata value matches a requested dashboard value.
        /// </summary>
        /// <param name="node">The architecture node to inspect.</param>
        /// <param name="metadataKey">The metadata key to read.</param>
        /// <param name="expectedValue">The expected metadata value.</param>
        /// <returns><see langword="true"/> when the metadata value matches; otherwise, <see langword="false"/>.</returns>
        private static bool HasMetadataValue(ArchitectureNode node, string metadataKey, object expectedValue)
        {
            // Metadata is used only for supplemental classifications; normalized node kinds remain the primary count source.
            using JsonDocument document = JsonDocument.Parse(node.Metadata.ToCanonicalJson());
            return document.RootElement.TryGetProperty(metadataKey, out JsonElement actualValue)
                && MetadataValueEquals(actualValue, expectedValue);
        }

        /// <summary>
        /// Compares a canonical JSON metadata value with a requested dashboard value.
        /// </summary>
        /// <param name="actualValue">The metadata JSON value read from canonical metadata.</param>
        /// <param name="expectedValue">The expected CLR value.</param>
        /// <returns><see langword="true"/> when the values match for dashboard classification; otherwise, <see langword="false"/>.</returns>
        private static bool MetadataValueEquals(JsonElement actualValue, object expectedValue)
        {
            // JSON parsing keeps the dashboard service independent of GraphMetadata internals while supporting string and boolean classifications.
            if (expectedValue is bool expectedBoolean && actualValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return actualValue.GetBoolean() == expectedBoolean;
            }

            string? actualText = actualValue.ValueKind == JsonValueKind.String
                ? actualValue.GetString()
                : actualValue.ToString();
            string? expectedText = Convert.ToString(expectedValue, System.Globalization.CultureInfo.InvariantCulture);
            return string.Equals(actualText, expectedText, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds compact top-hotspot summary rows for the selected snapshot.
        /// </summary>
        /// <param name="snapshot">The selected snapshot.</param>
        /// <param name="scopedNodes">The architecture nodes included in the selected scope.</param>
        /// <param name="warnings">The mutable warning list for missing optional hotspot data.</param>
        /// <param name="unknowns">The mutable unknown list for missing optional hotspot data.</param>
        /// <returns>The compact top-hotspot summary rows.</returns>
        private DashboardHotspotSummaryDto[] BuildTopHotspots(ExtractedArchitectureSnapshot snapshot, IReadOnlyList<ArchitectureNode> scopedNodes, List<DashboardWarningDto> warnings, List<DashboardUnknownDto> unknowns)
        {
            // Hotspots are optional summary enrichment, so absent metric or finding inputs produce explicit warnings instead of disappearing.
            if (snapshot.Metrics.Count == 0 && snapshot.Findings.Count == 0)
            {
                AddMissingOptionalData(warnings, unknowns, "topHotspots", "No persisted metrics or findings were available to calculate top hotspots.");
                return [];
            }

            HashSet<string> scopedNodeKeys = new(scopedNodes.Select(static node => node.StableKey.Value), StringComparer.Ordinal);
            return _hotspotDetector.DetectHotspots(snapshot, _hotspotThresholds)
                .Where(hotspot => scopedNodeKeys.Count == 0 || scopedNodeKeys.Contains(hotspot.TargetStableKey.Value) || hotspot.TargetStableKey.Value == snapshot.SnapshotHeader?.StableKey.Value)
                .OrderByDescending(static hotspot => hotspot.Score)
                .ThenBy(static hotspot => hotspot.Category, StringComparer.Ordinal)
                .ThenBy(static hotspot => hotspot.TargetStableKey.Value, StringComparer.Ordinal)
                .ThenBy(static hotspot => hotspot.StableKey.Value, StringComparer.Ordinal)
                .Take(TopHotspotLimit)
                .Select(static hotspot => new DashboardHotspotSummaryDto(
                    hotspot.StableKey.Value,
                    hotspot.Category,
                    hotspot.TargetStableKey.Value,
                    hotspot.TargetKind,
                    hotspot.DisplayName,
                    hotspot.Score,
                    hotspot.Rank,
                    hotspot.Confidence.Value,
                    hotspot.UnknownState.HasUnknownData,
                    hotspot.UnknownState.UnknownReason))
                .ToArray();
        }

        /// <summary>
        /// Builds compact latest-change summary rows against the previous scoped snapshot when available.
        /// </summary>
        /// <param name="selectedSnapshot">The selected current snapshot.</param>
        /// <param name="scopedSnapshots">All snapshots in the selected repository and solution scope.</param>
        /// <param name="warnings">The mutable warning list for missing optional change data.</param>
        /// <param name="unknowns">The mutable unknown list for missing optional change data.</param>
        /// <returns>The compact latest-change summary rows.</returns>
        private static DashboardLatestChangeSummaryDto[] BuildLatestChanges(ExtractedArchitectureSnapshot selectedSnapshot, IReadOnlyList<ExtractedArchitectureSnapshot> scopedSnapshots, List<DashboardWarningDto> warnings, List<DashboardUnknownDto> unknowns)
        {
            // Latest changes are optional because a repository's first snapshot has no previous snapshot to compare.
            ExtractedArchitectureSnapshot? previousSnapshot = scopedSnapshots
                .Where(snapshot => snapshot.SnapshotHeader is not null)
                .Where(snapshot => !StringComparer.Ordinal.Equals(snapshot.SnapshotHeader!.StableKey.Value, selectedSnapshot.SnapshotHeader!.StableKey.Value))
                .Where(snapshot => (snapshot.SnapshotHeader!.CompletedUtc ?? snapshot.SnapshotHeader.StartedUtc) <= (selectedSnapshot.SnapshotHeader!.CompletedUtc ?? selectedSnapshot.SnapshotHeader.StartedUtc))
                .OrderByDescending(snapshot => snapshot.SnapshotHeader!.CompletedUtc ?? snapshot.SnapshotHeader.StartedUtc)
                .ThenByDescending(snapshot => snapshot.SnapshotHeader!.StableKey.Value, StringComparer.Ordinal)
                .FirstOrDefault();
            if (previousSnapshot is null)
            {
                AddMissingOptionalData(warnings, unknowns, "latestChanges", "No previous snapshot was available for latest-change comparison.");
                return [];
            }

            return
            [
                new DashboardLatestChangeSummaryDto(SnapshotDiffDomains.Nodes, SnapshotDiffChangeKind.Added, CountAdded(previousSnapshot.Nodes.Select(static node => node.StableKey.Value), selectedSnapshot.Nodes.Select(static node => node.StableKey.Value))),
                new DashboardLatestChangeSummaryDto(SnapshotDiffDomains.Nodes, SnapshotDiffChangeKind.Removed, CountRemoved(previousSnapshot.Nodes.Select(static node => node.StableKey.Value), selectedSnapshot.Nodes.Select(static node => node.StableKey.Value))),
                new DashboardLatestChangeSummaryDto(SnapshotDiffDomains.Edges, SnapshotDiffChangeKind.Added, CountAdded(previousSnapshot.Edges.Select(static edge => edge.StableKey.Value), selectedSnapshot.Edges.Select(static edge => edge.StableKey.Value))),
                new DashboardLatestChangeSummaryDto(SnapshotDiffDomains.Edges, SnapshotDiffChangeKind.Removed, CountRemoved(previousSnapshot.Edges.Select(static edge => edge.StableKey.Value), selectedSnapshot.Edges.Select(static edge => edge.StableKey.Value))),
                new DashboardLatestChangeSummaryDto(SnapshotDiffDomains.Findings, SnapshotDiffChangeKind.Added, CountAdded(previousSnapshot.Findings.Select(static finding => finding.StableKey.Value), selectedSnapshot.Findings.Select(static finding => finding.StableKey.Value))),
                new DashboardLatestChangeSummaryDto(SnapshotDiffDomains.Metrics, SnapshotDiffChangeKind.Added, CountAdded(previousSnapshot.Metrics.Select(static metric => metric.StableKey.Value), selectedSnapshot.Metrics.Select(static metric => metric.StableKey.Value)))
            ];
        }

        /// <summary>
        /// Counts identities present in the current snapshot but absent from the previous snapshot.
        /// </summary>
        /// <param name="previousStableKeys">The previous snapshot stable identities.</param>
        /// <param name="currentStableKeys">The current snapshot stable identities.</param>
        /// <returns>The number of added stable identities.</returns>
        private static int CountAdded(IEnumerable<string> previousStableKeys, IEnumerable<string> currentStableKeys)
        {
            // Set comparison mirrors snapshot diff identity semantics while keeping the dashboard summary compact.
            HashSet<string> previous = new(previousStableKeys, StringComparer.Ordinal);
            return currentStableKeys.Count(stableKey => !previous.Contains(stableKey));
        }

        /// <summary>
        /// Counts identities present in the previous snapshot but absent from the current snapshot.
        /// </summary>
        /// <param name="previousStableKeys">The previous snapshot stable identities.</param>
        /// <param name="currentStableKeys">The current snapshot stable identities.</param>
        /// <returns>The number of removed stable identities.</returns>
        private static int CountRemoved(IEnumerable<string> previousStableKeys, IEnumerable<string> currentStableKeys)
        {
            // Removed counts use the same stable-key-only identity comparison as added counts.
            HashSet<string> current = new(currentStableKeys, StringComparer.Ordinal);
            return previousStableKeys.Count(stableKey => !current.Contains(stableKey));
        }

        /// <summary>
        /// Adds a paired warning and unknown entry for optional dashboard data that is unavailable.
        /// </summary>
        /// <param name="warnings">The mutable warning list.</param>
        /// <param name="unknowns">The mutable unknown list.</param>
        /// <param name="field">The summary field whose optional data is unavailable.</param>
        /// <param name="reason">The safe reason explaining why the optional data is unavailable.</param>
        private static void AddMissingOptionalData(List<DashboardWarningDto> warnings, List<DashboardUnknownDto> unknowns, string field, string reason)
        {
            // Pairing warnings with field unknowns lets human and automated clients understand partial dashboard summaries consistently.
            warnings.Add(new DashboardWarningDto(field + "Unavailable", reason));
            unknowns.Add(new DashboardUnknownDto(field, reason));
        }

        /// <summary>
        /// Adds snapshot warnings and errors as safe dashboard warnings.
        /// </summary>
        /// <param name="snapshot">The selected snapshot.</param>
        /// <param name="warnings">The mutable warning list.</param>
        private static void AddSnapshotDiagnostics(ExtractedArchitectureSnapshot snapshot, List<DashboardWarningDto> warnings)
        {
            // Existing snapshot diagnostics are already safe application diagnostics and should remain visible to dashboard clients.
            foreach (string warning in snapshot.Warnings.Concat(snapshot.SnapshotHeader?.Warnings ?? []))
            {
                warnings.Add(new DashboardWarningDto("SnapshotWarning", warning));
            }

            foreach (string error in snapshot.Errors.Concat(snapshot.SnapshotHeader?.Errors ?? []))
            {
                warnings.Add(new DashboardWarningDto("SnapshotError", error));
            }
        }
    }
}