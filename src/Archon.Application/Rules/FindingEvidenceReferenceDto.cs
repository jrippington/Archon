namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents a controlled evidence reference returned by finding and hotlist queries.
    /// </summary>
    public sealed class FindingEvidenceReferenceDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FindingEvidenceReferenceDto"/> class.
        /// </summary>
        /// <param name="stableKey">The stable evidence key.</param>
        /// <param name="displayName">The safe display text for the evidence reference.</param>
        public FindingEvidenceReferenceDto(string stableKey, string displayName)
        {
            // Evidence references expose identity and display text only; snippets and secret-like payloads are intentionally omitted.
            StableKey = RequireText(stableKey, nameof(stableKey));
            DisplayName = RequireText(displayName, nameof(displayName));
        }

        /// <summary>Gets the stable evidence key.</summary>
        public string StableKey { get; }

        /// <summary>Gets the safe display text for the evidence reference.</summary>
        public string DisplayName { get; }

        /// <summary>
        /// Requires non-empty evidence reference text.
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
