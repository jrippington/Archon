using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Search
{
    /// <summary>
    /// Implements bounded cross-domain search over application snapshot contracts without exposing persistence internals.
    /// </summary>
    public sealed class SearchQueryService : ISearchQueryService
    {
        /// <summary>
        /// Reads snapshots from the registered architecture snapshot writer when in-memory diagnostics are available.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Defines node kinds treated as project records for result classification.
        /// </summary>
        private static readonly HashSet<string> s_projectNodeKinds = new(StringComparer.OrdinalIgnoreCase)
        {
            NodeKind.Project.Value
        };

        /// <summary>
        /// Defines node kinds treated as semantic symbol records for result classification.
        /// </summary>
        private static readonly HashSet<string> s_symbolNodeKinds = new(StringComparer.OrdinalIgnoreCase)
        {
            NodeKind.Namespace.Value,
            NodeKind.Type.Value,
            NodeKind.Method.Value,
            NodeKind.Property.Value,
            NodeKind.Field.Value
        };

        /// <summary>
        /// Defines node kinds treated as runtime endpoint records for result classification.
        /// </summary>
        private static readonly HashSet<string> s_runtimeEndpointNodeKinds = new(StringComparer.OrdinalIgnoreCase)
        {
            NodeKind.Endpoint.Value,
            NodeKind.Controller.Value,
            "Handler",
            "Worker",
            NodeKind.HostedService.Value,
            "ScheduledJob"
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchQueryService"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        public SearchQueryService(IArchitectureSnapshotWriter snapshotWriter)
        {
            // Search uses the same snapshot seam as the rest of WP014 so API and future MCP clients do not depend on Neo4j access.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
        }

        /// <inheritdoc />
        public Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
        {
            // Search validates the request, resolves one bounded snapshot scope, projects supported record families, and applies deterministic paging.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<SearchQueryValidationError> optionErrors = ValidateQueryOptions(query);
            if (optionErrors.Count > 0)
            {
                return Task.FromResult(new SearchResult(optionErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new SearchResult(resolution.ValidationErrors));
            }

            SearchQueryContext context = BuildContext(query.Selector, resolution);
            HashSet<string> includedKinds = query.ResultKinds.Count == 0
                ? new HashSet<string>(SearchResultKinds.All, StringComparer.Ordinal)
                : new HashSet<string>(query.ResultKinds, StringComparer.Ordinal);
            SearchResultItemDto[] allItems = BuildSearchItems(resolution.Snapshot!, query.SearchText!.Trim(), query.ProjectStableKey, includedKinds);
            SearchResultItemDto[] ordered = allItems
                .OrderBy(static item => SearchKindOrder(item.ResultKind))
                .ThenBy(static item => item.DisplayText, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.StableKey, StringComparer.Ordinal)
                .ToArray();
            SearchResultItemDto[] pageItems = ordered.Skip(query.Skip).Take(query.Take).ToArray();
            PagedQueryResult<SearchResultItemDto> page = new(pageItems, ordered.Length, query.Skip, query.Take);
            SearchQueryContext responseContext = ordered.Length == 0
                ? MergeUnknownContext(context, new SearchUnknownDto("searchResults", "No supported persisted record matched the supplied search text and filters."))
                : context;
            return Task.FromResult(new SearchResult(page, responseContext));
        }

        /// <summary>
        /// Reads snapshots from the in-memory fallback writer when that diagnostic path is available.
        /// </summary>
        /// <returns>The snapshots available to application-layer query services.</returns>
        private IReadOnlyList<ExtractedArchitectureSnapshot> GetSnapshots()
        {
            // Infrastructure-backed stores can replace this service later; this slice keeps search behind the application contract seam.
            return _snapshotWriter is InMemoryArchitectureSnapshotWriter writer
                ? writer.GetSnapshotsSnapshotForDiagnostics()
                : [];
        }

        /// <summary>
        /// Validates search text, filters, and paging before snapshot work starts.
        /// </summary>
        /// <param name="query">The caller-supplied search query.</param>
        /// <returns>A deterministic list of option validation errors.</returns>
        private static List<SearchQueryValidationError> ValidateQueryOptions(SearchQuery query)
        {
            // Option validation is separated from snapshot resolution so clients can fix unsupported filters without inspecting server logs.
            List<SearchQueryValidationError> errors = [];
            if (string.IsNullOrWhiteSpace(query.SearchText))
            {
                errors.Add(new SearchQueryValidationError(SearchQueryValidationCodes.SearchTextRequired, "Search text is required for cross-domain search."));
            }

            foreach (string resultKind in query.ResultKinds)
            {
                if (!SearchResultKinds.All.Contains(resultKind, StringComparer.Ordinal))
                {
                    errors.Add(new SearchQueryValidationError(SearchQueryValidationCodes.UnsupportedResultKind, $"Unsupported search result kind '{resultKind}'."));
                }
            }

            if (query.Skip < 0)
            {
                errors.Add(new SearchQueryValidationError(SearchQueryValidationCodes.SkipInvalid, "Skip must be greater than or equal to zero."));
            }

            if (query.Take < 1 || query.Take > SearchQueryLimits.MaximumTake)
            {
                errors.Add(new SearchQueryValidationError(SearchQueryValidationCodes.TakeInvalid, "Take must be between 1 and 200."));
            }

            return errors;
        }

        /// <summary>
        /// Resolves and validates the selected search snapshot scope.
        /// </summary>
        /// <param name="selector">The repository, solution, and snapshot selector supplied by the query.</param>
        /// <returns>A successful snapshot resolution or deterministic validation errors.</returns>
        private SnapshotResolution ResolveSnapshot(SearchSnapshotSelector selector)
        {
            // Scope validation runs before graph matching so missing or malformed selectors produce client-correctable problem details.
            List<SearchQueryValidationError> validationErrors = ValidateSelector(selector);
            if (validationErrors.Count > 0)
            {
                return SnapshotResolution.Failed(validationErrors);
            }

            ExtractedArchitectureSnapshot[] repositorySnapshots = GetSnapshots()
                .Where(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.RepositoryStableKey.Value, selector.RepositoryStableKey))
                .ToArray();
            if (repositorySnapshots.Length == 0)
            {
                SearchQueryValidationError error = new(SearchQueryValidationCodes.RepositoryNotFound, "The requested repository scope was not found.");
                return SnapshotResolution.Failed([error]);
            }

            ExtractedArchitectureSnapshot[] scopedSnapshots = ApplySolutionScope(repositorySnapshots, selector);
            if (selector.SolutionStableKey is not null && scopedSnapshots.Length == 0)
            {
                SearchQueryValidationError error = new(SearchQueryValidationCodes.SolutionNotFound, "The requested solution scope was not found for the repository.");
                return SnapshotResolution.Failed([error]);
            }

            ExtractedArchitectureSnapshot? selectedSnapshot = ResolveSelectedSnapshot(scopedSnapshots, selector);
            if (selectedSnapshot?.SnapshotHeader is null)
            {
                SearchQueryValidationError error = new(SearchQueryValidationCodes.SnapshotNotFound, "The requested snapshot scope was not found.");
                return SnapshotResolution.Failed([error]);
            }

            return SnapshotResolution.Success(selectedSnapshot, scopedSnapshots);
        }

        /// <summary>
        /// Validates selector syntax before any snapshot matching occurs.
        /// </summary>
        /// <param name="selector">The caller-supplied search snapshot selector.</param>
        /// <returns>A deterministic list of selector validation errors.</returns>
        private static List<SearchQueryValidationError> ValidateSelector(SearchSnapshotSelector selector)
        {
            // Repository scope is required because latest search resolution must be bounded to one repository.
            List<SearchQueryValidationError> errors = [];
            if (selector.RepositoryStableKey is null)
            {
                errors.Add(new SearchQueryValidationError(SearchQueryValidationCodes.RepositoryStableKeyRequired, "A repository stable key is required for cross-domain search."));
            }

            if (!selector.RequestsLatestSnapshot && !selector.SnapshotStableKey.StartsWith("snapshot://", StringComparison.Ordinal))
            {
                errors.Add(new SearchQueryValidationError(SearchQueryValidationCodes.SnapshotSelectorInvalid, "Snapshot selector must be 'latest', 'current', or a snapshot:// stable key."));
            }

            return errors;
        }

        /// <summary>
        /// Applies the optional solution scope to repository snapshots.
        /// </summary>
        /// <param name="repositorySnapshots">The snapshots already matched to the requested repository.</param>
        /// <param name="selector">The caller-supplied search snapshot selector.</param>
        /// <returns>The snapshots matching the optional solution scope.</returns>
        private static ExtractedArchitectureSnapshot[] ApplySolutionScope(IEnumerable<ExtractedArchitectureSnapshot> repositorySnapshots, SearchSnapshotSelector selector)
        {
            // Solution scope is resolved through snapshot-level solution facts so query behavior does not depend on host routing details.
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
        /// <param name="selector">The caller-supplied search snapshot selector.</param>
        /// <returns>The selected snapshot, or null when none matches.</returns>
        private static ExtractedArchitectureSnapshot? ResolveSelectedSnapshot(IEnumerable<ExtractedArchitectureSnapshot> scopedSnapshots, SearchSnapshotSelector selector)
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
        /// Builds the response metadata context from a successful snapshot resolution.
        /// </summary>
        /// <param name="selector">The caller-supplied selector.</param>
        /// <param name="resolution">The successful snapshot resolution.</param>
        /// <returns>The search query context used by the API envelope.</returns>
        private static SearchQueryContext BuildContext(SearchSnapshotSelector selector, SnapshotResolution resolution)
        {
            // The context mirrors project query metadata so search fits the common WP014 envelope shape.
            SnapshotHeader header = resolution.Snapshot!.SnapshotHeader!;
            ProjectScopeDto scope = new(header.RepositoryStableKey.Value, null, selector.SolutionStableKey, null);
            ProjectSnapshotMetadataDto snapshot = new(header.StableKey.Value, selector.SnapshotStableKey, selector.RequestsLatestSnapshot, header.CommitSha, header.StartedUtc, header.CompletedUtc, header.Status);
            return new SearchQueryContext(scope, snapshot, [], []);
        }

        /// <summary>
        /// Builds all supported search result rows from the selected snapshot.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="searchText">The normalized search text.</param>
        /// <param name="projectStableKey">The optional project filter.</param>
        /// <param name="includedKinds">The controlled result kinds included by the request.</param>
        /// <returns>The matching search result rows before final ordering and paging.</returns>
        private static SearchResultItemDto[] BuildSearchItems(ExtractedArchitectureSnapshot snapshot, string searchText, string? projectStableKey, HashSet<string> includedKinds)
        {
            // Search projects each supported graph family into one common row shape that gives MCP clients deterministic follow-up affordances.
            List<SearchResultItemDto> items = [];
            foreach (ArchitectureNode node in snapshot.Nodes)
            {
                string resultKind = ClassifyNode(node);
                if (!includedKinds.Contains(resultKind) || !MatchesProjectFilter(node, projectStableKey) || !MatchesNode(node, searchText))
                {
                    continue;
                }

                items.Add(ToNodeSearchItem(snapshot, node, resultKind));
            }

            if (includedKinds.Contains(SearchResultKinds.Evidence))
            {
                foreach (EvidenceRecord evidence in snapshot.Evidence.Where(evidence => MatchesEvidence(evidence, searchText)))
                {
                    items.Add(ToEvidenceSearchItem(snapshot, evidence));
                }
            }

            if (includedKinds.Contains(SearchResultKinds.Finding))
            {
                foreach (FindingRecord finding in snapshot.Findings.Where(finding => MatchesFinding(finding, searchText, projectStableKey)))
                {
                    items.Add(ToFindingSearchItem(snapshot, finding));
                }
            }

            if (includedKinds.Contains(SearchResultKinds.Metric))
            {
                foreach (MetricRecord metric in snapshot.Metrics.Where(metric => MatchesMetric(metric, searchText, projectStableKey)))
                {
                    items.Add(ToMetricSearchItem(snapshot, metric));
                }
            }

            return items.ToArray();
        }

        /// <summary>
        /// Classifies an architecture node into one public search result kind.
        /// </summary>
        /// <param name="node">The architecture node to classify.</param>
        /// <returns>The controlled search result kind.</returns>
        private static string ClassifyNode(ArchitectureNode node)
        {
            // Project, symbol, and runtime records get first-class kinds; every other node remains a searchable fact.
            if (s_projectNodeKinds.Contains(node.NodeKind.Value))
            {
                return SearchResultKinds.Project;
            }

            if (s_symbolNodeKinds.Contains(node.NodeKind.Value))
            {
                return SearchResultKinds.Symbol;
            }

            return s_runtimeEndpointNodeKinds.Contains(node.NodeKind.Value)
                ? SearchResultKinds.RuntimeEndpoint
                : SearchResultKinds.Fact;
        }

        /// <summary>
        /// Determines whether a node belongs to the requested project filter when present.
        /// </summary>
        /// <param name="node">The architecture node to inspect.</param>
        /// <param name="projectStableKey">The optional project stable-key filter.</param>
        /// <returns><see langword="true"/> when the node should remain in the result set.</returns>
        private static bool MatchesProjectFilter(ArchitectureNode node, string? projectStableKey)
        {
            // Project nodes match on their own identity; child facts match on their owning project identity.
            return string.IsNullOrWhiteSpace(projectStableKey)
                || StringComparer.Ordinal.Equals(node.StableKey.Value, projectStableKey)
                || StringComparer.Ordinal.Equals(node.ProjectStableKey?.Value, projectStableKey);
        }

        /// <summary>
        /// Determines whether safe node text contains the requested search text.
        /// </summary>
        /// <param name="node">The architecture node to inspect.</param>
        /// <param name="searchText">The normalized search text.</param>
        /// <returns><see langword="true"/> when the node matches.</returns>
        private static bool MatchesNode(ArchitectureNode node, string searchText)
        {
            // Node search avoids raw metadata values and matches only stable identity and normalized public display fields.
            return Contains(node.StableKey.Value, searchText)
                || Contains(node.DisplayName, searchText)
                || Contains(node.QualifiedName, searchText)
                || Contains(node.SearchName, searchText)
                || Contains(node.NodeKind.Value, searchText)
                || Contains(node.Language, searchText)
                || Contains(node.ProjectStableKey?.Value, searchText);
        }

        /// <summary>
        /// Determines whether safe evidence text contains the requested search text.
        /// </summary>
        /// <param name="evidence">The evidence record to inspect.</param>
        /// <param name="searchText">The normalized search text.</param>
        /// <returns><see langword="true"/> when the evidence record matches.</returns>
        private static bool MatchesEvidence(EvidenceRecord evidence, string searchText)
        {
            // Evidence search treats snippets as untrusted display content and does not expand beyond persisted preview text.
            return Contains(evidence.StableKey.Value, searchText)
                || Contains(evidence.FilePath.Value, searchText)
                || Contains(evidence.SymbolName, searchText)
                || Contains(evidence.SnippetPreview, searchText)
                || Contains(evidence.EvidenceKind.Value, searchText);
        }

        /// <summary>
        /// Determines whether safe finding text contains the requested search text and optional project filter.
        /// </summary>
        /// <param name="finding">The finding record to inspect.</param>
        /// <param name="searchText">The normalized search text.</param>
        /// <param name="projectStableKey">The optional project stable-key filter.</param>
        /// <returns><see langword="true"/> when the finding record matches.</returns>
        private static bool MatchesFinding(FindingRecord finding, string searchText, string? projectStableKey)
        {
            // Finding search uses stable finding, rule, title, severity, affected-node, and evidence fields that are already safe for public API output.
            bool projectMatches = string.IsNullOrWhiteSpace(projectStableKey)
                || StringComparer.Ordinal.Equals(finding.PrimaryNodeStableKey?.Value, projectStableKey)
                || finding.AffectedNodeStableKeys.Any(key => StringComparer.Ordinal.Equals(key.Value, projectStableKey));
            return projectMatches
                && (Contains(finding.StableKey.Value, searchText)
                    || Contains(finding.RuleCode, searchText)
                    || Contains(finding.Title, searchText)
                    || Contains(finding.Description, searchText)
                    || Contains(finding.Severity.Value, searchText)
                    || Contains(finding.Status.Value, searchText)
                    || finding.AffectedNodeStableKeys.Any(key => Contains(key.Value, searchText))
                    || finding.EvidenceStableKeys.Any(key => Contains(key.Value, searchText)));
        }

        /// <summary>
        /// Determines whether safe metric text contains the requested search text and optional project filter.
        /// </summary>
        /// <param name="metric">The metric record to inspect.</param>
        /// <param name="searchText">The normalized search text.</param>
        /// <param name="projectStableKey">The optional project stable-key filter.</param>
        /// <returns><see langword="true"/> when the metric record matches.</returns>
        private static bool MatchesMetric(MetricRecord metric, string searchText, string? projectStableKey)
        {
            // Metric search includes target stable keys and display metric fields without exposing raw metadata maps.
            bool projectMatches = string.IsNullOrWhiteSpace(projectStableKey)
                || StringComparer.Ordinal.Equals(metric.NodeStableKey?.Value, projectStableKey)
                || StringComparer.Ordinal.Equals(metric.EdgeStableKey?.Value, projectStableKey);
            return projectMatches
                && (Contains(metric.StableKey.Value, searchText)
                    || Contains(metric.MetricKind, searchText)
                    || Contains(metric.ScopeKind.Value, searchText)
                    || Contains(metric.NodeStableKey?.Value, searchText)
                    || Contains(metric.EdgeStableKey?.Value, searchText)
                    || Contains(metric.Name, searchText)
                    || Contains(metric.TextValue, searchText)
                    || Contains(metric.Unit, searchText));
        }

        /// <summary>
        /// Converts a matched architecture node into a public search row.
        /// </summary>
        /// <param name="snapshot">The snapshot that owns the node.</param>
        /// <param name="node">The matched node.</param>
        /// <param name="resultKind">The classified public search result kind.</param>
        /// <returns>The public search result row.</returns>
        private static SearchResultItemDto ToNodeSearchItem(ExtractedArchitectureSnapshot snapshot, ArchitectureNode node, string resultKind)
        {
            // Follow-up routes are intentionally deterministic so MCP clients can continue without direct graph access.
            string route = resultKind switch
            {
                SearchResultKinds.Project => "/projects/detail",
                SearchResultKinds.Symbol => "/symbols/detail",
                SearchResultKinds.RuntimeEndpoint => "/runtime/endpoints",
                _ => "/graph-neighbourhood"
            };
            string parameterName = resultKind switch
            {
                SearchResultKinds.Project => "projectStableKey",
                SearchResultKinds.Symbol => "symbolStableKey",
                SearchResultKinds.RuntimeEndpoint => "projectStableKey",
                _ => "nodeStableKey"
            };
            string parameterValue = resultKind == SearchResultKinds.RuntimeEndpoint
                ? node.ProjectStableKey?.Value ?? node.StableKey.Value
                : node.StableKey.Value;
            Dictionary<string, string> parameters = BaseFollowUpParameters(snapshot, parameterName, parameterValue);
            return new SearchResultItemDto(
                resultKind,
                node.StableKey.Value,
                node.DisplayName,
                node.NodeKind.Value + (node.QualifiedName is null ? string.Empty : " " + node.QualifiedName),
                node.SnapshotStableKey.Value,
                node.Confidence.Value,
                ToEvidenceKeys(node.PrimaryEvidenceStableKey),
                ToRelatedNodes(node.StableKey, node.ProjectStableKey, node.ParentNodeStableKey),
                node.UnknownState.HasUnknownData,
                node.UnknownState.UnknownReason,
                [new SearchFollowUpAffordanceDto("Open related query", route, parameters)]);
        }

        /// <summary>
        /// Converts matched evidence into a public search row.
        /// </summary>
        /// <param name="snapshot">The snapshot that owns the evidence.</param>
        /// <param name="evidence">The matched evidence record.</param>
        /// <returns>The public search result row.</returns>
        private static SearchResultItemDto ToEvidenceSearchItem(ExtractedArchitectureSnapshot snapshot, EvidenceRecord evidence)
        {
            // Evidence summary uses bounded persisted preview text only and never reads source files during search.
            Dictionary<string, string> parameters = BaseFollowUpParameters(snapshot, "evidenceStableKey", evidence.StableKey.Value);
            string displayText = evidence.FilePath.Value;
            string summary = evidence.SnippetPreview is null ? evidence.EvidenceKind.Value : evidence.EvidenceKind.Value + " " + evidence.SnippetPreview;
            return new SearchResultItemDto(
                SearchResultKinds.Evidence,
                evidence.StableKey.Value,
                displayText,
                summary,
                evidence.SnapshotStableKey.Value,
                evidence.Confidence.Value,
                [evidence.StableKey.Value],
                [],
                evidence.UnknownState.HasUnknownData,
                evidence.UnknownState.UnknownReason,
                [new SearchFollowUpAffordanceDto("Open evidence detail", "/evidence/detail", parameters)]);
        }

        /// <summary>
        /// Converts matched finding into a public search row.
        /// </summary>
        /// <param name="snapshot">The snapshot that owns the finding.</param>
        /// <param name="finding">The matched finding record.</param>
        /// <returns>The public search result row.</returns>
        private static SearchResultItemDto ToFindingSearchItem(ExtractedArchitectureSnapshot snapshot, FindingRecord finding)
        {
            // Finding follow-up uses the query-parameter route because finding stable keys commonly contain slash-like separators.
            Dictionary<string, string> parameters = BaseFollowUpParameters(snapshot, "findingStableKey", finding.StableKey.Value);
            parameters["snapshotStableKey"] = finding.SnapshotStableKey.Value;
            return new SearchResultItemDto(
                SearchResultKinds.Finding,
                finding.StableKey.Value,
                finding.Title,
                finding.RuleCode + " " + finding.Severity.Value + " " + finding.Status.Value,
                finding.SnapshotStableKey.Value,
                finding.Confidence.Value,
                finding.EvidenceStableKeys.Select(static key => key.Value).ToArray(),
                finding.AffectedNodeStableKeys.Select(static key => key.Value).ToArray(),
                finding.UnknownState.HasUnknownData,
                finding.UnknownState.UnknownReason,
                [new SearchFollowUpAffordanceDto("Open finding detail", "/findings/detail", parameters)]);
        }

        /// <summary>
        /// Converts matched metric into a public search row.
        /// </summary>
        /// <param name="snapshot">The snapshot that owns the metric.</param>
        /// <param name="metric">The matched metric record.</param>
        /// <returns>The public search result row.</returns>
        private static SearchResultItemDto ToMetricSearchItem(ExtractedArchitectureSnapshot snapshot, MetricRecord metric)
        {
            // Metric follow-up keeps callers on the controlled metric list route with exact snapshot and metric-kind parameters.
            Dictionary<string, string> parameters = BaseFollowUpParameters(snapshot, "metricKind", metric.MetricKind);
            parameters["snapshotStableKey"] = metric.SnapshotStableKey.Value;
            string summary = metric.NumericValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? metric.TextValue ?? "Metric value unavailable";
            return new SearchResultItemDto(
                SearchResultKinds.Metric,
                metric.StableKey.Value,
                metric.Name,
                metric.MetricKind + " " + summary,
                metric.SnapshotStableKey.Value,
                metric.Confidence.Value,
                ToEvidenceKeys(metric.PrimaryEvidenceStableKey),
                ToRelatedNodes(metric.NodeStableKey, metric.EdgeStableKey),
                metric.UnknownState.HasUnknownData,
                metric.UnknownState.UnknownReason,
                [new SearchFollowUpAffordanceDto("Open metric list", "/snapshot-metrics", parameters)]);
        }

        /// <summary>
        /// Builds common follow-up query parameters for selected snapshot scope.
        /// </summary>
        /// <param name="snapshot">The snapshot that owns the search row.</param>
        /// <param name="identityParameterName">The identity parameter name for the follow-up route.</param>
        /// <param name="identityParameterValue">The identity parameter value for the follow-up route.</param>
        /// <returns>A mutable dictionary containing stable follow-up parameters.</returns>
        private static Dictionary<string, string> BaseFollowUpParameters(ExtractedArchitectureSnapshot snapshot, string identityParameterName, string identityParameterValue)
        {
            // Follow-ups include repository and snapshot scope so clients can replay the query without relying on hidden state.
            SnapshotHeader header = snapshot.SnapshotHeader!;
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["repositoryStableKey"] = header.RepositoryStableKey.Value,
                ["snapshotStableKey"] = header.StableKey.Value,
                [identityParameterName] = identityParameterValue
            };
        }

        /// <summary>
        /// Converts optional evidence stable key into deterministic evidence-key output.
        /// </summary>
        /// <param name="stableKey">The optional evidence stable key.</param>
        /// <returns>A stable-key list containing the evidence key when present.</returns>
        private static IReadOnlyList<string> ToEvidenceKeys(StableKey? stableKey)
        {
            // Null evidence is represented as an empty list so API consumers never see placeholder IDs.
            return stableKey.HasValue ? [stableKey.Value.Value] : [];
        }

        /// <summary>
        /// Builds a deterministic related-node list from optional stable-key values.
        /// </summary>
        /// <param name="stableKeys">The stable keys to normalize.</param>
        /// <returns>The deterministic related stable-key list.</returns>
        private static IReadOnlyList<string> ToRelatedNodes(params StableKey?[] stableKeys)
        {
            // Related nodes are deduplicated so consumers can follow stable identities without repeated affordances.
            return stableKeys
                .Where(static stableKey => stableKey.HasValue)
                .Select(static stableKey => stableKey!.Value.Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static stableKey => stableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Merges one unknown entry into the existing search query context.
        /// </summary>
        /// <param name="context">The existing search query context.</param>
        /// <param name="unknown">The unknown entry to merge.</param>
        /// <returns>A context containing the merged unknown entry.</returns>
        private static SearchQueryContext MergeUnknownContext(SearchQueryContext context, SearchUnknownDto unknown)
        {
            // Empty searches are successful but explicit so callers do not mistake missing coverage for an error or hidden graph access need.
            SearchUnknownDto[] unknowns = context.Unknowns
                .Concat([unknown])
                .DistinctBy(static item => item.Field + "\u001f" + item.Reason)
                .ToArray();
            return context with { Unknowns = unknowns };
        }

        /// <summary>
        /// Performs an ordinal-ignore-case containment check over optional public text.
        /// </summary>
        /// <param name="source">The public source text to inspect.</param>
        /// <param name="searchText">The normalized search text.</param>
        /// <returns><see langword="true"/> when <paramref name="source"/> contains <paramref name="searchText"/>.</returns>
        private static bool Contains(string? source, string searchText)
        {
            // Search is intentionally simple and deterministic; it is not a ranking engine or arbitrary source-code grep surface.
            return source?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Gets deterministic ordering for supported search result kinds.
        /// </summary>
        /// <param name="resultKind">The result kind to order.</param>
        /// <returns>The deterministic order index.</returns>
        private static int SearchKindOrder(string resultKind)
        {
            // Explicit ordering prevents incidental alphabetical changes from reshaping API responses.
            return resultKind switch
            {
                SearchResultKinds.Project => 0,
                SearchResultKinds.Symbol => 1,
                SearchResultKinds.RuntimeEndpoint => 2,
                SearchResultKinds.Fact => 3,
                SearchResultKinds.Evidence => 4,
                SearchResultKinds.Finding => 5,
                SearchResultKinds.Metric => 6,
                _ => 99
            };
        }

        /// <summary>
        /// Represents the result of resolving a search snapshot selector.
        /// </summary>
        private sealed class SnapshotResolution
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="SnapshotResolution"/> class.
            /// </summary>
            /// <param name="snapshot">The resolved snapshot when selection succeeded.</param>
            /// <param name="validationErrors">The validation errors when selection failed.</param>
            private SnapshotResolution(ExtractedArchitectureSnapshot? snapshot, IEnumerable<SearchQueryValidationError> validationErrors)
            {
                // A single resolution shape keeps success and failure handling explicit inside the query service.
                Snapshot = snapshot;
                ValidationErrors = validationErrors.ToArray();
                Succeeded = snapshot is not null && ValidationErrors.Count == 0;
            }

            /// <summary>
            /// Gets a value indicating whether snapshot resolution succeeded.
            /// </summary>
            public bool Succeeded { get; }

            /// <summary>
            /// Gets the resolved snapshot when <see cref="Succeeded"/> is true.
            /// </summary>
            public ExtractedArchitectureSnapshot? Snapshot { get; }

            /// <summary>
            /// Gets deterministic validation errors when <see cref="Succeeded"/> is false.
            /// </summary>
            public IReadOnlyList<SearchQueryValidationError> ValidationErrors { get; }

            /// <summary>
            /// Creates a successful snapshot resolution.
            /// </summary>
            /// <param name="snapshot">The selected snapshot.</param>
            /// <param name="scopedSnapshots">The scoped snapshot set retained for parity with related query services.</param>
            /// <returns>A successful snapshot resolution.</returns>
            public static SnapshotResolution Success(ExtractedArchitectureSnapshot snapshot, IEnumerable<ExtractedArchitectureSnapshot> scopedSnapshots)
            {
                // The scoped snapshot sequence is accepted to mirror other query services and to keep future previous-snapshot context easy to add.
                _ = scopedSnapshots;
                return new SnapshotResolution(snapshot, []);
            }

            /// <summary>
            /// Creates a failed snapshot resolution.
            /// </summary>
            /// <param name="validationErrors">The validation errors that prevented snapshot selection.</param>
            /// <returns>A failed snapshot resolution.</returns>
            public static SnapshotResolution Failed(IEnumerable<SearchQueryValidationError> validationErrors)
            {
                // Failed resolution carries all deterministic request problems without throwing application exceptions.
                return new SnapshotResolution(null, validationErrors);
            }
        }
    }
}
