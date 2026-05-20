using System.Text;

namespace Archon.Domain.Graph.Identity
{
    /// <summary>
    /// Represents a repository-root-relative path normalized for deterministic stable-key generation.
    /// </summary>
    /// <remarks>
    /// Repository-relative paths intentionally exclude developer-machine roots such as drive letters, home directories, and UNC shares.
    /// This keeps graph identities stable when the same repository is analyzed on different machines or CI agents.
    /// </remarks>
    public readonly record struct RepositoryRelativePath
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RepositoryRelativePath"/> struct.
        /// </summary>
        /// <param name="value">The already normalized repository-relative path.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, whitespace-only, or absolute.</exception>
        private RepositoryRelativePath(string? value)
        {
            // Construction goes through Parse so all path instances use one normalization and validation flow.
            Value = Normalize(value);
        }

        /// <summary>
        /// Gets the normalized repository-relative path using forward slashes.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Parses and normalizes a repository-relative path.
        /// </summary>
        /// <param name="path">The repository-relative path supplied by extraction or caller code.</param>
        /// <returns>A normalized repository-relative path value.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null, empty, whitespace-only, or absolute.</exception>
        public static RepositoryRelativePath Parse(string? path)
        {
            // Parse is the public gateway so invalid machine-specific paths are rejected before stable keys are generated.
            return new RepositoryRelativePath(path);
        }

        /// <summary>
        /// Returns the normalized repository-relative path.
        /// </summary>
        /// <returns>The normalized repository-relative path using forward slashes.</returns>
        public override string ToString()
        {
            // ToString mirrors Value to keep generated-key assembly and diagnostics simple and deterministic.
            return Value;
        }

        /// <summary>
        /// Normalizes separators and relative-prefix syntax while rejecting absolute paths.
        /// </summary>
        /// <param name="path">The caller-supplied repository-relative path.</param>
        /// <returns>The normalized repository-relative path.</returns>
        private static string Normalize(string? path)
        {
            // Blank paths cannot identify a repository artifact and therefore cannot participate in stable-key generation.
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Repository-relative paths cannot be null, empty, or whitespace.", nameof(path));
            }

            string trimmedPath = path.Trim();
            string slashPath = trimmedPath.Replace('\\', '/');

            // Reject common absolute forms before removing leading relative prefixes, so machine roots never leak into identities.
            if (IsAbsoluteOrRooted(slashPath))
            {
                throw new ArgumentException("Repository-relative paths must not be absolute or rooted.", nameof(path));
            }

            string withoutRelativePrefix = RemoveLeadingRelativePrefixes(slashPath);
            string collapsed = CollapseRepeatedSlashes(withoutRelativePrefix);
            string normalized = collapsed.Trim('/');

            if (normalized.Length == 0)
            {
                throw new ArgumentException("Repository-relative paths must identify a repository artifact.", nameof(path));
            }

            return normalized;
        }

        /// <summary>
        /// Determines whether a normalized slash path is absolute, rooted, drive-qualified, or UNC-like.
        /// </summary>
        /// <param name="slashPath">The path after backslashes have been converted to forward slashes.</param>
        /// <returns><see langword="true"/> when the path includes a machine-specific root; otherwise, <see langword="false"/>.</returns>
        private static bool IsAbsoluteOrRooted(string slashPath)
        {
            // Forward-slash rooted and UNC-like paths are machine or environment specific.
            if (slashPath.StartsWith("/", StringComparison.Ordinal))
            {
                return true;
            }

            // Windows drive-qualified paths such as D:/repo/file.cs embed a machine-local root and must be rejected.
            return slashPath.Length >= 3
                && char.IsLetter(slashPath[0])
                && slashPath[1] == ':'
                && slashPath[2] == '/';
        }

        /// <summary>
        /// Removes repeated leading current-directory markers from a relative path.
        /// </summary>
        /// <param name="slashPath">The slash-normalized repository-relative path.</param>
        /// <returns>The path without leading <c>./</c> segments.</returns>
        private static string RemoveLeadingRelativePrefixes(string slashPath)
        {
            // Leading ./ is a caller convenience and should not affect key identity.
            string result = slashPath;
            while (result.StartsWith("./", StringComparison.Ordinal))
            {
                result = result[2..];
            }

            return result;
        }

        /// <summary>
        /// Collapses repeated slash separators while preserving all path segment characters.
        /// </summary>
        /// <param name="path">The relative path whose separators should be collapsed.</param>
        /// <returns>The path with repeated slash separators reduced to one slash.</returns>
        private static string CollapseRepeatedSlashes(string path)
        {
            // A tiny deterministic loop avoids depending on platform path APIs that may reinterpret repository-relative strings.
            StringBuilder builder = new(path.Length);
            bool previousWasSlash = false;

            foreach (char character in path)
            {
                if (character == '/')
                {
                    if (!previousWasSlash)
                    {
                        builder.Append(character);
                    }

                    previousWasSlash = true;
                }
                else
                {
                    builder.Append(character);
                    previousWasSlash = false;
                }
            }

            return builder.ToString();
        }
    }
}
