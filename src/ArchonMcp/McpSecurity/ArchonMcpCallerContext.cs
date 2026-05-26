namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Describes the provider-neutral caller identity available to MCP operation authorization and audit logging.
    /// </summary>
    public sealed record ArchonMcpCallerContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpCallerContext" /> record.
        /// </summary>
        /// <param name="callerId">The stable caller identity supplied by the host authentication seam, or <see langword="null" /> when no authenticated caller is available.</param>
        /// <param name="displayName">The optional display name supplied by the host authentication seam.</param>
        /// <param name="roles">The provider-neutral role names associated with the caller.</param>
        public ArchonMcpCallerContext(string? callerId, string? displayName, IEnumerable<string>? roles)
        {
            // The context stores only identity metadata that is safe for authorization and audit; credentials and tokens never belong here.
            CallerId = string.IsNullOrWhiteSpace(callerId) ? null : callerId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName;
            Roles = roles?
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(role => role.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }

        /// <summary>
        /// Gets the stable caller identity supplied by the host authentication seam.
        /// </summary>
        public string? CallerId { get; init; }

        /// <summary>
        /// Gets the optional human-readable display name supplied by the host authentication seam.
        /// </summary>
        public string? DisplayName { get; init; }

        /// <summary>
        /// Gets the provider-neutral role names associated with the caller.
        /// </summary>
        public IReadOnlyList<string> Roles { get; init; }

        /// <summary>
        /// Gets a value indicating whether an authenticated caller identity is available.
        /// </summary>
        public bool IsAuthenticated => CallerId is not null;
    }
}
