using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Centralized service for handling dialogs and user prompts
    /// </summary>
    public sealed class DialogService
    {
        private static readonly Lazy<DialogService> _instance = new(() => new DialogService());
        public static DialogService Instance => _instance.Value;

        private Window? MainWindow => Application.Current?.MainWindow;

        private DialogService() { }

        #region Message Dialogs

        public void ShowInfo(string message, string? title = null)
        {
            MessageBox.Show(MainWindow, message, title ?? "Information", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowWarning(string message, string? title = null)
        {
            MessageBox.Show(MainWindow, message, title ?? "Warning", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void ShowError(string message, string? title = null)
        {
            MessageBox.Show(MainWindow, message, title ?? "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public bool ShowConfirmation(string message, string? title = null)
        {
            var result = MessageBox.Show(MainWindow, message, title ?? "Confirm", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }

        public bool? ShowYesNoCancel(string message, string? title = null)
        {
            var result = MessageBox.Show(MainWindow, message, title ?? "Confirm", 
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            return result switch
            {
                MessageBoxResult.Yes => true,
                MessageBoxResult.No => false,
                _ => null
            };
        }

        #endregion

        #region File Dialogs

        public string? ShowOpenFileDialog(string? filter = null, string? title = null, 
            string? initialDirectory = null)
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter ?? "All Files (*.*)|*.*",
                Title = title ?? "Open File"
            };
            
            if (!string.IsNullOrEmpty(initialDirectory))
                dialog.InitialDirectory = initialDirectory;

            return dialog.ShowDialog(MainWindow) == true ? dialog.FileName : null;
        }

        public string[]? ShowOpenFilesDialog(string? filter = null, string? title = null, 
            string? initialDirectory = null)
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter ?? "All Files (*.*)|*.*",
                Title = title ?? "Open Files",
                Multiselect = true
            };
            
            if (!string.IsNullOrEmpty(initialDirectory))
                dialog.InitialDirectory = initialDirectory;

            return dialog.ShowDialog(MainWindow) == true ? dialog.FileNames : null;
        }

        public string? ShowSaveFileDialog(string? filter = null, string? title = null, 
            string? defaultFileName = null, string? initialDirectory = null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter ?? "All Files (*.*)|*.*",
                Title = title ?? "Save File"
            };
            
            if (!string.IsNullOrEmpty(defaultFileName))
                dialog.FileName = defaultFileName;
            
            if (!string.IsNullOrEmpty(initialDirectory))
                dialog.InitialDirectory = initialDirectory;

            return dialog.ShowDialog(MainWindow) == true ? dialog.FileName : null;
        }

        public string? ShowFolderDialog(string? title = null, string? initialDirectory = null)
        {
            var dialog = new OpenFolderDialog
            {
                Title = title ?? "Select Folder"
            };
            
            if (!string.IsNullOrEmpty(initialDirectory))
                dialog.InitialDirectory = initialDirectory;

            return dialog.ShowDialog(MainWindow) == true ? dialog.FolderName : null;
        }

        #endregion

        #region Input Dialogs

        public string? ShowInputDialog(string prompt, string? title = null, 
            string? defaultValue = null)
        {
            var dialog = new Views.InputDialogWindow(prompt, defaultValue ?? "");
            dialog.Owner = MainWindow;
            return dialog.ShowDialog() == true ? dialog.EnteredText : null;
        }

        public string? ShowPasswordDialog(string prompt, string? title = null)
        {
            var dialog = new Views.PromptPasswordWindow(prompt);
            dialog.Owner = MainWindow;
            return dialog.ShowDialog() == true ? dialog.EnteredPassword : null;
        }

        #endregion

        #region Progress Dialogs

        public async Task RunWithProgressAsync(string title, Func<Views.ProgressDialog, Task> operation)
        {
            if (MainWindow != null)
            {
                await Views.ProgressDialog.RunWithProgressAsync(MainWindow, title, 
                    async (dialog, token) => await operation(dialog));
            }
        }

        public async Task<T?> RunWithProgressAsync<T>(string title, 
            Func<Views.ProgressDialog, Task<T>> operation)
        {
            if (MainWindow != null)
            {
                return await Views.ProgressDialog.RunWithProgressAsync(MainWindow, title, 
                    async (dialog, token) => await operation(dialog));
            }
            return default;
        }

        #endregion

        #region Custom Dialogs

        public bool ShowWindow<T>(Action<T>? configure = null) where T : Window, new()
        {
            var window = new T();
            configure?.Invoke(window);
            window.Owner = MainWindow;
            return window.ShowDialog() == true;
        }

        public void ShowNonModalWindow<T>(Action<T>? configure = null) where T : Window, new()
        {
            var window = new T();
            configure?.Invoke(window);
            window.Owner = MainWindow;
            window.Show();
        }

        #endregion
    }
}

