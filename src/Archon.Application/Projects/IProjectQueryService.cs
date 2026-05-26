using Archon.Application.Rules;

namespace Archon.Application.Projects
{
    /// <summary>
    /// Defines controlled application queries for project catalogue and project detail API surfaces.
    /// </summary>
    public interface IProjectQueryService
    {
        /// <summary>
        /// Lists projects within a bounded repository, solution, and snapshot scope using controlled filters and sorting.
        /// </summary>
        /// <param name="query">The project catalogue query input supplied by the API boundary.</param>
        /// <param name="cancellationToken">The token that can cancel query work before snapshot facts are read.</param>
        /// <returns>A successful project catalogue page or deterministic validation errors.</returns>
        Task<ProjectCatalogueResult> ListProjectsAsync(ProjectCatalogueQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Gets one project detail by exact stable key or unambiguous project display name.
        /// </summary>
        /// <param name="query">The project detail query input supplied by the API boundary.</param>
        /// <param name="cancellationToken">The token that can cancel query work before snapshot facts are read.</param>
        /// <returns>A successful project detail, validation errors, not-found errors, or an ambiguous-name conflict result.</returns>
        Task<ProjectDetailResult> GetProjectAsync(ProjectDetailQuery query, CancellationToken cancellationToken);
    }
}
