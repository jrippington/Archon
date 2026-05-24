using Archon.Domain.Graph.Model;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents the deterministic output from converting rule evaluation matches into finding records.
    /// </summary>
    public sealed class FindingConstructionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FindingConstructionResult"/> class.
        /// </summary>
        /// <param name="findings">The constructed finding records.</param>
        /// <param name="warnings">The non-blocking construction warnings.</param>
        public FindingConstructionResult(IEnumerable<FindingRecord> findings, IEnumerable<FindingConstructionWarning> warnings)
        {
            // Result ordering is normalized so persistence and tests can compare construction output without depending on evaluation traversal order.
            ArgumentNullException.ThrowIfNull(findings);
            ArgumentNullException.ThrowIfNull(warnings);
            Findings = findings.OrderBy(static finding => finding.StableKey.Value, StringComparer.Ordinal).ToArray();
            Warnings = warnings.OrderBy(static warning => warning.Code, StringComparer.Ordinal).ThenBy(static warning => warning.Message, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Gets the constructed finding records.
        /// </summary>
        public IReadOnlyList<FindingRecord> Findings { get; }

        /// <summary>
        /// Gets the non-blocking construction warnings.
        /// </summary>
        public IReadOnlyList<FindingConstructionWarning> Warnings { get; }
    }

    /// <summary>
    /// Represents a non-blocking warning emitted during finding construction.
    /// </summary>
    public sealed class FindingConstructionWarning
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FindingConstructionWarning"/> class.
        /// </summary>
        /// <param name="code">The stable warning code.</param>
        /// <param name="message">The developer-facing warning message.</param>
        public FindingConstructionWarning(string code, string message)
        {
            // Warnings are safe diagnostics, so both code and message must be meaningful.
            Code = RequireText(code, nameof(code));
            Message = RequireText(message, nameof(message));
        }

        /// <summary>
        /// Gets the stable warning code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the developer-facing warning message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Requires a non-empty text value and returns its trimmed form.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <param name="parameterName">The parameter name used in validation failures.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Construction diagnostics must remain stable and actionable for extraction warnings and tests.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }

    /// <summary>
    /// Defines stable warning codes emitted by finding construction.
    /// </summary>
    public static class FindingConstructionWarningCodes
    {
        /// <summary>
        /// Indicates an equivalent finding was already constructed for the same snapshot and was skipped.
        /// </summary>
        public const string DuplicateFindingInSnapshot = "FINDING-DUPLICATE-IN-SNAPSHOT";

        /// <summary>
        /// Indicates a matched rule identity was not present in the supplied catalog entries.
        /// </summary>
        public const string MissingRuleForMatch = "FINDING-MISSING-RULE-FOR-MATCH";
    }
}
