using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Universa.Desktop.Services;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Views;
using Universa.Desktop.Library;
using System.IO;
using System.Diagnostics;
using SQLite;
using Universa.Desktop.ViewModels;
using Universa.Desktop.Interfaces;
using System.Threading.Tasks;

namespace Universa.Desktop
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private IServiceProvider _serviceProvider;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // Ensure SQLite is properly initialized
                EnsureSQLiteInitialized();
                
                // Set up dependency injection
                var services = new ServiceCollection();
                
                // Register core services
                services.AddSingleton<IConfigurationService>(provider =>
                {
                    var configService = new ConfigurationService();
                    return configService;
                });
                services.AddSingleton<IDialogService, DialogService>();
                
                // ToDo services must be transient (not singleton) to handle multiple different ToDo files
                services.AddTransient<IToDoService>(provider => {
                    var configService = provider.GetRequiredService<IConfigurationService>();
                    var libraryPath = configService.GetValue<string>("LibraryPath") ?? 
                                      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Universa");
                    
                    // We'll set a temporary path here, but it will be overridden when a specific ToDo file is opened
                    var defaultTodoPath = Path.Combine(libraryPath, "default.todo");
                    
                    // Ensure the directory exists
                    if (!Directory.Exists(libraryPath))
                    {
                        try
                        {
                            Directory.CreateDirectory(libraryPath);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error creating default library directory: {ex.Message}");
                            // Use temp folder as fallback
                            libraryPath = Path.GetTempPath();
                            defaultTodoPath = Path.Combine(libraryPath, "default.todo");
                        }
                    }
                    
                    return new ToDoService(configService, defaultTodoPath);
                });
                
                // ToDo view model must also be transient to match the service lifecycle
                services.AddTransient<IToDoViewModel, ToDoViewModel>();
                services.AddTransient<Views.MainWindow>();
                services.AddTransient<Views.SettingsWindow>();
                services.AddTransient<ViewModels.SettingsViewModel>();

                // Get application data path
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Universa"
                );
                Debug.WriteLine($"Application data path: {appDataPath}");

                // Register additional services through ServiceLocator
                ServiceLocator.RegisterServices(services);

                // Build the service provider
                _serviceProvider = services.BuildServiceProvider();

                // Initialize ServiceLocator with our service provider
                ServiceLocator.Initialize(_serviceProvider);

                // Initialize configuration
                var configService = _serviceProvider.GetRequiredService<IConfigurationService>();
                await configService.InitializeAsync();

                // Create the main window but don't show it yet
                var mainWindow = _serviceProvider.GetRequiredService<Views.MainWindow>();

                // Check if library path is configured
                if (await ValidateLibraryPath(configService))
                {
                    // Show the main window only after successful validation
                    mainWindow.Show();
                }
                else
                {
                    // If validation failed, shutdown the application
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting application: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private async Task<bool> ValidateLibraryPath(IConfigurationService configService)
        {
            var libraryPath = configService.Provider.LibraryPath;
            Debug.WriteLine($"Validating library path: {libraryPath}");

            if (string.IsNullOrEmpty(libraryPath))
            {
                Debug.WriteLine("Library path is not configured, showing settings window");
                var settingsWindow = _serviceProvider.GetRequiredService<Views.SettingsWindow>();
                var result = settingsWindow.ShowDialog();

                if (result != true || string.IsNullOrEmpty(configService.Provider.LibraryPath))
                {
                    Debug.WriteLine("User cancelled or did not set library path");
                    MessageBox.Show("A library path must be configured to use the application.", 
                        "Configuration Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // Get the updated library path after settings window closes
                libraryPath = configService.Provider.LibraryPath;
                Debug.WriteLine($"User set library path to: {libraryPath}");
            }

            if (!Directory.Exists(libraryPath))
            {
                Debug.WriteLine($"Creating library directory: {libraryPath}");
                try
                {
                    Directory.CreateDirectory(libraryPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error creating library directory: {ex.Message}");
                    MessageBox.Show($"Could not create library directory: {ex.Message}", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }

            // Initialize trackers
            Debug.WriteLine("Initializing ProjectTracker and ToDoTracker");
            ProjectTracker.Instance.Initialize(libraryPath);
            await ToDoTracker.Instance.InitializeAsync(libraryPath);

            // Save the configuration after successful validation
            configService.Save();
            return true;
        }

        /// <summary>
        /// Ensures SQLite is properly initialized
        /// </summary>
        private void EnsureSQLiteInitialized()
        {
            try
            {
                // Get the directory where the application is running
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                Debug.WriteLine($"Application base directory: {baseDir}");
                
                // Check SQLite-net-pcl version
                Debug.WriteLine($"Checking SQLite-net-pcl initialization");
                
                // Create a test in-memory database to verify SQLite is working
                using (var connection = new SQLiteConnection(":memory:"))
                {
                    Debug.WriteLine("Successfully opened in-memory SQLite database with sqlite-net-pcl");
                    
                    // Create a test table
                    connection.CreateTable<TestTable>();
                    Debug.WriteLine("Successfully created test table");
                    
                    // Insert a test record
                    connection.Insert(new TestTable { Name = "Test" });
                    
                    // Query the test record
                    var count = connection.Table<TestTable>().Count();
                    Debug.WriteLine($"Test table record count: {count}");
                }
                
                Debug.WriteLine("SQLite initialization successful");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing SQLite: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                if (ex is DllNotFoundException || ex is BadImageFormatException)
                {
                    Debug.WriteLine("Native SQLite library could not be loaded. This is likely a deployment issue.");
                }
                
                // Don't rethrow - we want the application to continue even if SQLite fails
            }
        }

        // Simple class for testing SQLite initialization
        private class TestTable
        {
            [PrimaryKey, AutoIncrement]
            public int Id { get; set; }
            public string Name { get; set; }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                Debug.WriteLine("App.OnExit: Application is exiting");
                
                // We'll rely on the Window.Closing event to save chat history
                // since MainWindow is often null at this point
                
                // Just in case, verify the history file exists
                VerifyChatHistoryExists();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App.OnExit: Error during exit: {ex.Message}");
            }
            
            base.OnExit(e);
            
            // Dispose of trackers
            (ProjectTracker.Instance as IDisposable)?.Dispose();
            // We're no longer using ToDoTracker - it causes duplicate ToDos
            // (ToDoTracker.Instance as IDisposable)?.Dispose();
            
            (_serviceProvider as IDisposable)?.Dispose();
        }
        
        /// <summary>
        /// Verify that the chat history file exists and has content
        /// </summary>
        private void VerifyChatHistoryExists()
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var universaPath = Path.Combine(appDataPath, "Universa");
                var historyFilePath = Path.Combine(universaPath, "chat_history.json");
                
                if (File.Exists(historyFilePath))
                {
                    var fileInfo = new FileInfo(historyFilePath);
                    Debug.WriteLine($"App.VerifyChatHistoryExists: Chat history file exists, size: {fileInfo.Length} bytes, last write: {fileInfo.LastWriteTime}");
                }
                else
                {
                    Debug.WriteLine("App.VerifyChatHistoryExists: Chat history file does not exist");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App.VerifyChatHistoryExists: Error checking chat history file: {ex.Message}");
            }
        }
    }
}
