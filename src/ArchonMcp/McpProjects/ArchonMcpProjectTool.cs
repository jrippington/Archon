using Archon.Application.Projects;
using Archon.Domain.Graph.Metadata;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpSecurity;
using System.Text.Json;

namespace ArchonMcp.McpProjects
{
    /// <summary>
    /// Implements the read-only <c>archon.describe_project</c> MCP tool over the approved application project-query abstraction.
    /// </summary>
    public sealed class ArchonMcpProjectTool : IArchonMcpProjectTool
    {
        /// <summary>
        /// Executes authorization, allow-listing, and audit behavior before project query logic runs.
        /// </summary>
        private readonly IArchonMcpOperationExecutor _operationExecutor;

        /// <summary>
        /// Validates common MCP request fields before query-layer execution.
        /// </summary>
        private readonly IArchonMcpRequestValidator _requestValidator;

        /// <summary>
        /// Executes controlled project detail queries over persisted architecture snapshots.
        /// </summary>
        private readonly IProjectQueryService _projectQueryService;

        /// <summary>
        /// Applies configured MCP response limits to bounded collection sections.
        /// </summary>
        private readonly ArchonMcpLimitGuard _limitGuard;

        /// <summary>
        /// Creates a project description MCP tool handler.
        /// </summary>
        /// <param name="operationExecutor">The executor that performs security, allow-listing, audit, and safe failure mapping.</param>
        /// <param name="requestValidator">The common MCP request validator.</param>
        /// <param name="projectQueryService">The query-layer project abstraction used instead of direct persistence access.</param>
        /// <param name="limitGuard">The guard that applies configured MCP result limits.</param>
        public ArchonMcpProjectTool(
            IArchonMcpOperationExecutor operationExecutor,
            IArchonMcpRequestValidator requestValidator,
            IProjectQueryService projectQueryService,
            ArchonMcpLimitGuard limitGuard)
        {
            // Constructor injection keeps the tool testable and prevents the MCP layer from reaching persistence or filesystem details directly.
            _operationExecutor = operationExecutor ?? throw new ArgumentNullException(nameof(operationExecutor));
            _requestValidator = requestValidator ?? throw new ArgumentNullException(nameof(requestValidator));
            _projectQueryService = projectQueryService ?? throw new ArgumentNullException(nameof(projectQueryService));
            _limitGuard = limitGuard ?? throw new ArgumentNullException(nameof(limitGuard));
        }

