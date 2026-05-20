namespace Archon.Extractors.Razor
{
    /// <summary>
    /// Identifies the Archon.Extractors.Razor project in the WP001 solution skeleton.
    /// </summary>
    /// <remarks>
    /// The marker gives tests and later composition code a stable type without implementing behavior assigned to future work packages.
    /// </remarks>
    public sealed class ArchonExtractorsRazorProjectMarker
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonExtractorsRazorProjectMarker"/> class.
        /// </summary>
        public ArchonExtractorsRazorProjectMarker()
        {
            // The marker has no state; construction only proves the project can be referenced and loaded.
        }

        /// <summary>
        /// Gets the canonical project name represented by this marker.
        /// </summary>
        /// <returns>The production project name used by the solution skeleton.</returns>
        public string GetProjectName()
        {
            // Returning a literal keeps the marker deterministic across developer machines and build paths.
            return "Archon.Extractors.Razor";
        }
    }
}
