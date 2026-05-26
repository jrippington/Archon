using Microsoft.Extensions.Options;

namespace ArchonMcp.McpRuntime
{
    /// <summary>
    /// Implements the baseline Archon MCP capability catalog and validation rules.
    /// </summary>
    /// <remarks>
    /// The catalog intentionally behaves as an allow-list rather than a discovery mechanism. Work Item 1 only registers the
    /// operational baseline, and validation protects readiness from missing required entries or capability names that would imply
    /// mutation, arbitrary execution, direct database access, or source-code modification.
    /// </remarks>
    public sealed class ArchonMcpRegistrationCatalog : IArchonMcpRegistrationCatalog
    {
        /// <summary>
        /// Stores the case-insensitive fragments that identify capability names forbidden by the read-only MCP baseline.
        /// </summary>
        private static readonly string[] ForbiddenNameFragments =
        [
            "shell",
            "sql",
            "cypher",
            "filesystem",
            "file_system",
            "neo4j",
            "graph_query",
            "mutation",
            "mutate",
            "write",
            "delete",
            "update",
            "execute",
            "exec",
            "command",
            "code_modification",
            "code-edit",
            "code_edit"
        ];

        /// <summary>
        /// Keeps the configured registrations as the source list used by deterministic enumeration and validation.
        /// </summary>
        private readonly IReadOnlyList<ArchonMcpCapabilityRegistration> _registrations;

        /// <summary>
        /// Keeps the mandatory capability names bound from configuration and options defaults.
        /// </summary>
        private readonly ArchonMcpRegistrationCatalogOptions _options;

        /// <summary>
        /// Creates a catalog from the registered capability entries and registration options.
        /// </summary>
        /// <param name="registrations">The capability registrations supplied by host composition.</param>
        /// <param name="options">The options that list mandatory capability names for readiness validation.</param>
        public ArchonMcpRegistrationCatalog(
            IEnumerable<ArchonMcpCapabilityRegistration> registrations,
            IOptions<ArchonMcpRegistrationCatalogOptions> options)
        {
            // The constructor snapshots inputs so catalog validation remains deterministic for the lifetime of the host.
            ArgumentNullException.ThrowIfNull(registrations);
            ArgumentNullException.ThrowIfNull(options);

            _registrations = registrations
                .OrderBy(registration => registration.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _options = options.Value;
        }

        /// <inheritdoc />
        public IReadOnlyList<ArchonMcpCapabilityRegistration> GetRegistrations()
        {
            // Return the immutable snapshot rather than rebuilding the catalog for each readiness probe.
            return _registrations;
        }

        /// <inheritdoc />
        public ArchonMcpCatalogValidationResult Validate()
        {
            // Compare mandatory and registered names using ordinal-insensitive matching because MCP capability names are stable
            // protocol-facing tokens while configuration authors may not preserve casing exactly.
            HashSet<string> registeredNames = _registrations
                .Select(registration => registration.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            string[] missingRequiredCapabilityNames = _options.MandatoryCapabilityNames
                .Where(name => !registeredNames.Contains(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            string[] forbiddenCapabilityNames = _registrations
                .Where(registration => IsForbiddenCapabilityName(registration.Name) || !registration.ReadOnly)
                .Select(registration => registration.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            bool isReady = missingRequiredCapabilityNames.Length == 0 && forbiddenCapabilityNames.Length == 0;

            return new ArchonMcpCatalogValidationResult(
                isReady,
                missingRequiredCapabilityNames,
                forbiddenCapabilityNames);
        }

        /// <summary>
        /// Determines whether a capability name contains a forbidden fragment for the read-only baseline.
        /// </summary>
        /// <param name="capabilityName">The stable capability name being validated.</param>
        /// <returns><see langword="true" /> when the name implies an unsafe capability; otherwise, <see langword="false" />.</returns>
        private static bool IsForbiddenCapabilityName(string capabilityName)
        {
            // Empty names are treated as forbidden because they cannot be safely audited or matched to a mandatory registration.
            if (string.IsNullOrWhiteSpace(capabilityName))
            {
                return true;
            }

            return ForbiddenNameFragments.Any(fragment => capabilityName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }
    }
}
