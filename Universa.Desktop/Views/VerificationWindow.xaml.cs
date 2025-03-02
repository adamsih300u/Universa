using System;
using System.Windows;
using Universa.Desktop.Models;

namespace Universa.Desktop.Views
{
    public partial class VerificationWindow : Window
    {
        private readonly VerificationSession _session;
        private readonly MatrixClient _matrixClient;

        public VerificationWindow(VerificationSession session, MatrixClient matrixClient)
        {
            InitializeComponent();
            _session = session;
            _matrixClient = matrixClient;

            // Subscribe to state changes
            _session.StateChanged += Session_StateChanged;

            // Update UI when emojis are available
            if (_session.Emojis != null)
            {
                EmojiList.ItemsSource = _session.Emojis;
            }
        }

        private void Session_StateChanged(object sender, VerificationState e)
        {
            // Update UI based on state changes
            Dispatcher.Invoke(() =>
            {
                switch (e)
                {
                    case VerificationState.WaitingForKey:
                        if (_session.Emojis != null)
                        {
                            EmojiList.ItemsSource = _session.Emojis;
                        }
                        break;
                    case VerificationState.Cancelled:
                        MessageBox.Show("Verification was cancelled.", "Verification Cancelled", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Close();
                        break;
                    case VerificationState.Completed:
                        MessageBox.Show("Device successfully verified!", "Verification Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        Close();
                        break;
                }
            });
        }

        private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _matrixClient.ConfirmVerification(_session.TransactionId);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error confirming verification: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _matrixClient.CancelVerification(_session.TransactionId, "Emojis did not match");
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cancelling verification: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
} 