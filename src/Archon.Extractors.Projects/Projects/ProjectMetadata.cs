using Archon.Domain.Graph.Metadata;
using Archon.Extractors.Projects.Packages;

namespace Archon.Extractors.Projects.Projects
{
    /// <summary>
    /// Represents deterministic metadata extracted from a supported C# or VB.NET project file without executing build targets.
    /// </summary>
    /// <param name="ProjectName">The display name that should be used for the project node.</param>
    /// <param name="RelativeProjectPath">The repository-relative project path normalized with forward slash separators.</param>
    /// <param name="Language">The supported source language represented by the project file.</param>
    /// <param name="TargetFramework">The single target framework value declared by `TargetFramework`, when present.</param>
    /// <param name="TargetFrameworks">The ordered multi-target framework values declared by `TargetFrameworks`, when present.</param>
    /// <param name="LegacyTargetFramework">The legacy target framework version value declared by old-style projects, when present.</param>
    /// <param name="OutputType">The project output type value, when present.</param>
    /// <param name="AssemblyName">The explicit or deterministic default assembly name.</param>
    /// <param name="RootNamespace">The explicit project root namespace, when present.</param>
    /// <param name="Sdk">The SDK value declared on the project root element, when present.</param>
    /// <param name="IsSdkStyle">A value indicating whether the project uses SDK-style root metadata.</param>
    /// <param name="IsOldStyle">A value indicating whether the project uses old-style MSBuild XML metadata.</param>
    /// <param name="Nullable">The nullable context setting declared by the project, when present.</param>
    /// <param name="ImplicitUsings">The implicit using directives setting declared by the project, when present.</param>
    /// <param name="ProjectReferences">The project-reference declarations discovered in the project file.</param>
    /// <param name="PackageReferences">The package-reference declarations discovered in the project file and safe imported build files.</param>
    /// <param name="PackageDiagnostics">The controlled package extraction diagnostics produced by package-adjacent artifacts.</param>
    /// <param name="LineCount">The number of lines read from the project file for evidence fallback spans.</param>
    internal sealed record ProjectMetadata(
        string ProjectName,
        string RelativeProjectPath,
        ProjectLanguage Language,
        string? TargetFramework,
        IReadOnlyList<string> TargetFrameworks,
        string? LegacyTargetFramework,
        string? OutputType,
        string AssemblyName,
        string? RootNamespace,
        string? Sdk,
        bool IsSdkStyle,
        bool IsOldStyle,
        string? Nullable,
        string? ImplicitUsings,
        IReadOnlyList<ProjectReferenceDeclaration> ProjectReferences,
        IReadOnlyList<PackageReferenceDeclaration> PackageReferences,
        IReadOnlyList<PackageExtractionDiagnostic> PackageDiagnostics,
        int LineCount)
    {
        /// <summary>
        /// Converts the project metadata into deterministic graph metadata for a project architecture node.
        /// </summary>
        /// <returns>Graph metadata containing supported project identity, language, format, and build metadata facts.</returns>
        internal GraphMetadata ToGraphMetadata()
        {
            // Metadata intentionally stores concise build facts and does not retain full project-file XML content.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["project.name"] = ProjectName,
                ["project.relativePath"] = RelativeProjectPath,
                ["project.language"] = ToLanguageDisplayName(Language),
                ["project.isSdkStyle"] = IsSdkStyle,
                ["project.isOldStyle"] = IsOldStyle,
                ["project.assemblyName"] = AssemblyName,
                ["project.projectReferenceCount"] = ProjectReferences.Count,
                ["project.packageReferenceCount"] = PackageReferences.Count,
                ["project.lineCount"] = LineCount
            };

            AddOptional(values, "project.targetFramework", TargetFramework);
            AddOptional(values, "project.legacyTargetFramework", LegacyTargetFramework);
            AddOptional(values, "project.outputType", OutputType);
            AddOptional(values, "project.rootNamespace", RootNamespace);
            AddOptional(values, "project.sdk", Sdk);
            AddOptional(values, "project.nullable", Nullable);
            AddOptional(values, "project.implicitUsings", ImplicitUsings);

            if (TargetFrameworks.Count > 0)
            {
                values["project.targetFrameworks"] = TargetFrameworks.ToArray();
            }

            if (string.IsNullOrWhiteSpace(TargetFramework) && TargetFrameworks.Count == 0 && string.IsNullOrWhiteSpace(LegacyTargetFramework))
            {
                values["project.targetFrameworkUnknown"] = true;
            }

            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Converts a supported project language into the graph metadata display value.
        /// </summary>
        /// <param name="language">The supported project language value.</param>
        /// <returns>The display name used in project node language and metadata fields.</returns>
        internal static string ToLanguageDisplayName(ProjectLanguage language)
        {
            // Display names match developer terminology used by tests, wiki, and snapshot consumers.
            return language == ProjectLanguage.VisualBasic ? "VB.NET" : "C#";
        }

        /// <summary>
        /// Adds optional text metadata after trimming insignificant whitespace.
        /// </summary>
        /// <param name="values">The metadata dictionary being assembled.</param>
        /// <param name="key">The metadata key to populate.</param>
        /// <param name="value">The optional text value to add.</param>
        private static void AddOptional(Dictionary<string, object?> values, string key, string? value)
        {
            // Optional properties are omitted when blank so unknown values remain explicit only where required.
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value.Trim();
            }
        }
    }

}
