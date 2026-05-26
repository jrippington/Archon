namespace Archon.Application.Projects
{
    /// <summary>
    /// Represents the application-layer outcome of a project detail query.
    /// </summary>
    public sealed class ProjectDetailResult
    {
        /// <summary>
        /// Initializes a successful project detail result.
        /// </summary>
        /// <param name="detail">The selected project detail payload.</param>
        /// <param name="context">The scope and snapshot context for the result.</param>
        public ProjectDetailResult(ProjectDetailDto detail, ProjectQueryContext context)
        {
            // Successful detail results always carry both the selected project and envelope metadata for API mapping.
            Detail = detail ?? throw new ArgumentNullException(nameof(detail));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
            DisambiguationOptions = [];
        }

        /// <summary>
        /// Initializes a failed project detail result with validation or conflict errors.
        /// </summary>
        /// <param name="validationErrors">The validation or conflict errors that prevented detail creation.</param>
        /// <param name="disambiguationOptions">The optional stable project options returned for ambiguous name lookups.</param>
        public ProjectDetailResult(IEnumerable<ProjectQueryValidationError> validationErrors, IEnumerable<ProjectCatalogueItemDto>? disambiguationOptions = null)
        {
            // Failed results may include safe stable-key options when a project name maps to multiple projects.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
            DisambiguationOptions = disambiguationOptions?.ToArray() ?? [];
        }

        /// <summary>
        /// Gets a value indicating whether the detail query succeeded.
        /// </summary>
        public bool Succeeded
        {
            get
            {
                // A result succeeds only when no validation or conflict errors were recorded.
                return ValidationErrors.Count == 0;
            }
        }

        /// <summary>
        /// Gets the selected project detail payload for successful results.
        /// </summary>
        public ProjectDetailDto? Detail { get; }

        /// <summary>
        /// Gets the scope and snapshot context for successful results.
        /// </summary>
        public ProjectQueryContext? Context { get; }

        /// <summary>
        /// Gets validation or conflict errors for failed results.
        /// </summary>
        public IReadOnlyList<ProjectQueryValidationError> ValidationErrors { get; }

        /// <summary>
        /// Gets safe stable-key options returned for ambiguous project-name lookups.
        /// </summary>
        public IReadOnlyList<ProjectCatalogueItemDto> DisambiguationOptions { get; }
    }
}
