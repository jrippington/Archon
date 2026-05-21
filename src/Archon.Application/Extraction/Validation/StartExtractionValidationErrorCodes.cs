namespace Archon.Application.Extraction.Validation
{
    /// <summary>
    /// Provides stable validation error codes for extraction start request failures.
    /// </summary>
    public static class StartExtractionValidationErrorCodes
    {
        /// <summary>
        /// Identifies a missing or blank repository root directory value.
        /// </summary>
        public const string RepositoryRootRequired = nameof(RepositoryRootRequired);

        /// <summary>
        /// Identifies a repository root directory that does not exist.
        /// </summary>
        public const string RepositoryRootNotFound = nameof(RepositoryRootNotFound);

        /// <summary>
        /// Identifies a missing, blank, or empty solution path list.
        /// </summary>
        public const string SolutionPathRequired = nameof(SolutionPathRequired);

        /// <summary>
        /// Identifies a solution path that resolves outside the submitted repository root.
        /// </summary>
        public const string SolutionPathOutsideRepositoryRoot = nameof(SolutionPathOutsideRepositoryRoot);

        /// <summary>
        /// Identifies a solution path whose file does not exist.
        /// </summary>
        public const string SolutionPathNotFound = nameof(SolutionPathNotFound);

        /// <summary>
        /// Identifies a solution path that uses an unsupported file extension.
        /// </summary>
        public const string SolutionPathExtensionInvalid = nameof(SolutionPathExtensionInvalid);

        /// <summary>
        /// Identifies repeated solution paths after normalization.
        /// </summary>
        public const string SolutionPathDuplicate = nameof(SolutionPathDuplicate);
    }
}
