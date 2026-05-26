namespace Archon.Application.Management
{
    /// <summary>
    /// Captures extraction run-history filters exposed through the management API.
    /// </summary>
    /// <param name="Take">The optional maximum number of run-history rows to return.</param>
    /// <param name="Status">The optional lifecycle status filter.</param>
    public sealed record ExtractionRunHistoryQuery(int? Take, string? Status);
}
