namespace Archon.Application.ArchitectureRules
{
    /// <summary>
    /// Provides stable architecture-rule result status values used by evaluation, filtering, and public query APIs.
    /// </summary>
    public static class ArchitectureRuleResultStatus
    {
        /// <summary>
        /// Indicates that the rule found evidence of an architecture violation.
        /// </summary>
        public const string Violation = "Violation";

        /// <summary>
        /// Indicates that the rule found a risk requiring human review before change.
        /// </summary>
        public const string ReviewRequired = "ReviewRequired";

        /// <summary>
        /// Indicates that available evidence suggests the check matters but required facts are incomplete.
        /// </summary>
        public const string Unknown = "Unknown";
    }
}
