using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace zEClass.Tests;

/// <summary>
/// Marks every test class that constructs a real WPF window. xUnit runs different collections
/// in parallel, so without this the window smoke tests race each other.
/// </summary>
[CollectionDefinition("WpfWindows")]
public sealed class WpfWindowsCollection : ICollectionFixture<StaUiFixture>
{
}

/// <summary>
/// Owns the single UI thread that all WPF window smoke tests share.
///
/// Two WPF rules force this design. Only one <see cref="Application"/> may exist per AppDomain,
/// and every Window must be created on the same thread as that application's dispatcher. The
/// previous per-test helper created the application on one thread and the windows on another,
/// which worked exactly until a second test class did the same thing and the two raced —
/// producing intermittent "failed to construct" failures that looked like product defects but
/// were test-harness defects. One thread, one dispatcher, one application, every window test
/// marshalled onto it.
/// </summary>
public sealed class StaUiFixture : IDisposable
{
    private readonly Thread _ui;
    private readonly ManualResetEventSlim _ready = new(false);
    private Dispatcher? _dispatcher;
    private bool _disposed;

    public StaUiFixture()
    {
        _ui = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            _ready.Set();
            Dispatcher.Run();
            app.Shutdown();
        });
        _ui.SetApartmentState(ApartmentState.STA);
        _ui.IsBackground = true;
        _ui.Start();
        _ready.Wait();
    }

    private Dispatcher Dispatcher => _dispatcher
        ?? throw new InvalidOperationException("the UI thread is not running");

    /// <summary>
    /// Runs work on the UI thread and rethrows any failure on the calling thread, so a window
    /// that throws during construction fails its test with the real exception.
    /// </summary>
    public void Invoke(Action action) => Dispatcher.Invoke(action);

    public T Invoke<T>(Func<T> func) => Dispatcher.Invoke(func);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _dispatcher?.InvokeShutdown();
        }
        catch (Exception)
        {
            // Teardown best effort: a failing shutdown must not mask the test results.
        }

        _ui.Join(TimeSpan.FromSeconds(10));
        _ready.Dispose();
    }
}
