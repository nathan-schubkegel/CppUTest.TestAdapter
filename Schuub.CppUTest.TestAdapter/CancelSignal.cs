//
// This is free and unencumbered software released into the public domain.
//
// Anyone is free to copy, modify, publish, use, compile, sell, or
// distribute this software, either in source code form or as a compiled
// binary, for any purpose, commercial or non-commercial, and by any
// means.
//
// For more information, please refer to <https://unlicense.org>
//

using System;
using System.Diagnostics;
using System.Threading;

namespace Schuub.CppUTest.TestAdapter
{
  /// <summary>
  /// Object that can signal cancellation
  /// </summary>
  public class CancelSignal : IDisposable
  {
    private bool _disposed;
    private Thread _thread;
    private volatile bool _threadStopPlz;
    private volatile bool _isCanceled;

    /// <summary>
    /// Returns true if cancellation has been requested.
    /// </summary>
    public bool IsCancellationRequested => _isCanceled;

    /// <summary>
    /// Requests cancellation.
    /// </summary>
    public void Cancel()
    {
      _isCanceled = true;
    }

    /// <summary>
    /// Disposes this instance.
    /// This should only be called after it's done being used
    /// (by both the threads watching for cancellation and the threads that might try to cancel).
    /// </summary>
    public void Dispose()
    {
      _disposed = true;
      _threadStopPlz = true;
      if (_thread != null)
      {
        _thread.Join();
      }
    }

    /// <summary>
    /// Throws an exception if cancellation has been requested.
    /// </summary>
    public void ThrowIfCancellationRequested()
    {
      if (_isCanceled)
      {
        throw new OperationCanceledException();
      }
    }

    /// <summary>
    /// Arranges for cancellation to occur in so many milliseconds.
    /// </summary>
    /// <param name="ms">The count of milliseconds to wait before cancelling.</param>
    /// <returns>Returns this object, so this can be conveniently used right after the constructor in a using statement.</returns>
    public CancelSignal CancelAfterTimeout(int ms)
    {
      if (_disposed)
      {
        throw new ObjectDisposedException(nameof(CancelSignal));
      }
      _threadStopPlz = true;
      if (_thread != null)
      {
        _thread.Join();
      }
      _threadStopPlz = false;
      _thread = new Thread(() =>
      {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < ms && !_isCanceled && !_threadStopPlz)
        {
          Thread.Sleep(1);
        }
        if (!_threadStopPlz)
        {
          _isCanceled = true;
        }
      });
      _thread.IsBackground = true;
      _thread.Start();
      return this;
    }
  }
}
