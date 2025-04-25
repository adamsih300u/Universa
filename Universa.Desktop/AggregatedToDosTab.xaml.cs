using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Universa.Desktop.Models;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Services;
using Universa.Desktop.Core.Configuration;
using System.Text.Json;
using ServiceLocator = Universa.Desktop.Services.ServiceLocator;

namespace Universa.Desktop
{
    public partial class AggregatedToDosTab : UserControl, INotifyPropertyChanged
    {
        private bool _hideFutureItems;
        private bool _showCompletedItems;
        private ObservableCollection<ToDoItem> _allItems;
        private ObservableCollection<ToDoItem> _filteredItems;
        private string _basePath;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool HideFutureItems
        {
            get => _hideFutureItems;
            set
            {
                if (_hideFutureItems != value)
                {
                    _hideFutureItems = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HideFutureItems)));
                    UpdateFilteredItems();
                }
            }
        }
        
        public bool ShowCompletedItems
        {
            get => _showCompletedItems;
            set
            {
                if (_showCompletedItems != value)
                {
                    _showCompletedItems = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowCompletedItems)));
                    UpdateFilteredItems();
                }
            }
        }

        public AggregatedToDosTab(string basePath)
        {
            InitializeComponent();
            _basePath = basePath;
            _allItems = new ObservableCollection<ToDoItem>();
            _filteredItems = new ObservableCollection<ToDoItem>();

            var view = CollectionViewSource.GetDefaultView(_filteredItems);
            view.GroupDescriptions.Add(new PropertyGroupDescription("SourceFile"));
            ToDoItemsControl.ItemsSource = view;

            LoadAllTodoFiles();
        }

        private async void LoadAllTodoFiles()
        {
            _allItems.Clear();
            
            try
            {
                // Find all .todo and .todo.archive files
                var todoFiles = Directory.GetFiles(_basePath, "*.todo", SearchOption.AllDirectories);
                var archiveFiles = Directory.GetFiles(_basePath, "*.todo.archive", SearchOption.AllDirectories);
                
                var allFiles = todoFiles.Concat(archiveFiles).ToList();
                
                System.Diagnostics.Debug.WriteLine($"Found {todoFiles.Length} .todo files and {archiveFiles.Length} .todo.archive files");
                
                // Get the configuration service
                var configService = ServiceLocator.Instance.GetService<IConfigurationService>();
                
                foreach (var filePath in allFiles)
                {
                    try
                    {
                        bool isArchived = filePath.EndsWith(".archive");
                        var relativePath = Path.GetRelativePath(_basePath, filePath);
                        string sourceFileDisplay = isArchived 
                            ? $"{relativePath} (Archived)" 
                            : relativePath;
                        
                        // Create a temporary service to read this specific file
                        var todoService = new ToDoService(configService, filePath);
                        var todos = await todoService.GetAllTodosAsync();
                        
                        foreach (var todo in todos)
                        {
                            var item = new ToDoItem
                            {
                                Title = todo.Title,
                                Description = todo.Description,
                                AdditionalInfo = new[] { 
                                    todo.Description ?? "", 
                                    todo.Category ?? "", 
                                    todo.Notes ?? "", 
                                    todo.Priority ?? "", 
                                    todo.AssignedTo ?? "" 
                                },
                                StartDate = todo.StartDate,
                                DueDate = todo.DueDate,
                                IsCompleted = todo.IsCompleted,
                                SourceFile = sourceFileDisplay,
                                IsArchived = isArchived
                            };
                            
                            System.Diagnostics.Debug.WriteLine($"Added todo: {item.Title}, Completed: {item.IsCompleted}, Archived: {item.IsArchived}");
                            _allItems.Add(item);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading todos from {filePath}: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                    }
                }

                UpdateFilteredItems();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning for todo files: {ex.Message}");
                MessageBox.Show($"Error loading todo files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateFilteredItems()
        {
            if (_allItems == null) return;

            // Apply both filters - future items and completed items
            var filtered = _allItems.Where(item => 
                // Future items filter
                (!HideFutureItems || !item.StartDate.HasValue || item.StartDate.Value.Date <= DateTime.Today) &&
                // Completed items filter
                (ShowCompletedItems || !item.IsCompleted)
            );

            System.Diagnostics.Debug.WriteLine($"Filtered from {_allItems.Count} to {filtered.Count()} items");
            
            _filteredItems.Clear();
            foreach (var item in filtered)
            {
                _filteredItems.Add(item);
            }
        }

        public void Refresh()
        {
            LoadAllTodoFiles();
        }
    }
}
