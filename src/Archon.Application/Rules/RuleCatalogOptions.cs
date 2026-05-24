namespace Archon.Application.Rules
{
    /// <summary>
    /// Provides runtime configuration for locating copied rule catalog content.
    /// </summary>
    public sealed class RuleCatalogOptions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RuleCatalogOptions"/> class using the default copied-output rules folder.
        /// </summary>
        public RuleCatalogOptions()
            : this(null)
        {
            // The default constructor is used by hosts and tests that rely on AppContext.BaseDirectory/rules output content.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleCatalogOptions"/> class.
        /// </summary>
        /// <param name="rulesDirectory">The optional absolute or relative runtime folder that contains copied rule JSON files.</param>
        public RuleCatalogOptions(string? rulesDirectory)
        {
            // A null directory intentionally means copied output under AppContext.BaseDirectory, not a repository source path.
            RulesDirectory = string.IsNullOrWhiteSpace(rulesDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "rules")
                : Path.GetFullPath(rulesDirectory);
        }

        /// <summary>
        /// Gets the runtime folder that contains copied rule JSON files.
        /// </summary>
        public string RulesDirectory { get; }
    }
}
