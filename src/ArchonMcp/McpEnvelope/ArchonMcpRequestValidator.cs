using ArchonMcp.McpRuntime;
using Microsoft.Extensions.Options;

namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Implements shared MCP validation rules for stable keys, snapshots, search text, filters, pagination, depth, and count values.
    /// </summary>
    public sealed class ArchonMcpRequestValidator : IArchonMcpRequestValidator
    {
        /// <summary>
        /// Stores the configured conservative MCP limits used when validating caller-requested bounds.
        /// </summary>
        private readonly ArchonMcpLimitsOptions _limits;

        /// <summary>
        /// Creates a validator from configured MCP limits.
        /// </summary>
        /// <param name="limits">The options wrapper that supplies conservative MCP response and traversal limits.</param>
        public ArchonMcpRequestValidator(IOptions<ArchonMcpLimitsOptions> limits)
        {
            // The validator reads limits once because the MCP host uses startup-validated options for deterministic behavior.
            ArgumentNullException.ThrowIfNull(limits);
            _limits = limits.Value;
        }

        /// <inheritdoc />
        public ArchonMcpValidationResult Validate(ArchonMcpValidationRequest request)
        {
            // Aggregate all failures so MCP clients can correct a malformed request in one turn instead of iterative retries.
            ArgumentNullException.ThrowIfNull(request);
            List<ArchonMcpValidationFailure> failures = [];

            AddFailures(failures, ValidateStableKey(request.StableKey, "stableKey"));
            ValidateSnapshotSelector(request.SnapshotSelector, failures);
            ValidateSearchText(request.SearchText, failures);
            ValidateFilters(request.Filters, failures);
            ValidateRequestedCount(request.RequestedCount, failures);
            ValidateRequestedDepth(request.RequestedDepth, failures);
            ValidatePagination(request.PageNumber, request.PageSize, failures);

            return new ArchonMcpValidationResult(failures);
        }

        /// <summary>
        /// Validates a stable public key and rejects Neo4j internal integer identifiers.
        /// </summary>
        /// <param name="stableKey">The stable key value to validate.</param>
        /// <param name="fieldName">The safe request field name used in validation failures.</param>
        /// <returns>A validation result for the stable key field.</returns>
        public static ArchonMcpValidationResult ValidateStableKey(string? stableKey, string fieldName)
        {
            // Stable keys are public logical identities and normally include a scheme delimiter; raw numeric values look like Neo4j internals.
            if (stableKey is null)
            {
                return ArchonMcpValidationResult.Success();
            }

            if (string.IsNullOrWhiteSpace(stableKey))
            {
                return new ArchonMcpValidationResult([new ArchonMcpValidationFailure(fieldName, "Stable key must not be empty when supplied.")]);
            }

            if (long.TryParse(stableKey, out _) || !stableKey.Contains("://", StringComparison.Ordinal))
            {
                return new ArchonMcpValidationResult([new ArchonMcpValidationFailure(fieldName, "Stable key must use a public stable identity and must not be a Neo4j internal identifier.")]);
            }

            return ArchonMcpValidationResult.Success();
        }

        /// <summary>
        /// Appends failures from one validation result into the aggregate failure list.
        /// </summary>
        /// <param name="failures">The aggregate failure list being built for the request.</param>
        /// <param name="result">The result whose failures should be appended.</param>
        private static void AddFailures(List<ArchonMcpValidationFailure> failures, ArchonMcpValidationResult result)
        {
            // A helper keeps the main validation flow readable as more shared fields are added by later slices.
            failures.AddRange(result.Failures);
        }

        /// <summary>
        /// Validates snapshot selectors before they are passed to query-layer resolution.
        /// </summary>
        /// <param name="snapshotSelector">The optional snapshot selector or stable key.</param>
        /// <param name="failures">The aggregate failure list being built for the request.</param>
        private static void ValidateSnapshotSelector(string? snapshotSelector, List<ArchonMcpValidationFailure> failures)
        {
            // The shared selector accepts the controlled latest value or stable snapshot keys, but rejects ambiguous free-form text.
            if (snapshotSelector is null)
            {
                return;
            }

            if (string.Equals(snapshotSelector, "latest", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ArchonMcpValidationResult stableKeyResult = ValidateStableKey(snapshotSelector, "snapshotSelector");
            if (!stableKeyResult.IsValid)
            {
                failures.Add(new ArchonMcpValidationFailure("snapshotSelector", "Snapshot selector must be 'latest' or a stable snapshot key."));
            }
        }

        /// <summary>
        /// Validates common search text input for bounded MCP query operations.
        /// </summary>
        /// <param name="searchText">The optional search text supplied by the caller.</param>
        /// <param name="failures">The aggregate failure list being built for the request.</param>
        private static void ValidateSearchText(string? searchText, List<ArchonMcpValidationFailure> failures)
        {
            // Empty search text should fail before reaching query dependencies; long text is bounded for AI-client safety.
            if (searchText is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(searchText))
            {
                failures.Add(new ArchonMcpValidationFailure("searchText", "Search text must not be empty when supplied."));
                return;
            }

            if (searchText.Length > 512)
            {
                failures.Add(new ArchonMcpValidationFailure("searchText", "Search text must be 512 characters or fewer."));
            }
        }

        /// <summary>
        /// Validates common filter values for bounded MCP query operations.
        /// </summary>
        /// <param name="filters">The optional filter values supplied by the caller.</param>
        /// <param name="failures">The aggregate failure list being built for the request.</param>
        private static void ValidateFilters(IReadOnlyList<string>? filters, List<ArchonMcpValidationFailure> failures)
        {
            // Filters are intentionally simple tokens at this layer; concrete tools can add stricter allow-lists later.
            if (filters is null)
            {
                return;
            }

            if (filters.Any(string.IsNullOrWhiteSpace))
            {
                failures.Add(new ArchonMcpValidationFailure("filters", "Filter values must not be empty."));
            }
        }

        /// <summary>
        /// Validates requested result counts against configured MCP limits.
        /// </summary>
        /// <param name="requestedCount">The optional caller-requested result count.</param>
        /// <param name="failures">The aggregate failure list being built for the request.</param>
        private void ValidateRequestedCount(int? requestedCount, List<ArchonMcpValidationFailure> failures)
        {
            // Count validation protects query dependencies from unbounded result requests.
            if (requestedCount is null)
            {
                return;
            }

            if (requestedCount < 0 || requestedCount > _limits.MaxResultCount)
            {
                failures.Add(new ArchonMcpValidationFailure("requestedCount", $"Requested count must be between 0 and {_limits.MaxResultCount}."));
            }
        }

        /// <summary>
        /// Validates requested traversal depth against configured MCP limits.
        /// </summary>
        /// <param name="requestedDepth">The optional caller-requested traversal depth.</param>
        /// <param name="failures">The aggregate failure list being built for the request.</param>
        private void ValidateRequestedDepth(int? requestedDepth, List<ArchonMcpValidationFailure> failures)
        {
            // Depth validation protects graph traversals from expanding beyond the conservative default boundary.
            if (requestedDepth is null)
            {
                return;
            }

            if (requestedDepth < 0 || requestedDepth > _limits.MaxTraversalDepth)
            {
                failures.Add(new ArchonMcpValidationFailure("requestedDepth", $"Requested depth must be between 0 and {_limits.MaxTraversalDepth}."));
            }
        }

        /// <summary>
        /// Validates optional page-number and page-size fields.
        /// </summary>
        /// <param name="pageNumber">The optional one-based page number.</param>
        /// <param name="pageSize">The optional page size.</param>
        /// <param name="failures">The aggregate failure list being built for the request.</param>
        private void ValidatePagination(int? pageNumber, int? pageSize, List<ArchonMcpValidationFailure> failures)
        {
            // Pagination stays one-based to match common API conventions and uses the same result-count ceiling for page size.
            if (pageNumber is not null && pageNumber < 1)
            {
                failures.Add(new ArchonMcpValidationFailure("pageNumber", "Page number must be one or greater when supplied."));
            }

            if (pageSize is not null && (pageSize < 0 || pageSize > _limits.MaxResultCount))
            {
                failures.Add(new ArchonMcpValidationFailure("pageSize", $"Page size must be between 0 and {_limits.MaxResultCount}."));
            }
        }
    }
}
