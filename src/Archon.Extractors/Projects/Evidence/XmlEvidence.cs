using System.Xml;
using System.Xml.Linq;

namespace Archon.Extractors.Projects.Evidence
{
    /// <summary>
    /// Provides reusable XML evidence helpers for line spans and bounded snippet capture.
    /// </summary>
    internal static class XmlEvidence
    {
        /// <summary>
        /// Gets the line number exposed by an XML element when line information is available.
        /// </summary>
        /// <param name="element">The XML element whose source line should be read.</param>
        /// <returns>The element line number, or <see langword="null" /> when the parser did not expose line information.</returns>
        internal static int? GetLineNumber(XElement element)
        {
            // XDocument parsing with SetLineInfo gives deterministic source line numbers for XML-backed evidence when available.
            return element is IXmlLineInfo lineInfo && lineInfo.HasLineInfo() ? lineInfo.LineNumber : null;
        }

        /// <summary>
        /// Creates a bounded evidence snippet from an XML element.
        /// </summary>
        /// <param name="element">The XML element that supports the graph fact.</param>
        /// <returns>A deterministic snippet hash and preview for the element.</returns>
        internal static SourceSnippet CreateSnippet(XElement element)
        {
            // DisableFormatting keeps the snippet focused on XML tokens rather than source indentation.
            return SourceSnippet.FromText(element.ToString(SaveOptions.DisableFormatting));
        }
    }
}
