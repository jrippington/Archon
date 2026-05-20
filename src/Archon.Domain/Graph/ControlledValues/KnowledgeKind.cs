using System.Text.Json.Serialization;

namespace Archon.Domain.Graph.ControlledValues
{
    /// <summary>
    /// Identifies how Archon knows a graph fact, evidence record, or finding is true or uncertain.
    /// </summary>
    [JsonConverter(typeof(ControlledValueJsonConverterFactory))]
    public sealed class KnowledgeKind : ControlledValue<KnowledgeKind>
    {
        /// <summary>Represents deterministic knowledge directly supported by evidence.</summary>
        public static readonly KnowledgeKind Fact = new("Fact");
        /// <summary>Represents knowledge inferred from deterministic facts or rules.</summary>
        public static readonly KnowledgeKind Inference = new("Inference");
        /// <summary>Represents explicitly unknown knowledge that must include an unknown reason in later fact contracts.</summary>
        public static readonly KnowledgeKind Unknown = new("Unknown");
        /// <summary>Represents knowledge confirmed by a human contributor.</summary>
        public static readonly KnowledgeKind HumanConfirmed = new("HumanConfirmed");

        /// <summary>
        /// Initializes a new instance of the <see cref="KnowledgeKind"/> class.
        /// </summary>
        /// <param name="value">The stable external string for the knowledge kind.</param>
        private KnowledgeKind(string value)
            : base(value)
        {
            // Construction registers the knowledge kind with the shared controlled-value lookup table.
        }
    }
}
