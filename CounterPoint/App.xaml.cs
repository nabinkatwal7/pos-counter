using System.Windows;
namespace CounterPoint;
public partial class App : Application
{
    private System.Threading.Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        instance = new System.Threading.Mutex(true, "Local\\CounterPoint.Desktop", out var created);
        if (!created) { MessageBox.Show("CounterPoint is already running.", "CounterPoint"); Shutdown(); return; }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
