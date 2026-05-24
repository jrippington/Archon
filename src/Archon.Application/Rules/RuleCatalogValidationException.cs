namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents a visible startup or initialization failure caused by invalid built-in rule catalog content.
    /// </summary>
    public sealed class RuleCatalogValidationException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleCatalogValidationException"/> class.
        /// </summary>
        /// <param name="diagnostics">The diagnostics that explain why the catalog is invalid.</param>
        public RuleCatalogValidationException(IEnumerable<RuleCatalogDiagnostic> diagnostics)
            : base(CreateMessage(diagnostics))
        {
            // Preserve diagnostic objects so hosts can log or translate the detailed validation failure safely.
            Diagnostics = diagnostics.ToArray();
        }

        /// <summary>
        /// Gets the diagnostics that explain why the catalog is invalid.
        /// </summary>
        public IReadOnlyList<RuleCatalogDiagnostic> Diagnostics { get; }

        /// <summary>
        /// Creates a deterministic exception message from catalog diagnostics.
        /// </summary>
        /// <param name="diagnostics">The diagnostics to include.</param>
        /// <returns>A single exception message suitable for logs and startup failures.</returns>
        private static string CreateMessage(IEnumerable<RuleCatalogDiagnostic> diagnostics)
        {
            // The message includes each code and location so fail-fast startup failures are visible without inspecting object properties.
            RuleCatalogDiagnostic[] diagnosticArray = diagnostics?.ToArray() ?? [];
            if (diagnosticArray.Length == 0)
            {
                return "Rule catalog validation failed.";
            }

            return "Rule catalog validation failed: " + string.Join(" | ", diagnosticArray.Select(static diagnostic => diagnostic.ToString()));
        }
    }
}
