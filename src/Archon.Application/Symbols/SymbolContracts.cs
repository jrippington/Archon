using Archon.Application.Projects;
using Archon.Application.Rules;

namespace Archon.Application.Symbols
{
    /// <summary>
    /// Defines stable limits for symbol search and usage queries.
    /// </summary>
    public static class SymbolQueryLimits
    {
        /// <summary>
        /// Defines the default number of symbol rows returned when callers do not supply a take value.
        /// </summary>
        public const int DefaultTake = 50;

        /// <summary>
        /// Defines the maximum number of symbol rows or usage rows a single request can return.
        /// </summary>
        public const int MaximumTake = 200;

        /// <summary>
        /// Defines the maximum number of characters exposed from source evidence snippets.
        /// </summary>
        public const int MaximumSnippetPreviewLength = 160;
    }

    /// <summary>
    /// Defines deterministic validation codes for symbol query endpoints.
    /// </summary>
    public static class SymbolQueryValidationCodes
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
        /// Indicates that a required symbol identity was not supplied.
        /// </summary>
        public const string SymbolIdentityRequired = nameof(SymbolIdentityRequired);

        /// <summary>
        /// Indicates that both stable-key and search-text symbol identities were supplied for a single detail lookup.
        /// </summary>
        public const string SymbolIdentityAmbiguous = nameof(SymbolIdentityAmbiguous);

        /// <summary>
        /// Indicates that the requested symbol was not found.
        /// </summary>
        public const string SymbolNotFound = nameof(SymbolNotFound);

        /// <summary>
        /// Indicates that an exact search-text lookup matched more than one symbol.
        /// </summary>
        public const string SymbolSearchTextAmbiguous = nameof(SymbolSearchTextAmbiguous);

        /// <summary>
        /// Indicates that the supplied symbol kind filter is not a supported semantic symbol kind.
        /// </summary>
        public const string SymbolKindUnsupported = nameof(SymbolKindUnsupported);

        /// <summary>
        /// Indicates that the supplied sort field is not supported for symbol search.
        /// </summary>
        public const string SortUnsupported = nameof(SortUnsupported);

        /// <summary>
        /// Indicates that the supplied skip value is outside supported bounds.
        /// </summary>
        public const string SkipInvalid = nameof(SkipInvalid);

        /// <summary>
        /// Indicates that the supplied take value is outside supported bounds.
        /// </summary>
        public const string TakeInvalid = nameof(TakeInvalid);

