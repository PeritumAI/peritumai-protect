using System.Windows;

namespace Peritum.Protect.App.Tests;

/// <summary>One WPF application for the visual-tree tests. A process can create Application only once.</summary>
static class StaApp
{
    private static readonly object Gate = new();
    private static Peritum.Protect.App.App? _app;
    private static Exception? _startError;

    public static void Invoke(Action action)
    {
        Ensure();
        _app!.Dispatcher.Invoke(action);
    }

    private static void Ensure()
    {
        lock (Gate)
        {
            if (_app is not null)
                return;
            var ready = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try
                {
                    Peritum.Protect.App.App.UiTestHost = true;
                    var app = new Peritum.Protect.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.InitializeComponent();
                    app.Startup += (_, _) =>
                    {
                        _app = app;
                        ready.Set();
                    };
                    app.Run();
                }
                catch (Exception ex)
                {
                    _startError = ex;
                    ready.Set();
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("The test application did not start.");
            if (_startError is not null)
                throw _startError;
        }
    }
}
