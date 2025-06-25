using Microsoft.Extensions.DependencyInjection;
using System;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Managers;

namespace Universa.Desktop.Services
{
    public class ServiceLocator
    {
        private static ServiceLocator _instance;
        private readonly IServiceProvider _serviceProvider;

        private ServiceLocator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public static void RegisterServices(IServiceCollection services)
        {
            // Register additional services not registered in App.xaml.cs
            services.AddSingleton<WeatherManager>();
            services.AddSingleton<Managers.SyncManager>();
            services.AddSingleton<TTSManager>();
            services.AddSingleton<ModelProvider>();
            services.AddSingleton<ISubsonicService, SubsonicService>();
            services.AddSingleton<JellyfinService>();
            services.AddSingleton<AudiobookshelfService>();
            services.AddSingleton<IMusicDataService, MusicDataService>();
            services.AddTransient<IChapterNavigationService, ChapterNavigationService>();
            services.AddTransient<IMarkdownFontService, MarkdownFontService>();
            services.AddTransient<IMarkdownFileService, MarkdownFileService>();
            services.AddTransient<IMarkdownUIEventHandler, MarkdownUIEventHandler>();
            services.AddTransient<IMarkdownEditorSetupService, MarkdownEditorSetupService>();
            
            // Register VideoWindowManager
            services.AddSingleton<VideoWindowManager>();
            
            // Register MediaPlayerManager with a factory method that resolves IMediaWindow
            services.AddSingleton<MediaPlayerManager>(provider => {
                // The IMediaWindow will be resolved when MainWindow is created
                // and then the MediaPlayerManager will be properly initialized
                return new MediaPlayerManager(null);
            });
        }

        public static void Initialize(IServiceProvider serviceProvider)
        {
            _instance = new ServiceLocator(serviceProvider);
        }

        public T GetService<T>() where T : class
        {
            return _serviceProvider.GetService<T>();
        }

        public T GetRequiredService<T>() where T : class
        {
            return _serviceProvider.GetRequiredService<T>();
        }

        public static ServiceLocator Instance => _instance ?? throw new InvalidOperationException("ServiceLocator has not been initialized");
    }
} 