using Microsoft.CodeAnalysis;

namespace Archon.Roslyn.SemanticModel
{
    /// <summary>
    /// Classifies Roslyn symbols for graph-ready confidence and metadata decisions.
    /// </summary>
    /// <remarks>
    /// The classifier centralizes source, metadata, generated, unresolved, and unsupported symbol checks so C# and Visual Basic extractors make consistent degraded-extraction decisions.
    /// </remarks>
    public static class SemanticSymbolClassifier
    {
        /// <summary>
        /// Determines whether a symbol is declared only in referenced metadata rather than source in the analyzed compilation.
        /// </summary>
        /// <param name="symbol">The symbol to classify.</param>
        /// <returns><see langword="true" /> when the symbol has metadata locations and no source locations.</returns>
        public static bool IsMetadataOnly(ISymbol symbol)
        {
            // Metadata-only symbols should become symbol-reference endpoints, not invented repository source declaration nodes.
            ArgumentNullException.ThrowIfNull(symbol);
            return !symbol.Locations.Any(location => location.IsInSource) && symbol.Locations.Any(location => location.IsInMetadata);
        }

        /// <summary>
        /// Determines whether a symbol represents an unresolved compiler error placeholder.
        /// </summary>
        /// <param name="symbol">The symbol to classify.</param>
        /// <returns><see langword="true" /> when the symbol is an error type or contains unresolved type arguments.</returns>
        public static bool IsUnresolved(ISymbol symbol)
        {
            // Error symbols are Roslyn's explicit representation of missing or unresolved semantic targets.
            ArgumentNullException.ThrowIfNull(symbol);
            return symbol is IErrorTypeSymbol
                || symbol is INamedTypeSymbol namedType && ContainsErrorType(namedType);
        }

        /// <summary>
        /// Determines whether a symbol should be skipped as a resolved dependency endpoint.
        /// </summary>
        /// <param name="symbol">The symbol being considered as a dependency endpoint.</param>
        /// <returns><see langword="true" /> when the symbol is void, unresolved, or only a type parameter placeholder.</returns>
        public static bool IsUnsupportedDependencyTarget(ISymbol symbol)
        {
            // Void, type parameters, and error symbols do not represent concrete dependency endpoints and should become unknowns when relevant.
            ArgumentNullException.ThrowIfNull(symbol);
            return symbol switch
            {
                ITypeSymbol { SpecialType: SpecialType.System_Void } => true,
                ITypeParameterSymbol => true,
                _ when IsUnresolved(symbol) => true,
                _ => false
            };
        }

        /// <summary>
        /// Adds common symbol classification fields to a metadata dictionary.
        /// </summary>
        /// <param name="metadata">The metadata dictionary to enrich.</param>
        /// <param name="symbol">The symbol whose classification should be added.</param>
        /// <param name="generated">A value indicating whether the source evidence came from generated code.</param>
        public static void AddSymbolMetadata(IDictionary<string, string> metadata, ISymbol symbol, bool generated)
        {
            // Classifier metadata lets graph consumers distinguish repository source endpoints from external metadata endpoints without inspecting Roslyn objects.
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(symbol);
            metadata["targetKind"] = symbol.Kind.ToString();
            metadata["targetOrigin"] = IsMetadataOnly(symbol) ? "Metadata" : "Source";
            metadata["generated"] = generated ? "true" : "false";
        }

        /// <summary>
        /// Selects the confidence value for a resolved relationship target.
        /// </summary>
        /// <param name="symbol">The resolved relationship target symbol.</param>
        /// <param name="generated">A value indicating whether the source evidence came from generated code.</param>
        /// <returns>The confidence category that best describes the relationship.</returns>
        public static SemanticFactConfidence ClassifyResolvedConfidence(ISymbol symbol, bool generated)
        {
            // Generated facts remain resolved but receive a distinct confidence category required by Work Item 4.
            ArgumentNullException.ThrowIfNull(symbol);
            if (generated)
            {
                return SemanticFactConfidence.Generated;
            }

            return IsMetadataOnly(symbol) ? SemanticFactConfidence.MetadataOnly : SemanticFactConfidence.CompilerResolved;
        }

        /// <summary>
        /// Determines whether a named type contains any unresolved type argument.
        /// </summary>
        /// <param name="namedType">The named type to inspect.</param>
        /// <returns><see langword="true" /> when any nested type argument is an error type.</returns>
        private static bool ContainsErrorType(INamedTypeSymbol namedType)
        {
            // Generic error types can be nested inside otherwise named symbols, so recursive inspection avoids treating them as resolved metadata.
            foreach (ITypeSymbol argument in namedType.TypeArguments)
            {
                if (argument is IErrorTypeSymbol)
                {
                    return true;
                }

                if (argument is INamedTypeSymbol nestedType && ContainsErrorType(nestedType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
