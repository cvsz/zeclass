using System.Windows;
using System.Windows.Threading;

namespace EBoard;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                CrashLog.Write("AppDomain", ex);
            }
        };
    }

    /// <summary>
    /// A whiteboard must not die mid-lesson. Unexpected UI exceptions are logged and
    /// swallowed so a single bad sample or file cannot end the session; the operator can
    /// still save from the palette.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write("Dispatcher", e.Exception);
        e.Handled = true;
    }
}
