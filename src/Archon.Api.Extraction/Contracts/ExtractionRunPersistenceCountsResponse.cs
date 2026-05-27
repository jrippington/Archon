namespace Archon.Api.Extraction.Contracts
{
    /// <summary>
    /// Represents persistence-specific count values returned by the extraction status endpoint.
    /// </summary>
    /// <param name="RepositoryCount">The number of repositories included in the persistence operation.</param>
    /// <param name="SolutionCount">The number of solutions included in the persistence operation.</param>
    /// <param name="ProjectCount">The number of projects included in the persistence operation.</param>
    /// <param name="FileCount">The number of file or document records included when known.</param>
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
    public sealed record ExtractionRunPersistenceCountsResponse(
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
        long? SerializedPayloadBytes);
}
