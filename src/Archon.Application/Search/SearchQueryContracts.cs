using Archon.Application.Projects;
using Archon.Application.Rules;

namespace Archon.Application.Search
{
    /// <summary>
    /// Defines stable result-size limits for the WP014 cross-domain search surface.
    /// </summary>
    public static class SearchQueryLimits
    {
        /// <summary>
        /// Defines the default number of search rows returned when callers omit a take value.
        /// </summary>
        public const int DefaultTake = 50;

        /// <summary>
        /// Defines the maximum number of search rows a single request can return.
        /// </summary>
        public const int MaximumTake = 200;
    }

    /// <summary>
    /// Defines deterministic validation codes returned by cross-domain search requests.
    /// </summary>
    public static class SearchQueryValidationCodes
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
        /// Indicates that no meaningful search text was supplied.
        /// </summary>
        public const string SearchTextRequired = nameof(SearchTextRequired);

        /// <summary>
        /// Indicates that a supplied result-kind filter is not part of the controlled search vocabulary.
        /// </summary>
        public const string UnsupportedResultKind = nameof(UnsupportedResultKind);

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
    /// Defines controlled search result-kind values used by API and future MCP consumers.
    /// </summary>
    public static class SearchResultKinds
    {
        /// <summary>
        /// Identifies project architecture-node search results.
        /// </summary>
        public const string Project = nameof(Project);

        /// <summary>
        /// Identifies semantic symbol search results.
        /// </summary>
        public const string Symbol = nameof(Symbol);

        /// <summary>
        /// Identifies runtime endpoint search results.
        /// </summary>
        public const string RuntimeEndpoint = nameof(RuntimeEndpoint);

        /// <summary>
        /// Identifies data-access, configuration, integration, or UI-technology fact search results.
        /// </summary>
        public const string Fact = nameof(Fact);

        /// <summary>
        /// Identifies evidence search results.
        /// </summary>
        public const string Evidence = nameof(Evidence);

        /// <summary>
        /// Identifies finding search results.
        /// </summary>
        public const string Finding = nameof(Finding);

        /// <summary>
        /// Identifies metric search results.
        /// </summary>
        public const string Metric = nameof(Metric);

        /// <summary>
        /// Lists all supported search result kinds in deterministic ordering.
        /// </summary>
        public static readonly IReadOnlyList<string> All = [Project, Symbol, RuntimeEndpoint, Fact, Evidence, Finding, Metric];
    }

    /// <summary>
    /// Represents one deterministic validation problem produced by a cross-domain search query.
    /// </summary>
    /// <param name="Code">The stable machine-readable validation code.</param>
    /// <param name="Message">The safe developer-facing validation message.</param>
    public sealed record SearchQueryValidationError(string Code, string Message);

    /// <summary>
    /// Represents one safe warning emitted by search queries when response data is partial or bounded.
    /// </summary>
    /// <param name="Code">The stable machine-readable warning code.</param>
    /// <param name="Message">The safe developer-facing warning message.</param>
    public sealed record SearchWarningDto(string Code, string Message);

    /// <summary>
    /// Represents one explicit unknown field emitted by search when persisted facts cannot prove completeness.
    /// </summary>
    /// <param name="Field">The response field or result family whose value is unknown.</param>
    /// <param name="Reason">The safe reason that explains why the value is unknown.</param>
    public sealed record SearchUnknownDto(string Field, string Reason);

