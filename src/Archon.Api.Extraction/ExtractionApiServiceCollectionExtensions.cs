using Archon.Application.Extraction.Requests;
using Archon.Application.Extraction.Orchestration;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Runs;
using Archon.Application.Extraction.Scheduling;
using Archon.Application.Extraction.Snapshots;
using Archon.Application.Extraction.Validation;
using Archon.Application.Graph.Persistence;
using Archon.Application.Rules;
using Archon.Extractors.Avalonia;
using Archon.Extractors.Blazor;
using Archon.Extractors.Integrations.Foundation;
using Archon.Extractors.Maui;
using Archon.Extractors.Projects.Solutions;
using Archon.Extractors.Razor;
using Archon.Extractors.WinForms;
using Archon.Extractors.WinUI;
using Archon.Extractors.Wpf;
using Archon.Infrastructure.Roslyn.Extraction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Archon.Api.Extraction
{
    /// <summary>
    /// Registers the services required by the extraction API module.
    /// </summary>
    public static class ExtractionApiServiceCollectionExtensions
    {
        /// <summary>
        /// Adds application and module services needed by the WP004 extraction start/status endpoints.
        /// </summary>
        /// <param name="services">The service collection used by the API host or test host.</param>
        /// <returns>The same service collection so callers can chain additional registrations.</returns>
        public static IServiceCollection AddArchonExtractionApi(this IServiceCollection services)
        {
            // The module owns the application-level extraction workflow registrations while host composition can override infrastructure
            // ports, such as snapshot persistence, by adding concrete adapters after this module is registered.
            ArgumentNullException.ThrowIfNull(services);

            services.AddLogging();
            services.AddSingleton<StartExtractionRequestValidator>();
            services.AddSingleton<IExtractionRunHistory, InMemoryExtractionRunHistory>();
            services.AddSingleton<IExtractionStage, RepositorySolutionExtractionStage>();
            services.AddSingleton<IExtractionStage, RoslynSemanticExtractionStage>();
            services.AddSingleton<IExtractionStage, Wp007ExtractionStage>();
            services.AddSingleton<IExtractionStage, Wp008AspNetCoreMinimalApiExtractionStage>();
            services.AddSingleton<IExtractionStage, Wp009DataAccessExtractionStage>();
            services.AddSingleton<ExternalIntegrationFoundationExtractor>();
            services.AddSingleton<IExternalIntegrationObservationProvider, NoOpExternalIntegrationObservationProvider>();
            services.AddSingleton<IExtractionStage, Wp010ExternalIntegrationExtractionStage>();
            services.AddSingleton<BlazorRouteComponentExtractor>();
            services.AddSingleton<RazorPageViewExtractor>();
            services.AddSingleton<WinFormsStaticUiExtractor>();
            services.AddSingleton<WpfXamlExtractor>();
            services.AddSingleton<WinUiXamlExtractor>();
            services.AddSingleton<MauiXamlExtractor>();
            services.AddSingleton<AvaloniaAxamlExtractor>();
            services.AddSingleton<Wp011BlazorRouteComponentExtractionStage>();
            services.AddSingleton<Wp011RazorPageViewExtractionStage>();
            services.AddSingleton<Wp011WinFormsStaticUiExtractionStage>();
            services.AddSingleton<Wp011WpfXamlExtractionStage>();
            services.AddSingleton<Wp011WinUiXamlExtractionStage>();
            services.AddSingleton<Wp011MauiXamlExtractionStage>();
            services.AddSingleton<Wp011AvaloniaAxamlExtractionStage>();
            services.AddSingleton<IExtractionStage, Wp011UiClientExtractionStage>();
            services.AddSingleton(_ => new RuleCatalogOptions());
            services.AddSingleton<RuleCatalogLoader>();
            services.AddSingleton<RuleEvaluator>();
            services.AddSingleton<IRuleCatalogStore, InMemoryRuleCatalogStore>();
            services.AddSingleton<RuleExtractionIntegrationService>();
            services.AddSingleton<IExtractionStage, RuleEvaluationExtractionStage>();
            services.AddSingleton<ExtractionPipelineRunner>();
            services.AddSingleton<ExtractionSnapshotAssembler>();
            services.AddSingleton<IArchitectureSnapshotWriter, InMemoryArchitectureSnapshotWriter>();
            services.AddSingleton<ExtractionOrchestrator>();
            services.AddSingleton<IExtractionWorkScheduler, InProcessExtractionWorkScheduler>();
            services.AddSingleton<StartExtractionApplicationService>();

            return services;
        }
    }
}
