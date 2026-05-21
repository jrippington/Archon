using Archon.Application.Extraction.Accumulation;

namespace Archon.Application.Extraction.Pipeline
{
    /// <summary>
    /// Represents the result of executing the deterministic extraction stage pipeline.
    /// </summary>
    /// <param name="Succeeded">Whether every executed stage completed without a blocking error.</param>
    /// <param name="Accumulation">The accumulation model containing all stage contributions and diagnostics.</param>
    /// <param name="ExecutedStageIds">The stable identifiers of stages that actually ran.</param>
    /// <param name="FailedStageId">The optional stable identifier of the stage that stopped the pipeline.</param>
    public sealed record ExtractionPipelineResult(
        bool Succeeded,
        ArchitectureSnapshotAccumulator Accumulation,
        IReadOnlyList<string> ExecutedStageIds,
        string? FailedStageId);
}
