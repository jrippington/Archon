namespace Archon.Extractors.Integrations.InternalServices
{
    /// <summary>
    /// Provides deterministic lookup over prior endpoint facts for internal service correlation.
    /// </summary>
    internal sealed class InternalServiceEndpointIndex
    {
        /// <summary>
        /// Stores endpoint facts in deterministic request order for bounded correlation scans.
        /// </summary>
        private readonly IReadOnlyList<InternalServiceEndpointFact> _endpoints;

        /// <summary>
        /// Initializes a new instance of the <see cref="InternalServiceEndpointIndex" /> class.
        /// </summary>
        /// <param name="endpoints">The endpoint facts that may own internal client routes.</param>
        private InternalServiceEndpointIndex(IReadOnlyList<InternalServiceEndpointFact> endpoints)
        {
            // The index keeps an immutable snapshot of prior facts so correlation cannot observe caller-side collection mutations.
            _endpoints = endpoints;
        }

        /// <summary>
        /// Creates a deterministic endpoint index from prior runtime and endpoint extraction facts.
        /// </summary>
        /// <param name="endpoints">The endpoint facts to index.</param>
        /// <param name="cancellationToken">A token that signals when endpoint normalization should stop.</param>
        /// <returns>An endpoint index ready for route and ownership lookup.</returns>
        public static InternalServiceEndpointIndex Create(IEnumerable<InternalServiceEndpointFact> endpoints, CancellationToken cancellationToken)
        {
            // Sorting ensures that ambiguous input enumeration order never controls which endpoint wins a deterministic match.
            ArgumentNullException.ThrowIfNull(endpoints);
            List<InternalServiceEndpointFact> normalized = [];
            foreach (InternalServiceEndpointFact endpoint in endpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();
                normalized.Add(endpoint);
            }

            return new InternalServiceEndpointIndex(normalized.OrderBy(endpoint => endpoint.EndpointStableKey.Value, StringComparer.Ordinal).ToArray());
        }

        /// <summary>
        /// Finds one endpoint whose route, method, and optional ownership evidence match a client call.
        /// </summary>
        /// <param name="httpMethod">The HTTP operation observed at the client call site.</param>
        /// <param name="relativePath">The deterministic client route path.</param>
        /// <param name="baseUrl">The deterministic client base URL, when available.</param>
        /// <param name="configurationKey">The client base URL configuration key, when available.</param>
        /// <returns>The matched endpoint fact, or <see langword="null" /> when no deterministic single owner exists.</returns>
        public InternalServiceEndpointFact? Find(string httpMethod, string? relativePath, string? baseUrl, string? configurationKey)
        {
            // Matching requires exact method and route compatibility, then uses base URL or configuration key to disambiguate service ownership when available.
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            IReadOnlyList<InternalServiceEndpointFact> routeMatches = _endpoints
                .Where(endpoint => endpoint.HttpMethod.Equals(httpMethod, StringComparison.OrdinalIgnoreCase) && RouteMatches(endpoint.RouteTemplate, relativePath))
                .ToArray();
            if (routeMatches.Count == 0)
            {
                return null;
            }

            IReadOnlyList<InternalServiceEndpointFact> ownershipMatches = routeMatches
                .Where(endpoint => MatchesOwnership(endpoint, baseUrl, configurationKey))
                .ToArray();
            if (ownershipMatches.Count == 1)
            {
                return ownershipMatches[0];
            }

            return routeMatches.Count == 1 && baseUrl is null && configurationKey is null ? routeMatches[0] : null;
        }

        /// <summary>
        /// Determines whether endpoint ownership evidence matches the client-side base URL or configuration key.
        /// </summary>
        /// <param name="endpoint">The endpoint fact being evaluated.</param>
        /// <param name="baseUrl">The client-side base URL evidence.</param>
        /// <param name="configurationKey">The client-side configuration-key evidence.</param>
        /// <returns><see langword="true" /> when ownership evidence agrees; otherwise, <see langword="false" />.</returns>
        private static bool MatchesOwnership(InternalServiceEndpointFact endpoint, string? baseUrl, string? configurationKey)
        {
            // Either exact base URL or exact configuration key is enough because both are deterministic prior facts rather than naming guesses.
            return (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(endpoint.BaseUrl) && endpoint.BaseUrl.Equals(baseUrl, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(configurationKey) && !string.IsNullOrWhiteSpace(endpoint.BaseUrlConfigurationKey) && endpoint.BaseUrlConfigurationKey.Equals(configurationKey, StringComparison.Ordinal));
        }

        /// <summary>
        /// Determines whether a route template can own a concrete client route.
        /// </summary>
        /// <param name="routeTemplate">The endpoint route template.</param>
        /// <param name="relativePath">The concrete client request path.</param>
        /// <returns><see langword="true" /> when the route template matches the path; otherwise, <see langword="false" />.</returns>
        private static bool RouteMatches(string routeTemplate, string relativePath)
        {
            // Parameter segments such as {id} match one concrete path segment; other segments must match exactly to avoid route guessing.
            string[] templateSegments = NormalizePath(routeTemplate).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string[] pathSegments = NormalizePath(relativePath).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (templateSegments.Length != pathSegments.Length)
            {
                return false;
            }

            for (int index = 0; index < templateSegments.Length; index++)
            {
                string templateSegment = templateSegments[index];
                if (templateSegment.StartsWith("{", StringComparison.Ordinal) && templateSegment.EndsWith("}", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!templateSegment.Equals(pathSegments[index], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Normalizes a route or path before segment comparison.
        /// </summary>
        /// <param name="path">The route template or request path.</param>
        /// <returns>A slash-prefixed path without query text or duplicate edge slashes.</returns>
        private static string NormalizePath(string path)
        {
            // Queries are request details rather than route ownership evidence, so they are removed before matching.
            string withoutQuery = path.Split('?', StringSplitOptions.None)[0].Trim();
            return "/" + withoutQuery.Trim('/');
        }
    }
}
