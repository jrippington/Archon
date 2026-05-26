namespace Archon.Application.Management
{
    /// <summary>
    /// Represents one sanitized dependency readiness check.
    /// </summary>
    /// <param name="Name">The public dependency name, such as graph persistence or rule catalog.</param>
    /// <param name="Status">The dependency readiness status without sensitive connection details.</param>
    /// <param name="Message">The safe explanation for the dependency status.</param>
    public sealed record DependencyReadinessResponse(string Name, string Status, string Message);
}
