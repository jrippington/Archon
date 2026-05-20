using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;

namespace Archon.Domain.Graph.Model
{
    /// <summary>
    /// Represents a first-class snapshot metric with either numeric or textual architecture data.
    /// </summary>
    public sealed class MetricRecord
    {
        /// <summary>
        /// Initializes a validated metric record model.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the metric.</param>
        /// <param name="stableKey">The deterministic stable key that identifies the metric within the snapshot contract.</param>
        /// <param name="metricKind">The metric kind or metric family name.</param>
        /// <param name="scopeKind">The controlled metric scope kind.</param>
        /// <param name="nodeStableKey">The optional node stable key scoped by the metric.</param>
        /// <param name="edgeStableKey">The optional edge stable key scoped by the metric.</param>
        /// <param name="primaryEvidenceStableKey">The optional primary evidence stable key explaining the metric.</param>
        /// <param name="name">The developer-facing metric name.</param>
        /// <param name="numericValue">The optional numeric metric value.</param>
        /// <param name="textValue">The optional textual metric value.</param>
        /// <param name="unit">The optional unit associated with the metric value.</param>
        /// <param name="metadata">Deterministic metadata for metric details that are not normalized fields.</param>
        /// <param name="fingerprint">The deterministic fingerprint for diff-relevant metric content.</param>
        /// <exception cref="ArgumentException">Thrown when neither <paramref name="numericValue"/> nor <paramref name="textValue"/> is supplied.</exception>
        public MetricRecord(
            StableKey snapshotStableKey,
            StableKey stableKey,
            string? metricKind,
            MetricScopeKind scopeKind,
            StableKey? nodeStableKey,
            StableKey? edgeStableKey,
            StableKey? primaryEvidenceStableKey,
            string? name,
            decimal? numericValue,
            string? textValue,
            string? unit,
            GraphMetadata metadata,
            Fingerprint fingerprint)
        {
            // Metrics are useful only when they carry a numeric or textual value that downstream reporting can display.
            ArgumentNullException.ThrowIfNull(scopeKind);
            ArgumentNullException.ThrowIfNull(metadata);
            string? normalizedTextValue = GraphFactValidation.OptionalString(textValue);
            if (!numericValue.HasValue && normalizedTextValue is null)
            {
                throw new ArgumentException("Metric records require either a numeric value or a text value.", nameof(textValue));
            }

            SnapshotStableKey = snapshotStableKey;
            StableKey = stableKey;
            MetricKind = GraphFactValidation.RequiredString(metricKind, nameof(metricKind));
            ScopeKind = scopeKind;
            NodeStableKey = nodeStableKey;
            EdgeStableKey = edgeStableKey;
            PrimaryEvidenceStableKey = primaryEvidenceStableKey;
            Name = GraphFactValidation.RequiredString(name, nameof(name));
            NumericValue = numericValue;
            TextValue = normalizedTextValue;
            Unit = GraphFactValidation.OptionalString(unit);
            Metadata = metadata;
            Fingerprint = fingerprint;
        }

        /// <summary>
        /// Gets the stable key of the snapshot that scopes the metric.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the deterministic stable key that identifies the metric within the snapshot contract.
        /// </summary>
        public StableKey StableKey { get; }

        /// <summary>
        /// Gets the metric kind or metric family name.
        /// </summary>
        public string MetricKind { get; }

        /// <summary>
        /// Gets the controlled metric scope kind.
        /// </summary>
        public MetricScopeKind ScopeKind { get; }

        /// <summary>
        /// Gets the optional node stable key scoped by the metric.
        /// </summary>
        public StableKey? NodeStableKey { get; }

        /// <summary>
        /// Gets the optional edge stable key scoped by the metric.
        /// </summary>
        public StableKey? EdgeStableKey { get; }

        /// <summary>
        /// Gets the optional primary evidence stable key explaining the metric.
        /// </summary>
        public StableKey? PrimaryEvidenceStableKey { get; }

        /// <summary>
        /// Gets the developer-facing metric name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the optional numeric metric value.
        /// </summary>
        public decimal? NumericValue { get; }

        /// <summary>
        /// Gets the optional textual metric value.
        /// </summary>
        public string? TextValue { get; }

        /// <summary>
        /// Gets the optional unit associated with the metric value.
        /// </summary>
        public string? Unit { get; }

        /// <summary>
        /// Gets deterministic metadata for metric details that are not normalized fields.
        /// </summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Gets the deterministic fingerprint for diff-relevant metric content.
        /// </summary>
        public Fingerprint Fingerprint { get; }
    }
}
