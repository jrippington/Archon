using Archon.Domain.Graph.Model;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents the result of applying suppression requests to one finding record.
    /// </summary>
    public sealed class SuppressFindingResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SuppressFindingResult"/> class.
        /// </summary>
        /// <param name="finding">The finding after suppression processing.</param>
        /// <param name="suppressed">A value indicating whether a suppression request matched and was applied.</param>
        /// <param name="validationErrors">The validation errors that prevented a matching request from being applied.</param>
        public SuppressFindingResult(FindingRecord finding, bool suppressed, IEnumerable<SuppressFindingValidationError> validationErrors)
        {
            // The result always returns a finding so callers can persist or report unchanged lifecycle state when validation fails.
            Finding = finding ?? throw new ArgumentNullException(nameof(finding));
            Suppressed = suppressed;
            ValidationErrors = (validationErrors ?? throw new ArgumentNullException(nameof(validationErrors))).OrderBy(static error => error.Code, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Gets the finding after suppression processing.
        /// </summary>
        public FindingRecord Finding { get; }

        /// <summary>
        /// Gets a value indicating whether a suppression request matched and was applied.
        /// </summary>
        public bool Suppressed { get; }

        /// <summary>
        /// Gets validation errors that prevented a matching request from being applied.
        /// </summary>
        public IReadOnlyList<SuppressFindingValidationError> ValidationErrors { get; }
    }

    /// <summary>
    /// Represents one deterministic suppression validation error.
    /// </summary>
    public sealed class SuppressFindingValidationError
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SuppressFindingValidationError"/> class.
        /// </summary>
        /// <param name="code">The stable validation code.</param>
        /// <param name="message">The developer-facing validation message.</param>
        public SuppressFindingValidationError(string code, string message)
        {
            // Validation errors must be stable because API and persistence seams will surface them to callers and tests.
            Code = RequireText(code, nameof(code));
            Message = RequireText(message, nameof(message));
        }

        /// <summary>
        /// Gets the stable validation code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the developer-facing validation message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Requires non-empty text and returns its trimmed form.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <param name="parameterName">The parameter name used for validation failures.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Blank validation diagnostics cannot guide callers toward a fix.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }

    /// <summary>
    /// Defines stable validation codes for suppression requests.
    /// </summary>
    public static class SuppressFindingValidationCodes
    {
        /// <summary>
        /// Indicates the suppression request did not include a finding history key.
        /// </summary>
        public const string MissingFindingHistoryKey = "SUPPRESSION-MISSING-FINDING-HISTORY-KEY";

        /// <summary>
        /// Indicates the suppression request did not include a rule code.
        /// </summary>
        public const string MissingRuleCode = "SUPPRESSION-MISSING-RULE-CODE";

        /// <summary>
        /// Indicates the suppression request did not include a rule version.
        /// </summary>
        public const string MissingRuleVersion = "SUPPRESSION-MISSING-RULE-VERSION";

        /// <summary>
        /// Indicates the suppression request did not include a primary node stable key.
        /// </summary>
        public const string MissingPrimaryNodeStableKey = "SUPPRESSION-MISSING-PRIMARY-NODE";

        /// <summary>
        /// Indicates the suppression request did not include a reason.
        /// </summary>
        public const string MissingReason = "SUPPRESSION-MISSING-REASON";

        /// <summary>
        /// Indicates the suppression request did not include the actor or process that suppressed the finding.
        /// </summary>
        public const string MissingSuppressedBy = "SUPPRESSION-MISSING-SUPPRESSED-BY";
    }
}
