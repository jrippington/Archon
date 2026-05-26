namespace Archon.Application.Management
{
    /// <summary>
    /// Captures snapshot lifecycle filters exposed through the management API.
    /// </summary>
    /// <param name="RepositoryStableKey">The optional repository stable key filter.</param>
    /// <param name="SolutionStableKey">The optional solution stable key filter.</param>
    /// <param name="Status">The optional snapshot lifecycle status filter.</param>
    /// <param name="FromUtc">The optional inclusive started-after timestamp filter.</param>
    /// <param name="ToUtc">The optional inclusive started-before timestamp filter.</param>
    /// <param name="CommitSha">The optional source-control commit SHA filter.</param>
    /// <param name="Take">The optional maximum number of lifecycle rows to return.</param>
    public sealed record SnapshotLifecycleQuery(
        string? RepositoryStableKey,
        string? SolutionStableKey,
        string? Status,
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtc,
        string? CommitSha,
        int? Take);
}
