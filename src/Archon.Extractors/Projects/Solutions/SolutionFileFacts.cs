namespace Archon.Extractors.Projects.Solutions
{
    /// <summary>
    /// Represents the solution-file evidence facts parsed from one explicitly submitted solution file.
    /// </summary>
    /// <param name="ProjectDeclarations">The visible project declarations discovered in the submitted solution file.</param>
    /// <param name="LineCount">The total number of lines read from the solution file.</param>
    internal sealed record SolutionFileFacts(IReadOnlyList<SolutionProjectDeclaration> ProjectDeclarations, int LineCount)
    {
        /// <summary>
        /// Gets a value indicating whether the submitted file looked like a Visual Studio solution file.
        /// </summary>
        internal bool HasRecognizedHeader
        {
            get
            {
                // A recognized solution must have at least one line because the parser rejects blank files before constructing facts.
                return LineCount > 0;
            }
        }
    }
}
