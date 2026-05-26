using System.Diagnostics;
using ArchonMcp.McpEnvelope;

namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Executes MCP operations through provider-neutral caller lookup, authorization, allow-listing, audit, and safe error mapping.
    /// </summary>
    public sealed class ArchonMcpOperationExecutor : IArchonMcpOperationExecutor
    {
        /// <summary>
        /// Provides the normalized caller context for the current operation.
        /// </summary>
        private readonly IArchonMcpCallerContextProvider _callerContextProvider;

        /// <summary>
        /// Authorizes operation execution before handlers or query dependencies are invoked.
        /// </summary>
        private readonly IArchonMcpOperationAuthorizer _authorizer;

        /// <summary>
        /// Normalizes request parameters for safe audit logging.
        /// </summary>
        private readonly ArchonMcpAuditParameterNormalizer _parameterNormalizer;

        /// <summary>
        /// Receives sanitized audit events after every operation attempt.
        /// </summary>
        private readonly IArchonMcpAuditSink _auditSink;

        /// <summary>
        /// Creates an MCP operation executor.
        /// </summary>
        /// <param name="callerContextProvider">The provider-neutral caller context provider.</param>
        /// <param name="authorizer">The authorizer that checks authentication and allow-list configuration.</param>
        /// <param name="parameterNormalizer">The normalizer that removes sensitive request values before auditing.</param>
        /// <param name="auditSink">The sink that receives sanitized audit events.</param>
        public ArchonMcpOperationExecutor(
            IArchonMcpCallerContextProvider callerContextProvider,
            IArchonMcpOperationAuthorizer authorizer,
            ArchonMcpAuditParameterNormalizer parameterNormalizer,
            IArchonMcpAuditSink auditSink)
        {
            // The executor owns cross-cutting security flow; individual handlers should not duplicate this logic.
            _callerContextProvider = callerContextProvider ?? throw new ArgumentNullException(nameof(callerContextProvider));
            _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
            _parameterNormalizer = parameterNormalizer ?? throw new ArgumentNullException(nameof(parameterNormalizer));
            _auditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
        }

        /// <inheritdoc />
        public async Task<ArchonMcpOperationResult> ExecuteAsync(
            string operationName,
            IReadOnlyDictionary<string, string>? parameters,
            Func<Task<object>> operation,
            CancellationToken cancellationToken)
        {
            // The stopwatch starts before authorization so denied attempts still produce timing metadata for audit trails.
            ArgumentNullException.ThrowIfNull(operation);
            Stopwatch stopwatch = Stopwatch.StartNew();
            ArchonMcpCallerContext callerContext = _callerContextProvider.GetCurrentCaller();
            IReadOnlyDictionary<string, string> safeParameters = _parameterNormalizer.Normalize(parameters);
            ArchonMcpAuthorizationDecision decision = _authorizer.Authorize(operationName, callerContext);

            // Authorization and allow-list checks deliberately precede delegate invocation so query-layer dependencies are not reached.
            if (!decision.Allowed)
            {
                ArchonMcpErrorCategory category = decision.ErrorCategory ?? ArchonMcpErrorCategory.Forbidden;
                ArchonMcpErrorResponse error = ArchonMcpErrorResponse.Create(
                    operationName,
                    category,
                    decision.SafeReason ?? "The requested MCP operation is not allowed.",
                    suggestedFollowUps: null);
                stopwatch.Stop();
                RecordAudit(operationName, callerContext, safeParameters, ArchonMcpAuditResultStatus.Denied, truncated: false, stopwatch.Elapsed, category);
                return new ArchonMcpOperationResult(false, error);
            }

            try
            {
                // Cancellation is observed immediately before operation execution and delegated to handler/query code through later slices.
                cancellationToken.ThrowIfCancellationRequested();
                object payload = await operation().ConfigureAwait(false);
                stopwatch.Stop();
                RecordAudit(
                    operationName,
                    callerContext,
                    safeParameters,
                    ArchonMcpAuditResultStatus.Succeeded,
                    IsTruncated(payload),
                    stopwatch.Elapsed,
                    errorCategory: null);
                return new ArchonMcpOperationResult(true, payload);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is not converted to a server error because cooperative cancellation should remain visible to callers.
                stopwatch.Stop();
                RecordAudit(operationName, callerContext, safeParameters, ArchonMcpAuditResultStatus.Failed, truncated: false, stopwatch.Elapsed, ArchonMcpErrorCategory.ServerError);
                throw;
            }
            catch (Exception)
            {
                // Unexpected failures are mapped to a safe server error that omits exception type, stack trace, and sensitive details.
                stopwatch.Stop();
                ArchonMcpErrorResponse error = ArchonMcpErrorResponse.Create(
                    operationName,
                    ArchonMcpErrorCategory.ServerError,
                    "The MCP operation failed before a safe response could be produced.",
                    suggestedFollowUps: null);
                RecordAudit(operationName, callerContext, safeParameters, ArchonMcpAuditResultStatus.Failed, truncated: false, stopwatch.Elapsed, ArchonMcpErrorCategory.ServerError);
                return new ArchonMcpOperationResult(false, error);
            }
        }

        /// <summary>
        /// Records one sanitized audit event for the operation attempt.
        /// </summary>
        /// <param name="operationName">The stable MCP operation name that was requested.</param>
        /// <param name="callerContext">The normalized caller context for the request.</param>
        /// <param name="safeParameters">The sanitized request parameters.</param>
        /// <param name="status">The final operation status.</param>
        /// <param name="truncated">Indicates whether the response was truncated.</param>
        /// <param name="duration">The measured operation duration.</param>
        /// <param name="errorCategory">The structured error category when one applies.</param>
        private void RecordAudit(
            string operationName,
            ArchonMcpCallerContext callerContext,
            IReadOnlyDictionary<string, string> safeParameters,
            ArchonMcpAuditResultStatus status,
            bool truncated,
            TimeSpan duration,
            ArchonMcpErrorCategory? errorCategory)
        {
            // Centralized event creation keeps audit records uniform across success, denial, and failure paths.
            ArchonMcpAuditEvent auditEvent = new(
                operationName,
                callerContext.CallerId,
                safeParameters,
                status,
                truncated,
                duration,
                errorCategory);
            _auditSink.Record(auditEvent);
        }

        /// <summary>
        /// Determines whether a payload exposes common MCP limit metadata reporting truncation.
        /// </summary>
        /// <param name="payload">The operation payload returned by the handler.</param>
        /// <returns><see langword="true" /> when the payload reports truncation; otherwise, <see langword="false" />.</returns>
        private static bool IsTruncated(object payload)
        {
            // Reflection is used here because concrete MCP envelope payload types are generic and vary by tool/resource response.
            object? limitMetadata = payload.GetType().GetProperty("Limits")?.GetValue(payload);
            if (limitMetadata is ArchonMcpLimitMetadata limits)
            {
                return limits.Truncated;
            }

            return false;
        }
    }
}
