using System.Windows;

namespace Universa.Desktop.Dialogs
{
    public partial class NonFictionTypeDialog : Window
    {
        public string SelectedType { get; private set; } = "general";
        public string SubjectMatter { get; private set; } = "";
        public string TimePeriod { get; private set; } = "";

        public NonFictionTypeDialog()
        {
            InitializeComponent();
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            // Determine selected type
            if (BiographyRadio.IsChecked == true)
                SelectedType = "biography";
            else if (AutobiographyRadio.IsChecked == true)
                SelectedType = "autobiography";
            else if (HistoryRadio.IsChecked == true)
                SelectedType = "history";
            else if (AcademicRadio.IsChecked == true)
                SelectedType = "academic";
            else if (JournalismRadio.IsChecked == true)
                SelectedType = "journalism";
            else
                SelectedType = "general";

            // Get optional fields
            SubjectMatter = SubjectTextBox.Text?.Trim() ?? "";
            TimePeriod = TimePeriodTextBox.Text?.Trim() ?? "";

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
} 