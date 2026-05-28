using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.Ui
{
    /// <summary>
    /// Provides UI-specific merge helpers around the application-layer snapshot accumulator.
    /// </summary>
    /// <remarks>
    /// The helpers keep cross-framework UI/client stages from duplicating before-and-after counting logic while preserving the accumulator as the single source of truth for stable-key deduplication.
    /// </remarks>
    public static class UiSnapshotAccumulatorExtensions
    {
        /// <summary>
        /// Merges a framework-specific UI snapshot and returns the post-merge contribution deltas.
        /// </summary>
        /// <param name="accumulator">The shared extraction accumulator that receives UI graph facts.</param>
        /// <param name="snapshot">The framework-specific UI snapshot to merge into the shared accumulator.</param>
        /// <returns>A summary describing added nodes, edges, evidence, warnings, and errors after deduplication.</returns>
        public static UiSnapshotMergeSummary MergeUiSnapshot(this ArchitectureSnapshotAccumulator accumulator, ExtractedArchitectureSnapshot snapshot)
        {
            // The accumulator already owns deterministic replacement by stable key; this method simply measures the visible result for unified-stage diagnostics and tests.
            ArgumentNullException.ThrowIfNull(accumulator);
            ArgumentNullException.ThrowIfNull(snapshot);

            ExtractedArchitectureSnapshot before = accumulator.ToSnapshot();
            accumulator.Merge(snapshot);
            ExtractedArchitectureSnapshot after = accumulator.ToSnapshot();

            return new UiSnapshotMergeSummary(
                after.Nodes.Count - before.Nodes.Count,
                after.Edges.Count - before.Edges.Count,
                after.Evidence.Count - before.Evidence.Count,
                after.Warnings.Count - before.Warnings.Count,
                after.Errors.Count - before.Errors.Count);
        }
    }
}
