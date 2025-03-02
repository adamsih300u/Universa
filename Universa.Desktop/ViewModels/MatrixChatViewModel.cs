using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Universa.Desktop.Models;
using Universa.Desktop.Commands;
using System.Threading;
using System.Diagnostics;
using System.Windows;
using System.Runtime.CompilerServices;
using System.Linq;

namespace Universa.Desktop.ViewModels
{
    public class MatrixChatViewModel : INotifyPropertyChanged
    {
        private MatrixClient _matrixClient;
        private ObservableCollection<ChatRoom> _rooms;
        private ChatRoom _selectedRoom;
        private string _messageInput;
        private bool _isConnected;
        private ObservableCollection<MatrixMessage> _messages;

        public ObservableCollection<ChatRoom> Rooms
        {
            get => _rooms;
            set
            {
                _rooms = value;
                OnPropertyChanged();
            }
        }

        public ChatRoom SelectedRoom
        {
            get => _selectedRoom;
            set
            {
                if (_selectedRoom != value)
                {
                    _selectedRoom = value;
                    OnPropertyChanged();
                    LoadMessages();
                }
            }
        }

        public string MessageInput
        {
            get => _messageInput;
            set
            {
                _messageInput = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ObservableCollection<MatrixMessage> Messages
        {
            get => _messages;
            set
            {
                _messages = value;
                OnPropertyChanged();
            }
        }

        public ICommand SendMessageCommand { get; }
        public ICommand RefreshRoomsCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand VerifyDeviceCommand { get; }

        public MatrixChatViewModel()
        {
            _rooms = new ObservableCollection<ChatRoom>();
            _messages = new ObservableCollection<MatrixMessage>();
            
            var config = Configuration.Instance;
            if (!string.IsNullOrEmpty(config.MatrixServerUrl))
            {
                _matrixClient = new MatrixClient(config.MatrixServerUrl);
            }

            SendMessageCommand = new AsyncRelayCommand(SendMessage, CanSendMessage);
            RefreshRoomsCommand = new AsyncRelayCommand(LoadRooms);
            ConnectCommand = new AsyncRelayCommand(Connect);
            DisconnectCommand = new AsyncRelayCommand(Disconnect);
            VerifyDeviceCommand = new AsyncRelayCommand(VerifyDevice, () => IsConnected);

            // Add message handler
            if (_matrixClient != null)
            {
                _matrixClient.AddRoomMessageHandler(HandleRoomMessage);
            }
        }

        private bool CanSendMessage()
        {
            return IsConnected && SelectedRoom != null && !string.IsNullOrWhiteSpace(MessageInput);
        }

        private async Task SendMessage()
        {
            if (SelectedRoom == null || string.IsNullOrWhiteSpace(MessageInput))
                return;

            string messageToSend = MessageInput;
            try
            {
                if (!IsConnected)
                {
                    await Connect();
                    if (!IsConnected)
                    {
                        MessageBox.Show("Not connected to Matrix server. Please check your connection and try again.", 
                            "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                MessageInput = string.Empty; // Clear input immediately for better UX

                await _matrixClient.SendMessage(SelectedRoom.Id, messageToSend);
                
                // Add message to local list immediately for better UX
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Messages.Add(new MatrixMessage
                    {
                        Content = messageToSend,
                        Sender = Configuration.Instance.MatrixUsername,
                        Timestamp = DateTime.Now
                    });
                });

                // Refresh messages to ensure consistency
                await LoadMessages();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending message: {ex.Message}");
                MessageBox.Show("Failed to send message. Please check your connection and try again.", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Restore message input if send failed
                MessageInput = messageToSend;
            }
        }

        private async Task LoadRooms()
        {
            try
            {
                var rooms = await _matrixClient.GetRooms();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Rooms.Clear();
                    foreach (var room in rooms)
                    {
                        Rooms.Add(room);
                    }
                });

                // If we have rooms but none selected, select the first one
                if (Rooms.Any() && SelectedRoom == null)
                {
                    SelectedRoom = Rooms[0];
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading rooms: {ex.Message}");
                MessageBox.Show("Failed to load rooms. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadMessages()
        {
            if (SelectedRoom == null)
                return;

            try
            {
                var messages = await _matrixClient.GetRoomMessages(SelectedRoom.Id);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Messages.Clear();
                    foreach (var message in messages.OrderBy(m => m.Timestamp))
                    {
                        Messages.Add(message);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading messages: {ex.Message}");
                MessageBox.Show("Failed to load messages. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task Connect()
        {
            if (IsConnected) return;

            try
            {
                var config = Configuration.Instance;
                if (string.IsNullOrEmpty(config.MatrixServerUrl) || 
                    string.IsNullOrEmpty(config.MatrixUsername) || 
                    string.IsNullOrEmpty(config.MatrixPassword))
                {
                    MessageBox.Show("Please configure Matrix settings first.", "Configuration Required", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Initialize client with server URL if not already done
                if (_matrixClient == null)
                {
                    _matrixClient = new MatrixClient(config.MatrixServerUrl);
                }

                // Register for room message events
                _matrixClient.AddRoomMessageHandler((roomId, message) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (SelectedRoom?.Id == roomId)
                        {
                            Messages.Add(message);
                        }
                    });
                });

                await _matrixClient.Login(config.MatrixUsername, config.MatrixPassword);
                IsConnected = true;
                await LoadRooms();

                // Start sync after login
                await _matrixClient.StartSync();

                // Start periodic room refresh
                StartRoomRefresh();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error connecting: {ex.Message}");
                MessageBox.Show("Failed to connect. Please check your settings and try again.", 
                    "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                IsConnected = false;
            }
        }

        private CancellationTokenSource _refreshCancellation;
        private async void StartRoomRefresh()
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation = new CancellationTokenSource();
            var token = _refreshCancellation.Token;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(30000, token); // Refresh every 30 seconds
                    if (!token.IsCancellationRequested)
                    {
                        await LoadRooms();
                        if (SelectedRoom != null)
                        {
                            await LoadMessages();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in room refresh: {ex.Message}");
                }
            }
        }

        private async Task Disconnect()
        {
            if (_matrixClient != null)
            {
                _matrixClient.StopSync();
                _matrixClient.Dispose();
                _matrixClient = null;
            }

            _refreshCancellation?.Cancel();
            _refreshCancellation = null;

            IsConnected = false;
            Messages.Clear();
            Rooms.Clear();
            SelectedRoom = null;

            await Task.CompletedTask; // Ensure method returns a Task
        }

        private void HandleRoomMessage(string roomId, MatrixMessage message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (SelectedRoom?.Id == roomId)
                {
                    Messages.Add(message);
                }
            });
        }

        private async Task VerifyDevice()
        {
            try
            {
                // Ensure we're connected first
                if (!IsConnected)
                {
                    await Connect();
                    if (!IsConnected)
                    {
                        MessageBox.Show("Please connect to Matrix first.", "Not Connected", 
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                Debug.WriteLine("Getting devices for verification...");
                var devices = await _matrixClient.GetDevices();
                Debug.WriteLine($"Found {devices.Count} devices");
                
                // Show device selection dialog
                var deviceSelectionWindow = new Views.DeviceSelectionWindow(devices);
                deviceSelectionWindow.Owner = Application.Current.MainWindow;
                if (deviceSelectionWindow.ShowDialog() == true)
                {
                    var selectedDevice = deviceSelectionWindow.SelectedDevice;
                    Debug.WriteLine($"Starting verification with device {selectedDevice.DeviceId}");
                    await _matrixClient.StartVerificationWithDevice(selectedDevice.DeviceId);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting device verification: {ex.Message}");
                MessageBox.Show("Failed to start device verification: " + ex.Message, "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 