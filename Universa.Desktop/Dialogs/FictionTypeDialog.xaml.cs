using System.Windows;

namespace Universa.Desktop.Dialogs
{
    /// <summary>
    /// Interaction logic for FictionTypeDialog.xaml
    /// </summary>
    public partial class FictionTypeDialog : Window
    {
        public FictionTypeDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Gets the default genre (since Genre selection was removed)
        /// </summary>
        public string Genre
        {
            get { return "Fiction"; }
        }

        /// <summary>
        /// Gets the series name from the text box
        /// </summary>
        public string SeriesName
        {
            get { return SeriesTextBox.Text?.Trim() ?? string.Empty; }
        }

        /// <summary>
        /// Gets the book number from the text box
        /// </summary>
        public string BookNumber
        {
            get { return BookNumberTextBox.Text?.Trim() ?? string.Empty; }
        }

        /// <summary>
        /// Gets the rules file path from the text box
        /// </summary>
        public string RulesFile
        {
            get { return RulesFileTextBox.Text?.Trim() ?? "rules.md"; }
        }

        /// <summary>
        /// Gets the style file path from the text box
        /// </summary>
        public string StyleFile
        {
            get { return StyleFileTextBox.Text?.Trim() ?? "style.md"; }
        }

        /// <summary>
        /// Gets the outline file path from the text box
        /// </summary>
        public string OutlineFile
        {
            get { return OutlineFileTextBox.Text?.Trim() ?? "outline.md"; }
        }

        /// <summary>
        /// Gets whether to create the rules file if missing
        /// </summary>
        public bool CreateRules
        {
            get { return CreateRulesCheckBox.IsChecked == true; }
        }

        /// <summary>
        /// Gets whether to create the style file if missing
        /// </summary>
        public bool CreateStyle
        {
            get { return CreateStyleCheckBox.IsChecked == true; }
        }

        /// <summary>
        /// Gets whether to create the outline file if missing
        /// </summary>
        public bool CreateOutline
        {
            get { return CreateOutlineCheckBox.IsChecked == true; }
        }

        /// <summary>
        /// Handles the OK button click event
        /// </summary>
        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Handles the Cancel button click event
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
} 