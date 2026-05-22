using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.LegacyWeb;
using Xunit;

namespace Archon.Extractors.LegacyWeb.Tests
{
    /// <summary>
    /// Verifies the classic ASP.NET runtime extractor contributes graph-ready facts for legacy System.Web applications.
    /// </summary>
    public sealed class ClassicAspNetRuntimeExtractorTests
    {
        /// <summary>
        /// Verifies project, configuration, Global.asax, and lifecycle source artifacts contribute classic application metadata and evidence.
        /// </summary>
        [Fact]
        public void Extract_WhenClassicApplicationArtifactsExist_ShouldContributeApplicationMetadataAndLifecycleFacts()
        {
            // The fixture mirrors a legacy web application without compiling or executing it so extraction remains static and deterministic.
            string repositoryRoot = CreateRepositoryRoot();
            try
            {
                WriteClassicApplicationFixture(repositoryRoot);
                ClassicAspNetRuntimeExtractor extractor = new();

                ClassicAspNetRuntimeExtractionResult result = extractor.Extract(new ClassicAspNetRuntimeExtractionRequest(new StableKey("snapshot://legacy-web-test"), repositoryRoot, "src/Legacy.Web/Legacy.Web.csproj"), CancellationToken.None);

                ArchitectureNode projectNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
                Assert.Equal("Legacy.Web", projectNode.DisplayName);
                string projectMetadata = projectNode.Metadata.ToCanonicalJson();
                Assert.Contains("\"runtimeKind\":\"ClassicAspNetApplication\"", projectMetadata, StringComparison.Ordinal);
                Assert.Contains("\"framework\":\"Classic ASP.NET\"", projectMetadata, StringComparison.Ordinal);
                Assert.Contains("\"systemWebReferenceDetected\":true", projectMetadata, StringComparison.Ordinal);
                Assert.Contains("\"globalAsaxPath\":\"src/Legacy.Web/Global.asax\"", projectMetadata, StringComparison.Ordinal);
                Assert.Contains("\"webConfigPath\":\"src/Legacy.Web/Web.config\"", projectMetadata, StringComparison.Ordinal);

                ArchitectureNode lifecycleMethod = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Method && node.DisplayName == "Application_Start");
                Assert.Contains("\"lifecycleHook\":\"Application_Start\"", lifecycleMethod.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.DependsOn && edge.SourceNodeStableKey == projectNode.StableKey && edge.TargetNodeStableKey == lifecycleMethod.StableKey);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Legacy.Web/Global.asax" && evidence.EvidenceKind == EvidenceKind.SourceCode);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Legacy.Web/Web.config" && evidence.EvidenceKind == EvidenceKind.Configuration);
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The test owns the temporary repository and removes it to keep repeated runs isolated.
                DeleteRepositoryRoot(repositoryRoot);
            }
        }

        /// <summary>
        /// Verifies Web Forms pages, user controls, HTTP handlers, and HTTP modules are detected from markup, source, and configuration evidence.
        /// </summary>
        [Fact]
        public void Extract_WhenWebFormsHandlersAndModulesExist_ShouldContributeRuntimeFacts()
        {
            // This fixture combines markup and configuration declarations because legacy applications often split runtime facts across both artifact types.
            string repositoryRoot = CreateRepositoryRoot();
            try
            {
                WriteClassicApplicationFixture(repositoryRoot);
                ClassicAspNetRuntimeExtractor extractor = new();

                ClassicAspNetRuntimeExtractionResult result = extractor.Extract(new ClassicAspNetRuntimeExtractionRequest(new StableKey("snapshot://legacy-web-test"), repositoryRoot, "src/Legacy.Web/Legacy.Web.csproj"), CancellationToken.None);

                ArchitectureNode pageEndpoint = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "WEBFORMS /Pages/Orders.aspx");
                Assert.Contains("\"runtimeKind\":\"WebFormsPage\"", pageEndpoint.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"handlerType\":\"Legacy.Web.Pages.Orders\"", pageEndpoint.Metadata.ToCanonicalJson(), StringComparison.Ordinal);

                ArchitectureNode userControl = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.FilePath && node.DisplayName == "/Controls/OrderSummary.ascx");
                Assert.Contains("\"runtimeKind\":\"WebFormsUserControl\"", userControl.Metadata.ToCanonicalJson(), StringComparison.Ordinal);

                ArchitectureNode handlerType = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Type && node.DisplayName == "Legacy.Web.LegacyOrderHandler");
                Assert.Contains("\"runtimeKind\":\"HttpHandler\"", handlerType.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                ArchitectureNode handlerEndpoint = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "HANDLER /orders.axd");
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.Handles && edge.SourceNodeStableKey == handlerType.StableKey && edge.TargetNodeStableKey == handlerEndpoint.StableKey);

                ArchitectureNode moduleType = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Type && node.DisplayName == "Legacy.Web.SecurityModule");
                Assert.Contains("\"runtimeKind\":\"HttpModule\"", moduleType.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Legacy.Web/Pages/Orders.aspx" && evidence.EvidenceKind == EvidenceKind.SourceCode);
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The test owns the temporary repository and removes it to keep repeated runs isolated.
                DeleteRepositoryRoot(repositoryRoot);
            }
        }

        /// <summary>
        /// Verifies MVC 5 and Web API 2 controllers, route attributes, route tables, and unknown convention routes are represented deterministically.
        /// </summary>
        [Fact]
        public void Extract_WhenMvcAndWebApiArtifactsExist_ShouldContributeControllersEndpointsAndRouteUnknowns()
        {
            // MVC and Web API fixtures include deterministic attribute routes plus a conventional route table that must remain explicit unknown data.
            string repositoryRoot = CreateRepositoryRoot();
            try
            {
                WriteClassicApplicationFixture(repositoryRoot);
                ClassicAspNetRuntimeExtractor extractor = new();

                ClassicAspNetRuntimeExtractionResult result = extractor.Extract(new ClassicAspNetRuntimeExtractionRequest(new StableKey("snapshot://legacy-web-test"), repositoryRoot, "src/Legacy.Web/Legacy.Web.csproj"), CancellationToken.None);

                ArchitectureNode mvcController = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Controller && node.DisplayName == "OrdersController");
                Assert.Contains("\"framework\":\"ASP.NET MVC 5\"", mvcController.Metadata.ToCanonicalJson(), StringComparison.Ordinal);

                ArchitectureNode mvcEndpoint = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "GET /orders/{id}");
                Assert.Contains("\"runtimeKind\":\"Mvc5Action\"", mvcEndpoint.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.DeclaresEndpoint && edge.SourceNodeStableKey == mvcController.StableKey && edge.TargetNodeStableKey == mvcEndpoint.StableKey);

                ArchitectureNode apiController = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Controller && node.DisplayName == "CustomersController");
                Assert.Contains("\"framework\":\"ASP.NET Web API 2\"", apiController.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "GET /api/customers/{id}");

                ArchitectureNode unknownRoute = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.UnknownState.HasUnknownData);
                Assert.Equal("Classic ASP.NET conventional route contains controller/action tokens and cannot be resolved to one deterministic endpoint.", unknownRoute.UnknownState.UnknownReason);
                Assert.Contains("\"routeTemplate\":\"{controller}/{action}/{id}\"", unknownRoute.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The test owns the temporary repository and removes it to keep repeated runs isolated.
                DeleteRepositoryRoot(repositoryRoot);
            }
        }

        /// <summary>
        /// Creates a unique temporary repository root for one extraction fixture.
        /// </summary>
        /// <returns>The absolute repository root directory.</returns>
        private static string CreateRepositoryRoot()
        {
            // A unique root prevents file collisions and keeps repository-relative stable keys deterministic inside each test.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-wp008-legacy-web-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repositoryRoot);
            return repositoryRoot;
        }

        /// <summary>
        /// Deletes a temporary repository root when it still exists.
        /// </summary>
        /// <param name="repositoryRoot">The absolute repository root to delete.</param>
        private static void DeleteRepositoryRoot(string repositoryRoot)
        {
            // Tests clean up their own repositories so a failed assertion does not affect later fixture creation.
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Writes a compact classic ASP.NET fixture that includes project, configuration, markup, source, MVC, Web API, and route artifacts.
        /// </summary>
        /// <param name="repositoryRoot">The temporary repository root that receives the fixture.</param>
        private static void WriteClassicApplicationFixture(string repositoryRoot)
        {
            // The fixture intentionally uses old-style project, System.Web, markup, and route files to exercise the Work Item 3 legacy slice.
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Legacy.Web");
            Directory.CreateDirectory(Path.Combine(projectDirectory, "App_Start"));
            Directory.CreateDirectory(Path.Combine(projectDirectory, "Controllers"));
            Directory.CreateDirectory(Path.Combine(projectDirectory, "Api"));
            Directory.CreateDirectory(Path.Combine(projectDirectory, "Pages"));
            Directory.CreateDirectory(Path.Combine(projectDirectory, "Controls"));
            File.WriteAllText(Path.Combine(projectDirectory, "Legacy.Web.csproj"), CreateProjectSource());
            File.WriteAllText(Path.Combine(projectDirectory, "Web.config"), CreateWebConfigSource());
            File.WriteAllText(Path.Combine(projectDirectory, "Global.asax"), "<%@ Application Codebehind=\"Global.asax.cs\" Inherits=\"Legacy.Web.MvcApplication\" Language=\"C#\" %>");
            File.WriteAllText(Path.Combine(projectDirectory, "Global.asax.cs"), CreateGlobalAsaxSource());
            File.WriteAllText(Path.Combine(projectDirectory, "Pages", "Orders.aspx"), "<%@ Page Language=\"C#\" CodeBehind=\"Orders.aspx.cs\" Inherits=\"Legacy.Web.Pages.Orders\" %>");
            File.WriteAllText(Path.Combine(projectDirectory, "Pages", "Orders.aspx.cs"), "namespace Legacy.Web.Pages { public partial class Orders : System.Web.UI.Page { } }");
            File.WriteAllText(Path.Combine(projectDirectory, "Controls", "OrderSummary.ascx"), "<%@ Control Language=\"C#\" CodeBehind=\"OrderSummary.ascx.cs\" Inherits=\"Legacy.Web.Controls.OrderSummary\" %>");
            File.WriteAllText(Path.Combine(projectDirectory, "LegacyOrderHandler.cs"), "namespace Legacy.Web { public sealed class LegacyOrderHandler : System.Web.IHttpHandler { public bool IsReusable => false; public void ProcessRequest(System.Web.HttpContext context) { } } }");
            File.WriteAllText(Path.Combine(projectDirectory, "SecurityModule.cs"), "namespace Legacy.Web { public sealed class SecurityModule : System.Web.IHttpModule { public void Init(System.Web.HttpApplication application) { } public void Dispose() { } } }");
            File.WriteAllText(Path.Combine(projectDirectory, "Controllers", "OrdersController.cs"), CreateMvcControllerSource());
            File.WriteAllText(Path.Combine(projectDirectory, "Api", "CustomersController.cs"), CreateWebApiControllerSource());
            File.WriteAllText(Path.Combine(projectDirectory, "App_Start", "RouteConfig.cs"), CreateRouteConfigSource());
        }

        /// <summary>
        /// Creates the legacy project XML fixture with System.Web and MVC/Web API package evidence.
        /// </summary>
        /// <returns>The project XML source.</returns>
        private static string CreateProjectSource()
        {
            // The old-style XML shape gives the extractor project metadata and assembly-reference evidence without requiring MSBuild evaluation.
            return string.Join(
                Environment.NewLine,
                [
                    "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">",
                    "  <ItemGroup>",
                    "    <Reference Include=\"System.Web\" />",
                    "    <Reference Include=\"System.Web.Mvc\" />",
                    "    <Reference Include=\"System.Web.Http\" />",
                    "    <Compile Include=\"Global.asax.cs\" />",
                    "  </ItemGroup>",
                    "</Project>"
                ]);
        }

        /// <summary>
        /// Creates the Web.config fixture with handler and module declarations.
        /// </summary>
        /// <returns>The Web.config XML source.</returns>
        private static string CreateWebConfigSource()
        {
            // Handler and module configuration remains static XML evidence and is never executed during extraction.
            return string.Join(
                Environment.NewLine,
                [
                    "<configuration>",
                    "  <system.webServer>",
                    "    <handlers>",
                    "      <add name=\"LegacyOrderHandler\" path=\"orders.axd\" verb=\"GET\" type=\"Legacy.Web.LegacyOrderHandler\" />",
                    "    </handlers>",
                    "    <modules>",
                    "      <add name=\"SecurityModule\" type=\"Legacy.Web.SecurityModule\" />",
                    "    </modules>",
                    "  </system.webServer>",
                    "</configuration>"
                ]);
        }

        /// <summary>
        /// Creates the Global.asax code-behind fixture with lifecycle and route-registration evidence.
        /// </summary>
        /// <returns>The C# source for Global.asax.cs.</returns>
        private static string CreateGlobalAsaxSource()
        {
            // The lifecycle method is narrow but representative of classic ASP.NET application startup extraction.
            return string.Join(
                Environment.NewLine,
                [
                    "namespace Legacy.Web",
                    "{",
                    "    public class MvcApplication : System.Web.HttpApplication",
                    "    {",
                    "        protected void Application_Start()",
                    "        {",
                    "            RouteConfig.RegisterRoutes(System.Web.Routing.RouteTable.Routes);",
                    "        }",
                    "    }",
                    "}"
                ]);
        }

        /// <summary>
        /// Creates the MVC 5 controller fixture with attribute routing evidence.
        /// </summary>
        /// <returns>The C# source for the MVC controller.</returns>
        private static string CreateMvcControllerSource()
        {
            // Attribute declarations are local stubs so syntax-based extraction can run without System.Web assemblies.
            return string.Join(
                Environment.NewLine,
                [
                    "namespace System.Web.Mvc { public abstract class Controller { } public sealed class RouteAttribute(string template) : System.Attribute { } public sealed class HttpGetAttribute : System.Attribute { } }",
                    "namespace Legacy.Web.Controllers",
                    "{",
                    "    using System.Web.Mvc;",
                    "    public sealed class OrdersController : Controller",
                    "    {",
                    "        [HttpGet]",
                    "        [Route(\"orders/{id}\")]",
                    "        public string Details(int id) => id.ToString();",
                    "    }",
                    "}"
                ]);
        }

        /// <summary>
        /// Creates the Web API 2 controller fixture with attribute routing evidence.
        /// </summary>
        /// <returns>The C# source for the Web API controller.</returns>
        private static string CreateWebApiControllerSource()
        {
            // Attribute declarations are local stubs so syntax-based extraction can run without System.Web.Http assemblies.
            return string.Join(
                Environment.NewLine,
                [
                    "namespace System.Web.Http { public abstract class ApiController { } public sealed class RouteAttribute(string template) : System.Attribute { } public sealed class HttpGetAttribute : System.Attribute { } }",
                    "namespace Legacy.Web.Api",
                    "{",
                    "    using System.Web.Http;",
                    "    public sealed class CustomersController : ApiController",
                    "    {",
                    "        [HttpGet]",
                    "        [Route(\"api/customers/{id}\")]",
                    "        public string Get(int id) => id.ToString();",
                    "    }",
                    "}"
                ]);
        }

        /// <summary>
        /// Creates route-table configuration with a conventional route that must remain unknown.
        /// </summary>
        /// <returns>The C# source for route configuration.</returns>
        private static string CreateRouteConfigSource()
        {
            // The conventional route pattern has controller and action tokens, so the extractor should not invent concrete endpoint URLs.
            return string.Join(
                Environment.NewLine,
                [
                    "namespace Legacy.Web",
                    "{",
                    "    public static class RouteConfig",
                    "    {",
                    "        public static void RegisterRoutes(System.Web.Routing.RouteCollection routes)",
                    "        {",
                    "            routes.MapRoute(name: \"Default\", url: \"{controller}/{action}/{id}\");",
                    "        }",
                    "    }",
                    "}"
                ]);
        }
    }
}