        /// <summary>
        /// Indicates that the supplied usage direction is not supported.
        /// </summary>
        public const string UsageDirectionUnsupported = nameof(UsageDirectionUnsupported);
    }

    /// <summary>
    /// Represents one deterministic validation problem produced by a symbol query.
    /// </summary>
    /// <param name="Code">The stable machine-readable validation code.</param>
    /// <param name="Message">The safe developer-facing validation message.</param>
    public sealed record SymbolQueryValidationError(string Code, string Message);

    /// <summary>
    /// Represents one safe warning emitted by symbol queries when response data is partial or bounded.
    /// </summary>
    /// <param name="Code">The stable machine-readable warning code.</param>
    /// <param name="Message">The safe developer-facing warning message.</param>
    public sealed record SymbolWarningDto(string Code, string Message);

    /// <summary>
    /// Represents one explicit unknown field emitted by symbol queries when semantic extraction could not prove completeness.
    /// </summary>
    /// <param name="Field">The response field or semantic concept whose value is unknown.</param>
    /// <param name="Reason">The safe reason that explains why the value is unknown.</param>
    public sealed record SymbolUnknownDto(string Field, string Reason);

    /// <summary>
    /// Describes repository, solution, and snapshot selection for symbol queries.
    /// </summary>
    public sealed class SymbolSnapshotSelector
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SymbolSnapshotSelector"/> class.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable key that bounds latest/current snapshot resolution.</param>
        /// <param name="solutionStableKey">The optional solution stable key that narrows repository scope.</param>
        /// <param name="snapshotStableKey">The exact snapshot stable key or latest/current selector supplied by the caller.</param>
        public SymbolSnapshotSelector(string? repositoryStableKey, string? solutionStableKey, string? snapshotStableKey)
        {
            // Symbol query scope follows the existing WP014 selector behavior so latest resolution remains repository-bounded.
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
        public bool RequestsLatestSnapshot => string.Equals(SnapshotStableKey, "latest", StringComparison.OrdinalIgnoreCase) || string.Equals(SnapshotStableKey, "current", StringComparison.OrdinalIgnoreCase);

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
    /// Represents a controlled symbol search request.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the search.</param>
    /// <param name="SearchText">The optional search text matched against stable key, name, fully qualified name, and normalized search name.</param>
    /// <param name="ProjectStableKey">The optional exact owning project stable-key filter.</param>
    /// <param name="Kind">The optional exact symbol kind filter.</param>
    /// <param name="Namespace">The optional exact namespace filter.</param>
    /// <param name="ContainingType">The optional exact containing type filter.</param>
    /// <param name="Language">The optional exact language filter.</param>
    /// <param name="Sort">The optional deterministic sort field.</param>
    /// <param name="Descending">A value indicating whether the primary sort should be descending.</param>
    /// <param name="Skip">The number of sorted records to skip.</param>
    /// <param name="Take">The maximum number of sorted records to return.</param>
    public sealed record SymbolSearchQuery(SymbolSnapshotSelector Selector, string? SearchText, string? ProjectStableKey, string? Kind, string? Namespace, string? ContainingType, string? Language, string? Sort, bool Descending, int Skip, int Take);

    /// <summary>
    /// Represents a controlled symbol detail request.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the lookup.</param>
    /// <param name="SymbolStableKey">The optional exact stable symbol key.</param>
    /// <param name="SearchText">The optional exact symbol search text used when a stable key is unavailable.</param>
    public sealed record SymbolDetailQuery(SymbolSnapshotSelector Selector, string? SymbolStableKey, string? SearchText);

    /// <summary>
    /// Represents a controlled symbol usage request.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the lookup.</param>
    /// <param name="SymbolStableKey">The stable symbol key whose usages should be listed.</param>
    /// <param name="Direction">The optional usage direction, either Incoming for callers/references or Outgoing for calls made by the symbol.</param>
    /// <param name="Skip">The number of sorted usage records to skip.</param>
    /// <param name="Take">The maximum number of sorted usage records to return.</param>
    public sealed record SymbolUsageQuery(SymbolSnapshotSelector Selector, string? SymbolStableKey, string? Direction, int Skip, int Take);

    /// <summary>
    /// Represents source-location context for a symbol or usage without exposing unbounded source text.
    /// </summary>
    /// <param name="FilePath">The repository-relative file path associated with the source context.</param>
    /// <param name="StartLine">The optional starting line number for the source context.</param>
    /// <param name="EndLine">The optional ending line number for the source context.</param>
    /// <param name="SnippetPreview">The optional bounded snippet preview treated as untrusted display text.</param>
    public sealed record SymbolSourceContextDto(string? FilePath, int? StartLine, int? EndLine, string? SnippetPreview);

    /// <summary>
    /// Represents a safe evidence reference associated with a symbol query response.
    /// </summary>
    /// <param name="StableKey">The stable evidence identity.</param>
    /// <param name="EvidenceKind">The controlled evidence kind.</param>
    /// <param name="FilePath">The repository-relative file path associated with the evidence.</param>
    /// <param name="StartLine">The optional starting line number for source-backed evidence.</param>
    /// <param name="EndLine">The optional ending line number for source-backed evidence.</param>
    /// <param name="SymbolName">The optional symbol name carried by the evidence record.</param>
    /// <param name="ContainingSymbol">The optional containing symbol carried by the evidence record.</param>
    /// <param name="SnippetHash">The optional hash of the source snippet.</param>
    /// <param name="SnippetPreview">The optional bounded source snippet preview treated as untrusted display text.</param>
    /// <param name="Confidence">The normalized confidence assigned to the evidence.</param>
    public sealed record SymbolEvidenceReferenceDto(string StableKey, string EvidenceKind, string FilePath, int? StartLine, int? EndLine, string? SymbolName, string? ContainingSymbol, string? SnippetHash, string? SnippetPreview, decimal Confidence);

    /// <summary>
    /// Represents one symbol row returned by symbol search.
    /// </summary>
    /// <param name="StableKey">The durable public symbol identity.</param>
    /// <param name="Name">The developer-facing symbol name.</param>
    /// <param name="FullyQualifiedName">The fully qualified symbol name when extraction supplied one.</param>
    /// <param name="Kind">The controlled symbol node kind.</param>
    /// <param name="ContainingProjectStableKey">The owning project stable key when the symbol is project-owned.</param>
    /// <param name="Namespace">The namespace filter value derived from symbol metadata or qualified name.</param>
    /// <param name="ContainingType">The containing type filter value derived from symbol metadata or parent relationships.</param>
    /// <param name="Language">The programming language associated with the symbol.</param>
    /// <param name="SourceContext">The bounded source context for the primary evidence reference.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys associated with the symbol.</param>
    /// <param name="Confidence">The normalized confidence assigned to the symbol.</param>
    /// <param name="HasUnknownData">A value indicating whether the symbol carries explicit unknown semantic data.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown symbol data.</param>
    public sealed record SymbolSearchItemDto(string StableKey, string Name, string? FullyQualifiedName, string Kind, string? ContainingProjectStableKey, string? Namespace, string? ContainingType, string? Language, SymbolSourceContextDto? SourceContext, IReadOnlyList<string> EvidenceStableKeys, decimal Confidence, bool HasUnknownData, string? UnknownReason);

    /// <summary>
    /// Represents one semantic relationship connected to a symbol detail response.
    /// </summary>
    /// <param name="StableKey">The stable relationship identity.</param>
    /// <param name="Kind">The controlled edge kind for the relationship.</param>
    /// <param name="SourceSymbolStableKey">The stable key of the source symbol or node.</param>
    /// <param name="TargetSymbolStableKey">The stable key of the target symbol or node.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys associated with the relationship.</param>
    /// <param name="Confidence">The normalized confidence assigned to the relationship.</param>
    public sealed record SymbolRelationshipDto(string StableKey, string Kind, string SourceSymbolStableKey, string TargetSymbolStableKey, IReadOnlyList<string> EvidenceStableKeys, decimal Confidence);

    /// <summary>
    /// Represents a detailed symbol response with source context, evidence, relationships, and uncertainty metadata.
    /// </summary>
    /// <param name="Summary">The stable symbol summary shared with search results.</param>
    /// <param name="Evidence">The safe evidence references associated with the symbol.</param>
    /// <param name="Relationships">The semantic graph relationships directly connected to the symbol.</param>
    /// <param name="Warnings">The warnings specific to this detail response.</param>
    /// <param name="Unknowns">The unknown fields specific to this detail response.</param>
    public sealed record SymbolDetailDto(SymbolSearchItemDto Summary, IReadOnlyList<SymbolEvidenceReferenceDto> Evidence, IReadOnlyList<SymbolRelationshipDto> Relationships, IReadOnlyList<SymbolWarningDto> Warnings, IReadOnlyList<SymbolUnknownDto> Unknowns);

    /// <summary>
    /// Represents one symbol usage, reference, or call edge returned by symbol usage queries.
    /// </summary>
    /// <param name="UsageStableKey">The stable relationship identity for the usage.</param>
    /// <param name="UsageKind">The controlled edge kind for the usage relationship.</param>
    /// <param name="SourceSymbolStableKey">The stable key of the referencing or calling source symbol.</param>
    /// <param name="TargetSymbolStableKey">The stable key of the referenced or called target symbol.</param>
    /// <param name="SourceName">The developer-facing source symbol name.</param>
    /// <param name="TargetName">The developer-facing target symbol name.</param>
    /// <param name="FilePath">The repository-relative file path associated with usage evidence.</param>
    /// <param name="StartLine">The optional starting line for usage evidence.</param>
    /// <param name="EndLine">The optional ending line for usage evidence.</param>
    /// <param name="SnippetPreview">The optional bounded snippet preview treated as untrusted display text.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys associated with the usage relationship.</param>
    /// <param name="Confidence">The normalized confidence assigned to the usage relationship.</param>
    /// <param name="HasUnknownData">A value indicating whether the usage carries explicit unknown semantic data.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown usage data.</param>
    public sealed record SymbolUsageDto(string UsageStableKey, string UsageKind, string SourceSymbolStableKey, string TargetSymbolStableKey, string? SourceName, string? TargetName, string? FilePath, int? StartLine, int? EndLine, string? SnippetPreview, IReadOnlyList<string> EvidenceStableKeys, decimal Confidence, bool HasUnknownData, string? UnknownReason);

    /// <summary>
    /// Represents the response context shared by symbol envelopes.
    /// </summary>
    /// <param name="Scope">The resolved repository and optional solution scope.</param>
    /// <param name="Snapshot">The resolved snapshot metadata.</param>
    /// <param name="Warnings">The safe warnings emitted while building symbol output.</param>
    /// <param name="Unknowns">The explicit unknown fields emitted while building symbol output.</param>
    public sealed record SymbolQueryContext(ProjectScopeDto Scope, ProjectSnapshotMetadataDto Snapshot, IReadOnlyList<SymbolWarningDto> Warnings, IReadOnlyList<SymbolUnknownDto> Unknowns);

    /// <summary>
    /// Represents the application result for a symbol search request.
    /// </summary>
    public sealed class SymbolSearchResult
    {
        /// <summary>
        /// Initializes a successful symbol search result.
        /// </summary>
        /// <param name="page">The bounded page of matching symbols.</param>
        /// <param name="context">The symbol envelope context.</param>
        public SymbolSearchResult(PagedQueryResult<SymbolSearchItemDto> page, SymbolQueryContext context)
        {
            // Successful search results include both page data and envelope context for consistent API response mapping.
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a failed symbol search result with validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that explain why search did not run.</param>
        public SymbolSearchResult(IEnumerable<SymbolQueryValidationError> validationErrors)
        {
            // Failed search results carry deterministic validation errors and no page payload.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        /// <summary>
        /// Gets a value indicating whether search succeeded and produced a page payload.
        /// </summary>
        public bool Succeeded => ValidationErrors.Count == 0;

        /// <summary>
        /// Gets the bounded symbol page when search succeeds.
        /// </summary>
        public PagedQueryResult<SymbolSearchItemDto>? Page { get; }

        /// <summary>
        /// Gets the symbol envelope context when search succeeds.
        /// </summary>
        public SymbolQueryContext? Context { get; }

        /// <summary>
        /// Gets the validation errors that explain why search did not run.
        /// </summary>
        public IReadOnlyList<SymbolQueryValidationError> ValidationErrors { get; }
    }

    /// <summary>
    /// Represents the application result for a symbol detail request.
    /// </summary>
    public sealed class SymbolDetailResult
    {
        /// <summary>
        /// Initializes a successful symbol detail result.
        /// </summary>
        /// <param name="detail">The symbol detail payload.</param>
        /// <param name="context">The symbol envelope context.</param>
        public SymbolDetailResult(SymbolDetailDto detail, SymbolQueryContext context)
        {
            // Successful detail results include both data and envelope context for consistent API response mapping.
            Detail = detail ?? throw new ArgumentNullException(nameof(detail));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a failed symbol detail result with validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that explain why detail lookup did not run.</param>
        public SymbolDetailResult(IEnumerable<SymbolQueryValidationError> validationErrors)
        {
            // Failed detail results carry deterministic validation errors and no detail payload.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        /// <summary>
        /// Gets a value indicating whether detail lookup succeeded and produced a payload.
        /// </summary>
        public bool Succeeded => ValidationErrors.Count == 0;

        /// <summary>
        /// Gets the selected symbol detail when lookup succeeds.
        /// </summary>
        public SymbolDetailDto? Detail { get; }

        /// <summary>
        /// Gets the symbol envelope context when lookup succeeds.
        /// </summary>
        public SymbolQueryContext? Context { get; }

        /// <summary>
        /// Gets the validation errors that explain why detail lookup did not run.
        /// </summary>
        public IReadOnlyList<SymbolQueryValidationError> ValidationErrors { get; }
    }

    /// <summary>
    /// Represents the application result for a symbol usage request.
    /// </summary>
    public sealed class SymbolUsageResult
    {
        /// <summary>
        /// Initializes a successful symbol usage result.
        /// </summary>
        /// <param name="page">The bounded page of matching symbol usages.</param>
        /// <param name="context">The symbol envelope context.</param>
        public SymbolUsageResult(PagedQueryResult<SymbolUsageDto> page, SymbolQueryContext context)
        {
            // Successful usage results include both page data and envelope context for consistent API response mapping.
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a failed symbol usage result with validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that explain why usage lookup did not run.</param>
        public SymbolUsageResult(IEnumerable<SymbolQueryValidationError> validationErrors)
        {
            // Failed usage results carry deterministic validation errors and no usage page payload.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        /// <summary>
        /// Gets a value indicating whether usage lookup succeeded and produced a page payload.
        /// </summary>
        public bool Succeeded => ValidationErrors.Count == 0;

        /// <summary>
        /// Gets the bounded usage page when lookup succeeds.
        /// </summary>
        public PagedQueryResult<SymbolUsageDto>? Page { get; }

        /// <summary>
        /// Gets the symbol envelope context when lookup succeeds.
        /// </summary>
        public SymbolQueryContext? Context { get; }

        /// <summary>
        /// Gets the validation errors that explain why usage lookup did not run.
        /// </summary>
        public IReadOnlyList<SymbolQueryValidationError> ValidationErrors { get; }
    }
}
