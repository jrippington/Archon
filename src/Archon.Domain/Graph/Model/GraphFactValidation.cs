using Archon.Domain.Graph.ControlledValues;

namespace Archon.Domain.Graph.Model
{
    /// <summary>
    /// Provides shared validation helpers for WP002 graph fact domain contracts.
    /// </summary>
    internal static class GraphFactValidation
    {
        /// <summary>
        /// Ensures a required string value is not null, empty, or whitespace-only.
        /// </summary>
        /// <param name="value">The string value to validate.</param>
        /// <param name="parameterName">The constructor or method parameter name associated with the value.</param>
        /// <returns>The trimmed string value.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, or whitespace-only.</exception>
        internal static string RequiredString(string? value, string parameterName)
        {
            // Graph contracts store normalized required text so equivalent facts compare cleanly.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{parameterName} is required.", parameterName);
            }

            return value.Trim();
        }

        /// <summary>
        /// Normalizes an optional string value by trimming whitespace and converting blank values to null.
        /// </summary>
        /// <param name="value">The optional string value to normalize.</param>
        /// <returns>The trimmed string value, or <see langword="null"/> when the input was absent or blank.</returns>
        internal static string? OptionalString(string? value)
        {
            // Optional graph fields use null for absence rather than preserving accidental whitespace.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Ensures knowledge and unknown-state fields satisfy WP002 unknown-reason invariants.
        /// </summary>
        /// <param name="knowledgeKind">The knowledge classification assigned to the graph fact.</param>
        /// <param name="unknownState">The explicit unknown state assigned to the graph fact.</param>
        /// <param name="factName">A developer-facing name for the fact being validated.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="knowledgeKind"/> or <paramref name="unknownState"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when an unknown classification has no unknown reason.</exception>
        internal static void RequireUnknownReasonWhenNeeded(KnowledgeKind knowledgeKind, UnknownState unknownState, string factName)
        {
            // KnowledgeKind.Unknown and HasUnknownData both require a reason so uncertainty is never silent.
            ArgumentNullException.ThrowIfNull(knowledgeKind);
            ArgumentNullException.ThrowIfNull(unknownState);

            if (knowledgeKind == KnowledgeKind.Unknown && string.IsNullOrWhiteSpace(unknownState.UnknownReason))
            {
                throw new ArgumentException($"{factName} uses unknown knowledge and requires a non-empty unknown reason.", nameof(unknownState));
            }
        }

        /// <summary>
        /// Ensures a stable-key value object contains a non-empty external value.
        /// </summary>
        /// <param name="stableKey">The stable key to validate.</param>
        /// <param name="parameterName">The constructor or method parameter name associated with the stable key.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="stableKey"/> carries a null, empty, or whitespace-only value.</exception>
        internal static void RequireStableKey(Identity.StableKey stableKey, string parameterName)
        {
            // Default value-object instances can bypass the StableKey constructor, so graph endpoints re-check the stored value.
            if (string.IsNullOrWhiteSpace(stableKey.Value))
            {
                throw new ArgumentException($"{parameterName} is required.", parameterName);
            }
        }

        /// <summary>
        /// Ensures optional source line values are positive and form a valid inclusive range when both values are present.
        /// </summary>
        /// <param name="startLine">The optional starting line number.</param>
        /// <param name="endLine">The optional ending line number.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a supplied line number is not positive.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="endLine"/> is earlier than <paramref name="startLine"/>.</exception>
        internal static void RequireLineRange(int? startLine, int? endLine)
        {
            // Line numbers are one-based in source files and must keep their natural inclusive ordering.
            if (startLine <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startLine), startLine, "Start line must be positive when supplied.");
            }

            if (endLine <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(endLine), endLine, "End line must be positive when supplied.");
            }

            if (startLine.HasValue && endLine.HasValue && endLine.Value < startLine.Value)
            {
                throw new ArgumentException("End line cannot be earlier than start line.", nameof(endLine));
            }
        }
    }
}
