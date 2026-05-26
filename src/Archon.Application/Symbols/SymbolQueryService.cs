using System.Text.Json;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Symbols
{
    /// <summary>
    /// Implements controlled symbol search, detail, and usage query behavior over extracted architecture snapshots.
    /// </summary>
    public sealed class SymbolQueryService : ISymbolQueryService
    {
        /// <summary>
        /// Reads snapshots from the registered architecture snapshot writer when in-memory diagnostics are available.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Defines the graph node kinds that represent persisted Roslyn semantic symbols in the public query surface.
        /// </summary>
        private static readonly HashSet<string> s_symbolKinds = new(StringComparer.OrdinalIgnoreCase)
        {
            NodeKind.Namespace.Value,
            NodeKind.Type.Value,
            NodeKind.Method.Value,
            NodeKind.Property.Value,
            NodeKind.Field.Value
        };

        /// <summary>
        /// Defines the relationship kinds treated as symbol usages by the usage endpoint.
        /// </summary>
        private static readonly HashSet<string> s_usageEdgeKinds = new(StringComparer.Ordinal)
        {
            EdgeKind.References.Value,
            EdgeKind.Calls.Value,
            EdgeKind.Implements.Value,
            EdgeKind.Inherits.Value,
            EdgeKind.Handles.Value
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="SymbolQueryService"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        public SymbolQueryService(IArchitectureSnapshotWriter snapshotWriter)
        {
            // Symbol queries use the same snapshot seam as earlier WP014 slices so tests and local hosts do not require Neo4j.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
        }

        /// <inheritdoc />
        public Task<SymbolSearchResult> SearchSymbolsAsync(SymbolSearchQuery query, CancellationToken cancellationToken)
        {
            // Search validates scope and filter bounds, resolves one snapshot, maps symbol nodes, then applies deterministic filtering and paging.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<SymbolQueryValidationError> optionErrors = ValidatePagingAndSearchOptions(query.Kind, query.Sort, query.Skip, query.Take);
            if (optionErrors.Count > 0)
            {
                return Task.FromResult(new SymbolSearchResult(optionErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new SymbolSearchResult(resolution.ValidationErrors));
            }

            SymbolQueryContext context = BuildContext(query.Selector, resolution);
            SymbolSearchItemDto[] allItems = BuildSymbolSearchItems(resolution.Snapshot!);
            SymbolSearchItemDto[] filtered = ApplySearchFilters(allItems, query).ToArray();
            SymbolSearchItemDto[] ordered = ApplySearchOrdering(filtered, query).ToArray();
            SymbolSearchItemDto[] pageItems = ordered.Skip(query.Skip).Take(query.Take).ToArray();
            PagedQueryResult<SymbolSearchItemDto> page = new(pageItems, ordered.Length, query.Skip, query.Take);
            return Task.FromResult(new SymbolSearchResult(page, context));
        }

        /// <inheritdoc />
        public Task<SymbolDetailResult> GetSymbolAsync(SymbolDetailQuery query, CancellationToken cancellationToken)
        {
            // Detail lookup requires exactly one identity so callers cannot accidentally mix stable-key and text lookup semantics.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<SymbolQueryValidationError> identityErrors = ValidateDetailIdentity(query);
            if (identityErrors.Count > 0)
            {
                return Task.FromResult(new SymbolDetailResult(identityErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new SymbolDetailResult(resolution.ValidationErrors));
            }

            SymbolQueryContext context = BuildContext(query.Selector, resolution);
            SymbolSearchItemDto[] allItems = BuildSymbolSearchItems(resolution.Snapshot!);
            SymbolSearchItemDto[] matches = ResolveSymbolMatches(allItems, query);
            if (matches.Length == 0)
            {
                SymbolQueryValidationError error = new(SymbolQueryValidationCodes.SymbolNotFound, "The requested symbol was not found in the selected snapshot scope.");
                return Task.FromResult(new SymbolDetailResult([error]));
            }

            if (matches.Length > 1)
            {
                SymbolQueryValidationError error = new(SymbolQueryValidationCodes.SymbolSearchTextAmbiguous, "The requested symbol search text matches multiple symbols; use a stable symbol key to disambiguate.");
                return Task.FromResult(new SymbolDetailResult([error]));
            }

            SymbolDetailDto detail = BuildSymbolDetail(resolution.Snapshot!, matches[0]);
            return Task.FromResult(new SymbolDetailResult(detail, context));
        }

        /// <inheritdoc />
        public Task<SymbolUsageResult> ListSymbolUsagesAsync(SymbolUsageQuery query, CancellationToken cancellationToken)
        {
            // Usage lookup validates scope, identity, direction, and paging before mapping bounded symbol relationship evidence.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<SymbolQueryValidationError> optionErrors = ValidateUsageOptions(query);
            if (optionErrors.Count > 0)
            {
                return Task.FromResult(new SymbolUsageResult(optionErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new SymbolUsageResult(resolution.ValidationErrors));
            }

            ExtractedArchitectureSnapshot snapshot = resolution.Snapshot!;
            if (!SymbolExists(snapshot, query.SymbolStableKey!))
            {
                SymbolQueryValidationError error = new(SymbolQueryValidationCodes.SymbolNotFound, "The requested symbol was not found in the selected snapshot scope.");
                return Task.FromResult(new SymbolUsageResult([error]));
            }

            SymbolQueryContext context = BuildContext(query.Selector, resolution);
            string direction = NormalizeUsageDirection(query.Direction);
            SymbolUsageDto[] allUsages = BuildSymbolUsages(snapshot, query.SymbolStableKey!, direction);
            SymbolUsageDto[] ordered = allUsages
                .OrderBy(static usage => usage.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static usage => usage.StartLine ?? int.MaxValue)
                .ThenBy(static usage => usage.UsageStableKey, StringComparer.Ordinal)
                .ToArray();
            SymbolUsageDto[] pageItems = ordered.Skip(query.Skip).Take(query.Take).ToArray();
            PagedQueryResult<SymbolUsageDto> page = new(pageItems, ordered.Length, query.Skip, query.Take);
            SymbolQueryContext responseContext = ordered.Length == 0
                ? MergeUnknownContext(context, new SymbolUnknownDto("symbolUsages", "No persisted reference or call relationships were available for the requested symbol."))
                : context;
            return Task.FromResult(new SymbolUsageResult(page, responseContext));
        }

        /// <summary>
        /// Reads snapshots from the in-memory fallback writer when that diagnostic path is available.
        /// </summary>
        /// <returns>The snapshots available to application-layer query services.</returns>
        private IReadOnlyList<ExtractedArchitectureSnapshot> GetSnapshots()
        {
            // Infrastructure-backed stores can replace this service later; the current slice uses the repository-standard in-memory query seam.
            return _snapshotWriter is InMemoryArchitectureSnapshotWriter writer
                ? writer.GetSnapshotsSnapshotForDiagnostics()
                : [];
        }

        /// <summary>
        /// Resolves and validates the selected symbol snapshot scope.
        /// </summary>
        /// <param name="selector">The repository, solution, and snapshot selector supplied by the query.</param>
        /// <returns>A successful snapshot resolution or deterministic validation errors.</returns>
        private SnapshotResolution ResolveSnapshot(SymbolSnapshotSelector selector)
        {
            // Scope validation runs before graph matching so missing or malformed selectors produce client-correctable problem details.
            List<SymbolQueryValidationError> validationErrors = ValidateSelector(selector);
            if (validationErrors.Count > 0)
            {
                return SnapshotResolution.Failed(validationErrors);
            }

            ExtractedArchitectureSnapshot[] repositorySnapshots = GetSnapshots()
                .Where(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.RepositoryStableKey.Value, selector.RepositoryStableKey))
                .ToArray();
            if (repositorySnapshots.Length == 0)
            {
                SymbolQueryValidationError error = new(SymbolQueryValidationCodes.RepositoryNotFound, "The requested repository scope was not found.");
                return SnapshotResolution.Failed([error]);
            }

            ExtractedArchitectureSnapshot[] scopedSnapshots = ApplySolutionScope(repositorySnapshots, selector);
            if (selector.SolutionStableKey is not null && scopedSnapshots.Length == 0)
            {
                SymbolQueryValidationError error = new(SymbolQueryValidationCodes.SolutionNotFound, "The requested solution scope was not found for the repository.");
                return SnapshotResolution.Failed([error]);
            }

            ExtractedArchitectureSnapshot? selectedSnapshot = ResolveSelectedSnapshot(scopedSnapshots, selector);
            if (selectedSnapshot?.SnapshotHeader is null)
            {
                SymbolQueryValidationError error = new(SymbolQueryValidationCodes.SnapshotNotFound, "The requested snapshot scope was not found.");
                return SnapshotResolution.Failed([error]);
            }

            return SnapshotResolution.Success(selectedSnapshot, scopedSnapshots);
        }

        /// <summary>
        /// Validates selector syntax before any snapshot matching occurs.
        /// </summary>
        /// <param name="selector">The caller-supplied symbol snapshot selector.</param>
        /// <returns>A deterministic list of selector validation errors.</returns>
        private static List<SymbolQueryValidationError> ValidateSelector(SymbolSnapshotSelector selector)
        {
            // Repository scope is required because latest resolution must be bounded to one repository.
            List<SymbolQueryValidationError> errors = [];
            if (selector.RepositoryStableKey is null)
            {
                errors.Add(new SymbolQueryValidationError(SymbolQueryValidationCodes.RepositoryStableKeyRequired, "A repository stable key is required for symbol queries."));
            }

            if (!selector.RequestsLatestSnapshot && !selector.SnapshotStableKey.StartsWith("snapshot://", StringComparison.Ordinal))
            {
                errors.Add(new SymbolQueryValidationError(SymbolQueryValidationCodes.SnapshotSelectorInvalid, "Snapshot selector must be 'latest', 'current', or a snapshot:// stable key."));
            }

            return errors;
        }

        /// <summary>
        /// Applies the optional solution scope to repository snapshots.
        /// </summary>
        /// <param name="repositorySnapshots">The snapshots already matched to the requested repository.</param>
        /// <param name="selector">The caller-supplied symbol snapshot selector.</param>
        /// <returns>The snapshots matching the optional solution scope.</returns>
        private static ExtractedArchitectureSnapshot[] ApplySolutionScope(IEnumerable<ExtractedArchitectureSnapshot> repositorySnapshots, SymbolSnapshotSelector selector)
        {
            // Solution scope is resolved through snapshot-level solution facts just like project and traversal query scope resolution.
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
        /// <param name="selector">The caller-supplied symbol snapshot selector.</param>
        /// <returns>The selected snapshot, or null when none matches.</returns>
        private static ExtractedArchitectureSnapshot? ResolveSelectedSnapshot(IEnumerable<ExtractedArchitectureSnapshot> scopedSnapshots, SymbolSnapshotSelector selector)
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
        /// Validates symbol search paging, kind, and sort values.
        /// </summary>
        /// <param name="kind">The optional symbol kind filter.</param>
        /// <param name="sort">The optional sort field.</param>
        /// <param name="skip">The requested skip value.</param>
        /// <param name="take">The requested take value.</param>
        /// <returns>A deterministic list of search option validation errors.</returns>
        private static List<SymbolQueryValidationError> ValidatePagingAndSearchOptions(string? kind, string? sort, int skip, int take)
        {
            // Option validation happens before snapshot lookup so malformed automation can repair fixed query parameters safely.
            List<SymbolQueryValidationError> errors = [];
            if (!string.IsNullOrWhiteSpace(kind) && !s_symbolKinds.Contains(kind.Trim()))
            {
                errors.Add(new SymbolQueryValidationError(SymbolQueryValidationCodes.SymbolKindUnsupported, "Symbol kind must be Namespace, Type, Method, Property, or Field."));
            }

            if (!string.IsNullOrWhiteSpace(sort) && !IsSupportedSort(sort))
            {
                errors.Add(new SymbolQueryValidationError(SymbolQueryValidationCodes.SortUnsupported, "Symbol sort must be name, kind, project, namespace, containingType, language, or confidence."));
            }

            AddPagingErrors(skip, take, errors);
            return errors;
        }

        /// <summary>
        /// Validates detail lookup identity fields before snapshot work starts.
        /// </summary>
        /// <param name="query">The symbol detail query supplied by the caller.</param>
        /// <returns>A deterministic list of identity validation errors.</returns>
        private static List<SymbolQueryValidationError> ValidateDetailIdentity(SymbolDetailQuery query)
        {
            // Detail lookup must be explicit because text lookup can be ambiguous across overloads, languages, and namespaces.
            List<SymbolQueryValidationError> errors = [];
            if (string.IsNullOrWhiteSpace(query.SymbolStableKey) && string.IsNullOrWhiteSpace(query.SearchText))
            {
                errors.Add(new SymbolQueryValidationError(SymbolQueryValidationCodes.SymbolIdentityRequired, "A symbol stable key or exact search text is required for symbol detail."));
            }

            if (!string.IsNullOrWhiteSpace(query.SymbolStableKey) && !string.IsNullOrWhiteSpace(query.SearchText))
            {
                errors.Add(new SymbolQueryValidationError(SymbolQueryValidationCodes.SymbolIdentityAmbiguous, "Use either symbol stable key or search text for symbol detail, not both."));
            }

            return errors;
        }

        /// <summary>
        /// Validates usage lookup identity, direction, and paging fields.
        /// </summary>
        /// <param name="query">The symbol usage query supplied by the caller.</param>
        /// <returns>A deterministic list of usage validation errors.</returns>
        private static List<SymbolQueryValidationError> ValidateUsageOptions(SymbolUsageQuery query)
        {
            // Usage lookup requires one stable symbol key because text matching would make reference direction ambiguous.
            List<SymbolQueryValidationError> errors = [];
            if (string.IsNullOrWhiteSpace(query.SymbolStableKey))
            {
                errors.Add(new SymbolQueryValidationError(SymbolQueryValidationCodes.SymbolIdentityRequired, "A symbol stable key is required for symbol usage queries."));
            }

            if (!IsSupportedUsageDirection(query.Direction))
            {
                errors.Add(new SymbolQueryValidationError(SymbolQueryValidationCodes.UsageDirectionUnsupported, "Usage direction must be Incoming or Outgoing."));
            }

            AddPagingErrors(query.Skip, query.Take, errors);
            return errors;
        }

        /// <summary>
        /// Adds shared paging validation errors to a caller-owned error collection.
        /// </summary>
        /// <param name="skip">The requested skip value.</param>
        /// <param name="take">The requested take value.</param>
        /// <param name="errors">The validation error collection that receives paging diagnostics.</param>
        private static void AddPagingErrors(int skip, int take, List<SymbolQueryValidationError> errors)
        {
            // Paging bounds keep query responses predictable and prevent accidental large semantic graph reads.
            if (skip < 0)
            {
                errors.Add(new SymbolQueryValidationError(SymbolQueryValidationCodes.SkipInvalid, "Symbol query skip must be greater than or equal to zero."));
            }

            if (take < 1 || take > SymbolQueryLimits.MaximumTake)
            {
                errors.Add(new SymbolQueryValidationError(SymbolQueryValidationCodes.TakeInvalid, $"Symbol query take must be between 1 and {SymbolQueryLimits.MaximumTake}."));
            }
        }

        /// <summary>
        /// Builds the symbol query context shared by API envelopes.
        /// </summary>
        /// <param name="selector">The caller-supplied symbol snapshot selector.</param>
        /// <param name="resolution">The successful snapshot resolution.</param>
        /// <returns>The symbol query context for response mapping.</returns>
        private static SymbolQueryContext BuildContext(SymbolSnapshotSelector selector, SnapshotResolution resolution)
        {
            // Context construction centralizes envelope metadata so search, detail, and usage endpoints report scope consistently.
            ExtractedArchitectureSnapshot snapshot = resolution.Snapshot!;
            RepositoryModel? repository = snapshot.Repositories.FirstOrDefault(repository => StringComparer.Ordinal.Equals(repository.StableKey.Value, selector.RepositoryStableKey));
            SolutionModel? solution = selector.SolutionStableKey is null
                ? snapshot.Solutions.OrderBy(static candidate => candidate.StableKey.Value, StringComparer.Ordinal).FirstOrDefault()
                : snapshot.Solutions.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.StableKey.Value, selector.SolutionStableKey));
            ProjectScopeDto scope = new(selector.RepositoryStableKey!, repository?.Name, solution?.StableKey.Value, solution?.Name);
            ProjectSnapshotMetadataDto snapshotMetadata = new(
                snapshot.SnapshotHeader!.StableKey.Value,
                selector.SnapshotStableKey,
                selector.RequestsLatestSnapshot,
                snapshot.SnapshotHeader.CommitSha,
                snapshot.SnapshotHeader.StartedUtc,
                snapshot.SnapshotHeader.CompletedUtc,
                snapshot.SnapshotHeader.Status);
            SymbolWarningDto[] warnings = snapshot.Warnings.Select(static warning => new SymbolWarningDto("SnapshotWarning", warning)).ToArray();
            List<SymbolUnknownDto> unknowns = [];
            if (snapshot.Errors.Any())
            {
                unknowns.Add(new SymbolUnknownDto("semanticExtraction", "The selected snapshot contains extraction errors, so symbol data may be incomplete."));
            }

            if (!snapshot.Nodes.Any(static node => IsSymbolNode(node)))
            {
                unknowns.Add(new SymbolUnknownDto("symbols", "No persisted Roslyn semantic symbol nodes were available in the selected snapshot."));
            }

            return new SymbolQueryContext(scope, snapshotMetadata, warnings, unknowns);
        }

        /// <summary>
        /// Builds all searchable symbol rows from the selected snapshot.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <returns>The complete unfiltered symbol rows for the snapshot.</returns>
        private static SymbolSearchItemDto[] BuildSymbolSearchItems(ExtractedArchitectureSnapshot snapshot)
        {
            // Symbol nodes are the authoritative search source; evidence enriches source context without expanding unbounded source content.
            return snapshot.Nodes
                .Where(static node => IsSymbolNode(node))
                .Select(node => BuildSymbolSearchItem(snapshot, node))
                .OrderBy(static symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static symbol => symbol.StableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Builds one symbol search row from a graph node and its primary evidence.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="node">The symbol node being mapped.</param>
        /// <returns>The mapped symbol search item.</returns>
        private static SymbolSearchItemDto BuildSymbolSearchItem(ExtractedArchitectureSnapshot snapshot, ArchitectureNode node)
        {
            // Metadata is used only for optional semantic detail; normalized graph fields remain the stable public identity source.
            EvidenceRecord? evidence = FindEvidence(snapshot, node.PrimaryEvidenceStableKey);
            string? namespaceName = MetadataString(node.Metadata, "namespace") ?? InferNamespace(node.QualifiedName);
            string? containingType = MetadataString(node.Metadata, "containingType") ?? InferContainingType(snapshot, node);
            SymbolSourceContextDto? sourceContext = evidence is null
                ? null
                : BuildSourceContext(evidence);
            return new SymbolSearchItemDto(
                node.StableKey.Value,
                node.DisplayName,
                node.QualifiedName,
                node.NodeKind.Value,
                node.ProjectStableKey?.Value,
                namespaceName,
                containingType,
                node.Language,
                sourceContext,
                BuildEvidenceStableKeys(node, []),
                node.Confidence.Value,
                node.UnknownState.HasUnknownData,
                node.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Applies controlled symbol search filters.
        /// </summary>
        /// <param name="items">The complete symbol rows.</param>
        /// <param name="query">The normalized symbol search query.</param>
        /// <returns>The filtered symbol rows.</returns>
        private static IEnumerable<SymbolSearchItemDto> ApplySearchFilters(IEnumerable<SymbolSearchItemDto> items, SymbolSearchQuery query)
        {
            // Search is bounded to public identity fields while exact filters match stable graph classifications.
            return items
                .Where(item => string.IsNullOrWhiteSpace(query.SearchText) || Contains(item.StableKey, query.SearchText) || Contains(item.Name, query.SearchText) || Contains(item.FullyQualifiedName, query.SearchText))
                .Where(item => string.IsNullOrWhiteSpace(query.ProjectStableKey) || StringComparer.Ordinal.Equals(item.ContainingProjectStableKey, query.ProjectStableKey.Trim()))
                .Where(item => string.IsNullOrWhiteSpace(query.Kind) || string.Equals(item.Kind, query.Kind.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(item => string.IsNullOrWhiteSpace(query.Namespace) || string.Equals(item.Namespace, query.Namespace.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(item => string.IsNullOrWhiteSpace(query.ContainingType) || string.Equals(item.ContainingType, query.ContainingType.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(item => string.IsNullOrWhiteSpace(query.Language) || string.Equals(item.Language, query.Language.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Applies deterministic symbol search ordering.
        /// </summary>
        /// <param name="items">The filtered symbol rows.</param>
        /// <param name="query">The normalized symbol search query.</param>
        /// <returns>The deterministically ordered symbol rows.</returns>
        private static IEnumerable<SymbolSearchItemDto> ApplySearchOrdering(IEnumerable<SymbolSearchItemDto> items, SymbolSearchQuery query)
        {
            // Stable-key tie-breakers prevent paging drift when multiple symbols share a display name or containing type.
            IOrderedEnumerable<SymbolSearchItemDto> ordered = (query.Sort ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "kind" => Order(items, query.Descending, static item => item.Kind),
                "project" => Order(items, query.Descending, static item => item.ContainingProjectStableKey ?? string.Empty),
                "namespace" => Order(items, query.Descending, static item => item.Namespace ?? string.Empty),
                "containingtype" => Order(items, query.Descending, static item => item.ContainingType ?? string.Empty),
                "language" => Order(items, query.Descending, static item => item.Language ?? string.Empty),
                "confidence" => Order(items, query.Descending, static item => item.Confidence),
                _ => Order(items, query.Descending, static item => item.Name)
            };
            return ordered.ThenBy(static item => item.StableKey, StringComparer.Ordinal);
        }

        /// <summary>
        /// Orders symbol rows by a string key with requested direction.
        /// </summary>
        /// <param name="items">The symbol rows to order.</param>
        /// <param name="descending">A value indicating whether the first sort should be descending.</param>
        /// <param name="selector">The string key selector.</param>
        /// <returns>The ordered symbol rows.</returns>
        private static IOrderedEnumerable<SymbolSearchItemDto> Order(IEnumerable<SymbolSearchItemDto> items, bool descending, Func<SymbolSearchItemDto, string> selector)
        {
            // String ordering is ordinal-ignore-case for developer-facing fields, with stable key tie-breakers added by the caller.
            return descending
                ? items.OrderByDescending(selector, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(selector, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Orders symbol rows by a decimal key with requested direction.
        /// </summary>
        /// <param name="items">The symbol rows to order.</param>
        /// <param name="descending">A value indicating whether the first sort should be descending.</param>
        /// <param name="selector">The decimal key selector.</param>
        /// <returns>The ordered symbol rows.</returns>
        private static IOrderedEnumerable<SymbolSearchItemDto> Order(IEnumerable<SymbolSearchItemDto> items, bool descending, Func<SymbolSearchItemDto, decimal> selector)
        {
            // Numeric ordering is used for confidence while stable key tie-breakers preserve deterministic paging.
            return descending
                ? items.OrderByDescending(selector)
                : items.OrderBy(selector);
        }

        /// <summary>
        /// Resolves detail lookup matches by stable key or exact search text.
        /// </summary>
        /// <param name="items">The symbol rows available in the selected snapshot.</param>
        /// <param name="query">The normalized detail query.</param>
        /// <returns>The matched symbol rows.</returns>
        private static SymbolSearchItemDto[] ResolveSymbolMatches(IEnumerable<SymbolSearchItemDto> items, SymbolDetailQuery query)
        {
            // Stable-key lookup is exact; text lookup returns all exact name or qualified-name matches for ambiguity handling.
            return !string.IsNullOrWhiteSpace(query.SymbolStableKey)
                ? items.Where(item => StringComparer.Ordinal.Equals(item.StableKey, query.SymbolStableKey.Trim())).ToArray()
                : items.Where(item => string.Equals(item.Name, query.SearchText?.Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(item.FullyQualifiedName, query.SearchText?.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        /// <summary>
        /// Builds one symbol detail response from the selected symbol and related snapshot facts.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="summary">The selected symbol summary.</param>
        /// <returns>The detailed symbol response payload.</returns>
        private static SymbolDetailDto BuildSymbolDetail(ExtractedArchitectureSnapshot snapshot, SymbolSearchItemDto summary)
        {
            // Detail output gathers direct semantic relationships only; broader traversal remains owned by bounded graph traversal endpoints.
            ArchitectureNode node = snapshot.Nodes.First(candidate => StringComparer.Ordinal.Equals(candidate.StableKey.Value, summary.StableKey));
            ArchitectureEdge[] relatedEdges = snapshot.Edges
                .Where(edge => IsSemanticRelationship(edge) && (StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, summary.StableKey) || StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, summary.StableKey)))
                .OrderBy(static edge => edge.EdgeKind.Value, StringComparer.Ordinal)
                .ThenBy(static edge => edge.StableKey.Value, StringComparer.Ordinal)
                .ToArray();
            SymbolEvidenceReferenceDto[] evidence = BuildSymbolEvidence(snapshot, node, relatedEdges);
            SymbolRelationshipDto[] relationships = relatedEdges.Select(static edge => new SymbolRelationshipDto(
                edge.StableKey.Value,
                edge.EdgeKind.Value,
                edge.SourceNodeStableKey.Value,
                edge.TargetNodeStableKey.Value,
                BuildEvidenceStableKeys(null, [edge]),
                edge.Confidence.Value)).ToArray();
            List<SymbolUnknownDto> unknowns = [];
            if (node.UnknownState.HasUnknownData)
            {
                unknowns.Add(new SymbolUnknownDto("symbol", node.UnknownState.UnknownReason ?? "The symbol contains unresolved semantic data."));
            }

            if (evidence.Length == 0)
            {
                unknowns.Add(new SymbolUnknownDto("evidence", "No persisted evidence reference was available for this symbol."));
            }

            if (relationships.Length == 0)
            {
                unknowns.Add(new SymbolUnknownDto("relationships", "No persisted semantic relationships were available for this symbol."));
            }

            return new SymbolDetailDto(summary, evidence, relationships, [], unknowns);
        }

        /// <summary>
        /// Builds symbol usage rows from semantic relationships connected to the requested symbol.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="symbolStableKey">The stable key of the requested symbol.</param>
        /// <param name="direction">The normalized usage direction.</param>
        /// <returns>The complete unpaged usage rows.</returns>
        private static SymbolUsageDto[] BuildSymbolUsages(ExtractedArchitectureSnapshot snapshot, string symbolStableKey, string direction)
        {
            // Incoming usage answers who references/calls this symbol; outgoing usage answers what this symbol references/calls.
            IEnumerable<ArchitectureEdge> edges = snapshot.Edges.Where(edge => IsUsageRelationship(edge));
            edges = direction == "Outgoing"
                ? edges.Where(edge => StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, symbolStableKey))
                : edges.Where(edge => StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, symbolStableKey));
            return edges.Select(edge => BuildSymbolUsage(snapshot, edge)).ToArray();
        }

        /// <summary>
        /// Builds one symbol usage row from a semantic relationship and evidence record.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="edge">The semantic relationship being mapped.</param>
        /// <returns>The mapped symbol usage row.</returns>
        private static SymbolUsageDto BuildSymbolUsage(ExtractedArchitectureSnapshot snapshot, ArchitectureEdge edge)
        {
            // Usage evidence supplies file, line, and snippet data, but the snippet is bounded before it becomes public JSON.
            ArchitectureNode? source = FindNode(snapshot, edge.SourceNodeStableKey.Value);
            ArchitectureNode? target = FindNode(snapshot, edge.TargetNodeStableKey.Value);
            EvidenceRecord? evidence = FindEvidence(snapshot, edge.PrimaryEvidenceStableKey);
            string? snippetPreview = evidence is null ? null : BoundSnippetPreview(evidence.SnippetPreview);
            return new SymbolUsageDto(
                edge.StableKey.Value,
                edge.EdgeKind.Value,
                edge.SourceNodeStableKey.Value,
                edge.TargetNodeStableKey.Value,
                source?.DisplayName,
                target?.DisplayName,
                evidence?.FilePath.Value,
                evidence?.StartLine,
                evidence?.EndLine,
                snippetPreview,
                BuildEvidenceStableKeys(null, [edge]),
                edge.Confidence.Value,
                edge.UnknownState.HasUnknownData || evidence?.UnknownState.HasUnknownData == true,
                edge.UnknownState.UnknownReason ?? evidence?.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Builds safe evidence references for a symbol detail response.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="node">The selected symbol node.</param>
        /// <param name="relatedEdges">The related semantic edges that may carry additional evidence references.</param>
        /// <returns>The evidence references associated with the selected symbol.</returns>
        private static SymbolEvidenceReferenceDto[] BuildSymbolEvidence(ExtractedArchitectureSnapshot snapshot, ArchitectureNode node, IReadOnlyList<ArchitectureEdge> relatedEdges)
        {
            // Evidence keys are de-duplicated so the detail response can show node and relationship evidence without repeated references.
            string[] evidenceKeys = BuildEvidenceStableKeys(node, relatedEdges).ToArray();
            return evidenceKeys
                .Select(key => snapshot.Evidence.FirstOrDefault(evidence => StringComparer.Ordinal.Equals(evidence.StableKey.Value, key)))
                .Where(static evidence => evidence is not null)
                .Select(static evidence => BuildEvidenceReference(evidence!))
                .ToArray();
        }

        /// <summary>
        /// Builds one safe symbol evidence reference from a persisted evidence record.
        /// </summary>
        /// <param name="evidence">The evidence record being mapped.</param>
        /// <returns>The safe evidence reference DTO.</returns>
        private static SymbolEvidenceReferenceDto BuildEvidenceReference(EvidenceRecord evidence)
        {
            // Source text is untrusted display data, so only a bounded preview and hash are exposed.
            return new SymbolEvidenceReferenceDto(
                evidence.StableKey.Value,
                evidence.EvidenceKind.Value,
                evidence.FilePath.Value,
                evidence.StartLine,
                evidence.EndLine,
                evidence.SymbolName,
                evidence.ContainingSymbol,
                evidence.SnippetHash,
                BoundSnippetPreview(evidence.SnippetPreview),
                evidence.Confidence.Value);
        }

        /// <summary>
        /// Builds a bounded source-context DTO from one evidence record.
        /// </summary>
        /// <param name="evidence">The evidence record that supplies source context.</param>
        /// <returns>The bounded source context DTO.</returns>
        private static SymbolSourceContextDto BuildSourceContext(EvidenceRecord evidence)
        {
            // Source context intentionally includes location and a bounded preview only; callers never receive full source content.
            return new SymbolSourceContextDto(evidence.FilePath.Value, evidence.StartLine, evidence.EndLine, BoundSnippetPreview(evidence.SnippetPreview));
        }

        /// <summary>
        /// Builds de-duplicated evidence stable keys from a symbol node and related semantic edges.
        /// </summary>
        /// <param name="node">The optional symbol node with primary evidence.</param>
        /// <param name="edges">The related semantic edges with primary evidence.</param>
        /// <returns>The de-duplicated stable evidence keys.</returns>
        private static IReadOnlyList<string> BuildEvidenceStableKeys(ArchitectureNode? node, IReadOnlyList<ArchitectureEdge> edges)
        {
            // De-duplication preserves stable insertion order across node and relationship evidence sources.
            List<string> keys = [];
            AddIfPresent(keys, node?.PrimaryEvidenceStableKey?.Value);
            foreach (ArchitectureEdge edge in edges)
            {
                AddIfPresent(keys, edge.PrimaryEvidenceStableKey?.Value);
            }

            return keys.Distinct(StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Adds a non-empty string value to a list.
        /// </summary>
        /// <param name="values">The list receiving the value.</param>
        /// <param name="value">The optional value to add.</param>
        private static void AddIfPresent(List<string> values, string? value)
        {
            // Optional stable keys should be omitted rather than serialized as empty public identities.
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        /// <summary>
        /// Determines whether a graph node is part of the symbol query surface.
        /// </summary>
        /// <param name="node">The graph node to inspect.</param>
        /// <returns><see langword="true"/> when the node is a supported semantic symbol node; otherwise, <see langword="false"/>.</returns>
        private static bool IsSymbolNode(ArchitectureNode node)
        {
            // The controlled symbol surface currently exposes Roslyn namespace, type, method, property, and field facts.
            return s_symbolKinds.Contains(node.NodeKind.Value);
        }

        /// <summary>
        /// Determines whether a semantic symbol exists in a snapshot.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="symbolStableKey">The stable key to find.</param>
        /// <returns><see langword="true"/> when a matching symbol node exists; otherwise, <see langword="false"/>.</returns>
        private static bool SymbolExists(ExtractedArchitectureSnapshot snapshot, string symbolStableKey)
        {
            // Usage queries validate the requested target before reading relationships so missing symbols are client-correctable errors.
            return snapshot.Nodes.Any(node => IsSymbolNode(node) && StringComparer.Ordinal.Equals(node.StableKey.Value, symbolStableKey));
        }

        /// <summary>
        /// Finds one graph node by stable key.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="stableKey">The stable key to find.</param>
        /// <returns>The matching node, or null when no node exists.</returns>
        private static ArchitectureNode? FindNode(ExtractedArchitectureSnapshot snapshot, string stableKey)
        {
            // Usage rows include display names only when the related node is present in the selected snapshot.
            return snapshot.Nodes.FirstOrDefault(node => StringComparer.Ordinal.Equals(node.StableKey.Value, stableKey));
        }

        /// <summary>
        /// Finds one evidence record by optional stable key.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="stableKey">The optional evidence stable key to find.</param>
        /// <returns>The matching evidence record, or null when no key or record exists.</returns>
        private static EvidenceRecord? FindEvidence(ExtractedArchitectureSnapshot snapshot, StableKey? stableKey)
        {
            // Evidence lookup is optional because unresolved or partial semantic facts may not have persisted source context yet.
            return stableKey is null
                ? null
                : snapshot.Evidence.FirstOrDefault(evidence => evidence.StableKey.Equals(stableKey.Value));
        }

        /// <summary>
        /// Determines whether an edge should be exposed as a semantic relationship.
        /// </summary>
        /// <param name="edge">The edge to inspect.</param>
        /// <returns><see langword="true"/> when the edge kind is semantic-symbol related; otherwise, <see langword="false"/>.</returns>
        private static bool IsSemanticRelationship(ArchitectureEdge edge)
        {
            // Detail relationships include usage edges plus structural semantic edges so callers can inspect symbol context.
            return s_usageEdgeKinds.Contains(edge.EdgeKind.Value);
        }

        /// <summary>
        /// Determines whether an edge should be exposed by the symbol usage endpoint.
        /// </summary>
        /// <param name="edge">The edge to inspect.</param>
        /// <returns><see langword="true"/> when the edge kind is a symbol usage relationship; otherwise, <see langword="false"/>.</returns>
        private static bool IsUsageRelationship(ArchitectureEdge edge)
        {
            // Usage currently includes references, calls, implementations, inheritance, and handler relationships produced by semantic extraction.
            return s_usageEdgeKinds.Contains(edge.EdgeKind.Value);
        }

        /// <summary>
        /// Determines whether a sort field is supported by symbol search.
        /// </summary>
        /// <param name="sort">The caller-supplied sort field.</param>
        /// <returns><see langword="true"/> when the sort field is supported; otherwise, <see langword="false"/>.</returns>
        private static bool IsSupportedSort(string sort)
        {
            // The allowed sort list is intentionally fixed so callers cannot submit arbitrary property paths.
            return sort.Trim().ToLowerInvariant() is "name" or "kind" or "project" or "namespace" or "containingtype" or "language" or "confidence";
        }

        /// <summary>
        /// Determines whether a usage direction is supported.
        /// </summary>
        /// <param name="direction">The optional caller-supplied usage direction.</param>
        /// <returns><see langword="true"/> when the direction is supported; otherwise, <see langword="false"/>.</returns>
        private static bool IsSupportedUsageDirection(string? direction)
        {
            // Blank direction defaults to incoming usage because callers usually ask who references the selected symbol.
            return string.IsNullOrWhiteSpace(direction)
                || string.Equals(direction, "Incoming", StringComparison.OrdinalIgnoreCase)
                || string.Equals(direction, "Outgoing", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Normalizes usage direction to the canonical public value.
        /// </summary>
        /// <param name="direction">The optional caller-supplied usage direction.</param>
        /// <returns>The canonical usage direction.</returns>
        private static string NormalizeUsageDirection(string? direction)
        {
            // Incoming is the default symbol usage view and canonical casing keeps JSON responses stable.
            return string.Equals(direction, "Outgoing", StringComparison.OrdinalIgnoreCase) ? "Outgoing" : "Incoming";
        }

        /// <summary>
        /// Tests whether a candidate string contains search text using ordinal-ignore-case comparison.
        /// </summary>
        /// <param name="candidate">The optional candidate text.</param>
        /// <param name="searchText">The non-empty search text.</param>
        /// <returns><see langword="true"/> when the candidate contains the search text; otherwise, <see langword="false"/>.</returns>
        private static bool Contains(string? candidate, string searchText)
        {
            // Bounded contains matching is sufficient for stable symbol lookup without introducing a query language.
            return !string.IsNullOrWhiteSpace(candidate) && candidate.Contains(searchText.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads a string metadata property from canonical metadata JSON.
        /// </summary>
        /// <param name="metadata">The metadata object to inspect.</param>
        /// <param name="propertyName">The metadata property name to read.</param>
        /// <returns>The metadata string value, or null when no string value exists.</returns>
        private static string? MetadataString(GraphMetadata metadata, string propertyName)
        {
            // Metadata parsing is intentionally local and read-only so optional extraction details remain supplemental to normalized fields.
            using JsonDocument document = JsonDocument.Parse(metadata.ToCanonicalJson());
            return document.RootElement.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
        }

        /// <summary>
        /// Infers a namespace from a fully qualified symbol name when explicit metadata is unavailable.
        /// </summary>
        /// <param name="qualifiedName">The optional fully qualified symbol name.</param>
        /// <returns>The inferred namespace, or null when it cannot be determined.</returns>
        private static string? InferNamespace(string? qualifiedName)
        {
            // Namespace inference is intentionally conservative and stops before the final type or member segment.
            if (string.IsNullOrWhiteSpace(qualifiedName))
            {
                return null;
            }

            string[] parts = qualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length > 2 ? string.Join('.', parts.Take(parts.Length - 2)) : null;
        }

        /// <summary>
        /// Infers a containing type from metadata, parent node, or fully qualified symbol name.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="node">The symbol node being mapped.</param>
        /// <returns>The inferred containing type, or null when it cannot be determined.</returns>
        private static string? InferContainingType(ExtractedArchitectureSnapshot snapshot, ArchitectureNode node)
        {
            // Parent node identity is preferred over string parsing because extracted graph relationships are more explicit than naming heuristics.
            if (node.ParentNodeStableKey is not null)
            {
                ArchitectureNode? parent = snapshot.Nodes.FirstOrDefault(candidate => candidate.StableKey.Equals(node.ParentNodeStableKey.Value));
                if (parent is not null && parent.NodeKind == NodeKind.Type)
                {
                    return parent.QualifiedName ?? parent.DisplayName;
                }
            }

            if (node.NodeKind == NodeKind.Type || string.IsNullOrWhiteSpace(node.QualifiedName))
            {
                return null;
            }

            string[] parts = node.QualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length > 1 ? parts[^2] : null;
        }

        /// <summary>
        /// Bounds a source snippet preview for safe public display.
        /// </summary>
        /// <param name="snippetPreview">The optional source snippet preview from evidence.</param>
        /// <returns>The bounded snippet preview, or null when no meaningful preview exists.</returns>
        private static string? BoundSnippetPreview(string? snippetPreview)
        {
            // Snippets are source text and therefore untrusted; length bounding prevents large payloads and accidental secret expansion.
            if (string.IsNullOrWhiteSpace(snippetPreview))
            {
                return null;
            }

            string normalized = snippetPreview.Trim();
            return normalized.Length <= SymbolQueryLimits.MaximumSnippetPreviewLength
                ? normalized
                : normalized[..SymbolQueryLimits.MaximumSnippetPreviewLength];
        }

        /// <summary>
        /// Merges one unknown into a symbol query context without mutating the original context.
        /// </summary>
        /// <param name="context">The original symbol query context.</param>
        /// <param name="unknown">The unknown field to add.</param>
        /// <returns>A new context containing the additional unknown.</returns>
        private static SymbolQueryContext MergeUnknownContext(SymbolQueryContext context, SymbolUnknownDto unknown)
        {
            // Context is immutable, so merging creates a new context with de-duplicated unknown rows for response mapping.
            SymbolUnknownDto[] unknowns = context.Unknowns
                .Concat([unknown])
                .DistinctBy(static item => item.Field + "\u001f" + item.Reason)
                .ToArray();
            return new SymbolQueryContext(context.Scope, context.Snapshot, context.Warnings, unknowns);
        }

        /// <summary>
        /// Represents the internal outcome of snapshot resolution.
        /// </summary>
        private sealed class SnapshotResolution
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="SnapshotResolution"/> class.
            /// </summary>
            /// <param name="snapshot">The selected snapshot when resolution succeeds.</param>
            /// <param name="validationErrors">The validation errors when resolution fails.</param>
            private SnapshotResolution(ExtractedArchitectureSnapshot? snapshot, IReadOnlyList<SymbolQueryValidationError> validationErrors)
            {
                // Resolution is either successful with a snapshot or failed with validation errors; callers never inspect both.
                Snapshot = snapshot;
                ValidationErrors = validationErrors;
            }

            /// <summary>
            /// Gets a value indicating whether snapshot resolution succeeded.
            /// </summary>
            public bool Succeeded => ValidationErrors.Count == 0;

            /// <summary>
            /// Gets the selected snapshot when resolution succeeds.
            /// </summary>
            public ExtractedArchitectureSnapshot? Snapshot { get; }

            /// <summary>
            /// Gets the validation errors when resolution fails.
            /// </summary>
            public IReadOnlyList<SymbolQueryValidationError> ValidationErrors { get; }

            /// <summary>
            /// Creates a successful snapshot resolution.
            /// </summary>
            /// <param name="snapshot">The selected snapshot.</param>
            /// <param name="scopedSnapshots">The scoped snapshot set retained for parity with other query services.</param>
            /// <returns>A successful snapshot resolution.</returns>
            public static SnapshotResolution Success(ExtractedArchitectureSnapshot snapshot, IReadOnlyList<ExtractedArchitectureSnapshot> scopedSnapshots)
            {
                // The scoped snapshot set is accepted to keep the internal shape aligned with existing WP014 services for future extension.
                ArgumentNullException.ThrowIfNull(scopedSnapshots);
                return new SnapshotResolution(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), []);
            }

            /// <summary>
            /// Creates a failed snapshot resolution.
            /// </summary>
            /// <param name="validationErrors">The validation errors that explain the failure.</param>
            /// <returns>A failed snapshot resolution.</returns>
            public static SnapshotResolution Failed(IReadOnlyList<SymbolQueryValidationError> validationErrors)
            {
                // Failure records are copied through the caller-provided read-only list and carry no snapshot payload.
                return new SnapshotResolution(null, validationErrors ?? throw new ArgumentNullException(nameof(validationErrors)));
            }
        }
    }
}
