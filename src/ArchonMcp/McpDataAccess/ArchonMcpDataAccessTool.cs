using Archon.Application.Facts;
using Archon.Application.Rules;
using Archon.Domain.Graph.Metadata;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpSecurity;

namespace ArchonMcp.McpDataAccess
{
    /// <summary>
    /// Implements the read-only MCP data-access usage tool over the approved fact-query abstraction.
    /// </summary>
    public sealed class ArchonMcpDataAccessTool : IArchonMcpDataAccessTool
    {
        /// <summary>
        /// Executes authorization, allow-listing, and audit behavior before data-access query logic runs.
        /// </summary>
        private readonly IArchonMcpOperationExecutor _operationExecutor;

        /// <summary>
        /// Validates common MCP request fields before query-layer execution.
        /// </summary>
        private readonly IArchonMcpRequestValidator _requestValidator;

        /// <summary>
        /// Executes bounded fact queries over persisted architecture snapshots.
        /// </summary>
        private readonly IFactQueryService _factQueryService;

        /// <summary>
        /// Applies configured MCP response limits to data-access usage records.
        /// </summary>
        private readonly ArchonMcpLimitGuard _limitGuard;

        /// <summary>
        /// Maps evidence references while redacting untrusted snippet previews.
        /// </summary>
        private readonly IArchonMcpResponseMapper _responseMapper;

        /// <summary>
        /// Creates a data-access MCP tool handler.
        /// </summary>
        /// <param name="operationExecutor">The executor that performs security, allow-listing, audit, and safe failure mapping.</param>
        /// <param name="requestValidator">The common MCP request validator.</param>
        /// <param name="factQueryService">The query-layer fact abstraction used instead of direct SQL, Cypher, Neo4j, or filesystem access.</param>
        /// <param name="limitGuard">The guard that applies configured MCP result limits.</param>
        /// <param name="responseMapper">The mapper that creates secret-safe evidence references.</param>
        public ArchonMcpDataAccessTool(
            IArchonMcpOperationExecutor operationExecutor,
            IArchonMcpRequestValidator requestValidator,
            IFactQueryService factQueryService,
            ArchonMcpLimitGuard limitGuard,
            IArchonMcpResponseMapper responseMapper)
        {
            // Constructor injection keeps the tool testable and prevents MCP handlers from bypassing approved query seams.
            _operationExecutor = operationExecutor ?? throw new ArgumentNullException(nameof(operationExecutor));
            _requestValidator = requestValidator ?? throw new ArgumentNullException(nameof(requestValidator));
            _factQueryService = factQueryService ?? throw new ArgumentNullException(nameof(factQueryService));
            _limitGuard = limitGuard ?? throw new ArgumentNullException(nameof(limitGuard));
            _responseMapper = responseMapper ?? throw new ArgumentNullException(nameof(responseMapper));
        }

