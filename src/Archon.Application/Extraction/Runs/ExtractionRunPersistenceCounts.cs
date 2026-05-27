namespace Archon.Application.Extraction.Runs
{
    /// <summary>
    /// Represents persistence-specific count values that explain the volume handled by an extraction run's snapshot persistence step.
    /// </summary>
    /// <remarks>
    /// Known empty collections use zero while measurements that the writer cannot accurately or cheaply provide remain <see langword="null"/>.
    /// </remarks>
    public sealed record ExtractionRunPersistenceCounts
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExtractionRunPersistenceCounts"/> record.
        /// </summary>
        /// <param name="RepositoryCount">The number of repositories included in the persistence operation.</param>
        /// <param name="SolutionCount">The number of solutions included in the persistence operation.</param>
        /// <param name="ProjectCount">The number of projects included in the persistence operation.</param>
        /// <param name="FileCount">The number of file or document records included when the writer can identify them.</param>
        /// <param name="NodeCount">The number of generalized architecture nodes included in the persistence operation.</param>
        /// <param name="RelationshipCount">The number of generalized architecture relationships included in the persistence operation.</param>
        /// <param name="EvidenceCount">The number of evidence records included in the persistence operation.</param>
        /// <param name="FindingCount">The number of finding records included in the persistence operation.</param>
        /// <param name="WarningCount">The number of snapshot or persistence warnings associated with the persistence operation.</param>
        /// <param name="ErrorCount">The number of snapshot or persistence errors associated with the persistence operation.</param>
        /// <param name="MetricCount">The number of metric records included in the persistence operation.</param>
        /// <param name="GeneratedSummaryCount">The number of generated summary records included in the persistence operation.</param>
        /// <param name="MetadataEntryCount">The optional number of metadata entries included when this can be measured accurately.</param>
        /// <param name="PersistenceOperationCount">The optional number of persistence operations executed or planned when known.</param>
        /// <param name="PersistenceBatchCount">The optional number of persistence batches executed or planned when known.</param>
        /// <param name="SerializedPayloadBytes">The optional serialized payload size in bytes when materialization produced a measurable payload.</param>
        public ExtractionRunPersistenceCounts(
            int RepositoryCount,
            int SolutionCount,
            int ProjectCount,
            int FileCount,
            int NodeCount,
            int RelationshipCount,
            int EvidenceCount,
            int FindingCount,
            int WarningCount,
            int ErrorCount,
            int MetricCount,
            int GeneratedSummaryCount,
            int? MetadataEntryCount,
            int? PersistenceOperationCount,
            int? PersistenceBatchCount,
            long? SerializedPayloadBytes)
        {
            // Count normalization prevents malformed adapter output from producing negative values in public status diagnostics.
            this.RepositoryCount = Math.Max(0, RepositoryCount);
            this.SolutionCount = Math.Max(0, SolutionCount);
            this.ProjectCount = Math.Max(0, ProjectCount);
            this.FileCount = Math.Max(0, FileCount);
            this.NodeCount = Math.Max(0, NodeCount);
            this.RelationshipCount = Math.Max(0, RelationshipCount);
            this.EvidenceCount = Math.Max(0, EvidenceCount);
            this.FindingCount = Math.Max(0, FindingCount);
            this.WarningCount = Math.Max(0, WarningCount);
            this.ErrorCount = Math.Max(0, ErrorCount);
            this.MetricCount = Math.Max(0, MetricCount);
            this.GeneratedSummaryCount = Math.Max(0, GeneratedSummaryCount);
            this.MetadataEntryCount = NormalizeOptionalCount(MetadataEntryCount);
            this.PersistenceOperationCount = NormalizeOptionalCount(PersistenceOperationCount);
            this.PersistenceBatchCount = NormalizeOptionalCount(PersistenceBatchCount);
            this.SerializedPayloadBytes = SerializedPayloadBytes.HasValue ? Math.Max(0, SerializedPayloadBytes.Value) : null;
        }

        /// <summary>
        /// Gets the number of repositories included in the persistence operation.
        /// </summary>
        public int RepositoryCount { get; }

        /// <summary>
        /// Gets the number of solutions included in the persistence operation.
        /// </summary>
        public int SolutionCount { get; }

        /// <summary>
        /// Gets the number of projects included in the persistence operation.
        /// </summary>
        public int ProjectCount { get; }

        /// <summary>
        /// Gets the number of file or document records included when the writer can identify them.
        /// </summary>
        public int FileCount { get; }

        /// <summary>
        /// Gets the number of generalized architecture nodes included in the persistence operation.
        /// </summary>
        public int NodeCount { get; }

        /// <summary>
        /// Gets the number of generalized architecture relationships included in the persistence operation.
        /// </summary>
        public int RelationshipCount { get; }

        /// <summary>
        /// Gets the number of evidence records included in the persistence operation.
        /// </summary>
        public int EvidenceCount { get; }

        /// <summary>
        /// Gets the number of finding records included in the persistence operation.
        /// </summary>
        public int FindingCount { get; }

        /// <summary>
        /// Gets the number of snapshot or persistence warnings associated with the persistence operation.
        /// </summary>
        public int WarningCount { get; }

        /// <summary>
        /// Gets the number of snapshot or persistence errors associated with the persistence operation.
        /// </summary>
        public int ErrorCount { get; }

        /// <summary>
        /// Gets the number of metric records included in the persistence operation.
        /// </summary>
        public int MetricCount { get; }

        /// <summary>
        /// Gets the number of generated summary records included in the persistence operation.
        /// </summary>
        public int GeneratedSummaryCount { get; }

        /// <summary>
        /// Gets the optional number of metadata entries included when this can be measured accurately.
        /// </summary>
        public int? MetadataEntryCount { get; }

        /// <summary>
        /// Gets the optional number of persistence operations executed or planned when known.
        /// </summary>
        public int? PersistenceOperationCount { get; }

        /// <summary>
        /// Gets the optional number of persistence batches executed or planned when known.
        /// </summary>
        public int? PersistenceBatchCount { get; }

        /// <summary>
        /// Gets the optional serialized payload size in bytes when materialization produced a measurable payload.
        /// </summary>
        public long? SerializedPayloadBytes { get; }

        /// <summary>
        /// Normalizes nullable integer counts while preserving unknown measurements as null.
        /// </summary>
        /// <param name="value">The optional count value supplied by a persistence writer.</param>
        /// <returns>The non-negative count value, or <see langword="null"/> when the value was unknown.</returns>
        private static int? NormalizeOptionalCount(int? value)
        {
            // Unknown optional values remain null so status consumers can distinguish unknown from known-empty counts.
            return value.HasValue ? Math.Max(0, value.Value) : null;
        }
    }
}
