namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Normalizes source document paths into repository-relative evidence paths.
    /// </summary>
    /// <remarks>
    /// Semantic extraction must not place developer-machine roots into graph identity or evidence. This helper centralizes the path comparison rules so C# and Visual Basic extraction use the same deterministic repository-relative form.
    /// </remarks>
    public static class SemanticPathNormalizer
    {
        /// <summary>
        /// Converts a document path into a repository-relative path using forward slash separators.
        /// </summary>
        /// <param name="repositoryRootDirectory">The absolute repository root directory for the analyzed repository.</param>
        /// <param name="documentPath">The absolute or already-relative document path to normalize.</param>
        /// <returns>A repository-relative path using forward slash separators.</returns>
        /// <exception cref="ArgumentException">Thrown when the root or document path is missing, or when an absolute document path is outside the repository root.</exception>
        public static string ToRepositoryRelativePath(string repositoryRootDirectory, string documentPath)
        {
            // The helper accepts already-relative paths for in-memory tests while rejecting absolute paths outside the repository root.
            string root = RequireText(repositoryRootDirectory, nameof(repositoryRootDirectory));
            string path = RequireText(documentPath, nameof(documentPath));

            if (!Path.IsPathFullyQualified(path))
            {
                return NormalizeRelativePath(path);
            }

            string normalizedRoot = Path.GetFullPath(root);
            string normalizedPath = Path.GetFullPath(path);
            string rootWithSeparator = EnsureTrailingSeparator(normalizedRoot);

            if (!normalizedPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
                && !StringComparer.OrdinalIgnoreCase.Equals(normalizedPath, Path.TrimEndingDirectorySeparator(normalizedRoot)))
            {
                throw new ArgumentException("The document path must be inside the repository root directory.", nameof(documentPath));
            }

            string relativePath = Path.GetRelativePath(normalizedRoot, normalizedPath);
            return NormalizeRelativePath(relativePath);
        }

        /// <summary>
        /// Ensures an absolute root path ends with the platform directory separator used for containment checks.
        /// </summary>
        /// <param name="path">The absolute path to normalize.</param>
        /// <returns>The path with exactly one trailing directory separator.</returns>
        private static string EnsureTrailingSeparator(string path)
        {
            // Adding a separator prevents false positives such as C:\repo2 matching C:\repo.
            string trimmed = Path.TrimEndingDirectorySeparator(path);
            return string.Concat(trimmed, Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Normalizes a relative path by using forward slashes and removing leading current-directory markers.
        /// </summary>
        /// <param name="path">The relative path to normalize.</param>
        /// <returns>The normalized repository-relative path.</returns>
        private static string NormalizeRelativePath(string path)
        {
            // Path.GetRelativePath may return '.', which cannot identify source evidence and is therefore rejected after trimming.
            string normalized = path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/').Trim();
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized[2..];
            }

            normalized = normalized.Trim('/');
            if (normalized.Length == 0 || StringComparer.Ordinal.Equals(normalized, "."))
            {
                throw new ArgumentException("The document path must identify a repository file.", nameof(path));
            }

            return normalized;
        }

        /// <summary>
        /// Requires non-empty path text before normalization begins.
        /// </summary>
        /// <param name="value">The path text supplied by extraction logic.</param>
        /// <param name="parameterName">The source parameter name used in validation failures.</param>
        /// <returns>The trimmed path text.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Missing paths would produce ambiguous evidence, so callers must provide explicit root and document values.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Path values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }
    }
}
