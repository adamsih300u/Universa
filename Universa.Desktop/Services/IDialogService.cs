using System.Windows;

namespace Universa.Desktop.Services
{
    public interface IDialogService
    {
        bool ShowConfirmation(string message, string title);
        void ShowError(string message, string title);
        void ShowMessage(string message, string title);
        MessageBoxResult ShowQuestion(string message, string title);
    }

    public class DialogService : IDialogService
    {
        public bool ShowConfirmation(string message, string title)
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        public void ShowError(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public void ShowMessage(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public MessageBoxResult ShowQuestion(string message, string title)
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        }
    }
} 