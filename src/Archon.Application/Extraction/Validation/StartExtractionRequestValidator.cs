using Archon.Application.Extraction.Requests;
using Archon.Application.Extraction.Resolution;

namespace Archon.Application.Extraction.Validation
{
    /// <summary>
    /// Validates and normalizes extraction start requests before any run state or extraction work is created.
    /// </summary>
    public sealed class StartExtractionRequestValidator
    {
        /// <summary>
        /// Validates a start request and produces normalized extraction input when the request is acceptable.
        /// </summary>
        /// <param name="request">The submitted extraction start request to validate.</param>
        /// <returns>A validation result containing either normalized input or blocking validation errors.</returns>
        public StartExtractionValidationResult Validate(StartExtractionRequest request)
        {
            // Validation intentionally precedes run creation so rejected requests cannot create operational run records.
            ArgumentNullException.ThrowIfNull(request);

            List<StartExtractionValidationError> errors = [];
            string? repositoryRoot = NormalizeRepositoryRoot(request.RepositoryRootDirectory, errors);
            IReadOnlyList<string> normalizedSolutionPaths = NormalizeSolutionPaths(repositoryRoot, request.SolutionPaths, errors);

            if (errors.Count > 0 || repositoryRoot is null)
            {
                return new StartExtractionValidationResult(null, errors);
            }

            ResolvedExtractionInput resolvedInput = new(
                repositoryRoot,
                normalizedSolutionPaths,
                NormalizeOptionalText(request.BranchName),
                NormalizeOptionalText(request.CommitSha),
                NormalizeOptionalText(request.RequestedBy),
                CopyMetadata(request.Metadata));

            return new StartExtractionValidationResult(resolvedInput, []);
        }

        /// <summary>
        /// Normalizes and validates the repository root directory.
        /// </summary>
        /// <param name="repositoryRootDirectory">The submitted repository root directory value.</param>
        /// <param name="errors">The validation error collection to append to.</param>
        /// <returns>The normalized repository root when valid; otherwise <see langword="null"/>.</returns>
        private static string? NormalizeRepositoryRoot(string? repositoryRootDirectory, List<StartExtractionValidationError> errors)
        {
            // Repository root validation is separate so solution checks can safely resolve relative paths only when a root exists.
            if (string.IsNullOrWhiteSpace(repositoryRootDirectory))
            {
                errors.Add(new StartExtractionValidationError(
                    StartExtractionValidationErrorCodes.RepositoryRootRequired,
                    "Repository root directory is required."));
                return null;
            }

            string normalizedRoot = Path.GetFullPath(repositoryRootDirectory.Trim());
            if (!Directory.Exists(normalizedRoot))
            {
                errors.Add(new StartExtractionValidationError(
                    StartExtractionValidationErrorCodes.RepositoryRootNotFound,
                    "Repository root directory was not found or is not accessible."));
                return null;
            }

            return EnsureTrailingDirectorySeparator(normalizedRoot);
        }

        /// <summary>
        /// Normalizes, validates, and de-duplicates submitted solution paths.
        /// </summary>
        /// <param name="repositoryRoot">The normalized repository root, or <see langword="null"/> when root validation failed.</param>
        /// <param name="solutionPaths">The submitted solution path values.</param>
        /// <param name="errors">The validation error collection to append to.</param>
        /// <returns>The normalized solution paths that passed structural normalization.</returns>
        private static IReadOnlyList<string> NormalizeSolutionPaths(
            string? repositoryRoot,
            IReadOnlyList<string>? solutionPaths,
            List<StartExtractionValidationError> errors)
        {
            // The explicit list requirement prevents future extraction from silently scanning the repository for solutions.
            if (solutionPaths is null || solutionPaths.Count == 0 || solutionPaths.All(string.IsNullOrWhiteSpace))
            {
                errors.Add(new StartExtractionValidationError(
                    StartExtractionValidationErrorCodes.SolutionPathRequired,
                    "At least one explicit solution path is required."));
                return [];
            }

            if (repositoryRoot is null)
            {
                return [];
            }

            List<string> normalizedSolutionPaths = [];
            HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);

