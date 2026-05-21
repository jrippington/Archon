namespace Archon.Extractors.Projects.Projects
{
    /// <summary>
    /// Identifies the source language supported by the WP005 project metadata extractor.
    /// </summary>
    internal enum ProjectLanguage
    {
        /// <summary>
        /// Represents a C# project file declared with a `.csproj` extension or C# solution project type GUID.
        /// </summary>
        CSharp,

        /// <summary>
        /// Represents a Visual Basic .NET project file declared with a `.vbproj` extension or Visual Basic solution project type GUID.
        /// </summary>
        VisualBasic
    }
}
