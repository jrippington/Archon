namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Carries safe snapshot context needed by MCP current resources.
    /// </summary>
    /// <param name="SnapshotStableKey">The selected snapshot stable key.</param>
    /// <param name="RepositoryStableKey">The repository stable key that owns the selected snapshot.</param>
    /// <param name="SolutionStableKeys">The solution stable keys present in the selected snapshot.</param>
    /// <param name="BranchName">The optional source-control branch name recorded by the snapshot.</param>
    /// <param name="CommitSha">The optional source-control commit SHA recorded by the snapshot.</param>
    /// <param name="StartedUtc">The time extraction started.</param>
    /// <param name="CompletedUtc">The optional time extraction completed.</param>
    /// <param name="Status">The snapshot lifecycle status.</param>
    /// <param name="NodeCount">The number of architecture nodes in the snapshot.</param>
    /// <param name="EdgeCount">The number of architecture edges in the snapshot.</param>
    /// <param name="RuleCount">The number of rule definitions in the snapshot.</param>
    /// <param name="FindingCount">The number of findings in the snapshot.</param>
    /// <param name="MetricCount">The number of metrics in the snapshot.</param>
    /// <param name="EvidenceCount">The number of evidence records in the snapshot.</param>
    /// <param name="WarningCount">The number of extraction warnings in the snapshot.</param>
    /// <param name="ErrorCount">The number of extraction errors in the snapshot.</param>
    public sealed record ArchonMcpCurrentSnapshotContext(
        string SnapshotStableKey,
        string RepositoryStableKey,
        IReadOnlyList<string> SolutionStableKeys,
        string? BranchName,
        string? CommitSha,
        DateTimeOffset StartedUtc,
        DateTimeOffset? CompletedUtc,
        string Status,
        int NodeCount,
        int EdgeCount,
        int RuleCount,
        int FindingCount,
        int MetricCount,
        int EvidenceCount,
        int WarningCount,
        int ErrorCount);
}