            foreach (string? submittedSolutionPath in solutionPaths)
            {
                if (string.IsNullOrWhiteSpace(submittedSolutionPath))
                {
                    errors.Add(new StartExtractionValidationError(
                        StartExtractionValidationErrorCodes.SolutionPathRequired,
                        "Solution path entries must not be blank."));
                    continue;
                }

                string normalizedSolutionPath = NormalizeSolutionPath(repositoryRoot, submittedSolutionPath);

                if (!IsInsideRepositoryRoot(repositoryRoot, normalizedSolutionPath))
                {
                    errors.Add(new StartExtractionValidationError(
                        StartExtractionValidationErrorCodes.SolutionPathOutsideRepositoryRoot,
                        "Solution path must resolve inside the submitted repository root."));
                    continue;
                }

                if (!IsSupportedSolutionFile(normalizedSolutionPath))
                {
                    errors.Add(new StartExtractionValidationError(
                        StartExtractionValidationErrorCodes.SolutionPathExtensionInvalid,
                        "Solution path must reference a .sln or .slnx file."));
                    continue;
                }

                if (!File.Exists(normalizedSolutionPath))
                {
                    errors.Add(new StartExtractionValidationError(
                        StartExtractionValidationErrorCodes.SolutionPathNotFound,
                        "Solution file was not found or is not accessible."));
                    continue;
                }

                if (!seenPaths.Add(normalizedSolutionPath))
                {
                    errors.Add(new StartExtractionValidationError(
                        StartExtractionValidationErrorCodes.SolutionPathDuplicate,
                        "Solution paths must be unique after normalization."));
                    continue;
                }

                normalizedSolutionPaths.Add(normalizedSolutionPath);
            }

            return normalizedSolutionPaths;
        }

        /// <summary>
        /// Resolves a submitted solution path to a normalized absolute filesystem path.
        /// </summary>
        /// <param name="repositoryRoot">The normalized repository root used for relative path resolution.</param>
        /// <param name="solutionPath">The submitted solution path value.</param>
        /// <returns>The normalized absolute solution path.</returns>
        private static string NormalizeSolutionPath(string repositoryRoot, string solutionPath)
        {
            // Relative solution paths are interpreted from the accepted repository root, matching the public API contract.
            string trimmedPath = solutionPath.Trim();
            return Path.GetFullPath(Path.IsPathRooted(trimmedPath) ? trimmedPath : Path.Combine(repositoryRoot, trimmedPath));
        }

        /// <summary>
        /// Determines whether the normalized path names a supported solution file format.
        /// </summary>
        /// <param name="solutionPath">The normalized solution path to inspect.</param>
        /// <returns><see langword="true" /> when the path uses a supported solution extension; otherwise <see langword="false" />.</returns>
        private static bool IsSupportedSolutionFile(string solutionPath)
        {
            // Extraction accepts both legacy .sln files and newer .slnx files so API-triggered analysis matches repository guidance.
            string extension = Path.GetExtension(solutionPath);
            return string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a normalized path is inside the normalized repository root.
        /// </summary>
        /// <param name="repositoryRoot">The normalized repository root including a trailing separator.</param>
        /// <param name="candidatePath">The normalized candidate path to test.</param>
        /// <returns><see langword="true"/> when the candidate is inside the repository root; otherwise <see langword="false"/>.</returns>
        private static bool IsInsideRepositoryRoot(string repositoryRoot, string candidatePath)
        {
            // Prefix comparison uses OrdinalIgnoreCase because Work Item 1 targets deterministic Windows path behavior.
            return candidatePath.StartsWith(repositoryRoot, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Ensures a directory path ends with exactly one directory separator for safe containment checks.
        /// </summary>
        /// <param name="path">The normalized directory path to adjust.</param>
        /// <returns>The directory path with a trailing separator.</returns>
        private static string EnsureTrailingDirectorySeparator(string path)
        {
            // The trailing separator prevents sibling prefixes such as C:\repo-other from matching C:\repo.
            return Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// Trims optional text and converts blank values to null.
        /// </summary>
        /// <param name="value">The optional submitted text.</param>
        /// <returns>The trimmed value, or <see langword="null"/> when the input is blank.</returns>
        private static string? NormalizeOptionalText(string? value)
        {
            // Optional request values should not preserve accidental whitespace as meaningful audit data.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Copies metadata into deterministic key order while preserving caller-provided values for later application use.
        /// </summary>
        /// <param name="metadata">The nullable metadata dictionary supplied by the caller.</param>
        /// <returns>A read-only metadata dictionary ordered by key.</returns>
        private static IReadOnlyDictionary<string, string> CopyMetadata(IReadOnlyDictionary<string, string>? metadata)
        {
            // Metadata values are preserved in application state but only keys are copied into the public run summary to avoid accidental value disclosure.
            return metadata is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : metadata
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value, StringComparer.Ordinal);
        }
    }
}
