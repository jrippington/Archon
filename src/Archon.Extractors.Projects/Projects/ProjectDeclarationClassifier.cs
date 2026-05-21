using Archon.Extractors.Projects.Solutions;

namespace Archon.Extractors.Projects.Projects
{
    /// <summary>
    /// Classifies solution project declarations into supported C# and VB.NET projects or unsupported project kinds.
    /// </summary>
    internal static class ProjectDeclarationClassifier
    {
        /// <summary>
        /// Stores the canonical Visual Studio C# project type GUID in lowercase form for ordinal comparisons.
        /// </summary>
        private const string CSharpProjectTypeGuid = "{fae04ec0-301f-11d3-bf4b-00c04f79efbc}";

        /// <summary>
        /// Stores the canonical Visual Studio VB.NET project type GUID in lowercase form for ordinal comparisons.
        /// </summary>
        private const string VisualBasicProjectTypeGuid = "{f184b08f-c81c-45f6-a57f-5abd9991f28f}";

        /// <summary>
        /// Attempts to classify a visible solution project declaration as a supported .NET project language.
        /// </summary>
        /// <param name="declaration">The solution project declaration to classify.</param>
        /// <param name="language">The supported project language when classification succeeds.</param>
        /// <returns><see langword="true" /> when the declaration represents a supported C# or VB.NET project; otherwise, <see langword="false" />.</returns>
        internal static bool TryClassify(SolutionProjectDeclaration declaration, out ProjectLanguage language)
        {
            // Extension matching handles SDK-style solution declarations that use flavor GUIDs, while GUID matching supports legacy declarations.
            ArgumentNullException.ThrowIfNull(declaration);
            string extension = Path.GetExtension(declaration.DeclaredPath);
            if (string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase) || IsProjectTypeGuid(declaration, CSharpProjectTypeGuid))
            {
                language = ProjectLanguage.CSharp;
                return true;
            }

            if (string.Equals(extension, ".vbproj", StringComparison.OrdinalIgnoreCase) || IsProjectTypeGuid(declaration, VisualBasicProjectTypeGuid))
            {
                language = ProjectLanguage.VisualBasic;
                return true;
            }

            language = ProjectLanguage.CSharp;
            return false;
        }

        /// <summary>
        /// Determines whether a declaration uses a known solution project type GUID.
        /// </summary>
        /// <param name="declaration">The solution project declaration being classified.</param>
        /// <param name="projectTypeGuid">The lowercase canonical project type GUID to compare.</param>
        /// <returns><see langword="true" /> when the declaration has the supplied project type GUID; otherwise, <see langword="false" />.</returns>
        private static bool IsProjectTypeGuid(SolutionProjectDeclaration declaration, string projectTypeGuid)
        {
            // Braces are retained because Visual Studio solution files normally include them in project type GUID text.
            return string.Equals(declaration.ProjectTypeGuid?.Trim().ToLowerInvariant(), projectTypeGuid, StringComparison.Ordinal);
        }
    }
}
