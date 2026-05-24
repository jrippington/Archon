using System.Text.Json.Serialization;

namespace Archon.Domain.Graph.ControlledValues
{
    /// <summary>
    /// Identifies the logical scope to which a metric value applies.
    /// </summary>
    [JsonConverter(typeof(ControlledValueJsonConverterFactory))]
    public sealed class MetricScopeKind : ControlledValue<MetricScopeKind>
    {
        /// <summary>Represents a metric scoped to an extraction snapshot.</summary>
        public static readonly MetricScopeKind Snapshot = new("Snapshot");
        /// <summary>Represents a metric scoped to a repository boundary.</summary>
        public static readonly MetricScopeKind Repository = new("Repository");
        /// <summary>Represents a metric scoped to a solution boundary.</summary>
        public static readonly MetricScopeKind Solution = new("Solution");
        /// <summary>Represents a metric scoped to an architecture node.</summary>
        public static readonly MetricScopeKind Node = new("Node");
        /// <summary>Represents a metric scoped to an architecture edge.</summary>
        public static readonly MetricScopeKind Edge = new("Edge");
        /// <summary>Represents a metric scoped to the graph as a whole.</summary>
        public static readonly MetricScopeKind Graph = new("Graph");
        /// <summary>Represents a metric scoped to a project.</summary>
        public static readonly MetricScopeKind Project = new("Project");
        /// <summary>Represents a metric scoped to modernization analysis.</summary>
        public static readonly MetricScopeKind Modernization = new("Modernization");

        /// <summary>
        /// Initializes a new instance of the <see cref="MetricScopeKind"/> class.
        /// </summary>
        /// <param name="value">The stable external string for the metric scope kind.</param>
        private MetricScopeKind(string value)
            : base(value)
        {
            // Construction registers the metric scope with the shared controlled-value lookup table.
        }
    }
}
