using Microsoft.Extensions.Options;

namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Supplies a provider-neutral caller context from local/test MCP security configuration.
    /// </summary>
    internal sealed class ConfigurationArchonMcpCallerContextProvider : IArchonMcpCallerContextProvider
    {
        /// <summary>
        /// Stores the bound MCP security options that provide local caller metadata.
        /// </summary>
        private readonly IOptions<ArchonMcpSecurityOptions> _options;

        /// <summary>
        /// Creates a caller context provider backed by MCP security options.
        /// </summary>
        /// <param name="options">The options that provide local/test caller identity metadata.</param>
        public ConfigurationArchonMcpCallerContextProvider(IOptions<ArchonMcpSecurityOptions> options)
        {
            // The default provider is intentionally simple so production hosts can replace this seam with a real identity provider.
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc />
        public ArchonMcpCallerContext GetCurrentCaller()
        {
            // Only stable identity metadata is projected into the caller context; credentials, tokens, and claims payloads are omitted.
            ArchonMcpSecurityOptions value = _options.Value;
            return new ArchonMcpCallerContext(value.TestCallerId, value.TestCallerDisplayName, value.TestCallerRoles);
        }
    }
}
