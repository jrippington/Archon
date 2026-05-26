using Archon.Application.Hotspots;
using Archon.Application.Rules;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpHotlist;
using ArchonMcp.McpRules;

namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Handles current snapshot, rules, hotlist, and hotspots MCP resources through approved query abstractions.
    /// </summary>
    public sealed class ArchonMcpCurrentResourceHandler : IArchonMcpCurrentResourceHandler
    {
        /// <summary>
        /// Resolves explicit current snapshot context before resource-specific query execution.
        /// </summary>
        private readonly IArchonMcpCurrentSnapshotProvider _currentSnapshotProvider;

        /// <summary>
        /// Executes rule catalog and hotlist finding queries through the application layer.
        /// </summary>
        private readonly IHotlistQueryService _hotlistQueryService;

        /// <summary>
        /// Executes hotspot queries through the application layer.
        /// </summary>
        private readonly IHotspotQueryService _hotspotQueryService;

        /// <summary>
        /// Applies configured MCP response limits to resource records.
        /// </summary>
        private readonly ArchonMcpLimitGuard _limitGuard;

        /// <summary>
        /// Maps safe evidence references for common MCP envelopes.
        /// </summary>
        private readonly IArchonMcpResponseMapper _responseMapper;

        /// <summary>
        /// Creates a current resource handler.
        /// </summary>
        /// <param name="currentSnapshotProvider">The provider that resolves explicit current snapshot scope.</param>
        /// <param name="hotlistQueryService">The query-layer rule and hotlist abstraction.</param>
        /// <param name="hotspotQueryService">The query-layer hotspot abstraction.</param>
        /// <param name="limitGuard">The guard that applies configured MCP response limits.</param>
        /// <param name="responseMapper">The mapper that creates safe common envelope references.</param>
        public ArchonMcpCurrentResourceHandler(
            IArchonMcpCurrentSnapshotProvider currentSnapshotProvider,
            IHotlistQueryService hotlistQueryService,
            IHotspotQueryService hotspotQueryService,
            ArchonMcpLimitGuard limitGuard,
            IArchonMcpResponseMapper responseMapper)
        {
            // Constructor injection keeps resource handling testable and prevents direct persistence or graph access from MCP code.
            _currentSnapshotProvider = currentSnapshotProvider ?? throw new ArgumentNullException(nameof(currentSnapshotProvider));
            _hotlistQueryService = hotlistQueryService ?? throw new ArgumentNullException(nameof(hotlistQueryService));
            _hotspotQueryService = hotspotQueryService ?? throw new ArgumentNullException(nameof(hotspotQueryService));
            _limitGuard = limitGuard ?? throw new ArgumentNullException(nameof(limitGuard));
            _responseMapper = responseMapper ?? throw new ArgumentNullException(nameof(responseMapper));
        }

        /// <inheritdoc />
        public async Task<object> ReadCurrentResourceAsync(ArchonMcpResourceRequest request, CancellationToken cancellationToken)
        {
            // Every current resource starts by resolving exactly one snapshot so downstream queries never infer ambiguous current state.
            ArgumentNullException.ThrowIfNull(request);
            string repositoryStableKey = request.RepositoryStableKey ?? throw new InvalidOperationException("Current resources require a repository stable key after URI validation.");
            ArchonMcpCurrentSnapshotResolution resolution = await _currentSnapshotProvider.ResolveCurrentSnapshotAsync(
                new ArchonMcpCurrentSnapshotRequest(repositoryStableKey, request.SolutionStableKey),
                cancellationToken).ConfigureAwait(false);
            if (resolution.Kind != ArchonMcpCurrentSnapshotResolutionKind.Success)
            {
                return MapResolutionFailure(resolution);
            }

            ArchonMcpCurrentSnapshotContext snapshot = resolution.Snapshot!;
            return request.Family switch
            {
                ArchonMcpResourceFamily.Snapshot => MapSnapshotResource(request, snapshot),
                ArchonMcpResourceFamily.Rules => await MapRulesResourceAsync(request, snapshot, cancellationToken).ConfigureAwait(false),
                ArchonMcpResourceFamily.Hotlist => await MapHotlistResourceAsync(request, snapshot, cancellationToken).ConfigureAwait(false),
                ArchonMcpResourceFamily.Hotspots => await MapHotspotsResourceAsync(request, snapshot, cancellationToken).ConfigureAwait(false),
                _ => ArchonMcpErrorResponse.Create(ArchonMcpResourceOperations.ReadResource, ArchonMcpErrorCategory.UnsupportedOperation, "The requested resource family is not supported.", null)
            };
        }

        /// <summary>
        /// Maps current snapshot resolution failures into structured MCP errors.
        /// </summary>
        /// <param name="resolution">The failed resolution to map.</param>
        /// <returns>A safe MCP error response.</returns>
        private static ArchonMcpErrorResponse MapResolutionFailure(ArchonMcpCurrentSnapshotResolution resolution)
        {
            // Current selection errors are coarse and stable, while ambiguity follow-ups include only safe snapshot stable keys.
            return resolution.Kind == ArchonMcpCurrentSnapshotResolutionKind.Ambiguous
                ? ArchonMcpErrorResponse.Create(
                    ArchonMcpResourceOperations.ReadResource,
                    ArchonMcpErrorCategory.Ambiguous,
                    resolution.Message ?? "Current snapshot selection is ambiguous for the requested scope.",
                    [new ArchonMcpSuggestedFollowUp($"Use an explicit snapshot resource in a later parameterized-resource workflow. Candidate snapshots: {string.Join(", ", resolution.CandidateSnapshotStableKeys)}.", "user.question", null)])
                : ArchonMcpErrorResponse.Create(
                    ArchonMcpResourceOperations.ReadResource,
                    ArchonMcpErrorCategory.NotFound,
                    resolution.Message ?? "No current snapshot matched the requested scope.",
                    [new ArchonMcpSuggestedFollowUp("Check the repository and optional solution stable keys before retrying the current resource.", "user.question", null)]);
        }

        /// <summary>
        /// Maps selected current snapshot context into the snapshot resource envelope.
        /// </summary>
        /// <param name="request">The validated resource request.</param>
        /// <param name="snapshot">The selected current snapshot context.</param>
        /// <returns>A current snapshot resource envelope.</returns>
        private static ArchonMcpEnvelope<ArchonMcpCurrentSnapshotResourceFacts> MapSnapshotResource(ArchonMcpResourceRequest request, ArchonMcpCurrentSnapshotContext snapshot)
        {
            // Snapshot facts summarize scope and counts without exposing repository root paths, remotes, or persistence-local identifiers.
            ArchonMcpCurrentSnapshotResourceFacts facts = new(
                request.CanonicalUri,
                snapshot.SnapshotStableKey,
                snapshot.RepositoryStableKey,
                snapshot.SolutionStableKeys,
                snapshot.BranchName,
                snapshot.CommitSha,
                snapshot.StartedUtc,
                snapshot.CompletedUtc,
                snapshot.Status,
                snapshot.NodeCount,
                snapshot.EdgeCount,
                snapshot.RuleCount,
                snapshot.FindingCount,
                snapshot.MetricCount,
                snapshot.EvidenceCount,
                snapshot.WarningCount,
                snapshot.ErrorCount);
            return new ArchonMcpEnvelope<ArchonMcpCurrentSnapshotResourceFacts>(
                ArchonMcpResourceOperations.ReadResource,
                CreateSnapshotIdentity(snapshot),
                $"Current snapshot {snapshot.SnapshotStableKey} contains {snapshot.NodeCount} nodes, {snapshot.EdgeCount} edges, {snapshot.FindingCount} findings, and {snapshot.EvidenceCount} evidence records.",
                new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Current snapshot context came from the controlled application snapshot seam."),
                facts,
                evidence: null,
                findings: null,
                unknowns: null,
                warnings: CreateSnapshotWarnings(snapshot),
                new ArchonMcpLimitMetadata(false, "snapshotCurrent", appliedLimit: 1, requestedLimit: request.Limit, originalCount: 1, returnedCount: 1, reason: null),
                CreateSnapshotFollowUps(snapshot));
        }

        /// <summary>
        /// Maps the current rules resource into an MCP envelope.
        /// </summary>
        /// <param name="request">The validated resource request.</param>
        /// <param name="snapshot">The selected current snapshot context.</param>
        /// <param name="cancellationToken">The token that can cancel query execution.</param>
        /// <returns>A rules current resource envelope or safe error response.</returns>
        private async Task<object> MapRulesResourceAsync(ArchonMcpResourceRequest request, ArchonMcpCurrentSnapshotContext snapshot, CancellationToken cancellationToken)
        {
            // Rule catalog data is global in the current query seam, but the resource still includes selected snapshot identity for client context.
            PagedQueryResult<RuleCatalogItemDto> page;
            try
            {
                page = await _hotlistQueryService.ListRulesAsync(new RuleCatalogQuery(ruleCode: null, version: null, request.Category, severity: null, enabled: null, builtIn: null, ownerScope: null, skip: 0, take: request.Limit.GetValueOrDefault(RuleCatalogQuery.DefaultPageSize)), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cooperative cancellation remains host behavior and should not be serialized as a resource failure.
                throw;
            }
            catch (Exception)
            {
                // Public errors omit query exception details because rule stores can contain persistence internals.
                return QueryFailure("Architecture-rule current resource query failed before a safe response could be produced.");
            }

            ArchonMcpArchitectureRuleRecord[] records = page.Items.OrderBy(static item => item.RuleCode, StringComparer.Ordinal).Select(MapRuleRecord).ToArray();
            ArchonMcpLimitedList<ArchonMcpArchitectureRuleRecord> limited = _limitGuard.ApplyResultLimit(records, request.Limit, ArchonMcpResourceOperations.ReadResource);
            ArchonMcpRulesCurrentResourceFacts facts = new(request.CanonicalUri, snapshot.SnapshotStableKey, snapshot.RepositoryStableKey, request.SolutionStableKey, request.Category, records.Length, limited.Items);
            return new ArchonMcpEnvelope<ArchonMcpRulesCurrentResourceFacts>(
                ArchonMcpResourceOperations.ReadResource,
                CreateSnapshotIdentity(snapshot),
                $"Current rules resource returned {limited.Items.Count} of {records.Length} matching architecture rules for snapshot {snapshot.SnapshotStableKey}.",
                new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Rule records came from the controlled application rule catalog query layer."),
                facts,
                evidence: null,
                findings: null,
                unknowns: records.Length == 0 ? null : [new ArchonMcpUnknown("ruleCurrentSnapshotScope", snapshot.SnapshotStableKey, "The current rule catalog query is not snapshot-versioned in this slice.", "Rule catalog facts are reliable, but per-snapshot rule source references require a later resource or query seam.", null)],
                warnings: CreateLimitWarnings(limited.Limits, "Rule current resource output was truncated by MCP result limits."),
                limited.Limits,
                CreateLimitAwareFollowUps(limited.SuggestedFollowUps));
        }

        /// <summary>
        /// Maps the current hotlist resource into an MCP envelope.
        /// </summary>
        /// <param name="request">The validated resource request.</param>
        /// <param name="snapshot">The selected current snapshot context.</param>
        /// <param name="cancellationToken">The token that can cancel query execution.</param>
        /// <returns>A hotlist current resource envelope or safe error response.</returns>
        private async Task<object> MapHotlistResourceAsync(ArchonMcpResourceRequest request, ArchonMcpCurrentSnapshotContext snapshot, CancellationToken cancellationToken)
        {
            // The hotlist query is snapshot-scoped using the resolved current snapshot stable key rather than the textual current selector.
            PagedQueryResult<HotlistItemDto> page;
            try
            {
                HotlistQuery query = new(snapshot.SnapshotStableKey, request.Category, request.Severity, request.Status, projectStableKey: null, affectedNodeStableKey: null, criticalOnly: null, legacyDataAccess: null, outOfSupport: null, securitySensitive: null, frameworkOnly: null, technology: null, ruleCode: null, skip: 0, take: request.Limit.GetValueOrDefault(HotlistQuery.DefaultPageSize));
                page = await _hotlistQueryService.ListHotlistAsync(query, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cooperative cancellation remains host behavior and should not be serialized as a resource failure.
                throw;
            }
            catch (Exception)
            {
                // Public errors omit query exception details because finding stores can contain persistence internals.
                return QueryFailure("Hotlist current resource query failed before a safe response could be produced.");
            }

            ArchonMcpHotlistFindingRecord[] records = page.Items.OrderBy(static item => SeverityRank(item.Severity)).ThenBy(static item => item.StableKey, StringComparer.Ordinal).Select(MapHotlistRecord).ToArray();
            ArchonMcpLimitedList<ArchonMcpHotlistFindingRecord> limited = _limitGuard.ApplyResultLimit(records, request.Limit, ArchonMcpResourceOperations.ReadResource);
            ArchonMcpHotlistCurrentResourceFacts facts = new(request.CanonicalUri, snapshot.SnapshotStableKey, snapshot.RepositoryStableKey, request.SolutionStableKey, request.Category, request.Severity, request.Status, records.Length, limited.Items);
            return new ArchonMcpEnvelope<ArchonMcpHotlistCurrentResourceFacts>(
                ArchonMcpResourceOperations.ReadResource,
                CreateSnapshotIdentity(snapshot),
                $"Current hotlist resource returned {limited.Items.Count} of {records.Length} matching findings for snapshot {snapshot.SnapshotStableKey}.",
                CreateFindingConfidence(limited.Items),
                facts,
                CreateFindingEvidence(limited.Items),
                CreateFindingReferences(limited.Items),
                CreateFindingUnknowns(limited.Items),
                CreateLimitWarnings(limited.Limits, "Hotlist current resource output was truncated by MCP result limits."),
                limited.Limits,
                CreateLimitAwareFollowUps(limited.SuggestedFollowUps));
        }

        /// <summary>
        /// Maps the current hotspots resource into an MCP envelope.
        /// </summary>
        /// <param name="request">The validated resource request.</param>
        /// <param name="snapshot">The selected current snapshot context.</param>
        /// <param name="cancellationToken">The token that can cancel query execution.</param>
        /// <returns>A hotspots current resource envelope or safe error response.</returns>
        private async Task<object> MapHotspotsResourceAsync(ArchonMcpResourceRequest request, ArchonMcpCurrentSnapshotContext snapshot, CancellationToken cancellationToken)
        {
            // Hotspots are computed through the approved query service for the selected current snapshot and never from MCP-side scoring logic.
            PagedQueryResult<HotspotItemDto> page;
            try
            {
                HotspotQuery query = new(snapshot.SnapshotStableKey, targetStableKey: null, request.Category, skip: 0, take: request.Limit.GetValueOrDefault(QueryPagingOptions.DefaultPageSize));
                page = await _hotspotQueryService.ListHotspotsAsync(query, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cooperative cancellation remains host behavior and should not be serialized as a resource failure.
                throw;
            }
            catch (Exception)
            {
                // Public errors omit query exception details because hotspot dependencies can contain graph internals.
                return QueryFailure("Hotspots current resource query failed before a safe response could be produced.");
            }

            ArchonMcpHotspotRecord[] records = page.Items.OrderBy(static item => item.Category, StringComparer.Ordinal).ThenBy(static item => item.Rank).ThenBy(static item => item.StableKey, StringComparer.Ordinal).Select(MapHotspotRecord).ToArray();
            ArchonMcpLimitedList<ArchonMcpHotspotRecord> limited = _limitGuard.ApplyResultLimit(records, request.Limit, ArchonMcpResourceOperations.ReadResource);
            ArchonMcpHotspotsCurrentResourceFacts facts = new(request.CanonicalUri, snapshot.SnapshotStableKey, snapshot.RepositoryStableKey, request.SolutionStableKey, request.Category, records.Length, limited.Items);
            return new ArchonMcpEnvelope<ArchonMcpHotspotsCurrentResourceFacts>(
                ArchonMcpResourceOperations.ReadResource,
                CreateSnapshotIdentity(snapshot),
                $"Current hotspots resource returned {limited.Items.Count} of {records.Length} matching hotspots for snapshot {snapshot.SnapshotStableKey}.",
                CreateHotspotConfidence(limited.Items),
                facts,
                CreateHotspotEvidence(limited.Items),
                CreateHotspotFindingReferences(limited.Items),
                CreateHotspotUnknowns(limited.Items),
                CreateLimitWarnings(limited.Limits, "Hotspots current resource output was truncated by MCP result limits."),
                limited.Limits,
                CreateLimitAwareFollowUps(limited.SuggestedFollowUps));
        }

        /// <summary>
        /// Maps one query-layer rule catalog item into the MCP rule record shape.
        /// </summary>
        /// <param name="item">The query-layer catalog item.</param>
        /// <returns>The MCP rule catalog record.</returns>
        private static ArchonMcpArchitectureRuleRecord MapRuleRecord(RuleCatalogItemDto item)
        {
            // Current resource rules reuse the safe rule catalog shape and avoid rule source file reads or mutation metadata.
            return new ArchonMcpArchitectureRuleRecord(item.RuleCode, item.Version, item.Name, item.Category, item.Severity, item.DefaultStatus, item.Enabled, item.BuiltIn, item.OwnerScope, item.Summary, item.Tags, RelatedFindingCount: null, SourceReferences: []);
        }

        /// <summary>
        /// Maps one query-layer hotlist item into the MCP finding record shape.
        /// </summary>
        /// <param name="item">The query-layer hotlist item.</param>
        /// <returns>The MCP hotlist finding record.</returns>
        private static ArchonMcpHotlistFindingRecord MapHotlistRecord(HotlistItemDto item)
        {
            // Hotlist resource records expose stable affected-node and evidence references without raw snippets or suppression commands.
            ArchonMcpAffectedNodeFacts[] affectedNodes = item.AffectedNodes.Select(static node => new ArchonMcpAffectedNodeFacts(node.StableKey, node.DisplayName, node.NodeKind, node.ProjectStableKey)).OrderBy(static node => node.StableKey, StringComparer.Ordinal).ToArray();
            string[] evidenceKeys = item.EvidenceReferences.Select(static evidence => evidence.StableKey).OrderBy(static key => key, StringComparer.Ordinal).ToArray();
            Dictionary<string, string> metadata = new(StringComparer.Ordinal)
            {
                ["historyKey"] = item.HistoryKey,
                ["resource"] = "archon://hotlist/current"
            };
            return new ArchonMcpHotlistFindingRecord(item.SnapshotStableKey, item.StableKey, item.HistoryKey, item.RuleCode, item.RuleVersion, item.Title, item.Summary, item.Severity, item.Status, item.Confidence, item.Category, FirstSeen: null, LatestSeen: null, affectedNodes, evidenceKeys, metadata);
        }

        /// <summary>
        /// Maps one query-layer hotspot item into the MCP hotspot record shape.
        /// </summary>
        /// <param name="item">The query-layer hotspot item.</param>
        /// <returns>The MCP hotspot record.</returns>
        private static ArchonMcpHotspotRecord MapHotspotRecord(HotspotItemDto item)
        {
            // Hotspot records expose query-layer scores and contributing stable keys without exposing scoring internals as executable logic.
            return new ArchonMcpHotspotRecord(item.SnapshotStableKey, item.StableKey, item.Category, item.TargetStableKey, item.TargetKind, item.DisplayName, item.Score, item.Rank, item.ContributingMetricStableKeys, item.ContributingFindingStableKeys, item.EvidenceStableKeys, item.Confidence, item.HasUnknownData, item.UnknownReason, item.Fingerprint);
        }

        /// <summary>
        /// Creates snapshot identity metadata for a selected current snapshot.
        /// </summary>
        /// <param name="snapshot">The selected current snapshot context.</param>
        /// <returns>The common MCP snapshot identity.</returns>
        private static ArchonMcpSnapshotIdentity CreateSnapshotIdentity(ArchonMcpCurrentSnapshotContext snapshot)
        {
            // The selector explains that current has already resolved to a concrete snapshot stable key.
            return new ArchonMcpSnapshotIdentity(snapshot.SnapshotStableKey, "current", "Current resource selection resolved to one explicit snapshot stable key.");
        }

        /// <summary>
        /// Creates warnings for current snapshot diagnostics.
        /// </summary>
        /// <param name="snapshot">The selected current snapshot context.</param>
        /// <returns>Safe warnings for extraction diagnostics.</returns>
        private static IReadOnlyList<ArchonMcpWarning> CreateSnapshotWarnings(ArchonMcpCurrentSnapshotContext snapshot)
        {
            // Counts indicate diagnostic presence without returning arbitrary warning or error text from extraction outputs.
            List<ArchonMcpWarning> warnings = [];
            if (snapshot.WarningCount > 0)
            {
                warnings.Add(new ArchonMcpWarning("snapshotWarningsPresent", "The selected snapshot contains extraction warnings; details are not expanded by this current resource.", null));
            }

            if (snapshot.ErrorCount > 0)
            {
                warnings.Add(new ArchonMcpWarning("snapshotErrorsPresent", "The selected snapshot contains extraction errors; details are not expanded by this current resource.", null));
            }

            return warnings;
        }

        /// <summary>
        /// Creates warnings for limited current resource responses.
        /// </summary>
        /// <param name="limits">The applied MCP limit metadata.</param>
        /// <param name="message">The warning message used when truncation occurred.</param>
        /// <returns>Safe warnings for truncation.</returns>
        private static IReadOnlyList<ArchonMcpWarning> CreateLimitWarnings(ArchonMcpLimitMetadata limits, string message)
        {
            // Truncation is explicitly reported so clients know returned resource content is a bounded summary, not the entire graph.
            return limits.Truncated ? [new ArchonMcpWarning("truncated", message, null)] : [];
        }

        /// <summary>
        /// Creates safe suggested follow-ups for the current snapshot resource.
        /// </summary>
        /// <param name="snapshot">The selected current snapshot context.</param>
        /// <returns>Read-only follow-up suggestions.</returns>
        private static IReadOnlyList<ArchonMcpSuggestedFollowUp> CreateSnapshotFollowUps(ArchonMcpCurrentSnapshotContext snapshot)
        {
            // Follow-ups point to read-only resource and tool workflows rather than mutation or direct database access.
            return
            [
                new ArchonMcpSuggestedFollowUp($"Read archon://rules/current for rule context on {snapshot.SnapshotStableKey}.", "mcp.resource", new Dictionary<string, string>(StringComparer.Ordinal) { ["uri"] = "archon://rules/current" }),
                new ArchonMcpSuggestedFollowUp($"Read archon://hotlist/current for findings on {snapshot.SnapshotStableKey}.", "mcp.resource", new Dictionary<string, string>(StringComparer.Ordinal) { ["uri"] = "archon://hotlist/current" }),
                new ArchonMcpSuggestedFollowUp($"Read archon://hotspots/current for ranked hotspots on {snapshot.SnapshotStableKey}.", "mcp.resource", new Dictionary<string, string>(StringComparer.Ordinal) { ["uri"] = "archon://hotspots/current" })
            ];
        }

        /// <summary>
        /// Combines shared limit follow-ups with a generic narrowing suggestion.
        /// </summary>
        /// <param name="limitFollowUps">The follow-ups returned by the limit guard.</param>
        /// <returns>Safe follow-up suggestions.</returns>
        private static IReadOnlyList<ArchonMcpSuggestedFollowUp> CreateLimitAwareFollowUps(IReadOnlyList<ArchonMcpSuggestedFollowUp> limitFollowUps)
        {
            // Resource follow-ups stay read-only and encourage narrower filters rather than broader graph or filesystem access.
            List<ArchonMcpSuggestedFollowUp> followUps = [..limitFollowUps];
            followUps.Add(new ArchonMcpSuggestedFollowUp("Narrow the resource with category, severity, status, or a smaller limit when supported.", "user.question", null));
            return followUps;
        }

        /// <summary>
        /// Creates safe evidence references for bounded hotlist findings.
        /// </summary>
        /// <param name="records">The bounded hotlist records.</param>
        /// <returns>Safe evidence references.</returns>
        private IReadOnlyList<ArchonMcpEvidenceReference> CreateFindingEvidence(IReadOnlyList<ArchonMcpHotlistFindingRecord> records)
        {
            // Evidence references include only stable keys and type labels, never snippets or raw source text.
            return records.SelectMany(record => record.EvidenceStableKeys.Select(key => _responseMapper.MapEvidence(key, "FindingEvidence", sourcePath: null, startLine: null, endLine: null, symbolName: null, containingSymbol: null, snippetPreview: null, snippetHash: null, new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Evidence reference was returned by the hotlist current resource."), snapshot: null))).GroupBy(evidence => evidence.StableKey, StringComparer.Ordinal).Select(group => group.First()).OrderBy(evidence => evidence.StableKey, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Creates safe finding references for bounded hotlist findings.
        /// </summary>
        /// <param name="records">The bounded hotlist records.</param>
        /// <returns>Safe finding references.</returns>
        private static IReadOnlyList<ArchonMcpFindingReference> CreateFindingReferences(IReadOnlyList<ArchonMcpHotlistFindingRecord> records)
        {
            // Finding references summarize stable identities without duplicating the entire facts section.
            return records.Select(static record => new ArchonMcpFindingReference(record.StableKey, record.RuleCode, record.RuleVersion, record.Severity, record.Status, new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Finding reference was returned by the hotlist current resource."), record.AffectedNodes.Select(static node => node.StableKey), record.EvidenceStableKeys)).ToArray();
        }

        /// <summary>
        /// Creates unknown records for bounded hotlist findings.
        /// </summary>
        /// <param name="records">The bounded hotlist records.</param>
        /// <returns>Explicit unknown records for partial finding context.</returns>
        private static IReadOnlyList<ArchonMcpUnknown> CreateFindingUnknowns(IReadOnlyList<ArchonMcpHotlistFindingRecord> records)
        {
            // Unknowns preserve query-layer uncertainty and history timestamp gaps so clients do not invent missing context.
            List<ArchonMcpUnknown> unknowns = records.Where(static record => record.FirstSeen is null || record.LatestSeen is null).Select(static record => new ArchonMcpUnknown("findingHistoryTimestamps", record.StableKey, "Current hotlist resource does not include firstSeen or latestSeen timestamps.", "History timing conclusions require a dedicated finding history query.", null)).ToList();
            unknowns.AddRange(records.Where(static record => record.Metadata.ContainsKey("unknownReason")).Select(static record => new ArchonMcpUnknown("findingPartialContext", record.StableKey, record.Metadata["unknownReason"], "Finding confidence is lowered because some contributing context was unresolved.", null)));
            return unknowns;
        }

        /// <summary>
        /// Creates overall confidence for bounded findings.
        /// </summary>
        /// <param name="records">The bounded finding records.</param>
        /// <returns>The common MCP confidence record.</returns>
        private static ArchonMcpConfidence CreateFindingConfidence(IReadOnlyList<ArchonMcpHotlistFindingRecord> records)
        {
            // Partial or empty finding sets lower confidence because the resource is a bounded summary over selected current data.
            return records.Count == 0
                ? new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "The hotlist query succeeded but returned no current findings for the selected scope.")
                : new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Hotlist findings came from the controlled application query layer for the selected current snapshot.");
        }

        /// <summary>
        /// Creates safe evidence references for bounded hotspots.
        /// </summary>
        /// <param name="records">The bounded hotspot records.</param>
        /// <returns>Safe evidence references.</returns>
        private IReadOnlyList<ArchonMcpEvidenceReference> CreateHotspotEvidence(IReadOnlyList<ArchonMcpHotspotRecord> records)
        {
            // Hotspot evidence references include only stable keys and type labels, never snippets or raw source text.
            return records.SelectMany(record => record.EvidenceStableKeys.Select(key => _responseMapper.MapEvidence(key, "HotspotEvidence", sourcePath: null, startLine: null, endLine: null, symbolName: null, containingSymbol: null, snippetPreview: null, snippetHash: null, new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Evidence reference was returned by the hotspots current resource."), snapshot: null))).GroupBy(evidence => evidence.StableKey, StringComparer.Ordinal).Select(group => group.First()).OrderBy(evidence => evidence.StableKey, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Creates safe finding references for bounded hotspots.
        /// </summary>
        /// <param name="records">The bounded hotspot records.</param>
        /// <returns>Safe finding references for contributing findings.</returns>
        private static IReadOnlyList<ArchonMcpFindingReference> CreateHotspotFindingReferences(IReadOnlyList<ArchonMcpHotspotRecord> records)
        {
            // Hotspot finding references are intentionally minimal because hotspot DTOs expose stable keys but not rule or summary details.
            return records.SelectMany(static record => record.ContributingFindingStableKeys.Select(key => new ArchonMcpFindingReference(key, "unknown", ruleVersion: null, "unknown", "unknown", new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Finding stable key was returned by the hotspot query layer."), affectedStableKeys: [record.TargetStableKey], evidenceStableKeys: record.EvidenceStableKeys))).GroupBy(finding => finding.StableKey, StringComparer.Ordinal).Select(group => group.First()).OrderBy(finding => finding.StableKey, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Creates unknown records for bounded hotspots.
        /// </summary>
        /// <param name="records">The bounded hotspot records.</param>
        /// <returns>Explicit unknown records for partial hotspot context.</returns>
        private static IReadOnlyList<ArchonMcpUnknown> CreateHotspotUnknowns(IReadOnlyList<ArchonMcpHotspotRecord> records)
        {
            // Unknowns carry hotspot uncertainty so clients do not overstate the score or contributing data.
            return records.Where(static record => record.HasUnknownData).Select(static record => new ArchonMcpUnknown("hotspotPartialContext", record.StableKey, string.IsNullOrWhiteSpace(record.UnknownReason) ? "Current hotspot includes unknown contributing data." : record.UnknownReason!, "Hotspot confidence is lowered because some contributing facts were partial.", null)).ToArray();
        }

        /// <summary>
        /// Creates overall confidence for bounded hotspots.
        /// </summary>
        /// <param name="records">The bounded hotspot records.</param>
        /// <returns>The common MCP confidence record.</returns>
        private static ArchonMcpConfidence CreateHotspotConfidence(IReadOnlyList<ArchonMcpHotspotRecord> records)
        {
            // Empty hotspot responses are still valid but carry less investigative signal than ranked hotspot output.
            return records.Count == 0
                ? new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "The hotspot query succeeded but returned no current hotspots for the selected scope.")
                : new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Hotspots came from the controlled application query layer for the selected current snapshot.");
        }

        /// <summary>
        /// Assigns a deterministic rank to common severity labels.
        /// </summary>
        /// <param name="severity">The finding severity label.</param>
        /// <returns>A numeric sort rank where lower values represent higher severity.</returns>
        private static int SeverityRank(string severity)
        {
            // Unknown severity labels sort after known high-risk labels but remain deterministic by stable-key tie breaker.
            return severity.ToLowerInvariant() switch
            {
                "critical" => 0,
                "high" => 1,
                "medium" => 2,
                "low" => 3,
                "info" => 4,
                "informational" => 4,
                _ => 5
            };
        }

        /// <summary>
        /// Creates a safe query-layer failure for current resources.
        /// </summary>
        /// <param name="message">The public safe failure message.</param>
        /// <returns>A structured MCP error response.</returns>
        private static ArchonMcpErrorResponse QueryFailure(string message)
        {
            // Query failures hide exception type, stack trace, adapter names, and persistence details.
            return ArchonMcpErrorResponse.Create(
                ArchonMcpResourceOperations.ReadResource,
                ArchonMcpErrorCategory.QueryLayerFailure,
                message,
                [new ArchonMcpSuggestedFollowUp("Retry the resource read after verifying query data is available for the selected current snapshot.", "user.question", null)]);
        }
    }
}