        /// <inheritdoc />
        public async Task<object> GetDataAccessUsageAsync(ArchonMcpDataAccessUsageRequest request, CancellationToken cancellationToken)
        {
            // Authorization precedes validation and query execution so disabled or unauthorized data-access requests fail closed.
            ArgumentNullException.ThrowIfNull(request);
            ArchonMcpOperationResult result = await _operationExecutor.ExecuteAsync(
                ArchonMcpDataAccessOperations.GetDataAccessUsage,
                CreateAuditParameters(request),
                () => ExecuteAuthorizedGetDataAccessUsageAsync(request, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return result.Payload;
        }

        /// <summary>
        /// Executes validation, fact query, and response-envelope mapping after authorization succeeds.
        /// </summary>
        /// <param name="request">The authorized data-access usage request.</param>
        /// <param name="cancellationToken">The token that can cancel fact-query execution.</param>
        /// <returns>A data-access usage envelope or structured MCP error response.</returns>
        private async Task<object> ExecuteAuthorizedGetDataAccessUsageAsync(ArchonMcpDataAccessUsageRequest request, CancellationToken cancellationToken)
        {
            // Validation remains inside the authorized delegate to preserve the same fail-closed ordering as other MCP tools.
            ArchonMcpValidationResult validationResult = ValidateRequest(request);
            if (!validationResult.IsValid)
            {
                return CreateValidationError(validationResult);
            }

            DataAccessFactResult dataAccessResult;
            try
            {
                // The application fact service owns snapshot resolution, data-access family semantics, and persisted fact filtering.
                dataAccessResult = await _factQueryService.ListDataAccessFactsAsync(CreateQuery(request), cancellationToken).ConfigureAwait(false);
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
                    ArchonMcpDataAccessOperations.GetDataAccessUsage,
                    ArchonMcpErrorCategory.QueryLayerFailure,
                    "The data-access fact query layer failed before a safe response could be produced.",
                    [new ArchonMcpSuggestedFollowUp("Retry data-access usage after verifying fact query data is available.", "user.question", null)]);
            }

            if (!dataAccessResult.Succeeded)
            {
                return MapFailure(dataAccessResult);
            }

            return MapSuccess(request, dataAccessResult);
        }

        /// <summary>
        /// Validates scope, stable-key filters, text filters, family filters, and result limits for data-access usage.
        /// </summary>
        /// <param name="request">The request whose fields should be validated.</param>
        /// <returns>A validation result containing every detected failure.</returns>
        private ArchonMcpValidationResult ValidateRequest(ArchonMcpDataAccessUsageRequest request)
        {
            // Common validation handles snapshot selectors and limit bounds; tool-specific validation handles stable-key filters and simple text fields.
            List<ArchonMcpValidationFailure> failures = [];
            ArchonMcpValidationRequest validationRequest = new(
                StableKey: null,
                request.SnapshotSelector,
                SearchText: null,
                Filters: string.IsNullOrWhiteSpace(request.Family) ? null : [request.Family],
                request.Limit,
                RequestedDepth: null,
                PageNumber: null,
                PageSize: null);
            failures.AddRange(_requestValidator.Validate(validationRequest).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.ProjectStableKey, nameof(request.ProjectStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.DataContextStableKey, nameof(request.DataContextStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.RepositoryStableKey, nameof(request.RepositoryStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.SolutionStableKey, nameof(request.SolutionStableKey)).Failures);

            AddTextFilterFailure(failures, request.Entity, nameof(request.Entity));
            AddTextFilterFailure(failures, request.Table, nameof(request.Table));
            AddTextFilterFailure(failures, request.StoredProcedure, nameof(request.StoredProcedure));
            if (request.Limit is < 1)
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.Limit), "Limit must be one or greater when supplied."));
            }

            return new ArchonMcpValidationResult(failures);
        }

