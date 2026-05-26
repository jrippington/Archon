using Archon.Domain.Graph.Metadata;

namespace Archon.Application.Projects
{
    /// <summary>
    /// Represents detailed controlled project information for one selected project.
    /// </summary>
    public sealed class ProjectDetailDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectDetailDto"/> class.
        /// </summary>
        /// <param name="summary">The project catalogue summary for the selected project.</param>
        /// <param name="responsibilities">The inferred responsibilities associated with the project.</param>
        /// <param name="evidence">The safe evidence references associated with the project.</param>
        /// <param name="entryPoints">The stable entry-point names or keys associated with the project.</param>
        /// <param name="references">The stable keys of projects referenced by the selected project.</param>
        /// <param name="dependents">The stable keys of projects that reference the selected project.</param>
        /// <param name="packages">The stable package dependency names or keys associated with the project.</param>
        /// <param name="applicationType">The application type classification when available.</param>
        /// <param name="endpoints">The endpoint names or stable keys owned by the project.</param>
        /// <param name="workers">The worker or hosted-service names owned by the project.</param>
        /// <param name="dataAccess">The data-access indicators, nodes, or relationships owned by the project.</param>
        /// <param name="configurationKeys">The configuration keys used by the project.</param>
        /// <param name="integrations">The integration names, stable keys, or external service references associated with the project.</param>
        /// <param name="hotlistFindings">The hotlist finding stable keys associated with the project.</param>
        /// <param name="scopedGraphSummary">The direct graph summary scoped to the project.</param>
        /// <param name="unknowns">The unknown fields associated with this detail response.</param>
        /// <param name="warnings">The warnings associated with this detail response.</param>
        /// <param name="metadata">The sanitized supplemental project metadata.</param>
        public ProjectDetailDto(
            ProjectCatalogueItemDto summary,
            IEnumerable<ResponsibilitySummaryDto>? responsibilities,
            IEnumerable<EvidenceReferenceDto>? evidence,
            IEnumerable<string>? entryPoints,
            IEnumerable<string>? references,
            IEnumerable<string>? dependents,
            IEnumerable<string>? packages,
            string? applicationType,
            IEnumerable<string>? endpoints,
            IEnumerable<string>? workers,
            IEnumerable<string>? dataAccess,
            IEnumerable<string>? configurationKeys,
            IEnumerable<string>? integrations,
            IEnumerable<string>? hotlistFindings,
            ScopedGraphSummaryDto scopedGraphSummary,
            IEnumerable<ProjectUnknownDto>? unknowns,
            IEnumerable<ProjectWarningDto>? warnings,
            GraphMetadata metadata)
        {
            // Detail responses group bounded sections explicitly so clients can inspect one project without requesting an arbitrary graph traversal.
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
            Responsibilities = responsibilities?.ToArray() ?? [];
            Evidence = evidence?.ToArray() ?? [];
            EntryPoints = NormalizeList(entryPoints);
            References = NormalizeList(references);
            Dependents = NormalizeList(dependents);
            Packages = NormalizeList(packages);
            ApplicationType = NormalizeOptional(applicationType);
            Endpoints = NormalizeList(endpoints);
            Workers = NormalizeList(workers);
            DataAccess = NormalizeList(dataAccess);
            ConfigurationKeys = NormalizeList(configurationKeys);
            Integrations = NormalizeList(integrations);
            HotlistFindings = NormalizeList(hotlistFindings);
            ScopedGraphSummary = scopedGraphSummary ?? throw new ArgumentNullException(nameof(scopedGraphSummary));
            Unknowns = unknowns?.ToArray() ?? [];
            Warnings = warnings?.ToArray() ?? [];
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        /// <summary>
        /// Gets the project catalogue summary for the selected project.
        /// </summary>
        public ProjectCatalogueItemDto Summary { get; }

        /// <summary>
        /// Gets inferred responsibilities associated with the project.
        /// </summary>
        public IReadOnlyList<ResponsibilitySummaryDto> Responsibilities { get; }

        /// <summary>
        /// Gets safe evidence references associated with the project.
        /// </summary>
        public IReadOnlyList<EvidenceReferenceDto> Evidence { get; }

        /// <summary>
        /// Gets stable entry-point names or keys associated with the project.
        /// </summary>
        public IReadOnlyList<string> EntryPoints { get; }

        /// <summary>
        /// Gets stable keys of projects referenced by the selected project.
        /// </summary>
        public IReadOnlyList<string> References { get; }

        /// <summary>
        /// Gets stable keys of projects that reference the selected project.
        /// </summary>
        public IReadOnlyList<string> Dependents { get; }

        /// <summary>
        /// Gets stable package dependency names or keys associated with the project.
        /// </summary>
        public IReadOnlyList<string> Packages { get; }

        /// <summary>
        /// Gets the application type classification when available.
        /// </summary>
        public string? ApplicationType { get; }

        /// <summary>
        /// Gets endpoint names or stable keys owned by the project.
        /// </summary>
        public IReadOnlyList<string> Endpoints { get; }

        /// <summary>
        /// Gets worker or hosted-service names owned by the project.
        /// </summary>
        public IReadOnlyList<string> Workers { get; }

        /// <summary>
        /// Gets data-access indicators, nodes, or relationships owned by the project.
        /// </summary>
        public IReadOnlyList<string> DataAccess { get; }

        /// <summary>
        /// Gets configuration keys used by the project.
        /// </summary>
        public IReadOnlyList<string> ConfigurationKeys { get; }

        /// <summary>
        /// Gets integration names, stable keys, or external service references associated with the project.
        /// </summary>
        public IReadOnlyList<string> Integrations { get; }

        /// <summary>
        /// Gets hotlist finding stable keys associated with the project.
        /// </summary>
        public IReadOnlyList<string> HotlistFindings { get; }

        /// <summary>
        /// Gets the direct graph summary scoped to the project.
        /// </summary>
        public ScopedGraphSummaryDto ScopedGraphSummary { get; }

        /// <summary>
        /// Gets unknown fields associated with this detail response.
        /// </summary>
        public IReadOnlyList<ProjectUnknownDto> Unknowns { get; }

        /// <summary>
        /// Gets warnings associated with this detail response.
        /// </summary>
        public IReadOnlyList<ProjectWarningDto> Warnings { get; }

        /// <summary>
        /// Gets sanitized supplemental project metadata.
        /// </summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Normalizes optional contract string values.
        /// </summary>
        /// <param name="value">The optional candidate string value.</param>
        /// <returns>The trimmed value, or <see langword="null"/> when no meaningful value was supplied.</returns>
        private static string? NormalizeOptional(string? value)
        {
            // Optional strings serialize as null when extraction did not provide meaningful content.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Normalizes string list values into stable distinct order.
        /// </summary>
        /// <param name="values">The optional source values.</param>
        /// <returns>A stable read-only list of distinct values.</returns>
        private static IReadOnlyList<string> NormalizeList(IEnumerable<string>? values)
        {
            // Stable ordering makes detail arrays deterministic across repeated API calls and test runs.
            return values is null
                ? []
                : values.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value.Trim()).Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        }
    }
}
