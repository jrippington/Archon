namespace Archon.Application.ArchitectureRules
{
    /// <summary>
    /// Defines configurable policy-like options for WP013 architecture-rule checks.
    /// </summary>
    /// <remarks>
    /// These options intentionally keep organization-specific exceptions outside the built-in evaluator. A repository can allow legacy direct data-access patterns or change the shared-library review threshold without changing generic rule code.
    /// </remarks>
    /// <param name="AllowApplicationLinqToSqlDirectUse">A value indicating whether application projects may use LINQ to SQL directly without producing a rule result.</param>
    /// <param name="AllowControllerDataContextDirectUse">A value indicating whether controller nodes may use DataContext directly without producing a rule result.</param>
    /// <param name="SharedLibraryHighFanInThreshold">The minimum fan-in value that causes a shared library review result.</param>
    public sealed record ArchitectureRuleEvaluationOptions(
        bool AllowApplicationLinqToSqlDirectUse,
        bool AllowControllerDataContextDirectUse,
        decimal SharedLibraryHighFanInThreshold)
    {
        /// <summary>
        /// Gets the documented default options for built-in architecture-rule evaluation.
        /// </summary>
        public static ArchitectureRuleEvaluationOptions Default { get; } = new(
            AllowApplicationLinqToSqlDirectUse: false,
            AllowControllerDataContextDirectUse: false,
            SharedLibraryHighFanInThreshold: 8m);
    }
}
