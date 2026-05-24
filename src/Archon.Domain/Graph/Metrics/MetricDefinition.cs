using Archon.Domain.Graph.ControlledValues;

namespace Archon.Domain.Graph.Metrics
{
    /// <summary>
    /// Describes one stable Archon metric definition that calculators, persistence, APIs, and documentation can share.
    /// </summary>
    public sealed class MetricDefinition
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MetricDefinition"/> class.
        /// </summary>
        /// <param name="kind">The stable metric kind used in metric stable keys, persistence, and API filters.</param>
        /// <param name="name">The human-readable metric name shown to API and documentation consumers.</param>
        /// <param name="defaultScopeKind">The default scope kind that calculators should use for this metric.</param>
        /// <param name="unit">The optional unit for numeric values produced by this metric.</param>
        public MetricDefinition(string? kind, string? name, MetricScopeKind defaultScopeKind, string? unit)
        {
            // The registry keeps metric identity and display vocabulary centralized so future slices do not invent inconsistent names.
            Kind = string.IsNullOrWhiteSpace(kind) ? throw new ArgumentException("Metric definitions require a stable kind.", nameof(kind)) : kind.Trim();
            Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Metric definitions require a display name.", nameof(name)) : name.Trim();
            DefaultScopeKind = defaultScopeKind ?? throw new ArgumentNullException(nameof(defaultScopeKind));
            Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
        }

        /// <summary>
        /// Gets the stable metric kind used in metric stable keys, persistence, and API filters.
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// Gets the human-readable metric name shown to API and documentation consumers.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the default scope kind that calculators should use for this metric.
        /// </summary>
        public MetricScopeKind DefaultScopeKind { get; }

        /// <summary>
        /// Gets the optional unit for numeric values produced by this metric.
        /// </summary>
        public string? Unit { get; }
    }
}
