using Archon.Application.Projects;

namespace Archon.Api.Query.Contracts
{
    /// <summary>
    /// Represents a safe conflict response for ambiguous project-name lookup requests.
    /// </summary>
    public sealed record ProjectDisambiguationResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectDisambiguationResponse"/> record.
        /// </summary>
        /// <param name="code">The stable conflict code.</param>
        /// <param name="message">The safe human-readable conflict message.</param>
        /// <param name="options">The stable project options that can disambiguate a follow-up request.</param>
        /// <param name="traceId">The request trace identifier.</param>
        public ProjectDisambiguationResponse(string code, string message, IEnumerable<ProjectCatalogueItemDto> options, string? traceId)
        {
            // Conflict responses include only public catalogue rows so callers can retry by stable key without seeing persistence details.
            Code = string.IsNullOrWhiteSpace(code) ? "ProjectNameAmbiguous" : code.Trim();
            Message = string.IsNullOrWhiteSpace(message) ? "Project name matched multiple projects." : message.Trim();
            Options = options?.ToArray() ?? [];
            TraceId = string.IsNullOrWhiteSpace(traceId) ? null : traceId.Trim();
        }

        /// <summary>
        /// Gets the stable conflict code.
        /// </summary>
        public string Code { get; init; }

        /// <summary>
        /// Gets the safe human-readable conflict message.
        /// </summary>
        public string Message { get; init; }

        /// <summary>
        /// Gets the stable project options that can disambiguate a follow-up request.
        /// </summary>
        public IReadOnlyList<ProjectCatalogueItemDto> Options { get; init; }

        /// <summary>
        /// Gets the request trace identifier.
        /// </summary>
        public string? TraceId { get; init; }
    }
}
