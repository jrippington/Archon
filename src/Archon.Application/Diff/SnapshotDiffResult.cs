namespace Archon.Application.Diff
{
    /// <summary>
    /// Represents the complete application-layer result of a snapshot diff request.
    /// </summary>
    public sealed class SnapshotDiffResult
    {
        /// <summary>
        /// Initializes a successful snapshot diff result.
        /// </summary>
        /// <param name="currentSnapshotStableKey">The current snapshot stable key.</param>
        /// <param name="previousSnapshotStableKey">The previous snapshot stable key.</param>
        /// <param name="comparisonScope">The repository or compatibility scope used for comparison.</param>
        /// <param name="summaries">The per-domain summary counts.</param>
        /// <param name="items">The returned bounded detail rows.</param>
        /// <param name="truncation">The truncation and continuation metadata.</param>
        public SnapshotDiffResult(
            string currentSnapshotStableKey,
            string previousSnapshotStableKey,
            string comparisonScope,
            IEnumerable<SnapshotDiffSummaryDto> summaries,
            IEnumerable<SnapshotDiffItemDto> items,
            SnapshotDiffTruncationDto truncation)
        {
            // Successful results carry both summaries and bounded details so callers can show drift counts even when rows are paged.
            CurrentSnapshotStableKey = string.IsNullOrWhiteSpace(currentSnapshotStableKey) ? throw new ArgumentException("A current snapshot stable key is required.", nameof(currentSnapshotStableKey)) : currentSnapshotStableKey.Trim();
            PreviousSnapshotStableKey = string.IsNullOrWhiteSpace(previousSnapshotStableKey) ? throw new ArgumentException("A previous snapshot stable key is required.", nameof(previousSnapshotStableKey)) : previousSnapshotStableKey.Trim();
            ComparisonScope = string.IsNullOrWhiteSpace(comparisonScope) ? "Unknown" : comparisonScope.Trim();
            Summaries = summaries?.ToArray() ?? throw new ArgumentNullException(nameof(summaries));
            Items = items?.ToArray() ?? throw new ArgumentNullException(nameof(items));
            Truncation = truncation ?? throw new ArgumentNullException(nameof(truncation));
            ValidationErrors = [];
            Succeeded = true;
        }

        /// <summary>
        /// Initializes a failed snapshot diff result containing validation errors.
        /// </summary>
        /// <param name="currentSnapshotStableKey">The requested current snapshot stable key when supplied.</param>
        /// <param name="previousSnapshotStableKey">The requested previous snapshot stable key when supplied.</param>
        /// <param name="validationErrors">The deterministic validation errors.</param>
        public SnapshotDiffResult(string? currentSnapshotStableKey, string? previousSnapshotStableKey, IEnumerable<SnapshotDiffValidationError> validationErrors)
        {
            // Validation failures stay in the application result so API hosts can return consistent problem details without parsing exceptions.
            CurrentSnapshotStableKey = currentSnapshotStableKey?.Trim() ?? string.Empty;
            PreviousSnapshotStableKey = previousSnapshotStableKey?.Trim() ?? string.Empty;
            ComparisonScope = "Invalid";
            Summaries = [];
            Items = [];
            Truncation = new SnapshotDiffTruncationDto(false, 0, 0, 0, 1);
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
            Succeeded = false;
        }

        /// <summary>
        /// Gets a value indicating whether the diff request succeeded.
        /// </summary>
        public bool Succeeded { get; }

        /// <summary>
        /// Gets the current snapshot stable key for the comparison.
        /// </summary>
        public string CurrentSnapshotStableKey { get; }

        /// <summary>
        /// Gets the previous snapshot stable key for the comparison.
        /// </summary>
        public string PreviousSnapshotStableKey { get; }

        /// <summary>
        /// Gets the repository or explicit compatibility scope used for the comparison.
        /// </summary>
        public string ComparisonScope { get; }

        /// <summary>
        /// Gets per-domain summary counts for the comparison.
        /// </summary>
        public IReadOnlyList<SnapshotDiffSummaryDto> Summaries { get; }

        /// <summary>
        /// Gets the bounded detail rows returned for the comparison.
        /// </summary>
        public IReadOnlyList<SnapshotDiffItemDto> Items { get; }

        /// <summary>
        /// Gets truncation and continuation metadata for the returned detail rows.
        /// </summary>
        public SnapshotDiffTruncationDto Truncation { get; }

        /// <summary>
        /// Gets deterministic validation errors when <see cref="Succeeded"/> is false.
        /// </summary>
        public IReadOnlyList<SnapshotDiffValidationError> ValidationErrors { get; }
    }
}
