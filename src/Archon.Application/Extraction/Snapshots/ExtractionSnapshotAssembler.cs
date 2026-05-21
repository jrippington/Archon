using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Extraction.Snapshots
{
    /// <summary>
    /// Assembles a generalized extracted architecture snapshot from accepted run context and accumulated stage contributions.
    /// </summary>
    public sealed class ExtractionSnapshotAssembler
    {
        /// <summary>
        /// Assembles a snapshot containing deterministic repository and solution boundary facts plus accumulated contributions.
        /// </summary>
        /// <param name="run">The accepted extraction run that scopes the snapshot.</param>
        /// <param name="resolvedInput">The normalized input that has already passed start request validation.</param>
        /// <param name="accumulation">The accumulated stage contributions to merge into the assembled snapshot.</param>
        /// <returns>A generalized extracted architecture snapshot with explicit empty collections for unsupported sections.</returns>
        public ExtractedArchitectureSnapshot Assemble(
            ExtractionRun run,
            ResolvedExtractionInput resolvedInput,
            ArchitectureSnapshotAccumulator accumulation)
        {
            // Assembly is intentionally deterministic and infrastructure-free; persistence adapters decide how to store the resulting contract.
            ArgumentNullException.ThrowIfNull(run);
            ArgumentNullException.ThrowIfNull(resolvedInput);
            ArgumentNullException.ThrowIfNull(accumulation);

            ExtractedArchitectureSnapshot contributedSnapshot = accumulation.ToSnapshot();
            ArchitectureSnapshotAccumulator assembler = new();
            assembler.Merge(contributedSnapshot);

            StableKey repositoryStableKey = StableKeyGenerator.ForRepository(NormalizeIdentitySegment(resolvedInput.RepositoryRootDirectory));
            StableKey snapshotStableKey = StableKeyGenerator.ForSummary(repositoryStableKey.Value, "ExtractionRun", run.RunId.ToString());
            SnapshotHeader snapshotHeader = new(
                snapshotStableKey,
                repositoryStableKey,
                resolvedInput.BranchName,
                resolvedInput.CommitSha,
                run.StartedUtc,
                run.CompletedUtc,
                extractionVersion: "wp004-placeholder",
                status: run.Status.ToString(),
                contributedSnapshot.Warnings,
                contributedSnapshot.Errors,
                CreateMetadata(resolvedInput));
            RepositoryModel repository = new(
                repositoryStableKey,
                name: Path.GetFileName(Path.TrimEndingDirectorySeparator(resolvedInput.RepositoryRootDirectory)),
                rootPath: resolvedInput.RepositoryRootDirectory,
                remoteUrl: null,
                defaultBranch: resolvedInput.BranchName,
                CreateMetadata(resolvedInput));

            assembler.SetSnapshotHeader(snapshotHeader);
            assembler.AddRepository(repository);

            foreach (string solutionPath in resolvedInput.SolutionPaths)
            {
                // Each submitted solution becomes a boundary fact even though real project extraction is intentionally deferred.
                string relativeSolutionPath = GetRepositoryRelativePath(resolvedInput.RepositoryRootDirectory, solutionPath);
                SolutionModel solution = new(
                    repositoryStableKey,
                    StableKeyGenerator.ForSolution(relativeSolutionPath),
                    Path.GetFileName(solutionPath),
                    RepositoryRelativePath.Parse(relativeSolutionPath),
                    GraphMetadata.Empty);
                assembler.AddSolution(solution);
            }

            return assembler.ToSnapshot();
        }

        /// <summary>
        /// Creates deterministic graph metadata from non-sensitive request metadata keys.
        /// </summary>
        /// <param name="resolvedInput">The resolved input whose metadata keys should be represented.</param>
        /// <returns>Graph metadata containing request context suitable for snapshot boundaries.</returns>
        private static GraphMetadata CreateMetadata(ResolvedExtractionInput resolvedInput)
        {
            // Metadata values submitted by callers may be sensitive, so only sorted metadata keys are copied into boundary facts.
            IReadOnlyDictionary<string, object?> values = resolvedInput.Metadata.Keys
                .Order(StringComparer.Ordinal)
                .ToDictionary(key => string.Concat("request.metadataKey.", key), key => (object?)key, StringComparer.Ordinal);
            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Builds a repository-relative path for a validated solution path.
        /// </summary>
        /// <param name="repositoryRootDirectory">The normalized absolute repository root directory.</param>
        /// <param name="solutionPath">The normalized absolute solution path inside the repository root.</param>
        /// <returns>A repository-relative path using forward slash separators.</returns>
        private static string GetRepositoryRelativePath(string repositoryRootDirectory, string solutionPath)
        {
            // Validation already guaranteed containment; this method only makes the path stable for graph identity and display.
            string relativePath = Path.GetRelativePath(repositoryRootDirectory, solutionPath);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Normalizes a filesystem path into a stable identity segment without exposing machine-specific separators as-is.
        /// </summary>
        /// <param name="value">The absolute path value to normalize.</param>
        /// <returns>A deterministic lowercase segment suitable for stable-key generation.</returns>
        private static string NormalizeIdentitySegment(string value)
        {
            // Stable keys must not be database ids, so this path-derived segment is only a deterministic logical identity for the submitted repository.
            string trimmed = Path.TrimEndingDirectorySeparator(value).Replace('\\', '/').Trim();
            return trimmed.ToLowerInvariant();
        }
    }
}
