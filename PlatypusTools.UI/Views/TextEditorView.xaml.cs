using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Converts bool to TextWrapping (true=Wrap, false=NoWrap).
    /// </summary>
    public class BoolToTextWrappingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? TextWrapping.Wrap : TextWrapping.NoWrap;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is TextWrapping.Wrap;
    }

    /// <summary>
    /// Converts non-null to true, null to false.
    /// </summary>
    public class NotNullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public partial class TextEditorView : UserControl
    {
        public TextEditorView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is ViewModels.TextEditorViewModel vm)
            {
                vm.SelectionRequested += OnSelectionRequested;
                vm.GoToLineRequested += OnGoToLineRequested;
            }
            if (e.OldValue is ViewModels.TextEditorViewModel oldVm)
            {
                oldVm.SelectionRequested -= OnSelectionRequested;
                oldVm.GoToLineRequested -= OnGoToLineRequested;
            }
        }

        private void OnSelectionRequested(int startIndex, int length)
        {
            Dispatcher.InvokeAsync(() =>
            {
                EditorTextBox.Focus();
                if (startIndex >= 0 && startIndex + length <= EditorTextBox.Text.Length)
                {
                    EditorTextBox.Select(startIndex, length);
                    // Scroll to selection
                    var rect = EditorTextBox.GetRectFromCharacterIndex(startIndex);
                    EditorTextBox.ScrollToLine(EditorTextBox.GetLineIndexFromCharacterIndex(startIndex));
                }
            });
        }

        private void OnGoToLineRequested(int lineNumber)
        {
            Dispatcher.InvokeAsync(() =>
            {
                EditorTextBox.Focus();
                var lineIndex = Math.Max(0, lineNumber - 1);
                if (lineIndex < EditorTextBox.LineCount)
                {
                    var charIndex = EditorTextBox.GetCharacterIndexFromLineIndex(lineIndex);
                    EditorTextBox.CaretIndex = charIndex;
                    EditorTextBox.ScrollToLine(lineIndex);
                }
            });
        }

        private void OnSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.TextEditorViewModel vm && vm.ActiveDocument != null)
            {
                var caretIndex = EditorTextBox.CaretIndex;
                var lineIndex = EditorTextBox.GetLineIndexFromCharacterIndex(caretIndex);
                var charIndexOfLine = EditorTextBox.GetCharacterIndexFromLineIndex(lineIndex);

                vm.ActiveDocument.CaretLine = lineIndex + 1;
                vm.ActiveDocument.CaretColumn = caretIndex - charIndexOfLine + 1;
                vm.RefreshStatusInfo();
            }
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateLineNumbers();
        }

        private void UpdateLineNumbers()
        {
            if (DataContext is ViewModels.TextEditorViewModel vm && vm.ShowLineNumbers && vm.ActiveDocument != null)
            {
                var lineCount = vm.ActiveDocument.LineCount;
                var lines = Enumerable.Range(1, lineCount).Select(i => i.ToString());
                LineNumbersBlock.Text = string.Join("\n", lines);
            }
        }

        private void OnTabClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ViewModels.EditorDocumentViewModel doc)
            {
                if (DataContext is ViewModels.TextEditorViewModel vm)
                {
                    vm.ActiveDocument = doc;
                }
            }
        }

        private void OnCloseTabClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ViewModels.EditorDocumentViewModel doc)
            {
                if (DataContext is ViewModels.TextEditorViewModel vm)
                {
                    vm.CloseTab(doc);
                }
            }
        }

        private void OnEncodingChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is ViewModels.TextEditorViewModel vm && EncodingCombo.SelectedItem is ComboBoxItem item)
            {
                vm.ChangeEncodingCommand.Execute(item.Content?.ToString());
            }
        }

        private void OnFileDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && DataContext is ViewModels.TextEditorViewModel vm)
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    vm.OpenFilePath(file);
                }
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }
    }
}
