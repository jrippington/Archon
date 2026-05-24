using System.Text.Json.Serialization;
using Archon.Domain.Graph.ControlledValues;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Identifies the default modernization or lifecycle status assigned by a rule catalog entry.
    /// </summary>
    [JsonConverter(typeof(ControlledValueJsonConverterFactory))]
    public sealed class RuleFindingStatus : ControlledValue<RuleFindingStatus>
    {
        /// <summary>Represents a rule finding for technology outside supported lifecycle.</summary>
        public static readonly RuleFindingStatus OutOfSupport = new("OutOfSupport");

        /// <summary>Represents a rule finding for obsolete API or platform usage.</summary>
        public static readonly RuleFindingStatus Obsolete = new("Obsolete");

        /// <summary>Represents a rule finding for legacy technology usage.</summary>
        public static readonly RuleFindingStatus Legacy = new("Legacy");

        /// <summary>Represents a rule finding for behavior available only on .NET Framework.</summary>
        public static readonly RuleFindingStatus FrameworkOnly = new("FrameworkOnly");

        /// <summary>Represents a rule finding that blocks or materially complicates migration.</summary>
        public static readonly RuleFindingStatus MigrationBlocker = new("MigrationBlocker");

        /// <summary>Represents a rule finding for security-sensitive usage.</summary>
        public static readonly RuleFindingStatus SecuritySensitive = new("SecuritySensitive");

        /// <summary>Represents a rule finding for discouraged patterns.</summary>
        public static readonly RuleFindingStatus Discouraged = new("Discouraged");

        /// <summary>Represents a rule finding whose status cannot be classified more precisely.</summary>
        public static readonly RuleFindingStatus Unknown = new("Unknown");

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleFindingStatus"/> class.
        /// </summary>
        /// <param name="value">The stable external string for the rule finding status.</param>
        private RuleFindingStatus(string value)
            : base(value)
        {
            // Construction registers the rule finding status with the shared controlled-value lookup table.
        }
    }
}
