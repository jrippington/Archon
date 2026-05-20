using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Archon.Domain.Graph.ControlledValues
{
    /// <summary>
    /// Provides the shared smart-enum/value-object behavior for graph controlled values.
    /// </summary>
    /// <typeparam name="TValue">The concrete controlled-value type that owns a closed set of static instances.</typeparam>
    /// <remarks>
    /// A controlled value is a domain-specific string identity that behaves like a value object while avoiding numeric enum ordinals.
    /// Each derived type registers its known instances so external strings can be parsed deterministically back to canonical objects.
    /// </remarks>
    [JsonConverter(typeof(ControlledValueJsonConverterFactory))]
    public abstract class ControlledValue<TValue> : IEquatable<ControlledValue<TValue>>
        where TValue : ControlledValue<TValue>
    {
        /// <summary>
        /// Stores registered values by stable external string for each closed controlled-value type.
        /// </summary>
        private static readonly ConcurrentDictionary<string, TValue> s_valuesByExternalValue = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores registered values in declaration order for deterministic validation, documentation, and test output.
        /// </summary>
        private static readonly List<TValue> s_declaredValues = [];

        /// <summary>
        /// Coordinates writes to the ordered value list because static instances can be registered during type initialization.
        /// </summary>
        private static readonly object s_declarationLock = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="ControlledValue{TValue}"/> class and registers it for parsing.
        /// </summary>
        /// <param name="value">The stable external string used in JSON, persistence, API, MCP, markdown, and tests.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, whitespace-only, or already registered for the same value set.</exception>
        protected ControlledValue(string value)
        {
            // The external value is the durable identity, so invalid strings must be rejected before registration.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Controlled value external strings cannot be null, empty, or whitespace.", nameof(value));
            }

            Value = value;

            // Registration creates a deterministic lookup table while preserving the constructor-declaration order.
            if (!s_valuesByExternalValue.TryAdd(value, (TValue)this))
            {
                throw new ArgumentException($"The controlled value '{value}' is already registered for {typeof(TValue).Name}.", nameof(value));
            }

            lock (s_declarationLock)
            {
                s_declaredValues.Add((TValue)this);
            }
        }

        /// <summary>
        /// Gets the stable external string identity for this controlled value.
        /// </summary>
        /// <remarks>
        /// This value is the only representation that should cross JSON, persistence, API, MCP, markdown, or test-contract boundaries.
        /// </remarks>
        public string Value { get; }

        /// <summary>
        /// Gets every registered controlled value for <typeparamref name="TValue"/> in deterministic declaration order.
        /// </summary>
        /// <returns>A read-only snapshot of registered values for validation and enumeration.</returns>
        public static IReadOnlyList<TValue> All
        {
            get
            {
                // Touching the runtime type ensures static fields have run before the caller reads the registration table.
                EnsureInitialized();

                lock (s_declarationLock)
                {
                    return s_declaredValues.ToArray();
                }
            }
        }

        /// <summary>
        /// Parses a stable external string into the canonical controlled-value instance for <typeparamref name="TValue"/>.
        /// </summary>
        /// <param name="value">The stable external string to resolve.</param>
        /// <returns>The canonical controlled-value instance registered for <paramref name="value"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, whitespace-only, or not registered.</exception>
        public static TValue Parse(string? value)
        {
            // Parse is the strict path used when malformed external input should fail fast.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Controlled value external strings cannot be null, empty, or whitespace.", nameof(value));
            }

            if (TryParse(value, out TValue? parsed))
            {
                return parsed!;
            }

            throw new ArgumentException($"'{value}' is not a registered {typeof(TValue).Name} value.", nameof(value));
        }

        /// <summary>
        /// Attempts to parse a stable external string into the canonical controlled-value instance for <typeparamref name="TValue"/>.
        /// </summary>
        /// <param name="value">The stable external string to resolve.</param>
        /// <param name="result">The parsed controlled value when parsing succeeds; otherwise, <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when <paramref name="value"/> is registered; otherwise, <see langword="false"/>.</returns>
        public static bool TryParse(string? value, [NotNullWhen(true)] out TValue? result)
        {
            // TryParse supports non-throwing validation flows while still rejecting blank values as invalid input.
            EnsureInitialized();

            if (string.IsNullOrWhiteSpace(value))
            {
                result = null;
                return false;
            }

            return s_valuesByExternalValue.TryGetValue(value, out result);
        }

        /// <summary>
        /// Determines whether two controlled values have the same concrete type and stable external string.
        /// </summary>
        /// <param name="other">The other controlled value to compare with this instance.</param>
        /// <returns><see langword="true"/> when the values represent the same controlled-value member; otherwise, <see langword="false"/>.</returns>
        public bool Equals(ControlledValue<TValue>? other)
        {
            // Equality includes the closed generic type by construction and compares the external string deterministically.
            return other is not null && StringComparer.Ordinal.Equals(Value, other.Value);
        }

        /// <summary>
        /// Determines whether the current controlled value is equal to another object.
        /// </summary>
        /// <param name="obj">The object to compare with this instance.</param>
        /// <returns><see langword="true"/> when <paramref name="obj"/> is the same controlled-value member; otherwise, <see langword="false"/>.</returns>
        public override bool Equals(object? obj)
        {
            // The object overload delegates to the typed comparison so dictionary and assertion behavior stays consistent.
            return obj is ControlledValue<TValue> other && Equals(other);
        }

        /// <summary>
        /// Gets a deterministic hash code for this controlled value.
        /// </summary>
        /// <returns>A hash code derived from the concrete value-set type and stable external string.</returns>
        public override int GetHashCode()
        {
            // Including the concrete type prevents two different value sets with the same string from sharing hash identity.
            return HashCode.Combine(typeof(TValue), StringComparer.Ordinal.GetHashCode(Value));
        }

        /// <summary>
        /// Returns the stable external string for display, logging, and serialization fallbacks.
        /// </summary>
        /// <returns>The stable external string identity.</returns>
        public override string ToString()
        {
            // ToString intentionally mirrors Value so diagnostics and simple formatting remain contract-compatible.
            return Value;
        }

        /// <summary>
        /// Compares two controlled values for value-object equality.
        /// </summary>
        /// <param name="left">The left controlled value to compare.</param>
        /// <param name="right">The right controlled value to compare.</param>
        /// <returns><see langword="true"/> when both values represent the same controlled-value member; otherwise, <see langword="false"/>.</returns>
        public static bool operator ==(ControlledValue<TValue>? left, ControlledValue<TValue>? right)
        {
            // The operator uses EqualityComparer so null handling and typed equality match ordinary .NET value-object semantics.
            return EqualityComparer<ControlledValue<TValue>?>.Default.Equals(left, right);
        }

        /// <summary>
        /// Compares two controlled values for value-object inequality.
        /// </summary>
        /// <param name="left">The left controlled value to compare.</param>
        /// <param name="right">The right controlled value to compare.</param>
        /// <returns><see langword="true"/> when the values differ; otherwise, <see langword="false"/>.</returns>
        public static bool operator !=(ControlledValue<TValue>? left, ControlledValue<TValue>? right)
        {
            // Inequality is defined as the inverse of equality to avoid divergent null or string comparison behavior.
            return !(left == right);
        }

        /// <summary>
        /// Forces the closed controlled-value type to run its static field initializers before lookup operations read registrations.
        /// </summary>
        private static void EnsureInitialized()
        {
            // RuntimeHelpers guarantees static fields such as NodeKind.Project are registered before parsing or enumeration.
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(TValue).TypeHandle);
        }
    }
}
