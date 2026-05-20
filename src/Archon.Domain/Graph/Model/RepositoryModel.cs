using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;

namespace Archon.Domain.Graph.Model
{
    /// <summary>
    /// Represents a source repository independently of any single extraction snapshot.
    /// </summary>
    public sealed class RepositoryModel
    {
        /// <summary>
        /// Initializes a validated repository model.
        /// </summary>
        /// <param name="stableKey">The deterministic stable key that identifies the logical repository.</param>
        /// <param name="name">The developer-facing repository name.</param>
        /// <param name="rootPath">The local root path used during extraction.</param>
        /// <param name="remoteUrl">The optional remote source-control URL.</param>
        /// <param name="defaultBranch">The optional default branch name.</param>
        /// <param name="metadata">Deterministic metadata for repository details that are not normalized fields.</param>
        public RepositoryModel(StableKey stableKey, string? name, string? rootPath, string? remoteUrl, string? defaultBranch, GraphMetadata metadata)
        {
            // The validating constructor keeps required repository fields non-empty while preserving optional remote details.
            ArgumentNullException.ThrowIfNull(metadata);
            StableKey = stableKey;
            Name = GraphFactValidation.RequiredString(name, nameof(name));
            RootPath = GraphFactValidation.RequiredString(rootPath, nameof(rootPath));
            RemoteUrl = GraphFactValidation.OptionalString(remoteUrl);
            DefaultBranch = GraphFactValidation.OptionalString(defaultBranch);
            Metadata = metadata;
        }

        /// <summary>
        /// Gets the deterministic stable key that identifies the logical repository.
        /// </summary>
        public StableKey StableKey { get; }

        /// <summary>
        /// Gets the developer-facing repository name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the local root path used during extraction.
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// Gets the optional remote source-control URL.
        /// </summary>
        public string? RemoteUrl { get; }

        /// <summary>
        /// Gets the optional default branch name.
        /// </summary>
        public string? DefaultBranch { get; }

        /// <summary>
        /// Gets deterministic metadata for repository details that are not normalized fields.
        /// </summary>
        public GraphMetadata Metadata { get; }
    }
}
