namespace Archon.Extractors.Integrations
{
    /// <summary>
    /// Provides a stable marker type for locating the Archon external integration extractor assembly.
    /// </summary>
    /// <remarks>
    /// Marker types let tests and host composition verify project references without coupling to an implementation class whose responsibility may move as the WP010 extractor grows.
    /// </remarks>
    public sealed class ArchonExtractorsIntegrationsProjectMarker
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonExtractorsIntegrationsProjectMarker" /> class.
        /// </summary>
        public ArchonExtractorsIntegrationsProjectMarker()
        {
            // The marker has no behavior; construction simply proves the assembly can be loaded through normal project references.
        }
    }
}
