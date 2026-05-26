using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Domain.Graph.Metadata;

namespace Archon.Application.Runtime
{
    /// <summary>
    /// Defines bounded page-size defaults for runtime and worker query endpoints.
    /// </summary>
    public static class RuntimeQueryLimits
    {
        /// <summary>
        /// Defines the default number of runtime rows returned when callers omit a take value.
        /// </summary>
        public const int DefaultTake = 50;

        /// <summary>
        /// Defines the maximum number of runtime rows a single request can return.
        /// </summary>
        public const int MaximumTake = 200;
    }

    /// <summary>
    /// Defines deterministic validation codes for runtime and worker query endpoints.
    /// </summary>
    public static class RuntimeQueryValidationCodes
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
        /// Indicates that the supplied endpoint sort field is not supported.
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
        /// Indicates that a controller or handler identity was required but not supplied.
        /// </summary>
        public const string ControllerOrHandlerIdentityRequired = nameof(ControllerOrHandlerIdentityRequired);

        /// <summary>
        /// Indicates that the requested controller or handler was not found.
        /// </summary>
        public const string ControllerOrHandlerNotFound = nameof(ControllerOrHandlerNotFound);

        /// <summary>
        /// Indicates that the supplied runtime kind filter is not supported.
        /// </summary>
        public const string RuntimeKindUnsupported = nameof(RuntimeKindUnsupported);

