using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Maps validated application-layer rule catalog entries into generalized graph rule definitions.
    /// </summary>
    public static class RuleCatalogEntryMapper
    {
        /// <summary>
        /// Converts a validated catalog entry into the domain model used by snapshot accumulation and persistence.
        /// </summary>
        /// <param name="entry">The validated rule catalog entry to convert.</param>
        /// <returns>A generalized graph rule definition preserving the catalog entry identity and authored payload.</returns>
        public static RuleDefinition ToRuleDefinition(RuleCatalogEntry entry)
        {
            // The generalized graph model currently has a lifecycle-style finding status, so WP012-specific default statuses are preserved in definition JSON and metadata while persisted catalog rows get a safe query status.
            ArgumentNullException.ThrowIfNull(entry);
            return new RuleDefinition(
                entry.RuleCode,
                entry.Name,
                entry.Category,
                entry.Severity,
                MapDefaultStatus(entry.DefaultStatus),
                entry.Enabled,
                entry.Version,
                entry.Description,
                entry.DefinitionJson,
                entry.SourceUrls,
                entry.IsBuiltIn,
                entry.OwnerScope,
                entry.Metadata);
        }

        /// <summary>
        /// Maps WP012 rule default statuses onto the existing graph finding-status vocabulary for persistence compatibility.
        /// </summary>
        /// <param name="status">The WP012 rule-authored default status.</param>
        /// <returns>The compatible graph finding status used by the current generalized graph contract.</returns>
        private static FindingStatus MapDefaultStatus(RuleFindingStatus status)
        {
            // All specific rule statuses describe active catalog classifications in this slice; only explicit Unknown maps to the graph Unknown status.
            ArgumentNullException.ThrowIfNull(status);
            return status == RuleFindingStatus.Unknown ? FindingStatus.Unknown : FindingStatus.Open;
        }
    }
}
