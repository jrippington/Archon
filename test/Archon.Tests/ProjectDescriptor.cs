namespace Archon.Tests
{
    /// <summary>
    /// Describes a discovered project and the metadata needed for architecture-boundary tests.
    /// </summary>
    /// <param name="Name">The project name derived from the `.csproj` file name.</param>
    /// <param name="Identity">The deterministic repository-root-relative path used as the project identity.</param>
    /// <param name="FullPath">The absolute project file path used for reading project metadata.</param>
    /// <param name="Layer">The WP001 architectural layer assigned to the project.</param>
    /// <param name="References">The normalized project references declared by the project file.</param>
    internal sealed record ProjectDescriptor(
        string Name,
        string Identity,
        string FullPath,
        ProjectLayer Layer,
        IReadOnlyList<ProjectReferenceDescriptor> References)
    {
        /// <summary>
        /// Gets the project name derived from the `.csproj` file name.
        /// </summary>
        public string Name { get; } = Name;

        /// <summary>
        /// Gets the deterministic repository-root-relative path used as the project identity.
        /// </summary>
        public string Identity { get; } = Identity;

        /// <summary>
        /// Gets the absolute project file path used for reading project metadata during tests.
        /// </summary>
        public string FullPath { get; } = FullPath;

        /// <summary>
        /// Gets the WP001 architectural layer assigned to the project.
        /// </summary>
        public ProjectLayer Layer { get; } = Layer;

        /// <summary>
        /// Gets the normalized project references declared by the project file.
        /// </summary>
        public IReadOnlyList<ProjectReferenceDescriptor> References { get; } = References;
    }
}
