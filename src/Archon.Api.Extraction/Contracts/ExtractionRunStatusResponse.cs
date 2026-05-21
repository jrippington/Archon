namespace Archon.Api.Extraction.Contracts
{
    /// <summary>
    /// Represents the current API-visible state of an extraction run.
    /// </summary>
    /// <param name="RunId">The stable public run identifier.</param>
    /// <param name="Status">The current lifecycle status name.</param>
    /// <param name="SubmittedRequest">The accepted request summary.</param>
    /// <param name="StartedUtc">The UTC timestamp when the run was accepted.</param>
    /// <param name="CompletedUtc">The optional UTC timestamp when the run reached a terminal state.</param>
    /// <param name="Progress">The current progress details.</param>
    /// <param name="Warnings">The warning diagnostics recorded so far.</param>
    /// <param name="Errors">The error diagnostics recorded so far.</param>
    /// <param name="SnapshotIdentity">The optional persisted snapshot stable identity.</param>
    public sealed record ExtractionRunStatusResponse(
        string RunId,
        string Status,
        ExtractionRunRequestSummaryResponse SubmittedRequest,
        DateTimeOffset StartedUtc,
        DateTimeOffset? CompletedUtc,
        ExtractionRunProgressResponse Progress,
        IReadOnlyList<ExtractionRunDiagnosticResponse> Warnings,
        IReadOnlyList<ExtractionRunDiagnosticResponse> Errors,
        string? SnapshotIdentity);
}
