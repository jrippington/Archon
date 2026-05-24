using Archon.Domain.Graph.Metadata;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents the controlled public detail shape for one persisted rule catalog entry.
    /// </summary>
    public sealed class RuleDetailDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleDetailDto"/> class.
        /// </summary>
        /// <param name="item">The list-shape rule fields shared with catalog results.</param>
        /// <param name="description">The full rule description.</param>
        /// <param name="definitionJson">The normalized data-only rule definition JSON.</param>
        /// <param name="sourceUrls">The source URLs authored with the rule.</param>
        /// <param name="impact">The impact statements authored with the rule.</param>
        /// <param name="evidenceRequirements">The evidence requirements authored with the rule.</param>
        /// <param name="recommendedActions">The recommended actions authored with the rule.</param>
        /// <param name="metadata">The credential-safe lower camel case metadata returned for the rule.</param>
        public RuleDetailDto(
            RuleCatalogItemDto item,
            string description,
            string definitionJson,
            IEnumerable<string> sourceUrls,
            IEnumerable<string> impact,
            IEnumerable<string> evidenceRequirements,
            IEnumerable<string> recommendedActions,
            GraphMetadata metadata)
        {
            // Detail DTOs include authored explanatory fields while keeping infrastructure-only fields, such as runtime file paths, out of the API shape.
            Item = item ?? throw new ArgumentNullException(nameof(item));
            Description = string.IsNullOrWhiteSpace(description) ? item.Summary : description.Trim();
            DefinitionJson = string.IsNullOrWhiteSpace(definitionJson) ? "{}" : definitionJson.Trim();
            SourceUrls = NormalizeTextList(sourceUrls);
            Impact = NormalizeTextList(impact);
            EvidenceRequirements = NormalizeTextList(evidenceRequirements);
            RecommendedActions = NormalizeTextList(recommendedActions);
            Metadata = metadata ?? GraphMetadata.Empty;
        }

        /// <summary>Gets the list-shape rule fields shared with catalog results.</summary>
        public RuleCatalogItemDto Item { get; }

        /// <summary>Gets the full rule description.</summary>
        public string Description { get; }

        /// <summary>Gets the normalized data-only rule definition JSON.</summary>
        public string DefinitionJson { get; }

        /// <summary>Gets the source URLs authored with the rule.</summary>
        public IReadOnlyList<string> SourceUrls { get; }

        /// <summary>Gets the impact statements authored with the rule.</summary>
        public IReadOnlyList<string> Impact { get; }

        /// <summary>Gets the evidence requirements authored with the rule.</summary>
        public IReadOnlyList<string> EvidenceRequirements { get; }

        /// <summary>Gets the recommended actions authored with the rule.</summary>
        public IReadOnlyList<string> RecommendedActions { get; }

        /// <summary>Gets credential-safe lower camel case metadata returned for the rule.</summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Normalizes optional authored text into a sorted immutable sequence.
        /// </summary>
        /// <param name="values">The authored values to normalize.</param>
        /// <returns>A sorted sequence of non-empty values.</returns>
        private static IReadOnlyList<string> NormalizeTextList(IEnumerable<string> values)
        {
            // Sorting keeps responses deterministic regardless of original JSON array ordering differences.
            return values.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value.Trim()).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        }
    }
}
