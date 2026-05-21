using Archon.Api.Extraction.Contracts;
using Archon.Application.Extraction.Requests;
using Archon.Application.Extraction.Runs;
using Archon.Application.Extraction.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Archon.Api.Extraction
{
    /// <summary>
    /// Maps the extraction API endpoints onto an ASP.NET Core endpoint route builder.
    /// </summary>
    public static class ExtractionEndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Lists diagnostic fragments that indicate a message is likely to contain implementation details or sensitive configuration.
        /// </summary>
        private static readonly string[] UnsafeDiagnosticFragments =
        [
            "Password=",
            "Pwd=",
            "User Id=",
            "ConnectionString",
            "connection string",
            "System.",
            "StackTrace",
            " at ",
            "--- End of stack trace"
        ];

        /// <summary>
        /// Maps the WP004 extraction start and status endpoints.
        /// </summary>
        /// <param name="endpoints">The endpoint route builder that receives extraction routes.</param>
        /// <returns>The same endpoint route builder so callers can chain additional route mapping.</returns>
        public static IEndpointRouteBuilder MapArchonExtractionApi(this IEndpointRouteBuilder endpoints)
        {
            // Routes intentionally have no /api prefix because the WP004 contract resolves the public paths directly.
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapGet("/extractions", GetExtractionHistoryAsync)
                .WithName("GetExtractionHistory")
                .WithTags("Extraction")
                .WithSummary("List recent extraction runs")
                .WithDescription("Returns a bounded, newest-first operational history of architecture extraction runs so clients can build dashboards or polling views without reading individual run records one at a time.")
                .Produces<ExtractionRunHistoryResponse>(StatusCodes.Status200OK, "application/json");

            endpoints.MapPost("/extractions", StartExtractionAsync)
                .WithName("StartExtraction")
                .WithTags("Extraction")
                .WithSummary("Start an architecture extraction run")
                .WithDescription("Validates a repository root and submitted solution paths, creates an accepted extraction run, schedules asynchronous processing, and returns the initial run status for follow-up polling.")
                .Accepts<StartExtractionApiRequest>("application/json")
                .Produces<ExtractionRunStatusResponse>(StatusCodes.Status202Accepted)
                .ProducesValidationProblem(StatusCodes.Status400BadRequest);

            endpoints.MapGet("/extractions/{runId}", GetExtractionStatusAsync)
                .WithName("GetExtractionStatus")
                .WithTags("Extraction")
                .WithSummary("Get extraction run status")
                .WithDescription("Returns the current lifecycle state, progress, warnings, errors, and persisted snapshot identity for a previously accepted extraction run.")
                .Produces<ExtractionRunStatusResponse>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status404NotFound);

            return endpoints;
        }

        /// <summary>
        /// Handles GET /extractions by returning recent extraction run summaries for polling and operational views.
        /// </summary>
        /// <param name="limit">The optional maximum number of recent runs to return.</param>
        /// <param name="service">The application service that reads operational run history.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing recent run summaries.</returns>
        private static async Task<IResult> GetExtractionHistoryAsync(
            int? limit,
            StartExtractionApplicationService service,
            CancellationToken cancellationToken)
        {
            // A small bounded default prevents accidental unbounded history reads while avoiding a paging contract in this slice.
            int effectiveLimit = Math.Clamp(limit.GetValueOrDefault(50), 0, 100);
            IReadOnlyList<ExtractionRun> runs = await service.GetRecentRunsAsync(effectiveLimit, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new ExtractionRunHistoryResponse(runs.Select(ToSummaryResponse).ToArray()));
        }

        /// <summary>
        /// Handles POST /extractions by validating, accepting, scheduling, and returning the created run status.
        /// </summary>
        /// <param name="request">The API request body supplied by the caller.</param>
        /// <param name="service">The application service that owns validation, run creation, and scheduling.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An accepted run response or a validation problem response.</returns>
        private static async Task<IResult> StartExtractionAsync(
            StartExtractionApiRequest request,
            StartExtractionApplicationService service,
            CancellationToken cancellationToken)
        {
            // Endpoint logic translates HTTP JSON into the application command and leaves validation rules in the application layer.
            StartExtractionResult result = await service.StartAsync(
                new StartExtractionRequest(
                    request.RepositoryRootDirectory,
                    request.SolutionPaths,
                    request.BranchName,
                    request.CommitSha,
                    request.RequestedBy,
                    request.Metadata),
                cancellationToken).ConfigureAwait(false);

            if (!result.Accepted || result.Run is null)
            {
                return Results.ValidationProblem(ToValidationProblemDictionary(result.ValidationErrors));
            }

            return Results.Accepted($"/extractions/{result.Run.RunId}", ToResponse(result.Run));
        }

        /// <summary>
        /// Handles GET /extractions/{runId} by returning current run state or a controlled not-found response.
        /// </summary>
        /// <param name="runId">The route run identifier text supplied by the caller.</param>
        /// <param name="service">The application service that reads operational run state.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK run-status response or a not-found response.</returns>
        private static async Task<IResult> GetExtractionStatusAsync(
            string runId,
            StartExtractionApplicationService service,
            CancellationToken cancellationToken)
        {
            // Invalid GUID text is treated as not found to avoid leaking parsing exceptions through the HTTP boundary.
            if (!ExtractionRunId.TryParse(runId, out ExtractionRunId parsedRunId))
            {
                return Results.NotFound();
            }

            ExtractionRun? run = await service.GetStatusAsync(parsedRunId, cancellationToken).ConfigureAwait(false);
            return run is null ? Results.NotFound() : Results.Ok(ToResponse(run));
        }

        /// <summary>
        /// Converts an application run model into the public API response contract.
        /// </summary>
        /// <param name="run">The application run state to translate.</param>
        /// <returns>The API response contract for the supplied run.</returns>
        private static ExtractionRunStatusResponse ToResponse(ExtractionRun run)
        {
            // Response shaping keeps API contracts stable if the application run model grows internal fields later.
            return new ExtractionRunStatusResponse(
                run.RunId.ToString(),
                run.Status.ToString(),
                new ExtractionRunRequestSummaryResponse(
                    run.SubmittedRequest.RepositoryRootDirectory,
                    run.SubmittedRequest.SolutionPaths,
                    run.SubmittedRequest.BranchName,
                    run.SubmittedRequest.CommitSha,
                    run.SubmittedRequest.RequestedBy,
                    run.SubmittedRequest.MetadataKeys),
                run.StartedUtc,
                run.CompletedUtc,
                new ExtractionRunProgressResponse(
                    run.Progress.Stage,
                    run.Progress.Message,
                    run.Progress.Percentage,
                    run.Progress.LastUpdatedUtc),
                run.Warnings.Select(warning => new ExtractionRunDiagnosticResponse(warning.Code, SanitizeDiagnosticMessage(warning.Message), warning.Stage, warning.CreatedUtc)).ToArray(),
                run.Errors.Select(error => new ExtractionRunDiagnosticResponse(error.Code, SanitizeDiagnosticMessage(error.Message), error.Stage, error.CreatedUtc)).ToArray(),
                run.SnapshotIdentity);
        }

        /// <summary>
        /// Produces a credential-safe diagnostic message for public extraction API responses.
        /// </summary>
        /// <param name="message">The internal diagnostic message supplied by application or infrastructure code.</param>
        /// <returns>The original message when it appears safe; otherwise a generic redacted diagnostic.</returns>
        private static string SanitizeDiagnosticMessage(string message)
        {
            // The API boundary is the last defensive layer before diagnostics leave the process, so it blocks obvious stack traces,
            // connection-string fragments, and secret-like values even when an inner adapter accidentally returns unsafe text.
            if (string.IsNullOrWhiteSpace(message))
            {
                return "Diagnostic details were not available.";
            }

            if (UnsafeDiagnosticFragments.Any(fragment => message.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                return "Diagnostic details were redacted. Review server logs for details.";
            }

            return message;
        }

        /// <summary>
        /// Converts an application run model into the compact public history response contract.
        /// </summary>
        /// <param name="run">The application run state to summarize.</param>
        /// <returns>The API history summary contract for the supplied run.</returns>
        private static ExtractionRunSummaryResponse ToSummaryResponse(ExtractionRun run)
        {
            // The summary intentionally exposes counts instead of full diagnostics so history stays compact and credential-safe.
            return new ExtractionRunSummaryResponse(
                run.RunId.ToString(),
                run.Status.ToString(),
                run.StartedUtc,
                run.CompletedUtc,
                run.SubmittedRequest.RepositoryRootDirectory,
                run.SubmittedRequest.SolutionPaths.Count,
                run.Warnings.Count,
                run.Errors.Count,
                run.SnapshotIdentity);
        }

        /// <summary>
        /// Converts validation errors into the shape expected by ASP.NET Core validation problem responses.
        /// </summary>
        /// <param name="errors">The validation errors returned by the application layer.</param>
        /// <returns>A validation problem dictionary keyed by stable validation error code.</returns>
        private static Dictionary<string, string[]> ToValidationProblemDictionary(IReadOnlyList<StartExtractionValidationError> errors)
        {
            // Grouping by code gives callers stable keys while preserving one or more human-readable messages for each failure kind.
            return errors
                .GroupBy(error => error.Code, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => SanitizeDiagnosticMessage(error.Message)).ToArray(),
                    StringComparer.Ordinal);
        }
    }
}
