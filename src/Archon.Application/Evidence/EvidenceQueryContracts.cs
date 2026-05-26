using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Domain.Graph.Metadata;

namespace Archon.Application.Evidence
{
    /// <summary>
    /// Defines stable response-size limits for WP014 evidence drill-down queries.
    /// </summary>
    public static class EvidenceQueryLimits
    {
        /// <summary>
        /// Defines the default number of related evidence records returned when callers omit a take value.
        /// </summary>
        public const int DefaultTake = 50;

        /// <summary>
        /// Defines the maximum number of related evidence records a single request can return.
        /// </summary>
        public const int MaximumTake = 200;

        /// <summary>
        /// Defines the maximum number of characters exposed from a persisted snippet preview.
        /// </summary>
        public const int MaximumSnippetPreviewLength = 240;
    }

    /// <summary>
    /// Defines deterministic validation codes for evidence detail and related-evidence queries.
    /// </summary>
    public static class EvidenceQueryValidationCodes
    {
        /// <summary>
        /// Indicates that a repository stable key was not supplied.
        /// </summary>
        public const string RepositoryStableKeyRequired = nameof(RepositoryStableKeyRequired);

        /// <summary>
        /// Indicates that the supplied snapshot selector was neither latest/current nor a snapshot stable key.
        /// </summary>
        public const string SnapshotSelectorInvalid = nameof(SnapshotSelectorInvalid);

        /// <summary>
        /// Indicates that the requested repository scope was not found.
        /// </summary>
        public const string RepositoryNotFound = nameof(RepositoryNotFound);

        /// <summary>
        /// Indicates that the requested solution scope was not found within the repository scope.
        /// </summary>
        public const string SolutionNotFound = nameof(SolutionNotFound);

        /// <summary>
        /// Indicates that the requested snapshot scope was not found.
        /// </summary>
        public const string SnapshotNotFound = nameof(SnapshotNotFound);

        /// <summary>
        /// Indicates that no evidence stable key was supplied for evidence detail lookup.
        /// </summary>
        public const string EvidenceStableKeyRequired = nameof(EvidenceStableKeyRequired);

        /// <summary>
        /// Indicates that no related record stable key was supplied for related-evidence lookup.
        /// </summary>
        public const string RelatedStableKeyRequired = nameof(RelatedStableKeyRequired);

        /// <summary>
        /// Indicates that the requested evidence stable key was not found in the selected scope.
        /// </summary>
        public const string EvidenceNotFound = nameof(EvidenceNotFound);

        /// <summary>
        /// Indicates that the requested related record stable key did not resolve to evidence in the selected scope.
        /// </summary>
        public const string RelatedEvidenceNotFound = nameof(RelatedEvidenceNotFound);

        /// <summary>
        /// Indicates that the supplied skip value is outside supported bounds.
        /// </summary>
        public const string SkipInvalid = nameof(SkipInvalid);

        /// <summary>
        /// Indicates that the supplied take value is outside supported bounds.
        /// </summary>
        public const string TakeInvalid = nameof(TakeInvalid);
    }

    /// <summary>
    /// Represents one deterministic validation problem produced by an evidence query.
    /// </summary>
    /// <param name="Code">The stable machine-readable validation code.</param>
    /// <param name="Message">The safe developer-facing validation message.</param>
    public sealed record EvidenceQueryValidationError(string Code, string Message);

    /// <summary>
    /// Represents one safe warning emitted by evidence queries when response data is partial or bounded.
    /// </summary>
    /// <param name="Code">The stable machine-readable warning code.</param>
    /// <param name="Message">The safe developer-facing warning message.</param>
    public sealed record EvidenceWarningDto(string Code, string Message);

    /// <summary>
    /// Represents one explicit unknown field emitted by evidence queries when extraction could not prove completeness.
    /// </summary>
    /// <param name="Field">The response field or evidence section whose value is unknown.</param>
    /// <param name="Reason">The safe reason that explains why the value is unknown.</param>
    public sealed record EvidenceUnknownDto(string Field, string Reason);

