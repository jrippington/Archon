using Archon.ServiceDefaults;
using ArchonMcp.McpRuntime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies the WP015 Work Item 1 read-only MCP runtime baseline registration and readiness behavior.
    /// </summary>
    public sealed class ArchonMcpRuntimeBaselineTests
    {
        /// <summary>
        /// Confirms host startup composes the mandatory baseline capability, conservative limits, and query-layer services.
        /// </summary>
        [Fact]
        public void BuildApplicationRegistersBaselineCatalogAndLimits()
        {
            // BuildApplication gives the test direct access to the composed service provider without starting Kestrel.
            using WebApplication app = Program.BuildApplication(Array.Empty<string>());

            IArchonMcpRegistrationCatalog catalog = app.Services.GetRequiredService<IArchonMcpRegistrationCatalog>();
            IReadOnlyList<ArchonMcpCapabilityRegistration> registrations = catalog.GetRegistrations();
            ArchonMcpCatalogValidationResult validationResult = catalog.Validate();
            Microsoft.Extensions.Options.IOptions<ArchonMcpLimitsOptions> limits = app.Services
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<ArchonMcpLimitsOptions>>();

            // The catalog now contains the operational baseline, search, and project/dependency investigation tools.
            Assert.Contains(registrations, registration =>
                registration.Name == ArchonMcpBaselineCapabilities.Health.Name &&
                registration.Kind == ArchonMcpCapabilityKind.Operational &&
                registration.Required &&
                registration.ReadOnly);
            Assert.Contains(registrations, registration =>
                registration.Name == ArchonMcpBaselineCapabilities.Search.Name &&
                registration.Kind == ArchonMcpCapabilityKind.Tool &&
                registration.Required &&
                registration.ReadOnly);
            Assert.Contains(registrations, registration => registration.Name == ArchonMcpBaselineCapabilities.DescribeProject.Name && registration.Kind == ArchonMcpCapabilityKind.Tool && registration.Required && registration.ReadOnly);
            Assert.Contains(registrations, registration => registration.Name == ArchonMcpBaselineCapabilities.GetDependencies.Name && registration.Kind == ArchonMcpCapabilityKind.Tool && registration.Required && registration.ReadOnly);
            Assert.Contains(registrations, registration => registration.Name == ArchonMcpBaselineCapabilities.GetDependents.Name && registration.Kind == ArchonMcpCapabilityKind.Tool && registration.Required && registration.ReadOnly);
            Assert.Contains(registrations, registration => registration.Name == ArchonMcpBaselineCapabilities.FindDependencyPaths.Name && registration.Kind == ArchonMcpCapabilityKind.Tool && registration.Required && registration.ReadOnly);
            Assert.Contains(registrations, registration => registration.Name == ArchonMcpBaselineCapabilities.DescribeSymbol.Name && registration.Kind == ArchonMcpCapabilityKind.Tool && registration.Required && registration.ReadOnly);
            Assert.Contains(registrations, registration => registration.Name == ArchonMcpBaselineCapabilities.FindSymbolUsages.Name && registration.Kind == ArchonMcpCapabilityKind.Tool && registration.Required && registration.ReadOnly);
            Assert.Contains(registrations, registration => registration.Name == ArchonMcpBaselineCapabilities.GetDataAccessUsage.Name && registration.Kind == ArchonMcpCapabilityKind.Tool && registration.Required && registration.ReadOnly);
            Assert.Contains(registrations, registration => registration.Name == ArchonMcpBaselineCapabilities.AssessChangeImpact.Name && registration.Kind == ArchonMcpCapabilityKind.Tool && registration.Required && registration.ReadOnly);
            Assert.Contains(registrations, registration => registration.Name == ArchonMcpBaselineCapabilities.GetArchitectureRules.Name && registration.Kind == ArchonMcpCapabilityKind.Tool && registration.Required && registration.ReadOnly);
            Assert.Contains(registrations, registration => registration.Name == ArchonMcpBaselineCapabilities.GetHotlistFindings.Name && registration.Kind == ArchonMcpCapabilityKind.Tool && registration.Required && registration.ReadOnly);
            Assert.Contains(registrations, registration => registration.Name == ArchonMcpBaselineCapabilities.GetSnapshotDiff.Name && registration.Kind == ArchonMcpCapabilityKind.Tool && registration.Required && registration.ReadOnly);
            Assert.Contains(registrations, registration => registration.Name == ArchonMcpBaselineCapabilities.ReadResource.Name && registration.Kind == ArchonMcpCapabilityKind.Resource && registration.Required && registration.ReadOnly);
            Assert.True(validationResult.IsReady);
            Assert.Empty(validationResult.MissingRequiredCapabilityNames);
            Assert.Empty(validationResult.ForbiddenCapabilityNames);

            // Conservative defaults are part of the baseline contract that later MCP tool/resource slices should reuse.
            Assert.Equal(25, limits.Value.MaxResultCount);
            Assert.Equal(3, limits.Value.MaxTraversalDepth);
            Assert.Equal(10, limits.Value.MaxEvidenceCount);
            Assert.Equal(5, limits.Value.MaxPathCount);
            Assert.Equal(24000, limits.Value.MaxSerializedContextCharacters);
        }

        /// <summary>
        /// Confirms readiness fails closed when the mandatory baseline capability is not present in the catalog.
        /// </summary>
        /// <returns>A task that completes after the in-memory readiness endpoint has reported service-unavailable status.</returns>
        [Fact]
        public async Task ReadinessFailsClosedWhenMandatoryRegistrationIsMissing()
        {
            // The configuration switch simulates an incomplete catalog without removing the production readiness check itself.
            await using WebApplication app = Program.BuildApplication(
                ["Archon:Mcp:DisableBaselineCapabilityRegistration=true"],
                builder => builder.WebHost.UseTestServer());
            await app.StartAsync();

            IArchonMcpRegistrationCatalog catalog = app.Services.GetRequiredService<IArchonMcpRegistrationCatalog>();
            ArchonMcpCatalogValidationResult validationResult = catalog.Validate();
            using HttpClient client = app.GetTestClient();

            // The catalog and HTTP readiness endpoint should agree that the host is not ready when a mandatory entry is missing.
            Assert.False(validationResult.IsReady);
            Assert.Contains(ArchonMcpBaselineCapabilities.Health.Name, validationResult.MissingRequiredCapabilityNames);
            HttpResponseMessage healthResponse = await client.GetAsync(ServiceDefaultEndpointNames.Health);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, healthResponse.StatusCode);
        }

        /// <summary>
        /// Confirms forbidden capability names are rejected by catalog validation and therefore cannot be silently advertised.
        /// </summary>
        [Fact]
        public void CatalogValidationRejectsForbiddenCapabilityNames()
        {
            // A custom catalog instance keeps the test focused on name validation without changing host composition.
            ArchonMcpCapabilityRegistration forbiddenRegistration = new(
                "archon.execute_shell",
                ArchonMcpCapabilityKind.Tool,
                Required: false,
                ReadOnly: true,
                "Unsafe test-only capability name that must be rejected.");
            Microsoft.Extensions.Options.IOptions<ArchonMcpRegistrationCatalogOptions> options = Microsoft.Extensions.Options.Options.Create(
                new ArchonMcpRegistrationCatalogOptions());
            IArchonMcpRegistrationCatalog catalog = new ArchonMcpRegistrationCatalog(
                [
                    ArchonMcpBaselineCapabilities.Health,
                    ArchonMcpBaselineCapabilities.Search,
                    ArchonMcpBaselineCapabilities.DescribeProject,
                    ArchonMcpBaselineCapabilities.GetDependencies,
                    ArchonMcpBaselineCapabilities.GetDependents,
                    ArchonMcpBaselineCapabilities.FindDependencyPaths,
                    ArchonMcpBaselineCapabilities.DescribeSymbol,
                    ArchonMcpBaselineCapabilities.FindSymbolUsages,
                    ArchonMcpBaselineCapabilities.GetDataAccessUsage,
                    ArchonMcpBaselineCapabilities.AssessChangeImpact,
                    ArchonMcpBaselineCapabilities.GetArchitectureRules,
                    ArchonMcpBaselineCapabilities.GetHotlistFindings,
                    ArchonMcpBaselineCapabilities.GetSnapshotDiff,
                    ArchonMcpBaselineCapabilities.ReadResource,
                    ArchonMcpBaselineCapabilities.GetPrompt,
                    ArchonMcpBaselineCapabilities.ListPrompts,
                    ArchonMcpBaselineCapabilities.ImpactAnalysisPrompt,
                    ArchonMcpBaselineCapabilities.ModernizationBriefPrompt,
                    ArchonMcpBaselineCapabilities.RefactoringPreflightPrompt,
                    ArchonMcpBaselineCapabilities.NewFeaturePlacementPrompt,
                    ArchonMcpBaselineCapabilities.LegacyDataAccessReviewPrompt,
                    ArchonMcpBaselineCapabilities.HotlistSummaryPrompt,
                    ArchonMcpBaselineCapabilities.ArchitectureRuleCheckPrompt,
                    forbiddenRegistration
                ],
                options);

            ArchonMcpCatalogValidationResult validationResult = catalog.Validate();

            // The unsafe name proves the deny-list blocks arbitrary execution-style registrations before MCP exposure exists.
            Assert.False(validationResult.IsReady);
            Assert.Empty(validationResult.MissingRequiredCapabilityNames);
            Assert.Contains("archon.execute_shell", validationResult.ForbiddenCapabilityNames);
        }
    }
}
