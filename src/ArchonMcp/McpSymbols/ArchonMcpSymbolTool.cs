using Archon.Application.Symbols;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpSecurity;
using System.Globalization;

namespace ArchonMcp.McpSymbols
{
    /// <summary>
    /// Implements read-only MCP symbol description and usage tools over the approved symbol query abstraction.
    /// </summary>
    public sealed partial class ArchonMcpSymbolTool : IArchonMcpSymbolTool
    {
        /// <summary>
        /// Executes authorization, allow-listing, and audit behavior before symbol query logic runs.
        /// </summary>
        private readonly IArchonMcpOperationExecutor _operationExecutor;

        /// <summary>
        /// Validates common MCP request fields before query-layer execution.
        /// </summary>
        private readonly IArchonMcpRequestValidator _requestValidator;

        /// <summary>
        /// Executes controlled symbol detail and usage queries over persisted architecture snapshots.
        /// </summary>
        private readonly ISymbolQueryService _symbolQueryService;

        /// <summary>
        /// Applies configured MCP response limits to bounded symbol sections.
        /// </summary>
        private readonly ArchonMcpLimitGuard _limitGuard;

        /// <summary>
        /// Redacts secret-like text and labels source snippets as untrusted repository evidence.
        /// </summary>
        private readonly IArchonMcpSecureEvidenceMapper _secureEvidenceMapper;

        /// <summary>
        /// Creates a symbol MCP tool handler.
        /// </summary>
        /// <param name="operationExecutor">The executor that performs security, allow-listing, audit, and safe failure mapping.</param>
        /// <param name="requestValidator">The common MCP request validator.</param>
        /// <param name="symbolQueryService">The query-layer symbol abstraction used instead of direct repository file inspection.</param>
        /// <param name="limitGuard">The guard that applies configured MCP result limits.</param>
        /// <param name="secureEvidenceMapper">The mapper that redacts and labels untrusted snippet previews.</param>
        public ArchonMcpSymbolTool(
            IArchonMcpOperationExecutor operationExecutor,
            IArchonMcpRequestValidator requestValidator,
            ISymbolQueryService symbolQueryService,
            ArchonMcpLimitGuard limitGuard,
            IArchonMcpSecureEvidenceMapper secureEvidenceMapper)
        {
            // Constructor injection keeps symbol tools testable and ensures all facts flow through approved query abstractions.
            _operationExecutor = operationExecutor ?? throw new ArgumentNullException(nameof(operationExecutor));
            _requestValidator = requestValidator ?? throw new ArgumentNullException(nameof(requestValidator));
            _symbolQueryService = symbolQueryService ?? throw new ArgumentNullException(nameof(symbolQueryService));
            _limitGuard = limitGuard ?? throw new ArgumentNullException(nameof(limitGuard));
            _secureEvidenceMapper = secureEvidenceMapper ?? throw new ArgumentNullException(nameof(secureEvidenceMapper));
        }

