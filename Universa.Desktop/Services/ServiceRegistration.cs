using Microsoft.Extensions.DependencyInjection;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Services;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service registration for dependency injection
    /// Updated to support AvalonEdit migration while maintaining AI Chat Sidebar integration
    /// </summary>
    public static class ServiceRegistration
    {
        public static IServiceCollection RegisterMarkdownServices(this IServiceCollection services)
        {
            // Register AvalonEdit adapters as preferred implementations
            services.AddScoped<IChapterNavigationService, AvalonEditChapterNavigationAdapter>();
            services.AddScoped<IMarkdownStatusManager, AvalonEditStatusManager>();
            
            // Register existing services that work with both TextBox and AvalonEdit
            services.AddScoped<IFrontmatterProcessor, FrontmatterProcessor>();
            // Note: EnhancedTextSearchService may need interface implementation - using basic service for now
            services.AddScoped<IMarkdownFontService, MarkdownFontService>();
            services.AddScoped<IMarkdownFileService, MarkdownFileService>();
            services.AddScoped<IMarkdownUIEventHandler, MarkdownUIEventHandler>();
            services.AddScoped<IMarkdownEditorSetupService, MarkdownEditorSetupService>();
            
            // Register AI integration services (maintain all Fiction Chain Beta, etc.)
            services.AddScoped<FictionWritingBeta>();
            services.AddScoped<ManuscriptGenerationService>();
            // Note: ChapterDetectionService may be static - registering instance if available
            services.AddScoped<IConfigurationService, ConfigurationService>();
            
            return services;
        }

        public static IServiceCollection RegisterOrgModeServices(this IServiceCollection services)
        {
            // Existing OrgMode services remain unchanged
            services.AddScoped<IOrgModeService, OrgModeService>();
            services.AddScoped<IOrgStateConfigurationService, OrgStateConfigurationService>();
            
            return services;
        }

        public static IServiceCollection RegisterCoreServices(this IServiceCollection services)
        {
            // Core services used by both editors
            services.AddSingleton<IConfigurationService, ConfigurationService>();
            services.AddScoped<ISyncService, SyncService>();
            services.AddScoped<IWeatherService, WeatherService>();
            services.AddScoped<IMatrixService, MatrixService>();
            services.AddScoped<IAIService, AIService>();
            
            return services;
        }

        /// <summary>
        /// Registers all services for the application
        /// Maintains backward compatibility while supporting AvalonEdit migration
        /// </summary>
        public static IServiceCollection RegisterAllServices(this IServiceCollection services)
        {
            return services
                .RegisterCoreServices()
                .RegisterMarkdownServices()
                .RegisterOrgModeServices();
        }
    }
} 