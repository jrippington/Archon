using System.Text.Json.Serialization;

namespace Archon.Domain.Graph.ControlledValues
{
    /// <summary>
    /// Identifies the category of an architecture graph relationship between two architecture nodes.
    /// </summary>
    [JsonConverter(typeof(ControlledValueJsonConverterFactory))]
    public sealed class EdgeKind : ControlledValue<EdgeKind>
    {
        /// <summary>Represents a containment relationship.</summary>
        public static readonly EdgeKind Contains = new("CONTAINS");
        /// <summary>Represents a project, assembly, package, or symbol reference relationship.</summary>
        public static readonly EdgeKind References = new("REFERENCES");
        /// <summary>Represents a method or runtime invocation relationship.</summary>
        public static readonly EdgeKind Calls = new("CALLS");
        /// <summary>Represents an interface implementation relationship.</summary>
        public static readonly EdgeKind Implements = new("IMPLEMENTS");
        /// <summary>Represents an inheritance relationship.</summary>
        public static readonly EdgeKind Inherits = new("INHERITS");
        /// <summary>Represents a dependency-injection relationship.</summary>
        public static readonly EdgeKind Injects = new("INJECTS");
        /// <summary>Represents an exposed runtime surface relationship.</summary>
        public static readonly EdgeKind Exposes = new("EXPOSES");
        /// <summary>Represents an event, message, request, or route handling relationship.</summary>
        public static readonly EdgeKind Handles = new("HANDLES");
        /// <summary>Represents use of a configuration key.</summary>
        public static readonly EdgeKind UsesConfig = new("USES_CONFIG");
        /// <summary>Represents use of an Entity Framework DbContext.</summary>
        public static readonly EdgeKind UsesDbContext = new("USES_DB_CONTEXT");
        /// <summary>Represents use of a LINQ to SQL data context.</summary>
        public static readonly EdgeKind UsesLinqToSqlContext = new("USES_LINQ_TO_SQL_CONTEXT");
        /// <summary>Represents a mapping to a data entity.</summary>
        public static readonly EdgeKind MapsEntity = new("MAPS_ENTITY");
        /// <summary>Represents a mapping to a database table.</summary>
        public static readonly EdgeKind MapsTable = new("MAPS_TABLE");
        /// <summary>Represents a mapping to a database column.</summary>
        public static readonly EdgeKind MapsColumn = new("MAPS_COLUMN");
        /// <summary>Represents a read dependency on a database table.</summary>
        public static readonly EdgeKind ReadsTable = new("READS_TABLE");
        /// <summary>Represents a write dependency on a database table.</summary>
        public static readonly EdgeKind WritesTable = new("WRITES_TABLE");
        /// <summary>Represents a stored procedure call relationship.</summary>
        public static readonly EdgeKind CallsStoredProcedure = new("CALLS_STORED_PROCEDURE");
        /// <summary>Represents raw SQL execution.</summary>
        public static readonly EdgeKind ExecutesRawSql = new("EXECUTES_RAW_SQL");
        /// <summary>Represents a call to an external service.</summary>
        public static readonly EdgeKind CallsExternalService = new("CALLS_EXTERNAL_SERVICE");
        /// <summary>Represents use of a package dependency.</summary>
        public static readonly EdgeKind UsesPackage = new("USES_PACKAGE");
        /// <summary>Represents declaration of an endpoint.</summary>
        public static readonly EdgeKind DeclaresEndpoint = new("DECLARES_ENDPOINT");
        /// <summary>Represents declaration of a UI component.</summary>
        public static readonly EdgeKind DeclaresComponent = new("DECLARES_COMPONENT");
        /// <summary>Represents declaration of a UI route.</summary>
        public static readonly EdgeKind DeclaresUiRoute = new("DECLARES_UI_ROUTE");
        /// <summary>Represents use of a UI component.</summary>
        public static readonly EdgeKind UsesComponent = new("USES_COMPONENT");
        /// <summary>Represents use of a UI layout.</summary>
        public static readonly EdgeKind UsesLayout = new("USES_LAYOUT");
        /// <summary>Represents use of a UI control.</summary>
        public static readonly EdgeKind UsesControl = new("USES_CONTROL");
        /// <summary>Represents use of a UI resource.</summary>
        public static readonly EdgeKind UsesUiResource = new("USES_UI_RESOURCE");
        /// <summary>Represents use of a UI style.</summary>
        public static readonly EdgeKind UsesStyle = new("USES_STYLE");
        /// <summary>Represents a data-binding relationship.</summary>
        public static readonly EdgeKind BindsTo = new("BINDS_TO");
        /// <summary>Represents use of a command.</summary>
        public static readonly EdgeKind UsesCommand = new("USES_COMMAND");
        /// <summary>Represents use of a view model.</summary>
        public static readonly EdgeKind UsesViewModel = new("USES_VIEW_MODEL");
        /// <summary>Represents navigation to a UI route, page, view, or endpoint.</summary>
        public static readonly EdgeKind NavigatesTo = new("NAVIGATES_TO");
        /// <summary>Represents handling of a UI event.</summary>
        public static readonly EdgeKind HandlesUiEvent = new("HANDLES_UI_EVENT");
        /// <summary>Represents a UI or service call to an API endpoint.</summary>
        public static readonly EdgeKind CallsApi = new("CALLS_API");
        /// <summary>Represents a service registration relationship.</summary>
        public static readonly EdgeKind RegisteredAsService = new("REGISTERED_AS_SERVICE");
        /// <summary>Represents a dependency relationship not covered by a more specific edge kind.</summary>
        public static readonly EdgeKind DependsOn = new("DEPENDS_ON");

        /// <summary>
        /// Initializes a new instance of the <see cref="EdgeKind"/> class.
        /// </summary>
        /// <param name="value">The stable external string for the edge kind.</param>
        private EdgeKind(string value)
            : base(value)
        {
            // Construction registers the edge kind with the shared controlled-value lookup table.
        }
    }
}
