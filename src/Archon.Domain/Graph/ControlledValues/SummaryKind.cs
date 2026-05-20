using System.Text.Json.Serialization;

namespace Archon.Domain.Graph.ControlledValues
{
    /// <summary>
    /// Identifies the logical category or target scope of generated architecture summary content.
    /// </summary>
    [JsonConverter(typeof(ControlledValueJsonConverterFactory))]
    public sealed class SummaryKind : ControlledValue<SummaryKind>
    {
        /// <summary>Represents generated summary content for an extraction snapshot.</summary>
        public static readonly SummaryKind Snapshot = new("Snapshot");
        /// <summary>Represents generated summary content for an architecture node.</summary>
        public static readonly SummaryKind Node = new("Node");
        /// <summary>Represents generated summary content for an architecture edge.</summary>
        public static readonly SummaryKind Edge = new("Edge");
        /// <summary>Represents generated summary content for the graph as a whole.</summary>
        public static readonly SummaryKind Graph = new("Graph");
        /// <summary>Represents generated summary content for a project.</summary>
        public static readonly SummaryKind Project = new("Project");
        /// <summary>Represents generated summary content for modernization analysis.</summary>
        public static readonly SummaryKind Modernization = new("Modernization");

        /// <summary>
        /// Initializes a new instance of the <see cref="SummaryKind"/> class.
        /// </summary>
        /// <param name="value">The stable external string for the generated-summary kind.</param>
        private SummaryKind(string value)
            : base(value)
        {
            // Construction registers the summary kind with the shared controlled-value lookup table.
        }
    }
}
