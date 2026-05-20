namespace Archon.Domain.Graph.Identity
{
    /// <summary>
    /// Represents a deterministic content fingerprint used to detect diff-relevant graph fact changes.
    /// </summary>
    /// <remarks>
    /// A fingerprint is a content hash derived from normalized graph fields and canonical metadata. It must not include database IDs, process-local values, machine-local paths, or other values that do not describe logical architecture content.
    /// </remarks>
    public readonly record struct Fingerprint
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Fingerprint"/> struct.
        /// </summary>
        /// <param name="value">The deterministic fingerprint string.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, or whitespace-only.</exception>
        public Fingerprint(string? value)
        {
            // Fingerprints must be explicit because blank fingerprints cannot support snapshot diff comparisons.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Fingerprints cannot be null, empty, or whitespace.", nameof(value));
            }

            Value = value;
        }

        /// <summary>
        /// Gets the deterministic fingerprint string.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Returns the deterministic fingerprint string for diagnostics and display.
        /// </summary>
        /// <returns>The deterministic fingerprint string.</returns>
        public override string ToString()
        {
            // ToString mirrors Value so diagnostics show the exact persisted fingerprint representation.
            return Value;
        }
    }
}
