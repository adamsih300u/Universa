using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using Universa.Desktop.Services.VectorStore;

namespace Universa.Desktop.Commands
{
    /// <summary>
    /// Command for vectorizing the library content
    /// </summary>
    public class VectorizeLibraryCommand : ICommand
    {
        private readonly ContentVectorizationService _contentVectorizationService;
        private readonly string _libraryPath;
        private bool _isExecuting;

        /// <summary>
        /// Event that is fired when the command's ability to execute changes
        /// </summary>
        public event EventHandler CanExecuteChanged;

        /// <summary>
        /// Creates a new instance of the VectorizeLibraryCommand
        /// </summary>
        /// <param name="contentVectorizationService">Service for vectorizing content</param>
        /// <param name="libraryPath">Path to the library</param>
        public VectorizeLibraryCommand(ContentVectorizationService contentVectorizationService, string libraryPath)
        {
            _contentVectorizationService = contentVectorizationService ?? throw new ArgumentNullException(nameof(contentVectorizationService));
            _libraryPath = libraryPath ?? throw new ArgumentNullException(nameof(libraryPath));
            
            Debug.WriteLine($"Created VectorizeLibraryCommand for library path: {_libraryPath}");
        }

        /// <summary>
        /// Determines whether the command can be executed
        /// </summary>
        /// <param name="parameter">Command parameter</param>
        /// <returns>True if the command can be executed, false otherwise</returns>
        public bool CanExecute(object parameter)
        {
            return !_isExecuting && _contentVectorizationService != null && !string.IsNullOrEmpty(_libraryPath);
        }

        /// <summary>
        /// Executes the command
        /// </summary>
        /// <param name="parameter">Command parameter</param>
        public void Execute(object parameter)
        {
            if (!CanExecute(parameter))
                return;

            _isExecuting = true;
            RaiseCanExecuteChanged();

            Debug.WriteLine($"Executing VectorizeLibraryCommand for library path: {_libraryPath}");

            Task.Run(async () =>
            {
                try
                {
                    int totalChunks = await _contentVectorizationService.VectorizeLibraryAsync(_libraryPath);
                    Debug.WriteLine("Library vectorization completed successfully");
                    
                    // Dispatch to UI thread to update status
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        System.Windows.MessageBox.Show(
                            $"Library vectorization completed successfully.\n\n" +
                            $"Total chunks: {totalChunks}\n\n" +
                            "You can now search your content using the search box.",
                            "Vectorization Complete",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error vectorizing library: {ex.Message}");
                    
                    // Dispatch to UI thread to show error
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        System.Windows.MessageBox.Show(
                            $"Error vectorizing library: {ex.Message}",
                            "Vectorization Error",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error);
                    });
                }
                finally
                {
                    _isExecuting = false;
                    
                    // Dispatch to UI thread to raise event
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        RaiseCanExecuteChanged();
                    });
                }
            });
        }

        /// <summary>
        /// Raises the CanExecuteChanged event
        /// </summary>
        private void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
} 