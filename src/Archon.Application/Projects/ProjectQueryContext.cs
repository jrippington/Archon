namespace Archon.Application.Projects
{
    /// <summary>
    /// Carries scope, snapshot, warning, and unknown metadata shared by project query results.
    /// </summary>
    /// <param name="Scope">The repository and optional solution scope applied to the query.</param>
    /// <param name="Snapshot">The resolved snapshot metadata used to build the result.</param>
    /// <param name="Warnings">The safe warnings that explain partial result content.</param>
    /// <param name="Unknowns">The explicit unknown fields that distinguish unavailable data from empty values.</param>
    public sealed record ProjectQueryContext(ProjectScopeDto Scope, ProjectSnapshotMetadataDto Snapshot, IReadOnlyList<ProjectWarningDto> Warnings, IReadOnlyList<ProjectUnknownDto> Unknowns)
    {
        // The context is later mapped into the API envelope so every project query returns consistent metadata.
    }
}