        /// <summary>
        /// Appends a validation failure when an optional text filter is supplied as whitespace.
        /// </summary>
        /// <param name="failures">The aggregate failure list being built.</param>
        /// <param name="value">The optional filter value to inspect.</param>
        /// <param name="fieldName">The safe request field name used in validation failures.</param>
        private static void AddTextFilterFailure(List<ArchonMcpValidationFailure> failures, string? value, string fieldName)
        {
            // Empty text filters are ambiguous and should be corrected before the fact query layer runs.
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                failures.Add(new ArchonMcpValidationFailure(fieldName, "Filter value must not be empty when supplied."));
            }
        }

        /// <summary>
        /// Creates a controlled application-layer data-access fact query from a validated MCP request.
        /// </summary>
        /// <param name="request">The validated MCP data-access usage request.</param>
        /// <returns>A data-access fact query for the application layer.</returns>
        private static DataAccessFactQuery CreateQuery(ArchonMcpDataAccessUsageRequest request)
        {
            // The existing query seam does not expose a data-context filter, so MCP maps supported filters directly and applies data-context filtering after query execution.
            FactSnapshotSelector selector = new(request.RepositoryStableKey, request.SolutionStableKey, request.SnapshotSelector);
            int take = request.Limit.GetValueOrDefault(FactQueryLimits.DefaultTake);
            return new DataAccessFactQuery(
                selector,
                request.Family?.Trim(),
                request.ProjectStableKey?.Trim(),
                UsageSite: null,
                request.Entity?.Trim(),
                request.Table?.Trim(),
                request.StoredProcedure?.Trim(),
                Skip: 0,
                take);
        }

        /// <summary>
        /// Maps query-layer failures into safe structured MCP error responses.
        /// </summary>
        /// <param name="dataAccessResult">The failed data-access query result.</param>
        /// <returns>A structured MCP error response.</returns>
        private static ArchonMcpErrorResponse MapFailure(DataAccessFactResult dataAccessResult)
        {
            // Fact query validation codes are broadened to MCP categories without exposing persistence details.
            bool unavailable = HasAnyCode(dataAccessResult.ValidationErrors, FactQueryValidationCodes.RepositoryNotFound, FactQueryValidationCodes.SolutionNotFound, FactQueryValidationCodes.SnapshotNotFound);
            ArchonMcpErrorCategory category = unavailable ? ArchonMcpErrorCategory.DependencyUnavailable : ArchonMcpErrorCategory.Validation;
            string message = unavailable
                ? "Data-access fact data is unavailable for the requested repository, solution, or snapshot scope."
                : string.Join(" ", dataAccessResult.ValidationErrors.Select(error => error.Message));

            return ArchonMcpErrorResponse.Create(
                ArchonMcpDataAccessOperations.GetDataAccessUsage,
                category,
                message,
                [new ArchonMcpSuggestedFollowUp("Check repository, solution, snapshot, and data-access filters before retrying usage lookup.", "user.question", null)]);
        }

        /// <summary>
        /// Maps a successful data-access result into the common MCP envelope.
        /// </summary>
        /// <param name="request">The original MCP request containing caller filters and limits.</param>
        /// <param name="dataAccessResult">The successful query-layer result.</param>
        /// <returns>A typed MCP success envelope containing data-access usage facts.</returns>
        private ArchonMcpEnvelope<ArchonMcpDataAccessUsageFacts> MapSuccess(ArchonMcpDataAccessUsageRequest request, DataAccessFactResult dataAccessResult)
        {
            // Query-layer paging plus MCP limiting keep large data-access inventories bounded and explain any truncation to clients.
            PagedQueryResult<DataAccessFactDto> page = dataAccessResult.Page ?? throw new InvalidOperationException("Data-access page was not returned for a successful fact result.");
            FactQueryContext context = dataAccessResult.Context ?? throw new InvalidOperationException("Data-access context was not returned for a successful fact result.");
            DataAccessFactDto[] filteredRows = page.Items
                .Where(item => string.IsNullOrWhiteSpace(request.DataContextStableKey) || StringComparer.Ordinal.Equals(item.DataContextStableKey, request.DataContextStableKey.Trim()))
                .OrderBy(item => item.Family, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ProjectStableKey ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.StableKey, StringComparer.Ordinal)
                .ToArray();
            ArchonMcpDataAccessUsageRecord[] records = filteredRows.Select(MapRecord).ToArray();
            ArchonMcpLimitedList<ArchonMcpDataAccessUsageRecord> limitedRecords = _limitGuard.ApplyResultLimit(records, request.Limit, ArchonMcpDataAccessOperations.GetDataAccessUsage);
            ArchonMcpDataAccessUsageFacts facts = new(
                request.ProjectStableKey,
                request.DataContextStableKey,
                request.Entity,
                request.Table,
                request.StoredProcedure,
                request.Family,
                page.TotalCount,
                limitedRecords.Items);
            IReadOnlyList<ArchonMcpEvidenceReference> evidence = CreateEvidenceReferences(limitedRecords.Items, context);
            IReadOnlyList<ArchonMcpUnknown> unknowns = CreateUnknowns(limitedRecords.Items, context);
            IReadOnlyList<ArchonMcpWarning> warnings = CreateWarnings(context, limitedRecords.Limits, limitedRecords.Items.Count);

            return new ArchonMcpEnvelope<ArchonMcpDataAccessUsageFacts>(
                ArchonMcpDataAccessOperations.GetDataAccessUsage,
                CreateSnapshotIdentity(context),
                CreateSummary(facts),
                CreateConfidence(limitedRecords.Items, unknowns),
                facts,
                evidence,
                findings: null,
                unknowns,
                warnings,
                limitedRecords.Limits,
                CreateFollowUps(facts, limitedRecords.SuggestedFollowUps));
        }

        /// <summary>
        /// Maps one query-layer data-access DTO into an MCP usage record.
        /// </summary>
        /// <param name="item">The query-layer data-access fact row.</param>
        /// <returns>The MCP data-access usage record.</returns>
        private static ArchonMcpDataAccessUsageRecord MapRecord(DataAccessFactDto item)
        {
            // Operation and dynamic-SQL indicators come from explicit fields and sanitized metadata only; MCP does not inspect source text or SQL.
            IReadOnlyList<string> operationKinds = NormalizeOperationKinds(item);
            bool dynamicSql = MetadataContainsTrue(item.Metadata, "dynamicSql") || string.Equals(item.Family, "RawSql", StringComparison.OrdinalIgnoreCase) && item.HasUnknownData;
            return new ArchonMcpDataAccessUsageRecord(
                item.StableKey,
                item.Family,
                item.Name,
                item.ProjectStableKey,
                item.DataContextStableKey,
                item.EntityStableKey,
                item.TableStableKey,
                item.StoredProcedureStableKey,
                item.UsageSites,
                operationKinds,
                dynamicSql,
                item.EvidenceStableKeys,
                item.Confidence,
                item.HasUnknownData,
                item.UnknownReason);
        }

        /// <summary>
        /// Normalizes operation names into read, write, execute, and unknown-style categories.
        /// </summary>
        /// <param name="item">The data-access fact row whose operation names should be normalized.</param>
        /// <returns>The normalized operation kinds.</returns>
        private static IReadOnlyList<string> NormalizeOperationKinds(DataAccessFactDto item)
        {
            // The query layer may return method-like operations; MCP groups them into investigation-friendly operation kinds.
            string[] sourceOperations = item.Operations.Count > 0 ? item.Operations.ToArray() : [TryGetMetadataString(item.Metadata, "operationKind") ?? "Unknown"];
            return sourceOperations
                .Select(NormalizeOperationKind)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(operation => operation, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Normalizes one raw operation label into a controlled operation kind.
        /// </summary>
        /// <param name="operation">The operation label from query data or metadata.</param>
        /// <returns>A controlled operation kind.</returns>
        private static string NormalizeOperationKind(string? operation)
        {
            // Operation labels are intentionally broad so AI clients do not infer precise database behavior unsupported by persisted facts.
            if (string.IsNullOrWhiteSpace(operation))
            {
                return "Unknown";
            }

            string normalized = operation.Trim();
            if (ContainsAny(normalized, "read", "query", "select", "get", "find", "load"))
            {
                return "Read";
            }

            if (ContainsAny(normalized, "write", "insert", "update", "delete", "save", "submit"))
            {
                return "Write";
            }

            if (ContainsAny(normalized, "execute", "exec", "procedure", "stored"))
            {
                return "Execute";
            }

            return string.Equals(normalized, "Unknown", StringComparison.OrdinalIgnoreCase) ? "Unknown" : normalized;
        }

        /// <summary>
        /// Determines whether text contains any candidate tokens using ordinal-ignore-case comparison.
        /// </summary>
        /// <param name="text">The text to inspect.</param>
        /// <param name="tokens">The tokens to search for.</param>
        /// <returns><see langword="true" /> when any token is present; otherwise, <see langword="false" />.</returns>
        private static bool ContainsAny(string text, params string[] tokens)
        {
            // A helper keeps operation-kind normalization readable while avoiding regex complexity for controlled token checks.
            return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Reads a boolean-like metadata field from sanitized graph metadata.
        /// </summary>
        /// <param name="metadata">The sanitized metadata payload to inspect.</param>
        /// <param name="key">The metadata key to find.</param>
        /// <returns><see langword="true" /> when the metadata field is present and true; otherwise, <see langword="false" />.</returns>
        private static bool MetadataContainsTrue(GraphMetadata metadata, string key)
        {
            // GraphMetadata exposes canonical JSON, so simple quoted-token checks are enough for sanitized boolean metadata used by tests and extractors.
            string json = metadata.ToCanonicalJson();
            return json.Contains($"\"{key}\":true", StringComparison.OrdinalIgnoreCase) || json.Contains($"\"{key}\":\"true\"", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads a string-like metadata field from sanitized graph metadata.
        /// </summary>
        /// <param name="metadata">The sanitized metadata payload to inspect.</param>
        /// <param name="key">The metadata key to find.</param>
        /// <returns>The metadata string value when available; otherwise, <see langword="null" />.</returns>
        private static string? TryGetMetadataString(GraphMetadata metadata, string key)
        {
            // This narrow parser avoids adding dependencies for one safe metadata hint and falls back to unknown when the canonical shape differs.
            string json = metadata.ToCanonicalJson();
            string prefix = $"\"{key}\":\"";
            int start = json.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return null;
            }

            int valueStart = start + prefix.Length;
            int valueEnd = json.IndexOf('"', valueStart);
            return valueEnd > valueStart ? json[valueStart..valueEnd] : null;
        }

        /// <summary>
        /// Creates evidence references from bounded data-access usage records.
        /// </summary>
        /// <param name="records">The bounded records that may carry evidence stable keys.</param>
        /// <param name="context">The fact query context that supplies snapshot identity.</param>
        /// <returns>Deterministically ordered evidence references.</returns>
        private IReadOnlyList<ArchonMcpEvidenceReference> CreateEvidenceReferences(IEnumerable<ArchonMcpDataAccessUsageRecord> records, FactQueryContext context)
        {
            // Data-access DTOs currently expose stable evidence keys only, so MCP evidence references preserve stable identity without inventing source ranges.
            ArchonMcpSnapshotIdentity snapshot = CreateSnapshotIdentity(context);
            return records
                .SelectMany(record => record.EvidenceStableKeys.Select(key => _responseMapper.MapEvidence(
                    key,
                    "DataAccessEvidence",
                    sourcePath: null,
                    startLine: null,
                    endLine: null,
                    symbolName: record.Name,
                    containingSymbol: record.ProjectStableKey,
                    snippetPreview: CreateEvidencePreview(record),
                    snippetHash: null,
                    new ArchonMcpConfidence(ToConfidenceLevel(record.Confidence), "Evidence reference is associated with a persisted data-access fact."),
                    snapshot)))
                .GroupBy(evidence => evidence.StableKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(evidence => evidence.StableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates a bounded evidence preview from safe fact fields.
        /// </summary>
        /// <param name="record">The data-access record used to build a safe preview.</param>
        /// <returns>A safe preview string that still passes through the shared redactor.</returns>
        private static string CreateEvidencePreview(ArchonMcpDataAccessUsageRecord record)
        {
            // The preview intentionally uses fact names and operation labels rather than SQL text, connection strings, or raw source snippets.
            return $"{record.Family} {record.Name} operations: {string.Join(", ", record.OperationKinds)}";
        }

        /// <summary>
        /// Creates explicit unknowns from query context and fact-level unknown data.
        /// </summary>
        /// <param name="records">The bounded data-access usage records.</param>
        /// <param name="context">The fact query context containing query-level unknowns.</param>
        /// <returns>Distinct MCP unknown records.</returns>
        private static IReadOnlyList<ArchonMcpUnknown> CreateUnknowns(IEnumerable<ArchonMcpDataAccessUsageRecord> records, FactQueryContext context)
        {
            // Unknowns protect clients from treating dynamic SQL and partial extraction as complete dependency knowledge.
            List<ArchonMcpUnknown> unknowns = context.Unknowns
                .Select(unknown => new ArchonMcpUnknown(unknown.Field, ArchonMcpDataAccessOperations.GetDataAccessUsage, unknown.Reason, "Query-level unknowns reduce confidence in data-access completeness.", null))
                .ToList();
            unknowns.AddRange(records
                .Where(record => record.HasUnknownData || record.DynamicSqlIndicator)
                .Select(record => new ArchonMcpUnknown(
                    record.DynamicSqlIndicator ? "dynamicSql" : record.StableKey,
                    record.StableKey,
                    record.UnknownReason ?? "Data-access extraction could not prove every operation, target, or SQL composition detail.",
                    "Fact-level unknowns reduce confidence for this specific data-access record.",
                    null)));

            return unknowns
                .GroupBy(unknown => unknown.Kind + "|" + unknown.AffectedStableKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(unknown => unknown.Kind, StringComparer.Ordinal)
                .ThenBy(unknown => unknown.AffectedStableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates safe warnings from query context, truncation metadata, and known-empty results.
        /// </summary>
        /// <param name="context">The fact query context containing query-level warnings.</param>
        /// <param name="limits">The applied MCP limit metadata.</param>
        /// <param name="recordCount">The number of bounded records returned.</param>
        /// <returns>Safe MCP warning records.</returns>
        private static IReadOnlyList<ArchonMcpWarning> CreateWarnings(FactQueryContext context, ArchonMcpLimitMetadata limits, int recordCount)
        {
            // Warnings explain partial or bounded data without exposing internal extraction diagnostics.
            List<ArchonMcpWarning> warnings = context.Warnings
                .Select(warning => new ArchonMcpWarning(warning.Code, warning.Message, affectedStableKey: null))
                .ToList();
            if (limits.Truncated)
            {
                warnings.Add(new ArchonMcpWarning("mcp.archon.get_data_access_usage.truncated", "Data-access usage output was truncated by MCP response limits.", affectedStableKey: null));
            }

            if (recordCount == 0)
            {
                warnings.Add(new ArchonMcpWarning("mcp.archon.get_data_access_usage.no_matches", "No persisted data-access facts matched the requested filters.", affectedStableKey: null));
            }

            return warnings
                .GroupBy(warning => warning.Code, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(warning => warning.Code, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates safe suggested follow-up operations for continued data-access investigation.
        /// </summary>
        /// <param name="facts">The returned data-access usage facts.</param>
        /// <param name="limitFollowUps">The follow-ups emitted by shared limit handling.</param>
        /// <returns>Safe suggested follow-up records.</returns>
        private static IReadOnlyList<ArchonMcpSuggestedFollowUp> CreateFollowUps(ArchonMcpDataAccessUsageFacts facts, IEnumerable<ArchonMcpSuggestedFollowUp> limitFollowUps)
        {
            // Follow-ups stay inside read-only Archon investigation workflows and avoid SQL execution or remediation instructions.
            List<ArchonMcpSuggestedFollowUp> followUps = [.. limitFollowUps];
            if (!string.IsNullOrWhiteSpace(facts.ProjectStableKey))
            {
                followUps.Add(new ArchonMcpSuggestedFollowUp("Describe the owning project before drawing data-access responsibility conclusions.", "archon.describe_project", new Dictionary<string, string> { ["projectStableKey"] = facts.ProjectStableKey }));
            }

            if (facts.Usages.Count > 0)
            {
                followUps.Add(new ArchonMcpSuggestedFollowUp("Assess change impact for a selected data-access fact before planning a migration.", "archon.assess_change_impact", new Dictionary<string, string> { ["targetStableKey"] = facts.Usages[0].StableKey }));
            }

            return followUps;
        }

        /// <summary>
        /// Creates the concise natural-language summary for the data-access envelope.
        /// </summary>
        /// <param name="facts">The returned data-access facts.</param>
        /// <returns>A grounded summary string.</returns>
        private static string CreateSummary(ArchonMcpDataAccessUsageFacts facts)
        {
            // The summary reports only counts and requested filters from returned facts so it does not invent data-access risk or remediation.
            string filterText = string.IsNullOrWhiteSpace(facts.Family) ? "all configured data-access families" : facts.Family;
            return facts.Usages.Count == 0
                ? $"No persisted data-access usage facts matched {filterText} within the requested scope."
                : $"Returned {facts.Usages.Count} of {facts.TotalMatches} persisted data-access usage facts for {filterText}.";
        }

        /// <summary>
        /// Creates response confidence from bounded data-access records and unknowns.
        /// </summary>
        /// <param name="records">The bounded data-access usage records.</param>
        /// <param name="unknowns">The explicit unknowns returned with the response.</param>
        /// <returns>The overall MCP confidence classification.</returns>
        private static ArchonMcpConfidence CreateConfidence(IReadOnlyList<ArchonMcpDataAccessUsageRecord> records, IReadOnlyList<ArchonMcpUnknown> unknowns)
        {
            // Dynamic SQL and unknown targets reduce confidence because persisted facts cannot prove all database targets.
            if (records.Count == 0)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Unknown, "No matching data-access facts were returned for the requested scope.");
            }

            decimal average = records.Average(record => record.Confidence);
            ArchonMcpConfidenceLevel level = unknowns.Count > 0 && average < 0.8m ? ArchonMcpConfidenceLevel.Medium : ToConfidenceLevel(average);
            return new ArchonMcpConfidence(level, "Confidence is derived from persisted data-access fact confidence and explicit unknowns such as dynamic SQL.");
        }

        /// <summary>
        /// Converts a numeric confidence value into an MCP confidence level.
        /// </summary>
        /// <param name="confidence">The normalized confidence value.</param>
        /// <returns>The matching MCP confidence level.</returns>
        private static ArchonMcpConfidenceLevel ToConfidenceLevel(decimal confidence)
        {
            // Thresholds match earlier MCP tool mappers so response confidence is consistent across tool families.
            return confidence >= 0.8m
                ? ArchonMcpConfidenceLevel.High
                : confidence >= 0.5m
                    ? ArchonMcpConfidenceLevel.Medium
                    : confidence > 0m
                        ? ArchonMcpConfidenceLevel.Low
                        : ArchonMcpConfidenceLevel.Unknown;
        }

        /// <summary>
        /// Creates the common MCP snapshot identity from fact query context.
        /// </summary>
        /// <param name="context">The fact query context containing resolved snapshot metadata.</param>
        /// <returns>The MCP snapshot identity.</returns>
        private static ArchonMcpSnapshotIdentity CreateSnapshotIdentity(FactQueryContext context)
        {
            // Snapshot identity tells clients which persisted architecture snapshot supports the returned data-access facts.
            return new ArchonMcpSnapshotIdentity(
                context.Snapshot.SnapshotStableKey,
                context.Snapshot.Selector,
                context.Snapshot.ResolvedAsLatest ? "Resolved as the latest available architecture snapshot." : "Resolved as the requested architecture snapshot.");
        }

        /// <summary>
        /// Determines whether any validation error has one of the supplied stable validation codes.
        /// </summary>
        /// <param name="errors">The query-layer validation errors to inspect.</param>
        /// <param name="codes">The stable validation codes to match.</param>
        /// <returns><see langword="true" /> when any supplied code is present; otherwise, <see langword="false" />.</returns>
        private static bool HasAnyCode(IEnumerable<FactQueryValidationError> errors, params string[] codes)
        {
            // Broad category mapping uses stable validation codes and avoids leaking raw query details.
            HashSet<string> expectedCodes = new(codes, StringComparer.Ordinal);
            return errors.Any(error => expectedCodes.Contains(error.Code));
        }

        /// <summary>
        /// Creates a validation error response for invalid MCP data-access usage input.
        /// </summary>
        /// <param name="validationResult">The validation result containing all request failures.</param>
        /// <returns>A structured MCP validation error response.</returns>
        private static ArchonMcpErrorResponse CreateValidationError(ArchonMcpValidationResult validationResult)
        {
            // Public validation output gives clients corrective guidance without invoking query dependencies.
            string details = string.Join(" ", validationResult.Failures.Select(failure => $"{failure.Field}: {failure.Message}"));
            return ArchonMcpErrorResponse.Create(
                ArchonMcpDataAccessOperations.GetDataAccessUsage,
                ArchonMcpErrorCategory.Validation,
                details,
                [new ArchonMcpSuggestedFollowUp("Correct data-access filters, stable keys, snapshot selector, and limits before retrying.", "user.question", null)]);
        }

        /// <summary>
        /// Creates safe audit parameters for a data-access usage request.
        /// </summary>
        /// <param name="request">The caller-supplied request.</param>
        /// <returns>Safe normalized audit parameters.</returns>
        private static IReadOnlyDictionary<string, string> CreateAuditParameters(ArchonMcpDataAccessUsageRequest request)
        {
            // Audit parameters include only stable keys, filter names, and numeric bounds, never SQL text, connection strings, or evidence snippets.
            Dictionary<string, string> parameters = new(StringComparer.Ordinal)
            {
                [nameof(request.SnapshotSelector)] = request.SnapshotSelector ?? "latest"
            };
            AddIfPresent(parameters, nameof(request.ProjectStableKey), request.ProjectStableKey);
            AddIfPresent(parameters, nameof(request.DataContextStableKey), request.DataContextStableKey);
            AddIfPresent(parameters, nameof(request.Entity), request.Entity);
            AddIfPresent(parameters, nameof(request.Table), request.Table);
            AddIfPresent(parameters, nameof(request.StoredProcedure), request.StoredProcedure);
            AddIfPresent(parameters, nameof(request.Family), request.Family);
            AddIfPresent(parameters, nameof(request.RepositoryStableKey), request.RepositoryStableKey);
            AddIfPresent(parameters, nameof(request.SolutionStableKey), request.SolutionStableKey);
            if (request.Limit is not null)
            {
                parameters[nameof(request.Limit)] = request.Limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return parameters;
        }

        /// <summary>
        /// Adds a trimmed audit parameter when the value is meaningful.
        /// </summary>
        /// <param name="parameters">The audit parameter dictionary being built.</param>
        /// <param name="name">The parameter name.</param>
        /// <param name="value">The optional parameter value.</param>
        private static void AddIfPresent(Dictionary<string, string> parameters, string name, string? value)
        {
            // Blank values are omitted so audit records remain concise and deterministic.
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters[name] = value.Trim();
            }
        }
    }
}
