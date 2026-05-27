namespace Archon.Api.Extraction.Contracts
{
    /// <summary>
    /// Represents the persistence-specific diagnostic section returned by the extraction status endpoint.
    /// </summary>
    /// <param name="Timings">The ordered persistence sub-stage timings collected for the run.</param>
    /// <param name="Counts">The persistence volume and operation counts associated with the same run.</param>
    /// <param name="Completed">A value indicating whether the diagnostic set represents a completed persistence attempt.</param>
    public sealed record ExtractionRunPersistenceDiagnosticsResponse(
        IReadOnlyList<ExtractionRunTimingResponse> Timings,
        ExtractionRunPersistenceCountsResponse Counts,
        bool Completed);
}
