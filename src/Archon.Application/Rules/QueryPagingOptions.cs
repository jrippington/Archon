namespace Archon.Application.Rules
{
    /// <summary>
    /// Defines shared paging bounds for controlled query contracts used by API and future MCP consumers.
    /// </summary>
    public static class QueryPagingOptions
    {
        /// <summary>
        /// Defines the default number of records returned when a controlled query omits a page size.
        /// </summary>
        public const int DefaultPageSize = 100;

        /// <summary>
        /// Defines the maximum number of records returned by one controlled WP013 query request.
        /// </summary>
        public const int MaximumPageSize = 500;
    }
}