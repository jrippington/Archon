namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Enumerates supported Archon MCP resource URI families.
    /// </summary>
    public enum ArchonMcpResourceFamily
    {
        /// <summary>
        /// Represents snapshot summary resources.
        /// </summary>
        Snapshot,

        /// <summary>
        /// Represents architecture-rule catalog resources.
        /// </summary>
        Rules,

        /// <summary>
        /// Represents hotlist finding resources.
        /// </summary>
        Hotlist,

        /// <summary>
        /// Represents architecture hotspot resources.
        /// </summary>
        Hotspots,

        /// <summary>
        /// Represents project context resources addressed by project stable key.
        /// </summary>
        Project,

        /// <summary>
        /// Represents symbol context resources addressed by symbol stable key.
        /// </summary>
        Symbol
    }
}
