namespace Archon.Application.Projects
{
    /// <summary>
    /// Summarizes graph facts directly scoped to one project detail response.
    /// </summary>
    /// <param name="NodeCount">The number of project-owned or directly related nodes included in the scoped summary.</param>
    /// <param name="OutgoingDependencyCount">The number of outgoing dependency/reference edges from the project.</param>
    /// <param name="IncomingDependencyCount">The number of incoming dependency/reference edges to the project.</param>
    /// <param name="EndpointCount">The number of endpoint nodes owned by the project.</param>
    /// <param name="DataAccessCount">The number of data-access nodes or relationships owned by the project.</param>
    /// <param name="IntegrationCount">The number of integration nodes or relationships owned by the project.</param>
    public sealed record ScopedGraphSummaryDto(int NodeCount, int OutgoingDependencyCount, int IncomingDependencyCount, int EndpointCount, int DataAccessCount, int IntegrationCount)
    {
        // Negative counts should never appear because values are aggregate metadata derived from snapshot facts.
    }
}
