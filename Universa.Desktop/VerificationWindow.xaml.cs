using System;
using System.Collections.Generic;
using System.Windows;
using System.Threading.Tasks;
using System.ComponentModel;
using Universa.Desktop.Models;

namespace Universa.Desktop
{
    public partial class VerificationWindow : Window
    {
        private readonly VerificationSession _session;
        private readonly MatrixClient _client;

        public VerificationWindow(VerificationSession session, MatrixClient client)
        {
            InitializeComponent();
            _session = session;
            _client = client;
            _session.StateChanged += OnVerificationStateChanged;
            
            System.Diagnostics.Debug.WriteLine($"Opening verification window for session {session.TransactionId}");
            UpdateUI();
        }

        private void OnVerificationStateChanged(object sender, VerificationState newState)
        {
            System.Diagnostics.Debug.WriteLine($"Verification state changed to {newState}");
            Dispatcher.Invoke(UpdateUI);
        }

        private void UpdateUI()
        {
            System.Diagnostics.Debug.WriteLine($"Updating UI for state {_session.State}");
            
            switch (_session.State)
            {
                case VerificationState.Requested:
                    InstructionsText.Text = "Waiting for the other device to accept the verification request...";
                    EmojiPanel.Visibility = Visibility.Collapsed;
                    ConfirmButton.IsEnabled = false;
                    break;
                    
                case VerificationState.Started:
                    InstructionsText.Text = "Verification started. Exchanging keys...";
                    EmojiPanel.Visibility = Visibility.Collapsed;
                    ConfirmButton.IsEnabled = false;
                    break;
                    
                case VerificationState.KeysExchanged:
                    InstructionsText.Text = "Keys exchanged. Generating verification display...";
                    EmojiPanel.Visibility = Visibility.Collapsed;
                    ConfirmButton.IsEnabled = false;
                    break;
                    
                case VerificationState.KeysVerified:
                    InstructionsText.Text = "Compare these emojis with the other device.\nThey should be exactly the same.";
                    EmojiPanel.Visibility = Visibility.Visible;
                    ConfirmButton.IsEnabled = true;
                    DisplayEmojis();
                    break;
                    
                case VerificationState.Completed:
                    InstructionsText.Text = "Verification completed successfully!";
                    EmojiPanel.Visibility = Visibility.Collapsed;
                    ConfirmButton.IsEnabled = false;
                    CancelButton.Content = "Close";
                    break;
                    
                case VerificationState.Cancelled:
                    InstructionsText.Text = $"Verification cancelled: {_session.CancellationReason}";
                    EmojiPanel.Visibility = Visibility.Collapsed;
                    ConfirmButton.IsEnabled = false;
                    CancelButton.Content = "Close";
                    break;
            }
        }

        private void DisplayEmojis()
        {
            if (_session.Emojis != null)
            {
                EmojiList.ItemsSource = _session.Emojis;
            }
        }

        private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("User confirmed verification");
                ConfirmButton.IsEnabled = false;
                await _client.ConfirmVerification(_session.TransactionId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error confirming verification: {ex.Message}");
                MessageBox.Show("Failed to confirm verification: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_session.State != VerificationState.Completed && _session.State != VerificationState.Cancelled)
            {
                System.Diagnostics.Debug.WriteLine("User cancelled verification");
                await _client.CancelVerification(_session.TransactionId, "User cancelled");
            }
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _session.StateChanged -= OnVerificationStateChanged;
            base.OnClosing(e);
        }
    }
} 