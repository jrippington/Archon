using Archon.Application.Rules;

namespace Archon.Application.Projects
{
    /// <summary>
    /// Represents the application-layer outcome of a project catalogue query.
    /// </summary>
    public sealed class ProjectCatalogueResult
    {
        /// <summary>
        /// Initializes a successful project catalogue result.
        /// </summary>
        /// <param name="page">The bounded page of project catalogue items.</param>
        /// <param name="context">The scope and snapshot context for the result.</param>
        public ProjectCatalogueResult(PagedQueryResult<ProjectCatalogueItemDto> page, ProjectQueryContext context)
        {
            // Successful catalogue results always carry both items and envelope metadata for API mapping.
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a failed project catalogue result with validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that prevented catalogue creation.</param>
        public ProjectCatalogueResult(IEnumerable<ProjectQueryValidationError> validationErrors)
        {
            // Failed results carry only safe validation information and leave data sections unset.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        /// <summary>
        /// Gets a value indicating whether the catalogue query succeeded.
        /// </summary>
        public bool Succeeded
        {
            get
            {
                // A result succeeds only when no validation errors were recorded.
                return ValidationErrors.Count == 0;
            }
        }

        /// <summary>
        /// Gets the bounded page of project catalogue items for successful results.
        /// </summary>
        public PagedQueryResult<ProjectCatalogueItemDto>? Page { get; }

        /// <summary>
        /// Gets the scope and snapshot context for successful results.
        /// </summary>
        public ProjectQueryContext? Context { get; }

        /// <summary>
        /// Gets validation errors for failed results.
        /// </summary>
        public IReadOnlyList<ProjectQueryValidationError> ValidationErrors { get; }
    }
}
