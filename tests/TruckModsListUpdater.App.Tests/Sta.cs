using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace TruckModsListUpdater.App.Tests;

/// <summary>
/// Runs an async test body on an STA thread with a WPF dispatcher, like the real UI thread:
/// awaits resume on it, so collection views and bindings behave as in the app.
/// </summary>
static class Sta
{
    public static void Run(Func<Task> body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(async () =>
            {
                try { await body(); }
                catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
                finally { frame.Continue = false; }
            });
            Dispatcher.PushFrame(frame);
            dispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("STA test did not finish.");
        error?.Throw();
    }
}
