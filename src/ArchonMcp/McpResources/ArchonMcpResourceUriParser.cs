using ArchonMcp.McpEnvelope;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Parses and validates supported read-only <c>archon://</c> MCP resource URIs.
    /// </summary>
    public sealed class ArchonMcpResourceUriParser : IArchonMcpResourceUriParser
    {
        /// <summary>
        /// Defines the only URI scheme accepted by Archon MCP resources.
        /// </summary>
        private const string ArchonScheme = "archon";

        /// <summary>
        /// Defines the only selector implemented by the current-resource slice.
        /// </summary>
        private const string CurrentSelector = "current";

        /// <summary>
        /// Defines the selector segment used by explicit snapshot diff resources.
        /// </summary>
        private const string DiffSelector = "diff";

        /// <inheritdoc />
        public ArchonMcpResourceParseResult Parse(string? uri)
        {
            // Parsing is deliberately strict so malformed or ambiguous resource identifiers fail before any query dependency runs.
            if (string.IsNullOrWhiteSpace(uri))
            {
                return ValidationFailure("A non-empty archon:// resource URI is required.");
            }

            if (!Uri.TryCreate(uri.Trim(), UriKind.Absolute, out Uri? parsedUri))
            {
                return ValidationFailure("Resource URI must be an absolute archon:// URI.");
            }

            if (!string.Equals(parsedUri.Scheme, ArchonScheme, StringComparison.OrdinalIgnoreCase))
            {
                return ValidationFailure("Resource URI scheme must be archon://.");
            }

            if (!TryParseFamily(parsedUri.Host, out ArchonMcpResourceFamily family))
            {
                return UnsupportedFailure("Resource family must be snapshot, rules, hotlist, hotspots, project, or symbol.");
            }

            string[] pathSegments = parsedUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (IsCurrentResource(family, pathSegments))
            {
                return ParseCurrentResource(parsedUri, family);
            }

            return ParseParameterizedResource(parsedUri, family, pathSegments);
        }

        /// <summary>
        /// Determines whether the URI path selects a Work Item 9 current resource.
        /// </summary>
        /// <param name="family">The parsed resource family.</param>
        /// <param name="pathSegments">The decoded path segments from the URI.</param>
        /// <returns><see langword="true" /> when the URI is a current resource; otherwise, <see langword="false" />.</returns>
        private static bool IsCurrentResource(ArchonMcpResourceFamily family, IReadOnlyList<string> pathSegments)
        {
            // Current resources remain the only list-style resources and continue to require repository-scoped current snapshot resolution.
            return family is ArchonMcpResourceFamily.Snapshot or ArchonMcpResourceFamily.Rules or ArchonMcpResourceFamily.Hotlist or ArchonMcpResourceFamily.Hotspots
                && pathSegments.Count == 1
                && string.Equals(pathSegments[0], CurrentSelector, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Parses the original current-resource URI shape introduced by Work Item 9.
        /// </summary>
        /// <param name="parsedUri">The already validated absolute Archon URI.</param>
        /// <param name="family">The parsed current resource family.</param>
        /// <returns>A successful resource request or structured parse failure.</returns>
        private static ArchonMcpResourceParseResult ParseCurrentResource(Uri parsedUri, ArchonMcpResourceFamily family)
        {
            // Current resources support repository, solution, and list-filter query parameters only; parameterized resources are parsed separately.

            Dictionary<string, StringValues> queryValues = QueryHelpers.ParseQuery(parsedUri.Query);
            if (HasDuplicateValues(queryValues, "repository") || HasDuplicateValues(queryValues, "solution") || HasDuplicateValues(queryValues, "limit") || HasDuplicateValues(queryValues, "category") || HasDuplicateValues(queryValues, "severity") || HasDuplicateValues(queryValues, "status"))
            {
                return ValidationFailure("Resource URI query parameters must not be duplicated.");
            }

            string? repositoryStableKey = GetSingleQueryValue(queryValues, "repository");
            if (string.IsNullOrWhiteSpace(repositoryStableKey))
            {
                return ValidationFailure("Current resource URIs require a repository query parameter.");
            }

            string? solutionStableKey = NormalizeOptionalQueryValue(queryValues, "solution");
            string? category = NormalizeOptionalQueryValue(queryValues, "category");
            string? severity = NormalizeOptionalQueryValue(queryValues, "severity");
            string? status = NormalizeOptionalQueryValue(queryValues, "status");
            if (!TryParseLimit(queryValues, out int? limit, out string? limitError))
            {
                return ValidationFailure(limitError ?? "Resource limit is invalid.");
            }

            if (!LooksLikeStableKey(repositoryStableKey))
            {
                return ValidationFailure("Repository must be a stable key URI value such as repository://name.");
            }

            if (solutionStableKey is not null && !LooksLikeStableKey(solutionStableKey))
            {
                return ValidationFailure("Solution must be a stable key URI value such as solution://name/path.");
            }

            ArchonMcpResourceRequest request = new(
                parsedUri.ToString(),
                $"archon://{parsedUri.Host.ToLowerInvariant()}/{CurrentSelector}",
                family,
                CurrentSelector,
                repositoryStableKey.Trim(),
                solutionStableKey,
                limit,
                category,
                severity,
                status);
            return ArchonMcpResourceParseResult.Success(request);
        }

        /// <summary>
        /// Parses Work Item 10 parameterized project, symbol, and snapshot diff resources.
        /// </summary>
        /// <param name="parsedUri">The already validated absolute Archon URI.</param>
        /// <param name="family">The parsed resource family.</param>
        /// <param name="pathSegments">The decoded path segments from the URI.</param>
        /// <returns>A successful parameterized request or structured parse failure.</returns>
        private static ArchonMcpResourceParseResult ParseParameterizedResource(Uri parsedUri, ArchonMcpResourceFamily family, IReadOnlyList<string> pathSegments)
        {
            // Parameterized resources are strict stable-key views and avoid generic graph, file, or route passthrough behavior.
            Dictionary<string, StringValues> queryValues = QueryHelpers.ParseQuery(parsedUri.Query);
            if (HasDuplicateValues(queryValues, "repository") || HasDuplicateValues(queryValues, "solution") || HasDuplicateValues(queryValues, "limit") || HasDuplicateValues(queryValues, "includeDetails"))
            {
                return ValidationFailure("Resource URI query parameters must not be duplicated.");
            }

            if (!TryParseLimit(queryValues, out int? limit, out string? limitError))
            {
                return ValidationFailure(limitError ?? "Resource limit is invalid.");
            }

            if (!TryParseOptionalBoolean(queryValues, "includeDetails", out bool? includeDetails, out string? includeDetailsError))
            {
                return ValidationFailure(includeDetailsError ?? "includeDetails is invalid.");
            }

            string? repositoryStableKey = NormalizeOptionalStableKey(queryValues, "repository", required: false, out string? repositoryError);
            if (repositoryError is not null)
            {
                return ValidationFailure(repositoryError);
            }

            string? solutionStableKey = NormalizeOptionalStableKey(queryValues, "solution", required: false, out string? solutionError);
            if (solutionError is not null)
            {
                return ValidationFailure(solutionError);
            }

            return family switch
            {
                ArchonMcpResourceFamily.Project => ParseProjectResource(parsedUri, pathSegments, repositoryStableKey, solutionStableKey, limit),
                ArchonMcpResourceFamily.Symbol => ParseSymbolResource(parsedUri, pathSegments, repositoryStableKey, solutionStableKey, limit),
                ArchonMcpResourceFamily.Snapshot => ParseSnapshotDiffResource(parsedUri, pathSegments, repositoryStableKey, solutionStableKey, limit, includeDetails),
                _ => UnsupportedFailure("Use archon://project/{projectKey}, archon://symbol/{symbolKey}, or archon://snapshot/{snapshotId}/diff/{previousSnapshotId} for parameterized resources.")
            };
        }

        /// <summary>
        /// Parses a project resource path into a stable-key-backed request.
        /// </summary>
        /// <param name="parsedUri">The already validated absolute Archon URI.</param>
        /// <param name="pathSegments">The decoded path segments from the URI.</param>
        /// <param name="repositoryStableKey">The optional repository scope query value.</param>
        /// <param name="solutionStableKey">The optional solution scope query value.</param>
        /// <param name="limit">The optional caller-requested limit.</param>
        /// <returns>A project resource parse result.</returns>
        private static ArchonMcpResourceParseResult ParseProjectResource(Uri parsedUri, IReadOnlyList<string> pathSegments, string? repositoryStableKey, string? solutionStableKey, int? limit)
        {
            // Project resource identity lives in the path so clients do not need internal API route knowledge.
            if (pathSegments.Count != 1)
            {
                return UnsupportedFailure("Project resources must use archon://project/{projectKey}.");
            }

            string projectStableKey = Uri.UnescapeDataString(pathSegments[0]).Trim();
            if (!LooksLikeStableKey(projectStableKey) || !projectStableKey.StartsWith("project://", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationFailure("Project resource keys must be project stable-key URI values such as project://src/app/app.csproj.");
            }

            return ArchonMcpResourceParseResult.Success(new ArchonMcpResourceRequest(parsedUri.ToString(), $"archon://project/{Uri.EscapeDataString(projectStableKey)}", ArchonMcpResourceFamily.Project, projectStableKey, repositoryStableKey, solutionStableKey, limit, Category: null, Severity: null, Status: null, ProjectStableKey: projectStableKey));
        }

        /// <summary>
        /// Parses a symbol resource path into a stable-key-backed request.
        /// </summary>
        /// <param name="parsedUri">The already validated absolute Archon URI.</param>
        /// <param name="pathSegments">The decoded path segments from the URI.</param>
        /// <param name="repositoryStableKey">The optional repository scope query value.</param>
        /// <param name="solutionStableKey">The optional solution scope query value.</param>
        /// <param name="limit">The optional caller-requested limit.</param>
        /// <returns>A symbol resource parse result.</returns>
        private static ArchonMcpResourceParseResult ParseSymbolResource(Uri parsedUri, IReadOnlyList<string> pathSegments, string? repositoryStableKey, string? solutionStableKey, int? limit)
        {
            // Symbol resource identity is decoded before validation so percent-encoded stable keys remain usable in URI paths.
            if (pathSegments.Count != 1)
            {
                return UnsupportedFailure("Symbol resources must use archon://symbol/{symbolKey}.");
            }

            string symbolStableKey = Uri.UnescapeDataString(pathSegments[0]).Trim();
            if (!LooksLikeStableKey(symbolStableKey) || !symbolStableKey.StartsWith("symbol://", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationFailure("Symbol resource keys must be symbol stable-key URI values such as symbol://namespace/type/member.");
            }

            return ArchonMcpResourceParseResult.Success(new ArchonMcpResourceRequest(parsedUri.ToString(), $"archon://symbol/{Uri.EscapeDataString(symbolStableKey)}", ArchonMcpResourceFamily.Symbol, symbolStableKey, repositoryStableKey, solutionStableKey, limit, Category: null, Severity: null, Status: null, SymbolStableKey: symbolStableKey));
        }

        /// <summary>
        /// Parses an explicit snapshot diff resource path.
        /// </summary>
        /// <param name="parsedUri">The already validated absolute Archon URI.</param>
        /// <param name="pathSegments">The decoded path segments from the URI.</param>
        /// <param name="repositoryStableKey">The optional repository scope query value.</param>
        /// <param name="solutionStableKey">The optional solution scope query value.</param>
        /// <param name="limit">The optional caller-requested detail limit.</param>
        /// <param name="includeDetails">The optional flag controlling bounded detail output.</param>
        /// <returns>A snapshot diff resource parse result.</returns>
        private static ArchonMcpResourceParseResult ParseSnapshotDiffResource(Uri parsedUri, IReadOnlyList<string> pathSegments, string? repositoryStableKey, string? solutionStableKey, int? limit, bool? includeDetails)
        {
            // The path shape mirrors the public resource contract and avoids query-string snapshot identifiers for explicit comparisons.
            if (pathSegments.Count != 3 || !string.Equals(pathSegments[1], DiffSelector, StringComparison.OrdinalIgnoreCase))
            {
                return UnsupportedFailure("Snapshot diff resources must use archon://snapshot/{snapshotId}/diff/{previousSnapshotId}.");
            }

            string currentSnapshotStableKey = Uri.UnescapeDataString(pathSegments[0]).Trim();
            string previousSnapshotStableKey = Uri.UnescapeDataString(pathSegments[2]).Trim();
            if (!LooksLikeStableKey(currentSnapshotStableKey) || !currentSnapshotStableKey.StartsWith("snapshot://", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationFailure("Current snapshot resource keys must be snapshot stable-key URI values such as snapshot://current.");
            }

            if (!LooksLikeStableKey(previousSnapshotStableKey) || !previousSnapshotStableKey.StartsWith("snapshot://", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationFailure("Previous snapshot resource keys must be snapshot stable-key URI values such as snapshot://previous.");
            }

            string canonicalUri = $"archon://snapshot/{Uri.EscapeDataString(currentSnapshotStableKey)}/diff/{Uri.EscapeDataString(previousSnapshotStableKey)}";
            return ArchonMcpResourceParseResult.Success(new ArchonMcpResourceRequest(parsedUri.ToString(), canonicalUri, ArchonMcpResourceFamily.Snapshot, DiffSelector, repositoryStableKey, solutionStableKey, limit, Category: null, Severity: null, Status: null, CurrentSnapshotStableKey: currentSnapshotStableKey, PreviousSnapshotStableKey: previousSnapshotStableKey, IncludeDetails: includeDetails));
        }

        /// <summary>
        /// Attempts to parse the URI host into a supported resource family.
        /// </summary>
        /// <param name="host">The host component of the URI.</param>
        /// <param name="family">The parsed family when the host is supported.</param>
        /// <returns><see langword="true" /> when the host names a supported family; otherwise, <see langword="false" />.</returns>
        private static bool TryParseFamily(string host, out ArchonMcpResourceFamily family)
        {
            // Family names are intentionally fixed; there is no generic graph or file resource escape hatch.
            if (string.Equals(host, "snapshot", StringComparison.OrdinalIgnoreCase))
            {
                family = ArchonMcpResourceFamily.Snapshot;
                return true;
            }

            if (string.Equals(host, "rules", StringComparison.OrdinalIgnoreCase))
            {
                family = ArchonMcpResourceFamily.Rules;
                return true;
            }

            if (string.Equals(host, "hotlist", StringComparison.OrdinalIgnoreCase))
            {
                family = ArchonMcpResourceFamily.Hotlist;
                return true;
            }

            if (string.Equals(host, "hotspots", StringComparison.OrdinalIgnoreCase))
            {
                family = ArchonMcpResourceFamily.Hotspots;
                return true;
            }

            if (string.Equals(host, "project", StringComparison.OrdinalIgnoreCase))
            {
                family = ArchonMcpResourceFamily.Project;
                return true;
            }

            if (string.Equals(host, "symbol", StringComparison.OrdinalIgnoreCase))
            {
                family = ArchonMcpResourceFamily.Symbol;
                return true;
            }

            family = default;
            return false;
        }

        /// <summary>
        /// Determines whether a query parameter carries more than one decoded value.
        /// </summary>
        /// <param name="queryValues">The parsed query collection.</param>
        /// <param name="name">The parameter name to inspect.</param>
        /// <returns><see langword="true" /> when the parameter is duplicated; otherwise, <see langword="false" />.</returns>
        private static bool HasDuplicateValues(IReadOnlyDictionary<string, StringValues> queryValues, string name)
        {
            // Duplicate parameters are ambiguous because MCP resources should resolve one deterministic scope and set of filters.
            return queryValues.TryGetValue(name, out StringValues values) && values.Count > 1;
        }

        /// <summary>
        /// Reads one required query parameter value without normalizing blanks.
        /// </summary>
        /// <param name="queryValues">The parsed query collection.</param>
        /// <param name="name">The parameter name to read.</param>
        /// <returns>The decoded value when present; otherwise, <see langword="null" />.</returns>
        private static string? GetSingleQueryValue(IReadOnlyDictionary<string, StringValues> queryValues, string name)
        {
            // QueryHelpers already decodes percent-encoded values, so later validation sees stable-key text rather than encoded bytes.
            return queryValues.TryGetValue(name, out StringValues values) ? values.ToString() : null;
        }

        /// <summary>
        /// Reads and normalizes one optional query parameter value.
        /// </summary>
        /// <param name="queryValues">The parsed query collection.</param>
        /// <param name="name">The parameter name to read.</param>
        /// <returns>The trimmed decoded value, or <see langword="null" /> when omitted or blank.</returns>
        private static string? NormalizeOptionalQueryValue(IReadOnlyDictionary<string, StringValues> queryValues, string name)
        {
            // Optional blank filters behave like omitted filters rather than invisible stable keys.
            string? value = GetSingleQueryValue(queryValues, name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Parses the optional limit query parameter.
        /// </summary>
        /// <param name="queryValues">The parsed query collection.</param>
        /// <param name="limit">The parsed positive limit when supplied.</param>
        /// <param name="error">A safe validation message when parsing fails.</param>
        /// <returns><see langword="true" /> when the limit is valid or absent; otherwise, <see langword="false" />.</returns>
        private static bool TryParseLimit(IReadOnlyDictionary<string, StringValues> queryValues, out int? limit, out string? error)
        {
            // The global MCP limit guard applies final caps, while parser validation rejects non-numeric or non-positive caller input.
            limit = null;
            error = null;
            string? value = NormalizeOptionalQueryValue(queryValues, "limit");
            if (value is null)
            {
                return true;
            }

            if (!int.TryParse(value, out int parsed) || parsed < 1)
            {
                error = "Limit must be a positive integer when supplied.";
                return false;
            }

            limit = parsed;
            return true;
        }

        /// <summary>
        /// Parses an optional Boolean query parameter using strict true/false text.
        /// </summary>
        /// <param name="queryValues">The parsed query collection.</param>
        /// <param name="name">The parameter name to parse.</param>
        /// <param name="value">The parsed Boolean value when supplied.</param>
        /// <param name="error">A safe validation error when parsing fails.</param>
        /// <returns><see langword="true" /> when the parameter is absent or valid; otherwise, <see langword="false" />.</returns>
        private static bool TryParseOptionalBoolean(IReadOnlyDictionary<string, StringValues> queryValues, string name, out bool? value, out string? error)
        {
            // Strict Boolean parsing avoids surprising aliases such as 1/0 that can make shared resource examples ambiguous.
            value = null;
            error = null;
            string? text = NormalizeOptionalQueryValue(queryValues, name);
            if (text is null)
            {
                return true;
            }

            if (!bool.TryParse(text, out bool parsed))
            {
                error = $"{name} must be true or false when supplied.";
                return false;
            }

            value = parsed;
            return true;
        }

        /// <summary>
        /// Reads and validates an optional stable-key query parameter.
        /// </summary>
        /// <param name="queryValues">The parsed query collection.</param>
        /// <param name="name">The parameter name to inspect.</param>
        /// <param name="required">A value indicating whether the value is required.</param>
        /// <param name="error">A safe validation error when the value is missing or malformed.</param>
        /// <returns>The normalized stable key, or <see langword="null" /> when omitted and optional.</returns>
        private static string? NormalizeOptionalStableKey(IReadOnlyDictionary<string, StringValues> queryValues, string name, bool required, out string? error)
        {
            // Parameterized resources allow repository and solution context when callers want latest-like query selectors around stable path keys.
            error = null;
            string? value = NormalizeOptionalQueryValue(queryValues, name);
            if (value is null)
            {
                if (required)
                {
                    error = $"{name} query parameter is required.";
                }

                return null;
            }

            if (!LooksLikeStableKey(value))
            {
                error = $"{name} must be a stable key URI value.";
                return null;
            }

            return value;
        }

        /// <summary>
        /// Performs a conservative stable-key shape check for decoded query values.
        /// </summary>
        /// <param name="value">The decoded value to inspect.</param>
        /// <returns><see langword="true" /> when the value resembles a stable-key URI; otherwise, <see langword="false" />.</returns>
        private static bool LooksLikeStableKey(string value)
        {
            // Stable keys are logical identifiers such as repository://name and must not contain whitespace-only or shell-like text.
            return value.Contains("://", StringComparison.Ordinal) && !value.Any(char.IsWhiteSpace);
        }

        /// <summary>
        /// Creates a validation parse failure.
        /// </summary>
        /// <param name="message">The safe client-correctable validation message.</param>
        /// <returns>A failed parse result.</returns>
        private static ArchonMcpResourceParseResult ValidationFailure(string message)
        {
            // Parse failures use the common MCP error shape so resources and tools share one public failure vocabulary.
            return ArchonMcpResourceParseResult.Failed(ArchonMcpErrorResponse.Create(
                ArchonMcpResourceOperations.ReadResource,
                ArchonMcpErrorCategory.Validation,
                message,
                [new ArchonMcpSuggestedFollowUp("Use a supported URI such as archon://snapshot/current?repository=repository%3A%2F%2Fname.", "user.question", null)]));
        }

        /// <summary>
        /// Creates an unsupported-resource parse failure.
        /// </summary>
        /// <param name="message">The safe unsupported-operation message.</param>
        /// <returns>A failed parse result.</returns>
        private static ArchonMcpResourceParseResult UnsupportedFailure(string message)
        {
            // Unsupported resources fail closed rather than being routed to a generic graph or filesystem reader.
            return ArchonMcpResourceParseResult.Failed(ArchonMcpErrorResponse.Create(
                ArchonMcpResourceOperations.ReadResource,
                ArchonMcpErrorCategory.UnsupportedOperation,
                message,
                [new ArchonMcpSuggestedFollowUp("Use supported current resources or parameterized project, symbol, and snapshot diff resources only.", "user.question", null)]));
        }
    }
}
