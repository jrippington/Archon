using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.Razor;
using Xunit;

namespace Archon.Extractors.Tests.Razor
{
    /// <summary>
    /// Verifies the Razor Pages and MVC Razor extraction slice from repository fixture files into graph-ready snapshot facts.
    /// </summary>
    public sealed class RazorPageViewExtractorTests
    {
        /// <summary>
        /// Confirms Razor Pages artifacts contribute page, route, layout, partial, view component, tag helper, page-model, handler, form, navigation, authorization, evidence, and relationship facts.
        /// </summary>
        /// <returns>A task representing the asynchronous fixture creation and extraction assertion flow.</returns>
        [Fact]
        public async Task ExtractAsyncEmitsRazorPageLayoutPartialTagHelperHandlerAndNavigationFacts()
        {
            // The fixture uses a real temporary repository tree so discovery, path normalization, and build-output exclusion run together.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Web");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Pages", "Products"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Pages", "Shared"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "bin", "Debug"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Web.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "_ViewImports.cshtml"), "@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "_ViewStart.cshtml"), "@{ Layout = \"_Layout\"; }");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "Shared", "_ProductSummary.cshtml"), "<section>Summary</section>");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "bin", "Debug", "Ignored.cshtml"), "@page \"/ignored\"");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "Products", "Index.cshtml"), """
@page "/catalog/{id:int}"
@model Sample.Web.Pages.Products.IndexModel
@attribute [Authorize(Policy = "CatalogReaders")]
@{
    Layout = "_Layout";
    var secretToken = "super-secret-value";
}
<partial name="_ProductSummary" />
<vc:cart-summary product-id="Model.ProductId" />
<form method="post" asp-page-handler="Save">
    <button type="submit">Save</button>
</form>
<a asp-page="/Products/Details" asp-route-id="@Model.ProductId">Details</a>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "Products", "Index.cshtml.cs"), """
namespace Sample.Web.Pages.Products;

public sealed class IndexModel : Microsoft.AspNetCore.Mvc.RazorPages.PageModel
{
    public void OnGet(int id) { }

    public Microsoft.AspNetCore.Mvc.IActionResult OnPostSave() => Page();
}
""");

                RazorPageViewExtractor extractor = new();
                RazorPageViewExtractionRequest request = new(new StableKey("snapshot://sample/razor-pages"), repositoryRoot);

                RazorPageViewExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Empty(result.Snapshot.Errors);
                Assert.DoesNotContain(result.Snapshot.Nodes, node => node.DisplayName == "Ignored");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiApplication.Value && node.Metadata.ToCanonicalJson().Contains("\"uiFramework\":\"RazorPages\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiPage.Value && node.DisplayName == "Index" && node.Metadata.ToCanonicalJson().Contains("\"routeTemplate\":\"/catalog/{id:int}\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiRoute.Value && node.DisplayName == "/catalog/{id:int}");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiLayout.Value && node.DisplayName == "_Layout");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiComponent.Value && node.DisplayName == "_ProductSummary");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiComponent.Value && node.DisplayName == "cart-summary");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiControl.Value && node.DisplayName == "form");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.ViewModel.Value && node.DisplayName == "Sample.Web.Pages.Products.IndexModel");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Method.Value && node.DisplayName == "OnPostSave");
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DeclaresUiRoute.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesLayout.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesComponent.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesControl.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.UsesViewModel.Value);
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.HandlesUiEvent.Value && edge.Metadata.ToCanonicalJson().Contains("\"eventName\":\"post:Save\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.NavigatesTo.Value && edge.Metadata.ToCanonicalJson().Contains("\"navigationTarget\":\"/Products/Details\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.Metadata.ToCanonicalJson().Contains("\"authorizationPolicy\":\"CatalogReaders\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.Metadata.ToCanonicalJson().Contains("\"tagHelper\":\"Microsoft.AspNetCore.Mvc.TagHelpers\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.FilePath.Value == "src/Sample.Web/Pages/Products/Index.cshtml" && evidence.StartLine == 1 && evidence.SnippetHash is not null);
                Assert.Contains(result.Snapshot.Evidence, evidence => evidence.SnippetPreview is not null && evidence.SnippetPreview.Contains("[REDACTED]", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Confirms MVC Razor views contribute view, layout, partial, view component, tag helper, form, navigation, controller/action, evidence, and deduplicated relationship facts.
        /// </summary>
        /// <returns>A task representing the asynchronous fixture creation and extraction assertion flow.</returns>
        [Fact]
        public async Task ExtractAsyncEmitsMvcViewControllerActionAndDeduplicatedComponentFacts()
        {
            // The fixture follows the conventional Views/Controller layout so deterministic static controller/action correlation can be asserted without running MVC.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Web");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Views", "Orders"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Views", "Shared", "Components", "CartSummary"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Controllers"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Web.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "_ViewImports.cshtml"), "@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "_ViewStart.cshtml"), "@{ Layout = \"_Layout\"; }");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "Shared", "_OrderRow.cshtml"), "<tr><td>Order</td></tr>");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "Shared", "Components", "CartSummary", "Default.cshtml"), "<span>Cart</span>");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Controllers", "OrdersController.cs"), """
namespace Sample.Web.Controllers;

public sealed class OrdersController : Microsoft.AspNetCore.Mvc.Controller
{
    public Microsoft.AspNetCore.Mvc.IActionResult Details(int id) => View();
}
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "Orders", "Details.cshtml"), """
@model Sample.Web.Models.OrderDetailsViewModel
@{
    ViewData["Title"] = "Details";
    Layout = "_Layout";
}
<partial name="_OrderRow" />
<partial name="_OrderRow" />
@await Component.InvokeAsync("CartSummary", new { id = Model.Id })
<form asp-controller="Orders" asp-action="Details" method="post"></form>
<a asp-controller="Orders" asp-action="Index">Back</a>
""");

                RazorPageViewExtractor extractor = new();
                RazorPageViewExtractionRequest request = new(new StableKey("snapshot://sample/mvc-views"), repositoryRoot);

                RazorPageViewExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Empty(result.Snapshot.Errors);
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiView.Value && node.DisplayName == "Details" && node.Metadata.ToCanonicalJson().Contains("\"controllerName\":\"Orders\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiLayout.Value && node.DisplayName == "_Layout");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiComponent.Value && node.DisplayName == "_OrderRow");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.UiComponent.Value && node.DisplayName == "CartSummary");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Controller.Value && node.DisplayName == "OrdersController");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.Method.Value && node.DisplayName == "Details");
                Assert.Contains(result.Snapshot.Nodes, node => node.NodeKind.Value == NodeKind.ViewModel.Value && node.DisplayName == "Sample.Web.Models.OrderDetailsViewModel");
                Assert.Equal(1, result.Snapshot.Edges.Count(edge => edge.EdgeKind.Value == EdgeKind.UsesComponent.Value && result.Snapshot.Nodes.Any(node => node.StableKey == edge.TargetNodeStableKey && node.DisplayName == "_OrderRow")));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.DependsOn.Value && result.Snapshot.Nodes.Any(node => node.StableKey == edge.TargetNodeStableKey && node.DisplayName == "Details"));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.NavigatesTo.Value && edge.Metadata.ToCanonicalJson().Contains("\"navigationTarget\":\"Orders.Index\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Edges, edge => edge.EdgeKind.Value == EdgeKind.HandlesUiEvent.Value && edge.Metadata.ToCanonicalJson().Contains("\"eventName\":\"post:Orders.Details\"", StringComparison.Ordinal));
                Assert.Contains(result.Snapshot.Nodes, node => node.Metadata.ToCanonicalJson().Contains("\"tagHelper\":\"Microsoft.AspNetCore.Mvc.TagHelpers\"", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Confirms dynamic Razor targets produce non-fatal warnings and explicit unknown graph facts instead of guessed links.
        /// </summary>
        /// <returns>A task representing the asynchronous fixture creation and extraction assertion flow.</returns>
        [Fact]
        public async Task ExtractAsyncRecordsUnknownsForDynamicRazorTargets()
        {
            // Dynamic view, partial, page-model, controller/action, and navigation targets are unsupported by static extraction and must remain queryable unknowns.
            string repositoryRoot = CreateTemporaryRepositoryRoot();
            try
            {
                string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Web");
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Pages"));
                Directory.CreateDirectory(Path.Combine(projectDirectory, "Views", "Reports"));
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Sample.Web.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Razor\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Pages", "Dynamic.cshtml"), """
@page DynamicRoute
@model DynamicModel
<partial name="@Model.PartialName" />
<a asp-page="@Model.NextPage">Next</a>
""");
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Views", "Reports", "Summary.cshtml"), """
@{
    Layout = ViewBag.LayoutName;
}
@await Html.PartialAsync(Model.PartialName)
<a asp-controller="@Model.Controller" asp-action="@Model.Action">Dynamic</a>
""");

                RazorPageViewExtractor extractor = new();
                RazorPageViewExtractionRequest request = new(new StableKey("snapshot://sample/dynamic-razor"), repositoryRoot);

                RazorPageViewExtractionResult result = await extractor.ExtractAsync(request, CancellationToken.None);

                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("dynamic route", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("dynamic partial", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("dynamic navigation", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("unresolved page model", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Warnings, warning => warning.Contains("dynamic layout", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "Razor route template is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "Razor partial target is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "Razor navigation target is computed from runtime state.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "Razor page model type could not be resolved statically.");
                Assert.Contains(result.Snapshot.Nodes, node => node.UnknownState.HasUnknownData && node.UnknownState.UnknownReason == "Razor layout target is computed from runtime state.");
            }
            finally
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }

        /// <summary>
        /// Creates an empty temporary repository root for an extraction fixture.
        /// </summary>
        /// <returns>The absolute path to the temporary repository root.</returns>
        private static string CreateTemporaryRepositoryRoot()
        {
            // A GUID segment prevents tests from sharing paths while keeping cleanup simple and deterministic for each test invocation.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "ArchonRazorExtractorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repositoryRoot);
            return repositoryRoot;
        }
    }
}