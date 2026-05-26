namespace ArchonMcp.McpSymbols
{
    /// <summary>
    /// Provides stable MCP operation names for read-only symbol investigation tools.
    /// </summary>
    public static class ArchonMcpSymbolOperations
    {
        /// <summary>
        /// Identifies the symbol description tool in catalog, authorization, audit, and response envelopes.
        /// </summary>
        public const string DescribeSymbol = "archon.describe_symbol";

        /// <summary>
        /// Identifies the symbol usage investigation tool in catalog, authorization, audit, and response envelopes.
        /// </summary>
        public const string FindSymbolUsages = "archon.find_symbol_usages";
    }
}
