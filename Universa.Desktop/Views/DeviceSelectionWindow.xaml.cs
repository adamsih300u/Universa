using System.Collections.Generic;
using System.Windows;
using Universa.Desktop.Models;

namespace Universa.Desktop.Views
{
    public partial class DeviceSelectionWindow : Window
    {
        public MatrixDevice SelectedDevice { get; private set; }

        public DeviceSelectionWindow(IEnumerable<MatrixDevice> devices)
        {
            InitializeComponent();
            DevicesList.ItemsSource = devices;
        }

        private void VerifyButton_Click(object sender, RoutedEventArgs e)
        {
            if (DevicesList.SelectedItem is MatrixDevice selectedDevice)
            {
                SelectedDevice = selectedDevice;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Please select a device to verify.", "No Device Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
} 