        /// <inheritdoc />
        public async Task<object> DescribeProjectAsync(ArchonMcpDescribeProjectRequest request, CancellationToken cancellationToken)
        {
            // Security runs before validation or query work so disabled and unauthorized operations do not reveal request-shape details.
            ArgumentNullException.ThrowIfNull(request);
            IReadOnlyDictionary<string, string> auditParameters = CreateAuditParameters(request);
            ArchonMcpOperationResult result = await _operationExecutor.ExecuteAsync(
                ArchonMcpProjectOperation.Name,
                auditParameters,
                () => ExecuteAuthorizedDescribeProjectAsync(request, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return result.Payload;
        }

        /// <summary>
        /// Executes validation, application query mapping, and response-envelope mapping after security permits the operation.
        /// </summary>
        /// <param name="request">The caller-supplied MCP project description request.</param>
        /// <param name="cancellationToken">The token that can cancel project query execution.</param>
        /// <returns>A boxed MCP success or error payload for the operation executor.</returns>
        private async Task<object> ExecuteAuthorizedDescribeProjectAsync(ArchonMcpDescribeProjectRequest request, CancellationToken cancellationToken)
        {
            // Request validation is deliberately performed inside the authorized delegate to preserve fail-closed behavior.
            ArchonMcpValidationResult validationResult = ValidateRequest(request);
            if (!validationResult.IsValid)
            {
                return CreateValidationError(validationResult);
            }

            ProjectDetailQuery query = new(
                request.RepositoryStableKey,
                request.SolutionStableKey,
                request.SnapshotSelector,
                request.ProjectStableKey,
                request.ProjectName);
            ProjectDetailResult projectResult;
            try
            {
                // Query-layer exceptions are mapped to the shared safe error vocabulary without exposing stack traces or internals.
                projectResult = await _projectQueryService.GetProjectAsync(query, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation remains cooperative host behavior and should not be converted into an application error.
                throw;
            }
            catch (Exception)
            {
                // The public MCP response intentionally omits exception details because query failures may contain internal information.
                return ArchonMcpErrorResponse.Create(
                    ArchonMcpProjectOperation.Name,
                    ArchonMcpErrorCategory.QueryLayerFailure,
                    "The project query layer failed before a safe response could be produced.",
                    [new ArchonMcpSuggestedFollowUp("Retry the project description after verifying query data is available.", "user.question", null)]);
            }

            if (!projectResult.Succeeded)
            {
                return MapProjectFailure(projectResult);
            }

            return MapProjectSuccess(projectResult);
        }

        /// <summary>
        /// Validates scope and project identity fields for one MCP project description request.
        /// </summary>
        /// <param name="request">The request whose fields should be validated.</param>
        /// <returns>A validation result containing every detected failure.</returns>
        private ArchonMcpValidationResult ValidateRequest(ArchonMcpDescribeProjectRequest request)
        {
            // Common validation handles stable-key syntax and snapshot selectors while project identity rules reject missing or conflicting lookup modes.
            List<ArchonMcpValidationFailure> failures = [];
            ArchonMcpValidationRequest validationRequest = new(
                request.ProjectStableKey,
                request.SnapshotSelector,
                SearchText: null,
                Filters: null,
                RequestedCount: null,
                RequestedDepth: null,
                PageNumber: null,
                PageSize: null);
            failures.AddRange(_requestValidator.Validate(validationRequest).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.RepositoryStableKey, nameof(request.RepositoryStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.SolutionStableKey, nameof(request.SolutionStableKey)).Failures);

            if (string.IsNullOrWhiteSpace(request.ProjectStableKey) && string.IsNullOrWhiteSpace(request.ProjectName))
            {
                failures.Add(new ArchonMcpValidationFailure("projectIdentity", "A project stable key or unambiguous project name is required."));
            }

            if (!string.IsNullOrWhiteSpace(request.ProjectStableKey) && !string.IsNullOrWhiteSpace(request.ProjectName))
            {
                failures.Add(new ArchonMcpValidationFailure("projectIdentity", "Use either project stable key or project name, not both."));
            }

            if (request.ProjectName is { Length: > 256 })
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.ProjectName), "Project name must be 256 characters or fewer."));
            }

            return new ArchonMcpValidationResult(failures);
        }

        /// <summary>
        /// Maps a successful project query result into the common MCP envelope.
        /// </summary>
        /// <param name="projectResult">The successful query-layer project detail result.</param>
        /// <returns>A typed MCP success envelope containing project facts.</returns>
        private ArchonMcpEnvelope<ArchonMcpProjectFacts> MapProjectSuccess(ProjectDetailResult projectResult)
        {
            // Successful query results must include detail and context because validation failures returned before this mapper.
            ProjectDetailDto detail = projectResult.Detail ?? throw new InvalidOperationException("Project detail was not returned for a successful project result.");
            ProjectQueryContext context = projectResult.Context ?? throw new InvalidOperationException("Project context was not returned for a successful project result.");
            ArchonMcpProjectFacts facts = CreateProjectFacts(detail);
            IReadOnlyList<ArchonMcpEvidenceReference> evidence = CreateEvidenceReferences(detail, context);
            IReadOnlyList<ArchonMcpUnknown> unknowns = CreateUnknowns(detail, context);
            IReadOnlyList<ArchonMcpWarning> warnings = CreateWarnings(detail, context);
            ArchonMcpLimitedList<string> limitProbe = _limitGuard.ApplyResultLimit(evidence.Select(item => item.StableKey), requestedLimit: null, ArchonMcpProjectOperation.Name);
            ArchonMcpLimitMetadata limits = limitProbe.Limits;
            int returnedEvidenceCount = limits.ReturnedCount.GetValueOrDefault(evidence.Count);
            IReadOnlyList<ArchonMcpSuggestedFollowUp> followUps = CreateFollowUps(detail, limitProbe.SuggestedFollowUps);

            return new ArchonMcpEnvelope<ArchonMcpProjectFacts>(
                ArchonMcpProjectOperation.Name,
                CreateSnapshotIdentity(context),
                CreateSummary(detail),
                CreateConfidence(detail, unknowns),
                facts,
                evidence.Take(returnedEvidenceCount).ToArray(),
                CreateFindingReferences(detail, context),
                unknowns,
                warnings,
                limits,
                followUps);
        }

