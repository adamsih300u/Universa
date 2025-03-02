using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Universa.Desktop.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Linq;

namespace Universa.Desktop
{
    public partial class ChatTab : UserControl
    {
        private readonly MatrixClient _matrixClient;
        private ObservableCollection<ChatRoom> _rooms;
        private ChatRoom _selectedRoom;
        private ObservableCollection<MatrixMessage> _messages;
        private UserControl _navigator;
        private string _messageText;
        private string _searchText;
        private ObservableCollection<ChatRoom> _allRooms;

        public ChatTab()
        {
            InitializeComponent();
            _matrixClient = new MatrixClient(Configuration.Instance.MatrixServerUrl);
            _rooms = new ObservableCollection<ChatRoom>();
            _allRooms = new ObservableCollection<ChatRoom>();
            _messages = new ObservableCollection<MatrixMessage>();
            
            DataContext = this;
            
            // Start loading rooms immediately
            Task.Run(async () => await Initialize());
        }

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
                    RoomSelectionChanged();
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

        public string MessageText
        {
            get => _messageText;
            set
            {
                _messageText = value;
                OnPropertyChanged();
            }
        }

        public UserControl Navigator
        {
            get => _navigator;
            private set
            {
                _navigator = value;
                OnPropertyChanged();
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                FilterRooms();
            }
        }

        private async Task LoadRooms()
        {
            try
            {
                var rooms = await _matrixClient.GetRooms();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _allRooms.Clear();
                    foreach (var room in rooms)
                    {
                        _allRooms.Add(room);
                    }
                    FilterRooms(); // This will update the Rooms collection
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading rooms: {ex.Message}");
                MessageBox.Show("Failed to load rooms. Please try again.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadMessages()
        {
            if (SelectedRoom == null) return;

            try
            {
                var messages = await _matrixClient.GetRoomMessages(SelectedRoom.Id);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Messages.Clear();
                    foreach (var message in messages)
                    {
                        Messages.Add(message);
                    }
                    ScrollToBottom();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading messages: {ex.Message}");
                MessageBox.Show("Failed to load messages. Please try again.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ScrollToBottom()
        {
            if (MessageScroller != null)
            {
                MessageScroller.ScrollToBottom();
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendMessage();
        }

        private async void MessageInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    // Let Shift+Enter create a new line (default behavior)
                    return;
                }
                else
                {
                    // Enter without Shift sends the message
                    e.Handled = true;
                    await SendMessage();
                }
            }
        }

        private async Task SendMessage()
        {
            if (SelectedRoom == null || string.IsNullOrWhiteSpace(MessageText))
                return;

            try
            {
                var messageId = await _matrixClient.SendMessage(SelectedRoom.Id, MessageText);
                var sender = _matrixClient.UserId;
                if (sender.StartsWith("@"))
                {
                    var colonIndex = sender.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        sender = sender.Substring(1, colonIndex - 1);
                    }
                }
                
                var message = MatrixMessage.CreateUserMessage(
                    messageId,
                    sender,
                    MessageText
                );
                Messages.Add(message);
                MessageText = string.Empty;
                ScrollToBottom();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending message: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RoomList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is ChatRoom room)
            {
                SelectedRoom = room;
            }
        }

        private async void RoomSelectionChanged()
        {
            if (SelectedRoom != null)
            {
                try
                {
                    // Load messages for the selected room
                    // This would need to be implemented based on the specific chat service
                    Messages.Clear();
                    // await LoadMessages(SelectedRoom.Id);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading messages: {ex.Message}");
                    MessageBox.Show("Failed to load messages. Please try again.", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public async Task Initialize()
        {
            await LoadRooms();
        }

        private void InitializeNavigator()
        {
            // Create and initialize the chat navigator
            var panel = new StackPanel();
            
            // Create search box with placeholder text
            var searchBox = new TextBox();
            var style = new Style(typeof(TextBox));
            style.Setters.Add(new Setter(TextBox.TemplateProperty, CreateWatermarkedTextBoxTemplate("Search Chat...")));
            searchBox.Style = style;
            
            // Add chat-specific navigation elements
            var contactsView = new ListView();
            
            panel.Children.Add(searchBox);
            panel.Children.Add(contactsView);
            
            _navigator = new UserControl { Content = panel };
        }

        private ControlTemplate CreateWatermarkedTextBoxTemplate(string watermarkText)
        {
            var template = new ControlTemplate(typeof(TextBox));
            var grid = new FrameworkElementFactory(typeof(Grid));

            // Create the actual TextBox part
            var textBox = new FrameworkElementFactory(typeof(ScrollViewer));
            textBox.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            textBox.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            textBox.SetValue(ScrollViewer.PaddingProperty, new Thickness(3));
            textBox.Name = "PART_ContentHost";

            // Create the watermark TextBlock
            var watermark = new FrameworkElementFactory(typeof(TextBlock));
            watermark.SetValue(TextBlock.TextProperty, watermarkText);
            watermark.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Colors.Gray));
            watermark.SetValue(TextBlock.MarginProperty, new Thickness(3, 0, 0, 0));
            watermark.SetValue(TextBlock.VisibilityProperty, 
                new Binding("Text.IsEmpty") 
                { 
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                    Converter = new BooleanToVisibilityConverter()
                });

            grid.AppendChild(textBox);
            grid.AppendChild(watermark);
            template.VisualTree = grid;

            return template;
        }

        private void FilterRooms()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                Rooms.Clear();
                foreach (var room in _allRooms)
                {
                    Rooms.Add(room);
                }
            }
            else
            {
                var filteredRooms = _allRooms.Where(r => 
                    r.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    r.Topic?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true
                );
                
                Rooms.Clear();
                foreach (var room in filteredRooms)
                {
                    Rooms.Add(room);
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ChatNavigator : UserControl
    {
        public ChatNavigator()
        {
            // Initialize the chat navigator UI
            var panel = new StackPanel();
            
            // Add chat-specific navigation elements
            var contactsList = new ListView();
            var searchBox = new TextBox();
            
            var style = new Style(typeof(TextBox));
            style.Setters.Add(new Setter(TextBox.TemplateProperty, CreateWatermarkedTextBoxTemplate("Search Chats...")));
            searchBox.Style = style;
            
            panel.Children.Add(searchBox);
            panel.Children.Add(contactsList);
            
            Content = panel;
        }

        private ControlTemplate CreateWatermarkedTextBoxTemplate(string watermarkText)
        {
            var template = new ControlTemplate(typeof(TextBox));
            var grid = new FrameworkElementFactory(typeof(Grid));

            // Create the actual TextBox part
            var textBox = new FrameworkElementFactory(typeof(ScrollViewer));
            textBox.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            textBox.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            textBox.SetValue(ScrollViewer.PaddingProperty, new Thickness(3));
            textBox.Name = "PART_ContentHost";

            // Create the watermark TextBlock
            var watermark = new FrameworkElementFactory(typeof(TextBlock));
            watermark.SetValue(TextBlock.TextProperty, watermarkText);
            watermark.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Colors.Gray));
            watermark.SetValue(TextBlock.MarginProperty, new Thickness(3, 0, 0, 0));
            watermark.SetValue(TextBlock.VisibilityProperty, 
                new Binding("Text.IsEmpty") 
                { 
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                    Converter = new BooleanToVisibilityConverter()
                });

            grid.AppendChild(watermark);
            grid.AppendChild(textBox);
            template.VisualTree = grid;
            return template;
        }
    }
} 