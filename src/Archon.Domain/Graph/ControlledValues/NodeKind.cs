using System.Text.Json.Serialization;

namespace Archon.Domain.Graph.ControlledValues
{
    /// <summary>
    /// Identifies the category of an architecture graph node produced by extraction and later persisted or exposed externally.
    /// </summary>
    [JsonConverter(typeof(ControlledValueJsonConverterFactory))]
    public sealed class NodeKind : ControlledValue<NodeKind>
    {
        /// <summary>Represents a source repository node.</summary>
        public static readonly NodeKind Repository = new("Repository");
        /// <summary>Represents a solution file node.</summary>
        public static readonly NodeKind Solution = new("Solution");
        /// <summary>Represents a project file node.</summary>
        public static readonly NodeKind Project = new("Project");
        /// <summary>Represents a package dependency node.</summary>
        public static readonly NodeKind Package = new("Package");
        /// <summary>Represents a namespace symbol node.</summary>
        public static readonly NodeKind Namespace = new("Namespace");
        /// <summary>Represents a type symbol node.</summary>
        public static readonly NodeKind Type = new("Type");
        /// <summary>Represents a method symbol node.</summary>
        public static readonly NodeKind Method = new("Method");
        /// <summary>Represents a property symbol node.</summary>
        public static readonly NodeKind Property = new("Property");
        /// <summary>Represents a field symbol node.</summary>
        public static readonly NodeKind Field = new("Field");
        /// <summary>Represents an HTTP or service endpoint node.</summary>
        public static readonly NodeKind Endpoint = new("Endpoint");
        /// <summary>Represents a controller node.</summary>
        public static readonly NodeKind Controller = new("Controller");
        /// <summary>Represents a hosted service node.</summary>
        public static readonly NodeKind HostedService = new("HostedService");
        /// <summary>Represents a UI application node.</summary>
        public static readonly NodeKind UiApplication = new("UiApplication");
        /// <summary>Represents a UI component node.</summary>
        public static readonly NodeKind UiComponent = new("UiComponent");
        /// <summary>Represents a UI page node.</summary>
        public static readonly NodeKind UiPage = new("UiPage");
        /// <summary>Represents a UI view node.</summary>
        public static readonly NodeKind UiView = new("UiView");
        /// <summary>Represents a UI layout node.</summary>
        public static readonly NodeKind UiLayout = new("UiLayout");
        /// <summary>Represents a UI route node.</summary>
        public static readonly NodeKind UiRoute = new("UiRoute");
        /// <summary>Represents a UI control node.</summary>
        public static readonly NodeKind UiControl = new("UiControl");
        /// <summary>Represents a UI resource node.</summary>
        public static readonly NodeKind UiResource = new("UiResource");
        /// <summary>Represents a UI style node.</summary>
        public static readonly NodeKind UiStyle = new("UiStyle");
        /// <summary>Represents a view-model node.</summary>
        public static readonly NodeKind ViewModel = new("ViewModel");
        /// <summary>Represents a command node.</summary>
        public static readonly NodeKind Command = new("Command");
        /// <summary>Represents a data-binding node.</summary>
        public static readonly NodeKind Binding = new("Binding");
        /// <summary>Represents a configuration key node.</summary>
        public static readonly NodeKind ConfigurationKey = new("ConfigurationKey");
        /// <summary>Represents an Entity Framework DbContext node.</summary>
        public static readonly NodeKind DbContext = new("DbContext");
        /// <summary>Represents a LINQ to SQL data context node.</summary>
        public static readonly NodeKind LinqToSqlDataContext = new("LinqToSqlDataContext");
        /// <summary>Represents a data entity node.</summary>
        public static readonly NodeKind Entity = new("Entity");
        /// <summary>Represents a database table node.</summary>
        public static readonly NodeKind DatabaseTable = new("DatabaseTable");
        /// <summary>Represents a database column node.</summary>
        public static readonly NodeKind DatabaseColumn = new("DatabaseColumn");
        /// <summary>Represents a stored procedure node.</summary>
        public static readonly NodeKind StoredProcedure = new("StoredProcedure");
        /// <summary>Represents an external service node.</summary>
        public static readonly NodeKind ExternalService = new("ExternalService");
        /// <summary>Represents a queue node.</summary>
        public static readonly NodeKind Queue = new("Queue");
        /// <summary>Represents a topic node.</summary>
        public static readonly NodeKind Topic = new("Topic");
        /// <summary>Represents a file path node.</summary>
        public static readonly NodeKind FilePath = new("FilePath");
        /// <summary>Represents a pipeline node.</summary>
        public static readonly NodeKind Pipeline = new("Pipeline");
        /// <summary>Represents an OpenAPI document node.</summary>
        public static readonly NodeKind OpenApiDocument = new("OpenApiDocument");
        /// <summary>Represents a Dockerfile node.</summary>
        public static readonly NodeKind Dockerfile = new("Dockerfile");
        /// <summary>Represents a SQL script node.</summary>
        public static readonly NodeKind SqlScript = new("SqlScript");
        /// <summary>Represents a generated artifact node.</summary>
        public static readonly NodeKind GeneratedArtifact = new("GeneratedArtifact");

        /// <summary>
        /// Initializes a new instance of the <see cref="NodeKind"/> class.
        /// </summary>
        /// <param name="value">The stable external string for the node kind.</param>
        private NodeKind(string value)
            : base(value)
        {
            // Construction registers the node kind with the shared controlled-value lookup table.
        }
    }
}
