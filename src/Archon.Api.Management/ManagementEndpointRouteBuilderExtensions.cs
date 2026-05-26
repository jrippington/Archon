using Archon.Application.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Archon.Api.Management
{
    /// <summary>
    /// Maps controlled management and operations endpoints for WP014.
    /// </summary>
    public static class ManagementEndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps repository, solution, metadata, lifecycle, retention, run-history, rule, maintenance, health, and readiness routes.
        /// </summary>
        /// <param name="endpoints">The endpoint route builder that receives management routes.</param>
        /// <returns>The same endpoint route builder so callers can chain additional route mapping.</returns>
        public static IEndpointRouteBuilder MapArchonManagementApi(this IEndpointRouteBuilder endpoints)
        {
            // Routes use explicit nouns and avoid arbitrary mutation surfaces such as raw Cypher, shell, SQL, or filesystem commands.
            ArgumentNullException.ThrowIfNull(endpoints);

            RouteGroupBuilder group = endpoints.MapGroup("/management")
                .WithTags("Management");

            group.MapPost("/repositories", RegisterRepositoryAsync)
                .WithName("RegisterRepository")
                .WithSummary("Register repository metadata")
                .WithDescription("Registers repository identity and root metadata without starting extraction or exposing arbitrary graph mutation.")
                .Produces<RepositoryRegistrationResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status500InternalServerError);

            group.MapPost("/solutions", RegisterSolutionAsync)
                .WithName("RegisterSolution")
                .WithSummary("Register solution metadata")
                .WithDescription("Associates one repository-relative solution path with a registered repository context using extraction-compatible path-shape validation.")
                .Produces<SolutionRegistrationResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status500InternalServerError);

            group.MapPatch("/metadata", UpdateMetadataAsync)
                .WithName("UpdateManagementMetadata")
                .WithSummary("Update approved management metadata")
                .WithDescription("Applies only approved metadata fields to repository, solution, or snapshot targets and rejects arbitrary graph property updates.")
                .Produces<MetadataUpdateResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status500InternalServerError);

            group.MapGet("/snapshots", ListSnapshotsAsync)
                .WithName("ListSnapshotLifecycle")
                .WithSummary("List snapshot lifecycle rows")
                .WithDescription("Lists snapshot lifecycle metadata by repository, solution, status, date range, and commit filters using stable public identities.")
                .Produces<SnapshotLifecycleResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status500InternalServerError);

            group.MapPost("/retention", ApplyRetentionAsync)
                .WithName("ApplySnapshotRetention")
                .WithSummary("Validate or apply snapshot retention")
                .WithDescription("Evaluates retention boundaries inside the requested snapshot lifecycle scope and never deletes outside that controlled scope.")
                .Produces<RetentionResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status500InternalServerError);

            group.MapGet("/runs", ListExtractionRunsAsync)
                .WithName("ListExtractionRuns")
                .WithSummary("List extraction run history")
                .WithDescription("Returns bounded extraction run history with status, timestamps, summary counts, warnings, errors, and produced snapshot identity when available.")
                .Produces<ExtractionRunHistoryResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status500InternalServerError);

            group.MapPut("/rules/enablement", SetRuleEnablementAsync)
                .WithName("SetRuleEnablement")
                .WithSummary("Set controlled rule enablement")
                .WithDescription("Records enablement state for one rule code and version without editing rule definition files on disk.")
                .Produces<RuleEnablementResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status500InternalServerError);

            group.MapPost("/maintenance", RunMaintenanceAsync)
                .WithName("RunControlledMaintenance")
                .WithSummary("Run a controlled maintenance operation")
                .WithDescription("Runs or previews an allowlisted maintenance operation and rejects arbitrary database, shell, filesystem, or code mutation commands.")
                .Produces<MaintenanceResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status500InternalServerError);

            endpoints.MapGet("/health", GetHealthAsync)
                .WithName("GetManagementHealth")
                .WithTags("Operations")
                .WithSummary("Get management module health")
                .WithDescription("Returns local health status for development and monitoring without secrets or sensitive infrastructure details.")
                .Produces<ManagementHealthResponse>(StatusCodes.Status200OK, "application/json")
                .Produces(StatusCodes.Status500InternalServerError);

            endpoints.MapGet("/ready", GetReadinessAsync)
                .WithName("GetManagementReadiness")
                .WithTags("Operations")
                .WithSummary("Get management module readiness")
                .WithDescription("Returns sanitized readiness checks for required query dependencies, including graph lifecycle and rule catalog availability where applicable.")
                .Produces<ManagementReadinessResponse>(StatusCodes.Status200OK, "application/json")
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }

        /// <summary>
        /// Handles repository registration requests and maps validation failures to safe problem details.
        /// </summary>
        /// <param name="request">The repository registration request body.</param>
        /// <param name="service">The management application service.</param>
        /// <param name="logger">The route logger used for safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token for the HTTP request.</param>
        /// <returns>The accepted registration response or validation problem.</returns>
        private static async Task<IResult> RegisterRepositoryAsync(RegisterRepositoryRequest request, IManagementOperationsService service, ILogger<ArchonApiManagementProjectMarker> logger, CancellationToken cancellationToken)
        {
            // The handler delegates all validation and state changes to the application service.
            return await ExecuteAsync(() => service.RegisterRepositoryAsync(request, cancellationToken), logger).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles solution registration requests and maps validation failures to safe problem details.
        /// </summary>
        /// <param name="request">The solution registration request body.</param>
        /// <param name="service">The management application service.</param>
        /// <param name="logger">The route logger used for safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token for the HTTP request.</param>
        /// <returns>The accepted registration response or validation problem.</returns>
        private static async Task<IResult> RegisterSolutionAsync(RegisterSolutionRequest request, IManagementOperationsService service, ILogger<ArchonApiManagementProjectMarker> logger, CancellationToken cancellationToken)
        {
            // The handler keeps path and repository validation in the service so route code remains transport-focused.
            return await ExecuteAsync(() => service.RegisterSolutionAsync(request, cancellationToken), logger).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles constrained metadata updates and maps validation failures to safe problem details.
        /// </summary>
        /// <param name="request">The metadata update request body.</param>
        /// <param name="service">The management application service.</param>
        /// <param name="logger">The route logger used for safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token for the HTTP request.</param>
        /// <returns>The accepted metadata response or validation problem.</returns>
        private static async Task<IResult> UpdateMetadataAsync(UpdateMetadataRequest request, IManagementOperationsService service, ILogger<ArchonApiManagementProjectMarker> logger, CancellationToken cancellationToken)
        {
            // Metadata updates remain allowlisted in the application layer and cannot carry arbitrary graph properties.
            return await ExecuteAsync(() => service.UpdateMetadataAsync(request, cancellationToken), logger).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles bounded snapshot lifecycle queries.
        /// </summary>
        /// <param name="repositoryStableKey">The optional repository stable-key filter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key filter.</param>
        /// <param name="status">The optional lifecycle status filter.</param>
        /// <param name="fromUtc">The optional inclusive start timestamp filter.</param>
        /// <param name="toUtc">The optional inclusive end timestamp filter.</param>
        /// <param name="commitSha">The optional commit SHA filter.</param>
        /// <param name="take">The optional result-size bound.</param>
        /// <param name="service">The management application service.</param>
        /// <param name="logger">The route logger used for safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token for the HTTP request.</param>
        /// <returns>The bounded lifecycle response or validation problem.</returns>
        private static async Task<IResult> ListSnapshotsAsync(string? repositoryStableKey, string? solutionStableKey, string? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? commitSha, int? take, IManagementOperationsService service, ILogger<ArchonApiManagementProjectMarker> logger, CancellationToken cancellationToken)
        {
            // Query-string binding is transformed into an application query object before validation.
            SnapshotLifecycleQuery query = new(repositoryStableKey, solutionStableKey, status, fromUtc, toUtc, commitSha, take);
            return await ExecuteAsync(() => service.ListSnapshotsAsync(query, cancellationToken), logger).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles retention validation and execution requests.
        /// </summary>
        /// <param name="request">The retention request body.</param>
        /// <param name="service">The management application service.</param>
        /// <param name="logger">The route logger used for safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token for the HTTP request.</param>
        /// <returns>The retention response or validation problem.</returns>
        private static async Task<IResult> ApplyRetentionAsync(RetentionRequest request, IManagementOperationsService service, ILogger<ArchonApiManagementProjectMarker> logger, CancellationToken cancellationToken)
        {
            // Retention boundaries are validated before the service reports candidates or deleted identities.
            return await ExecuteAsync(() => service.ApplyRetentionAsync(request, cancellationToken), logger).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles extraction run-history queries.
        /// </summary>
        /// <param name="take">The optional result-size bound.</param>
        /// <param name="status">The optional status filter.</param>
        /// <param name="service">The management application service.</param>
        /// <param name="logger">The route logger used for safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token for the HTTP request.</param>
        /// <returns>The bounded run-history response or validation problem.</returns>
        private static async Task<IResult> ListExtractionRunsAsync(int? take, string? status, IManagementOperationsService service, ILogger<ArchonApiManagementProjectMarker> logger, CancellationToken cancellationToken)
        {
            // The query object lets the service own validation and default limit semantics.
            ExtractionRunHistoryQuery query = new(take, status);
            return await ExecuteAsync(() => service.ListExtractionRunsAsync(query, cancellationToken), logger).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles controlled rule enablement updates.
        /// </summary>
        /// <param name="request">The rule enablement request body.</param>
        /// <param name="service">The management application service.</param>
        /// <param name="logger">The route logger used for safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token for the HTTP request.</param>
        /// <returns>The rule enablement response or validation problem.</returns>
        private static async Task<IResult> SetRuleEnablementAsync(RuleEnablementRequest request, IManagementOperationsService service, ILogger<ArchonApiManagementProjectMarker> logger, CancellationToken cancellationToken)
        {
            // Enablement overlays are projected back to callers without exposing or modifying rule definition files.
            return await ExecuteAsync(() => service.SetRuleEnablementAsync(request, cancellationToken), logger).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles allowlisted maintenance operation requests.
        /// </summary>
        /// <param name="request">The maintenance request body.</param>
        /// <param name="service">The management application service.</param>
        /// <param name="logger">The route logger used for safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token for the HTTP request.</param>
        /// <returns>The maintenance response or validation problem.</returns>
        private static async Task<IResult> RunMaintenanceAsync(MaintenanceRequest request, IManagementOperationsService service, ILogger<ArchonApiManagementProjectMarker> logger, CancellationToken cancellationToken)
        {
            // The service enforces the maintenance allowlist and rejects arbitrary operational commands.
            return await ExecuteAsync(() => service.RunMaintenanceAsync(request, cancellationToken), logger).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles management health requests.
        /// </summary>
        /// <param name="service">The management application service.</param>
        /// <param name="logger">The route logger used for safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token for the HTTP request.</param>
        /// <returns>The safe health response.</returns>
        private static async Task<IResult> GetHealthAsync(IManagementOperationsService service, ILogger<ArchonApiManagementProjectMarker> logger, CancellationToken cancellationToken)
        {
            // Health exceptions are converted to a generic status so sensitive details stay out of responses.
            try
            {
                ManagementHealthResponse response = await service.GetHealthAsync(cancellationToken).ConfigureAwait(false);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Management health request failed.");
                return Results.Problem("Management health check failed.", statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles management readiness requests.
        /// </summary>
        /// <param name="service">The management application service.</param>
        /// <param name="logger">The route logger used for safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token for the HTTP request.</param>
        /// <returns>The safe readiness response.</returns>
        private static async Task<IResult> GetReadinessAsync(IManagementOperationsService service, ILogger<ArchonApiManagementProjectMarker> logger, CancellationToken cancellationToken)
        {
            // Readiness responses intentionally include only public dependency names and sanitized states.
            try
            {
                ManagementReadinessResponse response = await service.GetReadinessAsync(cancellationToken).ConfigureAwait(false);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Management readiness request failed.");
                return Results.Problem("Management readiness check failed.", statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Executes an application operation and translates validation or unexpected failures into safe HTTP results.
        /// </summary>
        /// <typeparam name="TData">The successful operation data type.</typeparam>
        /// <param name="operation">The application operation to execute.</param>
        /// <param name="logger">The route logger used for safe endpoint diagnostics.</param>
        /// <returns>The successful data response, validation problem, or safe server problem.</returns>
        private static async Task<IResult> ExecuteAsync<TData>(Func<Task<ManagementOperationResult<TData>>> operation, ILogger<ArchonApiManagementProjectMarker> logger)
        {
            // Centralized error shaping keeps every management route from leaking exception types or infrastructure details.
            try
            {
                ManagementOperationResult<TData> result = await operation().ConfigureAwait(false);
                if (!result.IsSuccess || result.Data is null)
                {
                    return ManagementValidationProblemFactory.Create(result.Errors);
                }

                return Results.Ok(result.Data);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Controlled management endpoint failed.");
                return Results.Problem("Controlled management operation failed.", statusCode: StatusCodes.Status500InternalServerError);
            }
        }
    }
}
