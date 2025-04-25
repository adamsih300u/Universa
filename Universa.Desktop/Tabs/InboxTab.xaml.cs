using System;
using System.Windows.Controls;
using Universa.Desktop.ViewModels;
using Universa.Desktop.Services;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Tabs;
using Universa.Desktop.Core;

namespace Universa.Desktop.Tabs
{
    public partial class InboxTab : UserControl
    {
        private UserControl editor;

        public InboxTab()
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
    }
} 