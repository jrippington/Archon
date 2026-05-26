using ArchonMcp.McpSecurity;

namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Dispatches Archon MCP resource reads through authorization, URI parsing, and current resource handlers.
    /// </summary>
    public sealed class ArchonMcpResourceDispatcher : IArchonMcpResourceDispatcher
    {
        /// <summary>
        /// Executes authorization, allow-listing, and audit behavior before resource parsing or query logic runs.
        /// </summary>
        private readonly IArchonMcpOperationExecutor _operationExecutor;

        /// <summary>
        /// Parses and validates caller-supplied resource URI text.
        /// </summary>
        private readonly IArchonMcpResourceUriParser _uriParser;

        /// <summary>
        /// Handles validated current resource requests.
        /// </summary>
        private readonly IArchonMcpCurrentResourceHandler _currentResourceHandler;

        /// <summary>
        /// Handles validated parameterized project, symbol, and snapshot diff resource requests.
        /// </summary>
        private readonly IArchonMcpParameterizedResourceHandler _parameterizedResourceHandler;

        /// <summary>
        /// Creates an MCP resource dispatcher.
        /// </summary>
        /// <param name="operationExecutor">The executor that performs security, allow-listing, audit, and safe failure mapping.</param>
        /// <param name="uriParser">The parser that validates resource URI syntax before query execution.</param>
        /// <param name="currentResourceHandler">The handler that reads validated current resources.</param>
        /// <param name="parameterizedResourceHandler">The handler that reads validated parameterized resources.</param>
        public ArchonMcpResourceDispatcher(
            IArchonMcpOperationExecutor operationExecutor,
            IArchonMcpResourceUriParser uriParser,
            IArchonMcpCurrentResourceHandler currentResourceHandler,
            IArchonMcpParameterizedResourceHandler parameterizedResourceHandler)
        {
            // The dispatcher owns cross-cutting ordering: authorization first, validation second, query-backed resource handling last.
            _operationExecutor = operationExecutor ?? throw new ArgumentNullException(nameof(operationExecutor));
            _uriParser = uriParser ?? throw new ArgumentNullException(nameof(uriParser));
            _currentResourceHandler = currentResourceHandler ?? throw new ArgumentNullException(nameof(currentResourceHandler));
            _parameterizedResourceHandler = parameterizedResourceHandler ?? throw new ArgumentNullException(nameof(parameterizedResourceHandler));
        }

        /// <inheritdoc />
        public async Task<object> ReadResourceAsync(string? uri, CancellationToken cancellationToken)
        {
            // Authorization precedes parsing so disabled resource reads cannot be used to probe supported URI forms or query scopes.
            ArchonMcpOperationResult result = await _operationExecutor.ExecuteAsync(
                ArchonMcpResourceOperations.ReadResource,
                CreateAuditParameters(uri),
                () => ExecuteAuthorizedReadAsync(uri, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return result.Payload;
        }

        /// <summary>
        /// Parses and dispatches a resource read after authorization succeeds.
        /// </summary>
        /// <param name="uri">The caller-supplied resource URI text.</param>
        /// <param name="cancellationToken">The token that can cancel parsing-adjacent or handler work.</param>
        /// <returns>A typed MCP success envelope or structured MCP error response.</returns>
        private async Task<object> ExecuteAuthorizedReadAsync(string? uri, CancellationToken cancellationToken)
        {
            // Parser errors are returned directly and intentionally do not invoke any current snapshot or query dependency.
            ArchonMcpResourceParseResult parseResult = _uriParser.Parse(uri);
            if (!parseResult.Succeeded)
            {
                return parseResult.Error!;
            }

            ArchonMcpResourceRequest request = parseResult.Request!;
            if (string.Equals(request.Selector, "current", StringComparison.OrdinalIgnoreCase))
            {
                return await _currentResourceHandler.ReadCurrentResourceAsync(request, cancellationToken).ConfigureAwait(false);
            }

            return await _parameterizedResourceHandler.ReadParameterizedResourceAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates safe audit parameters for a resource read attempt.
        /// </summary>
        /// <param name="uri">The caller-supplied resource URI text.</param>
        /// <returns>Audit parameters that can be sanitized by the shared audit normalizer.</returns>
        private static IReadOnlyDictionary<string, string> CreateAuditParameters(string? uri)
        {
            // The audit normalizer handles sensitive values, while this method avoids adding any derived filesystem or persistence context.
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["uri"] = uri ?? string.Empty
            };
        }
    }
}
