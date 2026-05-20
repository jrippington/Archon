using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Archon.Domain.Graph.Metadata
{
    /// <summary>
    /// Represents JSON-compatible extraction metadata with deterministic canonical serialization.
    /// </summary>
    /// <remarks>
    /// Metadata is for extraction-specific detail that does not belong in normalized graph properties. Core fields such as stable keys, graph kinds, confidence, and unknown-state indicators must remain first-class model fields so queries, rules, and fingerprints can use them consistently.
    /// </remarks>
    public sealed class GraphMetadata : IEquatable<GraphMetadata>
    {
        /// <summary>
        /// Contains normalized graph property names that must not be hidden inside metadata payloads.
        /// </summary>
        private static readonly HashSet<string> s_reservedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "snapshotId",
            "snapshotStableKey",
            "stableKey",
            "nodeKind",
            "edgeKind",
            "relationshipKind",
            "evidenceKind",
            "knowledgeKind",
            "ruleCode",
            "severity",
            "status",
            "confidence",
            "hasUnknownData",
            "unknownReason",
            "filePath",
            "startLine",
            "endLine"
        };

        /// <summary>
        /// Initializes the singleton empty metadata instance.
        /// </summary>
        public static readonly GraphMetadata Empty = new("{}");

        /// <summary>
        /// Stores the canonical JSON representation used for equality and fingerprint input.
        /// </summary>
        private readonly string _canonicalJson;

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphMetadata"/> class.
        /// </summary>
        /// <param name="canonicalJson">The deterministic canonical JSON representation for this metadata instance.</param>
        private GraphMetadata(string canonicalJson)
        {
            // The constructor accepts only already canonical JSON because all validation happens in factory methods.
            _canonicalJson = canonicalJson;
        }

        /// <summary>
        /// Gets a value indicating whether this metadata instance contains no properties.
        /// </summary>
        public bool IsEmpty
        {
            get
            {
                // The empty canonical representation is fixed, so emptiness can be checked without reparsing JSON.
                return StringComparer.Ordinal.Equals(_canonicalJson, "{}");
            }
        }

        /// <summary>
        /// Creates metadata from a dictionary of JSON-compatible values.
        /// </summary>
        /// <param name="values">The metadata properties to canonicalize.</param>
        /// <returns>A metadata value with deterministic canonical serialization.</returns>
        /// <exception cref="ArgumentException">Thrown when a property name is invalid, reserved, or a value is not JSON-compatible.</exception>
        public static GraphMetadata From(IReadOnlyDictionary<string, object?> values)
        {
            // Dictionary input is the common extractor path; it delegates to the pair sequence so validation is shared.
            ArgumentNullException.ThrowIfNull(values);
            return From(values.Select(static pair => new KeyValuePair<string?, object?>(pair.Key, pair.Value)));
        }

        /// <summary>
        /// Creates metadata from a sequence of JSON-compatible key/value pairs.
        /// </summary>
        /// <param name="values">The metadata properties to canonicalize.</param>
        /// <returns>A metadata value with deterministic canonical serialization.</returns>
        /// <exception cref="ArgumentException">Thrown when a property name is invalid, reserved, duplicated, or a value is not JSON-compatible.</exception>
        public static GraphMetadata From(IEnumerable<KeyValuePair<string?, object?>> values)
        {
            // Sequence input supports tests and extraction code that need null-key validation before a dictionary exists.
            ArgumentNullException.ThrowIfNull(values);

            SortedDictionary<string, object?> normalizedValues = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string?, object?> pair in values)
            {
                string key = NormalizeKey(pair.Key);
                if (!normalizedValues.TryAdd(key, pair.Value))
                {
                    throw new ArgumentException($"Metadata contains duplicate property '{key}'.", nameof(values));
                }
            }

            if (normalizedValues.Count == 0)
            {
                return Empty;
            }

            string canonicalJson = WriteCanonicalObject(normalizedValues);
            return new GraphMetadata(canonicalJson);
        }

        /// <summary>
        /// Returns the deterministic canonical JSON representation for this metadata instance.
        /// </summary>
        /// <returns>The canonical JSON string used for fingerprint input.</returns>
        public string ToCanonicalJson()
        {
            // Returning the cached canonical string avoids reordering or reserializing during repeated fingerprint generation.
            return _canonicalJson;
        }

        /// <summary>
        /// Determines whether this metadata instance has the same canonical JSON as another metadata instance.
        /// </summary>
        /// <param name="other">The metadata instance to compare with this instance.</param>
        /// <returns><see langword="true"/> when both instances have identical canonical JSON; otherwise, <see langword="false"/>.</returns>
        public bool Equals(GraphMetadata? other)
        {
            // Canonical JSON is the complete metadata identity for deterministic fingerprinting.
            return other is not null && StringComparer.Ordinal.Equals(_canonicalJson, other._canonicalJson);
        }

        /// <summary>
        /// Determines whether this metadata instance has the same canonical JSON as another object.
        /// </summary>
        /// <param name="obj">The object to compare with this instance.</param>
        /// <returns><see langword="true"/> when <paramref name="obj"/> is equal metadata; otherwise, <see langword="false"/>.</returns>
        public override bool Equals(object? obj)
        {
            // The object overload delegates to the typed comparison so assertion and collection behavior remains consistent.
            return obj is GraphMetadata other && Equals(other);
        }

        /// <summary>
        /// Gets a hash code derived from canonical JSON.
        /// </summary>
        /// <returns>A hash code suitable for dictionary and set usage.</returns>
        public override int GetHashCode()
        {
            // The hash code follows equality by using the same canonical JSON string.
            return StringComparer.Ordinal.GetHashCode(_canonicalJson);
        }

        /// <summary>
        /// Returns the canonical JSON representation for diagnostics and simple display.
        /// </summary>
        /// <returns>The canonical JSON string.</returns>
        public override string ToString()
        {
            // ToString mirrors canonical JSON so diagnostics show the exact fingerprint input payload.
            return _canonicalJson;
        }

        /// <summary>
        /// Normalizes and validates a metadata property key.
        /// </summary>
        /// <param name="key">The candidate metadata property key.</param>
        /// <returns>The validated metadata property key.</returns>
        private static string NormalizeKey(string? key)
        {
            // Metadata keys must be meaningful because they become part of canonical JSON and fingerprint input.
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Metadata property names cannot be null, empty, or whitespace.", nameof(key));
            }

            string normalizedKey = key.Trim();
            if (s_reservedPropertyNames.Contains(normalizedKey))
            {
                throw new ArgumentException($"Metadata property '{normalizedKey}' is a normalized graph property and must not be stored in metadata.", nameof(key));
            }

            return normalizedKey;
        }

        /// <summary>
        /// Writes a metadata object as deterministic canonical JSON.
        /// </summary>
        /// <param name="values">The sorted metadata properties to serialize.</param>
        /// <returns>A canonical JSON object string.</returns>
        private static string WriteCanonicalObject(IReadOnlyDictionary<string, object?> values)
        {
            // Utf8JsonWriter handles escaping and primitive formatting while this class controls object ordering.
            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartObject();
                foreach (KeyValuePair<string, object?> pair in values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(pair.Key);
                    WriteCanonicalValue(writer, pair.Value);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// Writes a JSON-compatible value using deterministic object ordering where nested objects are present.
        /// </summary>
        /// <param name="writer">The JSON writer receiving the canonical value.</param>
        /// <param name="value">The JSON-compatible value to write.</param>
        private static void WriteCanonicalValue(Utf8JsonWriter writer, object? value)
        {
            // The switch keeps supported value types explicit so unsupported runtime objects cannot serialize unstably.
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case string stringValue:
                    writer.WriteStringValue(stringValue);
                    break;
                case bool boolValue:
                    writer.WriteBooleanValue(boolValue);
                    break;
                case int intValue:
                    writer.WriteNumberValue(intValue);
                    break;
                case long longValue:
                    writer.WriteNumberValue(longValue);
                    break;
                case double doubleValue:
                    writer.WriteNumberValue(doubleValue);
                    break;
                case decimal decimalValue:
                    writer.WriteNumberValue(decimalValue);
                    break;
                case float floatValue:
                    writer.WriteNumberValue(floatValue);
                    break;
                case JsonElement element:
                    WriteJsonElement(writer, element);
                    break;
                case IReadOnlyDictionary<string, object?> readOnlyDictionary:
                    WriteDictionaryValue(writer, readOnlyDictionary.Select(static pair => new KeyValuePair<string?, object?>(pair.Key, pair.Value)));
                    break;
                case IDictionary<string, object?> dictionary:
                    WriteDictionaryValue(writer, dictionary.Select(static pair => new KeyValuePair<string?, object?>(pair.Key, pair.Value)));
                    break;
                case IEnumerable enumerable when value is not string:
                    WriteEnumerableValue(writer, enumerable);
                    break;
                default:
                    throw new ArgumentException($"Metadata value type '{value.GetType().FullName}' is not JSON-compatible for deterministic canonicalization.", nameof(value));
            }
        }

        /// <summary>
        /// Writes a nested dictionary with deterministic property ordering.
        /// </summary>
        /// <param name="writer">The JSON writer receiving the nested object.</param>
        /// <param name="values">The nested key/value pairs to write.</param>
        private static void WriteDictionaryValue(Utf8JsonWriter writer, IEnumerable<KeyValuePair<string?, object?>> values)
        {
            // Nested objects receive the same key validation and ordinal ordering as the root metadata object.
            SortedDictionary<string, object?> sortedValues = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string?, object?> pair in values)
            {
                string key = NormalizeKey(pair.Key);
                if (!sortedValues.TryAdd(key, pair.Value))
                {
                    throw new ArgumentException($"Metadata contains duplicate nested property '{key}'.", nameof(values));
                }
            }

            writer.WriteStartObject();
            foreach (KeyValuePair<string, object?> pair in sortedValues)
            {
                writer.WritePropertyName(pair.Key);
                WriteCanonicalValue(writer, pair.Value);
            }

            writer.WriteEndObject();
        }

        /// <summary>
        /// Writes an enumerable value as a JSON array while preserving array item order.
        /// </summary>
        /// <param name="writer">The JSON writer receiving the array.</param>
        /// <param name="values">The sequence of JSON-compatible values to write.</param>
        private static void WriteEnumerableValue(Utf8JsonWriter writer, IEnumerable values)
        {
            // Array order is considered meaningful metadata and is therefore preserved exactly.
            writer.WriteStartArray();
            foreach (object? item in values)
            {
                WriteCanonicalValue(writer, item);
            }

            writer.WriteEndArray();
        }

        /// <summary>
        /// Writes a <see cref="JsonElement"/> using deterministic ordering for object properties.
        /// </summary>
        /// <param name="writer">The JSON writer receiving the element.</param>
        /// <param name="element">The JSON element to write.</param>
        private static void WriteJsonElement(Utf8JsonWriter writer, JsonElement element)
        {
            // JsonElement support allows callers to reuse parsed JSON while still canonicalizing object order.
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (JsonProperty property in element.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                    {
                        string key = NormalizeKey(property.Name);
                        writer.WritePropertyName(key);
                        WriteJsonElement(writer, property.Value);
                    }

                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        WriteJsonElement(writer, item);
                    }

                    writer.WriteEndArray();
                    break;
                case JsonValueKind.String:
                    writer.WriteStringValue(element.GetString());
                    break;
                case JsonValueKind.Number:
                    WriteJsonNumber(writer, element);
                    break;
                case JsonValueKind.True:
                    writer.WriteBooleanValue(true);
                    break;
                case JsonValueKind.False:
                    writer.WriteBooleanValue(false);
                    break;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    writer.WriteNullValue();
                    break;
                default:
                    throw new ArgumentException($"Unsupported JSON metadata value kind '{element.ValueKind}'.", nameof(element));
            }
        }

        /// <summary>
        /// Writes a JSON number while preserving integer and decimal value shapes where possible.
        /// </summary>
        /// <param name="writer">The JSON writer receiving the number.</param>
        /// <param name="element">The JSON number element to write.</param>
        private static void WriteJsonNumber(Utf8JsonWriter writer, JsonElement element)
        {
            // Prefer integral and decimal forms to avoid culture-sensitive or scientific-notation surprises where possible.
            if (element.TryGetInt64(out long longValue))
            {
                writer.WriteNumberValue(longValue);
                return;
            }

            if (element.TryGetDecimal(out decimal decimalValue))
            {
                writer.WriteRawValue(decimalValue.ToString(CultureInfo.InvariantCulture), skipInputValidation: false);
                return;
            }

            writer.WriteNumberValue(element.GetDouble());
        }
    }
}
