namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Represents deterministic Roslyn symbol identity projected into extraction-friendly text fields.
    /// </summary>
    /// <remarks>
    /// Roslyn symbol instances are bound to a compilation and cannot be persisted directly. This model captures the stable names needed to build graph facts, evidence, and containment relationships without leaking compiler object lifetimes.
    /// </remarks>
    public sealed class SemanticSymbolIdentity
    {
        /// <summary>
        /// Initializes a new semantic symbol identity.
        /// </summary>
        /// <param name="metadataName">The compiler-facing metadata name or signature that distinguishes overloads where available.</param>
        /// <param name="displayName">The developer-facing symbol display name.</param>
        /// <param name="fullyQualifiedName">The fully qualified symbol name used for graph identity and search.</param>
        /// <param name="containingSymbolName">The fully qualified containing symbol name, or <see langword="null" /> when the symbol is a root declaration.</param>
        public SemanticSymbolIdentity(
            string metadataName,
            string displayName,
            string fullyQualifiedName,
            string? containingSymbolName)
        {
            // The identity keeps both display and fully qualified forms because graph nodes need stable identity and readable names.
            MetadataName = RequireText(metadataName, nameof(metadataName));
            DisplayName = RequireText(displayName, nameof(displayName));
            FullyQualifiedName = RequireText(fullyQualifiedName, nameof(fullyQualifiedName));
            ContainingSymbolName = NormalizeOptionalText(containingSymbolName);
        }

        /// <summary>
        /// Gets the compiler-facing metadata name or signature that distinguishes overloads where available.
        /// </summary>
        public string MetadataName { get; }

        /// <summary>
        /// Gets the developer-facing symbol display name.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets the fully qualified symbol name used for graph identity and search.
        /// </summary>
        public string FullyQualifiedName { get; }

        /// <summary>
        /// Gets the fully qualified containing symbol name, or <see langword="null" /> when the symbol is a root declaration.
        /// </summary>
        public string? ContainingSymbolName { get; }

        /// <summary>
        /// Requires non-empty symbol text before the identity can participate in stable-key generation.
        /// </summary>
        /// <param name="value">The symbol text supplied by an extractor.</param>
        /// <param name="parameterName">The source parameter name used in validation failures.</param>
        /// <returns>The trimmed symbol text.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Stable graph identity cannot be built from missing symbol text, so blank values fail fast at model boundaries.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Semantic symbol identity values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }

        /// <summary>
        /// Normalizes optional symbol text while preserving null when no containing symbol exists.
        /// </summary>
        /// <param name="value">The optional symbol text supplied by an extractor.</param>
        /// <returns>The trimmed text, or <see langword="null" /> when the supplied value is blank.</returns>
        private static string? NormalizeOptionalText(string? value)
        {
            // Optional symbol fields should not carry whitespace because metadata and evidence compare by exact string value.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