    /// <summary>
    /// Describes repository, solution, and snapshot selection for WP014 cross-domain search queries.
    /// </summary>
    public sealed class SearchSnapshotSelector
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SearchSnapshotSelector"/> class.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable key that bounds latest/current snapshot resolution.</param>
        /// <param name="solutionStableKey">The optional solution stable key that narrows repository scope.</param>
        /// <param name="snapshotStableKey">The exact snapshot stable key or latest/current selector supplied by the caller.</param>
        public SearchSnapshotSelector(string? repositoryStableKey, string? solutionStableKey, string? snapshotStableKey)
        {
            // Search follows the same WP014 selector model as project, symbol, runtime, fact, and evidence reads.
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
            // Blank selector fields should behave like omitted fields rather than invisible whitespace identities.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    /// <summary>
    /// Represents a bounded cross-domain search request.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the lookup.</param>
    /// <param name="SearchText">The required text matched against stable keys, display text, summaries, and safe metadata.</param>
    /// <param name="ResultKinds">The optional controlled result-kind filters.</param>
    /// <param name="ProjectStableKey">The optional owning or related project stable-key filter.</param>
    /// <param name="Skip">The number of sorted search rows to skip.</param>
    /// <param name="Take">The maximum number of sorted search rows to return.</param>
    public sealed record SearchQuery(SearchSnapshotSelector Selector, string? SearchText, IReadOnlyList<string> ResultKinds, string? ProjectStableKey, int Skip, int Take);

    /// <summary>
    /// Represents one deterministic follow-up route a caller can use after a search hit.
    /// </summary>
    /// <param name="Label">The developer-facing follow-up label.</param>
    /// <param name="Route">The API route path for the follow-up action.</param>
    /// <param name="Parameters">The stable query parameters that should be supplied to the route.</param>
    public sealed record SearchFollowUpAffordanceDto(string Label, string Route, IReadOnlyDictionary<string, string> Parameters);

    /// <summary>
    /// Represents one broad search result row suitable for direct API and future MCP clients.
    /// </summary>
    /// <param name="ResultKind">The controlled result kind for the row.</param>
    /// <param name="StableKey">The stable public identity of the matched record.</param>
    /// <param name="DisplayText">The short display text for the matched record.</param>
    /// <param name="Summary">The safe summary explaining why the record is useful.</param>
    /// <param name="SnapshotStableKey">The snapshot stable key that owns the matched record.</param>
    /// <param name="Confidence">The normalized confidence value associated with the matched record.</param>
    /// <param name="EvidenceStableKeys">Stable evidence identities that explain the record where available.</param>
    /// <param name="RelatedNodeStableKeys">Stable node identities related to the result.</param>
    /// <param name="HasUnknownData">Indicates whether the result carries explicit unknown-state context.</param>
    /// <param name="UnknownReason">The optional unknown-state reason carried by the result.</param>
    /// <param name="FollowUps">Deterministic route affordances for safe follow-up queries.</param>
    public sealed record SearchResultItemDto(
        string ResultKind,
        string StableKey,
        string DisplayText,
        string Summary,
        string SnapshotStableKey,
        decimal Confidence,
        IReadOnlyList<string> EvidenceStableKeys,
        IReadOnlyList<string> RelatedNodeStableKeys,
        bool HasUnknownData,
        string? UnknownReason,
        IReadOnlyList<SearchFollowUpAffordanceDto> FollowUps);

    /// <summary>
    /// Carries scope, snapshot, warning, and unknown metadata shared by search query results.
    /// </summary>
    /// <param name="Scope">The repository and optional solution scope applied to the query.</param>
    /// <param name="Snapshot">The resolved snapshot metadata used to build the result.</param>
    /// <param name="Warnings">The safe warnings that explain partial result content.</param>
    /// <param name="Unknowns">The explicit unknown fields that distinguish unavailable data from empty values.</param>
    public sealed record SearchQueryContext(ProjectScopeDto Scope, ProjectSnapshotMetadataDto Snapshot, IReadOnlyList<SearchWarningDto> Warnings, IReadOnlyList<SearchUnknownDto> Unknowns)
    {
        // The context is mapped into the API envelope so search follows the same metadata contract as other WP014 query families.
    }

    /// <summary>
    /// Represents the complete application result of a bounded cross-domain search request.
    /// </summary>
    public sealed class SearchResult
    {
        /// <summary>
        /// Initializes a successful search result.
        /// </summary>
        /// <param name="page">The bounded page of search results.</param>
        /// <param name="context">The scope and snapshot context for the search.</param>
        public SearchResult(PagedQueryResult<SearchResultItemDto> page, SearchQueryContext context)
        {
            // Successful results carry both rows and context so the API layer can build a common paged envelope.
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
            Succeeded = true;
        }

        /// <summary>
        /// Initializes a failed search result with deterministic validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that prevented search execution.</param>
        public SearchResult(IEnumerable<SearchQueryValidationError> validationErrors)
        {
            // Validation failures remain data so hosts can return grouped problem details without inspecting exceptions.
            Page = null;
            Context = null;
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
            Succeeded = false;
        }

        /// <summary>
        /// Gets a value indicating whether the search request succeeded.
        /// </summary>
        public bool Succeeded { get; }

        /// <summary>
        /// Gets the bounded page when the request succeeded.
        /// </summary>
        public PagedQueryResult<SearchResultItemDto>? Page { get; }

        /// <summary>
        /// Gets the resolved scope and snapshot context when the request succeeded.
        /// </summary>
        public SearchQueryContext? Context { get; }

        /// <summary>
        /// Gets deterministic validation errors when <see cref="Succeeded"/> is false.
        /// </summary>
        public IReadOnlyList<SearchQueryValidationError> ValidationErrors { get; }
    }
}
