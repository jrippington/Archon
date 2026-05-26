using System.Text.Json;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Runtime
{
    /// <summary>
    /// Implements controlled runtime endpoint, controller/handler, entry-point, and worker query behavior over extracted architecture snapshots.
    /// </summary>
    public sealed class RuntimeQueryService : IRuntimeQueryService
    {
        /// <summary>
        /// Reads snapshots from the registered architecture snapshot writer when in-memory diagnostics are available.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Defines supported runtime kind filters for entry-point queries.
        /// </summary>
        private static readonly HashSet<string> s_runtimeKinds = new(StringComparer.OrdinalIgnoreCase)
        {
            "Api",
            "Worker",
            "Console",
            "ServiceHost"
        };

        /// <summary>
        /// Defines supported worker kind filters for worker queries.
        /// </summary>
        private static readonly HashSet<string> s_workerKinds = new(StringComparer.OrdinalIgnoreCase)
        {
            "HostedService",
            "BackgroundService",
            "QueueConsumer",
            "TopicConsumer",
            "ScheduledJob"
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="RuntimeQueryService"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        public RuntimeQueryService(IArchitectureSnapshotWriter snapshotWriter)
        {
            // Runtime queries use the same snapshot seam as earlier WP014 slices so tests and local hosts do not require Neo4j.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
        }

        /// <inheritdoc />
        public Task<RuntimeEndpointResult> ListEndpointsAsync(RuntimeEndpointQuery query, CancellationToken cancellationToken)
        {
            // Endpoint lookup validates options, resolves one snapshot, maps endpoint nodes, then applies controlled filters and paging.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<RuntimeQueryValidationError> optionErrors = ValidatePagingAndSortOptions(query.Sort, query.Skip, query.Take);
            if (optionErrors.Count > 0)
            {
                return Task.FromResult(new RuntimeEndpointResult(optionErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new RuntimeEndpointResult(resolution.ValidationErrors));
            }

            RuntimeQueryContext context = BuildContext(query.Selector, resolution);
            RuntimeEndpointDto[] allItems = BuildEndpointItems(resolution.Snapshot!);
            RuntimeEndpointDto[] filtered = ApplyEndpointFilters(allItems, query).ToArray();
            RuntimeEndpointDto[] ordered = ApplyEndpointOrdering(filtered, query).ToArray();
            RuntimeEndpointDto[] pageItems = ordered.Skip(query.Skip).Take(query.Take).ToArray();
            PagedQueryResult<RuntimeEndpointDto> page = new(pageItems, ordered.Length, query.Skip, query.Take);
            RuntimeQueryContext responseContext = ordered.Length == 0
                ? MergeUnknownContext(context, new RuntimeUnknownDto("runtimeEndpoints", "No persisted endpoint facts matched the selected runtime query scope."))
                : context;
            return Task.FromResult(new RuntimeEndpointResult(page, responseContext));
        }

        /// <inheritdoc />
        public Task<ControllerHandlerResult> GetControllerOrHandlerAsync(ControllerHandlerQuery query, CancellationToken cancellationToken)
        {
            // Controller/handler detail lookup requires one identity so callers do not accidentally mix stable-key and display-name semantics.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<RuntimeQueryValidationError> identityErrors = ValidateControllerHandlerIdentity(query);
            if (identityErrors.Count > 0)
            {
                return Task.FromResult(new ControllerHandlerResult(identityErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new ControllerHandlerResult(resolution.ValidationErrors));
            }

            RuntimeQueryContext context = BuildContext(query.Selector, resolution);
            ArchitectureNode[] matches = ResolveControllerHandlerMatches(resolution.Snapshot!, query);
            if (matches.Length == 0)
            {
                RuntimeQueryValidationError error = new(RuntimeQueryValidationCodes.ControllerOrHandlerNotFound, "The requested controller or handler was not found in the selected snapshot scope.");
                return Task.FromResult(new ControllerHandlerResult([error]));
            }

            if (matches.Length > 1)
            {
                RuntimeQueryValidationError error = new(RuntimeQueryValidationCodes.ControllerOrHandlerNotFound, "The requested controller or handler name matched multiple runtime nodes; use a stable key to disambiguate.");
                return Task.FromResult(new ControllerHandlerResult([error]));
            }

            ControllerHandlerDetailDto detail = BuildControllerHandlerDetail(resolution.Snapshot!, matches[0]);
            return Task.FromResult(new ControllerHandlerResult(detail, context));
        }

        /// <inheritdoc />
        public Task<RuntimeEntryPointResult> ListEntryPointsAsync(RuntimeEntryPointQuery query, CancellationToken cancellationToken)
        {
            // Entry-point lookup exposes runtime bootstrap facts without introducing UI or host-specific graph browsing behavior.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<RuntimeQueryValidationError> optionErrors = ValidateEntryPointOptions(query);
            if (optionErrors.Count > 0)
            {
                return Task.FromResult(new RuntimeEntryPointResult(optionErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new RuntimeEntryPointResult(resolution.ValidationErrors));
            }

            RuntimeQueryContext context = BuildContext(query.Selector, resolution);
            RuntimeEntryPointDto[] allItems = BuildEntryPointItems(resolution.Snapshot!);
            RuntimeEntryPointDto[] filtered = allItems
                .Where(item => string.IsNullOrWhiteSpace(query.RuntimeKind) || string.Equals(item.RuntimeKind, query.RuntimeKind.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(item => string.IsNullOrWhiteSpace(query.ProjectStableKey) || StringComparer.Ordinal.Equals(item.ProjectStableKey, query.ProjectStableKey.Trim()))
                .OrderBy(static item => item.ProjectStableKey ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.RuntimeKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.StableKey, StringComparer.Ordinal)
                .ToArray();
            RuntimeEntryPointDto[] pageItems = filtered.Skip(query.Skip).Take(query.Take).ToArray();
            PagedQueryResult<RuntimeEntryPointDto> page = new(pageItems, filtered.Length, query.Skip, query.Take);
            RuntimeQueryContext responseContext = filtered.Length == 0
                ? MergeUnknownContext(context, new RuntimeUnknownDto("runtimeEntryPoints", "No persisted runtime entry-point facts matched the selected query scope."))
                : context;
            return Task.FromResult(new RuntimeEntryPointResult(page, responseContext));
        }

        /// <inheritdoc />
        public Task<WorkerResult> ListWorkersAsync(WorkerQuery query, CancellationToken cancellationToken)
        {
            // Worker lookup gathers hosted services, background services, queues, topics, and scheduled jobs into bounded worker records.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<RuntimeQueryValidationError> optionErrors = ValidateWorkerOptions(query);
            if (optionErrors.Count > 0)
            {
                return Task.FromResult(new WorkerResult(optionErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new WorkerResult(resolution.ValidationErrors));
            }

            RuntimeQueryContext context = BuildContext(query.Selector, resolution);
            WorkerDto[] allItems = BuildWorkerItems(resolution.Snapshot!);
            WorkerDto[] filtered = ApplyWorkerFilters(allItems, query)
                .OrderBy(static item => item.ProjectStableKey ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.WorkerKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.StableKey, StringComparer.Ordinal)
                .ToArray();
            WorkerDto[] pageItems = filtered.Skip(query.Skip).Take(query.Take).ToArray();
            PagedQueryResult<WorkerDto> page = new(pageItems, filtered.Length, query.Skip, query.Take);
            RuntimeQueryContext responseContext = filtered.Length == 0
                ? MergeUnknownContext(context, new RuntimeUnknownDto("workers", "No persisted worker, hosted-service, queue-consumer, topic-consumer, or scheduled-job facts matched the selected query scope."))
                : context;
            return Task.FromResult(new WorkerResult(page, responseContext));
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
        /// Resolves and validates the selected runtime snapshot scope.
        /// </summary>
        /// <param name="selector">The repository, solution, and snapshot selector supplied by the query.</param>
        /// <returns>A successful snapshot resolution or deterministic validation errors.</returns>
        private SnapshotResolution ResolveSnapshot(RuntimeSnapshotSelector selector)
        {
            // Scope validation runs before graph matching so missing or malformed selectors produce client-correctable problem details.
            List<RuntimeQueryValidationError> validationErrors = ValidateSelector(selector);
            if (validationErrors.Count > 0)
            {
                return SnapshotResolution.Failed(validationErrors);
            }

            ExtractedArchitectureSnapshot[] repositorySnapshots = GetSnapshots()
                .Where(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.RepositoryStableKey.Value, selector.RepositoryStableKey))
                .ToArray();
            if (repositorySnapshots.Length == 0)
            {
                RuntimeQueryValidationError error = new(RuntimeQueryValidationCodes.RepositoryNotFound, "The requested repository scope was not found.");
                return SnapshotResolution.Failed([error]);
            }

            ExtractedArchitectureSnapshot[] scopedSnapshots = ApplySolutionScope(repositorySnapshots, selector);
            if (selector.SolutionStableKey is not null && scopedSnapshots.Length == 0)
            {
                RuntimeQueryValidationError error = new(RuntimeQueryValidationCodes.SolutionNotFound, "The requested solution scope was not found for the repository.");
                return SnapshotResolution.Failed([error]);
            }

            ExtractedArchitectureSnapshot? selectedSnapshot = ResolveSelectedSnapshot(scopedSnapshots, selector);
            if (selectedSnapshot?.SnapshotHeader is null)
            {
                RuntimeQueryValidationError error = new(RuntimeQueryValidationCodes.SnapshotNotFound, "The requested snapshot scope was not found.");
                return SnapshotResolution.Failed([error]);
            }

            return SnapshotResolution.Success(selectedSnapshot, scopedSnapshots);
        }

        /// <summary>
        /// Validates selector syntax before any snapshot matching occurs.
        /// </summary>
        /// <param name="selector">The caller-supplied runtime snapshot selector.</param>
        /// <returns>A deterministic list of selector validation errors.</returns>
        private static List<RuntimeQueryValidationError> ValidateSelector(RuntimeSnapshotSelector selector)
        {
            // Repository scope is required because latest resolution must be bounded to one repository.
            List<RuntimeQueryValidationError> errors = [];
            if (selector.RepositoryStableKey is null)
            {
                errors.Add(new RuntimeQueryValidationError(RuntimeQueryValidationCodes.RepositoryStableKeyRequired, "A repository stable key is required for runtime queries."));
            }

            if (!selector.RequestsLatestSnapshot && !selector.SnapshotStableKey.StartsWith("snapshot://", StringComparison.Ordinal))
            {
                errors.Add(new RuntimeQueryValidationError(RuntimeQueryValidationCodes.SnapshotSelectorInvalid, "Snapshot selector must be 'latest', 'current', or a snapshot:// stable key."));
            }

            return errors;
        }

        /// <summary>
        /// Applies the optional solution scope to repository snapshots.
        /// </summary>
        /// <param name="repositorySnapshots">The snapshots already matched to the requested repository.</param>
        /// <param name="selector">The caller-supplied runtime snapshot selector.</param>
        /// <returns>The snapshots matching the optional solution scope.</returns>
        private static ExtractedArchitectureSnapshot[] ApplySolutionScope(IEnumerable<ExtractedArchitectureSnapshot> repositorySnapshots, RuntimeSnapshotSelector selector)
        {
            // Solution scope is resolved through snapshot-level solution facts just like existing WP014 query scope resolution.
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
        /// <param name="selector">The caller-supplied runtime snapshot selector.</param>
        /// <returns>The selected snapshot, or null when none matches.</returns>
        private static ExtractedArchitectureSnapshot? ResolveSelectedSnapshot(IEnumerable<ExtractedArchitectureSnapshot> scopedSnapshots, RuntimeSnapshotSelector selector)
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
        /// Validates shared paging and endpoint sort values.
        /// </summary>
        /// <param name="sort">The optional endpoint sort field.</param>
        /// <param name="skip">The requested skip value.</param>
        /// <param name="take">The requested take value.</param>
        /// <returns>A deterministic list of option validation errors.</returns>
        private static List<RuntimeQueryValidationError> ValidatePagingAndSortOptions(string? sort, int skip, int take)
        {
            // Option validation happens before snapshot lookup so malformed automation can repair fixed query parameters safely.
            List<RuntimeQueryValidationError> errors = [];
            if (!string.IsNullOrWhiteSpace(sort) && !IsSupportedEndpointSort(sort))
            {
                errors.Add(new RuntimeQueryValidationError(RuntimeQueryValidationCodes.SortUnsupported, "Runtime endpoint sort must be route, method, project, controller, handler, or confidence."));
            }

            AddPagingErrors(skip, take, errors);
            return errors;
        }

        /// <summary>
        /// Validates entry-point query options before snapshot work starts.
        /// </summary>
        /// <param name="query">The entry-point query supplied by the caller.</param>
        /// <returns>A deterministic list of option validation errors.</returns>
        private static List<RuntimeQueryValidationError> ValidateEntryPointOptions(RuntimeEntryPointQuery query)
        {
            // Runtime kind filters are controlled vocabulary values so clients cannot invent host classifications through query text.
            List<RuntimeQueryValidationError> errors = [];
            if (!string.IsNullOrWhiteSpace(query.RuntimeKind) && !s_runtimeKinds.Contains(query.RuntimeKind.Trim()))
            {
                errors.Add(new RuntimeQueryValidationError(RuntimeQueryValidationCodes.RuntimeKindUnsupported, "Runtime kind must be Api, Worker, Console, or ServiceHost."));
            }

            AddPagingErrors(query.Skip, query.Take, errors);
            return errors;
        }

        /// <summary>
        /// Validates worker query options before snapshot work starts.
        /// </summary>
        /// <param name="query">The worker query supplied by the caller.</param>
        /// <returns>A deterministic list of option validation errors.</returns>
        private static List<RuntimeQueryValidationError> ValidateWorkerOptions(WorkerQuery query)
        {
            // Worker kind filters are controlled vocabulary values because queue and schedule classification is extraction-owned.
            List<RuntimeQueryValidationError> errors = [];
            if (!string.IsNullOrWhiteSpace(query.WorkerKind) && !s_workerKinds.Contains(query.WorkerKind.Trim()))
            {
                errors.Add(new RuntimeQueryValidationError(RuntimeQueryValidationCodes.WorkerKindUnsupported, "Worker kind must be HostedService, BackgroundService, QueueConsumer, TopicConsumer, or ScheduledJob."));
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
        private static void AddPagingErrors(int skip, int take, List<RuntimeQueryValidationError> errors)
        {
            // Paging bounds keep query responses predictable and prevent accidental large runtime fact reads.
            if (skip < 0)
            {
                errors.Add(new RuntimeQueryValidationError(RuntimeQueryValidationCodes.SkipInvalid, "Runtime query skip must be greater than or equal to zero."));
            }

            if (take < 1 || take > RuntimeQueryLimits.MaximumTake)
            {
                errors.Add(new RuntimeQueryValidationError(RuntimeQueryValidationCodes.TakeInvalid, $"Runtime query take must be between 1 and {RuntimeQueryLimits.MaximumTake}."));
            }
        }

        /// <summary>
        /// Validates controller/handler detail identity fields before snapshot work starts.
        /// </summary>
        /// <param name="query">The controller/handler query supplied by the caller.</param>
        /// <returns>A deterministic list of identity validation errors.</returns>
        private static List<RuntimeQueryValidationError> ValidateControllerHandlerIdentity(ControllerHandlerQuery query)
        {
            // Detail lookup must be explicit because display-name lookup can be ambiguous across controllers, minimal handlers, and methods.
            List<RuntimeQueryValidationError> errors = [];
            if (string.IsNullOrWhiteSpace(query.StableKey) && string.IsNullOrWhiteSpace(query.Name))
            {
                errors.Add(new RuntimeQueryValidationError(RuntimeQueryValidationCodes.ControllerOrHandlerIdentityRequired, "A controller or handler stable key or exact name is required."));
            }

            if (!string.IsNullOrWhiteSpace(query.StableKey) && !string.IsNullOrWhiteSpace(query.Name))
            {
                errors.Add(new RuntimeQueryValidationError(RuntimeQueryValidationCodes.ControllerOrHandlerIdentityRequired, "Use either controller/handler stable key or name, not both."));
            }

            return errors;
        }

        /// <summary>
        /// Determines whether an endpoint sort field is supported.
        /// </summary>
        /// <param name="sort">The sort field supplied by the caller.</param>
        /// <returns><see langword="true"/> when the sort field is supported; otherwise, <see langword="false"/>.</returns>
        private static bool IsSupportedEndpointSort(string sort)
        {
            // Endpoint sort fields are intentionally fixed so response ordering is deterministic and testable.
            return sort.Trim().ToLowerInvariant() is "route" or "method" or "project" or "controller" or "handler" or "confidence";
        }

        /// <summary>
        /// Builds the runtime query context shared by API envelopes.
        /// </summary>
        /// <param name="selector">The caller-supplied runtime snapshot selector.</param>
        /// <param name="resolution">The successful snapshot resolution.</param>
        /// <returns>The runtime query context for response mapping.</returns>
        private static RuntimeQueryContext BuildContext(RuntimeSnapshotSelector selector, SnapshotResolution resolution)
        {
            // Context construction centralizes envelope metadata so runtime endpoints report scope consistently.
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
            RuntimeWarningDto[] warnings = snapshot.Warnings.Select(static warning => new RuntimeWarningDto("SnapshotWarning", warning)).ToArray();
            List<RuntimeUnknownDto> unknowns = [];
            if (snapshot.Errors.Any())
            {
                unknowns.Add(new RuntimeUnknownDto("runtimeExtraction", "The selected snapshot contains extraction errors, so runtime data may be incomplete."));
            }

            if (!snapshot.Nodes.Any(static node => IsRuntimeNode(node)))
            {
                unknowns.Add(new RuntimeUnknownDto("runtimeFacts", "No persisted endpoint, controller, hosted-service, queue, topic, or runtime entry-point nodes were available in the selected snapshot."));
            }

            return new RuntimeQueryContext(scope, snapshotMetadata, warnings, unknowns);
        }

        /// <summary>
        /// Builds all endpoint rows from the selected snapshot.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <returns>The complete unfiltered endpoint rows for the snapshot.</returns>
        private static RuntimeEndpointDto[] BuildEndpointItems(ExtractedArchitectureSnapshot snapshot)
        {
            // Endpoint nodes are the authoritative public runtime surface source; related edges enrich services and data-use metadata.
            return snapshot.Nodes
                .Where(static node => node.NodeKind == NodeKind.Endpoint)
                .Select(node => BuildEndpointItem(snapshot, node))
                .OrderBy(static endpoint => endpoint.Route ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static endpoint => endpoint.HttpMethod ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static endpoint => endpoint.StableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Builds one endpoint row from a graph node and its related facts.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="node">The endpoint node being mapped.</param>
        /// <returns>The mapped runtime endpoint row.</returns>
        private static RuntimeEndpointDto BuildEndpointItem(ExtractedArchitectureSnapshot snapshot, ArchitectureNode node)
        {
            // Endpoint metadata carries extractor-specific HTTP and DTO detail while graph edges identify related services and data dependencies.
            string endpointKey = node.StableKey.Value;
            ArchitectureEdge[] relatedEdges = snapshot.Edges
                .Where(edge => StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, endpointKey) || StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, endpointKey))
                .ToArray();
            List<string> evidenceStableKeys = BuildEvidenceStableKeys(node, relatedEdges);
            string? controllerName = MetadataString(node.Metadata, "controller") ?? MetadataString(node.Metadata, "controllerName") ?? RelatedNodeName(snapshot, relatedEdges, NodeKind.Controller);
            string? handlerName = MetadataString(node.Metadata, "handler") ?? MetadataString(node.Metadata, "handlerName") ?? RelatedHandlerName(snapshot, relatedEdges);
            return new RuntimeEndpointDto(
                endpointKey,
                MetadataString(node.Metadata, "httpMethod") ?? MetadataString(node.Metadata, "method"),
                MetadataString(node.Metadata, "route") ?? node.QualifiedName ?? node.DisplayName,
                node.ProjectStableKey?.Value,
                controllerName,
                handlerName,
                MetadataString(node.Metadata, "action") ?? MetadataString(node.Metadata, "actionName"),
                MetadataString(node.Metadata, "methodName"),
                MetadataString(node.Metadata, "requestDto") ?? MetadataString(node.Metadata, "requestType"),
                MetadataString(node.Metadata, "responseDto") ?? MetadataString(node.Metadata, "responseType"),
                MetadataStrings(node.Metadata, "authorizationAttributes", "authorization", "authorizeAttributes"),
                BuildRelatedNames(snapshot, relatedEdges, EdgeKind.Injects, EdgeKind.Calls, EdgeKind.References),
                BuildDataAccessIndicators(snapshot, node.ProjectStableKey?.Value, endpointKey),
                BuildConfigurationKeys(snapshot, node.ProjectStableKey?.Value, endpointKey),
                evidenceStableKeys,
                node.Confidence.Value,
                node.UnknownState.HasUnknownData,
                node.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Applies controlled endpoint filters to endpoint rows.
        /// </summary>
        /// <param name="items">The complete endpoint rows.</param>
        /// <param name="query">The normalized endpoint query.</param>
        /// <returns>The filtered endpoint rows.</returns>
        private static IEnumerable<RuntimeEndpointDto> ApplyEndpointFilters(IEnumerable<RuntimeEndpointDto> items, RuntimeEndpointQuery query)
        {
            // Filters are fixed and exact where practical, with text contains limited to route and handler/controller identity fields.
            return items
                .Where(item => string.IsNullOrWhiteSpace(query.HttpMethod) || string.Equals(item.HttpMethod, query.HttpMethod.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(item => string.IsNullOrWhiteSpace(query.Route) || Contains(item.Route, query.Route))
                .Where(item => string.IsNullOrWhiteSpace(query.ProjectStableKey) || StringComparer.Ordinal.Equals(item.ProjectStableKey, query.ProjectStableKey.Trim()))
                .Where(item => string.IsNullOrWhiteSpace(query.ControllerOrHandler) || Contains(item.ControllerName, query.ControllerOrHandler) || Contains(item.HandlerName, query.ControllerOrHandler) || Contains(item.ActionName, query.ControllerOrHandler) || Contains(item.MethodName, query.ControllerOrHandler))
                .Where(item => string.IsNullOrWhiteSpace(query.Authorization) || item.AuthorizationAttributes.Any(value => Contains(value, query.Authorization)));
        }

        /// <summary>
        /// Applies deterministic endpoint ordering.
        /// </summary>
        /// <param name="items">The filtered endpoint rows.</param>
        /// <param name="query">The normalized endpoint query.</param>
        /// <returns>The deterministically ordered endpoint rows.</returns>
        private static IEnumerable<RuntimeEndpointDto> ApplyEndpointOrdering(IEnumerable<RuntimeEndpointDto> items, RuntimeEndpointQuery query)
        {
            // Stable-key tie-breakers prevent paging drift when multiple endpoints share a route or controller.
            IOrderedEnumerable<RuntimeEndpointDto> ordered = (query.Sort ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "method" => Order(items, query.Descending, static item => item.HttpMethod ?? string.Empty),
                "project" => Order(items, query.Descending, static item => item.ProjectStableKey ?? string.Empty),
                "controller" => Order(items, query.Descending, static item => item.ControllerName ?? string.Empty),
                "handler" => Order(items, query.Descending, static item => item.HandlerName ?? item.MethodName ?? string.Empty),
                "confidence" => Order(items, query.Descending, static item => item.Confidence),
                _ => Order(items, query.Descending, static item => item.Route ?? string.Empty)
            };
            return ordered.ThenBy(static item => item.StableKey, StringComparer.Ordinal);
        }

        /// <summary>
        /// Resolves controller or handler lookup matches by stable key or exact display name.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="query">The normalized controller/handler detail query.</param>
        /// <returns>The matched controller or handler nodes.</returns>
        private static ArchitectureNode[] ResolveControllerHandlerMatches(ExtractedArchitectureSnapshot snapshot, ControllerHandlerQuery query)
        {
            // Stable-key lookup is exact; name lookup intentionally returns all exact name matches for safe ambiguity handling.
            IEnumerable<ArchitectureNode> candidates = snapshot.Nodes.Where(static node => IsControllerOrHandlerNode(node));
            return !string.IsNullOrWhiteSpace(query.StableKey)
                ? candidates.Where(node => StringComparer.Ordinal.Equals(node.StableKey.Value, query.StableKey.Trim())).ToArray()
                : candidates.Where(node => string.Equals(node.DisplayName, query.Name?.Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(node.QualifiedName, query.Name?.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        /// <summary>
        /// Builds one controller or handler detail response from a runtime node and related graph facts.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="node">The selected controller or handler node.</param>
        /// <returns>The mapped controller or handler detail response.</returns>
        private static ControllerHandlerDetailDto BuildControllerHandlerDetail(ExtractedArchitectureSnapshot snapshot, ArchitectureNode node)
        {
            // Detail sections are derived from direct stable relationships only so callers can inspect controller behavior without arbitrary traversal.
            string nodeKey = node.StableKey.Value;
            ArchitectureEdge[] relatedEdges = snapshot.Edges
                .Where(edge => StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, nodeKey) || StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, nodeKey))
                .ToArray();
            RuntimeEndpointDto[] endpoints = BuildEndpointItems(snapshot)
                .Where(endpoint => relatedEdges.Any(edge => StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, endpoint.StableKey) || StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, endpoint.StableKey)) || string.Equals(endpoint.ControllerName, node.DisplayName, StringComparison.OrdinalIgnoreCase) || string.Equals(endpoint.HandlerName, node.DisplayName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            RuntimeEvidenceReferenceDto[] evidence = BuildEvidenceReferences(snapshot, BuildEvidenceStableKeys(node, relatedEdges));
            return new ControllerHandlerDetailDto(
                nodeKey,
                node.DisplayName,
                node.NodeKind.Value,
                node.ProjectStableKey?.Value,
                node.QualifiedName,
                endpoints,
                BuildRelatedNames(snapshot, relatedEdges, EdgeKind.Injects, EdgeKind.Calls, EdgeKind.References),
                BuildDataAccessIndicators(snapshot, node.ProjectStableKey?.Value, nodeKey),
                BuildConfigurationKeys(snapshot, node.ProjectStableKey?.Value, nodeKey),
                evidence,
                PublicMetadataSanitizer.Sanitize(node.Metadata),
                node.Confidence.Value,
                node.UnknownState.HasUnknownData,
                node.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Builds runtime entry-point rows from project and runtime nodes.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <returns>The complete unfiltered entry-point rows for the snapshot.</returns>
        private static RuntimeEntryPointDto[] BuildEntryPointItems(ExtractedArchitectureSnapshot snapshot)
        {
            // Projects carry application-type metadata in existing slices; explicit runtime nodes enrich hosted-service and endpoint associations.
            ArchitectureNode[] projectNodes = snapshot.Nodes.Where(static node => node.NodeKind == NodeKind.Project).ToArray();
            ArchitectureNode[] explicitRuntimeNodes = snapshot.Nodes.Where(static node => node.NodeKind == NodeKind.OpenApiDocument || node.NodeKind == NodeKind.Dockerfile || node.NodeKind == NodeKind.Pipeline).ToArray();
            return projectNodes.Select(project => BuildProjectEntryPoint(snapshot, project))
                .Concat(explicitRuntimeNodes.Select(node => BuildNodeEntryPoint(snapshot, node)))
                .OrderBy(static item => item.ProjectStableKey ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.RuntimeKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.StableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Builds a runtime entry-point row from a project node.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="project">The project node being mapped as an entry point.</param>
        /// <returns>The mapped runtime entry-point row.</returns>
        private static RuntimeEntryPointDto BuildProjectEntryPoint(ExtractedArchitectureSnapshot snapshot, ArchitectureNode project)
        {
            // Application type and runtime metadata identify the host style while owned runtime nodes provide supporting service and endpoint links.
            string projectKey = project.StableKey.Value;
            ArchitectureNode[] ownedNodes = snapshot.Nodes.Where(node => StringComparer.Ordinal.Equals(node.ProjectStableKey?.Value, projectKey)).ToArray();
            string runtimeKind = NormalizeRuntimeKind(MetadataString(project.Metadata, "application.type") ?? MetadataString(project.Metadata, "runtimeKind") ?? MetadataString(project.Metadata, "runtime.kind"));
            return new RuntimeEntryPointDto(
                $"entrypoint://{projectKey}",
                project.DisplayName,
                runtimeKind,
                projectKey,
                project.DisplayName,
                MetadataString(project.Metadata, "entryPoint") ?? MetadataString(project.Metadata, "entryMethod") ?? MetadataString(project.Metadata, "programType"),
                ownedNodes.Where(static node => node.NodeKind == NodeKind.HostedService).Select(static node => node.StableKey.Value).Order(StringComparer.Ordinal).ToArray(),
                ownedNodes.Where(static node => node.NodeKind == NodeKind.Endpoint).Select(static node => node.StableKey.Value).Order(StringComparer.Ordinal).ToArray(),
                BuildConfigurationKeys(snapshot, projectKey, projectKey),
                BuildEvidenceStableKeys(project, []),
                project.Confidence.Value,
                project.UnknownState.HasUnknownData,
                project.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Builds a runtime entry-point row from an explicit runtime-like node.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="node">The runtime-like node being mapped.</param>
        /// <returns>The mapped runtime entry-point row.</returns>
        private static RuntimeEntryPointDto BuildNodeEntryPoint(ExtractedArchitectureSnapshot snapshot, ArchitectureNode node)
        {
            // Explicit runtime nodes preserve their own stable identity while still reporting associated project-level configuration keys.
            string? projectKey = node.ProjectStableKey?.Value;
            return new RuntimeEntryPointDto(
                node.StableKey.Value,
                node.DisplayName,
                NormalizeRuntimeKind(MetadataString(node.Metadata, "runtimeKind") ?? node.NodeKind.Value),
                projectKey,
                FindNode(snapshot, projectKey)?.DisplayName,
                MetadataString(node.Metadata, "entryPoint") ?? MetadataString(node.Metadata, "entryMethod"),
                [],
                [],
                BuildConfigurationKeys(snapshot, projectKey, node.StableKey.Value),
                BuildEvidenceStableKeys(node, []),
                node.Confidence.Value,
                node.UnknownState.HasUnknownData,
                node.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Builds worker rows from hosted-service, queue/topic, and scheduled-job runtime facts.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <returns>The complete unfiltered worker rows for the snapshot.</returns>
        private static WorkerDto[] BuildWorkerItems(ExtractedArchitectureSnapshot snapshot)
        {
            // Worker rows are grouped by hosted service where possible and by queue/topic/scheduled-job facts when a hosted service is unavailable.
            List<WorkerDto> workers = [];
            foreach (ArchitectureNode hostedService in snapshot.Nodes.Where(static node => node.NodeKind == NodeKind.HostedService))
            {
                workers.Add(BuildHostedServiceWorker(snapshot, hostedService));
            }

            foreach (ArchitectureNode consumerNode in snapshot.Nodes.Where(static node => node.NodeKind == NodeKind.Queue || node.NodeKind == NodeKind.Topic))
            {
                bool alreadyCovered = workers.Any(worker => worker.QueueConsumers.Any(consumer => StringComparer.Ordinal.Equals(consumer.StableKey, consumerNode.StableKey.Value)));
                if (!alreadyCovered)
                {
                    workers.Add(BuildConsumerWorker(snapshot, consumerNode));
                }
            }

            foreach (ArchitectureNode scheduledJob in snapshot.Nodes.Where(static node => IsScheduledJobNode(node)))
            {
                bool alreadyCovered = workers.Any(worker => worker.ScheduledJobs.Any(job => StringComparer.Ordinal.Equals(job.StableKey, scheduledJob.StableKey.Value)));
                if (!alreadyCovered)
                {
                    workers.Add(BuildScheduledJobWorker(snapshot, scheduledJob));
                }
            }

            return workers.ToArray();
        }

        /// <summary>
        /// Builds a worker row from one hosted-service node.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="hostedService">The hosted-service node being mapped.</param>
        /// <returns>The mapped worker row.</returns>
        private static WorkerDto BuildHostedServiceWorker(ExtractedArchitectureSnapshot snapshot, ArchitectureNode hostedService)
        {
            // Hosted-service workers collect directly related queues, topics, schedules, data-access nodes, integrations, and evidence.
            string workerKey = hostedService.StableKey.Value;
            ArchitectureEdge[] relatedEdges = snapshot.Edges
                .Where(edge => StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, workerKey) || StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, workerKey))
                .ToArray();
            ArchitectureNode[] relatedNodes = RelatedNodes(snapshot, relatedEdges).ToArray();
            RuntimeQueueConsumerDto[] consumers = relatedNodes.Where(static node => node.NodeKind == NodeKind.Queue || node.NodeKind == NodeKind.Topic).Select(node => BuildQueueConsumer(snapshot, node, workerKey)).ToArray();
            RuntimeScheduledJobDto[] scheduledJobs = relatedNodes.Where(static node => IsScheduledJobNode(node)).Select(node => BuildScheduledJob(node, workerKey)).ToArray();
            RuntimeUnknownDto[] unknowns = BuildWorkerUnknowns(hostedService, consumers, scheduledJobs);
            return new WorkerDto(
                workerKey,
                hostedService.DisplayName,
                NormalizeWorkerKind(MetadataString(hostedService.Metadata, "runtimeKind") ?? hostedService.NodeKind.Value),
                hostedService.ProjectStableKey?.Value,
                hostedService.ProjectStableKey is null ? null : $"entrypoint://{hostedService.ProjectStableKey.Value}",
                [workerKey],
                IsBackgroundService(hostedService) ? [workerKey] : [],
                consumers,
                scheduledJobs,
                BuildDataAccessIndicators(snapshot, hostedService.ProjectStableKey?.Value, workerKey),
                BuildIntegrationIndicators(snapshot, hostedService.ProjectStableKey?.Value, workerKey),
                BuildConfigurationKeys(snapshot, hostedService.ProjectStableKey?.Value, workerKey),
                BuildEvidenceReferences(snapshot, BuildEvidenceStableKeys(hostedService, relatedEdges)),
                hostedService.Confidence.Value,
                unknowns.Length > 0,
                unknowns.FirstOrDefault()?.Reason,
                unknowns);
        }

        /// <summary>
        /// Builds a worker row from one queue or topic consumer node when no hosted service owns it.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="consumerNode">The queue or topic node being mapped.</param>
        /// <returns>The mapped worker row.</returns>
        private static WorkerDto BuildConsumerWorker(ExtractedArchitectureSnapshot snapshot, ArchitectureNode consumerNode)
        {
            // Standalone queue/topic consumers remain visible rather than disappearing when hosted-service correlation is incomplete.
            RuntimeQueueConsumerDto consumer = BuildQueueConsumer(snapshot, consumerNode, handlerStableKey: null);
            return new WorkerDto(
                $"worker://{consumerNode.StableKey.Value}",
                consumerNode.DisplayName,
                consumerNode.NodeKind == NodeKind.Topic ? "TopicConsumer" : "QueueConsumer",
                consumerNode.ProjectStableKey?.Value,
                consumerNode.ProjectStableKey is null ? null : $"entrypoint://{consumerNode.ProjectStableKey.Value}",
                [],
                [],
                [consumer],
                [],
                BuildDataAccessIndicators(snapshot, consumerNode.ProjectStableKey?.Value, consumerNode.StableKey.Value),
                BuildIntegrationIndicators(snapshot, consumerNode.ProjectStableKey?.Value, consumerNode.StableKey.Value),
                BuildConfigurationKeys(snapshot, consumerNode.ProjectStableKey?.Value, consumerNode.StableKey.Value),
                BuildEvidenceReferences(snapshot, BuildEvidenceStableKeys(consumerNode, [])),
                consumerNode.Confidence.Value,
                consumerNode.UnknownState.HasUnknownData,
                consumerNode.UnknownState.UnknownReason,
                BuildStandaloneUnknowns(consumerNode, "queueConsumers"));
        }

        /// <summary>
        /// Builds a worker row from one scheduled-job node when no hosted service owns it.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="scheduledJob">The scheduled-job node being mapped.</param>
        /// <returns>The mapped worker row.</returns>
        private static WorkerDto BuildScheduledJobWorker(ExtractedArchitectureSnapshot snapshot, ArchitectureNode scheduledJob)
        {
            // Standalone scheduled jobs are represented as worker rows so automation can discover timer-driven behavior.
            RuntimeScheduledJobDto job = BuildScheduledJob(scheduledJob, handlerStableKey: null);
            return new WorkerDto(
                $"worker://{scheduledJob.StableKey.Value}",
                scheduledJob.DisplayName,
                "ScheduledJob",
                scheduledJob.ProjectStableKey?.Value,
                scheduledJob.ProjectStableKey is null ? null : $"entrypoint://{scheduledJob.ProjectStableKey.Value}",
                [],
                [],
                [],
                [job],
                BuildDataAccessIndicators(snapshot, scheduledJob.ProjectStableKey?.Value, scheduledJob.StableKey.Value),
                BuildIntegrationIndicators(snapshot, scheduledJob.ProjectStableKey?.Value, scheduledJob.StableKey.Value),
                BuildConfigurationKeys(snapshot, scheduledJob.ProjectStableKey?.Value, scheduledJob.StableKey.Value),
                BuildEvidenceReferences(snapshot, BuildEvidenceStableKeys(scheduledJob, [])),
                scheduledJob.Confidence.Value,
                scheduledJob.UnknownState.HasUnknownData,
                scheduledJob.UnknownState.UnknownReason,
                BuildStandaloneUnknowns(scheduledJob, "scheduledJobs"));
        }

        /// <summary>
        /// Builds worker-level unknown metadata from a hosted service and its child runtime facts.
        /// </summary>
        /// <param name="hostedService">The hosted-service node that owns the worker response.</param>
        /// <param name="consumers">The queue or topic consumers attached to the worker.</param>
        /// <param name="scheduledJobs">The scheduled jobs attached to the worker.</param>
        /// <returns>The explicit unknown values that should be visible at the envelope level.</returns>
        private static RuntimeUnknownDto[] BuildWorkerUnknowns(ArchitectureNode hostedService, IEnumerable<RuntimeQueueConsumerDto> consumers, IEnumerable<RuntimeScheduledJobDto> scheduledJobs)
        {
            // Child unknowns are promoted to the envelope so clients can detect partial worker extraction without inspecting every nested record.
            List<RuntimeUnknownDto> unknowns = [];
            if (hostedService.UnknownState.HasUnknownData)
            {
                unknowns.Add(new RuntimeUnknownDto("workers", hostedService.UnknownState.UnknownReason ?? "The hosted-service worker contains incomplete runtime extraction data."));
            }

            unknowns.AddRange(consumers.Where(static consumer => consumer.HasUnknownData).Select(static consumer => new RuntimeUnknownDto("queueConsumers", consumer.UnknownReason ?? "A queue or topic consumer contains incomplete runtime extraction data.")));
            unknowns.AddRange(scheduledJobs.Where(static job => job.HasUnknownData).Select(static job => new RuntimeUnknownDto("scheduledJobs", job.UnknownReason ?? "A scheduled job contains incomplete runtime extraction data.")));
            return unknowns.DistinctBy(static unknown => unknown.Field + "\u001f" + unknown.Reason).ToArray();
        }

        /// <summary>
        /// Builds unknown metadata for a standalone runtime node.
        /// </summary>
        /// <param name="node">The standalone runtime node being represented as a worker row.</param>
        /// <param name="field">The response field associated with the standalone node.</param>
        /// <returns>The explicit unknown values that should be visible at the envelope level.</returns>
        private static RuntimeUnknownDto[] BuildStandaloneUnknowns(ArchitectureNode node, string field)
        {
            // Standalone nodes only contribute envelope unknowns when the graph fact itself declares unknown data.
            return node.UnknownState.HasUnknownData
                ? [new RuntimeUnknownDto(field, node.UnknownState.UnknownReason ?? "The standalone runtime fact contains incomplete extraction data.")]
                : [];
        }

        /// <summary>
        /// Applies controlled worker filters to worker rows.
        /// </summary>
        /// <param name="items">The complete worker rows.</param>
        /// <param name="query">The normalized worker query.</param>
        /// <returns>The filtered worker rows.</returns>
        private static IEnumerable<WorkerDto> ApplyWorkerFilters(IEnumerable<WorkerDto> items, WorkerQuery query)
        {
            // Filters stay limited to stable project identity and safe queue/topic/schedule display metadata.
            return items
                .Where(item => string.IsNullOrWhiteSpace(query.ProjectStableKey) || StringComparer.Ordinal.Equals(item.ProjectStableKey, query.ProjectStableKey.Trim()))
                .Where(item => string.IsNullOrWhiteSpace(query.WorkerKind) || string.Equals(item.WorkerKind, query.WorkerKind.Trim(), StringComparison.OrdinalIgnoreCase) || item.QueueConsumers.Any(consumer => string.Equals(consumer.Kind + "Consumer", query.WorkerKind.Trim(), StringComparison.OrdinalIgnoreCase)) || item.ScheduledJobs.Any(_ => string.Equals("ScheduledJob", query.WorkerKind.Trim(), StringComparison.OrdinalIgnoreCase)))
                .Where(item => string.IsNullOrWhiteSpace(query.QueueOrTopic) || item.QueueConsumers.Any(consumer => Contains(consumer.Name, query.QueueOrTopic) || Contains(consumer.StableKey, query.QueueOrTopic)))
                .Where(item => string.IsNullOrWhiteSpace(query.ScheduledJob) || item.ScheduledJobs.Any(job => Contains(job.Name, query.ScheduledJob) || Contains(job.StableKey, query.ScheduledJob)));
        }

        /// <summary>
        /// Builds one queue or topic consumer DTO from a graph node.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="node">The queue or topic node being mapped.</param>
        /// <param name="handlerStableKey">The optional handler or hosted-service stable key associated with the consumer.</param>
        /// <returns>The mapped queue or topic consumer DTO.</returns>
        private static RuntimeQueueConsumerDto BuildQueueConsumer(ExtractedArchitectureSnapshot snapshot, ArchitectureNode node, string? handlerStableKey)
        {
            // Queue/topic metadata supplies transport hints while graph edges provide additional handler identities when available.
            string nodeKey = node.StableKey.Value;
            string[] handlerStableKeys = snapshot.Edges
                .Where(edge => StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, nodeKey) || StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, nodeKey))
                .Select(edge => StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, nodeKey) ? edge.TargetNodeStableKey.Value : edge.SourceNodeStableKey.Value)
                .Append(handlerStableKey)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            return new RuntimeQueueConsumerDto(
                nodeKey,
                node.DisplayName,
                node.NodeKind.Value,
                MetadataString(node.Metadata, "transportKind") ?? MetadataString(node.Metadata, "transport"),
                MetadataString(node.Metadata, "subscriptionName"),
                handlerStableKeys,
                BuildEvidenceStableKeys(node, []),
                node.UnknownState.HasUnknownData,
                node.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Builds one scheduled-job DTO from a graph node.
        /// </summary>
        /// <param name="node">The scheduled-job node being mapped.</param>
        /// <param name="handlerStableKey">The optional handler or hosted-service stable key associated with the scheduled job.</param>
        /// <returns>The mapped scheduled-job DTO.</returns>
        private static RuntimeScheduledJobDto BuildScheduledJob(ArchitectureNode node, string? handlerStableKey)
        {
            // Schedule values are treated as safe descriptions, not executable scheduler configuration.
            return new RuntimeScheduledJobDto(
                node.StableKey.Value,
                node.DisplayName,
                MetadataString(node.Metadata, "schedule") ?? MetadataString(node.Metadata, "cron") ?? MetadataString(node.Metadata, "timer"),
                handlerStableKey,
                BuildEvidenceStableKeys(node, []),
                node.UnknownState.HasUnknownData,
                node.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Builds safe evidence references for selected stable evidence keys.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="evidenceStableKeys">The evidence stable keys to resolve.</param>
        /// <returns>The safe evidence references that matched the selected keys.</returns>
        private static RuntimeEvidenceReferenceDto[] BuildEvidenceReferences(ExtractedArchitectureSnapshot snapshot, IEnumerable<string> evidenceStableKeys)
        {
            // Evidence references preserve source location and small previews but do not expand arbitrary source files.
            HashSet<string> keys = new(evidenceStableKeys.Where(static key => !string.IsNullOrWhiteSpace(key)), StringComparer.Ordinal);
            return snapshot.Evidence
                .Where(evidence => keys.Contains(evidence.StableKey.Value))
                .OrderBy(static evidence => evidence.StableKey.Value, StringComparer.Ordinal)
                .Select(static evidence => new RuntimeEvidenceReferenceDto(
                    evidence.StableKey.Value,
                    evidence.EvidenceKind.Value,
                    evidence.FilePath.Value,
                    evidence.StartLine,
                    evidence.EndLine,
                    evidence.SymbolName,
                    evidence.ContainingSymbol,
                    evidence.SnippetHash,
                    BoundSnippet(evidence.SnippetPreview),
                    evidence.Confidence.Value))
                .ToArray();
        }

        /// <summary>
        /// Builds evidence stable keys from a node and related edges.
        /// </summary>
        /// <param name="node">The node whose primary evidence may be included.</param>
        /// <param name="edges">The related edges whose primary evidence may be included.</param>
        /// <returns>The stable evidence keys associated with the node and edges.</returns>
        private static List<string> BuildEvidenceStableKeys(ArchitectureNode node, IEnumerable<ArchitectureEdge> edges)
        {
            // Evidence stable-key aggregation removes duplicates so response rows remain compact and deterministic.
            List<string> evidenceStableKeys = [];
            AddIfPresent(evidenceStableKeys, node.PrimaryEvidenceStableKey?.Value);
            foreach (ArchitectureEdge edge in edges)
            {
                AddIfPresent(evidenceStableKeys, edge.PrimaryEvidenceStableKey?.Value);
            }

            return evidenceStableKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Builds data-access indicators related to a project or runtime node.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="projectStableKey">The optional project stable key used for ownership fallback.</param>
        /// <param name="nodeStableKey">The runtime node stable key used for direct relationship matching.</param>
        /// <returns>The stable data-access indicator names.</returns>
        private static string[] BuildDataAccessIndicators(ExtractedArchitectureSnapshot snapshot, string? projectStableKey, string? nodeStableKey)
        {
            // Data-access indicators combine direct runtime edges and project-owned facts so partial extraction still produces useful worker/endpoint context.
            return snapshot.Nodes
                .Where(node => IsDataAccessNode(node))
                .Where(node => IsRelatedByProjectOrEdge(snapshot, node, projectStableKey, nodeStableKey))
                .Select(static node => node.NodeKind.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Builds configuration keys related to a project or runtime node.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="projectStableKey">The optional project stable key used for ownership fallback.</param>
        /// <param name="nodeStableKey">The runtime node stable key used for direct relationship matching.</param>
        /// <returns>The safe configuration key names.</returns>
        private static string[] BuildConfigurationKeys(ExtractedArchitectureSnapshot snapshot, string? projectStableKey, string? nodeStableKey)
        {
            // Configuration responses expose key names only and never attempt to surface values, secrets, or connection strings.
            return snapshot.Nodes
                .Where(static node => node.NodeKind == NodeKind.ConfigurationKey)
                .Where(node => IsRelatedByProjectOrEdge(snapshot, node, projectStableKey, nodeStableKey))
                .Select(static node => node.DisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Builds external integration indicators related to a project or runtime node.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="projectStableKey">The optional project stable key used for ownership fallback.</param>
        /// <param name="nodeStableKey">The runtime node stable key used for direct relationship matching.</param>
        /// <returns>The safe integration display names.</returns>
        private static string[] BuildIntegrationIndicators(ExtractedArchitectureSnapshot snapshot, string? projectStableKey, string? nodeStableKey)
        {
            // Integration indicators use display names and stable graph relationships while leaving secret target details to later specialized slices.
            return snapshot.Nodes
                .Where(static node => node.NodeKind == NodeKind.ExternalService)
                .Where(node => IsRelatedByProjectOrEdge(snapshot, node, projectStableKey, nodeStableKey))
                .Select(static node => node.DisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Determines whether a node is directly related by ownership or by an edge to the selected runtime context.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="node">The candidate node to inspect.</param>
        /// <param name="projectStableKey">The optional project stable key used for ownership fallback.</param>
        /// <param name="nodeStableKey">The runtime node stable key used for direct relationship matching.</param>
        /// <returns><see langword="true"/> when the node is related to the selected runtime context; otherwise, <see langword="false"/>.</returns>
        private static bool IsRelatedByProjectOrEdge(ExtractedArchitectureSnapshot snapshot, ArchitectureNode node, string? projectStableKey, string? nodeStableKey)
        {
            // Project ownership is a safe fallback because some extractors attach configuration and data-access facts only at project scope.
            if (!string.IsNullOrWhiteSpace(projectStableKey) && StringComparer.Ordinal.Equals(node.ProjectStableKey?.Value, projectStableKey))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(nodeStableKey))
            {
                return false;
            }

            return snapshot.Edges.Any(edge =>
                (StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, nodeStableKey) && StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, node.StableKey.Value))
                || (StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, nodeStableKey) && StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, node.StableKey.Value)));
        }

        /// <summary>
        /// Builds related node display names for selected edge kinds.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="edges">The related edges to inspect.</param>
        /// <param name="edgeKinds">The edge kinds allowed to contribute names.</param>
        /// <returns>The deterministic related node display names.</returns>
        private static string[] BuildRelatedNames(ExtractedArchitectureSnapshot snapshot, IEnumerable<ArchitectureEdge> edges, params EdgeKind[] edgeKinds)
        {
            // Related names are human-readable hints; stable identities remain available elsewhere in the response.
            HashSet<string> allowedKinds = new(edgeKinds.Select(static kind => kind.Value), StringComparer.Ordinal);
            return RelatedNodes(snapshot, edges.Where(edge => allowedKinds.Contains(edge.EdgeKind.Value)))
                .Select(static node => node.DisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Resolves nodes at the other end of a related edge collection.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="edges">The related edges whose source and target nodes should be resolved.</param>
        /// <returns>The related nodes that matched edge endpoints.</returns>
        private static IEnumerable<ArchitectureNode> RelatedNodes(ExtractedArchitectureSnapshot snapshot, IEnumerable<ArchitectureEdge> edges)
        {
            // Related-node resolution uses stable keys only and skips missing nodes so partial graph extraction remains safe.
            HashSet<string> keys = new(edges.SelectMany(static edge => new[] { edge.SourceNodeStableKey.Value, edge.TargetNodeStableKey.Value }), StringComparer.Ordinal);
            return snapshot.Nodes.Where(node => keys.Contains(node.StableKey.Value));
        }

        /// <summary>
        /// Finds a related node display name for a specific kind.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="edges">The related edges to inspect.</param>
        /// <param name="nodeKind">The node kind to select.</param>
        /// <returns>The first deterministic related node display name, or null when none exists.</returns>
        private static string? RelatedNodeName(ExtractedArchitectureSnapshot snapshot, IEnumerable<ArchitectureEdge> edges, NodeKind nodeKind)
        {
            // Controller names can be discovered from either endpoint metadata or direct graph relationships.
            return RelatedNodes(snapshot, edges)
                .Where(node => node.NodeKind == nodeKind)
                .OrderBy(static node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(static node => node.DisplayName)
                .FirstOrDefault();
        }

        /// <summary>
        /// Finds a related handler display name from method or type nodes.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="edges">The related edges to inspect.</param>
        /// <returns>The first deterministic related handler display name, or null when none exists.</returns>
        private static string? RelatedHandlerName(ExtractedArchitectureSnapshot snapshot, IEnumerable<ArchitectureEdge> edges)
        {
            // Minimal API handlers and message handlers are commonly represented as method or type nodes rather than controller nodes.
            return RelatedNodes(snapshot, edges)
                .Where(static node => node.NodeKind == NodeKind.Method || node.NodeKind == NodeKind.Type)
                .OrderBy(static node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(static node => node.DisplayName)
                .FirstOrDefault();
        }

        /// <summary>
        /// Finds one node by stable key when a stable key is available.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="stableKey">The optional stable key to find.</param>
        /// <returns>The matching node, or null when no key or node is available.</returns>
        private static ArchitectureNode? FindNode(ExtractedArchitectureSnapshot snapshot, string? stableKey)
        {
            // Stable-key lookup avoids any display-name ambiguity when enriching entry-point rows with project names.
            return string.IsNullOrWhiteSpace(stableKey)
                ? null
                : snapshot.Nodes.FirstOrDefault(node => StringComparer.Ordinal.Equals(node.StableKey.Value, stableKey));
        }

        /// <summary>
        /// Determines whether a node represents any runtime query concept.
        /// </summary>
        /// <param name="node">The architecture node to inspect.</param>
        /// <returns><see langword="true"/> when the node is runtime-oriented; otherwise, <see langword="false"/>.</returns>
        private static bool IsRuntimeNode(ArchitectureNode node)
        {
            // Runtime nodes include HTTP endpoint/controller facts plus worker, queue, topic, and project entry-point facts.
            return node.NodeKind == NodeKind.Endpoint
                || node.NodeKind == NodeKind.Controller
                || node.NodeKind == NodeKind.HostedService
                || node.NodeKind == NodeKind.Queue
                || node.NodeKind == NodeKind.Topic
                || node.NodeKind == NodeKind.OpenApiDocument
                || node.NodeKind == NodeKind.Dockerfile
                || node.NodeKind == NodeKind.Pipeline
                || IsControllerOrHandlerNode(node)
                || IsScheduledJobNode(node)
                || node.NodeKind == NodeKind.Project;
        }

        /// <summary>
        /// Determines whether a node is eligible for controller or handler detail lookup.
        /// </summary>
        /// <param name="node">The architecture node to inspect.</param>
        /// <returns><see langword="true"/> when the node can be treated as a controller or handler; otherwise, <see langword="false"/>.</returns>
        private static bool IsControllerOrHandlerNode(ArchitectureNode node)
        {
            // Controllers are explicit node kinds while handlers can be method/type nodes marked with runtime role metadata.
            return node.NodeKind == NodeKind.Controller
                || HasMetadataValue(node.Metadata, "runtimeRole", "Handler")
                || HasMetadataValue(node.Metadata, "handlerKind", "ControllerHandler")
                || HasMetadataValue(node.Metadata, "handlerKind", "MessageHandler")
                || HasMetadataValue(node.Metadata, "typeRole", "HostedServiceImplementation");
        }

        /// <summary>
        /// Determines whether a node represents a scheduled job fact.
        /// </summary>
        /// <param name="node">The architecture node to inspect.</param>
        /// <returns><see langword="true"/> when the node represents a scheduled job; otherwise, <see langword="false"/>.</returns>
        private static bool IsScheduledJobNode(ArchitectureNode node)
        {
            // Scheduled jobs may be represented by method, pipeline, or generated artifact nodes with scheduler metadata until a dedicated node kind exists.
            return HasMetadataValue(node.Metadata, "runtimeKind", "ScheduledJob")
                || HasMetadataValue(node.Metadata, "workerKind", "ScheduledJob")
                || HasMetadataValue(node.Metadata, "scheduleKind", "ScheduledJob")
                || MetadataString(node.Metadata, "schedule") is not null
                || MetadataString(node.Metadata, "cron") is not null;
        }

        /// <summary>
        /// Determines whether a hosted-service node is specifically a background service.
        /// </summary>
        /// <param name="node">The hosted-service node to inspect.</param>
        /// <returns><see langword="true"/> when the node represents a background service; otherwise, <see langword="false"/>.</returns>
        private static bool IsBackgroundService(ArchitectureNode node)
        {
            // Runtime extractor metadata distinguishes BackgroundService from general IHostedService when that distinction is available.
            return HasMetadataValue(node.Metadata, "runtimeKind", "BackgroundService")
                || HasMetadataValue(node.Metadata, "baseType", "BackgroundService")
                || Contains(node.QualifiedName, "BackgroundService");
        }

        /// <summary>
        /// Determines whether a node is a data-access concept.
        /// </summary>
        /// <param name="node">The architecture node to inspect.</param>
        /// <returns><see langword="true"/> when the node is a data-access fact; otherwise, <see langword="false"/>.</returns>
        private static bool IsDataAccessNode(ArchitectureNode node)
        {
            // Data-access concepts are normalized by node kind so runtime responses can expose safe indicators without database details.
            return node.NodeKind == NodeKind.DbContext
                || node.NodeKind == NodeKind.LinqToSqlDataContext
                || node.NodeKind == NodeKind.Entity
                || node.NodeKind == NodeKind.DatabaseTable
                || node.NodeKind == NodeKind.StoredProcedure
                || node.NodeKind == NodeKind.SqlScript;
        }

        /// <summary>
        /// Normalizes application or runtime metadata into one public runtime kind.
        /// </summary>
        /// <param name="value">The raw runtime kind or application type value.</param>
        /// <returns>The normalized runtime kind.</returns>
        private static string NormalizeRuntimeKind(string? value)
        {
            // Runtime kind normalization maps common project and host labels onto the limited public vocabulary.
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Console";
            }

            string normalized = value.Trim();
            if (normalized.Contains("api", StringComparison.OrdinalIgnoreCase) || normalized.Contains("web", StringComparison.OrdinalIgnoreCase) || normalized.Contains("openapi", StringComparison.OrdinalIgnoreCase))
            {
                return "Api";
            }

            if (normalized.Contains("worker", StringComparison.OrdinalIgnoreCase) || normalized.Contains("hosted", StringComparison.OrdinalIgnoreCase) || normalized.Contains("background", StringComparison.OrdinalIgnoreCase))
            {
                return "Worker";
            }

            if (normalized.Contains("service", StringComparison.OrdinalIgnoreCase))
            {
                return "ServiceHost";
            }

            return "Console";
        }

        /// <summary>
        /// Normalizes hosted-service metadata into one public worker kind.
        /// </summary>
        /// <param name="value">The raw worker kind or runtime kind value.</param>
        /// <returns>The normalized worker kind.</returns>
        private static string NormalizeWorkerKind(string? value)
        {
            // Worker kind normalization keeps supported filters stable even when extractors provide slightly different labels.
            if (string.IsNullOrWhiteSpace(value))
            {
                return "HostedService";
            }

            string normalized = value.Trim();
            if (normalized.Contains("background", StringComparison.OrdinalIgnoreCase))
            {
                return "BackgroundService";
            }

            if (normalized.Contains("scheduled", StringComparison.OrdinalIgnoreCase) || normalized.Contains("timer", StringComparison.OrdinalIgnoreCase))
            {
                return "ScheduledJob";
            }

            return "HostedService";
        }

        /// <summary>
        /// Reads a metadata string value from canonical graph metadata.
        /// </summary>
        /// <param name="metadata">The metadata value to inspect.</param>
        /// <param name="key">The metadata key to read.</param>
        /// <returns>The metadata string value, or null when absent or non-string.</returns>
        private static string? MetadataString(GraphMetadata metadata, string key)
        {
            // JSON parsing keeps the runtime service independent of GraphMetadata internals.
            using JsonDocument document = JsonDocument.Parse(metadata.ToCanonicalJson());
            return document.RootElement.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        /// <summary>
        /// Reads metadata string array values from the first matching key.
        /// </summary>
        /// <param name="metadata">The metadata value to inspect.</param>
        /// <param name="keys">The metadata keys to read in precedence order.</param>
        /// <returns>The metadata string values, or an empty array when no matching string or array exists.</returns>
        private static string[] MetadataStrings(GraphMetadata metadata, params string[] keys)
        {
            // Arrays and comma-separated strings are both accepted because extractor metadata can evolve across slices.
            using JsonDocument document = JsonDocument.Parse(metadata.ToCanonicalJson());
            foreach (string key in keys)
            {
                if (!document.RootElement.TryGetProperty(key, out JsonElement value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Array)
                {
                    return value.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.String).Select(static item => item.GetString()).Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item!).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    string? text = value.GetString();
                    return SplitMetadataList(text);
                }
            }

            return [];
        }

        /// <summary>
        /// Determines whether a metadata value matches a requested value.
        /// </summary>
        /// <param name="metadata">The metadata value to inspect.</param>
        /// <param name="key">The metadata key to read.</param>
        /// <param name="expectedValue">The expected metadata value.</param>
        /// <returns><see langword="true"/> when the metadata value matches; otherwise, <see langword="false"/>.</returns>
        private static bool HasMetadataValue(GraphMetadata metadata, string key, object expectedValue)
        {
            // Metadata matching supports string and boolean values commonly emitted by extractors.
            using JsonDocument document = JsonDocument.Parse(metadata.ToCanonicalJson());
            return document.RootElement.TryGetProperty(key, out JsonElement actualValue)
                && MetadataValueEquals(actualValue, expectedValue);
        }

        /// <summary>
        /// Compares a canonical JSON metadata value with a requested runtime value.
        /// </summary>
        /// <param name="actualValue">The metadata JSON value read from canonical metadata.</param>
        /// <param name="expectedValue">The expected CLR value.</param>
        /// <returns><see langword="true"/> when the values match; otherwise, <see langword="false"/>.</returns>
        private static bool MetadataValueEquals(JsonElement actualValue, object expectedValue)
        {
            // JSON parsing keeps metadata comparison deterministic while supporting string and boolean classifications.
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
        /// Splits metadata list text into deterministic non-empty values.
        /// </summary>
        /// <param name="value">The comma- or semicolon-separated metadata text.</param>
        /// <returns>The normalized metadata values.</returns>
        private static string[] SplitMetadataList(string? value)
        {
            // Metadata lists remain simple display hints, so comma and semicolon separators are sufficient for current extractor output.
            return string.IsNullOrWhiteSpace(value)
                ? []
                : value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>
        /// Determines whether candidate text contains a requested search value.
        /// </summary>
        /// <param name="candidate">The candidate text to inspect.</param>
        /// <param name="search">The requested search text.</param>
        /// <returns><see langword="true"/> when the candidate contains the search text; otherwise, <see langword="false"/>.</returns>
        private static bool Contains(string? candidate, string? search)
        {
            // Contains matching is limited to public display and identity fields and uses ordinal-ignore-case semantics.
            return !string.IsNullOrWhiteSpace(candidate)
                && !string.IsNullOrWhiteSpace(search)
                && candidate.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Bounds a snippet preview to the runtime query maximum.
        /// </summary>
        /// <param name="snippetPreview">The source snippet preview to bound.</param>
        /// <returns>The bounded snippet preview, or null when absent.</returns>
        private static string? BoundSnippet(string? snippetPreview)
        {
            // Snippets are untrusted display text and must stay compact at the API boundary.
            if (string.IsNullOrEmpty(snippetPreview) || snippetPreview.Length <= 160)
            {
                return snippetPreview;
            }

            return snippetPreview[..160];
        }

        /// <summary>
        /// Adds a non-empty value to a mutable list.
        /// </summary>
        /// <param name="items">The mutable list that receives the value.</param>
        /// <param name="value">The candidate value to add.</param>
        private static void AddIfPresent(List<string> items, string? value)
        {
            // Null or blank stable keys cannot help consumers navigate evidence and are omitted.
            if (!string.IsNullOrWhiteSpace(value))
            {
                items.Add(value.Trim());
            }
        }

        /// <summary>
        /// Orders runtime rows by a string key with requested direction.
        /// </summary>
        /// <param name="items">The runtime rows to order.</param>
        /// <param name="descending">A value indicating whether the first sort should be descending.</param>
        /// <param name="selector">The string key selector.</param>
        /// <returns>The ordered runtime rows.</returns>
        private static IOrderedEnumerable<RuntimeEndpointDto> Order(IEnumerable<RuntimeEndpointDto> items, bool descending, Func<RuntimeEndpointDto, string> selector)
        {
            // String ordering is ordinal-ignore-case for developer-facing fields, with stable key tie-breakers added by the caller.
            return descending
                ? items.OrderByDescending(selector, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(selector, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Orders runtime rows by a decimal key with requested direction.
        /// </summary>
        /// <param name="items">The runtime rows to order.</param>
        /// <param name="descending">A value indicating whether the first sort should be descending.</param>
        /// <param name="selector">The decimal key selector.</param>
        /// <returns>The ordered runtime rows.</returns>
        private static IOrderedEnumerable<RuntimeEndpointDto> Order(IEnumerable<RuntimeEndpointDto> items, bool descending, Func<RuntimeEndpointDto, decimal> selector)
        {
            // Numeric ordering is used for confidence while stable key tie-breakers preserve deterministic paging.
            return descending
                ? items.OrderByDescending(selector)
                : items.OrderBy(selector);
        }

        /// <summary>
        /// Merges an additional unknown into an existing runtime query context.
        /// </summary>
        /// <param name="context">The existing runtime query context.</param>
        /// <param name="unknown">The unknown value to add.</param>
        /// <returns>A new context with the additional unknown value.</returns>
        private static RuntimeQueryContext MergeUnknownContext(RuntimeQueryContext context, RuntimeUnknownDto unknown)
        {
            // Merging keeps query-level warnings intact while making empty-but-valid runtime sections explicit.
            RuntimeUnknownDto[] unknowns = context.Unknowns.Concat([unknown]).DistinctBy(static item => item.Field + "\u001f" + item.Reason).ToArray();
            return new RuntimeQueryContext(context.Scope, context.Snapshot, context.Warnings, unknowns);
        }

        /// <summary>
        /// Captures either a successful snapshot resolution or validation errors.
        /// </summary>
        private sealed class SnapshotResolution
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="SnapshotResolution"/> class.
            /// </summary>
            /// <param name="snapshot">The selected snapshot when resolution succeeds.</param>
            /// <param name="scopedSnapshots">The repository and solution scoped snapshots used for latest resolution.</param>
            /// <param name="validationErrors">The validation errors emitted when resolution fails.</param>
            private SnapshotResolution(ExtractedArchitectureSnapshot? snapshot, IReadOnlyList<ExtractedArchitectureSnapshot> scopedSnapshots, IReadOnlyList<RuntimeQueryValidationError> validationErrors)
            {
                // Resolution groups successful data and failures so public query methods can share a single validation path.
                Snapshot = snapshot;
                ScopedSnapshots = scopedSnapshots;
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
            /// Gets the repository and solution scoped snapshots used for latest resolution.
            /// </summary>
            public IReadOnlyList<ExtractedArchitectureSnapshot> ScopedSnapshots { get; }

            /// <summary>
            /// Gets the validation errors emitted when resolution fails.
            /// </summary>
            public IReadOnlyList<RuntimeQueryValidationError> ValidationErrors { get; }

            /// <summary>
            /// Creates a successful snapshot resolution.
            /// </summary>
            /// <param name="snapshot">The selected snapshot.</param>
            /// <param name="scopedSnapshots">The repository and solution scoped snapshots.</param>
            /// <returns>The successful snapshot resolution.</returns>
            public static SnapshotResolution Success(ExtractedArchitectureSnapshot snapshot, IReadOnlyList<ExtractedArchitectureSnapshot> scopedSnapshots)
            {
                // Successful resolution includes scoped snapshots for future context expansion and deterministic latest behavior.
                return new SnapshotResolution(snapshot, scopedSnapshots, []);
            }

            /// <summary>
            /// Creates a failed snapshot resolution.
            /// </summary>
            /// <param name="validationErrors">The validation errors that prevented snapshot selection.</param>
            /// <returns>The failed snapshot resolution.</returns>
            public static SnapshotResolution Failed(IReadOnlyList<RuntimeQueryValidationError> validationErrors)
            {
                // Failed resolution carries deterministic validation errors and no snapshot payload.
                return new SnapshotResolution(null, [], validationErrors);
            }
        }
    }
}
