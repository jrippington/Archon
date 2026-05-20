using System.Text.Json.Serialization;

namespace Archon.Domain.Graph.ControlledValues
{
    /// <summary>
    /// Identifies the severity of an architecture finding without relying on numeric enum ordering.
    /// </summary>
    [JsonConverter(typeof(ControlledValueJsonConverterFactory))]
    public sealed class FindingSeverity : ControlledValue<FindingSeverity>
    {
        /// <summary>Represents a critical finding severity.</summary>
        public static readonly FindingSeverity Critical = new("Critical");
        /// <summary>Represents a high finding severity.</summary>
        public static readonly FindingSeverity High = new("High");
        /// <summary>Represents a medium finding severity.</summary>
        public static readonly FindingSeverity Medium = new("Medium");
        /// <summary>Represents a low finding severity.</summary>
        public static readonly FindingSeverity Low = new("Low");
        /// <summary>Represents an informational finding severity.</summary>
        public static readonly FindingSeverity Info = new("Info");

        /// <summary>
        /// Initializes a new instance of the <see cref="FindingSeverity"/> class.
        /// </summary>
        /// <param name="value">The stable external string for the finding severity.</param>
        private FindingSeverity(string value)
            : base(value)
        {
            // Construction registers the severity with the shared controlled-value lookup table.
        }
    }
}
