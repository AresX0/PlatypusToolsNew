using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for managing undo/redo operations on file system changes.
    /// </summary>
    public class UndoRedoService
    {
        private static readonly Lazy<UndoRedoService> _instance = new(() => new UndoRedoService());
        public static UndoRedoService Instance => _instance.Value;
        
        private readonly Stack<FileOperation> _undoStack = new();
        private readonly Stack<FileOperation> _redoStack = new();
        private const int MaxStackSize = 100;
        
        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;
        
        public event EventHandler? StackChanged;
        
        private UndoRedoService() { }
        
        /// <summary>
        /// Records a file rename operation.
        /// </summary>
        public void RecordRename(string oldPath, string newPath)
        {
            RecordOperation(new FileOperation
            {
                Type = OperationType.Rename,
                OriginalPath = oldPath,
                NewPath = newPath,
                Timestamp = DateTime.Now
            });
        }
        
        /// <summary>
        /// Records a file move operation.
        /// </summary>
        public void RecordMove(string sourcePath, string destinationPath)
        {
            RecordOperation(new FileOperation
            {
                Type = OperationType.Move,
                OriginalPath = sourcePath,
                NewPath = destinationPath,
                Timestamp = DateTime.Now
            });
        }
        
        /// <summary>
        /// Records a file copy operation.
        /// </summary>
        public void RecordCopy(string sourcePath, string copiedPath)
        {
            RecordOperation(new FileOperation
            {
                Type = OperationType.Copy,
                OriginalPath = sourcePath,
                NewPath = copiedPath,
                Timestamp = DateTime.Now
            });
        }
        
        /// <summary>
        /// Records a file deletion (with backup for potential undo).
        /// </summary>
        public void RecordDelete(string path, string? backupPath = null)
        {
            RecordOperation(new FileOperation
            {
                Type = OperationType.Delete,
                OriginalPath = path,
                BackupPath = backupPath,
                Timestamp = DateTime.Now
            });
        }
        
        /// <summary>
        /// Records multiple operations as a batch.
        /// </summary>
        public void RecordBatch(IEnumerable<FileOperation> operations, string description)
        {
            var batch = new FileOperation
            {
                Type = OperationType.Batch,
                Description = description,
                BatchOperations = operations.ToList(),
                Timestamp = DateTime.Now
            };
            RecordOperation(batch);
        }
        
        /// <summary>
        /// Undoes the last operation.
        /// </summary>
        public async Task<bool> UndoAsync()
        {
            if (!CanUndo) return false;
            
            var operation = _undoStack.Pop();
            
            try
            {
                await UndoOperationAsync(operation);
                _redoStack.Push(operation);
                StackChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception)
            {
                // If undo fails, put it back
                _undoStack.Push(operation);
                throw;
            }
        }
        
        /// <summary>
        /// Redoes the last undone operation.
        /// </summary>
        public async Task<bool> RedoAsync()
        {
            if (!CanRedo) return false;
            
            var operation = _redoStack.Pop();
            
            try
            {
                await RedoOperationAsync(operation);
                _undoStack.Push(operation);
                StackChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception)
            {
                _redoStack.Push(operation);
                throw;
            }
        }
        
        /// <summary>
        /// Clears all undo/redo history.
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            StackChanged?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Gets the description of the last undoable operation.
        /// </summary>
        public string? GetUndoDescription()
        {
            if (!CanUndo) return null;
            return _undoStack.Peek().GetDescription();
        }
        
        /// <summary>
        /// Gets the description of the last redoable operation.
        /// </summary>
        public string? GetRedoDescription()
        {
            if (!CanRedo) return null;
            return _redoStack.Peek().GetDescription();
        }
        
        /// <summary>
        /// Gets recent operations for display.
        /// </summary>
        public IEnumerable<FileOperation> GetRecentOperations(int count = 10)
        {
            return _undoStack.Take(count);
        }
        
        private void RecordOperation(FileOperation operation)
        {
            _undoStack.Push(operation);
            _redoStack.Clear(); // Clear redo stack on new operation
            
            // Trim if too large
            while (_undoStack.Count > MaxStackSize)
            {
                var items = _undoStack.ToArray();
                _undoStack.Clear();
                foreach (var item in items.Take(MaxStackSize - 10).Reverse())
                {
                    _undoStack.Push(item);
                }
            }
            
            StackChanged?.Invoke(this, EventArgs.Empty);
        }
        
        private async Task UndoOperationAsync(FileOperation operation)
        {
            switch (operation.Type)
            {
                case OperationType.Rename:
                case OperationType.Move:
                    // Move back to original location
                    if (File.Exists(operation.NewPath))
                    {
                        File.Move(operation.NewPath!, operation.OriginalPath);
                    }
                    else if (Directory.Exists(operation.NewPath))
                    {
                        Directory.Move(operation.NewPath!, operation.OriginalPath);
                    }
                    break;
                    
                case OperationType.Copy:
                    // Delete the copy
                    if (File.Exists(operation.NewPath))
                    {
                        File.Delete(operation.NewPath!);
                    }
                    else if (Directory.Exists(operation.NewPath))
                    {
                        Directory.Delete(operation.NewPath!, true);
                    }
                    break;
                    
                case OperationType.Delete:
                    // Restore from backup if available
                    if (!string.IsNullOrEmpty(operation.BackupPath) && File.Exists(operation.BackupPath))
                    {
                        File.Move(operation.BackupPath, operation.OriginalPath);
                    }
                    break;
                    
                case OperationType.Batch:
                    // Undo in reverse order
                    foreach (var batchOp in operation.BatchOperations.AsEnumerable().Reverse())
                    {
                        await UndoOperationAsync(batchOp);
                    }
                    break;
            }
            
            await Task.CompletedTask;
        }
        
        private async Task RedoOperationAsync(FileOperation operation)
        {
            switch (operation.Type)
            {
                case OperationType.Rename:
                case OperationType.Move:
                    if (File.Exists(operation.OriginalPath))
                    {
                        File.Move(operation.OriginalPath, operation.NewPath!);
                    }
                    else if (Directory.Exists(operation.OriginalPath))
                    {
                        Directory.Move(operation.OriginalPath, operation.NewPath!);
                    }
                    break;
                    
                case OperationType.Copy:
                    if (File.Exists(operation.OriginalPath))
                    {
                        File.Copy(operation.OriginalPath, operation.NewPath!);
                    }
                    break;
                    
                case OperationType.Delete:
                    if (!string.IsNullOrEmpty(operation.BackupPath))
                    {
                        File.Move(operation.OriginalPath, operation.BackupPath);
                    }
                    else if (File.Exists(operation.OriginalPath))
                    {
                        File.Delete(operation.OriginalPath);
                    }
                    break;
                    
                case OperationType.Batch:
                    foreach (var batchOp in operation.BatchOperations)
                    {
                        await RedoOperationAsync(batchOp);
                    }
                    break;
            }
            
            await Task.CompletedTask;
        }
    }
    
    public enum OperationType
    {
        Rename,
        Move,
        Copy,
        Delete,
        Batch
    }
    
    public class FileOperation
    {
        public OperationType Type { get; set; }
        public string OriginalPath { get; set; } = "";
        public string? NewPath { get; set; }
        public string? BackupPath { get; set; }
        public string? Description { get; set; }
        public DateTime Timestamp { get; set; }
        public List<FileOperation> BatchOperations { get; set; } = new();
        
        public string GetDescription()
        {
            if (!string.IsNullOrEmpty(Description)) return Description;
            
            return Type switch
            {
                OperationType.Rename => $"Rename: {Path.GetFileName(OriginalPath)} → {Path.GetFileName(NewPath)}",
                OperationType.Move => $"Move: {Path.GetFileName(OriginalPath)}",
                OperationType.Copy => $"Copy: {Path.GetFileName(OriginalPath)}",
                OperationType.Delete => $"Delete: {Path.GetFileName(OriginalPath)}",
                OperationType.Batch => $"Batch: {BatchOperations.Count} operations",
                _ => "Unknown operation"
            };
        }
    }
}