        /// <summary>
        /// Maps query-layer validation, not-found, ambiguity, or scope failures into safe MCP error responses.
        /// </summary>
        /// <param name="projectResult">The failed project query result.</param>
        /// <returns>A structured MCP error response.</returns>
        private static ArchonMcpErrorResponse MapProjectFailure(ProjectDetailResult projectResult)
        {
            // The query layer returns deterministic validation codes, which are mapped to broad MCP categories without leaking internals.
            bool unavailable = HasAnyCode(projectResult, ProjectQueryValidationCodes.RepositoryNotFound, ProjectQueryValidationCodes.SolutionNotFound, ProjectQueryValidationCodes.SnapshotNotFound);
            bool notFound = HasAnyCode(projectResult, ProjectQueryValidationCodes.ProjectNotFound);
            bool ambiguous = HasAnyCode(projectResult, ProjectQueryValidationCodes.ProjectNameAmbiguous);
            ArchonMcpErrorCategory category = unavailable
                ? ArchonMcpErrorCategory.DependencyUnavailable
                : ambiguous
                    ? ArchonMcpErrorCategory.Ambiguous
                    : notFound
                        ? ArchonMcpErrorCategory.NotFound
                        : ArchonMcpErrorCategory.Validation;
            string message = ambiguous
                ? "Project name lookup is ambiguous; retry with one of the returned project stable keys."
                : unavailable
                    ? "Project data is unavailable for the requested repository, solution, or snapshot scope."
                    : string.Join(" ", projectResult.ValidationErrors.Select(error => error.Message));
            IReadOnlyDictionary<string, string>? candidateParameters = ambiguous
                ? CreateDisambiguationParameters(projectResult.DisambiguationOptions)
                : null;

            return ArchonMcpErrorResponse.Create(
                ArchonMcpProjectOperation.Name,
                category,
                message,
                [new ArchonMcpSuggestedFollowUp("Retry archon.describe_project with an exact project stable key.", "archon.describe_project", candidateParameters)]);
        }

        /// <summary>
        /// Creates the structured facts section from the query-layer project detail payload.
        /// </summary>
        /// <param name="detail">The project detail returned by the application query layer.</param>
        /// <returns>The MCP project facts section.</returns>
        private static ArchonMcpProjectFacts CreateProjectFacts(ProjectDetailDto detail)
        {
            // Facts are direct projections from persisted query DTOs and avoid inventing responsibilities, runtime roles, or missing metadata.
            ProjectCatalogueItemDto summary = detail.Summary;
            ArchonMcpProjectIdentityFacts identity = new(
                summary.StableKey,
                summary.Name,
                summary.Path,
                summary.Language,
                summary.TargetFramework,
                CreateProjectFormat(summary.IsSdkStyle),
                detail.ApplicationType,
                summary.ProjectType);
            ArchonMcpProjectGraphFacts graph = new(
                summary.DependencyCount,
                summary.DependentCount,
                summary.PackageCount,
                summary.EndpointCount,
                detail.ScopedGraphSummary.NodeCount,
                detail.ScopedGraphSummary.DataAccessCount,
                detail.ScopedGraphSummary.IntegrationCount,
                detail.References,
                detail.Dependents,
                detail.Packages);
            ArchonMcpProjectRuntimeFacts runtime = new(
                detail.EntryPoints,
                detail.Endpoints,
                detail.Workers,
                detail.DataAccess,
                detail.ConfigurationKeys,
                detail.Integrations);
            ArchonMcpProjectResponsibilityFacts[] responsibilities = detail.Responsibilities
                .OrderBy(responsibility => responsibility.Name, StringComparer.Ordinal)
                .Select(responsibility => new ArchonMcpProjectResponsibilityFacts(responsibility.Name, responsibility.Description, responsibility.EvidenceStableKeys))
                .ToArray();
            ArchonMcpProjectRiskFacts risk = new(
                summary.HotlistCount,
                detail.HotlistFindings,
                summary.RiskIndicators.HasHotlistFindings,
                summary.RiskIndicators.HighestSeverity,
                summary.RiskIndicators.HasUnknownData,
                summary.RiskIndicators.UnknownReason,
                summary.Confidence);

            return new ArchonMcpProjectFacts(identity, graph, runtime, responsibilities, risk, CreateMetadataDictionary(detail.Metadata));
        }

