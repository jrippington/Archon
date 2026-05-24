namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents a controlled affected-node reference returned by finding and hotlist queries.
    /// </summary>
    public sealed class AffectedNodeReferenceDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AffectedNodeReferenceDto"/> class.
        /// </summary>
        /// <param name="stableKey">The stable architecture-node key.</param>
        /// <param name="displayName">The safe display name for the node.</param>
        /// <param name="nodeKind">The optional architecture-node kind when known.</param>
        /// <param name="projectStableKey">The optional project stable key associated with the node.</param>
        public AffectedNodeReferenceDto(string stableKey, string displayName, string? nodeKind, string? projectStableKey)
        {
            // Node references expose stable identity and safe labels instead of unrestricted node property maps.
            StableKey = RequireText(stableKey, nameof(stableKey));
            DisplayName = RequireText(displayName, nameof(displayName));
            NodeKind = string.IsNullOrWhiteSpace(nodeKind) ? null : nodeKind.Trim();
            ProjectStableKey = string.IsNullOrWhiteSpace(projectStableKey) ? null : projectStableKey.Trim();
        }

        /// <summary>Gets the stable architecture-node key.</summary>
        public string StableKey { get; }

        /// <summary>Gets the safe display name for the node.</summary>
        public string DisplayName { get; }

        /// <summary>Gets the optional architecture-node kind when known.</summary>
        public string? NodeKind { get; }

        /// <summary>Gets the optional project stable key associated with the node.</summary>
        public string? ProjectStableKey { get; }

        /// <summary>
        /// Requires non-empty affected-node reference text.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <param name="parameterName">The parameter name used for invalid input reporting.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Public references need deterministic identity and display text so consumers never need raw graph access.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }
}