        /// <summary>
        /// Indicates that the supplied worker kind filter is not supported.
        /// </summary>
        public const string WorkerKindUnsupported = nameof(WorkerKindUnsupported);
    }

    /// <summary>
    /// Represents one deterministic validation problem produced by runtime queries.
    /// </summary>
    /// <param name="Code">The stable machine-readable validation code.</param>
    /// <param name="Message">The safe developer-facing validation message.</param>
    public sealed record RuntimeQueryValidationError(string Code, string Message);

    /// <summary>
    /// Represents one safe warning emitted by runtime queries when response data is partial or bounded.
    /// </summary>
    /// <param name="Code">The stable machine-readable warning code.</param>
    /// <param name="Message">The safe developer-facing warning message.</param>
    public sealed record RuntimeWarningDto(string Code, string Message);

    /// <summary>
    /// Represents one explicit unknown field emitted by runtime queries when extraction could not prove completeness.
    /// </summary>
    /// <param name="Field">The response field or runtime concept whose value is unknown.</param>
    /// <param name="Reason">The safe reason that explains why the value is unknown.</param>
    public sealed record RuntimeUnknownDto(string Field, string Reason);

    /// <summary>
    /// Describes repository, solution, and snapshot selection for runtime queries.
    /// </summary>
    public sealed class RuntimeSnapshotSelector
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuntimeSnapshotSelector"/> class.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable key that bounds latest/current snapshot resolution.</param>
        /// <param name="solutionStableKey">The optional solution stable key that narrows repository scope.</param>
        /// <param name="snapshotStableKey">The exact snapshot stable key or latest/current selector supplied by the caller.</param>
        public RuntimeSnapshotSelector(string? repositoryStableKey, string? solutionStableKey, string? snapshotStableKey)
        {
            // Runtime query scope follows the existing WP014 selector behavior so latest resolution remains repository-bounded.
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
    /// Represents a controlled endpoint lookup request.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the lookup.</param>
    /// <param name="HttpMethod">The optional exact HTTP method filter.</param>
    /// <param name="Route">The optional route-template search filter.</param>
    /// <param name="ProjectStableKey">The optional exact owning project stable-key filter.</param>
    /// <param name="ControllerOrHandler">The optional controller, handler, action, or method search filter.</param>
    /// <param name="Authorization">The optional authorization attribute search filter.</param>
    /// <param name="Sort">The optional deterministic sort field.</param>
    /// <param name="Descending">A value indicating whether the primary sort should be descending.</param>
    /// <param name="Skip">The number of sorted endpoint records to skip.</param>
    /// <param name="Take">The maximum number of endpoint records to return.</param>
    public sealed record RuntimeEndpointQuery(RuntimeSnapshotSelector Selector, string? HttpMethod, string? Route, string? ProjectStableKey, string? ControllerOrHandler, string? Authorization, string? Sort, bool Descending, int Skip, int Take);

    /// <summary>
    /// Represents a controlled controller or handler lookup request.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the lookup.</param>
    /// <param name="StableKey">The optional exact controller or handler stable key.</param>
    /// <param name="Name">The optional exact controller or handler display name.</param>
    public sealed record ControllerHandlerQuery(RuntimeSnapshotSelector Selector, string? StableKey, string? Name);

    /// <summary>
    /// Represents a controlled runtime entry-point lookup request.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the lookup.</param>
    /// <param name="RuntimeKind">The optional runtime kind filter such as API, Worker, Console, or ServiceHost.</param>
    /// <param name="ProjectStableKey">The optional exact owning project stable-key filter.</param>
    /// <param name="Skip">The number of sorted entry-point records to skip.</param>
    /// <param name="Take">The maximum number of entry-point records to return.</param>
    public sealed record RuntimeEntryPointQuery(RuntimeSnapshotSelector Selector, string? RuntimeKind, string? ProjectStableKey, int Skip, int Take);

    /// <summary>
    /// Represents a controlled worker lookup request.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the lookup.</param>
    /// <param name="ProjectStableKey">The optional exact owning project stable-key filter.</param>
    /// <param name="WorkerKind">The optional worker kind filter such as HostedService, BackgroundService, QueueConsumer, TopicConsumer, or ScheduledJob.</param>
    /// <param name="QueueOrTopic">The optional queue or topic display-name search filter.</param>
    /// <param name="ScheduledJob">The optional scheduled-job display-name search filter.</param>
    /// <param name="Skip">The number of sorted worker records to skip.</param>
    /// <param name="Take">The maximum number of worker records to return.</param>
    public sealed record WorkerQuery(RuntimeSnapshotSelector Selector, string? ProjectStableKey, string? WorkerKind, string? QueueOrTopic, string? ScheduledJob, int Skip, int Take);

    /// <summary>
    /// Represents source-location context for runtime evidence without exposing unbounded source text.
    /// </summary>
    /// <param name="FilePath">The repository-relative file path associated with the source context.</param>
    /// <param name="StartLine">The optional starting line number for the source context.</param>
    /// <param name="EndLine">The optional ending line number for the source context.</param>
    /// <param name="SnippetPreview">The optional bounded snippet preview treated as untrusted display text.</param>
    public sealed record RuntimeSourceContextDto(string? FilePath, int? StartLine, int? EndLine, string? SnippetPreview);

    /// <summary>
    /// Represents a safe evidence reference associated with a runtime query response.
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
    public sealed record RuntimeEvidenceReferenceDto(string StableKey, string EvidenceKind, string FilePath, int? StartLine, int? EndLine, string? SymbolName, string? ContainingSymbol, string? SnippetHash, string? SnippetPreview, decimal Confidence);

    /// <summary>
    /// Represents one runtime HTTP or service endpoint row.
    /// </summary>
    /// <param name="StableKey">The durable public endpoint identity.</param>
    /// <param name="HttpMethod">The HTTP method when the endpoint is HTTP-backed.</param>
    /// <param name="Route">The route template or logical runtime address.</param>
    /// <param name="ProjectStableKey">The owning project stable key when known.</param>
    /// <param name="ControllerName">The controller name associated with the endpoint when extracted.</param>
    /// <param name="HandlerName">The handler or endpoint delegate name associated with the endpoint when extracted.</param>
    /// <param name="ActionName">The controller action name when extracted.</param>
    /// <param name="MethodName">The implementation method name when extracted.</param>
    /// <param name="RequestDto">The request DTO name when extracted.</param>
    /// <param name="ResponseDto">The response DTO name when extracted.</param>
    /// <param name="AuthorizationAttributes">The authorization attributes associated with the endpoint.</param>
    /// <param name="ServicesUsed">The service identities or display names used by the endpoint.</param>
    /// <param name="DataAccess">The data-access indicators used by the endpoint.</param>
    /// <param name="ConfigurationKeys">The safe configuration key names used by the endpoint.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys associated with the endpoint.</param>
    /// <param name="Confidence">The normalized confidence assigned to the endpoint.</param>
    /// <param name="HasUnknownData">A value indicating whether the endpoint carries explicit unknown runtime data.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown endpoint data.</param>
    public sealed record RuntimeEndpointDto(string StableKey, string? HttpMethod, string? Route, string? ProjectStableKey, string? ControllerName, string? HandlerName, string? ActionName, string? MethodName, string? RequestDto, string? ResponseDto, IReadOnlyList<string> AuthorizationAttributes, IReadOnlyList<string> ServicesUsed, IReadOnlyList<string> DataAccess, IReadOnlyList<string> ConfigurationKeys, IReadOnlyList<string> EvidenceStableKeys, decimal Confidence, bool HasUnknownData, string? UnknownReason);

    /// <summary>
    /// Represents one controller or handler detail response.
    /// </summary>
    /// <param name="StableKey">The durable public controller or handler identity.</param>
    /// <param name="Name">The developer-facing controller or handler name.</param>
    /// <param name="Kind">The runtime node kind, usually Controller, Type, or Method.</param>
    /// <param name="ProjectStableKey">The owning project stable key when known.</param>
    /// <param name="QualifiedName">The qualified implementation name when extracted.</param>
    /// <param name="Endpoints">The endpoints declared or handled by this controller or handler.</param>
    /// <param name="ServicesUsed">The service identities or display names used by this controller or handler.</param>
    /// <param name="DataAccess">The data-access indicators used by this controller or handler.</param>
    /// <param name="ConfigurationKeys">The safe configuration key names used by this controller or handler.</param>
    /// <param name="Evidence">The safe evidence references associated with this controller or handler.</param>
    /// <param name="Metadata">The sanitized public metadata associated with this controller or handler.</param>
    /// <param name="Confidence">The normalized confidence assigned to this controller or handler.</param>
    /// <param name="HasUnknownData">A value indicating whether explicit unknown runtime data exists.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown runtime data.</param>
    public sealed record ControllerHandlerDetailDto(string StableKey, string Name, string Kind, string? ProjectStableKey, string? QualifiedName, IReadOnlyList<RuntimeEndpointDto> Endpoints, IReadOnlyList<string> ServicesUsed, IReadOnlyList<string> DataAccess, IReadOnlyList<string> ConfigurationKeys, IReadOnlyList<RuntimeEvidenceReferenceDto> Evidence, GraphMetadata Metadata, decimal Confidence, bool HasUnknownData, string? UnknownReason);

    /// <summary>
    /// Represents one application runtime entry point.
    /// </summary>
    /// <param name="StableKey">The durable public entry-point identity.</param>
    /// <param name="Name">The developer-facing entry-point display name.</param>
    /// <param name="RuntimeKind">The runtime kind such as API, Worker, Console, or ServiceHost.</param>
    /// <param name="ProjectStableKey">The owning project stable key when known.</param>
    /// <param name="ProjectName">The owning project display name when known.</param>
    /// <param name="EntryMethod">The method or bootstrap artifact that starts the runtime.</param>
    /// <param name="HostedServices">The hosted services associated with this entry point.</param>
    /// <param name="EndpointStableKeys">The endpoint stable keys exposed by this entry point.</param>
    /// <param name="ConfigurationKeys">The safe configuration key names used by the entry point.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys associated with this entry point.</param>
    /// <param name="Confidence">The normalized confidence assigned to the entry point.</param>
    /// <param name="HasUnknownData">A value indicating whether explicit unknown entry-point data exists.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown entry-point data.</param>
    public sealed record RuntimeEntryPointDto(string StableKey, string Name, string RuntimeKind, string? ProjectStableKey, string? ProjectName, string? EntryMethod, IReadOnlyList<string> HostedServices, IReadOnlyList<string> EndpointStableKeys, IReadOnlyList<string> ConfigurationKeys, IReadOnlyList<string> EvidenceStableKeys, decimal Confidence, bool HasUnknownData, string? UnknownReason);

    /// <summary>
    /// Represents one queue or topic consumer associated with a worker.
    /// </summary>
    /// <param name="StableKey">The durable public queue or topic identity.</param>
    /// <param name="Name">The queue or topic display name.</param>
    /// <param name="Kind">The runtime target kind, usually Queue or Topic.</param>
    /// <param name="TransportKind">The messaging transport hint when known.</param>
    /// <param name="SubscriptionName">The optional topic subscription name.</param>
    /// <param name="HandlerStableKeys">The handler stable keys associated with the consumer.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys associated with the consumer.</param>
    /// <param name="HasUnknownData">A value indicating whether explicit unknown consumer data exists.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown consumer data.</param>
    public sealed record RuntimeQueueConsumerDto(string StableKey, string Name, string Kind, string? TransportKind, string? SubscriptionName, IReadOnlyList<string> HandlerStableKeys, IReadOnlyList<string> EvidenceStableKeys, bool HasUnknownData, string? UnknownReason);

    /// <summary>
    /// Represents one scheduled job associated with a worker.
    /// </summary>
    /// <param name="StableKey">The durable public scheduled-job identity.</param>
    /// <param name="Name">The scheduled-job display name.</param>
    /// <param name="Schedule">The safe schedule expression or description when extracted.</param>
    /// <param name="HandlerStableKey">The handler stable key associated with the scheduled job.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys associated with the scheduled job.</param>
    /// <param name="HasUnknownData">A value indicating whether explicit unknown scheduled-job data exists.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown scheduled-job data.</param>
    public sealed record RuntimeScheduledJobDto(string StableKey, string Name, string? Schedule, string? HandlerStableKey, IReadOnlyList<string> EvidenceStableKeys, bool HasUnknownData, string? UnknownReason);

    /// <summary>
    /// Represents one worker, hosted service, background service, or non-HTTP runtime consumer row.
    /// </summary>
    /// <param name="StableKey">The durable public worker identity.</param>
    /// <param name="Name">The worker or hosted-service display name.</param>
    /// <param name="WorkerKind">The worker kind such as HostedService, BackgroundService, QueueConsumer, TopicConsumer, or ScheduledJob.</param>
    /// <param name="ProjectStableKey">The owning project stable key when known.</param>
    /// <param name="EntryPointStableKey">The associated runtime entry-point stable key when inferred.</param>
    /// <param name="HostedServices">The hosted-service stable keys associated with this worker.</param>
    /// <param name="BackgroundServices">The background-service stable keys associated with this worker.</param>
    /// <param name="QueueConsumers">The queue or topic consumers associated with this worker.</param>
    /// <param name="ScheduledJobs">The scheduled jobs associated with this worker.</param>
    /// <param name="DataAccess">The data-access indicators used by this worker.</param>
    /// <param name="Integrations">The external integration indicators used by this worker.</param>
    /// <param name="ConfigurationKeys">The safe configuration key names used by this worker.</param>
    /// <param name="Evidence">The safe evidence references associated with this worker.</param>
    /// <param name="Confidence">The normalized confidence assigned to this worker.</param>
    /// <param name="HasUnknownData">A value indicating whether explicit unknown worker data exists.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown worker data.</param>
    /// <param name="Unknowns">The explicit unknown values contributed by this worker and its nested runtime facts.</param>
    public sealed record WorkerDto(string StableKey, string Name, string WorkerKind, string? ProjectStableKey, string? EntryPointStableKey, IReadOnlyList<string> HostedServices, IReadOnlyList<string> BackgroundServices, IReadOnlyList<RuntimeQueueConsumerDto> QueueConsumers, IReadOnlyList<RuntimeScheduledJobDto> ScheduledJobs, IReadOnlyList<string> DataAccess, IReadOnlyList<string> Integrations, IReadOnlyList<string> ConfigurationKeys, IReadOnlyList<RuntimeEvidenceReferenceDto> Evidence, decimal Confidence, bool HasUnknownData, string? UnknownReason, IReadOnlyList<RuntimeUnknownDto> Unknowns);

    /// <summary>
    /// Represents the response context shared by runtime envelopes.
    /// </summary>
    /// <param name="Scope">The resolved repository and optional solution scope.</param>
    /// <param name="Snapshot">The resolved snapshot metadata.</param>
    /// <param name="Warnings">The safe warnings emitted while building runtime output.</param>
    /// <param name="Unknowns">The explicit unknown fields emitted while building runtime output.</param>
    public sealed record RuntimeQueryContext(ProjectScopeDto Scope, ProjectSnapshotMetadataDto Snapshot, IReadOnlyList<RuntimeWarningDto> Warnings, IReadOnlyList<RuntimeUnknownDto> Unknowns);

    /// <summary>
    /// Represents the application result for a runtime endpoint lookup request.
    /// </summary>
    public sealed class RuntimeEndpointResult
    {
        /// <summary>
        /// Initializes a successful runtime endpoint result.
        /// </summary>
        /// <param name="page">The bounded page of matching endpoints.</param>
        /// <param name="context">The runtime envelope context.</param>
        public RuntimeEndpointResult(PagedQueryResult<RuntimeEndpointDto> page, RuntimeQueryContext context)
        {
            // Successful endpoint results include both page data and envelope context for consistent API response mapping.
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a failed runtime endpoint result with validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that explain why endpoint lookup did not run.</param>
        public RuntimeEndpointResult(IEnumerable<RuntimeQueryValidationError> validationErrors)
        {
            // Failed endpoint results carry deterministic validation errors and no page payload.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        /// <summary>
        /// Gets a value indicating whether endpoint lookup succeeded and produced a page payload.
        /// </summary>
        public bool Succeeded => ValidationErrors.Count == 0;

        /// <summary>
        /// Gets the bounded endpoint page when lookup succeeds.
        /// </summary>
        public PagedQueryResult<RuntimeEndpointDto>? Page { get; }

        /// <summary>
        /// Gets the runtime envelope context when lookup succeeds.
        /// </summary>
        public RuntimeQueryContext? Context { get; }

        /// <summary>
        /// Gets the validation errors that explain why endpoint lookup did not run.
        /// </summary>
        public IReadOnlyList<RuntimeQueryValidationError> ValidationErrors { get; }
    }

    /// <summary>
    /// Represents the application result for a controller or handler detail request.
    /// </summary>
    public sealed class ControllerHandlerResult
    {
        /// <summary>
        /// Initializes a successful controller or handler detail result.
        /// </summary>
        /// <param name="detail">The selected controller or handler payload.</param>
        /// <param name="context">The runtime envelope context.</param>
        public ControllerHandlerResult(ControllerHandlerDetailDto detail, RuntimeQueryContext context)
        {
            // Successful detail results include both data and envelope context for consistent API response mapping.
            Detail = detail ?? throw new ArgumentNullException(nameof(detail));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a failed controller or handler detail result with validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that explain why detail lookup did not run.</param>
        public ControllerHandlerResult(IEnumerable<RuntimeQueryValidationError> validationErrors)
        {
            // Failed detail results carry deterministic validation errors and no detail payload.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        /// <summary>
        /// Gets a value indicating whether detail lookup succeeded and produced a payload.
        /// </summary>
        public bool Succeeded => ValidationErrors.Count == 0;

        /// <summary>
        /// Gets the selected controller or handler detail when lookup succeeds.
        /// </summary>
        public ControllerHandlerDetailDto? Detail { get; }

        /// <summary>
        /// Gets the runtime envelope context when lookup succeeds.
        /// </summary>
        public RuntimeQueryContext? Context { get; }

        /// <summary>
        /// Gets the validation errors that explain why detail lookup did not run.
        /// </summary>
        public IReadOnlyList<RuntimeQueryValidationError> ValidationErrors { get; }
    }

    /// <summary>
    /// Represents the application result for a runtime entry-point lookup request.
    /// </summary>
    public sealed class RuntimeEntryPointResult
    {
        /// <summary>
        /// Initializes a successful runtime entry-point result.
        /// </summary>
        /// <param name="page">The bounded page of matching entry points.</param>
        /// <param name="context">The runtime envelope context.</param>
        public RuntimeEntryPointResult(PagedQueryResult<RuntimeEntryPointDto> page, RuntimeQueryContext context)
        {
            // Successful entry-point results include both page data and envelope context for consistent API response mapping.
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a failed runtime entry-point result with validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that explain why entry-point lookup did not run.</param>
        public RuntimeEntryPointResult(IEnumerable<RuntimeQueryValidationError> validationErrors)
        {
            // Failed entry-point results carry deterministic validation errors and no page payload.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        /// <summary>
        /// Gets a value indicating whether entry-point lookup succeeded and produced a page payload.
        /// </summary>
        public bool Succeeded => ValidationErrors.Count == 0;

        /// <summary>
        /// Gets the bounded entry-point page when lookup succeeds.
        /// </summary>
        public PagedQueryResult<RuntimeEntryPointDto>? Page { get; }

        /// <summary>
        /// Gets the runtime envelope context when lookup succeeds.
        /// </summary>
        public RuntimeQueryContext? Context { get; }

        /// <summary>
        /// Gets the validation errors that explain why entry-point lookup did not run.
        /// </summary>
        public IReadOnlyList<RuntimeQueryValidationError> ValidationErrors { get; }
    }

    /// <summary>
    /// Represents the application result for a worker lookup request.
    /// </summary>
    public sealed class WorkerResult
    {
        /// <summary>
        /// Initializes a successful worker result.
        /// </summary>
        /// <param name="page">The bounded page of matching workers.</param>
        /// <param name="context">The runtime envelope context.</param>
        public WorkerResult(PagedQueryResult<WorkerDto> page, RuntimeQueryContext context)
        {
            // Successful worker results include both page data and envelope context for consistent API response mapping.
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a failed worker result with validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that explain why worker lookup did not run.</param>
        public WorkerResult(IEnumerable<RuntimeQueryValidationError> validationErrors)
        {
            // Failed worker results carry deterministic validation errors and no page payload.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        /// <summary>
        /// Gets a value indicating whether worker lookup succeeded and produced a page payload.
        /// </summary>
        public bool Succeeded => ValidationErrors.Count == 0;

        /// <summary>
        /// Gets the bounded worker page when lookup succeeds.
        /// </summary>
        public PagedQueryResult<WorkerDto>? Page { get; }

        /// <summary>
        /// Gets the runtime envelope context when lookup succeeds.
        /// </summary>
        public RuntimeQueryContext? Context { get; }

        /// <summary>
        /// Gets the validation errors that explain why worker lookup did not run.
        /// </summary>
        public IReadOnlyList<RuntimeQueryValidationError> ValidationErrors { get; }
    }
}
