using System.Threading;
using System.Windows;

namespace HomeEasy.Desktop;

public partial class App : Application
{
    private const string InstanceName = "HomeEasyLocal-7f3c2a";
    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, InstanceName, out bool first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + "-show");
        if (!first)
        {
            // Already running (possibly hidden in the tray): ask it to show its window and exit.
            _showSignal.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        var listener = new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.Invoke(window.ShowFromTray);
        }) { IsBackground = true };
        listener.Start();
    }
}

