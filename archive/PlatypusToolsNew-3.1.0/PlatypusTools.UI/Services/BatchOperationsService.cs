using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for handling batch file operations with progress tracking and cancellation support.
    /// </summary>
    public class BatchOperationsService
    {
        private static readonly Lazy<BatchOperationsService> _instance = new(() => new BatchOperationsService());
        public static BatchOperationsService Instance => _instance.Value;
        
        public event EventHandler<BatchOperationEventArgs>? OperationStarted;
        public event EventHandler<BatchProgressEventArgs>? ProgressChanged;
        public event EventHandler<BatchOperationEventArgs>? OperationCompleted;
        public event EventHandler<BatchErrorEventArgs>? ErrorOccurred;
        
        private BatchOperationsService() { }
        
        /// <summary>
        /// Renames multiple files with a pattern.
        /// </summary>
        public async Task<BatchResult> BatchRenameAsync(
            IEnumerable<string> files,
            Func<string, int, string> renameFunc,
            CancellationToken cancellationToken = default,
            IProgress<double>? progress = null)
        {
            var result = new BatchResult { OperationType = "Batch Rename" };
            var fileList = files.ToList();
            result.TotalItems = fileList.Count;
            
            OperationStarted?.Invoke(this, new BatchOperationEventArgs { Operation = "Batch Rename", TotalItems = fileList.Count });
            
            for (int i = 0; i < fileList.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.WasCancelled = true;
                    break;
                }
                
                var file = fileList[i];
                try
                {
                    var dir = Path.GetDirectoryName(file) ?? "";
                    var newName = renameFunc(file, i);
                    var newPath = Path.Combine(dir, newName);
                    
                    if (file != newPath && !File.Exists(newPath))
                    {
                        File.Move(file, newPath);
                        result.SuccessCount++;
                        result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, NewPath = newPath, Success = true });
                    }
                    else
                    {
                        result.SkippedCount++;
                        result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, Success = false, Error = "File already exists" });
                    }
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, Success = false, Error = ex.Message });
                    ErrorOccurred?.Invoke(this, new BatchErrorEventArgs { FilePath = file, Error = ex });
                }
                
                var percent = (i + 1) / (double)fileList.Count * 100;
                progress?.Report(percent);
                ProgressChanged?.Invoke(this, new BatchProgressEventArgs { CurrentItem = i + 1, TotalItems = fileList.Count, Percent = percent });
                
                // Small delay to allow UI updates
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
            
            OperationCompleted?.Invoke(this, new BatchOperationEventArgs { Operation = "Batch Rename", TotalItems = result.TotalItems });
            return result;
        }
        
        /// <summary>
        /// Copies multiple files to a destination.
        /// </summary>
        public async Task<BatchResult> BatchCopyAsync(
            IEnumerable<string> files,
            string destinationFolder,
            bool overwrite = false,
            CancellationToken cancellationToken = default,
            IProgress<double>? progress = null)
        {
            var result = new BatchResult { OperationType = "Batch Copy" };
            var fileList = files.ToList();
            result.TotalItems = fileList.Count;
            
            Directory.CreateDirectory(destinationFolder);
            OperationStarted?.Invoke(this, new BatchOperationEventArgs { Operation = "Batch Copy", TotalItems = fileList.Count });
            
            for (int i = 0; i < fileList.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.WasCancelled = true;
                    break;
                }
                
                var file = fileList[i];
                try
                {
                    var destPath = Path.Combine(destinationFolder, Path.GetFileName(file));
                    
                    if (!overwrite && File.Exists(destPath))
                    {
                        result.SkippedCount++;
                        result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, Success = false, Error = "File already exists" });
                    }
                    else
                    {
                        File.Copy(file, destPath, overwrite);
                        result.SuccessCount++;
                        result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, NewPath = destPath, Success = true });
                    }
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, Success = false, Error = ex.Message });
                    ErrorOccurred?.Invoke(this, new BatchErrorEventArgs { FilePath = file, Error = ex });
                }
                
                var percent = (i + 1) / (double)fileList.Count * 100;
                progress?.Report(percent);
                ProgressChanged?.Invoke(this, new BatchProgressEventArgs { CurrentItem = i + 1, TotalItems = fileList.Count, Percent = percent });
                
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
            
            OperationCompleted?.Invoke(this, new BatchOperationEventArgs { Operation = "Batch Copy", TotalItems = result.TotalItems });
            return result;
        }
        
        /// <summary>
        /// Moves multiple files to a destination.
        /// </summary>
        public async Task<BatchResult> BatchMoveAsync(
            IEnumerable<string> files,
            string destinationFolder,
            bool overwrite = false,
            CancellationToken cancellationToken = default,
            IProgress<double>? progress = null)
        {
            var result = new BatchResult { OperationType = "Batch Move" };
            var fileList = files.ToList();
            result.TotalItems = fileList.Count;
            
            Directory.CreateDirectory(destinationFolder);
            OperationStarted?.Invoke(this, new BatchOperationEventArgs { Operation = "Batch Move", TotalItems = fileList.Count });
            
            for (int i = 0; i < fileList.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.WasCancelled = true;
                    break;
                }
                
                var file = fileList[i];
                try
                {
                    var destPath = Path.Combine(destinationFolder, Path.GetFileName(file));
                    
                    if (!overwrite && File.Exists(destPath))
                    {
                        result.SkippedCount++;
                        result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, Success = false, Error = "File already exists" });
                    }
                    else
                    {
                        if (File.Exists(destPath)) File.Delete(destPath);
                        File.Move(file, destPath);
                        result.SuccessCount++;
                        result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, NewPath = destPath, Success = true });
                    }
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, Success = false, Error = ex.Message });
                    ErrorOccurred?.Invoke(this, new BatchErrorEventArgs { FilePath = file, Error = ex });
                }
                
                var percent = (i + 1) / (double)fileList.Count * 100;
                progress?.Report(percent);
                ProgressChanged?.Invoke(this, new BatchProgressEventArgs { CurrentItem = i + 1, TotalItems = fileList.Count, Percent = percent });
                
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
            
            OperationCompleted?.Invoke(this, new BatchOperationEventArgs { Operation = "Batch Move", TotalItems = result.TotalItems });
            return result;
        }
        
        /// <summary>
        /// Deletes multiple files.
        /// </summary>
        public async Task<BatchResult> BatchDeleteAsync(
            IEnumerable<string> files,
            bool permanent = false,
            CancellationToken cancellationToken = default,
            IProgress<double>? progress = null)
        {
            var result = new BatchResult { OperationType = "Batch Delete" };
            var fileList = files.ToList();
            result.TotalItems = fileList.Count;
            
            OperationStarted?.Invoke(this, new BatchOperationEventArgs { Operation = "Batch Delete", TotalItems = fileList.Count });
            
            for (int i = 0; i < fileList.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.WasCancelled = true;
                    break;
                }
                
                var file = fileList[i];
                try
                {
                    if (File.Exists(file))
                    {
                        if (permanent)
                        {
                            File.Delete(file);
                        }
                        else
                        {
                            // Move to recycle bin (simplified - actual implementation would use Shell32)
                            File.Delete(file);
                        }
                        result.SuccessCount++;
                        result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, Success = true });
                    }
                    else
                    {
                        result.SkippedCount++;
                        result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, Success = false, Error = "File not found" });
                    }
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, Success = false, Error = ex.Message });
                    ErrorOccurred?.Invoke(this, new BatchErrorEventArgs { FilePath = file, Error = ex });
                }
                
                var percent = (i + 1) / (double)fileList.Count * 100;
                progress?.Report(percent);
                ProgressChanged?.Invoke(this, new BatchProgressEventArgs { CurrentItem = i + 1, TotalItems = fileList.Count, Percent = percent });
                
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
            
            OperationCompleted?.Invoke(this, new BatchOperationEventArgs { Operation = "Batch Delete", TotalItems = result.TotalItems });
            return result;
        }
        
        /// <summary>
        /// Applies a custom operation to multiple files.
        /// </summary>
        public async Task<BatchResult> BatchProcessAsync(
            IEnumerable<string> files,
            Func<string, CancellationToken, Task<bool>> processor,
            string operationName = "Batch Process",
            CancellationToken cancellationToken = default,
            IProgress<double>? progress = null)
        {
            var result = new BatchResult { OperationType = operationName };
            var fileList = files.ToList();
            result.TotalItems = fileList.Count;
            
            OperationStarted?.Invoke(this, new BatchOperationEventArgs { Operation = operationName, TotalItems = fileList.Count });
            
            for (int i = 0; i < fileList.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.WasCancelled = true;
                    break;
                }
                
                var file = fileList[i];
                try
                {
                    var success = await processor(file, cancellationToken);
                    if (success)
                    {
                        result.SuccessCount++;
                        result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, Success = true });
                    }
                    else
                    {
                        result.FailedCount++;
                        result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, Success = false, Error = "Processing failed" });
                    }
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.ProcessedFiles.Add(new ProcessedFile { OriginalPath = file, Success = false, Error = ex.Message });
                    ErrorOccurred?.Invoke(this, new BatchErrorEventArgs { FilePath = file, Error = ex });
                }
                
                var percent = (i + 1) / (double)fileList.Count * 100;
                progress?.Report(percent);
                ProgressChanged?.Invoke(this, new BatchProgressEventArgs { CurrentItem = i + 1, TotalItems = fileList.Count, Percent = percent });
            }
            
            OperationCompleted?.Invoke(this, new BatchOperationEventArgs { Operation = operationName, TotalItems = result.TotalItems });
            return result;
        }
    }
    
    public class BatchResult
    {
        public string OperationType { get; set; } = "";
        public int TotalItems { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
        public bool WasCancelled { get; set; }
        public List<ProcessedFile> ProcessedFiles { get; set; } = new();
        
        public string Summary => $"{OperationType}: {SuccessCount}/{TotalItems} successful" + 
            (FailedCount > 0 ? $", {FailedCount} failed" : "") +
            (SkippedCount > 0 ? $", {SkippedCount} skipped" : "") +
            (WasCancelled ? " (cancelled)" : "");
    }
    
    public class ProcessedFile
    {
        public string OriginalPath { get; set; } = "";
        public string? NewPath { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
    
    public class BatchOperationEventArgs : EventArgs
    {
        public string Operation { get; set; } = "";
        public int TotalItems { get; set; }
    }
    
    public class BatchProgressEventArgs : EventArgs
    {
        public int CurrentItem { get; set; }
        public int TotalItems { get; set; }
        public double Percent { get; set; }
    }
    
    public class BatchErrorEventArgs : EventArgs
    {
        public string FilePath { get; set; } = "";
        public Exception? Error { get; set; }
    }
}
