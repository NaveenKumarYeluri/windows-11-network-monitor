using Microsoft.UI.Xaml;

namespace Windows11NetworkMonitor;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Application.Start(_ => new App());
    }
}
