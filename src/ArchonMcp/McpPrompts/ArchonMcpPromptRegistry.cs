using System.Reflection;
using System.Text.RegularExpressions;

namespace ArchonMcp.McpPrompts
{
    /// <summary>
    /// Loads versioned MCP prompt templates from embedded assembly resources and exposes them as read-only registry entries.
    /// </summary>
    public sealed class ArchonMcpPromptRegistry : IArchonMcpPromptRegistry
    {
        /// <summary>
        /// Matches the front-matter name field used by prompt markdown assets.
        /// </summary>
        private static readonly Regex NamePattern = new("^name:\\s*(?<value>[^\\r\\n]+)\\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>
        /// Matches the front-matter version field used by prompt markdown assets.
        /// </summary>
        private static readonly Regex VersionPattern = new("^version:\\s*(?<value>\\d+)\\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>
        /// Matches the front-matter summary field used by prompt markdown assets.
        /// </summary>
        private static readonly Regex SummaryPattern = new("^summary:\\s*(?<value>[^\\r\\n]+)\\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>
        /// Stores prompt templates by stable name using a case-insensitive comparer for configuration and request resilience.
        /// </summary>
        private readonly IReadOnlyDictionary<string, ArchonMcpPromptTemplate> _templatesByName;

        /// <summary>
        /// Stores prompt descriptors in deterministic display order for prompt-list responses.
        /// </summary>
        private readonly IReadOnlyList<ArchonMcpPromptDescriptor> _descriptors;

        /// <summary>
        /// Creates a prompt registry by loading every configured embedded prompt resource from the MCP host assembly.
        /// </summary>
        public ArchonMcpPromptRegistry()
        {
            // Embedded resources keep prompt templates versioned with the host binary and prevent runtime filesystem probing.
            Assembly assembly = typeof(ArchonMcpPromptRegistry).Assembly;
            string[] resourceNames = assembly.GetManifestResourceNames()
                .Where(name => name.Contains(".Prompts.v1.", StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Dictionary<string, ArchonMcpPromptTemplate> templates = new(StringComparer.OrdinalIgnoreCase);
            foreach (string resourceName in resourceNames)
            {
                // Each asset carries front matter so tests can validate name/version alignment independent of file naming.
                ArchonMcpPromptTemplate template = LoadTemplate(assembly, resourceName);
                templates.Add(template.Name, template);
            }

            _templatesByName = templates;
            _descriptors = templates.Values
                .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
                .Select(template => new ArchonMcpPromptDescriptor(template.Name, template.Version, template.Summary))
                .ToArray();
        }

        /// <inheritdoc />
        public IReadOnlyList<ArchonMcpPromptDescriptor> ListPrompts()
        {
            // Return the immutable descriptor snapshot so callers cannot mutate registry state.
            return _descriptors;
        }

        /// <inheritdoc />
        public bool TryGetPrompt(string name, out ArchonMcpPromptTemplate? template)
        {
            // Empty names never match a prompt and are left to the tool layer for structured validation messages.
            if (string.IsNullOrWhiteSpace(name))
            {
                template = null;
                return false;
            }

            return _templatesByName.TryGetValue(name.Trim(), out template);
        }

        /// <summary>
        /// Loads and validates one embedded markdown prompt template.
        /// </summary>
        /// <param name="assembly">The assembly that owns the embedded prompt resource.</param>
        /// <param name="resourceName">The manifest resource name to load.</param>
        /// <returns>The parsed prompt template.</returns>
        private static ArchonMcpPromptTemplate LoadTemplate(Assembly assembly, string resourceName)
        {
            // The registry fails fast if an expected embedded resource cannot be opened because prompts are mandatory runtime assets.
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded MCP prompt resource '{resourceName}' was not found.");
            using StreamReader reader = new(stream);
            string content = reader.ReadToEnd();

            string name = ReadRequiredFrontMatterValue(NamePattern, content, resourceName, "name");
            string versionText = ReadRequiredFrontMatterValue(VersionPattern, content, resourceName, "version");
            string summary = ReadRequiredFrontMatterValue(SummaryPattern, content, resourceName, "summary");

            if (!int.TryParse(versionText, out int version) || version <= 0)
            {
                // A non-positive version would make prompt compatibility and audit context ambiguous.
                throw new InvalidOperationException($"Embedded MCP prompt resource '{resourceName}' has an invalid version value.");
            }

            return new ArchonMcpPromptTemplate(name, version, summary, resourceName, content);
        }

        /// <summary>
        /// Reads one required front-matter value from a prompt asset.
        /// </summary>
        /// <param name="pattern">The compiled regular expression that locates the required field.</param>
        /// <param name="content">The prompt markdown content being parsed.</param>
        /// <param name="resourceName">The resource name used for safe diagnostics.</param>
        /// <param name="fieldName">The front-matter field name used for safe diagnostics.</param>
        /// <returns>The trimmed front-matter value.</returns>
        private static string ReadRequiredFrontMatterValue(Regex pattern, string content, string resourceName, string fieldName)
        {
            // Prompt assets are developer-maintained, so a missing field is a startup-time asset defect rather than a caller error.
            Match match = pattern.Match(content);
            if (!match.Success)
            {
                throw new InvalidOperationException($"Embedded MCP prompt resource '{resourceName}' is missing required '{fieldName}' front matter.");
            }

            string value = match.Groups["value"].Value.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Embedded MCP prompt resource '{resourceName}' has an empty '{fieldName}' front-matter value.");
            }

            return value;
        }
    }
}
