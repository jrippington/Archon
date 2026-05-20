using System.Globalization;
using System.Text.Json.Serialization;

namespace Archon.Domain.Graph.Model
{
    /// <summary>
    /// Represents deterministic confidence for an extracted graph fact as a decimal value between zero and one.
    /// </summary>
    /// <remarks>
    /// Confidence is a normalized first-class graph field, not metadata. It lets later query, rule, and reporting logic compare fact certainty without interpreting provider-specific strings.
    /// </remarks>
    public readonly record struct Confidence : IComparable<Confidence>
    {
        /// <summary>
        /// Gets a low-confidence convenience value.
        /// </summary>
        public static readonly Confidence Low = new(0.25m);

        /// <summary>
        /// Gets a medium-confidence convenience value.
        /// </summary>
        public static readonly Confidence Medium = new(0.50m);

        /// <summary>
        /// Gets a high-confidence convenience value.
        /// </summary>
        public static readonly Confidence High = new(0.90m);

        /// <summary>
        /// Gets a certain-confidence convenience value.
        /// </summary>
        public static readonly Confidence Certain = new(1.00m);

        /// <summary>
        /// Initializes a new instance of the <see cref="Confidence"/> struct.
        /// </summary>
        /// <param name="value">The normalized confidence value from zero through one.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is outside the inclusive zero-to-one range.</exception>
        [JsonConstructor]
        public Confidence(decimal value)
        {
            // Confidence is bounded so deterministic comparisons and threshold rules are always meaningful.
            if (value < 0m || value > 1m)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Confidence must be between 0 and 1 inclusive.");
            }

            Value = value;
        }

        /// <summary>
        /// Gets the normalized confidence value from zero through one.
        /// </summary>
        public decimal Value { get; }

        /// <summary>
        /// Compares this confidence value with another confidence value.
        /// </summary>
        /// <param name="other">The other confidence value to compare.</param>
        /// <returns>A negative value when this confidence is lower, zero when equal, or a positive value when higher.</returns>
        public int CompareTo(Confidence other)
        {
            // Decimal comparison is deterministic and avoids floating-point precision surprises for confidence thresholds.
            return Value.CompareTo(other.Value);
        }

        /// <summary>
        /// Returns the invariant string representation of the confidence value.
        /// </summary>
        /// <returns>The confidence value formatted with invariant culture.</returns>
        public override string ToString()
        {
            // Invariant formatting keeps serialized diagnostics stable across developer machines and locales.
            return Value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
