using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Universa.Desktop.Models;

namespace Universa.Desktop.Dialogs
{
    public partial class DependencyDialog : Window
    {
        public DependencyItem SelectedDependency { get; private set; }

        public DependencyDialog(List<DependencyItem> availableDependencies)
        {
            InitializeComponent();
            DependenciesListBox.ItemsSource = availableDependencies;
            DependenciesListBox.DisplayMemberPath = "DisplayName";
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (DependenciesListBox.SelectedItem is DependencyItem selectedDependency)
            {
                SelectedDependency = selectedDependency;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Please select a dependency.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
} 