using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.AspNet.MinimalApis;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Archon.Extractors.AspNet.Tests
{
    /// <summary>
    /// Verifies the ASP.NET Core minimal API endpoint extractor contributes graph-ready endpoint facts for direct route mappings.
    /// </summary>
    public sealed class AspNetCoreMinimalApiEndpointExtractorTests
    {
        /// <summary>
        /// Verifies a direct <c>MapGet</c> call in <c>Program.cs</c> produces an endpoint node, project declaration edge, and source evidence.
        /// </summary>
        [Fact]
        public void Extract_WhenProgramContainsDirectMapGet_ShouldContributeEndpointNodeEdgeAndEvidence()
        {
            // The fixture uses a syntax-only minimal API shape so the extractor proves static endpoint recognition without running the target app.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-wp008-aspnet-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.Api"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "Customer.Api", "Program.cs");
                File.WriteAllText(documentPath, CreateProgramSource());
                StableKey snapshotStableKey = new("snapshot://aspnet-test");
                SemanticExtractionRequest semanticRequest = CreateSemanticRequest(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", documentPath);
                AspNetCoreMinimalApiEndpointExtractor extractor = new();

                MinimalApiEndpointExtractionResult result = extractor.Extract(new MinimalApiEndpointExtractionRequest(snapshotStableKey, [semanticRequest]), CancellationToken.None);

                ArchitectureNode endpointNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint);
                Assert.Equal("GET /customers/{id}", endpointNode.DisplayName);
                Assert.Equal("project://src/Customer.Api/Customer.Api.csproj", endpointNode.ProjectStableKey?.Value);
                Assert.Contains("\"runtimeKind\":\"MinimalApi\"", endpointNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"framework\":\"ASP.NET Core\"", endpointNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"routeTemplate\":\"/customers/{id}\"", endpointNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"httpMethod\":\"GET\"", endpointNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"handlerSymbol\":", endpointNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"detectionMode\":\"DirectMapGetInvocation\"", endpointNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"confidenceReason\":\"Direct MapGet invocation with literal route template in Program.cs.\"", endpointNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);

                ArchitectureEdge declarationEdge = Assert.Single(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.DeclaresEndpoint);
                Assert.Equal("project://src/Customer.Api/Customer.Api.csproj", declarationEdge.SourceNodeStableKey.Value);
                Assert.Equal(endpointNode.StableKey.Value, declarationEdge.TargetNodeStableKey.Value);
                Assert.True(declarationEdge.IsDirect);

                EvidenceRecord evidence = Assert.Single(result.Snapshot.Evidence);
                Assert.Equal(EvidenceKind.SourceCode, evidence.EvidenceKind);
                Assert.Equal("src/Customer.Api/Program.cs", evidence.FilePath.Value);
                Assert.Equal("MapGet", evidence.SymbolName);
                Assert.Equal("Program.cs", evidence.ContainingSymbol);
                Assert.NotNull(evidence.StartLine);
                Assert.NotNull(evidence.EndLine);
                Assert.NotNull(evidence.SnippetHash);
                Assert.Contains("MapGet(\"/customers/{id}\"", evidence.SnippetPreview, StringComparison.Ordinal);
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The test owns the temporary repository and removes it after all assertions complete.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies ASP.NET Core controller and action attributes produce controller and endpoint graph facts with route, authorization, anonymous access, and filter metadata.
        /// </summary>
        [Fact]
        public void Extract_WhenControllerContainsAttributedActions_ShouldContributeControllerAndEndpointFacts()
        {
            // The fixture uses local attribute stubs so controller routing can be tested without loading ASP.NET Core runtime assemblies.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-wp008-aspnet-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.Api", "Controllers"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "Customer.Api", "Controllers", "OrdersController.cs");
                File.WriteAllText(documentPath, CreateControllerSource());
                SemanticExtractionRequest semanticRequest = CreateSemanticRequest(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", documentPath);
                AspNetCoreMinimalApiEndpointExtractor extractor = new();

                MinimalApiEndpointExtractionResult result = extractor.Extract(new MinimalApiEndpointExtractionRequest(new StableKey("snapshot://aspnet-test"), [semanticRequest]), CancellationToken.None);

                ArchitectureNode controllerNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Controller);
                Assert.Equal("OrdersController", controllerNode.DisplayName);
                Assert.Contains("\"framework\":\"ASP.NET Core\"", controllerNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"controllerName\":\"Orders\"", controllerNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);

                ArchitectureNode getEndpoint = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "GET /api/orders/{id}");
                Assert.Contains("\"controllerName\":\"Orders\"", getEndpoint.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"actionName\":\"Get\"", getEndpoint.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"authorizationPolicy\":\"Orders.Read\"", getEndpoint.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains("\"filterTypes\":[\"ServiceFilterAttribute\"]", getEndpoint.Metadata.ToCanonicalJson(), StringComparison.Ordinal);

                ArchitectureNode postEndpoint = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "POST /api/orders");
                Assert.Contains("\"allowsAnonymous\":true", postEndpoint.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.DeclaresEndpoint && edge.SourceNodeStableKey == controllerNode.StableKey && edge.TargetNodeStableKey == getEndpoint.StableKey);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.DeclaresEndpoint && edge.SourceNodeStableKey == controllerNode.StableKey && edge.TargetNodeStableKey == postEndpoint.StableKey);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Customer.Api/Controllers/OrdersController.cs" && evidence.SymbolName == "Get");
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated runs deterministic and isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies additional minimal API verbs, literal endpoint groups, and computed group routes are represented through endpoint metadata and unknown state.
        /// </summary>
        [Fact]
        public void Extract_WhenProgramContainsEndpointGroupsAndAdditionalVerbs_ShouldContributeGroupedEndpointFacts()
        {
            // Endpoint group handling proves Work Item 2 can combine literal group prefixes while preserving unknowns for computed prefixes.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-wp008-aspnet-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.Api"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "Customer.Api", "Program.cs");
                File.WriteAllText(documentPath, CreateGroupedMinimalApiSource());
                SemanticExtractionRequest semanticRequest = CreateSemanticRequest(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", documentPath);
                AspNetCoreMinimalApiEndpointExtractor extractor = new();

                MinimalApiEndpointExtractionResult result = extractor.Extract(new MinimalApiEndpointExtractionRequest(new StableKey("snapshot://aspnet-test"), [semanticRequest]), CancellationToken.None);

                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "POST /api/orders");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "PUT /api/orders/{id}");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "DELETE /api/orders/{id}");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "PATCH /api/orders/{id}");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "GET /api/orders/search");
                ArchitectureNode groupedEndpoint = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "POST /api/orders");
                Assert.Contains("\"endpointGroupPrefix\":\"/api\"", groupedEndpoint.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                ArchitectureNode fallbackEndpoint = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.DisplayName == "FALLBACK /fallback");
                Assert.Contains("\"httpMethod\":\"FALLBACK\"", fallbackEndpoint.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                ArchitectureNode unknownGroupedEndpoint = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint && node.UnknownState.HasUnknownData);
                Assert.Equal("Endpoint group route prefix is not a compile-time string literal.", unknownGroupedEndpoint.UnknownState.UnknownReason);
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated runs deterministic and isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies MVC setup, controller mapping, middleware ordering, custom middleware targets, and OpenAPI setup are captured as project runtime metadata.
        /// </summary>
        [Fact]
        public void Extract_WhenProgramContainsPipelineSetup_ShouldContributeProjectRuntimeMetadata()
        {
            // Pipeline metadata stays on the project node because Work Item 2 has no dedicated middleware node kind in the compiled graph contract.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-wp008-aspnet-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.Api"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "Customer.Api", "Program.cs");
                File.WriteAllText(documentPath, CreatePipelineSetupSource());
                SemanticExtractionRequest semanticRequest = CreateSemanticRequest(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", documentPath);
                AspNetCoreMinimalApiEndpointExtractor extractor = new();

                MinimalApiEndpointExtractionResult result = extractor.Extract(new MinimalApiEndpointExtractionRequest(new StableKey("snapshot://aspnet-test"), [semanticRequest]), CancellationToken.None);

                ArchitectureNode projectNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Project);
                string metadata = projectNode.Metadata.ToCanonicalJson();
                Assert.Contains("\"mvcSetupCalls\":[\"AddControllers\"]", metadata, StringComparison.Ordinal);
                Assert.Contains("\"controllerMappingCalls\":[\"MapControllers\"]", metadata, StringComparison.Ordinal);
                Assert.Contains("\"openApiEnabled\":true", metadata, StringComparison.Ordinal);
                Assert.Contains("\"middlewareOrder\":[\"UseRouting\",\"UseAuthentication\",\"UseAuthorization\",\"UseMiddleware\",\"UseSwagger\",\"UseSwaggerUI\"]", metadata, StringComparison.Ordinal);
                Assert.Contains("\"middlewareTypes\":[\"Customer.Api.RequestLoggingMiddleware\"]", metadata, StringComparison.Ordinal);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.SymbolName == "UseMiddleware");
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed after assertions to keep repeated runs deterministic and isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies non-literal route templates are reported as explicit unknowns instead of guessed endpoint routes.
        /// </summary>
        [Fact]
        public void Extract_WhenMapGetRouteIsComputed_ShouldContributeUnknownEndpointFact()
        {
            // Computed routes are intentionally preserved as unknown facts so contributors can see runtime surface uncertainty.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-wp008-aspnet-extractor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "Customer.Api"));
            try
            {
                string documentPath = Path.Combine(repositoryRoot, "src", "Customer.Api", "Program.cs");
                File.WriteAllText(documentPath, CreateComputedRouteProgramSource());
                SemanticExtractionRequest semanticRequest = CreateSemanticRequest(repositoryRoot, "src/Customer.Api/Customer.Api.csproj", documentPath);
                AspNetCoreMinimalApiEndpointExtractor extractor = new();

                MinimalApiEndpointExtractionResult result = extractor.Extract(new MinimalApiEndpointExtractionRequest(new StableKey("snapshot://aspnet-test"), [semanticRequest]), CancellationToken.None);

                ArchitectureNode endpointNode = Assert.Single(result.Snapshot.Nodes, node => node.NodeKind == NodeKind.Endpoint);
                Assert.True(endpointNode.UnknownState.HasUnknownData);
                Assert.Equal("MapGet route template is not a compile-time string literal.", endpointNode.UnknownState.UnknownReason);
                Assert.Contains("\"routeTemplate\":\"\\u003Cunknown\\u003E\"", endpointNode.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
                Assert.Single(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.DeclaresEndpoint);
                Assert.Single(result.Snapshot.Evidence);
                Assert.Empty(result.Snapshot.Errors);
            }
            finally
            {
                // The temporary repository is removed even when assertions fail so repeated test runs stay isolated.
                if (Directory.Exists(repositoryRoot))
                {
                    Directory.Delete(repositoryRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Creates a semantic extraction request for one C# source document.
        /// </summary>
        /// <param name="repositoryRoot">The absolute repository root that scopes repository-relative evidence paths.</param>
        /// <param name="projectContext">The repository-relative project path used to scope project and endpoint stable keys.</param>
        /// <param name="documentPath">The absolute source document path to parse.</param>
        /// <returns>A semantic extraction request with a C# syntax tree and semantic model.</returns>
        private static SemanticExtractionRequest CreateSemanticRequest(string repositoryRoot, string projectContext, string documentPath)
        {
            // The extractor only needs syntax and basic semantic access for this slice, so a lightweight compilation is sufficient.
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(documentPath), path: documentPath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Customer.Api",
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            return new SemanticExtractionRequest(repositoryRoot, projectContext, documentPath, syntaxTree, compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true));
        }

        /// <summary>
        /// Creates fixture source containing one direct minimal API endpoint mapping.
        /// </summary>
        /// <returns>The C# source text for the direct route fixture.</returns>
        private static string CreateProgramSource()
        {
            // The source mirrors a common top-level Program.cs pattern without depending on ASP.NET Core runtime assemblies.
            return string.Join(
                Environment.NewLine,
                [
                    "var builder = WebApplication.CreateBuilder(args);",
                    "var app = builder.Build();",
                    "app.MapGet(\"/customers/{id}\", (int id) => Results.Ok(id));",
                    "app.Run();"
                ]);
        }

        /// <summary>
        /// Creates fixture source containing one minimal API endpoint whose route is computed from a local variable.
        /// </summary>
        /// <returns>The C# source text for the computed route fixture.</returns>
        private static string CreateComputedRouteProgramSource()
        {
            // The variable prevents literal route extraction while still preserving evidence for the MapGet call.
            return string.Join(
                Environment.NewLine,
                [
                    "var builder = WebApplication.CreateBuilder(args);",
                    "var app = builder.Build();",
                    "var route = \"/customers\";",
                    "app.MapGet(route, () => Results.Ok());",
                    "app.Run();"
                ]);
        }

        /// <summary>
        /// Creates fixture source containing an ASP.NET Core controller with route, authorization, anonymous access, and filter attributes.
        /// </summary>
        /// <returns>The C# source text for the controller fixture.</returns>
        private static string CreateControllerSource()
        {
            // Attribute stubs are enough for syntax and semantic extraction while keeping the fixture independent of external ASP.NET Core assemblies.
            return string.Join(
                Environment.NewLine,
                [
                    "namespace Microsoft.AspNetCore.Mvc { public abstract class ControllerBase { } public sealed class ApiControllerAttribute : System.Attribute { } public sealed class RouteAttribute(string template) : System.Attribute { public string Template { get; } = template; } public sealed class HttpGetAttribute(string template) : System.Attribute { public string Template { get; } = template; } public sealed class HttpPostAttribute(string template = \"\") : System.Attribute { public string Template { get; } = template; } public sealed class ServiceFilterAttribute(System.Type type) : System.Attribute { } }",
                    "namespace Microsoft.AspNetCore.Authorization { public sealed class AuthorizeAttribute : System.Attribute { public string? Policy { get; set; } } public sealed class AllowAnonymousAttribute : System.Attribute { } }",
                    "namespace Customer.Api.Controllers",
                    "{",
                    "    using Microsoft.AspNetCore.Authorization;",
                    "    using Microsoft.AspNetCore.Mvc;",
                    "    [ApiController]",
                    "    [Route(\"api/[controller]\")]",
                    "    [Authorize(Policy = \"Orders.Read\")]",
                    "    public sealed class OrdersController : ControllerBase",
                    "    {",
                    "        [HttpGet(\"{id}\")]",
                    "        [ServiceFilter(typeof(OrderAuditFilter))]",
                    "        public string Get(int id) => id.ToString();",
                    "",
                    "        [HttpPost]",
                    "        [AllowAnonymous]",
                    "        public string Create() => \"created\";",
                    "    }",
                    "    public sealed class OrderAuditFilter { }",
                    "}"
                ]);
        }

        /// <summary>
        /// Creates fixture source containing endpoint groups and additional minimal API mapping methods.
        /// </summary>
        /// <returns>The C# source text for grouped minimal API fixture.</returns>
        private static string CreateGroupedMinimalApiSource()
        {
            // The grouped fixture covers literal group prefix combination and a computed group prefix unknown in one source file.
            return string.Join(
                Environment.NewLine,
                [
                    "var builder = WebApplication.CreateBuilder(args);",
                    "var app = builder.Build();",
                    "var api = app.MapGroup(\"/api\");",
                    "api.MapPost(\"/orders\", () => Results.Ok());",
                    "api.MapPut(\"/orders/{id}\", (int id) => Results.Ok(id));",
                    "api.MapDelete(\"/orders/{id}\", (int id) => Results.Ok(id));",
                    "api.MapPatch(\"/orders/{id}\", (int id) => Results.Ok(id));",
                    "api.MapMethods(\"/orders/search\", new[] { \"GET\" }, () => Results.Ok());",
                    "app.MapFallback(\"/fallback\", () => Results.Ok());",
                    "var prefix = \"/computed\";",
                    "var dynamicGroup = app.MapGroup(prefix);",
                    "dynamicGroup.MapGet(\"/items\", () => Results.Ok());",
                    "app.Run();"
                ]);
        }

        /// <summary>
        /// Creates fixture source containing MVC, controller mapping, middleware, and OpenAPI setup calls.
        /// </summary>
        /// <returns>The C# source text for pipeline setup fixture.</returns>
        private static string CreatePipelineSetupSource()
        {
            // The pipeline fixture uses common ASP.NET Core setup method names that Work Item 2 records as project metadata.
            return string.Join(
                Environment.NewLine,
                [
                    "namespace Customer.Api { public sealed class RequestLoggingMiddleware { } }",
                    "var builder = WebApplication.CreateBuilder(args);",
                    "builder.Services.AddControllers();",
                    "builder.Services.AddEndpointsApiExplorer();",
                    "builder.Services.AddSwaggerGen();",
                    "var app = builder.Build();",
                    "app.UseRouting();",
                    "app.UseAuthentication();",
                    "app.UseAuthorization();",
                    "app.UseMiddleware<Customer.Api.RequestLoggingMiddleware>();",
                    "app.UseSwagger();",
                    "app.UseSwaggerUI();",
                    "app.MapControllers();",
                    "app.Run();"
                ]);
        }
    }
}
