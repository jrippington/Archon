namespace Archon.Application.Projects
{
    /// <summary>
    /// Represents a safe warning that explains partial project query output.
    /// </summary>
    /// <param name="Code">The stable warning code.</param>
    /// <param name="Message">The safe warning message.</param>
    public sealed record ProjectWarningDto(string Code, string Message)
    {
        // Warnings are public response metadata, so they carry fixed codes rather than exception or adapter details.
    }
}
