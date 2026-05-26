namespace Archon.Application.Projects
{
    /// <summary>
    /// Represents one deterministic validation or conflict error produced by project query handling.
    /// </summary>
    /// <param name="Code">The stable machine-readable validation code.</param>
    /// <param name="Message">The safe human-readable validation message.</param>
    public sealed record ProjectQueryValidationError(string Code, string Message)
    {
        // Validation errors become public problem details, so both code and message must be meaningful and non-empty.
    }
}
