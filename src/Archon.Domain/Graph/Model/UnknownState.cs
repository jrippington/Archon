using System.Text.Json.Serialization;

namespace Archon.Domain.Graph.Model
{
    /// <summary>
    /// Represents explicit unknown-data state for a graph fact.
    /// </summary>
    /// <remarks>
    /// Unknown state is intentionally explicit because Archon treats uncertainty as useful architecture information. A fact can be known, or it can declare that some data is unknown together with the reason that extraction could not determine it.
    /// </remarks>
    public sealed class UnknownState : IEquatable<UnknownState>
    {
        /// <summary>
        /// Gets the singleton state for graph facts without unknown data.
        /// </summary>
        public static readonly UnknownState Known = new(false, null);

        /// <summary>
        /// Initializes a new instance of the <see cref="UnknownState"/> class.
        /// </summary>
        /// <param name="hasUnknownData">A value indicating whether the graph fact contains unknown data.</param>
        /// <param name="unknownReason">The reason data is unknown when <paramref name="hasUnknownData"/> is <see langword="true"/>.</param>
        /// <exception cref="ArgumentException">Thrown when unknown data is present without a non-empty reason.</exception>
        [JsonConstructor]
        public UnknownState(bool hasUnknownData, string? unknownReason)
        {
            // Unknown information must be explainable so consumers can distinguish true absence from extractor limitations.
            if (hasUnknownData && string.IsNullOrWhiteSpace(unknownReason))
            {
                throw new ArgumentException("Unknown data requires a non-empty unknown reason.", nameof(unknownReason));
            }

            HasUnknownData = hasUnknownData;
            UnknownReason = hasUnknownData ? unknownReason!.Trim() : null;
        }

        /// <summary>
        /// Gets a value indicating whether the graph fact contains unknown data.
        /// </summary>
        public bool HasUnknownData { get; }

        /// <summary>
        /// Gets the reason data is unknown when <see cref="HasUnknownData"/> is <see langword="true"/>.
        /// </summary>
        public string? UnknownReason { get; }

        /// <summary>
        /// Creates an unknown state with a required reason.
        /// </summary>
        /// <param name="reason">The reason the graph fact contains unknown data.</param>
        /// <returns>An unknown state that carries the supplied reason.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="reason"/> is null, empty, or whitespace-only.</exception>
        public static UnknownState Unknown(string? reason)
        {
            // The factory improves readability where tests or extractors intentionally model unknown data.
            return new UnknownState(true, reason);
        }

        /// <summary>
        /// Determines whether this unknown state is equal to another unknown state.
        /// </summary>
        /// <param name="other">The other unknown state to compare.</param>
        /// <returns><see langword="true"/> when the unknown flag and reason match; otherwise, <see langword="false"/>.</returns>
        public bool Equals(UnknownState? other)
        {
            // Unknown-state equality is value based so fact models can be compared deterministically in tests.
            return other is not null
                && HasUnknownData == other.HasUnknownData
                && StringComparer.Ordinal.Equals(UnknownReason, other.UnknownReason);
        }

        /// <summary>
        /// Determines whether this unknown state is equal to another object.
        /// </summary>
        /// <param name="obj">The object to compare with this instance.</param>
        /// <returns><see langword="true"/> when <paramref name="obj"/> is an equal unknown state; otherwise, <see langword="false"/>.</returns>
        public override bool Equals(object? obj)
        {
            // The object overload delegates to typed equality for collection and assertion consistency.
            return obj is UnknownState other && Equals(other);
        }

        /// <summary>
        /// Gets a hash code based on the unknown flag and reason.
        /// </summary>
        /// <returns>A hash code for dictionary or set usage.</returns>
        public override int GetHashCode()
        {
            // Hash code fields match equality fields.
            return HashCode.Combine(HasUnknownData, UnknownReason is null ? 0 : StringComparer.Ordinal.GetHashCode(UnknownReason));
        }
    }
}
