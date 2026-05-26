namespace Archon.Application.Projects
{
    /// <summary>
    /// Defines stable validation and conflict codes returned by controlled project query services.
    /// </summary>
    public static class ProjectQueryValidationCodes
    {
        /// <summary>
        /// Indicates that a repository stable key was not supplied for a project query.
        /// </summary>
        public const string RepositoryStableKeyRequired = nameof(RepositoryStableKeyRequired);

        /// <summary>
        /// Indicates that a snapshot selector was neither latest/current nor a snapshot stable key.
        /// </summary>
        public const string SnapshotSelectorInvalid = nameof(SnapshotSelectorInvalid);

        /// <summary>
        /// Indicates that the requested repository scope does not exist in persisted snapshots.
        /// </summary>
        public const string RepositoryNotFound = nameof(RepositoryNotFound);

        /// <summary>
        /// Indicates that the requested solution scope does not exist within the repository scope.
        /// </summary>
        public const string SolutionNotFound = nameof(SolutionNotFound);

        /// <summary>
        /// Indicates that the requested snapshot scope does not exist.
        /// </summary>
        public const string SnapshotNotFound = nameof(SnapshotNotFound);

        /// <summary>
        /// Indicates that a project detail request did not supply a stable key or project name.
        /// </summary>
        public const string ProjectIdentityRequired = nameof(ProjectIdentityRequired);

        /// <summary>
        /// Indicates that a project detail request supplied both stable key and project name.
        /// </summary>
        public const string ProjectIdentityAmbiguous = nameof(ProjectIdentityAmbiguous);

        /// <summary>
        /// Indicates that no project matched the requested lookup identity.
        /// </summary>
        public const string ProjectNotFound = nameof(ProjectNotFound);

        /// <summary>
        /// Indicates that a project name matched multiple projects and requires caller disambiguation.
        /// </summary>
        public const string ProjectNameAmbiguous = nameof(ProjectNameAmbiguous);
    }
}
