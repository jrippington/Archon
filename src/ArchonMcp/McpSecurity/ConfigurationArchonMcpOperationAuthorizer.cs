using ArchonMcp.McpEnvelope;
using Microsoft.Extensions.Options;

namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Authorizes MCP operations by combining configured authentication requirements with operation allow-list checks.
    /// </summary>
    internal sealed class ConfigurationArchonMcpOperationAuthorizer : IArchonMcpOperationAuthorizer
    {
        /// <summary>
        /// Stores the current MCP security options used to evaluate authorization decisions.
        /// </summary>
        private readonly IOptions<ArchonMcpSecurityOptions> _options;

        /// <summary>
        /// Creates a configuration-backed MCP operation authorizer.
        /// </summary>
        /// <param name="options">The options that define authentication and allow-list requirements.</param>
        public ConfigurationArchonMcpOperationAuthorizer(IOptions<ArchonMcpSecurityOptions> options)
        {
            // The authorizer is provider-neutral; it evaluates the normalized caller context and configured operation names only.
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc />
        public ArchonMcpAuthorizationDecision Authorize(string operationName, ArchonMcpCallerContext callerContext)
        {
            // Fail closed for malformed operation names because they cannot be safely matched to registrations or audited.
            if (string.IsNullOrWhiteSpace(operationName))
            {
                return ArchonMcpAuthorizationDecision.Deny(
                    ArchonMcpErrorCategory.UnsupportedOperation,
                    "The requested MCP operation is not supported.");
            }

            ArchonMcpSecurityOptions value = _options.Value;

            // Authentication is evaluated before allow-listing so callers can distinguish missing identity from forbidden access.
            if (value.RequireAuthenticatedCaller && !callerContext.IsAuthenticated)
            {
                return ArchonMcpAuthorizationDecision.Deny(
                    ArchonMcpErrorCategory.Unauthorized,
                    "An authenticated MCP caller is required for this operation.");
            }

            // An empty allow-list disables every operation by design, which is the safest behavior for misconfiguration.
            bool allowed = value.AllowedOperations.Any(allowedOperation => string.Equals(
                allowedOperation,
                operationName,
                StringComparison.OrdinalIgnoreCase));
            if (!allowed)
            {
                return ArchonMcpAuthorizationDecision.Deny(
                    ArchonMcpErrorCategory.Forbidden,
                    "The requested MCP operation is disabled by the configured allow-list.");
            }

            return ArchonMcpAuthorizationDecision.Allow();
        }
    }
}
