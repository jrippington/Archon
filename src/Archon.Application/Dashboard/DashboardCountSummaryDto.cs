namespace Archon.Application.Dashboard
{
    /// <summary>
    /// Carries deterministic count fields for the dashboard summary.
    /// </summary>
    public sealed class DashboardCountSummaryDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DashboardCountSummaryDto"/> class.
        /// </summary>
        /// <param name="projectCount">The number of project nodes in the selected scope.</param>
        /// <param name="cSharpProjectCount">The number of C# project nodes in the selected scope.</param>
        /// <param name="visualBasicProjectCount">The number of VB.NET project nodes in the selected scope.</param>
        /// <param name="apiCount">The number of projects classified as API applications in the selected scope.</param>
        /// <param name="workerCount">The number of worker or hosted-service projects in the selected scope.</param>
        /// <param name="endpointCount">The number of endpoint nodes in the selected scope.</param>
        /// <param name="dataContextCount">The number of data-context nodes in the selected scope.</param>
        /// <param name="hotlistFindingCount">The number of finding records in the selected scope.</param>
        public DashboardCountSummaryDto(int projectCount, int cSharpProjectCount, int visualBasicProjectCount, int apiCount, int workerCount, int endpointCount, int dataContextCount, int hotlistFindingCount)
        {
            // Counts are clamped at zero so malformed fixture input cannot leak negative dashboard values.
            ProjectCount = Math.Max(0, projectCount);
            CSharpProjectCount = Math.Max(0, cSharpProjectCount);
            VisualBasicProjectCount = Math.Max(0, visualBasicProjectCount);
            ApiCount = Math.Max(0, apiCount);
            WorkerCount = Math.Max(0, workerCount);
            EndpointCount = Math.Max(0, endpointCount);
            DataContextCount = Math.Max(0, dataContextCount);
            HotlistFindingCount = Math.Max(0, hotlistFindingCount);
        }

        /// <summary>
        /// Gets the number of project nodes in the selected scope.
        /// </summary>
        public int ProjectCount { get; }

        /// <summary>
        /// Gets the number of C# project nodes in the selected scope.
        /// </summary>
        public int CSharpProjectCount { get; }

        /// <summary>
        /// Gets the number of VB.NET project nodes in the selected scope.
        /// </summary>
        public int VisualBasicProjectCount { get; }

        /// <summary>
        /// Gets the number of projects classified as API applications in the selected scope.
        /// </summary>
        public int ApiCount { get; }

        /// <summary>
        /// Gets the number of worker or hosted-service projects in the selected scope.
        /// </summary>
        public int WorkerCount { get; }

        /// <summary>
        /// Gets the number of endpoint nodes in the selected scope.
        /// </summary>
        public int EndpointCount { get; }

        /// <summary>
        /// Gets the number of data-context nodes in the selected scope.
        /// </summary>
        public int DataContextCount { get; }

        /// <summary>
        /// Gets the number of finding records in the selected scope.
        /// </summary>
        public int HotlistFindingCount { get; }
    }
}