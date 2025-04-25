using System;
using System.Windows.Controls;
using Universa.Desktop.ViewModels;
using Universa.Desktop.Services;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Tabs;
using Universa.Desktop.Core;

namespace Universa.Desktop.Tabs
{
    public partial class EditorTab : UserControl
    {
        private UserControl editor;

        // Add ContentChanged event
        public event EventHandler ContentChanged;
        
        // Property to get/set FilePath
        public string FilePath { get; set; }

        public EditorTab()
        {
            InitializeComponent();
        }

        private void HandleFileOpen(string filePath)
        {
            var extension = System.IO.Path.GetExtension(filePath).ToLower();
            switch (extension)
            {
                case ".todo":
                    editor = new ToDoTab(filePath, ServiceLocator.Instance.GetService<IToDoViewModel>(), ServiceLocator.Instance.GetService<IServiceProvider>());
                    break;
                // ... existing code ...
            }
        }
        
        // Method to notify content has changed
        protected virtual void OnContentChanged()
        {
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }
        
        // Method to get content from the editor
        public string GetContent()
        {
            // Implement logic to return the editor content
            // This will depend on how your editor stores its content
            return string.Empty; // Placeholder implementation
        }
    }
} 