using System.Text;
using Archon.Domain.Graph.Metadata;

namespace Archon.Domain.Graph.Identity
{
    /// <summary>
    /// Builds deterministic canonical fingerprint input from record category, named fields, and canonical metadata.
    /// </summary>
    /// <remarks>
    /// This type is intentionally explicit about diff-relevant fields. Callers should add only logical graph content, not database IDs, machine-local paths, process IDs, timestamps that are not part of the fact, or other non-diff values.
    /// </remarks>
    public sealed class FingerprintInput
    {
        /// <summary>
        /// Stores the graph record category, such as Node, Edge, Evidence, Finding, Metric, or GeneratedSummary.
        /// </summary>
        private readonly string _recordCategory;

        /// <summary>
        /// Stores diff-relevant field values in ordinal field-name order for deterministic canonicalization.
        /// </summary>
        private readonly SortedDictionary<string, string> _fields = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores the canonical metadata payload that participates in the fingerprint.
        /// </summary>
        private GraphMetadata _metadata = GraphMetadata.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="FingerprintInput"/> class.
        /// </summary>
        /// <param name="recordCategory">The graph record category represented by this fingerprint input.</param>
        private FingerprintInput(string recordCategory)
        {
            // Record category separates otherwise identical field sets across graph fact types.
            _recordCategory = recordCategory;
        }

        /// <summary>
        /// Creates a fingerprint input builder for one graph record category.
        /// </summary>
        /// <param name="recordCategory">The graph record category represented by this fingerprint input.</param>
        /// <returns>A fingerprint input builder for the supplied category.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="recordCategory"/> is null, empty, or whitespace-only.</exception>
        public static FingerprintInput Create(string? recordCategory)
        {
            // The category is part of canonical input so node and edge content cannot collide accidentally.
            return new FingerprintInput(RequireText(recordCategory, nameof(recordCategory)));
        }

        /// <summary>
        /// Adds a diff-relevant field to the fingerprint input.
        /// </summary>
        /// <param name="name">The stable field name.</param>
        /// <param name="value">The stable field value; null values are represented explicitly.</param>
        /// <returns>The current builder so calls can be chained.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is invalid or duplicated.</exception>
        public FingerprintInput AddField(string? name, string? value)
        {
            // Field names are sorted later, but duplicates are rejected because they make input ambiguous.
            string normalizedName = RequireText(name, nameof(name));
            string normalizedValue = value ?? "<null>";
            if (!_fields.TryAdd(normalizedName, normalizedValue))
            {
                throw new ArgumentException($"Fingerprint input already contains field '{normalizedName}'.", nameof(name));
            }

            return this;
        }

        /// <summary>
        /// Adds a diff-relevant field to the fingerprint input from a stable key value.
        /// </summary>
        /// <param name="name">The stable field name.</param>
        /// <param name="value">The stable key value to include.</param>
        /// <returns>The current builder so calls can be chained.</returns>
        public FingerprintInput AddField(string? name, StableKey value)
        {
            // Stable keys are already deterministic external strings and can be included directly.
            return AddField(name, value.Value);
        }

        /// <summary>
        /// Adds a diff-relevant field to the fingerprint input from a boolean value.
        /// </summary>
        /// <param name="name">The stable field name.</param>
        /// <param name="value">The boolean value to include.</param>
        /// <returns>The current builder so calls can be chained.</returns>
        public FingerprintInput AddField(string? name, bool value)
        {
            // Boolean values are lower-case to match JSON canonical conventions.
            return AddField(name, value ? "true" : "false");
        }

        /// <summary>
        /// Adds a diff-relevant field to the fingerprint input from an optional integer value.
        /// </summary>
        /// <param name="name">The stable field name.</param>
        /// <param name="value">The optional integer value to include.</param>
        /// <returns>The current builder so calls can be chained.</returns>
        public FingerprintInput AddField(string? name, int? value)
        {
            // Nullable numeric fields are represented explicitly so missing and zero are not conflated.
            return AddField(name, value?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Adds a diff-relevant field to the fingerprint input from an optional decimal value.
        /// </summary>
        /// <param name="name">The stable field name.</param>
        /// <param name="value">The optional decimal value to include.</param>
        /// <returns>The current builder so calls can be chained.</returns>
        public FingerprintInput AddField(string? name, decimal? value)
        {
            // Decimal values use invariant formatting so metric values hash the same across cultures and machines.
            return AddField(name, value?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Adds canonical metadata to the fingerprint input.
        /// </summary>
        /// <param name="metadata">The canonical metadata value to include.</param>
        /// <returns>The current builder so calls can be chained.</returns>
        public FingerprintInput AddMetadata(GraphMetadata metadata)
        {
            // Metadata is kept separate from fields to make its role in fingerprint input explicit.
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            return this;
        }

        /// <summary>
        /// Converts the builder state into deterministic canonical text used for hashing.
        /// </summary>
        /// <returns>A canonical text representation of category, fields, and metadata.</returns>
        public string ToCanonicalText()
        {
            // The format is intentionally simple, line-delimited, and ordinally sorted to remain stable across runtimes.
            StringBuilder builder = new();
            builder.Append("category=").Append(_recordCategory).Append('\n');
            foreach (KeyValuePair<string, string> field in _fields)
            {
                builder.Append("field:").Append(field.Key).Append('=').Append(field.Value).Append('\n');
            }

            builder.Append("metadata=").Append(_metadata.ToCanonicalJson()).Append('\n');
            return builder.ToString();
        }

        /// <summary>
        /// Requires a non-empty canonical text component.
        /// </summary>
        /// <param name="value">The candidate text component.</param>
        /// <param name="parameterName">The source parameter name to report in validation failures.</param>
        /// <returns>The trimmed text component.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Canonical input names and categories must be explicit to keep hash input explainable.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Fingerprint input text cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }
    }
}
