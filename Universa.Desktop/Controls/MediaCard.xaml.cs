using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Universa.Desktop.Controls
{
    public partial class MediaCard : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty ImageSourceProperty =
            DependencyProperty.Register("ImageSource", typeof(string), typeof(MediaCard), new PropertyMetadata(null));

        public static readonly DependencyProperty MediaTypeProperty =
            DependencyProperty.Register("MediaType", typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty OverviewProperty =
            DependencyProperty.Register("Overview", typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string ImageSource
        {
            get => (string)GetValue(ImageSourceProperty);
            set => SetValue(ImageSourceProperty, value);
        }

        public string MediaType
        {
            get => (string)GetValue(MediaTypeProperty);
            set => SetValue(MediaTypeProperty, value);
        }

        public string Overview
        {
            get => (string)GetValue(OverviewProperty);
            set => SetValue(OverviewProperty, value);
        }

        public event RoutedEventHandler Clicked;

        public MediaCard()
        {
            InitializeComponent();
        }

        private void UserControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Clicked?.Invoke(this, new RoutedEventArgs());
        }
    }
} 