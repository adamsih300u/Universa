using System;
using System.Windows.Controls;
using Universa.Desktop.ViewModels;
using Universa.Desktop.Interfaces;
using System.IO;
using System.Threading.Tasks;

namespace Universa.Desktop.Tabs
{
    public partial class ToDoTab : UserControl, IFileTab
    {
        private readonly ToDoViewModel _viewModel;
        private string _filePath;
        private bool _isModified;

        public ToDoTab()
        {
            InitializeComponent();
            _viewModel = new ToDoViewModel();
            DataContext = _viewModel;
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                }
            }
        }

        public bool IsModified
        {
            get => _isModified;
            set
            {
                if (_isModified != value)
                {
                    _isModified = value;
                }
            }
        }

        public void OnTabSelected()
        {
            // No action needed as the ViewModel handles updates through TodosChanged event
        }

        public void OnTabDeselected()
        {
            // No action needed
        }

        public string GetContent()
        {
            // ToDo tab doesn't support direct content editing
            return string.Empty;
        }

        public async Task<bool> Save()
        {
            // ToDos are saved individually through the ViewModel
            // Return true to indicate success
            IsModified = false;
            return await Task.FromResult(true);
        }

        public async Task<bool> SaveAs(string path)
        {
            // ToDos are managed individually through the ViewModel
            // This operation is not supported for the ToDo tab
            // Return false to indicate failure or not supported
            return await Task.FromResult(false);
        }

        public void Reload()
        {
            // Refresh the ToDo list
            _viewModel.RefreshTodos();
            IsModified = false;
        }

        public void Dispose()
        {
            // Unsubscribe from events if needed
            _viewModel?.Dispose();
        }
    }
} 