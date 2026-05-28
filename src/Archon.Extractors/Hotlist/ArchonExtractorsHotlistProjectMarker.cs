namespace Archon.Extractors.Hotlist
{
    /// <summary>
    /// Identifies the Hotlist extractor category after it has been migrated into the consolidated extractor assembly.
    /// </summary>
    /// <remarks>
    /// The marker gives tests and later composition code a stable category-specific type while the category now
    /// loads from the shared <c>Archon.Extractors</c> production assembly rather than a standalone category project.
    /// </remarks>
    public sealed class ArchonExtractorsHotlistProjectMarker
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonExtractorsHotlistProjectMarker"/> class.
        /// </summary>
        /// <remarks>
        /// The marker has no dependencies or state. Construction only proves that the consolidated extractor
        /// assembly exposes a loadable type for the Hotlist category.
        /// </remarks>
        public ArchonExtractorsHotlistProjectMarker()
        {
            // The marker has no state; construction only proves the consolidated project can be referenced and loaded.
        }

        /// <summary>
        /// Gets the canonical production assembly name represented by this migrated category marker.
        /// </summary>
        /// <returns>The consolidated production project name used by the migrated extractor category.</returns>
        /// <remarks>
        /// Returning the consolidated assembly name keeps the marker aligned with the migrated runtime identity while
        /// the namespace and type name continue to identify the Hotlist category itself.
        /// </remarks>
        public string GetProjectName()
        {
            // Returning a literal keeps the marker deterministic across developer machines and build paths.
            return "Archon.Extractors";
        }
    }
}
