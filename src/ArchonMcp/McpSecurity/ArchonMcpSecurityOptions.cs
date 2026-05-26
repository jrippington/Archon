namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Provides configuration-backed MCP authentication and allow-list settings.
    /// </summary>
    public sealed class ArchonMcpSecurityOptions
    {
        /// <summary>
        /// Identifies the configuration section that binds MCP security options.
        /// </summary>
        public const string SectionName = "Archon:Mcp:Security";

        /// <summary>
        /// Gets or sets a value indicating whether MCP operation execution requires an authenticated caller identity.
        /// </summary>
        public bool RequireAuthenticatedCaller { get; set; } = true;

        /// <summary>
        /// Gets or sets the test/local caller identity used by the provider-neutral default caller context provider.
        /// </summary>
        public string? TestCallerId { get; set; } = "local-development";

        /// <summary>
        /// Gets or sets the optional test/local display name used by the default caller context provider.
        /// </summary>
        public string? TestCallerDisplayName { get; set; } = "Local Development Caller";

        /// <summary>
        /// Gets or sets the test/local role names used by the default caller context provider.
        /// </summary>
        public string[] TestCallerRoles { get; set; } = ["ArchonMcpReader"];

        /// <summary>
        /// Gets or sets the allow-list of enabled operation names; an empty list fails closed and disables all operations.
        /// </summary>
        public string[] AllowedOperations { get; set; } = [];

        /// <summary>
        /// Gets or sets the allow-list of enabled resource family names for later MCP resource slices.
        /// </summary>
        public string[] AllowedResourceFamilies { get; set; } = [];
    }
}
