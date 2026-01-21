using System;
using System.Threading;

namespace PlatypusTools.UI.Utilities
{
    /// <summary>
    /// Helper class for proper disposal of common resources.
    /// Provides extension methods and utilities for IDisposable patterns.
    /// </summary>
    public static class DisposableHelper
    {
        /// <summary>
        /// Safely disposes an object and sets the reference to null.
        /// </summary>
        /// <typeparam name="T">Type implementing IDisposable</typeparam>
        /// <param name="disposable">Reference to the disposable object</param>
        public static void SafeDispose<T>(ref T? disposable) where T : class, IDisposable
        {
            var temp = Interlocked.Exchange(ref disposable, null);
            temp?.Dispose();
        }

        /// <summary>
        /// Safely disposes a CancellationTokenSource and sets the reference to null.
        /// </summary>
        /// <param name="cts">Reference to the CancellationTokenSource</param>
        public static void SafeDisposeCts(ref CancellationTokenSource? cts)
        {
            var temp = Interlocked.Exchange(ref cts, null);
            if (temp != null)
            {
                try
                {
                    if (!temp.IsCancellationRequested)
                        temp.Cancel();
                }
                catch { }
                finally
                {
                    temp.Dispose();
                }
            }
        }

        /// <summary>
        /// Creates a new CancellationTokenSource, cancelling and disposing any existing one.
        /// Thread-safe replacement for the common pattern of recreating CTS instances.
        /// </summary>
        /// <param name="cts">Reference to the CancellationTokenSource field</param>
        /// <returns>The newly created CancellationTokenSource</returns>
        public static CancellationTokenSource ReplaceCts(ref CancellationTokenSource? cts)
        {
            SafeDisposeCts(ref cts);
            var newCts = new CancellationTokenSource();
            cts = newCts;
            return newCts;
        }

        /// <summary>
        /// Cancels a CancellationTokenSource if it exists and is not already cancelled.
        /// Does not dispose - use when you need to reuse the token.
        /// </summary>
        /// <param name="cts">The CancellationTokenSource to cancel</param>
        public static void SafeCancel(CancellationTokenSource? cts)
        {
            if (cts != null && !cts.IsCancellationRequested)
            {
                try { cts.Cancel(); } catch { }
            }
        }
    }

    /// <summary>
    /// Base class providing standard IDisposable implementation with the dispose pattern.
    /// ViewModels and services with resources should inherit from this.
    /// </summary>
    public abstract class DisposableBase : IDisposable
    {
        private bool _disposed;
        private readonly object _disposeLock = new();

        /// <summary>
        /// Gets whether this instance has been disposed.
        /// </summary>
        protected bool IsDisposed => _disposed;

        /// <summary>
        /// Throws ObjectDisposedException if this instance has been disposed.
        /// </summary>
        protected void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; 
        /// false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            lock (_disposeLock)
            {
                if (_disposed)
                    return;

                if (disposing)
                {
                    DisposeManagedResources();
                }

                DisposeUnmanagedResources();
                _disposed = true;
            }
        }

        /// <summary>
        /// Override to dispose managed resources (IDisposable objects, event handlers, etc.)
        /// </summary>
        protected virtual void DisposeManagedResources() { }

        /// <summary>
        /// Override to dispose unmanaged resources (handles, native memory, etc.)
        /// </summary>
        protected virtual void DisposeUnmanagedResources() { }

        /// <summary>
        /// Destructor - ensures resources are released if Dispose is not called.
        /// </summary>
        ~DisposableBase()
        {
            Dispose(false);
        }
    }
}