        /// <inheritdoc />
        public async Task<object> DescribeSymbolAsync(ArchonMcpDescribeSymbolRequest request, CancellationToken cancellationToken)
        {
            // Authorization precedes validation and query execution so disabled or unauthorized symbol requests fail closed.
            ArgumentNullException.ThrowIfNull(request);
            ArchonMcpOperationResult result = await _operationExecutor.ExecuteAsync(
                ArchonMcpSymbolOperations.DescribeSymbol,
                CreateDescribeAuditParameters(request),
                () => ExecuteAuthorizedDescribeSymbolAsync(request, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return result.Payload;
        }

        /// <summary>
        /// Executes validation, symbol detail lookup, and envelope mapping after authorization succeeds.
        /// </summary>
        /// <param name="request">The authorized symbol description request.</param>
        /// <param name="cancellationToken">The token that can cancel symbol detail execution.</param>
        /// <returns>A symbol description envelope or structured MCP error response.</returns>
        private async Task<object> ExecuteAuthorizedDescribeSymbolAsync(ArchonMcpDescribeSymbolRequest request, CancellationToken cancellationToken)
        {
            // Request validation is deliberately performed inside the authorized delegate to preserve fail-closed behavior.
            ArchonMcpValidationResult validationResult = ValidateDescribeRequest(request);
            if (!validationResult.IsValid)
            {
                return CreateValidationError(ArchonMcpSymbolOperations.DescribeSymbol, validationResult, "Correct symbol identity and scope fields before retrying symbol description.");
            }

            SymbolDetailResult symbolResult;
            try
            {
                // The query service owns symbol resolution and ambiguity detection; MCP only maps safe output.
                symbolResult = await _symbolQueryService.GetSymbolAsync(CreateDetailQuery(request), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cooperative cancellation remains host behavior and should not be serialized as an MCP query failure.
                throw;
            }
            catch (Exception)
            {
                // Public errors omit exception details because query failures may contain internal extraction or persistence information.
                return ArchonMcpErrorResponse.Create(
                    ArchonMcpSymbolOperations.DescribeSymbol,
                    ArchonMcpErrorCategory.QueryLayerFailure,
                    "The symbol query layer failed before a safe response could be produced.",
                    [new ArchonMcpSuggestedFollowUp("Retry symbol description after verifying query data is available.", "user.question", null)]);
            }

            if (!symbolResult.Succeeded)
            {
                return MapSymbolDetailFailure(symbolResult);
            }

            return MapSymbolDetailSuccess(symbolResult);
        }

        /// <summary>
        /// Validates scope and identity rules for symbol description.
        /// </summary>
        /// <param name="request">The request whose fields should be validated.</param>
        /// <returns>A validation result containing every detected failure.</returns>
        private ArchonMcpValidationResult ValidateDescribeRequest(ArchonMcpDescribeSymbolRequest request)
        {
            // Common validation handles stable-key syntax, snapshot selectors, and search text; symbol identity rules reject missing or conflicting modes.
            List<ArchonMcpValidationFailure> failures = [];
            ArchonMcpValidationRequest validationRequest = new(
                request.SymbolStableKey,
                request.SnapshotSelector,
                request.SearchText,
                Filters: null,
                RequestedCount: null,
                RequestedDepth: null,
                PageNumber: null,
                PageSize: null);
            failures.AddRange(_requestValidator.Validate(validationRequest).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.RepositoryStableKey, nameof(request.RepositoryStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.SolutionStableKey, nameof(request.SolutionStableKey)).Failures);

            if (string.IsNullOrWhiteSpace(request.SymbolStableKey) && string.IsNullOrWhiteSpace(request.SearchText))
            {
                failures.Add(new ArchonMcpValidationFailure("symbolIdentity", "A symbol stable key or exact symbol search text is required."));
            }

            if (!string.IsNullOrWhiteSpace(request.SymbolStableKey) && !string.IsNullOrWhiteSpace(request.SearchText))
            {
                failures.Add(new ArchonMcpValidationFailure("symbolIdentity", "Use either symbol stable key or search text, not both."));
            }

            return new ArchonMcpValidationResult(failures);
        }

        /// <summary>
        /// Creates a controlled symbol detail query from a validated MCP request.
        /// </summary>
        /// <param name="request">The validated symbol description request.</param>
        /// <returns>A symbol detail query for the application layer.</returns>
        private static SymbolDetailQuery CreateDetailQuery(ArchonMcpDescribeSymbolRequest request)
        {
            // Symbol query selectors mirror API query selector behavior and keep latest resolution repository-bounded.
            SymbolSnapshotSelector selector = new(request.RepositoryStableKey, request.SolutionStableKey, request.SnapshotSelector);
            return new SymbolDetailQuery(selector, request.SymbolStableKey?.Trim(), request.SearchText?.Trim());
        }

        /// <summary>
        /// Maps a successful symbol detail result into the common MCP envelope.
        /// </summary>
        /// <param name="symbolResult">The successful query-layer symbol detail result.</param>
        /// <returns>A typed MCP success envelope containing symbol facts.</returns>
        private ArchonMcpEnvelope<ArchonMcpSymbolFacts> MapSymbolDetailSuccess(SymbolDetailResult symbolResult)
        {
            // Successful detail results must include symbol detail and context because validation failures return before this mapper.
            SymbolDetailDto detail = symbolResult.Detail ?? throw new InvalidOperationException("Symbol detail was not returned for a successful symbol result.");
            SymbolQueryContext context = symbolResult.Context ?? throw new InvalidOperationException("Symbol context was not returned for a successful symbol result.");
            ArchonMcpSymbolFacts facts = CreateSymbolFacts(detail);
            IReadOnlyList<ArchonMcpEvidenceReference> evidence = CreateDetailEvidenceReferences(detail, context);
            IReadOnlyList<ArchonMcpUnknown> unknowns = CreateDetailUnknowns(detail, context);
            IReadOnlyList<ArchonMcpWarning> warnings = CreateDetailWarnings(detail, context);
            ArchonMcpLimitedList<ArchonMcpEvidenceReference> limitedEvidence = _limitGuard.ApplyResultLimit(evidence, requestedLimit: null, ArchonMcpSymbolOperations.DescribeSymbol);

            return new ArchonMcpEnvelope<ArchonMcpSymbolFacts>(
                ArchonMcpSymbolOperations.DescribeSymbol,
                CreateSnapshotIdentity(context),
                CreateDetailSummary(detail),
                CreateConfidence(detail.Summary.Confidence, unknowns, "Persisted symbol facts provide strong support for this response."),
                facts,
                limitedEvidence.Items,
                CreateFindingReferences(detail, context),
                unknowns,
                warnings,
                limitedEvidence.Limits,
                CreateDetailFollowUps(detail, limitedEvidence.SuggestedFollowUps));
        }

        /// <summary>
        /// Maps query-layer symbol detail failures into safe MCP error responses.
        /// </summary>
        /// <param name="symbolResult">The failed symbol detail result.</param>
        /// <returns>A structured MCP error response.</returns>
        private static ArchonMcpErrorResponse MapSymbolDetailFailure(SymbolDetailResult symbolResult)
        {
            // Query-layer validation codes are mapped to broad MCP categories without exposing extraction internals.
            bool unavailable = HasAnyCode(symbolResult.ValidationErrors, SymbolQueryValidationCodes.RepositoryNotFound, SymbolQueryValidationCodes.SolutionNotFound, SymbolQueryValidationCodes.SnapshotNotFound);
            bool notFound = HasAnyCode(symbolResult.ValidationErrors, SymbolQueryValidationCodes.SymbolNotFound);
            bool ambiguous = HasAnyCode(symbolResult.ValidationErrors, SymbolQueryValidationCodes.SymbolSearchTextAmbiguous, SymbolQueryValidationCodes.SymbolIdentityAmbiguous);
            ArchonMcpErrorCategory category = unavailable
                ? ArchonMcpErrorCategory.DependencyUnavailable
                : ambiguous
                    ? ArchonMcpErrorCategory.Ambiguous
                    : notFound
                        ? ArchonMcpErrorCategory.NotFound
                        : ArchonMcpErrorCategory.Validation;
            string message = ambiguous
                ? "Symbol lookup is ambiguous; retry with an exact symbol stable key or use archon.search to disambiguate."
                : unavailable
                    ? "Symbol data is unavailable for the requested repository, solution, or snapshot scope."
                    : string.Join(" ", symbolResult.ValidationErrors.Select(error => error.Message));

            return ArchonMcpErrorResponse.Create(
                ArchonMcpSymbolOperations.DescribeSymbol,
                category,
                message,
                [new ArchonMcpSuggestedFollowUp("Search for candidate symbols and retry archon.describe_symbol with an exact stable key.", "archon.search", null)]);
        }

        /// <summary>
        /// Creates structured symbol facts from a query-layer detail payload.
        /// </summary>
        /// <param name="detail">The symbol detail returned by the application query layer.</param>
        /// <returns>The MCP symbol facts section.</returns>
        private ArchonMcpSymbolFacts CreateSymbolFacts(SymbolDetailDto detail)
        {
            // Facts are direct projections from persisted symbol DTOs, with snippet previews redacted and labeled as untrusted repository content.
            SymbolSearchItemDto summary = detail.Summary;
            ArchonMcpSymbolIdentityFacts identity = new(
                summary.StableKey,
                summary.Name,
                summary.FullyQualifiedName,
                summary.Kind,
                summary.Namespace,
                summary.ContainingType,
                summary.Language);
            ArchonMcpSymbolSourceFacts source = CreateSourceFacts(
                summary.StableKey,
                "SymbolSourcePreview",
                summary.SourceContext?.FilePath,
                summary.SourceContext?.StartLine,
                summary.SourceContext?.EndLine,
                summary.SourceContext?.SnippetPreview,
                snippetHash: null);
            ArchonMcpSymbolRelationshipFacts[] relationships = detail.Relationships
                .OrderBy(relationship => relationship.StableKey, StringComparer.Ordinal)
                .Select(relationship => new ArchonMcpSymbolRelationshipFacts(relationship.StableKey, relationship.Kind, relationship.SourceSymbolStableKey, relationship.TargetSymbolStableKey, relationship.EvidenceStableKeys, relationship.Confidence))
                .ToArray();

            return new ArchonMcpSymbolFacts(identity, summary.ContainingProjectStableKey, source, relationships, summary.EvidenceStableKeys, summary.HasUnknownData, summary.UnknownReason);
        }

        /// <summary>
        /// Creates bounded and redacted source facts from query-layer source context or evidence snippet text.
        /// </summary>
        /// <param name="stableKey">The stable evidence or symbol key associated with the snippet.</param>
        /// <param name="kind">The source or evidence kind used for untrusted evidence labeling.</param>
        /// <param name="filePath">The optional repository-relative file path.</param>
        /// <param name="startLine">The optional starting line number.</param>
        /// <param name="endLine">The optional ending line number.</param>
        /// <param name="snippetPreview">The optional snippet preview to redact and label.</param>
        /// <param name="snippetHash">The optional snippet hash.</param>
        /// <returns>The MCP source facts.</returns>
        private ArchonMcpSymbolSourceFacts CreateSourceFacts(string stableKey, string kind, string? filePath, int? startLine, int? endLine, string? snippetPreview, string? snippetHash)
        {
            // The secure mapper keeps snippet text separate from privileged instructions and removes secret-like values before output.
            ArchonMcpUntrustedEvidence untrustedEvidence = _secureEvidenceMapper.CreateUntrustedEvidence(stableKey, kind, snippetPreview);
            return new ArchonMcpSymbolSourceFacts(filePath, startLine, endLine, untrustedEvidence.RedactedContent, snippetHash, untrustedEvidence.TrustLabel);
        }

        /// <summary>
        /// Creates evidence references from symbol detail evidence and stable evidence keys.
        /// </summary>
        /// <param name="detail">The symbol detail containing evidence references.</param>
        /// <param name="context">The symbol query context that supplies snapshot identity.</param>
        /// <returns>Deterministically ordered evidence references.</returns>
        private IReadOnlyList<ArchonMcpEvidenceReference> CreateDetailEvidenceReferences(SymbolDetailDto detail, SymbolQueryContext context)
        {
            // Rich evidence references are preferred, then summary and relationship keys fill any stable-key-only gaps.
            ArchonMcpSnapshotIdentity snapshot = CreateSnapshotIdentity(context);
            Dictionary<string, ArchonMcpEvidenceReference> references = new(StringComparer.Ordinal);
            foreach (SymbolEvidenceReferenceDto evidence in detail.Evidence.OrderBy(item => item.StableKey, StringComparer.Ordinal))
            {
                ArchonMcpSymbolSourceFacts source = CreateSourceFacts(evidence.StableKey, evidence.EvidenceKind, evidence.FilePath, evidence.StartLine, evidence.EndLine, evidence.SnippetPreview, evidence.SnippetHash);
                references[evidence.StableKey] = new ArchonMcpEvidenceReference(
                    evidence.StableKey,
                    evidence.EvidenceKind,
                    evidence.FilePath,
                    evidence.StartLine,
                    evidence.EndLine,
                    evidence.SymbolName,
                    evidence.ContainingSymbol,
                    source.SnippetPreview,
                    evidence.SnippetHash,
                    MapConfidence(evidence.Confidence),
                    snapshot);
            }

            foreach (string evidenceStableKey in detail.Summary.EvidenceStableKeys.Concat(detail.Relationships.SelectMany(relationship => relationship.EvidenceStableKeys)).Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
            {
                references.TryAdd(evidenceStableKey, new ArchonMcpEvidenceReference(evidenceStableKey, "SymbolEvidenceReference", sourcePath: null, startLine: null, endLine: null, symbolName: null, containingSymbol: null, snippetPreview: null, snippetHash: null, MapConfidence(detail.Summary.Confidence), snapshot));
            }

            return references.Values.OrderBy(reference => reference.StableKey, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Creates safe finding-like references for rules associated with symbol context.
        /// </summary>
        /// <param name="detail">The symbol detail that supplies relationship kinds and evidence keys.</param>
        /// <param name="context">The symbol query context that supplies snapshot identity.</param>
        /// <returns>Deterministically ordered finding references.</returns>
        private static IReadOnlyList<ArchonMcpFindingReference> CreateFindingReferences(SymbolDetailDto detail, SymbolQueryContext context)
        {
            // Symbol detail currently exposes rule-like relationship classifications, so findings are compact investigation pointers rather than remediation claims.
            ArchonMcpConfidence confidence = MapConfidence(detail.Summary.Confidence);
            return detail.Relationships
                .Where(relationship => relationship.Kind.Contains("Rule", StringComparison.OrdinalIgnoreCase) || relationship.StableKey.StartsWith("rule://", StringComparison.Ordinal))
                .OrderBy(relationship => relationship.StableKey, StringComparer.Ordinal)
                .Select(relationship => new ArchonMcpFindingReference(relationship.StableKey, relationship.Kind, ruleVersion: null, severity: "Informational", status: "Observed", confidence, [detail.Summary.StableKey], relationship.EvidenceStableKeys))
                .ToArray();
        }

        /// <summary>
        /// Creates explicit unknown records from symbol detail and query context unknowns.
        /// </summary>
        /// <param name="detail">The symbol detail containing symbol-specific unknowns.</param>
        /// <param name="context">The symbol query context containing query-wide unknowns.</param>
        /// <returns>Deterministically ordered unknown records.</returns>
        private static IReadOnlyList<ArchonMcpUnknown> CreateDetailUnknowns(SymbolDetailDto detail, SymbolQueryContext context)
        {
            // Unknowns prevent clients from treating missing dynamic or semantic information as proven absence.
            List<ArchonMcpUnknown> unknowns = [];
            unknowns.AddRange(context.Unknowns.Select(unknown => new ArchonMcpUnknown(unknown.Field, affectedStableKey: null, unknown.Reason, "Symbol query context reported incomplete data for the selected snapshot.", new ArchonMcpSuggestedFollowUp("Inspect snapshot extraction diagnostics before drawing symbol conclusions.", "user.question", null))));
            unknowns.AddRange(detail.Unknowns.Select(unknown => new ArchonMcpUnknown(unknown.Field, detail.Summary.StableKey, unknown.Reason, "Symbol detail reported incomplete semantic data for this symbol.", new ArchonMcpSuggestedFollowUp("Use archon.find_symbol_usages to inspect persisted symbol relationships.", ArchonMcpSymbolOperations.FindSymbolUsages, new Dictionary<string, string> { ["symbolStableKey"] = detail.Summary.StableKey }))));
            if (detail.Summary.HasUnknownData && !string.IsNullOrWhiteSpace(detail.Summary.UnknownReason))
            {
                unknowns.Add(new ArchonMcpUnknown("symbolSummaryUnknownData", detail.Summary.StableKey, detail.Summary.UnknownReason, "The symbol summary carries unknown-state metadata from persisted graph facts.", new ArchonMcpSuggestedFollowUp("Inspect symbol evidence before assuming semantic completeness.", "user.question", new Dictionary<string, string> { ["symbolStableKey"] = detail.Summary.StableKey })));
            }

            return unknowns
                .OrderBy(unknown => unknown.Kind, StringComparer.Ordinal)
                .ThenBy(unknown => unknown.AffectedStableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates safe warnings from symbol detail and query context warnings.
        /// </summary>
        /// <param name="detail">The symbol detail containing symbol-specific warnings.</param>
        /// <param name="context">The symbol query context containing query-wide warnings.</param>
        /// <returns>Deterministically ordered safe warning records.</returns>
        private static IReadOnlyList<ArchonMcpWarning> CreateDetailWarnings(SymbolDetailDto detail, SymbolQueryContext context)
        {
            // Warning records stay concise and avoid echoing source snippets or extraction internals.
            return context.Warnings.Select(warning => new ArchonMcpWarning(warning.Code, warning.Message, affectedStableKey: null))
                .Concat(detail.Warnings.Select(warning => new ArchonMcpWarning(warning.Code, warning.Message, detail.Summary.StableKey)))
                .OrderBy(warning => warning.Code, StringComparer.Ordinal)
                .ThenBy(warning => warning.AffectedStableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates safe response-wide follow-up suggestions for symbol description.
        /// </summary>
        /// <param name="detail">The symbol detail that supplies stable follow-up parameters.</param>
        /// <param name="limitFollowUps">The follow-ups generated by MCP limit enforcement.</param>
        /// <returns>Deterministically ordered follow-up suggestions.</returns>
        private static IReadOnlyList<ArchonMcpSuggestedFollowUp> CreateDetailFollowUps(SymbolDetailDto detail, IEnumerable<ArchonMcpSuggestedFollowUp> limitFollowUps)
        {
            // Follow-ups stay within read-only Archon MCP investigation operations and stable-key parameters.
            Dictionary<string, string> parameters = new(StringComparer.Ordinal)
            {
                ["symbolStableKey"] = detail.Summary.StableKey
            };
            List<ArchonMcpSuggestedFollowUp> followUps = [.. limitFollowUps];
            followUps.Add(new ArchonMcpSuggestedFollowUp("Find callers and references for this symbol.", ArchonMcpSymbolOperations.FindSymbolUsages, parameters));
            followUps.Add(new ArchonMcpSuggestedFollowUp("Search for related architecture facts.", "archon.search", new Dictionary<string, string> { ["searchText"] = detail.Summary.Name }));
            return followUps
                .OrderBy(followUp => followUp.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(followUp => followUp.Operation, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates a concise symbol detail summary grounded in returned facts.
        /// </summary>
        /// <param name="detail">The symbol detail being summarized.</param>
        /// <returns>A safe natural-language summary.</returns>
        private static string CreateDetailSummary(SymbolDetailDto detail)
        {
            // Summary text reports only symbol identity, kind, project, and relationship count without inferring unsupported behavior.
            return $"Symbol '{detail.Summary.Name}' ({detail.Summary.Kind}) was described with {detail.Relationships.Count} related persisted relationships.";
        }

        /// <summary>
        /// Creates snapshot identity metadata from query-layer symbol context.
        /// </summary>
        /// <param name="context">The symbol context that includes resolved snapshot metadata.</param>
        /// <returns>The MCP snapshot identity record.</returns>
        private static ArchonMcpSnapshotIdentity CreateSnapshotIdentity(SymbolQueryContext context)
        {
            // Snapshot identity is explicit so clients can cite which graph state produced symbol facts.
            string selectionMode = context.Snapshot.ResolvedAsLatest ? "latest" : "explicit";
            return new ArchonMcpSnapshotIdentity(context.Snapshot.SnapshotStableKey, selectionMode, $"Symbol data resolved for repository '{context.Scope.RepositoryStableKey}' using {selectionMode} snapshot selection.");
        }

        /// <summary>
        /// Creates overall response confidence from numeric confidence and unknown records.
        /// </summary>
        /// <param name="confidence">The normalized decimal confidence returned by the query layer.</param>
        /// <param name="unknowns">The explicit unknowns mapped into the response.</param>
        /// <param name="highRationale">The rationale to use for high-confidence responses.</param>
        /// <returns>The overall MCP confidence classification.</returns>
        private static ArchonMcpConfidence CreateConfidence(decimal confidence, IReadOnlyList<ArchonMcpUnknown> unknowns, string highRationale)
        {
            // Unknowns lower the overall classification because semantic extraction can be incomplete even when individual facts have high confidence.
            if (unknowns.Count > 0)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Symbol query completed, but explicit unknowns indicate some semantic data may be incomplete.");
            }

            return confidence >= 0.8m
                ? new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, highRationale)
                : MapConfidence(confidence);
        }

        /// <summary>
        /// Converts numeric query confidence into the common MCP confidence vocabulary.
        /// </summary>
        /// <param name="confidence">The normalized decimal confidence returned by the query layer.</param>
        /// <returns>The MCP confidence classification and rationale.</returns>
        private static ArchonMcpConfidence MapConfidence(decimal confidence)
        {
            // Numeric thresholds mirror other MCP mappers so confidence language remains consistent.
            if (confidence >= 0.8m)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Persisted symbol evidence provides strong support for this reference.");
            }

            if (confidence >= 0.5m)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Persisted symbol evidence provides partial support for this reference.");
            }

            if (confidence > 0m)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Low, "Persisted symbol evidence provides limited support for this reference.");
            }

            return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Unknown, "Symbol confidence was not available in persisted data.");
        }

        /// <summary>
        /// Creates a validation error response from MCP request validation failures.
        /// </summary>
        /// <param name="operationName">The stable operation name being validated.</param>
        /// <param name="validationResult">The validation result produced before query execution.</param>
        /// <param name="followUpLabel">The safe follow-up label to return with the error.</param>
        /// <returns>A structured MCP validation error response.</returns>
        private static ArchonMcpErrorResponse CreateValidationError(string operationName, ArchonMcpValidationResult validationResult, string followUpLabel)
        {
            // The message references safe field names and bounded validation rules without echoing source snippets or query internals.
            string message = string.Join(" ", validationResult.Failures.Select(failure => $"{failure.Field}: {failure.Message}"));
            return ArchonMcpErrorResponse.Create(
                operationName,
                ArchonMcpErrorCategory.Validation,
                message,
                [new ArchonMcpSuggestedFollowUp(followUpLabel, "user.question", null)]);
        }

        /// <summary>
        /// Checks validation errors for any of the supplied validation codes.
        /// </summary>
        /// <param name="errors">The validation errors to inspect.</param>
        /// <param name="codes">The validation codes that should match.</param>
        /// <returns><see langword="true" /> when any supplied code is present; otherwise, <see langword="false" />.</returns>
        private static bool HasAnyCode(IEnumerable<SymbolQueryValidationError> errors, params string[] codes)
        {
            // String comparer is ordinal because validation codes are stable machine-readable tokens.
            return errors.Any(error => codes.Contains(error.Code, StringComparer.Ordinal));
        }

        /// <summary>
        /// Creates safe audit parameters for symbol description requests.
        /// </summary>
        /// <param name="request">The request whose non-sensitive fields should be captured for audit.</param>
        /// <returns>Safe request parameters for audit normalization.</returns>
        private static IReadOnlyDictionary<string, string> CreateDescribeAuditParameters(ArchonMcpDescribeSymbolRequest request)
        {
            // Audit captures symbol identity and scope without source snippets or evidence content.
            Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);
            AddIfPresent(parameters, "symbolStableKey", request.SymbolStableKey);
            AddIfPresent(parameters, "searchText", request.SearchText);
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
            // Blank values are omitted so audit records focus on supplied symbol request shape.
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters[name] = value.Trim();
            }
        }

        /// <summary>
        /// Adds an integer audit parameter when a caller supplied a value.
        /// </summary>
        /// <param name="parameters">The parameter dictionary being built.</param>
        /// <param name="name">The safe audit parameter name.</param>
        /// <param name="value">The optional integer value.</param>
        private static void AddIfPresent(IDictionary<string, string> parameters, string name, int? value)
        {
            // Culture-invariant formatting keeps audit metadata stable across developer machines.
            if (value is not null)
            {
                parameters[name] = value.Value.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
