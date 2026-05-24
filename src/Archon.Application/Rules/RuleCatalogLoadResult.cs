namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents the complete result of loading and validating a runtime rule catalog folder.
    /// </summary>
    public sealed class RuleCatalogLoadResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleCatalogLoadResult"/> class.
        /// </summary>
        /// <param name="rules">The validated rule entries that can be exposed through catalog behavior.</param>
        /// <param name="diagnostics">The deterministic diagnostics produced during loading and validation.</param>
        public RuleCatalogLoadResult(IEnumerable<RuleCatalogEntry> rules, IEnumerable<RuleCatalogDiagnostic> diagnostics)
        {
            // Arrays keep result ordering deterministic and prevent mutation after the loader returns.
            Rules = (rules ?? throw new ArgumentNullException(nameof(rules))).OrderBy(static rule => rule.RuleCode, StringComparer.Ordinal).ThenBy(static rule => rule.Version, StringComparer.Ordinal).ToArray();
            Diagnostics = (diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray();
        }

        /// <summary>
        /// Gets the validated rule entries that can be exposed through catalog behavior.
        /// </summary>
        public IReadOnlyList<RuleCatalogEntry> Rules { get; }

        /// <summary>
        /// Gets the deterministic diagnostics produced during loading and validation.
        /// </summary>
        public IReadOnlyList<RuleCatalogDiagnostic> Diagnostics { get; }

        /// <summary>
        /// Gets a value indicating whether loading and validation completed without diagnostics.
        /// </summary>
        public bool IsValid
        {
            get
            {
                // A catalog is valid only when every parsed file and cross-file identity check succeeded.
                return Diagnostics.Count == 0;
            }
        }
    }
}
