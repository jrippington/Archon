using System.Text.Json;
using System.Text.Json.Serialization;

namespace Archon.Domain.Graph.ControlledValues
{
    /// <summary>
    /// Converts a concrete controlled-value set to and from its stable JSON string representation.
    /// </summary>
    /// <typeparam name="TValue">The concrete controlled-value type handled by this converter.</typeparam>
    public sealed class ControlledValueJsonConverter<TValue> : JsonConverter<TValue>
        where TValue : ControlledValue<TValue>
    {
        /// <summary>
        /// Reads a controlled value from a JSON string token.
        /// </summary>
        /// <param name="reader">The JSON reader positioned on the token to convert.</param>
        /// <param name="typeToConvert">The concrete controlled-value type requested by the serializer.</param>
        /// <param name="options">The serializer options active for the current operation.</param>
        /// <returns>The canonical controlled-value instance represented by the JSON string.</returns>
        /// <exception cref="JsonException">Thrown when the JSON token is not a valid string for <typeparamref name="TValue"/>.</exception>
        public override TValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Controlled values are externalized only as JSON strings, never as objects or numeric enum ordinals.
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected a JSON string when reading {typeof(TValue).Name}.");
            }

            string? value = reader.GetString();
            try
            {
                return ControlledValue<TValue>.Parse(value);
            }
            catch (ArgumentException exception)
            {
                throw new JsonException($"Unable to convert '{value}' to {typeof(TValue).Name}.", exception);
            }
        }

        /// <summary>
        /// Writes a controlled value as a JSON string token.
        /// </summary>
        /// <param name="writer">The JSON writer that receives the controlled-value string.</param>
        /// <param name="value">The controlled value to serialize.</param>
        /// <param name="options">The serializer options active for the current operation.</param>
        public override void Write(Utf8JsonWriter writer, TValue value, JsonSerializerOptions options)
        {
            // Writing the stable external string guarantees numeric enum drift cannot affect external contracts.
            writer.WriteStringValue(value.Value);
        }
    }
}
