using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Archon.Domain.Graph.ControlledValues
{
    /// <summary>
    /// Creates System.Text.Json converters that serialize controlled values as their stable external strings.
    /// </summary>
    /// <remarks>
    /// The factory pattern follows System.Text.Json guidance for open generic converter scenarios and keeps all controlled-value sets on one converter implementation.
    /// </remarks>
    public sealed class ControlledValueJsonConverterFactory : JsonConverterFactory
    {
        /// <summary>
        /// Determines whether the requested type is a concrete controlled-value type supported by this factory.
        /// </summary>
        /// <param name="typeToConvert">The runtime type that System.Text.Json needs to convert.</param>
        /// <returns><see langword="true"/> when <paramref name="typeToConvert"/> derives from <see cref="ControlledValue{TValue}"/>; otherwise, <see langword="false"/>.</returns>
        public override bool CanConvert(Type typeToConvert)
        {
            // The converter is intentionally limited to Archon controlled-value types so it cannot intercept unrelated domain objects.
            return FindControlledValueBase(typeToConvert) is not null;
        }

        /// <summary>
        /// Creates a converter for the concrete controlled-value type requested by System.Text.Json.
        /// </summary>
        /// <param name="typeToConvert">The concrete controlled-value type to convert.</param>
        /// <param name="options">The serializer options active for the current serialization operation.</param>
        /// <returns>A typed JSON converter for <paramref name="typeToConvert"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <paramref name="typeToConvert"/> is not a supported controlled-value type.</exception>
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            // The closed converter calls the strongly typed Parse method for the exact controlled-value set.
            Type? controlledValueBase = FindControlledValueBase(typeToConvert);
            if (controlledValueBase is null)
            {
                throw new InvalidOperationException($"Type '{typeToConvert.FullName}' is not an Archon controlled-value type.");
            }

            Type valueType = controlledValueBase.GetGenericArguments()[0];
            Type converterType = typeof(ControlledValueJsonConverter<>).MakeGenericType(valueType);

            return (JsonConverter)Activator.CreateInstance(
                converterType,
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                args: null,
                culture: null)!;
        }

        /// <summary>
        /// Locates the closed <see cref="ControlledValue{TValue}"/> base type in a runtime inheritance chain.
        /// </summary>
        /// <param name="typeToConvert">The concrete or intermediate type to inspect.</param>
        /// <returns>The closed controlled-value base type when found; otherwise, <see langword="null"/>.</returns>
        private static Type? FindControlledValueBase(Type typeToConvert)
        {
            // Walking the base chain supports sealed concrete value sets without requiring each one to register its own converter.
            for (Type? current = typeToConvert; current is not null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ControlledValue<>))
                {
                    return current;
                }
            }

            return null;
        }
    }
}
