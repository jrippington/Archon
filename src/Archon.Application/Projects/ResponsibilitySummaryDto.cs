namespace Archon.Application.Projects
{
    /// <summary>
    /// Describes one inferred project responsibility for project detail responses.
    /// </summary>
    /// <param name="Name">The stable responsibility name.</param>
    /// <param name="Description">The developer-facing explanation for the responsibility.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys supporting the responsibility.</param>
    public sealed record ResponsibilitySummaryDto(string Name, string Description, IReadOnlyList<string> EvidenceStableKeys)
    {
        // Responsibility summaries use evidence references rather than source snippets so detail responses stay safe and compact.
    }
}
