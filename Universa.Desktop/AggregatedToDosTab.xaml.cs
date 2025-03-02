using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Newtonsoft.Json;
using Universa.Desktop.Models;

namespace Universa.Desktop
{
    public partial class AggregatedToDosTab : UserControl, INotifyPropertyChanged
    {
        private bool _hideFutureItems;
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

        private void LoadAllTodoFiles()
        {
            _allItems.Clear();
            var todoFiles = Directory.GetFiles(_basePath, "*.todo", SearchOption.AllDirectories);

            foreach (var file in todoFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var items = JsonConvert.DeserializeObject<List<ToDoItem>>(json) ?? new List<ToDoItem>();
                    var relativePath = Path.GetRelativePath(_basePath, file);
                    
                    foreach (var item in items)
                    {
                        item.SourceFile = relativePath;
                        _allItems.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading {file}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            UpdateFilteredItems();
        }

        private void UpdateFilteredItems()
        {
            if (_allItems == null) return;

            var filtered = _allItems.Where(item => 
                !HideFutureItems || 
                !item.StartDate.HasValue || 
                item.StartDate.Value.Date <= DateTime.Today);

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