using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;

namespace Archon.Application.Extraction.Pipeline
{
    /// <summary>
    /// Carries the validated extraction input, accepted run state, and shared accumulation model into a pipeline stage.
    /// </summary>
    /// <param name="ResolvedInput">The normalized input that has already passed request validation.</param>
    /// <param name="Run">The accepted run whose identifier and lifecycle context scope this execution.</param>
    /// <param name="Accumulation">The shared accumulation model that receives stage contributions.</param>
    public sealed record ExtractionStageContext(
        ResolvedExtractionInput ResolvedInput,
        ExtractionRun Run,
        ArchitectureSnapshotAccumulator Accumulation);
}
