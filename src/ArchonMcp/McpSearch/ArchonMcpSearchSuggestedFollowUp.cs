namespace ArchonMcp.McpSearch
{
    /// <summary>
    /// Represents a safe result-level follow-up returned inside <c>archon.search</c> facts.
    /// </summary>
    /// <param name="Label">The human-readable label for the follow-up action.</param>
    /// <param name="Operation">The safe MCP operation, API route, resource, or user-question marker.</param>
    /// <param name="Parameters">Safe stable-key-based follow-up parameters.</param>
    public sealed record ArchonMcpSearchSuggestedFollowUp(string Label, string Operation, IReadOnlyDictionary<string, string> Parameters)
    {
        // Search facts keep follow-up affordances close to each result while the common envelope carries response-wide suggestions.
    }
}
