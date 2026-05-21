namespace Archon.Extractors.Projects.Classification
{
    /// <summary>
    /// Represents the deterministic application type decision produced for one project file.
    /// </summary>
    /// <param name="ApplicationType">The supported application category value written to project graph metadata.</param>
    /// <param name="ConfidenceLabel">The human-readable confidence band for the classification decision.</param>
    /// <param name="ConfidenceValue">The normalized confidence value associated with <paramref name="ConfidenceLabel" />.</param>
    /// <param name="Evidence">The ordered evidence descriptions that explain why the classifier selected the category.</param>
    /// <param name="Contradictions">The ordered contradictory high-confidence indicators that caused an Unknown decision, when present.</param>
    /// <param name="IsUnknown">A value indicating whether the result intentionally preserves unknown application type state.</param>
    /// <param name="UnknownReason">The reason the application type remains unknown when <paramref name="IsUnknown" /> is <see langword="true" />.</param>
    internal sealed record ApplicationTypeClassification(
        string ApplicationType,
        string ConfidenceLabel,
        decimal ConfidenceValue,
        IReadOnlyList<string> Evidence,
        IReadOnlyList<string> Contradictions,
        bool IsUnknown,
        string? UnknownReason)
    {
        /// <summary>
        /// Gets the canonical Unknown classification used when evidence is absent or unsafe to interpret.
        /// </summary>
        /// <param name="reason">The reason the classifier could not select a supported application type.</param>
        /// <returns>An Unknown classification with explicit unknown-state metadata.</returns>
        internal static ApplicationTypeClassification Unknown(string reason)
        {
            // Unknown is an intentional result, not a failure, because downstream behavior must not rely on unsupported guesses.
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            return new ApplicationTypeClassification(
                "Unknown",
                "Low",
                0.25m,
                [],
                [],
                IsUnknown: true,
                reason.Trim());
        }

        /// <summary>
        /// Creates an Unknown classification for contradictory high-confidence indicators.
        /// </summary>
        /// <param name="evidence">The high-confidence indicators observed before contradiction resolution.</param>
        /// <returns>An Unknown classification that records the conflicting indicators for troubleshooting.</returns>
        internal static ApplicationTypeClassification Contradictory(IReadOnlyList<string> evidence)
        {
            // Recording contradictions explains why a seemingly obvious project was not assigned either competing category.
            ArgumentNullException.ThrowIfNull(evidence);
            return new ApplicationTypeClassification(
                "Unknown",
                "Low",
                0.25m,
                [],
                evidence.Order(StringComparer.Ordinal).ToArray(),
                IsUnknown: true,
                "Contradictory high-confidence indicators were found.");
        }
    }
}
