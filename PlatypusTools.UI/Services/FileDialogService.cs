using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Unified file and folder dialog service.
    /// Eliminates duplicated dialog code across ViewModels.
    /// </summary>
    public static class FileDialogService
    {
        #region Common Filter Strings

        /// <summary>Video file filter for OpenFileDialog.</summary>
        public const string VideoFilter = "Video Files|*.mp4;*.mkv;*.mov;*.avi;*.ts;*.wmv;*.flv;*.webm|All Files|*.*";
        
        /// <summary>Audio file filter for OpenFileDialog.</summary>
        public const string AudioFilter = "Audio Files|*.mp3;*.wav;*.flac;*.aac;*.ogg;*.m4a;*.wma|All Files|*.*";
        
        /// <summary>Image file filter for OpenFileDialog.</summary>
        public const string ImageFilter = "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tiff;*.webp|All Files|*.*";
        
        /// <summary>Media file filter (video + audio + images).</summary>
        public const string MediaFilter = "Media Files|*.mp4;*.mkv;*.mov;*.avi;*.ts;*.mp3;*.wav;*.flac;*.png;*.jpg;*.jpeg;*.gif|All Files|*.*";
        
        /// <summary>PDF file filter.</summary>
        public const string PdfFilter = "PDF Files|*.pdf|All Files|*.*";
        
        /// <summary>All files filter.</summary>
        public const string AllFilesFilter = "All Files|*.*";

        #endregion

        #region Folder Dialog

        /// <summary>
        /// Shows a folder browser dialog and returns the selected path.
        /// </summary>
        /// <param name="description">Description text shown in the dialog.</param>
        /// <param name="initialPath">Initial folder to start in.</param>
        /// <param name="showNewFolderButton">Whether to show the New Folder button.</param>
        /// <returns>Selected folder path, or null if cancelled.</returns>
        public static string? BrowseForFolder(
            string description = "Select a folder",
            string? initialPath = null,
            bool showNewFolderButton = false)
        {
            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = description,
                ShowNewFolderButton = showNewFolderButton
            };

            if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
            {
                dialog.SelectedPath = initialPath;
            }

            return dialog.ShowDialog() == WinForms.DialogResult.OK
                ? dialog.SelectedPath
                : null;
        }

        #endregion

        #region Open File Dialog

        /// <summary>
        /// Shows an open file dialog for selecting a single file.
        /// </summary>
        /// <param name="filter">File type filter (e.g., VideoFilter, AudioFilter).</param>
        /// <param name="title">Dialog title.</param>
        /// <param name="initialDirectory">Initial directory to open.</param>
        /// <returns>Selected file path, or null if cancelled.</returns>
        public static string? OpenFile(
            string filter = AllFilesFilter,
            string title = "Open File",
            string? initialDirectory = null)
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter,
                Title = title,
                Multiselect = false
            };

            if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            return dialog.ShowDialog() == true
                ? dialog.FileName
                : null;
        }

        /// <summary>
        /// Shows an open file dialog for selecting multiple files.
        /// </summary>
        /// <param name="filter">File type filter (e.g., VideoFilter, AudioFilter).</param>
        /// <param name="title">Dialog title.</param>
        /// <param name="initialDirectory">Initial directory to open.</param>
        /// <returns>Array of selected file paths, or empty array if cancelled.</returns>
        public static string[] OpenFiles(
            string filter = AllFilesFilter,
            string title = "Open Files",
            string? initialDirectory = null)
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter,
                Title = title,
                Multiselect = true
            };

            if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            return dialog.ShowDialog() == true
                ? dialog.FileNames
                : Array.Empty<string>();
        }

        #endregion

        #region Save File Dialog

        /// <summary>
        /// Shows a save file dialog for selecting a save location.
        /// </summary>
        /// <param name="filter">File type filter.</param>
        /// <param name="title">Dialog title.</param>
        /// <param name="defaultFileName">Suggested file name.</param>
        /// <param name="initialDirectory">Initial directory to open.</param>
        /// <returns>Selected save path, or null if cancelled.</returns>
        public static string? SaveFile(
            string filter = AllFilesFilter,
            string title = "Save File",
            string? defaultFileName = null,
            string? initialDirectory = null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                Title = title,
                OverwritePrompt = true
            };

            if (!string.IsNullOrEmpty(defaultFileName))
            {
                dialog.FileName = defaultFileName;
            }

            if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            return dialog.ShowDialog() == true
                ? dialog.FileName
                : null;
        }

        #endregion

        #region Specialized Dialogs

        /// <summary>
        /// Opens a video file selection dialog.
        /// </summary>
        /// <param name="multiselect">Allow multiple selection.</param>
        /// <param name="initialDirectory">Initial directory.</param>
        /// <returns>Selected file(s) or null/empty if cancelled.</returns>
        public static string[] OpenVideoFiles(bool multiselect = true, string? initialDirectory = null)
        {
            return multiselect
                ? OpenFiles(VideoFilter, "Select Video Files", initialDirectory)
                : OpenFile(VideoFilter, "Select Video File", initialDirectory) is string file
                    ? new[] { file }
                    : Array.Empty<string>();
        }

        /// <summary>
        /// Opens an audio file selection dialog.
        /// </summary>
        /// <param name="multiselect">Allow multiple selection.</param>
        /// <param name="initialDirectory">Initial directory.</param>
        /// <returns>Selected file(s) or null/empty if cancelled.</returns>
        public static string[] OpenAudioFiles(bool multiselect = true, string? initialDirectory = null)
        {
            return multiselect
                ? OpenFiles(AudioFilter, "Select Audio Files", initialDirectory)
                : OpenFile(AudioFilter, "Select Audio File", initialDirectory) is string file
                    ? new[] { file }
                    : Array.Empty<string>();
        }

        /// <summary>
        /// Opens an image file selection dialog.
        /// </summary>
        /// <param name="multiselect">Allow multiple selection.</param>
        /// <param name="initialDirectory">Initial directory.</param>
        /// <returns>Selected file(s) or null/empty if cancelled.</returns>
        public static string[] OpenImageFiles(bool multiselect = true, string? initialDirectory = null)
        {
            return multiselect
                ? OpenFiles(ImageFilter, "Select Image Files", initialDirectory)
                : OpenFile(ImageFilter, "Select Image File", initialDirectory) is string file
                    ? new[] { file }
                    : Array.Empty<string>();
        }

        /// <summary>
        /// Opens a media file selection dialog (video + audio + images).
        /// </summary>
        /// <param name="multiselect">Allow multiple selection.</param>
        /// <param name="initialDirectory">Initial directory.</param>
        /// <returns>Selected file(s) or null/empty if cancelled.</returns>
        public static string[] OpenMediaFiles(bool multiselect = true, string? initialDirectory = null)
        {
            return multiselect
                ? OpenFiles(MediaFilter, "Select Media Files", initialDirectory)
                : OpenFile(MediaFilter, "Select Media File", initialDirectory) is string file
                    ? new[] { file }
                    : Array.Empty<string>();
        }

        /// <summary>
        /// Opens a PDF file selection dialog.
        /// </summary>
        /// <param name="multiselect">Allow multiple selection.</param>
        /// <param name="initialDirectory">Initial directory.</param>
        /// <returns>Selected file(s) or null/empty if cancelled.</returns>
        public static string[] OpenPdfFiles(bool multiselect = true, string? initialDirectory = null)
        {
            return multiselect
                ? OpenFiles(PdfFilter, "Select PDF Files", initialDirectory)
                : OpenFile(PdfFilter, "Select PDF File", initialDirectory) is string file
                    ? new[] { file }
                    : Array.Empty<string>();
        }

        /// <summary>
        /// Opens a folder browser for selecting an output/destination folder.
        /// </summary>
        /// <param name="initialPath">Initial folder path.</param>
        /// <returns>Selected folder path, or null if cancelled.</returns>
        public static string? BrowseForOutputFolder(string? initialPath = null)
        {
            return BrowseForFolder(
                description: "Select output folder",
                initialPath: initialPath,
                showNewFolderButton: true);
        }

        /// <summary>
        /// Opens a folder browser for selecting a source/input folder.
        /// </summary>
        /// <param name="initialPath">Initial folder path.</param>
        /// <returns>Selected folder path, or null if cancelled.</returns>
        public static string? BrowseForSourceFolder(string? initialPath = null)
        {
            return BrowseForFolder(
                description: "Select source folder",
                initialPath: initialPath,
                showNewFolderButton: false);
        }

        #endregion
    }
}
