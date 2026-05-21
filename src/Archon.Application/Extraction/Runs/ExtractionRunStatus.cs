namespace Archon.Application.Extraction.Runs
{
    /// <summary>
    /// Defines the operational lifecycle states exposed for an extraction run.
    /// </summary>
    public enum ExtractionRunStatus
    {
        /// <summary>
        /// Indicates the request has been accepted but has not yet been queued for background execution.
        /// </summary>
        Accepted = 0,

        /// <summary>
        /// Indicates the accepted request has been queued through the scheduler seam.
        /// </summary>
        Queued = 1,

        /// <summary>
        /// Indicates background extraction work is currently executing.
        /// </summary>
        Running = 2,

        /// <summary>
        /// Indicates extraction and persistence completed successfully.
        /// </summary>
        Completed = 3,

        /// <summary>
        /// Indicates accepted extraction work failed after run creation.
        /// </summary>
        Failed = 4,

        /// <summary>
        /// Indicates accepted extraction work was cancelled before completion.
        /// </summary>
        Cancelled = 5
    }
}
