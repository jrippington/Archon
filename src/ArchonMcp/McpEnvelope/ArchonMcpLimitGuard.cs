using ArchonMcp.McpRuntime;
using Microsoft.Extensions.Options;

namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Applies conservative MCP response limits and returns truncation metadata for bounded list sections.
    /// </summary>
    public sealed class ArchonMcpLimitGuard
    {
        /// <summary>
        /// Stores the configured conservative MCP limits used by shared guard methods.
        /// </summary>
        private readonly ArchonMcpLimitsOptions _limits;

        /// <summary>
        /// Creates a limit guard from configured MCP limits.
        /// </summary>
        /// <param name="limits">The options wrapper that supplies conservative MCP response and traversal limits.</param>
        public ArchonMcpLimitGuard(IOptions<ArchonMcpLimitsOptions> limits)
        {
            // Startup options validation guarantees positive values before operation handlers use the guard.
            ArgumentNullException.ThrowIfNull(limits);
            _limits = limits.Value;
        }

        /// <summary>
        /// Applies the configured result-count limit to a response section.
        /// </summary>
        /// <typeparam name="TItem">The item type contained in the bounded list.</typeparam>
        /// <param name="items">The items to bound.</param>
        /// <param name="requestedLimit">The caller-requested limit when supplied.</param>
        /// <param name="operation">The operation that will return the bounded list.</param>
        /// <returns>A bounded list with truncation metadata and narrowing suggestions.</returns>
        public ArchonMcpLimitedList<TItem> ApplyResultLimit<TItem>(IEnumerable<TItem> items, int? requestedLimit, string operation)
        {
            // Materializing once lets the guard report the original count and return deterministic ordering supplied by the caller.
            ArgumentNullException.ThrowIfNull(items);
            TItem[] materializedItems = items.ToArray();
            int appliedLimit = requestedLimit is > 0
                ? Math.Min(requestedLimit.Value, _limits.MaxResultCount)
                : _limits.MaxResultCount;
            TItem[] returnedItems = materializedItems.Take(appliedLimit).ToArray();
            bool truncated = materializedItems.Length > returnedItems.Length;

            ArchonMcpLimitMetadata metadata = new(
                truncated,
                "resultCount",
                appliedLimit,
                requestedLimit,
                materializedItems.Length,
                returnedItems.Length,
                truncated ? "Result count exceeded the configured MCP response limit." : null);

            ArchonMcpSuggestedFollowUp[] followUps = truncated
                ? [new ArchonMcpSuggestedFollowUp("Narrow the request with a more specific filter or stable key.", "user.question", new Dictionary<string, string> { ["operation"] = operation })]
                : [];

            return new ArchonMcpLimitedList<TItem>(returnedItems, metadata, followUps);
        }
    }
}
