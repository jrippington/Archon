namespace Archon.Application.Runtime
{
    /// <summary>
    /// Defines controlled runtime and worker query operations over extracted architecture snapshots.
    /// </summary>
    public interface IRuntimeQueryService
    {
        /// <summary>
        /// Lists runtime endpoints from one selected snapshot using bounded filters, deterministic ordering, and paging.
        /// </summary>
        /// <param name="query">The endpoint lookup request containing scope, filters, sort, and paging options.</param>
        /// <param name="cancellationToken">The token that cancels query execution when the caller abandons the request.</param>
        /// <returns>A runtime endpoint result containing either a page of endpoints or validation errors.</returns>
        Task<RuntimeEndpointResult> ListEndpointsAsync(RuntimeEndpointQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Gets one controller or handler detail by stable key or by exact display name within a selected snapshot.
        /// </summary>
        /// <param name="query">The controller or handler lookup request containing scope and identity.</param>
        /// <param name="cancellationToken">The token that cancels query execution when the caller abandons the request.</param>
        /// <returns>A controller or handler result containing either the selected detail or validation errors.</returns>
        Task<ControllerHandlerResult> GetControllerOrHandlerAsync(ControllerHandlerQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Lists runtime entry points from one selected snapshot using bounded filters and deterministic paging.
        /// </summary>
        /// <param name="query">The entry-point lookup request containing scope, runtime kind, project filter, and paging options.</param>
        /// <param name="cancellationToken">The token that cancels query execution when the caller abandons the request.</param>
        /// <returns>A runtime entry-point result containing either a page of entry points or validation errors.</returns>
        Task<RuntimeEntryPointResult> ListEntryPointsAsync(RuntimeEntryPointQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Lists worker-oriented runtime facts from one selected snapshot using bounded filters and deterministic paging.
        /// </summary>
        /// <param name="query">The worker lookup request containing scope, worker filters, and paging options.</param>
        /// <param name="cancellationToken">The token that cancels query execution when the caller abandons the request.</param>
        /// <returns>A worker result containing either a page of workers or validation errors.</returns>
        Task<WorkerResult> ListWorkersAsync(WorkerQuery query, CancellationToken cancellationToken);
    }
}
