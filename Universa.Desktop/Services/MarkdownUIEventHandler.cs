using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Universa.Desktop.Dialogs;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service for handling UI events in markdown editors
    /// </summary>
    public class MarkdownUIEventHandler : IMarkdownUIEventHandler
    {
        private readonly IMarkdownTTSService _ttsService;
        private readonly IMarkdownFontService _fontService;
        private readonly IMarkdownFileService _fileService;
        private readonly IChapterNavigationService _chapterNavigationService;
        
        private TextBox _editor;
        private Func<string> _getFilePath;
        private Action<bool> _setModified;
        private Action _showFrontmatterDialog;
        private Action _toggleFrontmatterVisibility;
        private Action _updateToggleButtonAppearance;
        private Action _refreshEditorContent;

        public event EventHandler<bool> ModifiedStateChanged;

        public MarkdownUIEventHandler(
            IMarkdownTTSService ttsService = null,
            IMarkdownFontService fontService = null,
            IMarkdownFileService fileService = null,
            IChapterNavigationService chapterNavigationService = null)
        {
            _ttsService = ttsService;
            _fontService = fontService;
            _fileService = fileService;
            _chapterNavigationService = chapterNavigationService;
        }

        public void Initialize(TextBox editor, Func<string> getFilePath, Action<bool> setModified)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _getFilePath = getFilePath ?? throw new ArgumentNullException(nameof(getFilePath));
            _setModified = setModified ?? throw new ArgumentNullException(nameof(setModified));
        }

        public void SetCallbacks(
            Action showFrontmatterDialog,
            Action toggleFrontmatterVisibility,
            Action updateToggleButtonAppearance,
            Action refreshEditorContent)
        {
            _showFrontmatterDialog = showFrontmatterDialog;
            _toggleFrontmatterVisibility = toggleFrontmatterVisibility;
            _updateToggleButtonAppearance = updateToggleButtonAppearance;
            _refreshEditorContent = refreshEditorContent;
        }

        public void HandleBoldButtonClick()
        {
            var selectionStart = _editor.SelectionStart;
            var selectionLength = _editor.SelectionLength;
            var selectedText = _editor.SelectedText;

            if (string.IsNullOrEmpty(selectedText))
            {
                // If no text is selected, insert bold markers at cursor position
                _editor.Text = _editor.Text.Insert(selectionStart, "****");
                _editor.SelectionStart = selectionStart + 2;
            }
            else
            {
                // Wrap selected text in bold markers
                _editor.Text = _editor.Text.Remove(selectionStart, selectionLength)
                                      .Insert(selectionStart, $"**{selectedText}**");
                _editor.SelectionStart = selectionStart;
                _editor.SelectionLength = selectedText.Length + 4;
            }
            
            _setModified(true);
            ModifiedStateChanged?.Invoke(this, true);
        }

        public void HandleItalicButtonClick()
        {
            var selectionStart = _editor.SelectionStart;
            var selectionLength = _editor.SelectionLength;
            var selectedText = _editor.SelectedText;

            if (string.IsNullOrEmpty(selectedText))
            {
                // If no text is selected, insert italic markers at cursor position
                _editor.Text = _editor.Text.Insert(selectionStart, "**");
                _editor.SelectionStart = selectionStart + 1;
            }
            else
            {
                // Wrap selected text in italic markers
                _editor.Text = _editor.Text.Remove(selectionStart, selectionLength)
                                      .Insert(selectionStart, $"*{selectedText}*");
                _editor.SelectionStart = selectionStart;
                _editor.SelectionLength = selectedText.Length + 2;
            }
            
            _setModified(true);
            ModifiedStateChanged?.Invoke(this, true);
        }

        public void HandleTTSButtonClick()
        {
            if (_ttsService == null) return;
            
            var textToSpeak = _ttsService.GetTextToSpeak(_editor.SelectedText, _editor.Text);
            _ttsService.StartTTS(textToSpeak);
        }

        public void HandleFrontmatterButtonClick()
        {
            _showFrontmatterDialog?.Invoke();
        }

        public void HandleToggleFrontmatterButtonClick()
        {
            _toggleFrontmatterVisibility?.Invoke();
            _updateToggleButtonAppearance?.Invoke();
            _refreshEditorContent?.Invoke();
        }

        public void HandleHeadingButtonClick(int level)
        {
            // Simplified: just insert the heading markup at cursor position
            var selectionStart = _editor.SelectionStart;
            string headingMarkup = new string('#', level) + " ";
            
            _editor.Text = _editor.Text.Insert(selectionStart, headingMarkup);
            _editor.SelectionStart = selectionStart + headingMarkup.Length;
            
            _setModified(true);
            ModifiedStateChanged?.Invoke(this, true);
        }

        public void HandleFontSelectionChanged(FontFamily selectedFont)
        {
            _fontService?.OnFontSelectionChanged(selectedFont, _editor, null);
        }

        public void HandleFontSizeSelectionChanged(double fontSize)
        {
            _fontService?.OnFontSizeSelectionChanged(fontSize, _editor, null);
        }

        public void HandleRefreshVersionsButtonClick()
        {
            // This method can be implemented to refresh versions if needed
            // For now, it's a placeholder to satisfy the interface
        }
    }
} 