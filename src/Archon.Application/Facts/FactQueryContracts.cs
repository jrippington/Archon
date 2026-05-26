using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Domain.Graph.Metadata;

namespace Archon.Application.Facts
{
    /// <summary>
    /// Defines stable paging limits for WP014 fact-query endpoints.
    /// </summary>
    public static class FactQueryLimits
    {
        /// <summary>
        /// Defines the default number of fact rows returned when callers omit a take value.
        /// </summary>
        public const int DefaultTake = 50;

        /// <summary>
        /// Defines the maximum number of fact rows a single request can return.
        /// </summary>
        public const int MaximumTake = 200;
    }

    /// <summary>
    /// Defines deterministic validation codes for data-access, configuration, integration, and UI-technology fact queries.
    /// </summary>
    public static class FactQueryValidationCodes
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
        /// Indicates that the supplied skip value is outside supported bounds.
        /// </summary>
        public const string SkipInvalid = nameof(SkipInvalid);

        /// <summary>
        /// Indicates that the supplied take value is outside supported bounds.
        /// </summary>
        public const string TakeInvalid = nameof(TakeInvalid);

        /// <summary>
        /// Indicates that a supplied controlled fact-family filter is unsupported.
        /// </summary>
        public const string FactFamilyUnsupported = nameof(FactFamilyUnsupported);
    }

    /// <summary>
    /// Represents one deterministic validation problem produced by a fact query.
    /// </summary>
    /// <param name="Code">The stable machine-readable validation code.</param>
    /// <param name="Message">The safe developer-facing validation message.</param>
    public sealed record FactQueryValidationError(string Code, string Message);

    /// <summary>
    /// Represents one safe warning emitted by fact queries when response data is partial or bounded.
    /// </summary>
    /// <param name="Code">The stable machine-readable warning code.</param>
    /// <param name="Message">The safe developer-facing warning message.</param>
    public sealed record FactWarningDto(string Code, string Message);

    /// <summary>
    /// Represents one explicit unknown field emitted by fact queries when extraction could not prove completeness.
    /// </summary>
    /// <param name="Field">The response field or fact family whose value is unknown.</param>
    /// <param name="Reason">The safe reason that explains why the value is unknown.</param>
    public sealed record FactUnknownDto(string Field, string Reason);

    /// <summary>
    /// Describes repository, solution, and snapshot selection for WP014 fact queries.
    /// </summary>
    public sealed class FactSnapshotSelector
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FactSnapshotSelector"/> class.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable key that bounds latest/current snapshot resolution.</param>
        /// <param name="solutionStableKey">The optional solution stable key that narrows repository scope.</param>
        /// <param name="snapshotStableKey">The exact snapshot stable key or latest/current selector supplied by the caller.</param>
        public FactSnapshotSelector(string? repositoryStableKey, string? solutionStableKey, string? snapshotStableKey)
        {
            // Fact queries follow earlier WP014 selector behavior so latest resolution remains repository-bounded and deterministic.
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
    /// Represents a bounded data-access fact query request.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the lookup.</param>
    /// <param name="Family">The optional data-access family filter such as EF Core, LINQ to SQL, ADO.NET, or raw SQL.</param>
    /// <param name="ProjectStableKey">The optional exact owning project stable-key filter.</param>
    /// <param name="UsageSite">The optional usage-site text filter.</param>
    /// <param name="Entity">The optional entity display-name or stable-key filter.</param>
    /// <param name="Table">The optional table display-name or stable-key filter.</param>
    /// <param name="StoredProcedure">The optional stored-procedure display-name or stable-key filter.</param>
    /// <param name="Skip">The number of sorted records to skip.</param>
    /// <param name="Take">The maximum number of sorted records to return.</param>
    public sealed record DataAccessFactQuery(FactSnapshotSelector Selector, string? Family, string? ProjectStableKey, string? UsageSite, string? Entity, string? Table, string? StoredProcedure, int Skip, int Take);

    /// <summary>
    /// Represents a bounded configuration-usage fact query request.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the lookup.</param>
    /// <param name="ConfigurationKey">The optional configuration-key text filter.</param>
    /// <param name="ProjectStableKey">The optional exact owning project stable-key filter.</param>
    /// <param name="ConsumerStableKey">The optional exact consumer node stable-key filter.</param>
    /// <param name="Provider">The optional configuration provider filter.</param>
    /// <param name="Environment">The optional environment-name filter.</param>
    /// <param name="SourceFile">The optional source-file path filter.</param>
    /// <param name="Skip">The number of sorted records to skip.</param>
    /// <param name="Take">The maximum number of sorted records to return.</param>
    public sealed record ConfigurationUsageQuery(FactSnapshotSelector Selector, string? ConfigurationKey, string? ProjectStableKey, string? ConsumerStableKey, string? Provider, string? Environment, string? SourceFile, int Skip, int Take);

    /// <summary>
    /// Represents a bounded external-integration fact query request.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the lookup.</param>
    /// <param name="ProjectStableKey">The optional exact owning project stable-key filter.</param>
    /// <param name="IntegrationKind">The optional integration kind filter.</param>
    /// <param name="EndpointHost">The optional safe endpoint host or service-name filter.</param>
    /// <param name="Protocol">The optional protocol filter.</param>
    /// <param name="ClientType">The optional client type filter.</param>
    /// <param name="ConfigurationKey">The optional safe configuration-key filter.</param>
    /// <param name="Skip">The number of sorted records to skip.</param>
    /// <param name="Take">The maximum number of sorted records to return.</param>
    public sealed record IntegrationFactQuery(FactSnapshotSelector Selector, string? ProjectStableKey, string? IntegrationKind, string? EndpointHost, string? Protocol, string? ClientType, string? ConfigurationKey, int Skip, int Take);

    /// <summary>
    /// Represents a bounded UI-technology fact query request.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the lookup.</param>
    /// <param name="Technology">The optional UI technology filter such as Blazor, Razor, WinForms, WPF, WinUI, MAUI, or Avalonia.</param>
    /// <param name="ProjectStableKey">The optional exact owning project stable-key filter.</param>
    /// <param name="Route">The optional route or view path filter.</param>
    /// <param name="Component">The optional component, page, view, control, or binding text filter.</param>
    /// <param name="Skip">The number of sorted records to skip.</param>
    /// <param name="Take">The maximum number of sorted records to return.</param>
    public sealed record UiTechnologyFactQuery(FactSnapshotSelector Selector, string? Technology, string? ProjectStableKey, string? Route, string? Component, int Skip, int Take);

    /// <summary>
    /// Represents source-location context for a fact without exposing unbounded source text.
    /// </summary>
    /// <param name="FilePath">The repository-relative file path associated with the source context.</param>
    /// <param name="StartLine">The optional starting line number for the source context.</param>
    /// <param name="EndLine">The optional ending line number for the source context.</param>
    /// <param name="SnippetPreview">The optional bounded snippet preview treated as untrusted display text.</param>
    public sealed record FactSourceContextDto(string? FilePath, int? StartLine, int? EndLine, string? SnippetPreview);

    /// <summary>
    /// Represents a safe evidence reference associated with a fact query response.
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
    public sealed record FactEvidenceReferenceDto(string StableKey, string EvidenceKind, string FilePath, int? StartLine, int? EndLine, string? SymbolName, string? ContainingSymbol, string? SnippetHash, string? SnippetPreview, decimal Confidence);

    /// <summary>
    /// Represents one data-access architecture fact row.
    /// </summary>
    /// <param name="StableKey">The durable public data-access fact identity.</param>
    /// <param name="Family">The data-access family, such as EF Core, EF6, LINQ to SQL, ADO.NET, typed DataSet, raw SQL, or stored procedure.</param>
    /// <param name="Name">The developer-facing fact name.</param>
    /// <param name="ProjectStableKey">The owning project stable key when known.</param>
    /// <param name="DataContextStableKey">The DbContext or LINQ to SQL DataContext stable key when applicable.</param>
    /// <param name="EntityStableKey">The data entity stable key when applicable.</param>
    /// <param name="TableStableKey">The database table stable key when applicable.</param>
    /// <param name="StoredProcedureStableKey">The stored procedure stable key when applicable.</param>
    /// <param name="UsageSites">The stable usage-site identities or safe display names connected to the fact.</param>
    /// <param name="Operations">The safe operation names such as SubmitChanges, ExecuteQuery, ExecuteCommand, read, write, or map.</param>
    /// <param name="EvidenceStableKeys">The stable evidence keys associated with the fact.</param>
    /// <param name="Confidence">The normalized confidence assigned to the fact.</param>
    /// <param name="HasUnknownData">A value indicating whether explicit unknown data exists for the fact.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown fact data.</param>
    /// <param name="Metadata">The sanitized public metadata associated with the fact.</param>
    public sealed record DataAccessFactDto(string StableKey, string Family, string Name, string? ProjectStableKey, string? DataContextStableKey, string? EntityStableKey, string? TableStableKey, string? StoredProcedureStableKey, IReadOnlyList<string> UsageSites, IReadOnlyList<string> Operations, IReadOnlyList<string> EvidenceStableKeys, decimal Confidence, bool HasUnknownData, string? UnknownReason, GraphMetadata Metadata);

    /// <summary>
    /// Represents one secret-safe configuration usage fact row.
    /// </summary>
    /// <param name="StableKey">The durable public configuration fact identity.</param>
    /// <param name="Key">The safe configuration key name with any value omitted.</param>
    /// <param name="ProjectStableKey">The owning project stable key when known.</param>
    /// <param name="ConsumerStableKeys">The consumer node stable keys related to the key.</param>
    /// <param name="Providers">The safe configuration provider names inferred from metadata.</param>
    /// <param name="Environment">The optional safe environment name inferred from metadata.</param>
    /// <param name="SourceFiles">The safe source file paths associated with the key.</param>
    /// <param name="ValueAvailable">A value indicating whether extraction saw a value but intentionally withheld it.</param>
    /// <param name="SecretLike">A value indicating whether the key looks secret-like and therefore receives stronger redaction treatment.</param>
    /// <param name="EvidenceStableKeys">The stable evidence keys associated with the configuration fact.</param>
    /// <param name="Confidence">The normalized confidence assigned to the fact.</param>
    /// <param name="HasUnknownData">A value indicating whether explicit unknown data exists for the fact.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown fact data.</param>
    /// <param name="Metadata">The sanitized public metadata associated with the fact.</param>
    public sealed record ConfigurationUsageDto(string StableKey, string Key, string? ProjectStableKey, IReadOnlyList<string> ConsumerStableKeys, IReadOnlyList<string> Providers, string? Environment, IReadOnlyList<string> SourceFiles, bool ValueAvailable, bool SecretLike, IReadOnlyList<string> EvidenceStableKeys, decimal Confidence, bool HasUnknownData, string? UnknownReason, GraphMetadata Metadata);

    /// <summary>
    /// Represents one secret-safe external integration fact row.
    /// </summary>
    /// <param name="StableKey">The durable public integration fact identity.</param>
    /// <param name="Name">The developer-facing integration name.</param>
    /// <param name="IntegrationKind">The integration kind such as HTTP, queue, topic, database, storage, or external service.</param>
    /// <param name="ProjectStableKey">The owning project stable key when known.</param>
    /// <param name="EndpointHost">The safe endpoint host or service name with paths, credentials, and query strings removed.</param>
    /// <param name="Protocol">The safe protocol hint when extracted.</param>
    /// <param name="ClientType">The safe client type or library hint when extracted.</param>
    /// <param name="ConfigurationKeys">The safe configuration key names connected to the integration.</param>
    /// <param name="ConsumerStableKeys">The consumer node stable keys related to the integration.</param>
    /// <param name="EvidenceStableKeys">The stable evidence keys associated with the integration fact.</param>
    /// <param name="Confidence">The normalized confidence assigned to the fact.</param>
    /// <param name="HasUnknownData">A value indicating whether explicit unknown data exists for the fact.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown fact data.</param>
    /// <param name="Metadata">The sanitized public metadata associated with the fact.</param>
    public sealed record IntegrationFactDto(string StableKey, string Name, string IntegrationKind, string? ProjectStableKey, string? EndpointHost, string? Protocol, string? ClientType, IReadOnlyList<string> ConfigurationKeys, IReadOnlyList<string> ConsumerStableKeys, IReadOnlyList<string> EvidenceStableKeys, decimal Confidence, bool HasUnknownData, string? UnknownReason, GraphMetadata Metadata);

    /// <summary>
    /// Represents one backend UI-technology architecture fact row.
    /// </summary>
    /// <param name="StableKey">The durable public UI-technology fact identity.</param>
    /// <param name="Technology">The UI technology such as Blazor, Razor, Windows Forms, WPF, WinUI, .NET MAUI, or Avalonia.</param>
    /// <param name="FactKind">The controlled fact kind, such as application, component, page, view, route, control, binding, command, or view model.</param>
    /// <param name="Name">The developer-facing fact name.</param>
    /// <param name="ProjectStableKey">The owning project stable key when known.</param>
    /// <param name="Route">The safe route template or view path when extracted.</param>
    /// <param name="RelatedStableKeys">The related UI or backend stable keys connected by UI graph edges.</param>
    /// <param name="EvidenceStableKeys">The stable evidence keys associated with the UI fact.</param>
    /// <param name="Confidence">The normalized confidence assigned to the fact.</param>
    /// <param name="HasUnknownData">A value indicating whether explicit unknown data exists for the fact.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown fact data.</param>
    /// <param name="Metadata">The sanitized public metadata associated with the fact.</param>
    public sealed record UiTechnologyFactDto(string StableKey, string Technology, string FactKind, string Name, string? ProjectStableKey, string? Route, IReadOnlyList<string> RelatedStableKeys, IReadOnlyList<string> EvidenceStableKeys, decimal Confidence, bool HasUnknownData, string? UnknownReason, GraphMetadata Metadata);

    /// <summary>
    /// Represents the response context shared by fact-query envelopes.
    /// </summary>
    /// <param name="Scope">The resolved repository and optional solution scope.</param>
    /// <param name="Snapshot">The resolved snapshot metadata.</param>
    /// <param name="Warnings">The safe warnings emitted while building fact output.</param>
    /// <param name="Unknowns">The explicit unknown fields emitted while building fact output.</param>
    public sealed record FactQueryContext(ProjectScopeDto Scope, ProjectSnapshotMetadataDto Snapshot, IReadOnlyList<FactWarningDto> Warnings, IReadOnlyList<FactUnknownDto> Unknowns);

    /// <summary>
    /// Represents a successful or validation-failed data-access fact query result.
    /// </summary>
    public sealed class DataAccessFactResult
    {
        /// <summary>
        /// Initializes a successful data-access fact result.
        /// </summary>
        /// <param name="page">The bounded page of matching data-access facts.</param>
        /// <param name="context">The fact-query envelope context.</param>
        public DataAccessFactResult(PagedQueryResult<DataAccessFactDto> page, FactQueryContext context)
        {
            // Successful results include both page data and context so API mapping can build the common WP014 envelope.
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a failed data-access fact result with validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that explain why the query did not run.</param>
        public DataAccessFactResult(IEnumerable<FactQueryValidationError> validationErrors)
        {
            // Failed results carry deterministic validation errors and no page payload.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        /// <summary>
        /// Gets a value indicating whether the query succeeded and produced a page payload.
        /// </summary>
        public bool Succeeded => ValidationErrors.Count == 0;

        /// <summary>
        /// Gets the bounded page when the query succeeds.
        /// </summary>
        public PagedQueryResult<DataAccessFactDto>? Page { get; }

        /// <summary>
        /// Gets the envelope context when the query succeeds.
        /// </summary>
        public FactQueryContext? Context { get; }

        /// <summary>
        /// Gets the validation errors that explain why the query did not run.
        /// </summary>
        public IReadOnlyList<FactQueryValidationError> ValidationErrors { get; }
    }

    /// <summary>
    /// Represents a successful or validation-failed configuration usage query result.
    /// </summary>
    public sealed class ConfigurationUsageResult
    {
        /// <summary>
        /// Initializes a successful configuration usage result.
        /// </summary>
        /// <param name="page">The bounded page of matching configuration usage facts.</param>
        /// <param name="context">The fact-query envelope context.</param>
        public ConfigurationUsageResult(PagedQueryResult<ConfigurationUsageDto> page, FactQueryContext context)
        {
            // Successful results include both page data and context so API mapping can build the common WP014 envelope.
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a failed configuration usage result with validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that explain why the query did not run.</param>
        public ConfigurationUsageResult(IEnumerable<FactQueryValidationError> validationErrors)
        {
            // Failed results carry deterministic validation errors and no page payload.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        /// <summary>
        /// Gets a value indicating whether the query succeeded and produced a page payload.
        /// </summary>
        public bool Succeeded => ValidationErrors.Count == 0;

        /// <summary>
        /// Gets the bounded page when the query succeeds.
        /// </summary>
        public PagedQueryResult<ConfigurationUsageDto>? Page { get; }

        /// <summary>
        /// Gets the envelope context when the query succeeds.
        /// </summary>
        public FactQueryContext? Context { get; }

        /// <summary>
        /// Gets the validation errors that explain why the query did not run.
        /// </summary>
        public IReadOnlyList<FactQueryValidationError> ValidationErrors { get; }
    }

    /// <summary>
    /// Represents a successful or validation-failed integration fact query result.
    /// </summary>
    public sealed class IntegrationFactResult
    {
        /// <summary>
        /// Initializes a successful integration fact result.
        /// </summary>
        /// <param name="page">The bounded page of matching integration facts.</param>
        /// <param name="context">The fact-query envelope context.</param>
        public IntegrationFactResult(PagedQueryResult<IntegrationFactDto> page, FactQueryContext context)
        {
            // Successful results include both page data and context so API mapping can build the common WP014 envelope.
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a failed integration fact result with validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that explain why the query did not run.</param>
        public IntegrationFactResult(IEnumerable<FactQueryValidationError> validationErrors)
        {
            // Failed results carry deterministic validation errors and no page payload.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        /// <summary>
        /// Gets a value indicating whether the query succeeded and produced a page payload.
        /// </summary>
        public bool Succeeded => ValidationErrors.Count == 0;

        /// <summary>
        /// Gets the bounded page when the query succeeds.
        /// </summary>
        public PagedQueryResult<IntegrationFactDto>? Page { get; }

        /// <summary>
        /// Gets the envelope context when the query succeeds.
        /// </summary>
        public FactQueryContext? Context { get; }

        /// <summary>
        /// Gets the validation errors that explain why the query did not run.
        /// </summary>
        public IReadOnlyList<FactQueryValidationError> ValidationErrors { get; }
    }

    /// <summary>
    /// Represents a successful or validation-failed UI-technology fact query result.
    /// </summary>
    public sealed class UiTechnologyFactResult
    {
        /// <summary>
        /// Initializes a successful UI-technology fact result.
        /// </summary>
        /// <param name="page">The bounded page of matching UI-technology facts.</param>
        /// <param name="context">The fact-query envelope context.</param>
        public UiTechnologyFactResult(PagedQueryResult<UiTechnologyFactDto> page, FactQueryContext context)
        {
            // Successful results include both page data and context so API mapping can build the common WP014 envelope.
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a failed UI-technology fact result with validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that explain why the query did not run.</param>
        public UiTechnologyFactResult(IEnumerable<FactQueryValidationError> validationErrors)
        {
            // Failed results carry deterministic validation errors and no page payload.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        /// <summary>
        /// Gets a value indicating whether the query succeeded and produced a page payload.
        /// </summary>
        public bool Succeeded => ValidationErrors.Count == 0;

        /// <summary>
        /// Gets the bounded page when the query succeeds.
        /// </summary>
        public PagedQueryResult<UiTechnologyFactDto>? Page { get; }

        /// <summary>
        /// Gets the envelope context when the query succeeds.
        /// </summary>
        public FactQueryContext? Context { get; }

        /// <summary>
        /// Gets the validation errors that explain why the query did not run.
        /// </summary>
        public IReadOnlyList<FactQueryValidationError> ValidationErrors { get; }
    }
}
