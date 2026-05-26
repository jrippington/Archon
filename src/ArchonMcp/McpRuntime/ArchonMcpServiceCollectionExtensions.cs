using Archon.Api.Query;
using ArchonMcp.McpDataAccess;
using ArchonMcp.McpDependencies;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpHotlist;
using ArchonMcp.McpImpact;
using ArchonMcp.McpPrompts;
using ArchonMcp.McpProjects;
using ArchonMcp.McpResources;
using ArchonMcp.McpRules;
using ArchonMcp.McpSearch;
using ArchonMcp.McpSecurity;
using ArchonMcp.McpSnapshotDiff;
using ArchonMcp.McpSymbols;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArchonMcp.McpRuntime
{
    /// <summary>
    /// Registers the baseline Archon MCP runtime services with the host dependency-injection container.
    /// </summary>
    /// <remarks>
    /// The extension method is the composition seam for the early WP015 foundation. It adds query-layer services for later MCP
    /// handlers, binds conservative MCP limits, registers the baseline operational capability, contributes the catalog readiness
    /// health check, and composes the shared response-envelope helpers used by future tools and resources.
    /// </remarks>
    public static class ArchonMcpServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the read-only MCP runtime baseline and its readiness validation services.
        /// </summary>
        /// <param name="services">The service collection owned by the Archon MCP host.</param>
        /// <param name="configuration">The configuration source used to bind MCP catalog and limit options.</param>
        /// <returns>The same service collection so host composition can continue chaining registrations.</returns>
        public static IServiceCollection AddArchonMcpRuntimeBaseline(this IServiceCollection services, IConfiguration configuration)
        {
            // The baseline is registered through DI so tests and later MCP slices can validate composition without launching a process.
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddArchonQueryApi();
            services.AddOptions<ArchonMcpLimitsOptions>()
                .Bind(configuration.GetSection(ArchonMcpLimitsOptions.SectionName))
                .Validate(options => options.MaxResultCount > 0, "MaxResultCount must be greater than zero.")
                .Validate(options => options.MaxTraversalDepth > 0, "MaxTraversalDepth must be greater than zero.")
                .Validate(options => options.MaxEvidenceCount > 0, "MaxEvidenceCount must be greater than zero.")
                .Validate(options => options.MaxPathCount > 0, "MaxPathCount must be greater than zero.")
                .Validate(options => options.MaxSerializedContextCharacters > 0, "MaxSerializedContextCharacters must be greater than zero.")
                .ValidateOnStart();
            services.AddOptions<ArchonMcpRegistrationCatalogOptions>()
                .Bind(configuration.GetSection(ArchonMcpRegistrationCatalogOptions.SectionName))
                .Validate(options => options.MandatoryCapabilityNames.Length > 0, "At least one mandatory MCP capability name is required.")
                .ValidateOnStart();
            services.AddOptions<ArchonMcpSecurityOptions>()
                .Bind(configuration.GetSection(ArchonMcpSecurityOptions.SectionName))
                .Validate(options => options.RequireAuthenticatedCaller || !string.IsNullOrWhiteSpace(options.TestCallerId), "A local caller identity is required when authentication is disabled for MCP testing.")
                .ValidateOnStart();

            // The baseline operation is enabled when no explicit security allow-list is configured, while any configured allow-list
            // remains authoritative and can disable individual operations for fail-closed tests or deployments.
            if (!configuration.GetSection($"{ArchonMcpSecurityOptions.SectionName}:AllowedOperations").Exists())
            {
                services.Configure<ArchonMcpSecurityOptions>(options => options.AllowedOperations =
                [
                    ArchonMcpBaselineCapabilities.Health.Name,
                    ArchonMcpBaselineCapabilities.Search.Name,
                    ArchonMcpBaselineCapabilities.DescribeProject.Name,
                    ArchonMcpBaselineCapabilities.GetDependencies.Name,
                    ArchonMcpBaselineCapabilities.GetDependents.Name,
                    ArchonMcpBaselineCapabilities.FindDependencyPaths.Name,
                    ArchonMcpBaselineCapabilities.DescribeSymbol.Name,
                    ArchonMcpBaselineCapabilities.FindSymbolUsages.Name,
                    ArchonMcpBaselineCapabilities.GetDataAccessUsage.Name,
                    ArchonMcpBaselineCapabilities.AssessChangeImpact.Name,
                    ArchonMcpBaselineCapabilities.GetArchitectureRules.Name,
                    ArchonMcpBaselineCapabilities.GetHotlistFindings.Name,
                    ArchonMcpBaselineCapabilities.GetSnapshotDiff.Name,
                    ArchonMcpBaselineCapabilities.ReadResource.Name,
                    ArchonMcpBaselineCapabilities.GetPrompt.Name,
                    ArchonMcpBaselineCapabilities.ListPrompts.Name
                ]);
            }

            // The baseline capability is added only when configuration does not explicitly request omission for fail-closed
            // readiness testing or future controlled deployment scenarios.
            if (!configuration.GetValue<bool>("Archon:Mcp:DisableBaselineCapabilityRegistration"))
            {
                services.AddSingleton(ArchonMcpBaselineCapabilities.Health);
                services.AddSingleton(ArchonMcpBaselineCapabilities.Search);
                services.AddSingleton(ArchonMcpBaselineCapabilities.DescribeProject);
                services.AddSingleton(ArchonMcpBaselineCapabilities.GetDependencies);
                services.AddSingleton(ArchonMcpBaselineCapabilities.GetDependents);
                services.AddSingleton(ArchonMcpBaselineCapabilities.FindDependencyPaths);
                services.AddSingleton(ArchonMcpBaselineCapabilities.DescribeSymbol);
                services.AddSingleton(ArchonMcpBaselineCapabilities.FindSymbolUsages);
                services.AddSingleton(ArchonMcpBaselineCapabilities.GetDataAccessUsage);
                services.AddSingleton(ArchonMcpBaselineCapabilities.AssessChangeImpact);
                services.AddSingleton(ArchonMcpBaselineCapabilities.GetArchitectureRules);
                services.AddSingleton(ArchonMcpBaselineCapabilities.GetHotlistFindings);
                services.AddSingleton(ArchonMcpBaselineCapabilities.GetSnapshotDiff);
                services.AddSingleton(ArchonMcpBaselineCapabilities.ReadResource);
                services.AddSingleton(ArchonMcpBaselineCapabilities.GetPrompt);
                services.AddSingleton(ArchonMcpBaselineCapabilities.ListPrompts);
                services.AddSingleton(ArchonMcpBaselineCapabilities.ImpactAnalysisPrompt);
                services.AddSingleton(ArchonMcpBaselineCapabilities.ModernizationBriefPrompt);
                services.AddSingleton(ArchonMcpBaselineCapabilities.RefactoringPreflightPrompt);
                services.AddSingleton(ArchonMcpBaselineCapabilities.NewFeaturePlacementPrompt);
                services.AddSingleton(ArchonMcpBaselineCapabilities.LegacyDataAccessReviewPrompt);
                services.AddSingleton(ArchonMcpBaselineCapabilities.HotlistSummaryPrompt);
                services.AddSingleton(ArchonMcpBaselineCapabilities.ArchitectureRuleCheckPrompt);
            }

            services.TryAddSingleton<IArchonMcpRegistrationCatalog, ArchonMcpRegistrationCatalog>();
            services.TryAddSingleton<IArchonMcpRequestValidator, ArchonMcpRequestValidator>();
            services.TryAddSingleton<ArchonMcpRequestValidator>();
            services.TryAddSingleton<ArchonMcpLimitGuard>();
            services.TryAddSingleton<IArchonMcpSensitiveTextRedactor, ArchonMcpSensitiveTextRedactor>();
            services.TryAddSingleton<IArchonMcpResponseMapper, ArchonMcpResponseMapper>();
            services.TryAddSingleton<ArchonMcpResponseMapper>();
            services.TryAddSingleton<IArchonMcpBaselineOperation, ArchonMcpBaselineOperation>();
            services.TryAddSingleton<IArchonMcpSearchTool, ArchonMcpSearchTool>();
            services.TryAddSingleton<IArchonMcpProjectTool, ArchonMcpProjectTool>();
            services.TryAddSingleton<IArchonMcpDependencyTool, ArchonMcpDependencyTool>();
            services.TryAddSingleton<IArchonMcpDependencyPathTool, ArchonMcpDependencyPathTool>();
            services.TryAddSingleton<IArchonMcpSymbolTool, ArchonMcpSymbolTool>();
            services.TryAddSingleton<IArchonMcpDataAccessTool, ArchonMcpDataAccessTool>();
            services.TryAddSingleton<IArchonMcpImpactTool, ArchonMcpImpactTool>();
            services.TryAddSingleton<IArchonMcpRulesTool, ArchonMcpRulesTool>();
            services.TryAddSingleton<IArchonMcpHotlistTool, ArchonMcpHotlistTool>();
            services.TryAddSingleton<IArchonMcpSnapshotDiffTool, ArchonMcpSnapshotDiffTool>();
            services.TryAddSingleton<IArchonMcpResourceUriParser, ArchonMcpResourceUriParser>();
            services.TryAddSingleton<IArchonMcpCurrentSnapshotProvider, ArchonMcpCurrentSnapshotProvider>();
            services.TryAddSingleton<IArchonMcpCurrentResourceHandler, ArchonMcpCurrentResourceHandler>();
            services.TryAddSingleton<IArchonMcpParameterizedResourceHandler, ArchonMcpParameterizedResourceHandler>();
            services.TryAddSingleton<IArchonMcpResourceDispatcher, ArchonMcpResourceDispatcher>();
            services.TryAddSingleton<IArchonMcpPromptRegistry, ArchonMcpPromptRegistry>();
            services.TryAddSingleton<IArchonMcpPromptTool, ArchonMcpPromptTool>();
            services.TryAddSingleton<IArchonMcpCallerContextProvider, ConfigurationArchonMcpCallerContextProvider>();
            services.TryAddSingleton<IArchonMcpOperationAuthorizer, ConfigurationArchonMcpOperationAuthorizer>();
            services.TryAddSingleton<ArchonMcpAuditParameterNormalizer>();
            services.TryAddSingleton<IArchonMcpAuditSink, LoggingArchonMcpAuditSink>();
            services.TryAddSingleton<IArchonMcpOperationExecutor, ArchonMcpOperationExecutor>();
            services.TryAddSingleton<IArchonMcpSecureEvidenceMapper, ArchonMcpSecureEvidenceMapper>();
            services.AddHealthChecks()
                .AddCheck<ArchonMcpCatalogHealthCheck>("archon_mcp_catalog");

            return services;
        }
    }
}