    /// <summary>
    /// Describes repository, solution, and snapshot selection for WP014 evidence queries.
    /// </summary>
    public sealed class EvidenceSnapshotSelector
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EvidenceSnapshotSelector"/> class.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable key that bounds latest/current snapshot resolution.</param>
        /// <param name="solutionStableKey">The optional solution stable key that narrows repository scope.</param>
        /// <param name="snapshotStableKey">The exact snapshot stable key or latest/current selector supplied by the caller.</param>
        public EvidenceSnapshotSelector(string? repositoryStableKey, string? solutionStableKey, string? snapshotStableKey)
        {
            // Evidence queries follow the shared WP014 selector model so latest resolution remains repository-bounded and deterministic.
            RepositoryStableKey = NormalizeOptional(repositoryStableKey);
            SolutionStableKey = NormalizeOptional(solutionStableKey);
            SnapshotStableKey = string.IsNullOrWhiteSpace(snapshotStableKey) ? "latest" : snapshotStableKey.Trim();
        }

        /// <summary>
        /// Gets the repository stable key that bounds latest/current snapshot resolution.
        /// </summary>
        public string? RepositoryStableKey { get; }

        /// <summary>
        /// Gets the optional solution stable key that narrows repository scope.
        /// </summary>
        public string? SolutionStableKey { get; }

        /// <summary>
        /// Gets the exact snapshot stable key or latest/current selector supplied by the caller.
        /// </summary>
        public string SnapshotStableKey { get; }

