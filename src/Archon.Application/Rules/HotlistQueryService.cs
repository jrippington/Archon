using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Implements controlled WP012 rule catalog, hotlist, finding detail, finding history, and suppression query behavior.
    /// </summary>
    public sealed class HotlistQueryService : IHotlistQueryService
    {
        /// <summary>
        /// Executes controlled persistence-backed catalog and finding queries.
        /// </summary>
        private readonly IHotlistQueryStore _queryStore;

        /// <summary>
        /// Retrieves individual findings and applies suppression requests through the existing finding-store seam.
        /// </summary>
        private readonly IFindingStore _findingStore;

        /// <summary>
        /// Initializes a new instance of the <see cref="HotlistQueryService"/> class.
        /// </summary>
        /// <param name="queryStore">The controlled query store used for catalog and finding reads.</param>
        /// <param name="findingStore">The finding store used for detail retrieval and suppression writes.</param>
        public HotlistQueryService(IHotlistQueryStore queryStore, IFindingStore findingStore)
        {
            // Keeping query shaping in Application ensures API endpoints and future MCP consumers share one controlled DTO model.
            _queryStore = queryStore ?? throw new ArgumentNullException(nameof(queryStore));
            _findingStore = findingStore ?? throw new ArgumentNullException(nameof(findingStore));
        }

        /// <summary>
        /// Lists persisted rule catalog entries using controlled filters and bounded paging.
        /// </summary>
        /// <param name="query">The controlled catalog filter and paging contract.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>A page of stable rule catalog DTOs.</returns>
        public async Task<PagedQueryResult<RuleCatalogItemDto>> ListRulesAsync(RuleCatalogQuery query, CancellationToken cancellationToken)
        {
            // The store handles field filters and paging; the service maps persisted entries to public DTOs.
            ArgumentNullException.ThrowIfNull(query);
            PagedQueryResult<RuleCatalogEntry> result = await _queryStore.QueryRulesAsync(query, cancellationToken).ConfigureAwait(false);
            return new PagedQueryResult<RuleCatalogItemDto>(result.Items.Select(ToRuleItem), result.TotalCount, result.Skip, result.Take);
        }

        /// <summary>
        /// Retrieves one persisted rule detail by exact rule code and version.
        /// </summary>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="version">The exact rule version.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>The matching rule detail DTO, or <see langword="null"/> when none exists.</returns>
        public async Task<RuleDetailDto?> GetRuleAsync(string ruleCode, string version, CancellationToken cancellationToken)
        {
            // Detail lookup remains exact so later rule versions do not accidentally satisfy older finding explanations.
            RuleCatalogEntry? rule = await _queryStore.GetRuleAsync(RequireText(ruleCode, nameof(ruleCode)), RequireText(version, nameof(version)), cancellationToken).ConfigureAwait(false);
            return rule is null ? null : ToRuleDetail(rule);
        }

        /// <summary>
        /// Lists persisted findings using controlled hotlist filters and bounded paging.
        /// </summary>
        /// <param name="query">The controlled hotlist filter and paging contract.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>A page of stable hotlist item DTOs.</returns>
        public async Task<PagedQueryResult<HotlistItemDto>> ListHotlistAsync(HotlistQuery query, CancellationToken cancellationToken)
        {
            // The hotlist omits raw metadata and evidence snippets; detail callers receive safe metadata through a dedicated endpoint.
            ArgumentNullException.ThrowIfNull(query);
            PagedQueryResult<FindingRecord> result = await _queryStore.QueryFindingsAsync(query, cancellationToken).ConfigureAwait(false);
            return new PagedQueryResult<HotlistItemDto>(result.Items.Select(finding => ToHotlistItem(finding, category: null)), result.TotalCount, result.Skip, result.Take);
        }

        /// <summary>
        /// Retrieves one persisted finding detail by snapshot stable key and finding stable key.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the finding.</param>
        /// <param name="findingStableKey">The finding stable key.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>The matching finding detail DTO, or <see langword="null"/> when none exists.</returns>
        public async Task<FindingDetailDto?> GetFindingAsync(string snapshotStableKey, string findingStableKey, CancellationToken cancellationToken)
        {
            // IFindingStore already exposes exact snapshot-scoped lookup, which prevents ambiguous cross-snapshot finding detail responses.
            FindingRecord? finding = await _findingStore.GetFindingAsync(RequireText(snapshotStableKey, nameof(snapshotStableKey)), RequireText(findingStableKey, nameof(findingStableKey)), cancellationToken).ConfigureAwait(false);
            return finding is null ? null : ToFindingDetail(finding);
        }

        /// <summary>
        /// Retrieves cross-snapshot history for one finding history key.
        /// </summary>
        /// <param name="historyKey">The deterministic finding history key.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>The matching finding history DTO, or <see langword="null"/> when no history exists.</returns>
        public async Task<FindingHistoryDto?> GetFindingHistoryAsync(string historyKey, CancellationToken cancellationToken)
        {
            // History combines summary seeds from the finding store with concrete historical records when the adapter can provide them.
            string normalizedHistoryKey = RequireText(historyKey, nameof(historyKey));
            IReadOnlyList<FindingHistorySeed> seeds = await _findingStore.GetHistoryAsync([normalizedHistoryKey], cancellationToken).ConfigureAwait(false);
            FindingHistorySeed? seed = seeds.SingleOrDefault(seed => StringComparer.Ordinal.Equals(seed.HistoryKey, normalizedHistoryKey));
            IReadOnlyList<FindingRecord> records = await _queryStore.GetFindingHistoryRecordsAsync(normalizedHistoryKey, cancellationToken).ConfigureAwait(false);
            if (seed is null && records.Count == 0)
            {
                return null;
            }

            string firstSeen = seed?.FirstSeenSnapshotStableKey ?? records.Select(static finding => finding.FirstSeenSnapshotStableKey?.Value ?? finding.SnapshotStableKey.Value).OrderBy(static value => value, StringComparer.Ordinal).First();
            string latestSeen = seed?.LatestSeenSnapshotStableKey ?? records.Select(static finding => finding.LatestSeenSnapshotStableKey?.Value ?? finding.SnapshotStableKey.Value).OrderByDescending(static value => value, StringComparer.Ordinal).First();
            return new FindingHistoryDto(normalizedHistoryKey, firstSeen, latestSeen, records.Select(ToHistoryRecord));
        }

        /// <summary>
        /// Validates and persists a suppression overlay through the configured finding store.
        /// </summary>
        /// <param name="command">The suppression command supplied by the API boundary.</param>
        /// <param name="cancellationToken">The cancellation token that can stop validation or persistence before store work starts.</param>
        /// <returns>A command result describing success, validation errors, warnings, or persistence errors.</returns>
        public async Task<SuppressionCommandResult> SuppressFindingAsync(SuppressFindingCommand command, CancellationToken cancellationToken)
        {
            // The service performs validation through the existing suppression request type so API and persistence behavior stay aligned.
            ArgumentNullException.ThrowIfNull(command);
            SuppressFindingRequest request = new(
                command.FindingHistoryKey ?? string.Empty,
                command.RuleCode ?? string.Empty,
                command.RuleVersion ?? string.Empty,
                command.PrimaryNodeStableKey ?? string.Empty,
                command.Reason ?? string.Empty,
                command.SuppressedBy ?? string.Empty,
                command.Metadata);
            SuppressionPersistenceResult result = await _findingStore.SuppressFindingsAsync([request], cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                return SuppressionCommandResult.Success(result.SuppressedFindingCount, result.Warnings);
            }

            if (result.ValidationErrors.Count > 0)
            {
                return SuppressionCommandResult.ValidationFailure(result.ValidationErrors, result.Warnings);
            }

            return SuppressionCommandResult.Failure(result.Errors, result.Warnings);
        }

        /// <summary>
        /// Maps a persisted rule catalog entry to a public list item DTO.
        /// </summary>
        /// <param name="rule">The persisted rule catalog entry.</param>
        /// <returns>The stable rule catalog list item DTO.</returns>
        private static RuleCatalogItemDto ToRuleItem(RuleCatalogEntry rule)
        {
            // The item shape excludes raw definition JSON so catalog lists stay compact.
            return new RuleCatalogItemDto(
                rule.RuleCode,
                rule.Version,
                rule.Name,
                rule.Category.Value,
                rule.Severity.Value,
                rule.DefaultStatus.Value,
                rule.Enabled,
                rule.IsBuiltIn,
                rule.OwnerScope,
                rule.Description,
                rule.Tags);
        }

        /// <summary>
        /// Maps a persisted rule catalog entry to a public detail DTO.
        /// </summary>
        /// <param name="rule">The persisted rule catalog entry.</param>
        /// <returns>The stable rule detail DTO.</returns>
        private static RuleDetailDto ToRuleDetail(RuleCatalogEntry rule)
        {
            // Details expose authored rule explanation and data-only JSON while still omitting runtime source file paths.
            return new RuleDetailDto(
                ToRuleItem(rule),
                rule.Description,
                rule.DefinitionJson,
                rule.SourceUrls,
                rule.Impact,
                rule.EvidenceRequirements,
                rule.RecommendedActions,
                PublicMetadataSanitizer.Sanitize(rule.Metadata));
        }

        /// <summary>
        /// Maps a persisted finding to a public hotlist item DTO.
        /// </summary>
        /// <param name="finding">The persisted finding record.</param>
        /// <param name="category">The optional category resolved from the catalog.</param>
        /// <returns>The stable hotlist item DTO.</returns>
        private static HotlistItemDto ToHotlistItem(FindingRecord finding, string? category)
        {
            // Affected nodes and evidence are returned as references only, preventing direct graph traversal or snippet exposure.
            return new HotlistItemDto(
                finding.SnapshotStableKey.Value,
                finding.StableKey.Value,
                finding.HistoryKey,
                finding.RuleCode,
                finding.RuleVersion,
                finding.Title,
                finding.Description,
                finding.Severity.Value,
                finding.Status.Value,
                finding.Confidence.Value,
                category,
                finding.AffectedNodeStableKeys.Select(key => new AffectedNodeReferenceDto(key.Value, key.Value, null, ReadMetadataText(finding, "projectStableKey"))),
                finding.EvidenceStableKeys.Select(key => new FindingEvidenceReferenceDto(key.Value, key.Value)),
                finding.UnknownState.HasUnknownData,
                finding.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Maps a persisted finding to a public detail DTO.
        /// </summary>
        /// <param name="finding">The persisted finding record.</param>
        /// <returns>The stable finding detail DTO.</returns>
        private static FindingDetailDto ToFindingDetail(FindingRecord finding)
        {
            // Metadata is sanitized before it crosses the API boundary, while evidence remains reference-only.
            return new FindingDetailDto(
                ToHotlistItem(finding, category: null),
                finding.Description,
                finding.KnowledgeKind.Value,
                finding.PrimaryNodeStableKey?.Value,
                finding.PrimaryEvidenceStableKey?.Value,
                finding.FirstSeenSnapshotStableKey?.Value,
                finding.LatestSeenSnapshotStableKey?.Value,
                finding.SuppressionReason,
                finding.SuppressedBy,
                PublicMetadataSanitizer.Sanitize(finding.Metadata),
                finding.Fingerprint.Value);
        }

        /// <summary>
        /// Maps a persisted finding to a compact history record DTO.
        /// </summary>
        /// <param name="finding">The persisted finding record.</param>
        /// <returns>The compact history record DTO.</returns>
        private static FindingHistoryRecordDto ToHistoryRecord(FindingRecord finding)
        {
            // History records expose stable fields that let clients link back to detail endpoints for each snapshot record.
            return new FindingHistoryRecordDto(
                finding.SnapshotStableKey.Value,
                finding.StableKey.Value,
                finding.Status.Value,
                finding.Severity.Value,
                finding.Confidence.Value,
                finding.Fingerprint.Value);
        }

        /// <summary>
        /// Reads a string metadata value from a finding.
        /// </summary>
        /// <param name="finding">The finding containing metadata JSON.</param>
        /// <param name="metadataName">The lower camel case metadata property name to read.</param>
        /// <returns>The metadata string value, or <see langword="null"/> when the value is absent or not a string.</returns>
        private static string? ReadMetadataText(FindingRecord finding, string metadataName)
        {
            // Only a small set of stable metadata fields is read for presentation hints; callers cannot request arbitrary metadata paths.
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(finding.Metadata.ToCanonicalJson());
            return document.RootElement.TryGetProperty(metadataName, out System.Text.Json.JsonElement value) && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;
        }

        /// <summary>
        /// Requires a non-empty text value and returns its trimmed form.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <param name="parameterName">The parameter name used for validation failures.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Query lookups require explicit identities to avoid ambiguous not-found behavior.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }
}
