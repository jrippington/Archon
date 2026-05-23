using System.Security.Cryptography;
using System.Text;
using Archon.Domain.Graph.Identity;

namespace Archon.Extractors.Integrations.Foundation
{
    /// <summary>
    /// Generates deterministic WP010 external integration stable keys without depending on machine-local paths or live external state.
    /// </summary>
    /// <remarks>
    /// The foundation helper wraps the domain <see cref="StableKeyGenerator" /> for known logical targets and adds explicit unknown and relationship keys required by external integration extraction.
    /// </remarks>
    public static class ExternalIntegrationStableKey
    {
        /// <summary>
        /// Generates the stable key for a known external service integration target.
        /// </summary>
        /// <param name="repositoryRootDirectory">The analyzed repository root; accepted for symmetry with unknown helpers but intentionally excluded from the key.</param>
        /// <param name="serviceName">The canonical service identity supported by source evidence.</param>
        /// <returns>A deterministic external-service stable key.</returns>
        public static StableKey ForExternalService(string? repositoryRootDirectory, string? serviceName)
        {
            // Known external services are logical identities, so absolute repository roots must never influence the key.
            _ = repositoryRootDirectory;
            return StableKeyGenerator.ForExternalService(serviceName);
        }

        /// <summary>
        /// Generates a placeholder-safe stable key for an unknown external service target from repository-relative evidence identity.
        /// </summary>
        /// <param name="repositoryRootDirectory">The analyzed repository root used only to derive a repository-relative evidence path.</param>
        /// <param name="evidenceFilePath">The evidence file path, either absolute under the repository root or already repository-relative.</param>
        /// <param name="lineNumber">The one-based evidence line number that scopes the unknown target.</param>
        /// <param name="discriminator">The source detector discriminator, such as a method name or detection mode.</param>
        /// <returns>A deterministic unknown external-service stable key.</returns>
        public static StableKey ForUnknownExternalService(string? repositoryRootDirectory, string? evidenceFilePath, int lineNumber, string? discriminator)
        {
            // Unknown keys use evidence location plus a hash of the detector discriminator so no absolute path or runtime endpoint value is persisted.
            string relativePath = NormalizeRepositoryRelativePath(repositoryRootDirectory, evidenceFilePath);
            string discriminatorHash = CreateHash(RequireText(discriminator, nameof(discriminator)));
            return new StableKey($"externalservice://unknown/{relativePath}/{lineNumber}/{discriminatorHash}");
        }

        /// <summary>
        /// Generates the stable key for a known queue integration target.
        /// </summary>
        /// <param name="provider">The transport or provider name, such as Azure Service Bus or RabbitMQ.</param>
        /// <param name="queueName">The queue name supported by source evidence.</param>
        /// <returns>A deterministic queue stable key.</returns>
        public static StableKey ForQueue(string? provider, string? queueName)
        {
            // Provider plus logical name avoids collisions between queues with the same name on different transports.
            return StableKeyGenerator.ForQueue($"{RequireText(provider, nameof(provider))}:{RequireText(queueName, nameof(queueName))}");
        }

        /// <summary>
        /// Generates the stable key for a known topic integration target.
        /// </summary>
        /// <param name="provider">The transport or provider name, such as Azure Service Bus or RabbitMQ.</param>
        /// <param name="topicName">The topic name supported by source evidence.</param>
        /// <returns>A deterministic topic stable key.</returns>
        public static StableKey ForTopic(string? provider, string? topicName)
        {
            // Provider plus logical name avoids collisions between topics with the same name on different transports.
            return StableKeyGenerator.ForTopic($"{RequireText(provider, nameof(provider))}:{RequireText(topicName, nameof(topicName))}");
        }

        /// <summary>
        /// Generates a stable key for a graph relationship emitted by the integration extractor foundation slice.
        /// </summary>
        /// <param name="relationshipKind">The relationship kind value, such as <c>CALLS_EXTERNAL_SERVICE</c> or <c>HANDLES</c>.</param>
        /// <param name="sourceStableKey">The source node stable-key string.</param>
        /// <param name="targetStableKey">The target node stable-key string.</param>
        /// <returns>A deterministic relationship stable key.</returns>
        public static StableKey ForRelationship(string? relationshipKind, string? sourceStableKey, string? targetStableKey)
        {
            // Relationship keys are intentionally semantic and endpoint-based so accumulation can de-duplicate equivalent facts.
            return new StableKey($"relationship://{RequireText(relationshipKind, nameof(relationshipKind))}/{RequireText(sourceStableKey, nameof(sourceStableKey))}/{RequireText(targetStableKey, nameof(targetStableKey))}");
        }

        /// <summary>
        /// Generates a stable key for evidence emitted by the integration extractor foundation slice.
        /// </summary>
        /// <param name="targetStableKey">The stable key of the target fact explained by the evidence.</param>
        /// <param name="repositoryRootDirectory">The analyzed repository root used only to normalize the evidence file path.</param>
        /// <param name="evidenceFilePath">The evidence file path, either absolute under the repository root or repository-relative.</param>
        /// <param name="startLine">The one-based start line for evidence.</param>
        /// <param name="endLine">The one-based end line for evidence.</param>
        /// <param name="detectionMode">The detector mode that found the evidence.</param>
        /// <returns>A deterministic evidence stable key.</returns>
        public static StableKey ForEvidence(StableKey targetStableKey, string? repositoryRootDirectory, string? evidenceFilePath, int? startLine, int? endLine, string? detectionMode)
        {
            // Evidence identity includes normalized location and detection mode so multiple detectors can explain the same target without collisions.
            string relativePath = NormalizeRepositoryRelativePath(repositoryRootDirectory, evidenceFilePath);
            string location = $"{relativePath}:{startLine?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}-{endLine?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}";
            string discriminator = CreateHash(targetStableKey.Value + "|" + location + "|" + RequireText(detectionMode, nameof(detectionMode)));
            return new StableKey($"evidence://integration/{relativePath}/{discriminator}");
        }

        /// <summary>
        /// Normalizes an evidence path to a repository-relative slash-separated path.
        /// </summary>
        /// <param name="repositoryRootDirectory">The analyzed repository root, if available.</param>
        /// <param name="path">The candidate absolute or repository-relative path.</param>
        /// <returns>A repository-relative path using forward slashes.</returns>
        private static string NormalizeRepositoryRelativePath(string? repositoryRootDirectory, string? path)
        {
            // Absolute evidence paths are reduced under the repository root; relative paths are parsed directly by the domain value object.
            string text = RequireText(path, nameof(path));
            string normalizedText = text.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            if (!string.IsNullOrWhiteSpace(repositoryRootDirectory) && Path.IsPathRooted(text))
            {
                string relative = Path.GetRelativePath(repositoryRootDirectory, text);
                normalizedText = relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            }

            return RepositoryRelativePath.Parse(normalizedText).Value;
        }

        /// <summary>
        /// Requires a non-empty text value and trims surrounding whitespace.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <param name="parameterName">The parameter name used when reporting validation failures.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Explicit key segments prevent ambiguous identities for integration graph facts.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("External integration stable-key components cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }

        /// <summary>
        /// Creates a lowercase SHA-256 hash for key-safe discriminators.
        /// </summary>
        /// <param name="value">The canonical value to hash.</param>
        /// <returns>A lowercase hexadecimal SHA-256 hash.</returns>
        private static string CreateHash(string value)
        {
            // Hashing keeps unknown and evidence discriminators deterministic without exposing long source snippets or runtime endpoint values.
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