        /// <summary>
        /// Gets a value indicating whether the selector requests deterministic latest/current snapshot resolution.
        /// </summary>
        public bool RequestsLatestSnapshot
        {
            get
            {
                // Latest and current are accepted aliases to match existing WP014 query endpoint behavior.
                return string.Equals(SnapshotStableKey, "latest", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(SnapshotStableKey, "current", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Normalizes optional selector text into trimmed values or null.
        /// </summary>
        /// <param name="value">The optional selector value supplied by the caller.</param>
        /// <returns>The trimmed value, or <see langword="null"/> when no meaningful value was supplied.</returns>
        private static string? NormalizeOptional(string? value)
        {
            // Blank selector fields should behave like omitted fields rather than introducing invisible whitespace identities.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    /// <summary>
    /// Represents an evidence detail lookup request.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the lookup.</param>
    /// <param name="EvidenceStableKey">The stable evidence identity to resolve.</param>
    public sealed record EvidenceDetailQuery(EvidenceSnapshotSelector Selector, string? EvidenceStableKey);

    /// <summary>
    /// Represents a related-evidence lookup request for a node, edge, finding, metric, or rule result stable identity.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the lookup.</param>
    /// <param name="RelatedStableKey">The related stable identity whose evidence should be returned.</param>
    /// <param name="RelatedKind">The optional related-record kind hint supplied by the caller.</param>
    /// <param name="Skip">The number of sorted related evidence records to skip.</param>
    /// <param name="Take">The maximum number of sorted related evidence records to return.</param>
    public sealed record RelatedEvidenceQuery(EvidenceSnapshotSelector Selector, string? RelatedStableKey, string? RelatedKind, int Skip, int Take);

    /// <summary>
    /// Represents the bounded source snippet preview exposed by evidence detail responses.
    /// </summary>
    /// <param name="Text">The bounded, redacted snippet preview treated as untrusted display text.</param>
    /// <param name="Hash">The persisted snippet hash when available for correlation without expanding source text.</param>
    /// <param name="OriginalLength">The original persisted preview length before API bounding or redaction.</param>
    /// <param name="ReturnedLength">The number of characters returned by the API after bounding and redaction.</param>
    /// <param name="Truncated">A value indicating whether the persisted preview was longer than the API limit.</param>
    /// <param name="Redacted">A value indicating whether secret-like preview content was withheld.</param>
    /// <param name="Limit">The maximum number of preview characters the API may expose.</param>
    public sealed record EvidenceSnippetPreviewDto(string? Text, string? Hash, int OriginalLength, int ReturnedLength, bool Truncated, bool Redacted, int Limit);

    /// <summary>
    /// Represents the explicit unknown reason attached to evidence or to its selected snapshot.
    /// </summary>
    /// <param name="HasUnknownData">A value indicating whether explicit unknown data exists.</param>
    /// <param name="Reason">The safe unknown reason when unknown data exists.</param>
    public sealed record EvidenceUnknownReasonDto(bool HasUnknownData, string? Reason);

    /// <summary>
    /// Represents the graph record that connects a stable claim to an evidence record.
    /// </summary>
    /// <param name="StableKey">The stable identity of the related record.</param>
    /// <param name="Kind">The related record kind, such as Node, Edge, Finding, Metric, or Rule.</param>
    /// <param name="DisplayName">The safe display name or title for the related record when available.</param>
    /// <param name="Relationship">The evidence relationship name used by the selected record.</param>
    public sealed record EvidenceRelatedRecordDto(string StableKey, string Kind, string? DisplayName, string Relationship);

    /// <summary>
    /// Represents one complete evidence detail response without exposing persistence-local identifiers.
    /// </summary>
    /// <param name="StableKey">The stable evidence identity.</param>
    /// <param name="EvidenceKind">The controlled evidence kind.</param>
    /// <param name="FilePath">The repository-relative file path associated with the evidence.</param>
    /// <param name="StartLine">The optional starting line number for source-backed evidence.</param>
    /// <param name="EndLine">The optional ending line number for source-backed evidence.</param>
    /// <param name="SymbolName">The optional symbol name carried by the evidence record.</param>
    /// <param name="ContainingSymbol">The optional containing symbol carried by the evidence record.</param>
    /// <param name="SnippetPreview">The bounded and secret-safe snippet preview metadata.</param>
    /// <param name="FindingContext">The finding records directly connected to the evidence.</param>
    /// <param name="RuleContext">The rule records directly connected through findings or catalog metadata.</param>
    /// <param name="RelatedRecords">The graph records that directly point at the evidence.</param>
    /// <param name="SnapshotStableKey">The snapshot stable key that scopes this evidence record.</param>
    /// <param name="Confidence">The normalized confidence assigned to the evidence.</param>
    /// <param name="Classification">The evidence knowledge classification.</param>
    /// <param name="UnknownReason">The explicit unknown reason carried by the evidence.</param>
    /// <param name="Metadata">The sanitized public metadata associated with the evidence.</param>
    public sealed record EvidenceDetailDto(
        string StableKey,
        string EvidenceKind,
        string FilePath,
        int? StartLine,
        int? EndLine,
        string? SymbolName,
        string? ContainingSymbol,
        EvidenceSnippetPreviewDto SnippetPreview,
        IReadOnlyList<EvidenceRelatedRecordDto> FindingContext,
        IReadOnlyList<EvidenceRelatedRecordDto> RuleContext,
        IReadOnlyList<EvidenceRelatedRecordDto> RelatedRecords,
        string SnapshotStableKey,
        decimal Confidence,
        string Classification,
        EvidenceUnknownReasonDto UnknownReason,
        GraphMetadata Metadata);

    /// <summary>
    /// Represents scope, snapshot, warning, and unknown metadata shared by evidence API envelopes.
    /// </summary>
    /// <param name="Scope">The repository and solution scope resolved for the selected snapshot.</param>
    /// <param name="Snapshot">The selected snapshot metadata exposed in the API envelope.</param>
    /// <param name="Warnings">The safe warnings associated with the selected evidence query.</param>
    /// <param name="Unknowns">The explicit unknowns associated with the selected evidence query.</param>
    public sealed record EvidenceQueryContext(ProjectScopeDto Scope, ProjectSnapshotMetadataDto Snapshot, IReadOnlyList<EvidenceWarningDto> Warnings, IReadOnlyList<EvidenceUnknownDto> Unknowns);

    /// <summary>
    /// Represents either a successful evidence detail payload or deterministic validation errors.
    /// </summary>
    public sealed class EvidenceDetailResult
    {
        /// <summary>
        /// Initializes a new successful instance of the <see cref="EvidenceDetailResult"/> class.
        /// </summary>
        /// <param name="detail">The successful evidence detail payload.</param>
        /// <param name="context">The shared response context for the selected evidence query.</param>
        public EvidenceDetailResult(EvidenceDetailDto detail, EvidenceQueryContext context)
        {
            // Successful results carry the detail and envelope context while keeping validation errors empty.
            Detail = detail ?? throw new ArgumentNullException(nameof(detail));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a new unsuccessful instance of the <see cref="EvidenceDetailResult"/> class.
        /// </summary>
        /// <param name="validationErrors">The deterministic validation errors that prevented evidence detail creation.</param>
        public EvidenceDetailResult(IEnumerable<EvidenceQueryValidationError> validationErrors)
        {
            // Validation errors are copied and exposed without exception details so the API can produce a safe error shape.
            Detail = null;
            Context = null;
            ValidationErrors = validationErrors?.ToArray() ?? [];
        }

        /// <summary>
        /// Gets a value indicating whether the evidence detail request succeeded.
        /// </summary>
        public bool Succeeded
        {
            get
            {
                // Success is determined by the presence of both detail and context so callers cannot ignore validation errors accidentally.
                return Detail is not null && Context is not null;
            }
        }

        /// <summary>
        /// Gets the successful evidence detail when <see cref="Succeeded"/> is true.
        /// </summary>
        public EvidenceDetailDto? Detail { get; }

        /// <summary>
        /// Gets the successful evidence query context when <see cref="Succeeded"/> is true.
        /// </summary>
        public EvidenceQueryContext? Context { get; }

        /// <summary>
        /// Gets the deterministic validation errors that prevented evidence detail creation.
        /// </summary>
        public IReadOnlyList<EvidenceQueryValidationError> ValidationErrors { get; }
    }

    /// <summary>
    /// Represents either a successful related-evidence page or deterministic validation errors.
    /// </summary>
    public sealed class RelatedEvidenceResult
    {
        /// <summary>
        /// Initializes a new successful instance of the <see cref="RelatedEvidenceResult"/> class.
        /// </summary>
        /// <param name="page">The successful related-evidence page.</param>
        /// <param name="context">The shared response context for the selected evidence query.</param>
        public RelatedEvidenceResult(PagedQueryResult<EvidenceDetailDto> page, EvidenceQueryContext context)
        {
            // Successful related-evidence results carry a bounded page and envelope context while keeping validation errors empty.
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a new unsuccessful instance of the <see cref="RelatedEvidenceResult"/> class.
        /// </summary>
        /// <param name="validationErrors">The deterministic validation errors that prevented related-evidence lookup.</param>
        public RelatedEvidenceResult(IEnumerable<EvidenceQueryValidationError> validationErrors)
        {
            // Validation errors are copied and exposed without exception details so the API can produce a safe error shape.
            Page = null;
            Context = null;
            ValidationErrors = validationErrors?.ToArray() ?? [];
        }

        /// <summary>
        /// Gets a value indicating whether the related-evidence request succeeded.
        /// </summary>
        public bool Succeeded
        {
            get
            {
                // Success is determined by the presence of both page and context so callers cannot ignore validation errors accidentally.
                return Page is not null && Context is not null;
            }
        }

        /// <summary>
        /// Gets the successful related-evidence page when <see cref="Succeeded"/> is true.
        /// </summary>
        public PagedQueryResult<EvidenceDetailDto>? Page { get; }

        /// <summary>
        /// Gets the successful evidence query context when <see cref="Succeeded"/> is true.
        /// </summary>
        public EvidenceQueryContext? Context { get; }

        /// <summary>
        /// Gets the deterministic validation errors that prevented related-evidence lookup.
        /// </summary>
        public IReadOnlyList<EvidenceQueryValidationError> ValidationErrors { get; }
    }
}
