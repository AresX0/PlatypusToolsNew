using System;
using System.Threading;
using PlatypusTools.UI.Utilities;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// Base class for ViewModels that need disposal support.
    /// Combines BindableBase with IDisposable pattern.
    /// </summary>
    public abstract class DisposableBindableBase : BindableBase, IDisposable
    {
        private bool _disposed;
        private CancellationTokenSource? _operationCts;

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        protected bool IsDisposed => _disposed;

        /// <summary>
        /// Gets or replaces the operation cancellation token source.
        /// Automatically cancels and disposes any existing CTS before creating a new one.
        /// </summary>
        protected CancellationTokenSource GetOrCreateOperationCts()
        {
            ThrowIfDisposed();
            return DisposableHelper.ReplaceCts(ref _operationCts);
        }

        /// <summary>
        /// Gets the current operation cancellation token, or CancellationToken.None if no operation is active.
        /// </summary>
        protected CancellationToken CurrentOperationToken => _operationCts?.Token ?? CancellationToken.None;

        /// <summary>
        /// Cancels the current operation if one is in progress.
        /// </summary>
        protected void CancelCurrentOperation()
        {
            DisposableHelper.SafeCancel(_operationCts);
        }

        /// <summary>
        /// Throws ObjectDisposedException if the object has been disposed.
        /// </summary>
        protected void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        /// <summary>
        /// Override this method to dispose managed resources.
        /// </summary>
        protected virtual void DisposeManagedResources()
        {
        }

        /// <summary>
        /// Override this method to dispose unmanaged resources.
        /// </summary>
        protected virtual void DisposeUnmanagedResources()
        {
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            
            if (disposing)
            {
                // Dispose managed resources
                DisposableHelper.SafeDisposeCts(ref _operationCts);
                DisposeManagedResources();
            }
            
            // Dispose unmanaged resources
            DisposeUnmanagedResources();
            
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~DisposableBindableBase()
        {
            Dispose(false);
        }
    }
}
