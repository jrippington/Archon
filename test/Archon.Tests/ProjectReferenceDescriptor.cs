namespace Archon.Tests
{
    /// <summary>
    /// Describes a normalized project-to-project reference discovered from a `.csproj` file.
    /// </summary>
    /// <param name="IncludePath">The project-file-relative include path exactly as represented after normalization.</param>
    /// <param name="TargetIdentity">The repository-root-relative identity of the referenced project.</param>
    internal sealed record ProjectReferenceDescriptor(string IncludePath, string TargetIdentity)
    {
        /// <summary>
        /// Gets the normalized include path that appeared in the source project file.
        /// </summary>
        public string IncludePath { get; } = IncludePath;

        /// <summary>
        /// Gets the repository-root-relative identity of the referenced project.
        /// </summary>
        public string TargetIdentity { get; } = TargetIdentity;
    }
}
