using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;

namespace Archon.Domain.Graph.Model
{
    /// <summary>
    /// Represents a solution file associated with a repository and submitted for architecture extraction.
    /// </summary>
    public sealed class SolutionModel
    {
        /// <summary>
        /// Initializes a validated solution model.
        /// </summary>
        /// <param name="repositoryStableKey">The stable key of the repository containing the solution.</param>
        /// <param name="stableKey">The deterministic stable key that identifies the solution.</param>
        /// <param name="name">The developer-facing solution name.</param>
        /// <param name="path">The repository-relative path to the solution file.</param>
        /// <param name="metadata">Deterministic metadata for solution details that are not normalized fields.</param>
        public SolutionModel(StableKey repositoryStableKey, StableKey stableKey, string? name, RepositoryRelativePath path, GraphMetadata metadata)
        {
            // The stable keys are value objects; this constructor validates the remaining reference-style inputs.
            ArgumentNullException.ThrowIfNull(metadata);
            RepositoryStableKey = repositoryStableKey;
            StableKey = stableKey;
            Name = GraphFactValidation.RequiredString(name, nameof(name));
            Path = path;
            Metadata = metadata;
        }

        /// <summary>
        /// Gets the stable key of the repository containing the solution.
        /// </summary>
        public StableKey RepositoryStableKey { get; }

        /// <summary>
        /// Gets the deterministic stable key that identifies the solution.
        /// </summary>
        public StableKey StableKey { get; }

        /// <summary>
        /// Gets the developer-facing solution name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the repository-relative path to the solution file.
        /// </summary>
        public RepositoryRelativePath Path { get; }

        /// <summary>
        /// Gets deterministic metadata for solution details that are not normalized fields.
        /// </summary>
        public GraphMetadata Metadata { get; }
    }
}
