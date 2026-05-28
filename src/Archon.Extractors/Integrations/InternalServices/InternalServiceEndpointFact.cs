using Archon.Domain.Graph.Identity;

namespace Archon.Extractors.Integrations.InternalServices
{
    /// <summary>
    /// Describes an endpoint, controller, method, and project ownership fact produced by earlier extraction slices and consumed by internal service correlation.
    /// </summary>
    /// <param name="EndpointStableKey">The stable key of the endpoint node exposed by an analyzed project.</param>
    /// <param name="ProjectStableKey">The stable key of the project that owns the endpoint.</param>
    /// <param name="HttpMethod">The HTTP verb used by the endpoint route.</param>
    /// <param name="RouteTemplate">The deterministic endpoint route template, such as <c>/api/orders/{id}</c>.</param>
    /// <param name="ProjectName">The developer-facing project or service name that owns the endpoint.</param>
    /// <param name="ControllerStableKey">The optional stable key of the controller that declares the endpoint.</param>
    /// <param name="MethodStableKey">The optional stable key of the endpoint method that handles the route.</param>
    /// <param name="BaseUrlConfigurationKey">The optional configuration key that maps clients to the owning service base URL.</param>
    /// <param name="BaseUrl">The optional deterministic base URL associated with the internal service.</param>
    public sealed record InternalServiceEndpointFact(
        StableKey EndpointStableKey,
        StableKey ProjectStableKey,
        string HttpMethod,
        string RouteTemplate,
        string ProjectName,
        StableKey? ControllerStableKey,
        StableKey? MethodStableKey,
        string? BaseUrlConfigurationKey,
        string? BaseUrl);
}
