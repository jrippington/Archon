using System.Text.Json;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Evidence
{
    /// <summary>
    /// Implements controlled WP014 evidence detail and related-evidence queries over extracted architecture snapshots.
    /// </summary>
    public sealed class EvidenceQueryService : IEvidenceQueryService
    {
        /// <summary>
        /// Reads snapshots from the registered architecture snapshot writer when in-memory diagnostics are available.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Defines metadata keys whose values should never be echoed through evidence API metadata.
        /// </summary>
        private static readonly string[] s_secretMetadataNames =
        [
            "password",
            "secret",
            "token",
            "apikey",
            "apiKey",
            "connectionString",
            "credential"
        ];

        /// <summary>
        /// Initializes a new instance of the <see cref="EvidenceQueryService"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        public EvidenceQueryService(IArchitectureSnapshotWriter snapshotWriter)
        {
            // Evidence queries use the same snapshot seam as earlier WP014 slices so tests and local hosts do not require Neo4j.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
        }

        /// <inheritdoc />
        public Task<EvidenceDetailResult> GetEvidenceAsync(EvidenceDetailQuery query, CancellationToken cancellationToken)
        {
            // Detail lookup validates identity and scope, resolves one snapshot, then maps a bounded safe evidence response.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<EvidenceQueryValidationError> identityErrors = ValidateEvidenceIdentity(query.EvidenceStableKey);
            if (identityErrors.Count > 0)
            {
                return Task.FromResult(new EvidenceDetailResult(identityErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new EvidenceDetailResult(resolution.ValidationErrors));
            }

            ExtractedArchitectureSnapshot snapshot = resolution.Snapshot!;
            EvidenceRecord? evidence = snapshot.Evidence.FirstOrDefault(record => StringComparer.Ordinal.Equals(record.StableKey.Value, query.EvidenceStableKey!.Trim()));
            if (evidence is null)
            {
                EvidenceQueryValidationError error = new(EvidenceQueryValidationCodes.EvidenceNotFound, "The requested evidence stable key was not found in the selected snapshot scope.");
                return Task.FromResult(new EvidenceDetailResult([error]));
            }

            EvidenceQueryContext context = BuildContext(query.Selector, resolution, snapshot.Evidence.Count == 0 ? new EvidenceUnknownDto("evidence", "No persisted evidence records were available in the selected snapshot.") : null);
            EvidenceDetailDto detail = BuildEvidenceDetail(snapshot, evidence);
            return Task.FromResult(new EvidenceDetailResult(detail, context));
        }

        /// <inheritdoc />
        public Task<RelatedEvidenceResult> ListRelatedEvidenceAsync(RelatedEvidenceQuery query, CancellationToken cancellationToken)
        {
            // Related lookup validates identity, bounded paging, and scope before following only explicit evidence relationships.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<EvidenceQueryValidationError> validationErrors = ValidateRelatedQuery(query);
            if (validationErrors.Count > 0)
            {
                return Task.FromResult(new RelatedEvidenceResult(validationErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new RelatedEvidenceResult(resolution.ValidationErrors));
            }

            ExtractedArchitectureSnapshot snapshot = resolution.Snapshot!;
            EvidenceRecord[] relatedEvidence = ResolveRelatedEvidence(snapshot, query.RelatedStableKey!.Trim(), query.RelatedKind)
                .OrderBy(static evidence => evidence.StableKey.Value, StringComparer.Ordinal)
                .ToArray();
            if (relatedEvidence.Length == 0)
            {
                EvidenceQueryValidationError error = new(EvidenceQueryValidationCodes.RelatedEvidenceNotFound, "No evidence records were related to the requested stable key in the selected snapshot scope.");
                return Task.FromResult(new RelatedEvidenceResult([error]));
            }

            EvidenceQueryContext baseContext = BuildContext(query.Selector, resolution, snapshot.Evidence.Count == 0 ? new EvidenceUnknownDto("evidence", "No persisted evidence records were available in the selected snapshot.") : null);
            EvidenceQueryContext context = relatedEvidence.Length > query.Take
                ? MergeWarning(baseContext, new EvidenceWarningDto("RelatedEvidenceTruncated", "The related-evidence result was bounded by the requested take value."))
                : baseContext;
            EvidenceDetailDto[] pageItems = relatedEvidence.Skip(query.Skip).Take(query.Take).Select(evidence => BuildEvidenceDetail(snapshot, evidence)).ToArray();
            PagedQueryResult<EvidenceDetailDto> page = new(pageItems, relatedEvidence.Length, query.Skip, query.Take);
            return Task.FromResult(new RelatedEvidenceResult(page, context));
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
        /// Resolves and validates the selected evidence snapshot scope.
        /// </summary>
        /// <param name="selector">The repository, solution, and snapshot selector supplied by the query.</param>
        /// <returns>A successful snapshot resolution or deterministic validation errors.</returns>
        private SnapshotResolution ResolveSnapshot(EvidenceSnapshotSelector selector)
        {
            // Scope validation runs before graph matching so missing or malformed selectors produce client-correctable problem details.
            List<EvidenceQueryValidationError> validationErrors = ValidateSelector(selector);
            if (validationErrors.Count > 0)
            {
                return SnapshotResolution.Failed(validationErrors);
            }

            ExtractedArchitectureSnapshot[] repositorySnapshots = GetSnapshots()
                .Where(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.RepositoryStableKey.Value, selector.RepositoryStableKey))
                .ToArray();
            if (repositorySnapshots.Length == 0)
            {
                EvidenceQueryValidationError error = new(EvidenceQueryValidationCodes.RepositoryNotFound, "The requested repository scope was not found.");
                return SnapshotResolution.Failed([error]);
            }

            ExtractedArchitectureSnapshot[] scopedSnapshots = ApplySolutionScope(repositorySnapshots, selector);
            if (selector.SolutionStableKey is not null && scopedSnapshots.Length == 0)
            {
                EvidenceQueryValidationError error = new(EvidenceQueryValidationCodes.SolutionNotFound, "The requested solution scope was not found for the repository.");
                return SnapshotResolution.Failed([error]);
            }

            ExtractedArchitectureSnapshot? selectedSnapshot = ResolveSelectedSnapshot(scopedSnapshots, selector);
            if (selectedSnapshot?.SnapshotHeader is null)
            {
                EvidenceQueryValidationError error = new(EvidenceQueryValidationCodes.SnapshotNotFound, "The requested snapshot scope was not found.");
                return SnapshotResolution.Failed([error]);
            }

            return SnapshotResolution.Success(selectedSnapshot, scopedSnapshots);
        }

        /// <summary>
        /// Validates selector syntax before any snapshot matching occurs.
        /// </summary>
        /// <param name="selector">The caller-supplied evidence snapshot selector.</param>
        /// <returns>A deterministic list of selector validation errors.</returns>
        private static List<EvidenceQueryValidationError> ValidateSelector(EvidenceSnapshotSelector selector)
        {
            // Repository scope is required because latest resolution must be bounded to one repository.
            List<EvidenceQueryValidationError> errors = [];
            if (selector.RepositoryStableKey is null)
            {
                errors.Add(new EvidenceQueryValidationError(EvidenceQueryValidationCodes.RepositoryStableKeyRequired, "A repository stable key is required for evidence queries."));
            }

            if (!selector.RequestsLatestSnapshot && !selector.SnapshotStableKey.StartsWith("snapshot://", StringComparison.Ordinal))
            {
                errors.Add(new EvidenceQueryValidationError(EvidenceQueryValidationCodes.SnapshotSelectorInvalid, "Snapshot selector must be 'latest', 'current', or a snapshot:// stable key."));
            }

            return errors;
        }

        /// <summary>
        /// Validates the requested evidence stable key before snapshot work starts.
        /// </summary>
        /// <param name="evidenceStableKey">The evidence stable key supplied by the caller.</param>
        /// <returns>A deterministic list of identity validation errors.</returns>
        private static List<EvidenceQueryValidationError> ValidateEvidenceIdentity(string? evidenceStableKey)
        {
            // Evidence detail lookup must be explicit so callers cannot accidentally retrieve unrelated evidence.
            return string.IsNullOrWhiteSpace(evidenceStableKey)
                ? [new EvidenceQueryValidationError(EvidenceQueryValidationCodes.EvidenceStableKeyRequired, "An evidence stable key is required for evidence detail.")]
                : [];
        }

        /// <summary>
        /// Validates related-evidence identity and paging options before snapshot work starts.
        /// </summary>
        /// <param name="query">The caller-supplied related-evidence query.</param>
        /// <returns>A deterministic list of validation errors.</returns>
        private static List<EvidenceQueryValidationError> ValidateRelatedQuery(RelatedEvidenceQuery query)
        {
            // Related evidence queries are bounded because a broad node or rule can connect to many evidence records.
            List<EvidenceQueryValidationError> errors = [];
            if (string.IsNullOrWhiteSpace(query.RelatedStableKey))
            {
                errors.Add(new EvidenceQueryValidationError(EvidenceQueryValidationCodes.RelatedStableKeyRequired, "A related stable key is required for related-evidence lookup."));
            }

            if (query.Skip < 0)
            {
                errors.Add(new EvidenceQueryValidationError(EvidenceQueryValidationCodes.SkipInvalid, "Related-evidence skip must be greater than or equal to zero."));
            }

            if (query.Take < 1 || query.Take > EvidenceQueryLimits.MaximumTake)
            {
                errors.Add(new EvidenceQueryValidationError(EvidenceQueryValidationCodes.TakeInvalid, $"Related-evidence take must be between 1 and {EvidenceQueryLimits.MaximumTake}."));
            }

            return errors;
        }

        /// <summary>
        /// Applies the optional solution scope to repository snapshots.
        /// </summary>
        /// <param name="repositorySnapshots">The snapshots already matched to the requested repository.</param>
        /// <param name="selector">The caller-supplied evidence snapshot selector.</param>
        /// <returns>The snapshots matching the optional solution scope.</returns>
        private static ExtractedArchitectureSnapshot[] ApplySolutionScope(IEnumerable<ExtractedArchitectureSnapshot> repositorySnapshots, EvidenceSnapshotSelector selector)
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
        /// <param name="selector">The caller-supplied evidence snapshot selector.</param>
        /// <returns>The selected snapshot, or null when none matches.</returns>
        private static ExtractedArchitectureSnapshot? ResolveSelectedSnapshot(IEnumerable<ExtractedArchitectureSnapshot> scopedSnapshots, EvidenceSnapshotSelector selector)
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
        /// Builds the evidence query context shared by API envelopes.
        /// </summary>
        /// <param name="selector">The caller-supplied evidence snapshot selector.</param>
        /// <param name="resolution">The successful snapshot resolution.</param>
        /// <param name="additionalUnknown">The optional evidence-specific unknown to append.</param>
        /// <returns>The evidence query context for response mapping.</returns>
        private static EvidenceQueryContext BuildContext(EvidenceSnapshotSelector selector, SnapshotResolution resolution, EvidenceUnknownDto? additionalUnknown)
        {
            // Context construction centralizes envelope metadata so detail and related-evidence endpoints report scope consistently.
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
            EvidenceWarningDto[] warnings = snapshot.Warnings.Select(static warning => new EvidenceWarningDto("SnapshotWarning", warning)).ToArray();
            List<EvidenceUnknownDto> unknowns = [];
            if (snapshot.Errors.Any())
            {
                unknowns.Add(new EvidenceUnknownDto("evidenceExtraction", "The selected snapshot contains extraction errors, so evidence query data may be incomplete."));
            }

            if (additionalUnknown is not null)
            {
                unknowns.Add(additionalUnknown);
            }

            return new EvidenceQueryContext(scope, snapshotMetadata, warnings, unknowns);
        }

        /// <summary>
        /// Appends one warning to a context when it is not already present.
        /// </summary>
        /// <param name="context">The base evidence query context.</param>
        /// <param name="warning">The warning value to append.</param>
        /// <returns>A context containing the additional warning.</returns>
        private static EvidenceQueryContext MergeWarning(EvidenceQueryContext context, EvidenceWarningDto warning)
        {
            // Warning aggregation de-duplicates by code and message so clients receive compact metadata.
            EvidenceWarningDto[] warnings = context.Warnings.Append(warning).DistinctBy(static value => value.Code + "\u001f" + value.Message).ToArray();
            return new EvidenceQueryContext(context.Scope, context.Snapshot, warnings, context.Unknowns);
        }

        /// <summary>
        /// Maps one domain evidence record into the public evidence detail DTO.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="evidence">The domain evidence record being mapped.</param>
        /// <returns>A safe evidence detail DTO.</returns>
        private static EvidenceDetailDto BuildEvidenceDetail(ExtractedArchitectureSnapshot snapshot, EvidenceRecord evidence)
        {
            // Evidence detail deliberately exposes only bounded source context and stable related identities, never persistence-local IDs.
            EvidenceRelatedRecordDto[] relatedRecords = BuildRelatedRecords(snapshot, evidence.StableKey.Value).ToArray();
            EvidenceRelatedRecordDto[] findingContext = relatedRecords.Where(static record => string.Equals(record.Kind, "Finding", StringComparison.Ordinal)).ToArray();
            EvidenceRelatedRecordDto[] ruleContext = BuildRuleContext(snapshot, findingContext).ToArray();
            return new EvidenceDetailDto(
                evidence.StableKey.Value,
                evidence.EvidenceKind.Value,
                evidence.FilePath.Value,
                evidence.StartLine,
                evidence.EndLine,
                evidence.SymbolName,
                evidence.ContainingSymbol,
                BuildSnippetPreview(evidence),
                findingContext,
                ruleContext,
                relatedRecords,
                evidence.SnapshotStableKey.Value,
                evidence.Confidence.Value,
                evidence.KnowledgeKind.Value,
                new EvidenceUnknownReasonDto(evidence.UnknownState.HasUnknownData, evidence.UnknownState.UnknownReason),
                SanitizeMetadata(evidence.Metadata));
        }

        /// <summary>
        /// Builds all graph records that point directly at one evidence stable key.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="evidenceStableKey">The evidence stable key whose related records should be found.</param>
        /// <returns>The related records using stable public identities only.</returns>
        private static IEnumerable<EvidenceRelatedRecordDto> BuildRelatedRecords(ExtractedArchitectureSnapshot snapshot, string evidenceStableKey)
        {
            // Each record family has an explicit evidence relationship field, so lookup stays bounded and avoids arbitrary graph traversal.
            foreach (ArchitectureNode node in snapshot.Nodes.Where(node => StringComparer.Ordinal.Equals(node.PrimaryEvidenceStableKey?.Value, evidenceStableKey)))
            {
                yield return new EvidenceRelatedRecordDto(node.StableKey.Value, "Node", node.DisplayName, "PrimaryEvidence");
            }

            foreach (ArchitectureEdge edge in snapshot.Edges.Where(edge => StringComparer.Ordinal.Equals(edge.PrimaryEvidenceStableKey?.Value, evidenceStableKey)))
            {
                yield return new EvidenceRelatedRecordDto(edge.StableKey.Value, "Edge", edge.EdgeKind.Value, "PrimaryEvidence");
            }

            foreach (MetricRecord metric in snapshot.Metrics.Where(metric => StringComparer.Ordinal.Equals(metric.PrimaryEvidenceStableKey?.Value, evidenceStableKey)))
            {
                yield return new EvidenceRelatedRecordDto(metric.StableKey.Value, "Metric", metric.Name, "PrimaryEvidence");
            }

            foreach (FindingRecord finding in snapshot.Findings.Where(finding => finding.EvidenceStableKeys.Any(key => StringComparer.Ordinal.Equals(key.Value, evidenceStableKey))))
            {
                string relationship = StringComparer.Ordinal.Equals(finding.PrimaryEvidenceStableKey?.Value, evidenceStableKey) ? "PrimaryEvidence" : "SupportingEvidence";
                yield return new EvidenceRelatedRecordDto(finding.StableKey.Value, "Finding", finding.Title, relationship);
            }
        }

        /// <summary>
        /// Builds rule context records from the finding records connected to an evidence record.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="findingContext">The finding context records already connected to evidence.</param>
        /// <returns>The rule context records that explain finding/rule provenance.</returns>
        private static IEnumerable<EvidenceRelatedRecordDto> BuildRuleContext(ExtractedArchitectureSnapshot snapshot, IEnumerable<EvidenceRelatedRecordDto> findingContext)
        {
            // Rule context is derived through findings so callers can understand why an evidence-backed claim became a rule finding.
            HashSet<string> findingKeys = new(findingContext.Select(static finding => finding.StableKey), StringComparer.Ordinal);
            foreach (FindingRecord finding in snapshot.Findings.Where(finding => findingKeys.Contains(finding.StableKey.Value)))
            {
                RuleDefinition? rule = snapshot.Rules.FirstOrDefault(rule => string.Equals(rule.RuleCode, finding.RuleCode, StringComparison.OrdinalIgnoreCase) && string.Equals(rule.Version, finding.RuleVersion, StringComparison.OrdinalIgnoreCase));
                string ruleStableKey = $"rule://{finding.RuleCode}/{finding.RuleVersion}";
                yield return new EvidenceRelatedRecordDto(ruleStableKey, "Rule", rule?.Name ?? finding.RuleCode, "FindingRule");
            }
        }

        /// <summary>
        /// Resolves evidence records related to a supplied graph record stable key.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="relatedStableKey">The related node, edge, finding, metric, or rule stable key.</param>
        /// <param name="relatedKind">The optional related-record kind hint supplied by the caller.</param>
        /// <returns>The evidence records that are explicitly connected to the related record.</returns>
        private static IEnumerable<EvidenceRecord> ResolveRelatedEvidence(ExtractedArchitectureSnapshot snapshot, string relatedStableKey, string? relatedKind)
        {
            // Kind hints can narrow work, but the service still uses stable-key matching so callers are not forced to know the internal record family.
            HashSet<string> evidenceKeys = new(StringComparer.Ordinal);
            string? normalizedKind = string.IsNullOrWhiteSpace(relatedKind) ? null : relatedKind.Trim();
            if (KindMatches(normalizedKind, "Node"))
            {
                ArchitectureNode? node = snapshot.Nodes.FirstOrDefault(node => StringComparer.Ordinal.Equals(node.StableKey.Value, relatedStableKey));
                AddEvidenceKey(evidenceKeys, node?.PrimaryEvidenceStableKey?.Value);
            }

            if (KindMatches(normalizedKind, "Edge"))
            {
                ArchitectureEdge? edge = snapshot.Edges.FirstOrDefault(edge => StringComparer.Ordinal.Equals(edge.StableKey.Value, relatedStableKey));
                AddEvidenceKey(evidenceKeys, edge?.PrimaryEvidenceStableKey?.Value);
            }

            if (KindMatches(normalizedKind, "Finding"))
            {
                FindingRecord? finding = snapshot.Findings.FirstOrDefault(finding => StringComparer.Ordinal.Equals(finding.StableKey.Value, relatedStableKey));
                if (finding is not null)
                {
                    foreach (string evidenceStableKey in finding.EvidenceStableKeys.Select(static key => key.Value))
                    {
                        AddEvidenceKey(evidenceKeys, evidenceStableKey);
                    }
                }
            }

            if (KindMatches(normalizedKind, "Metric"))
            {
                MetricRecord? metric = snapshot.Metrics.FirstOrDefault(metric => StringComparer.Ordinal.Equals(metric.StableKey.Value, relatedStableKey));
                AddEvidenceKey(evidenceKeys, metric?.PrimaryEvidenceStableKey?.Value);
            }

            if (KindMatches(normalizedKind, "Rule"))
            {
                foreach (FindingRecord finding in snapshot.Findings.Where(finding => RuleStableKeyMatches(relatedStableKey, finding)))
                {
                    foreach (string evidenceStableKey in finding.EvidenceStableKeys.Select(static key => key.Value))
                    {
                        AddEvidenceKey(evidenceKeys, evidenceStableKey);
                    }
                }
            }

            return snapshot.Evidence.Where(evidence => evidenceKeys.Contains(evidence.StableKey.Value));
        }

        /// <summary>
        /// Determines whether a caller kind hint permits looking in the supplied record family.
        /// </summary>
        /// <param name="relatedKind">The optional caller-supplied kind hint.</param>
        /// <param name="candidateKind">The candidate record family being considered.</param>
        /// <returns><see langword="true"/> when no hint was supplied or the hint matches the candidate family.</returns>
        private static bool KindMatches(string? relatedKind, string candidateKind)
        {
            // Empty hints intentionally search all supported families while exact hints keep large snapshots cheaper.
            return relatedKind is null || string.Equals(relatedKind, candidateKind, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Adds one evidence stable key to a set when the key is available.
        /// </summary>
        /// <param name="evidenceKeys">The evidence key set being accumulated.</param>
        /// <param name="evidenceStableKey">The optional evidence stable key to add.</param>
        private static void AddEvidenceKey(HashSet<string> evidenceKeys, string? evidenceStableKey)
        {
            // Empty evidence links are ignored because absence should not create a false relationship.
            if (!string.IsNullOrWhiteSpace(evidenceStableKey))
            {
                evidenceKeys.Add(evidenceStableKey.Trim());
            }
        }

        /// <summary>
        /// Determines whether a rule stable key maps to a finding's rule identity.
        /// </summary>
        /// <param name="relatedStableKey">The caller-supplied related rule stable key or rule code.</param>
        /// <param name="finding">The finding whose rule identity is compared.</param>
        /// <returns><see langword="true"/> when the rule stable key or rule code matches the finding.</returns>
        private static bool RuleStableKeyMatches(string relatedStableKey, FindingRecord finding)
        {
            // Rule callers may use the synthetic public rule stable key or the rule code exposed by existing rule endpoints.
            string expectedStableKey = $"rule://{finding.RuleCode}/{finding.RuleVersion}";
            return string.Equals(relatedStableKey, expectedStableKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(relatedStableKey, finding.RuleCode, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds a bounded, secret-safe snippet preview from persisted evidence snippet data.
        /// </summary>
        /// <param name="evidence">The evidence record containing persisted snippet preview metadata.</param>
        /// <returns>The bounded snippet preview response.</returns>
        private static EvidenceSnippetPreviewDto BuildSnippetPreview(EvidenceRecord evidence)
        {
            // Snippets are treated as untrusted display text; the API never reads source files or expands beyond the persisted preview.
            string? snippet = evidence.SnippetPreview;
            int originalLength = snippet?.Length ?? 0;
            if (string.IsNullOrEmpty(snippet))
            {
                return new EvidenceSnippetPreviewDto(null, evidence.SnippetHash, originalLength, 0, false, false, EvidenceQueryLimits.MaximumSnippetPreviewLength);
            }

            bool redacted = ContainsSecretLikeText(snippet);
            if (redacted)
            {
                const string redactedText = "[redacted secret-like evidence preview]";
                return new EvidenceSnippetPreviewDto(redactedText, evidence.SnippetHash, originalLength, redactedText.Length, originalLength > EvidenceQueryLimits.MaximumSnippetPreviewLength, true, EvidenceQueryLimits.MaximumSnippetPreviewLength);
            }

            bool truncated = originalLength > EvidenceQueryLimits.MaximumSnippetPreviewLength;
            string returnedText = truncated ? snippet[..EvidenceQueryLimits.MaximumSnippetPreviewLength] : snippet;
            return new EvidenceSnippetPreviewDto(returnedText, evidence.SnippetHash, originalLength, returnedText.Length, truncated, false, EvidenceQueryLimits.MaximumSnippetPreviewLength);
        }

        /// <summary>
        /// Sanitizes metadata before exposing it through the evidence API.
        /// </summary>
        /// <param name="metadata">The graph metadata attached to evidence.</param>
        /// <returns>Metadata with secret-like keys or values removed.</returns>
        private static GraphMetadata SanitizeMetadata(GraphMetadata metadata)
        {
            // Evidence metadata may originate from source analysis, so secret-looking entries are removed before public projection.
            if (metadata.IsEmpty)
            {
                return GraphMetadata.Empty;
            }

            using JsonDocument document = JsonDocument.Parse(metadata.ToCanonicalJson());
            Dictionary<string, object?> sanitizedValues = [];
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (IsSecretLikeName(property.Name) || ContainsSecretLikeText(property.Value.ToString()))
                {
                    continue;
                }

                sanitizedValues[property.Name] = ConvertJsonValue(property.Value);
            }

            return sanitizedValues.Count == 0 ? GraphMetadata.Empty : GraphMetadata.From(sanitizedValues);
        }

        /// <summary>
        /// Converts a JSON metadata value into a JSON-compatible CLR value for metadata reconstruction.
        /// </summary>
        /// <param name="element">The JSON element to convert.</param>
        /// <returns>A JSON-compatible CLR value.</returns>
        private static object? ConvertJsonValue(JsonElement element)
        {
            // The conversion preserves safe scalar and nested metadata values without exposing JsonElement lifetime issues.
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt64(out long integer) => integer,
                JsonValueKind.Number when element.TryGetDecimal(out decimal decimalValue) => decimalValue,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToArray(),
                JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => ConvertJsonValue(property.Value), StringComparer.Ordinal),
                _ => element.ToString()
            };
        }

        /// <summary>
        /// Determines whether text appears to contain secret material.
        /// </summary>
        /// <param name="text">The optional text to inspect.</param>
        /// <returns><see langword="true"/> when the text includes secret-like markers; otherwise, <see langword="false"/>.</returns>
        private static bool ContainsSecretLikeText(string? text)
        {
            // The heuristic intentionally favors redaction for common credential markers rather than risking accidental secret disclosure.
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return s_secretMetadataNames.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Determines whether a metadata property name appears to represent secret material.
        /// </summary>
        /// <param name="name">The metadata property name to inspect.</param>
        /// <returns><see langword="true"/> when the name includes secret-like markers; otherwise, <see langword="false"/>.</returns>
        private static bool IsSecretLikeName(string name)
        {
            // Metadata names are checked separately because values may be benign placeholders under sensitive keys.
            return s_secretMetadataNames.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Represents the result of resolving a snapshot selector.
        /// </summary>
        /// <param name="Snapshot">The selected snapshot when resolution succeeded.</param>
        /// <param name="ValidationErrors">The validation errors that prevented resolution.</param>
        private sealed record SnapshotResolution(ExtractedArchitectureSnapshot? Snapshot, IReadOnlyList<EvidenceQueryValidationError> ValidationErrors)
        {
            /// <summary>
            /// Gets a value indicating whether snapshot resolution succeeded.
            /// </summary>
            public bool Succeeded
            {
                get
                {
                    // Success requires a selected snapshot and no validation errors.
                    return Snapshot is not null && ValidationErrors.Count == 0;
                }
            }

            /// <summary>
            /// Creates a successful snapshot resolution.
            /// </summary>
            /// <param name="snapshot">The selected snapshot.</param>
            /// <param name="scopedSnapshots">The scoped snapshot set retained for signature consistency with other query services.</param>
            /// <returns>A successful snapshot resolution.</returns>
            public static SnapshotResolution Success(ExtractedArchitectureSnapshot snapshot, IReadOnlyList<ExtractedArchitectureSnapshot> scopedSnapshots)
            {
                // The scoped snapshot set is accepted to keep the resolver shape aligned with earlier WP014 query services.
                ArgumentNullException.ThrowIfNull(scopedSnapshots);
                return new SnapshotResolution(snapshot, []);
            }

            /// <summary>
            /// Creates a failed snapshot resolution.
            /// </summary>
            /// <param name="validationErrors">The validation errors that prevented resolution.</param>
            /// <returns>A failed snapshot resolution.</returns>
            public static SnapshotResolution Failed(IReadOnlyList<EvidenceQueryValidationError> validationErrors)
            {
                // Errors are copied by the caller-level result and are safe for API validation-problem responses.
                return new SnapshotResolution(null, validationErrors);
            }
        }
    }
}
