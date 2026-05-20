using System.Text.Json.Serialization;

namespace Archon.Domain.Graph.ControlledValues
{
    /// <summary>
    /// Identifies the lifecycle status of an architecture finding.
    /// </summary>
    [JsonConverter(typeof(ControlledValueJsonConverterFactory))]
    public sealed class FindingStatus : ControlledValue<FindingStatus>
    {
        /// <summary>Represents a finding that is open and still active.</summary>
        public static readonly FindingStatus Open = new("Open");
        /// <summary>Represents a finding that has been acknowledged but not resolved or suppressed.</summary>
        public static readonly FindingStatus Acknowledged = new("Acknowledged");
        /// <summary>Represents a finding that is intentionally suppressed.</summary>
        public static readonly FindingStatus Suppressed = new("Suppressed");
        /// <summary>Represents a finding that has been resolved.</summary>
        public static readonly FindingStatus Resolved = new("Resolved");
        /// <summary>Represents a finding whose lifecycle state is explicitly unknown.</summary>
        public static readonly FindingStatus Unknown = new("Unknown");

        /// <summary>
        /// Initializes a new instance of the <see cref="FindingStatus"/> class.
        /// </summary>
        /// <param name="value">The stable external string for the finding status.</param>
        private FindingStatus(string value)
            : base(value)
        {
            // Construction registers the status with the shared controlled-value lookup table.
        }
    }
}
