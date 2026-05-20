using System.Text.Json.Serialization;

namespace Archon.Domain.Graph.ControlledValues
{
    /// <summary>
    /// Identifies the source category for evidence that supports an architecture fact, finding, or metric.
    /// </summary>
    [JsonConverter(typeof(ControlledValueJsonConverterFactory))]
    public sealed class EvidenceKind : ControlledValue<EvidenceKind>
    {
        /// <summary>Represents evidence from a project file.</summary>
        public static readonly EvidenceKind ProjectFile = new("ProjectFile");
        /// <summary>Represents evidence from source code.</summary>
        public static readonly EvidenceKind SourceCode = new("SourceCode");
        /// <summary>Represents evidence from configuration content.</summary>
        public static readonly EvidenceKind Configuration = new("Configuration");
        /// <summary>Represents evidence from a DBML file.</summary>
        public static readonly EvidenceKind Dbml = new("Dbml");
        /// <summary>Represents evidence from designer-generated code.</summary>
        public static readonly EvidenceKind DesignerGeneratedCode = new("DesignerGeneratedCode");
        /// <summary>Represents evidence from a SQL script.</summary>
        public static readonly EvidenceKind SqlScript = new("SqlScript");
        /// <summary>Represents evidence from a pipeline file.</summary>
        public static readonly EvidenceKind PipelineFile = new("PipelineFile");
        /// <summary>Represents evidence from an OpenAPI document.</summary>
        public static readonly EvidenceKind OpenApiDocument = new("OpenApiDocument");
        /// <summary>Represents evidence from a Dockerfile.</summary>
        public static readonly EvidenceKind Dockerfile = new("Dockerfile");
        /// <summary>Represents evidence from a generated artifact.</summary>
        public static readonly EvidenceKind GeneratedArtifact = new("GeneratedArtifact");
        /// <summary>Represents evidence from a package reference.</summary>
        public static readonly EvidenceKind PackageReference = new("PackageReference");
        /// <summary>Represents evidence from a compiler symbol.</summary>
        public static readonly EvidenceKind CompilerSymbol = new("CompilerSymbol");
        /// <summary>Represents evidence from a compiler diagnostic.</summary>
        public static readonly EvidenceKind CompilerDiagnostic = new("CompilerDiagnostic");
        /// <summary>Represents evidence derived by deterministic inference from other facts.</summary>
        public static readonly EvidenceKind Inference = new("Inference");
        /// <summary>Represents evidence supplied through manual annotation.</summary>
        public static readonly EvidenceKind ManualAnnotation = new("ManualAnnotation");

        /// <summary>
        /// Initializes a new instance of the <see cref="EvidenceKind"/> class.
        /// </summary>
        /// <param name="value">The stable external string for the evidence kind.</param>
        private EvidenceKind(string value)
            : base(value)
        {
            // Construction registers the evidence kind with the shared controlled-value lookup table.
        }
    }
}
