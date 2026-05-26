using Archon.Application.Rules;

namespace Archon.Application.Projects
{
    /// <summary>
    /// Represents controlled filters, sorting, paging, and snapshot scope for project catalogue queries.
    /// </summary>
    public sealed class ProjectCatalogueQuery
    {
        /// <summary>
        /// Defines the default catalogue sort field used when a caller omits sorting.
        /// </summary>
        public const string DefaultSort = "name";

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectCatalogueQuery"/> class.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable key that bounds snapshot resolution.</param>
        /// <param name="solutionStableKey">The optional solution stable key that narrows the project scope.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="search">The optional case-insensitive search text for project name, path, and stable key matching.</param>
        /// <param name="language">The optional exact language filter.</param>
        /// <param name="projectType">The optional exact project type filter.</param>
        /// <param name="targetFramework">The optional exact target framework filter.</param>
        /// <param name="applicationType">The optional exact application type filter.</param>
        /// <param name="hasDataAccess">The optional data-access indicator filter.</param>
        /// <param name="hasRisk">The optional risk indicator filter.</param>
        /// <param name="sort">The optional sort field.</param>
        /// <param name="descending">The optional sort direction flag.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        public ProjectCatalogueQuery(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? search,
            string? language,
            string? projectType,
            string? targetFramework,
            string? applicationType,
            bool? hasDataAccess,
            bool? hasRisk,
            string? sort,
            bool? descending,
            int? skip,
            int? take)
        {
            // Construction validates bounded paging and normalizes filter text before any graph facts are inspected.
            Selector = new ProjectSnapshotSelector(repositoryStableKey, solutionStableKey, snapshotStableKey);
            Search = NormalizeOptional(search);
            Language = NormalizeOptional(language);
            ProjectType = NormalizeOptional(projectType);
            TargetFramework = NormalizeOptional(targetFramework);
            ApplicationType = NormalizeOptional(applicationType);
            HasDataAccess = hasDataAccess;
            HasRisk = hasRisk;
            Sort = NormalizeSort(sort);
            Descending = descending.GetValueOrDefault(false);
            Skip = ValidateSkip(skip);
            Take = ValidateTake(take);
        }

        /// <summary>
        /// Gets the repository, solution, and snapshot selector for the catalogue query.
        /// </summary>
        public ProjectSnapshotSelector Selector { get; }

        /// <summary>
        /// Gets the optional case-insensitive search text for project name, path, and stable key matching.
        /// </summary>
        public string? Search { get; }

        /// <summary>
        /// Gets the optional exact language filter.
        /// </summary>
        public string? Language { get; }

        /// <summary>
        /// Gets the optional exact project type filter.
        /// </summary>
        public string? ProjectType { get; }

        /// <summary>
        /// Gets the optional exact target framework filter.
        /// </summary>
        public string? TargetFramework { get; }

        /// <summary>
        /// Gets the optional exact application type filter.
        /// </summary>
        public string? ApplicationType { get; }

        /// <summary>
        /// Gets the optional data-access indicator filter.
        /// </summary>
        public bool? HasDataAccess { get; }

        /// <summary>
        /// Gets the optional risk indicator filter.
        /// </summary>
        public bool? HasRisk { get; }

        /// <summary>
        /// Gets the validated catalogue sort field.
        /// </summary>
        public string Sort { get; }

        /// <summary>
        /// Gets a value indicating whether the selected sort should be applied descending before stable tie-breakers.
        /// </summary>
        public bool Descending { get; }

        /// <summary>
        /// Gets the number of sorted records to skip.
        /// </summary>
        public int Skip { get; }

        /// <summary>
        /// Gets the maximum number of sorted records to return.
        /// </summary>
        public int Take { get; }

        /// <summary>
        /// Normalizes optional text filter values from HTTP query-string input.
        /// </summary>
        /// <param name="value">The optional caller-supplied filter value.</param>
        /// <returns>The trimmed filter value, or <see langword="null"/> when no meaningful value was supplied.</returns>
        private static string? NormalizeOptional(string? value)
        {
            // Blank filters are equivalent to omitted filters so callers do not accidentally request impossible matches.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Normalizes and validates the catalogue sort field.
        /// </summary>
        /// <param name="sort">The optional caller-supplied sort field.</param>
        /// <returns>The validated lower-invariant sort field.</returns>
        private static string NormalizeSort(string? sort)
        {
            // The endpoint supports a closed set of sort fields to keep catalogue ordering deterministic and indexable later.
            string value = string.IsNullOrWhiteSpace(sort) ? DefaultSort : sort.Trim().ToLowerInvariant();
            return value switch
            {
                "name" or "path" or "language" or "projecttype" or "targetframework" or "dependencycount" or "packagecount" or "endpointcount" or "hotlistcount" or "risk" => value,
                _ => throw new ArgumentException("Project catalogue sort must be one of name, path, language, projectType, targetFramework, dependencyCount, packageCount, endpointCount, hotlistCount, or risk.", nameof(sort))
            };
        }

        /// <summary>
        /// Validates the optional skip value used by the catalogue query.
        /// </summary>
        /// <param name="skip">The optional caller-provided skip value.</param>
        /// <returns>The validated non-negative skip value.</returns>
        private static int ValidateSkip(int? skip)
        {
            // A negative skip would make continuation state ambiguous, so it is rejected before graph work starts.
            if (skip.GetValueOrDefault(0) < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(skip), skip, "Skip must be greater than or equal to zero.");
            }

            return skip.GetValueOrDefault(0);
        }

        /// <summary>
        /// Validates the optional take value used by the catalogue query.
        /// </summary>
        /// <param name="take">The optional caller-provided take value.</param>
        /// <returns>The validated page size.</returns>
        private static int ValidateTake(int? take)
        {
            // Bounded catalogue pages protect the API from accidentally returning an entire repository graph in one response.
            int value = take.GetValueOrDefault(QueryPagingOptions.DefaultPageSize);
            if (value < 1 || value > QueryPagingOptions.MaximumPageSize)
            {
                throw new ArgumentOutOfRangeException(nameof(take), take, "Take must be between 1 and 500.");
            }

            return value;
        }
    }
}