        /// <summary>
        /// Creates safe evidence references from project detail evidence and stable evidence keys.
        /// </summary>
        /// <param name="detail">The project detail containing evidence references and summary evidence keys.</param>
        /// <param name="context">The project query context that supplies snapshot identity.</param>
        /// <returns>Deterministically ordered evidence references.</returns>
        private static IReadOnlyList<ArchonMcpEvidenceReference> CreateEvidenceReferences(ProjectDetailDto detail, ProjectQueryContext context)
        {
            // Rich evidence references from project detail are preferred, then summary/responsibility keys fill any stable-key-only gaps.
            ArchonMcpSnapshotIdentity snapshot = CreateSnapshotIdentity(context);
            Dictionary<string, ArchonMcpEvidenceReference> references = new(StringComparer.Ordinal);
            foreach (EvidenceReferenceDto evidence in detail.Evidence.OrderBy(item => item.StableKey, StringComparer.Ordinal))
            {
                references[evidence.StableKey] = new ArchonMcpEvidenceReference(
                    evidence.StableKey,
                    evidence.EvidenceKind ?? "ProjectEvidenceReference",
                    evidence.FilePath,
                    evidence.StartLine,
                    evidence.EndLine,
                    evidence.SymbolName,
                    containingSymbol: null,
                    snippetPreview: null,
                    evidence.SnippetHash,
                    MapConfidence(detail.Summary.Confidence),
                    snapshot);
            }

            foreach (string evidenceStableKey in detail.Summary.EvidenceStableKeys.Concat(detail.Responsibilities.SelectMany(item => item.EvidenceStableKeys)).Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
            {
                references.TryAdd(evidenceStableKey, new ArchonMcpEvidenceReference(
                    evidenceStableKey,
                    "ProjectEvidenceReference",
                    sourcePath: null,
                    startLine: null,
                    endLine: null,
                    symbolName: null,
                    containingSymbol: null,
                    snippetPreview: null,
                    snippetHash: null,
                    MapConfidence(detail.Summary.Confidence),
                    snapshot));
            }

            return references.Values.OrderBy(reference => reference.StableKey, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Creates safe finding references from hotlist finding keys associated with the project.
        /// </summary>
        /// <param name="detail">The project detail containing hotlist finding stable keys.</param>
        /// <param name="context">The project query context that supplies snapshot identity.</param>
        /// <returns>Deterministically ordered finding references.</returns>
        private static IReadOnlyList<ArchonMcpFindingReference> CreateFindingReferences(ProjectDetailDto detail, ProjectQueryContext context)
        {
            // Project detail currently exposes finding stable keys and aggregate severity, so descriptions stay intentionally compact.
            ArchonMcpSnapshotIdentity snapshot = CreateSnapshotIdentity(context);
            return detail.HotlistFindings
                .OrderBy(findingStableKey => findingStableKey, StringComparer.Ordinal)
                .Select(findingStableKey => new ArchonMcpFindingReference(
                    findingStableKey,
                    "HotlistIndicator",
                    ruleVersion: null,
                    detail.Summary.RiskIndicators.HighestSeverity ?? "Unknown",
                    "Active",
                    MapConfidence(detail.Summary.Confidence),
                    [detail.Summary.StableKey],
                    detail.Summary.EvidenceStableKeys))
                .ToArray();
        }

        /// <summary>
        /// Creates unknown records from project detail and query context unknowns.
        /// </summary>
        /// <param name="detail">The project detail containing project-specific unknowns.</param>
        /// <param name="context">The project query context containing query-wide unknowns.</param>
        /// <returns>Deterministically ordered unknown records.</returns>
        private static IReadOnlyList<ArchonMcpUnknown> CreateUnknowns(ProjectDetailDto detail, ProjectQueryContext context)
        {
            // Unknowns make unavailable optional sections explicit so AI clients do not infer absence as certainty.
            List<ArchonMcpUnknown> unknowns = [];
            unknowns.AddRange(context.Unknowns.Select(unknown => new ArchonMcpUnknown(
                unknown.Field,
                affectedStableKey: null,
                unknown.Reason,
                "Project context reported incomplete data for the selected snapshot.",
                new ArchonMcpSuggestedFollowUp("Inspect snapshot extraction diagnostics before drawing conclusions.", "user.question", null))));
            unknowns.AddRange(detail.Unknowns.Select(unknown => new ArchonMcpUnknown(
                unknown.Field,
                detail.Summary.StableKey,
                unknown.Reason,
                "Project detail reported incomplete data for this project.",
                new ArchonMcpSuggestedFollowUp("Use archon.search or dependency traversal to inspect related persisted facts.", "user.question", new Dictionary<string, string> { ["projectStableKey"] = detail.Summary.StableKey }))));
            if (detail.Summary.HasUnknownData && !string.IsNullOrWhiteSpace(detail.Summary.UnknownReason))
            {
                unknowns.Add(new ArchonMcpUnknown(
                    "projectSummaryUnknownData",
                    detail.Summary.StableKey,
                    detail.Summary.UnknownReason,
                    "The project summary carries unknown-state metadata from persisted graph facts.",
                    new ArchonMcpSuggestedFollowUp("Review project evidence and related traversal data before assuming completeness.", "user.question", new Dictionary<string, string> { ["projectStableKey"] = detail.Summary.StableKey })));
            }

            return unknowns
                .OrderBy(unknown => unknown.Kind, StringComparer.Ordinal)
                .ThenBy(unknown => unknown.AffectedStableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates safe warnings from project detail and query context warnings.
        /// </summary>
        /// <param name="detail">The project detail containing project-specific warnings.</param>
        /// <param name="context">The project query context containing query-wide warnings.</param>
        /// <returns>Deterministically ordered safe warning records.</returns>
        private static IReadOnlyList<ArchonMcpWarning> CreateWarnings(ProjectDetailDto detail, ProjectQueryContext context)
        {
            // Warning records remain concise and do not echo source snippets, stack traces, or secret-bearing metadata.
            return context.Warnings.Select(warning => new ArchonMcpWarning(warning.Code, warning.Message, affectedStableKey: null))
                .Concat(detail.Warnings.Select(warning => new ArchonMcpWarning(warning.Code, warning.Message, detail.Summary.StableKey)))
                .OrderBy(warning => warning.Code, StringComparer.Ordinal)
                .ThenBy(warning => warning.AffectedStableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates safe response-wide follow-up suggestions for project investigation.
        /// </summary>
        /// <param name="detail">The project detail that supplies stable follow-up parameters.</param>
        /// <param name="limitFollowUps">The follow-ups generated by MCP limit enforcement.</param>
        /// <returns>Deterministically ordered follow-up suggestions.</returns>
        private static IReadOnlyList<ArchonMcpSuggestedFollowUp> CreateFollowUps(ProjectDetailDto detail, IEnumerable<ArchonMcpSuggestedFollowUp> limitFollowUps)
        {
            // Follow-ups stay within read-only Archon MCP investigation operations and stable-key parameters.
            Dictionary<string, string> parameters = new(StringComparer.Ordinal)
            {
                ["nodeStableKey"] = detail.Summary.StableKey
            };
            List<ArchonMcpSuggestedFollowUp> followUps = [.. limitFollowUps];
            followUps.Add(new ArchonMcpSuggestedFollowUp("Inspect this project's outgoing dependencies.", "archon.get_dependencies", parameters));
            followUps.Add(new ArchonMcpSuggestedFollowUp("Inspect this project's incoming dependents.", "archon.get_dependents", parameters));
            return followUps
                .OrderBy(followUp => followUp.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(followUp => followUp.Operation, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates a validation error envelope from MCP request validation failures.
        /// </summary>
        /// <param name="validationResult">The validation result produced before query execution.</param>
        /// <returns>A structured MCP validation error response.</returns>
        private static ArchonMcpErrorResponse CreateValidationError(ArchonMcpValidationResult validationResult)
        {
            // The public message references safe field names and validation text without echoing untrusted payloads wholesale.
            string message = string.Join(" ", validationResult.Failures.Select(failure => $"{failure.Field}: {failure.Message}"));
            return ArchonMcpErrorResponse.Create(
                ArchonMcpProjectOperation.Name,
                ArchonMcpErrorCategory.Validation,
                message,
                [new ArchonMcpSuggestedFollowUp("Correct project identity and snapshot scope before retrying.", "user.question", null)]);
        }

        /// <summary>
        /// Creates a concise natural-language summary grounded in returned project facts.
        /// </summary>
        /// <param name="detail">The project detail used to build the summary.</param>
        /// <returns>A safe project summary string.</returns>
        private static string CreateSummary(ProjectDetailDto detail)
        {
            // Summary text is intentionally limited to stable returned counts and identity fields to avoid unsupported architectural claims.
            return $"Project '{detail.Summary.Name}' has {detail.Summary.DependencyCount} outgoing dependencies, {detail.Summary.DependentCount} incoming dependents, {detail.Summary.PackageCount} packages, and {detail.Summary.EndpointCount} endpoints in the selected snapshot.";
        }

        /// <summary>
        /// Creates overall response confidence from project confidence and unknown records.
        /// </summary>
        /// <param name="detail">The project detail containing normalized confidence.</param>
        /// <param name="unknowns">The explicit unknowns mapped into the response.</param>
        /// <returns>The overall MCP confidence classification.</returns>
        private static ArchonMcpConfidence CreateConfidence(ProjectDetailDto detail, IReadOnlyList<ArchonMcpUnknown> unknowns)
        {
            // Unknowns lower confidence even when the selected project itself has persisted evidence.
            if (unknowns.Count > 0 || detail.Summary.HasUnknownData)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Project detail was returned, but explicit unknowns indicate some optional project data may be incomplete.");
            }

            return MapConfidence(detail.Summary.Confidence);
        }

        /// <summary>
        /// Converts numeric query confidence into the common MCP confidence vocabulary.
        /// </summary>
        /// <param name="confidence">The normalized decimal confidence returned by the query layer.</param>
        /// <returns>The MCP confidence classification and rationale.</returns>
        private static ArchonMcpConfidence MapConfidence(decimal confidence)
        {
            // Numeric thresholds mirror the search MCP mapper so confidence language remains consistent across tools.
            if (confidence >= 0.8m)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Persisted project facts provide strong support for this response.");
            }

            if (confidence >= 0.5m)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Persisted project facts provide partial support for this response.");
            }

            if (confidence > 0m)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Low, "Persisted project facts provide limited support for this response.");
            }

            return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Unknown, "Project confidence was not available in persisted data.");
        }

        /// <summary>
        /// Creates snapshot identity metadata from query-layer project context.
        /// </summary>
        /// <param name="context">The project context that includes resolved snapshot metadata.</param>
        /// <returns>The MCP snapshot identity record.</returns>
        private static ArchonMcpSnapshotIdentity CreateSnapshotIdentity(ProjectQueryContext context)
        {
            // Snapshot identity is explicit so clients can cite which extraction state produced the project detail.
            string selectionMode = context.Snapshot.ResolvedAsLatest ? "latest" : "explicit";
            return new ArchonMcpSnapshotIdentity(
                context.Snapshot.SnapshotStableKey,
                selectionMode,
                $"Project data resolved for repository '{context.Scope.RepositoryStableKey}' using {selectionMode} snapshot selection.");
        }

        /// <summary>
        /// Converts SDK-style status into a project format label without inventing unknown values.
        /// </summary>
        /// <param name="isSdkStyle">The nullable SDK-style flag returned by the query layer.</param>
        /// <returns>A project format label or <see langword="null" /> when unknown.</returns>
        private static string? CreateProjectFormat(bool? isSdkStyle)
        {
            // A null flag means extraction did not determine project format and should remain explicit unknown data elsewhere.
            return isSdkStyle switch
            {
                true => "SdkStyle",
                false => "NonSdkStyle",
                _ => null
            };
        }

        /// <summary>
        /// Converts sanitized graph metadata into deterministic string values for MCP facts.
        /// </summary>
        /// <param name="metadata">The graph metadata supplied by the project query layer.</param>
        /// <returns>A stable dictionary containing scalar metadata values.</returns>
        private static IReadOnlyDictionary<string, string> CreateMetadataDictionary(GraphMetadata metadata)
        {
            // Metadata values are already sanitized by the query layer; MCP keeps only scalar string representations for compact responses.
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            using JsonDocument document = JsonDocument.Parse(metadata.ToCanonicalJson());
            foreach (JsonProperty property in document.RootElement.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                values[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.GetRawText();
            }

            return values;
        }

        /// <summary>
        /// Checks a failed result for any of the supplied validation codes.
        /// </summary>
        /// <param name="projectResult">The failed project result to inspect.</param>
        /// <param name="codes">The validation codes that should match.</param>
        /// <returns><see langword="true" /> when any supplied code is present; otherwise, <see langword="false" />.</returns>
        private static bool HasAnyCode(ProjectDetailResult projectResult, params string[] codes)
        {
            // String comparer is ordinal because validation codes are stable machine-readable tokens.
            return projectResult.ValidationErrors.Any(error => codes.Contains(error.Code, StringComparer.Ordinal));
        }

        /// <summary>
        /// Creates compact disambiguation parameters for ambiguous project-name lookup follow-ups.
        /// </summary>
        /// <param name="options">The safe project candidates returned by the query layer.</param>
        /// <returns>A dictionary containing stable candidate identities.</returns>
        private static IReadOnlyDictionary<string, string> CreateDisambiguationParameters(IReadOnlyList<ProjectCatalogueItemDto> options)
        {
            // Follow-up metadata is compact because the structured error contract exposes parameters as strings rather than nested objects.
            string candidates = string.Join("|", options
                .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.StableKey, StringComparer.Ordinal)
                .Select(option => $"{option.StableKey},{option.Name},{option.Path}"));
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["candidates"] = candidates
            };
        }

        /// <summary>
        /// Creates safe audit parameters for project description requests.
        /// </summary>
        /// <param name="request">The request whose non-sensitive fields should be captured for audit.</param>
        /// <returns>Safe request parameters for audit normalization.</returns>
        private static IReadOnlyDictionary<string, string> CreateAuditParameters(ArchonMcpDescribeProjectRequest request)
        {
            // Audit captures project identity and scope shape without evidence snippets, source text, or sensitive values.
            Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);
            AddIfPresent(parameters, "projectStableKey", request.ProjectStableKey);
            AddIfPresent(parameters, "projectName", request.ProjectName);
            AddIfPresent(parameters, "snapshotSelector", request.SnapshotSelector);
            AddIfPresent(parameters, "repositoryStableKey", request.RepositoryStableKey);
            AddIfPresent(parameters, "solutionStableKey", request.SolutionStableKey);
            return parameters;
        }

        /// <summary>
        /// Adds a non-blank audit parameter value to the dictionary.
        /// </summary>
        /// <param name="parameters">The parameter dictionary being built.</param>
        /// <param name="name">The safe audit parameter name.</param>
        /// <param name="value">The optional parameter value.</param>
        private static void AddIfPresent(IDictionary<string, string> parameters, string name, string? value)
        {
            // Blank values are omitted so audit records focus on supplied request shape.
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters[name] = value.Trim();
            }
        }
    }
}
