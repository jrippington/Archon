namespace Archon.Application.Projects
{
    /// <summary>
    /// Represents one project row in the controlled project catalogue response.
    /// </summary>
    public sealed class ProjectCatalogueItemDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectCatalogueItemDto"/> class.
        /// </summary>
        /// <param name="stableKey">The stable project key.</param>
        /// <param name="name">The project display name.</param>
        /// <param name="path">The repository-relative project path when available.</param>
        /// <param name="language">The programming or artifact language associated with the project.</param>
        /// <param name="projectType">The project type classification when available.</param>
        /// <param name="targetFramework">The target framework when available.</param>
        /// <param name="isSdkStyle">The SDK-style project status when known.</param>
        /// <param name="dependencyCount">The number of outgoing project dependency/reference edges.</param>
        /// <param name="dependentCount">The number of incoming project dependency/reference edges.</param>
        /// <param name="packageCount">The number of package dependencies associated with the project.</param>
        /// <param name="endpointCount">The number of endpoint nodes owned by the project.</param>
        /// <param name="dataAccessIndicators">The stable data-access indicator names associated with the project.</param>
        /// <param name="hotlistCount">The number of hotlist findings targeting the project.</param>
        /// <param name="riskIndicators">The derived risk indicators for the project.</param>
        /// <param name="evidenceStableKeys">The evidence stable keys associated with the project row.</param>
        /// <param name="confidence">The normalized confidence assigned to the project node.</param>
        /// <param name="hasUnknownData">A value indicating whether the project row contains unknown data.</param>
        /// <param name="unknownReason">The safe reason unknown data is present when available.</param>
        public ProjectCatalogueItemDto(
            string stableKey,
            string name,
            string? path,
            string? language,
            string? projectType,
            string? targetFramework,
            bool? isSdkStyle,
            int dependencyCount,
            int dependentCount,
            int packageCount,
            int endpointCount,
            IEnumerable<string>? dataAccessIndicators,
            int hotlistCount,
            ProjectRiskIndicatorsDto riskIndicators,
            IEnumerable<string>? evidenceStableKeys,
            decimal confidence,
            bool hasUnknownData,
            string? unknownReason)
        {
            // Catalogue rows expose stable identities and aggregate counts so callers can discover projects without raw graph traversal.
            StableKey = RequireText(stableKey, nameof(stableKey));
            Name = RequireText(name, nameof(name));
            Path = NormalizeOptional(path);
            Language = NormalizeOptional(language);
            ProjectType = NormalizeOptional(projectType);
            TargetFramework = NormalizeOptional(targetFramework);
            IsSdkStyle = isSdkStyle;
            DependencyCount = Math.Max(0, dependencyCount);
            DependentCount = Math.Max(0, dependentCount);
            PackageCount = Math.Max(0, packageCount);
            EndpointCount = Math.Max(0, endpointCount);
            DataAccessIndicators = NormalizeList(dataAccessIndicators);
            HotlistCount = Math.Max(0, hotlistCount);
            RiskIndicators = riskIndicators ?? throw new ArgumentNullException(nameof(riskIndicators));
            EvidenceStableKeys = NormalizeList(evidenceStableKeys);
            Confidence = confidence;
            HasUnknownData = hasUnknownData;
            UnknownReason = NormalizeOptional(unknownReason);
        }

        /// <summary>
        /// Gets the stable project key.
        /// </summary>
        public string StableKey { get; }

        /// <summary>
        /// Gets the project display name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the repository-relative project path when available.
        /// </summary>
        public string? Path { get; }

        /// <summary>
        /// Gets the programming or artifact language associated with the project.
        /// </summary>
        public string? Language { get; }

        /// <summary>
        /// Gets the project type classification when available.
        /// </summary>
        public string? ProjectType { get; }

        /// <summary>
        /// Gets the target framework when available.
        /// </summary>
        public string? TargetFramework { get; }

        /// <summary>
        /// Gets the SDK-style project status when known.
        /// </summary>
        public bool? IsSdkStyle { get; }

        /// <summary>
        /// Gets the number of outgoing project dependency/reference edges.
        /// </summary>
        public int DependencyCount { get; }

        /// <summary>
        /// Gets the number of incoming project dependency/reference edges.
        /// </summary>
        public int DependentCount { get; }

        /// <summary>
        /// Gets the number of package dependencies associated with the project.
        /// </summary>
        public int PackageCount { get; }

        /// <summary>
        /// Gets the number of endpoint nodes owned by the project.
        /// </summary>
        public int EndpointCount { get; }

        /// <summary>
        /// Gets stable data-access indicator names associated with the project.
        /// </summary>
        public IReadOnlyList<string> DataAccessIndicators { get; }

        /// <summary>
        /// Gets the number of hotlist findings targeting the project.
        /// </summary>
        public int HotlistCount { get; }

        /// <summary>
        /// Gets derived risk indicators for the project.
        /// </summary>
        public ProjectRiskIndicatorsDto RiskIndicators { get; }

        /// <summary>
        /// Gets evidence stable keys associated with the project row.
        /// </summary>
        public IReadOnlyList<string> EvidenceStableKeys { get; }

        /// <summary>
        /// Gets the normalized confidence assigned to the project node.
        /// </summary>
        public decimal Confidence { get; }

        /// <summary>
        /// Gets a value indicating whether the project row contains unknown data.
        /// </summary>
        public bool HasUnknownData { get; }

        /// <summary>
        /// Gets the safe reason unknown data is present when available.
        /// </summary>
        public string? UnknownReason { get; }

        /// <summary>
        /// Requires a non-empty contract string value.
        /// </summary>
        /// <param name="value">The candidate string value.</param>
        /// <param name="parameterName">The source parameter name used in validation errors.</param>
        /// <returns>The trimmed non-empty string value.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Required catalogue identity fields must never be blank because clients use them for stable linking.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A required project catalogue value is missing.", parameterName) : value.Trim();
        }

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
            // Distinct ordinal ordering keeps aggregate lists deterministic for tests and clients.
            return values is null
                ? []
                : values.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value.Trim()).Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        }
    }
}
